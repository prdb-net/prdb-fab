using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Status;

public sealed class NotificationSettings(FabDbContext context)
{
    public async Task<NotificationSettingsState> ReadAsync(CancellationToken token)
    {
        var installation = await context.Installation.SingleAsync(token);
        var delivery = await context.NotificationDeliveries.SingleOrDefaultAsync(token);
        return new(installation.NotificationsEnabled, installation.NotificationWebhookUrl != null,
            delivery?.DeliveredAt, delivery?.Error);
    }

    public async Task<NotificationSettingsVerdict> SaveAsync(bool enabled, string? webhookUrl, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var installation = await context.Installation.AsTracking().SingleAsync(token);
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            if (webhookUrl.Length > 2048 || !Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
                return new(false, "Enter an HTTP or HTTPS webhook URL without user information or a fragment.");
            if (installation.NotificationWebhookUrl != uri.AbsoluteUri)
                await context.NotificationDeliveries.ExecuteDeleteAsync(token);
            installation.NotificationWebhookUrl = uri.AbsoluteUri;
        }
        if (enabled && installation.NotificationWebhookUrl is null)
            return new(false, "Enter a webhook URL before enabling notifications.");
        installation.NotificationsEnabled = enabled;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(true, enabled ? "Notifications enabled. Changed conditions are grouped into at most one message every 30 minutes." : "Notifications disabled.");
    }
}

public sealed record NotificationSettingsState(bool Enabled, bool Configured, DateTimeOffset? LastDeliveredAt, string? Error);
public sealed record NotificationSettingsVerdict(bool Accepted, string Detail);

/// <summary>One grouped, privacy-preserving webhook per changed attention state.</summary>
public sealed class NotificationRoutine(FabDbContext context, StatusService status,
    IHttpClientFactory clients, TimeProvider time) : IRoutine
{
    public const string RoutineName = "Notifications";
    public string Name => RoutineName;
    public Lane Lane => Lane.Live;
    public TimeSpan Cadence => TimeSpan.FromMinutes(1);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);
        if (!installation.NotificationsEnabled || installation.NotificationWebhookUrl is null)
            return RunResult.NothingToDo;
        var delivery = await context.NotificationDeliveries.AsTracking().SingleOrDefaultAsync(cancellationToken);
        var now = time.GetUtcNow();
        if (delivery?.DeliveredAt > now.AddMinutes(-30)) return RunResult.NothingToDo;

        var state = await status.ReadAsync(cancellationToken);
        // Titles and detail text can contain an indexer's user-chosen name.
        // Only stage ids and counts leave; no titles, paths, keys or artwork.
        var gaps = state.Stages.Select(stage => new NotificationStage(stage.Id,
                stage.Gaps.Count(gap => !gap.Cleared && !(gap.Route?.StartsWith("/settings/notifications", StringComparison.Ordinal) ?? false))))
            .Where(stage => stage.Gaps > 0).ToArray();
        var review = await context.ArrivingFiles.CountAsync(row => row.Reason != null, cancellationToken);
        var message = new NotificationMessage("prdb-fab", gaps.Length == 0 && review == 0 ? "Recovered" : "Attention needed", gaps, review);
        var serialized = System.Text.Json.JsonSerializer.Serialize(message);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        if (delivery?.Fingerprint == fingerprint || (delivery?.DeliveredAt is null && gaps.Length == 0 && review == 0))
        {
            if (delivery?.Error is not null)
            {
                delivery.Error = null;
                await context.SaveChangesAsync(cancellationToken);
            }
            return RunResult.NothingToDo;
        }
        if (delivery is null)
        {
            delivery = new NotificationDeliveryRow();
            context.NotificationDeliveries.Add(delivery);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, installation.NotificationWebhookUrl)
            {
                Content = JsonContent.Create(message),
            };
            request.Headers.Add("Idempotency-Key", fingerprint + "-" + (delivery.DeliveredAt?.ToUnixTimeMilliseconds() ?? 0));
            using var response = await clients.CreateClient("notifications").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("The notification destination did not accept the message.");
            delivery.Fingerprint = fingerprint;
            delivery.DeliveredAt = now;
            delivery.Error = null;
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Handled(1);
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Never persist or log transport errors: they can contain the URL's secret.
            delivery.Error = "The notification could not be delivered. Check the destination; the schedule will retry.";
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Failed(delivery.Error);
        }
    }
}

public sealed record NotificationStage(string Stage, int Gaps);
public sealed record NotificationMessage(string Source, string State, IReadOnlyList<NotificationStage> Stages, int ReviewFiles);

public static class NotificationRegistration
{
    public static IServiceCollection AddFabNotifications(this IServiceCollection services)
    {
        services.AddScoped<NotificationSettings>();
        services.AddScoped<NotificationRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<NotificationRoutine>());
        services.AddHttpClient("notifications", client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        return services;
    }
}
