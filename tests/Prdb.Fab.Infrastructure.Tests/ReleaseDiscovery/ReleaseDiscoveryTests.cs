using System.Net;
using System.Text;
using System.Web;
using System.Net.Http.Headers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed class ReleaseDiscoveryTests
{
    private static readonly Guid IndexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000001");
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_shared_reader_requests_extended_results_and_understands_recorded_shapes()
    {
        var fake = new RecordedIndexer(Recorded("search-shapes.xml"));
        await using var services = Services(fake);
        var gateway = services.GetRequiredService<NewznabGateway>();

        var read = await gateway.SearchAsync(
            "https://indexer.invalid/api",
            "first-key",
            [5000, 5010],
            offset: 100,
            maxAgeDays: 90,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(read.Refusal);
        Assert.Equal(["attribute-id", "uri-id", "<spot.42@example.invalid>"], read.Releases.Select(item => item.DerivedReleaseId));
        Assert.Equal(1, read.DroppedWithoutIdentity);
        Assert.Equal("a release title", read.Releases[0].NormalisedTitle);
        Assert.Equal(12345, read.Releases[0].Size);
        Assert.Equal("1", read.Releases[0].Password);

        var query = HttpUtility.ParseQueryString(fake.Requests.Single().Query);
        Assert.Equal("1", query["extended"]);
        Assert.Equal("100", query["offset"]);
        Assert.Equal("90", query["maxage"]);
        Assert.Equal("5000,5010", query["cat"]);
    }

    [Fact]
    public async Task A_refusal_cannot_echo_a_key_or_a_whole_url()
    {
        const string key = "secret-indexer-key";
        var body = $"<error code=\"900\" description=\"{key} at https://private.invalid/api?apikey={key}\" />";
        var fake = new RecordedIndexer(body);
        await using var services = Services(fake);

        var read = await services.GetRequiredService<NewznabGateway>().SearchAsync(
            "https://indexer.invalid/api", key, [], 0, null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(read.Said);
        Assert.DoesNotContain(key, read.Said, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", read.Said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_429_is_a_limit_even_when_its_body_is_not_xml()
    {
        var fake = new RecordedIndexer("too many requests", HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes(7));
        await using var services = Services(fake);
        var read = await services.GetRequiredService<NewznabGateway>().SearchAsync(
            "https://indexer.invalid/api", "key", [], 0, null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(IndexerConnectionOutcome.LimitReached, read.Refusal);
        Assert.Equal(TimeSpan.FromMinutes(7), read.RetryAfter);
    }

    [Fact]
    public async Task An_http_nzb_from_an_https_indexer_is_upgraded_before_the_key_is_sent()
    {
        var fake = new RecordedIndexer("nzb bytes");
        await using var services = Services(fake);

        var read = await services.GetRequiredService<NewznabGateway>().NzbAsync(
            "http://indexer.invalid/api?t=get&id=release&apikey=secret-key",
            "https://indexer.invalid/api",
            TestContext.Current.CancellationToken);

        Assert.Null(read.Refusal);
        var request = Assert.Single(fake.Requests);
        Assert.Equal(Uri.UriSchemeHttps, request.Scheme);
        Assert.Equal("indexer.invalid", request.Host);
        Assert.Equal("secret-key", HttpUtility.ParseQueryString(request.Query)["apikey"]);
    }

    [Fact]
    public async Task An_http_nzb_on_another_host_is_refused_before_the_key_is_sent()
    {
        var fake = new RecordedIndexer("must not be read");
        await using var services = Services(fake);

        var read = await services.GetRequiredService<NewznabGateway>().NzbAsync(
            "http://download.invalid/api?t=get&id=release&apikey=secret-key",
            "https://indexer.invalid/api",
            TestContext.Current.CancellationToken);

        Assert.Equal(IndexerConnectionOutcome.NotAnIndexer, read.Refusal);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Repeated_reads_and_key_rotation_update_one_release_without_moving_state_or_first_seen()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database);

        await using (var scope = database.Scope())
        {
            var rows = scope.ServiceProvider.GetRequiredService<ReleaseRows>();
            var original = Item("stable-id", "https://indexer.invalid/get/stable-id?apikey=old-key");
            Assert.Equal(1, (await rows.UpsertAsync(IndexerId, [original], FirstSeen, ReleaseSource.IndexerWalk, TestContext.Current.CancellationToken)).Added);
        }

        await using (var scope = database.Scope())
        {
            var rows = scope.ServiceProvider.GetRequiredService<ReleaseRows>();
            var rotated = Item("stable-id", "https://indexer.invalid/get/stable-id?apikey=new-key") with
            {
                Title = "Corrected title",
                Password = "1",
            };
            Assert.Equal(0, (await rows.UpsertAsync(IndexerId, [rotated], FirstSeen.AddDays(1), ReleaseSource.WantedSweep, TestContext.Current.CancellationToken)).Added);
        }

        await using (var scope = database.Scope())
        {
            var release = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Releases.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(FirstSeen, release.FirstSeenAt);
            Assert.Contains("new-key", release.DownloadUrl, StringComparison.Ordinal);
            Assert.Equal("Corrected title", release.Title);
            Assert.Equal("1", release.Password);
            Assert.Equal(IdentificationState.Unexamined, release.IdentificationState);
            Assert.False(release.SearchWasReason);
        }
    }

    [Fact]
    public async Task Caps_follow_renumbering_raise_one_gap_and_open_one_extension_catch_up()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database, "Adult");

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<DiscoveryState>().InitialiseAsync(
                IndexerId,
                [new CapsCategory(5000, "Adult", [new CapsCategory(5010, "Movies")])],
                TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().IndexerWalkStates.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.BootstrapCompletedAt, FirstSeen),
                TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var change = await scope.ServiceProvider.GetRequiredService<DiscoveryState>().StoreCapsAsync(
                IndexerId,
                [new CapsCategory(6000, "Adult", [new CapsCategory(6010, "Movies")])],
                TestContext.Current.CancellationToken);
            Assert.Empty(change.AddedIds);
        }

        await using (var scope = database.Scope())
        {
            var change = await scope.ServiceProvider.GetRequiredService<DiscoveryState>().StoreCapsAsync(
                IndexerId,
                [new CapsCategory(6000, "Adult", [new CapsCategory(6010, "Movies"), new CapsCategory(6020, "Packs")])],
                TestContext.Current.CancellationToken);
            Assert.Equal([6020], change.AddedIds);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var state = await context.IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(state.CatchUpFrom);
            Assert.Equal(1, await context.Routines.CountAsync(row => row.Name == DiscoveryRoutineNames.CatchUp, TestContext.Current.CancellationToken));
        }

        await using (var scope = database.Scope())
        {
            var change = await scope.ServiceProvider.GetRequiredService<DiscoveryState>().StoreCapsAsync(
                IndexerId,
                [new CapsCategory(6000, "Something else")],
                TestContext.Current.CancellationToken);
            Assert.Equal(["Adult"], change.MissingNames);
            var indexer = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Indexers.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(IndexerConnectionOutcome.NoCategories, indexer.LastVerdict);
        }
    }

    [Fact]
    public async Task The_database_rejects_an_eighth_identification_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database);
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.Releases.Add(new ReleaseRow
        {
            IndexerId = IndexerId,
            DerivedReleaseId = "bad-state",
            PostDate = FirstSeen,
            PubDate = FirstSeen,
            FirstSeenAt = FirstSeen,
            IdentificationState = (IdentificationState)999,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_upgraded_indexer_gets_its_cache_half_and_the_whole_slice_schedule()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<DiscoveryState>()
                .EnsureFoundationAsync(TestContext.Current.CancellationToken);
        }

        await using var check = database.Scope();
        var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
        var state = await context.IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Adult"], DiscoveryState.DeserialiseNames(state.MissingCategoryNames));

        var rows = await context.Routines
            .Where(row => row.Name.StartsWith("indexer.") || row.Name.StartsWith("release."))
            .Select(row => row.Name)
            .Order()
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            new[]
            {
                DiscoveryRoutineNames.Caps,
                DiscoveryRoutineNames.Walk,
                DiscoveryRoutineNames.Bootstrap,
                DiscoveryRoutineNames.WantedSweep,
                DiscoveryRoutineNames.BackwardsSearch,
                DiscoveryRoutineNames.Identification,
                DiscoveryRoutineNames.Screening,
            }.Order(),
            rows);

        var discoveryRows = await context.Routines
            .Where(row => row.Name == DiscoveryRoutineNames.WantedSweep
                || row.Name == DiscoveryRoutineNames.Screening
                || row.Name == DiscoveryRoutineNames.BackwardsSearch
                || row.Name == DiscoveryRoutineNames.Identification)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(discoveryRows, row => Assert.Equal(database.Time.GetUtcNow(), row.DueAt));
    }

    [Fact]
    public async Task Only_never_run_dormant_foundation_rows_are_activated_on_upgrade()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Routines.AddRange(
                new RoutineRow
                {
                    Name = DiscoveryRoutineNames.WantedSweep,
                    Target = IndexerId.ToString("D"),
                    Lane = Lane.Sync,
                    DueAt = DateTimeOffset.MaxValue,
                },
                new RoutineRow
                {
                    Name = DiscoveryRoutineNames.Screening,
                    Lane = Lane.Bulk,
                    DueAt = DateTimeOffset.MaxValue,
                },
                new RoutineRow
                {
                    Name = DiscoveryRoutineNames.Identification,
                    Lane = Lane.Sync,
                    DueAt = DateTimeOffset.MaxValue,
                    LastFailureAt = FirstSeen,
                    ConsecutiveFailures = 3,
                });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            await scope.ServiceProvider.GetRequiredService<DiscoveryState>()
                .EnsureFoundationAsync(TestContext.Current.CancellationToken);
        }

        await using var check = database.Scope();
        var rows = await check.ServiceProvider.GetRequiredService<FabDbContext>().Routines
            .Where(row => row.Name == DiscoveryRoutineNames.WantedSweep
                || row.Name == DiscoveryRoutineNames.Screening
                || row.Name == DiscoveryRoutineNames.BackwardsSearch
                || row.Name == DiscoveryRoutineNames.Identification)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(database.Time.GetUtcNow(), rows.Single(row => row.Name == DiscoveryRoutineNames.WantedSweep).DueAt);
        Assert.Equal(database.Time.GetUtcNow(), rows.Single(row => row.Name == DiscoveryRoutineNames.Screening).DueAt);
        Assert.Equal(database.Time.GetUtcNow(), rows.Single(row => row.Name == DiscoveryRoutineNames.BackwardsSearch).DueAt);
        Assert.Equal(DateTimeOffset.MaxValue, rows.Single(row => row.Name == DiscoveryRoutineNames.Identification).DueAt);
    }

    [Fact]
    public async Task The_registrar_reuses_canonical_targets_created_for_an_upgraded_indexer()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<DiscoveryState>()
                .EnsureFoundationAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<RoutineRegistrar>()
                .EnsureRowsExistAsync(TestContext.Current.CancellationToken);
        }

        await using var check = database.Scope();
        var rows = await check.ServiceProvider.GetRequiredService<FabDbContext>().Routines
            .Where(row => row.Name.StartsWith("indexer.") || row.Name.StartsWith("release."))
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, rows.Count);
        Assert.All(
            rows.Where(row => row.Target is not null),
            row => Assert.Equal(IndexerId.ToString("D"), row.Target));
    }

    private static async Task SeedIndexerAsync(TestDatabase database, string categories = "Adult")
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.Indexers.Add(new IndexerRow
        {
            Id = IndexerId,
            Name = "Recorded",
            Url = "https://indexer.invalid/api",
            ApiKey = "first-key",
            Categories = categories,
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = FirstSeen,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static NewznabRelease Item(string id, string url) => new(
        id, id, "A title", "a title", 123, ["5010"], FirstSeen, FirstSeen, url);

    private static ServiceProvider Services(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddLogging();
        services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddScoped<NewznabGateway>();
        return services.BuildServiceProvider();
    }

    private static string Recorded(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "ReleaseDiscovery", "Recorded", name));

    private sealed class RecordedIndexer(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        TimeSpan? retryAfter = null) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            };
            if (retryAfter is not null) response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
            return Task.FromResult(response);
        }
    }
}
