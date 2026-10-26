using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Status;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Status;

public sealed class StatusSnapshotTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Overlapping_readers_share_one_snapshot_and_expiry_and_invalidation_read_fresh_state()
    {
        var commands = new Commands();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddDbContext<FabDbContext>(options => options.AddInterceptors(commands)));
        var token = TestContext.Current.CancellationToken;
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var indexer = new IndexerRow { Id = Guid.NewGuid(), Name = "Benchmark", Enabled = false };
            context.Indexers.Add(indexer);
            context.Releases.AddRange(Enumerable.Range(0, 10000).Select(number => new ReleaseRow
            {
                IndexerId = indexer.Id, DerivedReleaseId = number.ToString(), Title = "Release " + number,
                NormalisedTitle = "release " + number, PostDate = database.Time.GetUtcNow(), PubDate = database.Time.GetUtcNow(),
            }));
            await context.SaveChangesAsync(token);
        }
        commands.Count = 0;
        var clock = Stopwatch.StartNew();
        await using (var scope = database.Scope())
            await scope.ServiceProvider.GetRequiredService<StatusService>().ReadAsync(token);
        var rawCount = commands.Count;
        output.WriteLine($"10,000 releases: uncached Status read: {rawCount} SQL reads, {clock.Elapsed.TotalMilliseconds:F1} ms.");
        commands.Count = 0;
        var snapshot = database.Services.GetRequiredService<StatusSnapshot>();
        var first = await snapshot.ReadAsync(token);
        Assert.Equal(rawCount, commands.Count);
        clock.Restart();
        var overlapping = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => snapshot.ReadAsync(token)));
        Assert.All(overlapping, item => Assert.Same(first, item));
        Assert.Equal(rawCount, commands.Count);
        output.WriteLine($"10 overlapping cached reads: 0 additional SQL reads, {clock.Elapsed.TotalMilliseconds:F1} ms.");
        database.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.NotSame(first, await snapshot.ReadAsync(token));
        Assert.Equal(rawCount * 2, commands.Count);
        await snapshot.InvalidateAsync(token);
        await snapshot.ReadAsync(token);
        Assert.Equal(rawCount * 3, commands.Count);
    }

    private sealed class Commands : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }
    }
}
