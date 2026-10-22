using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class SyncCostTests
{
    [Fact]
    public async Task Transport_and_runner_measure_real_elapsed_time_without_retaining_secrets()
    {
        var socket = new TimedResponse();
        await using var database = await TestDatabase.CreateAsync(prdb: socket,
            also: services => services.AddScoped<IRoutine, GovernedRoutine>());
        socket.Advance = () => database.Time.Advance(TimeSpan.FromSeconds(3));
        await database.Services.PrepareFabScheduleAsync(TestContext.Current.CancellationToken);
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<IRoutineStore>().RunNowAsync(
            GovernedRoutine.RoutineName, null, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<RoutineRunner>().TurnAsync(
            Lane.Sync, TestContext.Current.CancellationToken);

        var run = await scope.ServiceProvider.GetRequiredService<FabDbContext>().RoutineRuns
            .SingleAsync(r => r.Routine!.Name == GovernedRoutine.RoutineName, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(3), run.FinishedAt - run.StartedAt);
        var costs = database.Services.GetRequiredService<PrdbGovernor>().Costs;
        var request = Assert.Single(costs.Snapshot().Requests, r => r.Endpoint == "Verification");
        Assert.Equal(3000, request.ElapsedMilliseconds);
        Assert.True(request.PayloadBytes > 0);
        Assert.Equal(1, request.Requests);

        costs.Request(Core.Sync.PrdbWork.UserFeeds, "/unknown/secret-input?apikey=secret-key", 200,
            TimeSpan.FromSeconds(1), 12);
        var serialized = JsonSerializer.Serialize(costs.Snapshot());
        Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(GovernedRoutine.ApiKey, serialized);
        Assert.DoesNotContain("private-account", serialized);
    }

    private sealed class TimedResponse : HttpMessageHandler
    {
        public Action? Advance { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Advance?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"userHash\":\"private-account\"}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
