using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed partial class IdentificationCutTests
{
    [Fact]
    public async Task A_bounded_backlog_turn_yields_due_now_and_an_empty_set_keeps_its_idle_tick()
    {
        var answers = Enumerable.Range(1, ReleaseIdentificationRoutine.BatchSize)
            .Select(i => "{\"ref\":\"" + i + "\",\"confidence\":0}");
        var socket = new FakePrdbApi().Answers("/videos/identify", "{\"results\":[" + string.Join(',', answers) + "]}")
            .Answers("/videos/identify", "{\"results\":[{\"ref\":\"201\",\"confidence\":0}]}");
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        await SeedIndexerAsync(database, IndexerId);
        await SetPrdbKeyAsync(database);
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(Enumerable.Range(1, 201).Select(i =>
                Release("release-" + i, "Name." + i, IdentificationState.Awaiting, database.Time.GetUtcNow())));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using (var first = database.Scope())
        {
            var result = await first.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(200, result.ItemsHandled);
            Assert.Equal(TimeSpan.Zero, result.DueIn);
        }
        // No ten-second advance. A different due routine can use the lane
        // before the next scheduler tick takes the remaining bounded turn.
        await using (var next = database.Scope())
        {
            var routine = next.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>();
            var result = await routine.RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.ItemsHandled);
            Assert.Equal(routine.Cadence, result.DueIn);
            var idle = await routine.RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Null(idle.Outcome);
            Assert.Null(idle.DueIn);
        }
        Assert.Equal(2, socket.AskingFor("/videos/identify").Count);
    }
}
