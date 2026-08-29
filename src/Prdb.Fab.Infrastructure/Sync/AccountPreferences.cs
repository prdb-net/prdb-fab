using Microsoft.EntityFrameworkCore;
using Microsoft.Kiota.Abstractions;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The one write path from catalogue intent to the connected prdb account and
/// its immediate local projection.
/// </summary>
public sealed class AccountPreferences(
    FabDbContext context,
    PrdbGateway prdb,
    TimeProvider time)
{
    public async Task<AccountPreferenceVerdict> SetAsync(
        AccountPreferenceKind kind,
        Guid entityId,
        bool desired,
        CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(kind, entityId, cancellationToken))
        {
            return AccountPreferenceVerdict.NotFound(desired);
        }

        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AccountPreferenceVerdict.Failed(
                desired,
                "The prdb connection is not configured. Check it and retry.");
        }

        var sent = await SendAsync(apiKey, kind, entityId, desired, cancellationToken);
        if (!sent.Updated)
        {
            return sent;
        }

        await ApplyAsync(kind, entityId, desired, time.GetUtcNow(), cancellationToken);
        await context.AccountPreferenceWrites
            .Where(row => row.Kind == kind && row.EntityId == entityId)
            .ExecuteDeleteAsync(cancellationToken);

        return sent;
    }

    /// <summary>
    /// Records manual Wanted intent inside the caller's transaction and makes
    /// its desired local state visible before either remote system is touched.
    /// </summary>
    public async Task StageWantedAsync(
        Guid videoId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        var pending = await context.AccountPreferenceWrites
            .SingleOrDefaultAsync(
                row => row.Kind == AccountPreferenceKind.WantedVideo && row.EntityId == videoId,
                cancellationToken);
        if (pending is null)
        {
            context.AccountPreferenceWrites.Add(new AccountPreferenceWriteRow
            {
                Id = Guid.CreateVersion7(requestedAt),
                Kind = AccountPreferenceKind.WantedVideo,
                EntityId = videoId,
                Desired = true,
                RequestedAt = requestedAt,
            });
        }
        else
        {
            pending.Desired = true;
            pending.RequestedAt = requestedAt;
            pending.LastFailure = null;
            pending.Blocked = false;
        }

        await ApplyAsync(
            AccountPreferenceKind.WantedVideo,
            videoId,
            desired: true,
            requestedAt,
            cancellationToken);
    }

    /// <summary>Converges one durable desired state and keeps retryable failures pending.</summary>
    public async Task<AccountPreferenceVerdict> CompleteAsync(
        AccountPreferenceWriteRow write,
        CancellationToken cancellationToken)
    {
        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return await RecordFailureAsync(
                write,
                "The prdb connection is not configured.",
                blocked: true,
                cancellationToken);
        }

        var verdict = await SendAsync(
            apiKey,
            write.Kind,
            write.EntityId,
            write.Desired,
            cancellationToken);
        if (!verdict.Updated)
        {
            var blocked = verdict.Outcome is AccountPreferenceOutcome.Rejected
                or AccountPreferenceOutcome.NotFound;
            return await RecordFailureAsync(write, verdict.Detail, blocked, cancellationToken);
        }

        await ApplyAsync(
            write.Kind,
            write.EntityId,
            write.Desired,
            write.RequestedAt,
            cancellationToken);
        await context.AccountPreferenceWrites
            .Where(row => row.Id == write.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return verdict;
    }

    private async Task<AccountPreferenceVerdict> SendAsync(
        string apiKey,
        AccountPreferenceKind kind,
        Guid entityId,
        bool desired,
        CancellationToken cancellationToken)
    {
        try
        {
            await prdb.ActAsync(
                apiKey,
                PrdbWork.Writes,
                (client, token) => (kind, desired) switch
                {
                    (AccountPreferenceKind.WantedVideo, true) => client.WantedVideos[entityId].PostAsync(cancellationToken: token),
                    (AccountPreferenceKind.WantedVideo, false) => client.WantedVideos[entityId].DeleteAsync(cancellationToken: token),
                    (AccountPreferenceKind.FavouriteActor, true) => client.FavoriteActors[entityId].PostAsync(cancellationToken: token),
                    (AccountPreferenceKind.FavouriteActor, false) => client.FavoriteActors[entityId].DeleteAsync(cancellationToken: token),
                    (AccountPreferenceKind.FavouriteSite, true) => client.FavoriteSites[entityId].PostAsync(cancellationToken: token),
                    (AccountPreferenceKind.FavouriteSite, false) => client.FavoriteSites[entityId].DeleteAsync(cancellationToken: token),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                },
                cancellationToken);

            return AccountPreferenceVerdict.UpdatedState(desired);
        }
        catch (ApiException refused) when (!desired && refused.ResponseStatusCode == 404)
        {
            // DELETE is idempotent locally even though prdb truthfully reports
            // that there was no row left to remove.
            return AccountPreferenceVerdict.UpdatedState(desired);
        }
        catch (PrdbDeferredException deferred)
        {
            return new(
                AccountPreferenceOutcome.Deferred,
                desired,
                "prdb is rate-limited. Retry when its request budget is available.",
                Math.Max(1, (int)Math.Ceiling(deferred.Wait.TotalSeconds)));
        }
        catch (ApiException refused)
        {
            return refused.ResponseStatusCode switch
            {
                401 => AccountPreferenceVerdict.Rejected(desired, "The prdb API key was rejected. Check the connection and retry."),
                403 => AccountPreferenceVerdict.Rejected(desired, "The connected prdb account cannot write this preference."),
                404 => AccountPreferenceVerdict.NotFound(desired),
                409 => AccountPreferenceVerdict.Rejected(desired, "prdb refused this preference because the account limit was reached."),
                _ => AccountPreferenceVerdict.Failed(desired, "prdb could not update the preference. Retry shortly."),
            };
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            return AccountPreferenceVerdict.Failed(
                desired,
                "prdb did not answer. The previous state is unchanged; retry shortly.");
        }
    }

    private async Task<AccountPreferenceVerdict> RecordFailureAsync(
        AccountPreferenceWriteRow write,
        string detail,
        bool blocked,
        CancellationToken cancellationToken)
    {
        await context.AccountPreferenceWrites
            .Where(row => row.Id == write.Id)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.LastFailure, detail)
                .SetProperty(row => row.Blocked, blocked),
                cancellationToken);

        if (blocked && write.Kind == AccountPreferenceKind.WantedVideo)
        {
            await ApplyAsync(write.Kind, write.EntityId, desired: false, time.GetUtcNow(), cancellationToken);
        }

        return blocked
            ? AccountPreferenceVerdict.Rejected(write.Desired, detail)
            : AccountPreferenceVerdict.Failed(write.Desired, detail);
    }

    private Task<bool> ExistsAsync(
        AccountPreferenceKind kind,
        Guid entityId,
        CancellationToken cancellationToken) => kind switch
        {
            AccountPreferenceKind.WantedVideo => context.CatalogueVideos.AnyAsync(row => row.PrdbId == entityId, cancellationToken),
            AccountPreferenceKind.FavouriteActor => context.CatalogueActors.AnyAsync(row => row.PrdbId == entityId, cancellationToken),
            AccountPreferenceKind.FavouriteSite => context.CatalogueSites.AnyAsync(row => row.PrdbId == entityId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private Task ApplyAsync(
        AccountPreferenceKind kind,
        Guid entityId,
        bool desired,
        DateTimeOffset at,
        CancellationToken cancellationToken) => kind switch
        {
            AccountPreferenceKind.WantedVideo => ApplyWantedAsync(entityId, desired, at, cancellationToken),
            AccountPreferenceKind.FavouriteActor => ApplyActorAsync(entityId, desired, at, cancellationToken),
            AccountPreferenceKind.FavouriteSite => ApplySiteAsync(entityId, desired, at, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private async Task ApplyWantedAsync(
        Guid prdbId,
        bool desired,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var video = await context.CatalogueVideos
            .AsTracking()
            .SingleAsync(row => row.PrdbId == prdbId, cancellationToken);

        if (!desired)
        {
            await context.Downloads
                .Where(row => row.VideoId == prdbId
                    && !row.OriginIsPerson
                    && (row.State == DownloadState.Outstanding || row.State == DownloadState.Completed))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.State, DownloadState.Abandoned)
                    .SetProperty(row => row.Cause, (DownloadCause?)null),
                    cancellationToken);
            await context.Releases
                .Where(row => row.VideoId == video.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.AutomationPending, false)
                    .SetProperty(row => row.AutomationDecisionReason, AutomationDecisionReason.NotWanted),
                    cancellationToken);
            await context.WantedVideos
                .Where(row => row.VideoId == video.Id)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var held = await context.WantedVideos
            .SingleOrDefaultAsync(row => row.VideoId == video.Id, cancellationToken);
        var joined = held is null;
        if (held is null)
        {
            video.TitleSearchedBackwards = false;
            context.WantedVideos.Add(new WantedVideoRow { VideoId = video.Id, SinceAt = at });
        }
        else
        {
            held.SinceAt = at;
        }

        await context.SaveChangesAsync(cancellationToken);

        if (joined)
        {
            await context.Releases
                .Where(row => row.VideoId == video.Id && row.IdentificationState == IdentificationState.Matched)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.AutomationPending, true)
                    .SetProperty(row => row.AutomationDecisionReason, (AutomationDecisionReason?)null),
                    cancellationToken);
        }

        var userHash = await context.Installation.Select(row => row.PrdbUserHash).SingleAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(userHash))
        {
            await context.ReportedStates
                .Where(row => row.VideoId == prdbId
                    && row.UserHash == userHash
                    && row.TerminalOutcome == ReportingOutcome.NotWanted)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.TerminalOutcome, (ReportingOutcome?)null),
                    cancellationToken);
        }
    }

    private async Task ApplyActorAsync(
        Guid prdbId,
        bool desired,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var actorId = await context.CatalogueActors
            .Where(row => row.PrdbId == prdbId)
            .Select(row => row.Id)
            .SingleAsync(cancellationToken);
        var held = await context.FavouriteActors.SingleOrDefaultAsync(row => row.ActorId == actorId, cancellationToken);
        if (!desired)
        {
            if (held is not null) context.FavouriteActors.Remove(held);
        }
        else if (held is null)
        {
            context.FavouriteActors.Add(new FavouriteActorRow { ActorId = actorId, SinceAt = at });
        }
        else
        {
            held.SinceAt = at;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplySiteAsync(
        Guid prdbId,
        bool desired,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var siteId = await context.CatalogueSites
            .Where(row => row.PrdbId == prdbId)
            .Select(row => row.Id)
            .SingleAsync(cancellationToken);
        var held = await context.FavouriteSites.SingleOrDefaultAsync(row => row.SiteId == siteId, cancellationToken);
        if (!desired)
        {
            if (held is not null) context.FavouriteSites.Remove(held);
        }
        else if (held is null)
        {
            context.FavouriteSites.Add(new FavouriteSiteRow { SiteId = siteId, SinceAt = at });
        }
        else
        {
            held.SinceAt = at;
        }
        await context.SaveChangesAsync(cancellationToken);
    }
}

public enum AccountPreferenceOutcome
{
    Updated,
    Deferred,
    Rejected,
    NotFound,
    Failed,
}

public sealed record AccountPreferenceVerdict(
    AccountPreferenceOutcome Outcome,
    bool Desired,
    string Detail,
    int? RetryAfterSeconds = null)
{
    public bool Updated => Outcome == AccountPreferenceOutcome.Updated;

    public static AccountPreferenceVerdict UpdatedState(bool desired) => new(
        AccountPreferenceOutcome.Updated,
        desired,
        desired ? "The preference is now present in prdb." : "The preference is now absent from prdb.");

    public static AccountPreferenceVerdict Rejected(bool desired, string detail) =>
        new(AccountPreferenceOutcome.Rejected, desired, detail);

    public static AccountPreferenceVerdict NotFound(bool desired) => new(
        AccountPreferenceOutcome.NotFound,
        desired,
        "prdb does not know this entity. The previous local state is unchanged.");

    public static AccountPreferenceVerdict Failed(bool desired, string detail) =>
        new(AccountPreferenceOutcome.Failed, desired, detail);
}
