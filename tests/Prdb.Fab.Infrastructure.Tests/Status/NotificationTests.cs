using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Status;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Status;

public sealed class NotificationTests
{
    [Fact]
    public async Task Delivery_is_opt_in_deduplicated_durable_and_recovers_with_a_grouped_message()
    {
        var socket = new Receiver();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient("notifications").ConfigurePrimaryHttpMessageHandler(() => socket));
        var token = TestContext.Current.CancellationToken;
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationRoutine>().RunAsync(null, token);
            Assert.Empty(socket.Messages);
            var settings = scope.ServiceProvider.GetRequiredService<NotificationSettings>();
            Assert.False((await settings.SaveAsync(true, "file:///secret", token)).Accepted);
            Assert.True((await settings.SaveAsync(true, "https://receiver.example/secret", token)).Accepted);
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.ArrivingFiles.Add(new ArrivingFileRow { Id = Guid.NewGuid(), SourcePath = "/private/title.mkv", Reason = ArrivingFileReason.Duplicate });
            await context.SaveChangesAsync(token);
        }
        await RunAsync(database);
        Assert.Single(socket.Messages);
        Assert.DoesNotContain("private", socket.Messages[0]);
        Assert.DoesNotContain("secret", socket.Messages[0]);
        await RunAsync(database);
        database.Time.Advance(TimeSpan.FromHours(1));
        await RunAsync(database);
        Assert.Single(socket.Messages);

        await using (var scope = database.Scope())
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles.ExecuteDeleteAsync(token);
        await RunAsync(database);
        Assert.Equal(2, socket.Messages.Count);
        Assert.Contains("\"reviewFiles\":0", socket.Messages[1]);
        Assert.Contains("\"state\":\"Recovered\"", socket.Messages[1]);
    }

    [Fact]
    public async Task Failed_delivery_is_visible_and_retried_without_recording_the_destination_secret()
    {
        var socket = new Receiver { Status = HttpStatusCode.ServiceUnavailable };
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient("notifications").ConfigurePrimaryHttpMessageHandler(() => socket));
        var token = TestContext.Current.CancellationToken;
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationSettings>().SaveAsync(true, "https://receiver.example/secret", token);
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.ArrivingFiles.Add(new ArrivingFileRow { Id = Guid.NewGuid(), Reason = ArrivingFileReason.Duplicate });
            await context.SaveChangesAsync(token);
        }
        await RunAsync(database);
        await using (var scope = database.Scope())
        {
            var state = await scope.ServiceProvider.GetRequiredService<NotificationSettings>().ReadAsync(token);
            Assert.NotNull(state.Error);
            Assert.DoesNotContain("secret", state.Error);
            Assert.Null(state.LastDeliveredAt);
        }
        socket.Status = HttpStatusCode.OK;
        await RunAsync(database);
        await using var read = database.Scope();
        Assert.Null((await read.ServiceProvider.GetRequiredService<NotificationSettings>().ReadAsync(token)).Error);
        Assert.Equal(2, socket.Messages.Count);
    }

    private static async Task RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<NotificationRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
    }

    private sealed class Receiver : HttpMessageHandler
    {
        public List<string> Messages { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Messages.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(Status);
        }
    }
}
