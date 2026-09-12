using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Reporting;
using Prdb.Fab.Infrastructure.Scheduling;

namespace ReleaseTablePaging;

/// <summary>
/// What the Release table costs in the Video context, where ADR 0008's ranking
/// is applied in memory before the page is cut, against what the same table
/// costs in the Site context, where the page is cut in SQL.
/// </summary>
internal static class Program
{
    private const int Repeats = 21;

    private static readonly int[] Counts = [5, 50, 200, 500, 1_000, 2_000, 5_000, 10_000, 20_000];

    private static readonly Guid SiteId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid VideoId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid[] Indexers =
    [
        Guid.Parse("0198ec28-1c00-7000-8000-000000000001"),
        Guid.Parse("0198ec28-1c00-7000-8000-000000000002"),
        Guid.Parse("0198ec28-1c00-7000-8000-000000000003"),
    ];

    private static async Task Main(string[] args)
    {
        // Where the database file goes decides what is being priced. The
        // default temporary directory is a tmpfs on most Linux machines, which
        // removes storage from the measurement and leaves the in-memory
        // ordering on its own; a path on real storage prices the reads too,
        // and that is the half the Site branch does not pay.
        var root = args.Length > 0
            ? args[0]
            : Path.Combine(Path.GetTempPath(), "prdb-fab-release-paging");

        Console.WriteLine($".NET {Environment.Version}, {Environment.ProcessorCount} cores");
        Console.WriteLine($"database under: {root}");
        Console.WriteLine($"repeats: {Repeats}, page size: {ReleaseBrowse.APage}");
        Console.WriteLine();
        Console.WriteLine("| releases | video p50 | video p95 | site p50 | site p95 | ranking p50 | video last p50 | site last p50 |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");

        foreach (var count in Counts)
        {
            var directory = Path.Combine(root, Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(directory);

            try
            {
                await using var provider = Build(directory);
                await MigrateAsync(provider);
                await SeedAsync(provider, count);

                var lastPage = (count + ReleaseBrowse.APage - 1) / ReleaseBrowse.APage;

                // One untimed pass so the schema, the query cache and SQLite's
                // page cache are warm for every row of the table alike.
                await MeasureAsync(provider, 2, browse => browse.VideoAsync(VideoId, null, null, 1, default));

                var video = await MeasureAsync(
                    provider, Repeats, browse => browse.VideoAsync(VideoId, null, null, 1, default));
                var site = await MeasureAsync(
                    provider, Repeats, browse => browse.SiteAsync(SiteId, null, null, 1, default));
                var videoLast = await MeasureAsync(
                    provider, Repeats, browse => browse.VideoAsync(VideoId, null, null, lastPage, default));
                var siteLast = await MeasureAsync(
                    provider, Repeats, browse => browse.SiteAsync(SiteId, null, null, lastPage, default));
                var ranking = await MeasureRankingAsync(provider, Repeats);

                Console.WriteLine(
                    $"| {count:n0} "
                    + $"| {Show(video.P50)} | {Show(video.P95)} "
                    + $"| {Show(site.P50)} | {Show(site.P95)} "
                    + $"| {Show(ranking.P50)} "
                    + $"| {Show(videoLast.P50)} | {Show(siteLast.P50)} |");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ServiceProvider Build(string directory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddFabPersistence(directory);
        services.AddFabScheduling();
        services.AddFabAccess();
        services.AddFabConnections();
        services.AddFabReleaseDiscovery();
        services.AddFabAcquisition();
        services.AddFabAutomation();
        services.AddFabFiling();
        services.AddFabReporting();
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Database.MigrateAsync();
    }

    /// <summary>
    /// One Site, one Video in it, three indexers, and <paramref name="count"/>
    /// Releases matched to the Video — so the Video and the Site context select
    /// exactly the same rows and only the paging differs.
    /// </summary>
    private static async Task SeedAsync(IServiceProvider provider, int count)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var now = DateTimeOffset.UtcNow;

        var site = new CatalogueSiteRow { PrdbId = SiteId, Title = "A Site" };
        context.Add(site);
        for (var number = 0; number < Indexers.Length; number++)
        {
            context.Add(new IndexerRow
            {
                Id = Indexers[number],
                Name = $"Indexer {number + 1}",
                Url = $"https://indexer{number + 1}.invalid/api",
                ApiKey = "fixture",
                Categories = "Adult",
                LastVerdict = IndexerConnectionOutcome.Saved,
                LastCheckedAt = now,
                Rank = number + 1,
            });
        }

        await context.SaveChangesAsync();

        var video = new CatalogueVideoRow
        {
            PrdbId = VideoId,
            Title = "A Video",
            NormalisedTitle = "a video",
            SiteId = site.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        context.Add(video);
        await context.SaveChangesAsync();

        for (var written = 0; written < count; written += 2_000)
        {
            context.Releases.AddRange(Enumerable
                .Range(written, Math.Min(2_000, count - written))
                .Select(number => new ReleaseRow
                {
                    IndexerId = Indexers[number % Indexers.Length],
                    DerivedReleaseId = $"release-{number}",
                    RawGuid = $"release-{number}",
                    Title = $"A.Video.{number}.1080p.WEB-DL",
                    NormalisedTitle = $"a video {number} 1080p web dl",
                    // Spread so the size comparison decides rather than
                    // ADR 0008's 5 % tolerance collapsing into indexer rank.
                    Size = 1_000_000_000L + (number * 50_000_000L),
                    Categories = "[]",
                    PostDate = now.AddMinutes(-number),
                    PubDate = now.AddMinutes(-number),
                    DownloadUrl = "https://indexer.invalid/nzb",
                    FirstSeenAt = now.AddMinutes(-number),
                    IdentificationState = IdentificationState.Matched,
                    VideoId = video.Id,
                    Confidence = number % 4 == 0
                        ? IdentificationConfidence.Probable
                        : IdentificationConfidence.Exact,
                    MatchedBy = IdentificationRung.ReleaseName,
                    // Every fiftieth release confesses a password, so the
                    // excluded group is populated the way a real table's is.
                    Password = number % 50 == 0 ? "1" : null,
                }));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        await scope.ServiceProvider.GetRequiredService<DatabaseAnalysisRoutine>()
            .RunAsync(null, default);
    }

    private static async Task<Sample> MeasureAsync(
        IServiceProvider provider,
        int repeats,
        Func<ReleaseBrowse, Task<ReleasePage?>> read)
    {
        var taken = new List<double>(repeats);
        for (var number = 0; number < repeats; number++)
        {
            // A scope per read, because that is what a request is: ADR 0039
            // holds no context open across one.
            await using var scope = provider.CreateAsyncScope();
            var browse = scope.ServiceProvider.GetRequiredService<ReleaseBrowse>();
            var clock = Stopwatch.StartNew();
            var page = await read(browse);
            clock.Stop();
            ArgumentNullException.ThrowIfNull(page);
            taken.Add(clock.Elapsed.TotalMilliseconds);
        }

        return Sample.Of(taken);
    }

    private static async Task<Sample> MeasureRankingAsync(IServiceProvider provider, int repeats)
    {
        var taken = new List<double>(repeats);
        for (var number = 0; number < repeats; number++)
        {
            await using var scope = provider.CreateAsyncScope();
            var rankings = scope.ServiceProvider.GetRequiredService<ReleaseRankings>();
            var clock = Stopwatch.StartNew();
            _ = await rankings.ForVideoAsync(VideoId, observeDecision: false);
            clock.Stop();
            taken.Add(clock.Elapsed.TotalMilliseconds);
        }

        return Sample.Of(taken);
    }

    private static string Show(double milliseconds) => milliseconds < 10
        ? $"{milliseconds:0.0} ms"
        : $"{milliseconds:n0} ms";

    private sealed record Sample(double P50, double P95, double Max)
    {
        public static Sample Of(List<double> taken)
        {
            taken.Sort();
            return new(
                taken[taken.Count / 2],
                taken[Math.Min(taken.Count - 1, (int)(taken.Count * 0.95))],
                taken[^1]);
        }
    }
}
