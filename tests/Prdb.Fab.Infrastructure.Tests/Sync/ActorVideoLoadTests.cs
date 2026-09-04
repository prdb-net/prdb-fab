using System.Globalization;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class ActorVideoLoadTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Videos = "/videos";
    private const string Batch = "/videos/batch";
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Video = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Site = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Noon = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_button_starts_a_durable_release_date_ordered_fill()
    {
        var prdb = new FakePrdbApi()
            .Answers(Videos, Page([Video]))
            .Answers(Batch, Details(Video));
        await using var database = await CreateAsync(prdb);

        ActorVideoLoadStart started;
        await using (var scope = database.Scope())
        {
            started = await scope.ServiceProvider.GetRequiredService<ActorVideoLoads>()
                .StartAsync(Actor, TestContext.Current.CancellationToken);
        }
        Assert.Equal(ActorVideoLoadStartOutcome.Started, started.Outcome);

        await RunAsync(database, Actor);

        var request = Assert.Single(prdb.AskedFor(Videos));
        Assert.Equal(Actor.ToString(), Query(request, "ActorId"));
        Assert.Equal("releaseDate", Query(request, "SortBy"));
        Assert.Equal("desc", Query(request, "SortDirection"));
        Assert.Equal("100", Query(request, "PageSize"));

        await using var after = database.Scope();
        var context = after.ServiceProvider.GetRequiredService<FabDbContext>();
        var state = await context.ActorVideoLoadStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, state.VideosSeen);
        Assert.NotNull(state.CompletedAt);
        Assert.Equal("A loaded Video", (await context.CatalogueVideos.SingleAsync(
            TestContext.Current.CancellationToken)).Title);
        Assert.Single(await context.ActorVideoLoadVideos.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await context.Routines.AnyAsync(row => row.Name == ActorVideoLoadRoutine.RoutineName,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_current_fill_survives_catalogue_eviction()
    {
        var prdb = new FakePrdbApi()
            .Answers(Videos, Page([Video]))
            .Answers(Batch, Details(Video));
        await using var database = await CreateAsync(prdb);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<ActorVideoLoads>()
                .StartAsync(Actor, TestContext.Current.CancellationToken);
        }
        await RunAsync(database, Actor);

        await using var evictionScope = database.Scope();
        var context = evictionScope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.CatalogueVideos.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.CreatedAtUtc, DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);

        var eviction = await evictionScope.ServiceProvider.GetRequiredService<CatalogueEviction>()
            .EvictAsync(ceiling: 0, TestContext.Current.CancellationToken);

        Assert.Equal(0, eviction.Removed);
        Assert.Single(await context.CatalogueVideos.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_fill_stops_at_five_full_pages_and_can_be_started_again()
    {
        var ids = Enumerable.Range(1, 100)
            .Select(index => Guid.Parse($"dddddddd-0000-4000-8000-{index:D12}"))
            .ToList();
        var prdb = new FakePrdbApi().Answers(Videos, Page(ids)).Answers(Batch, "[]");
        await using var database = await CreateAsync(prdb);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<ActorVideoLoads>()
                .StartAsync(Actor, TestContext.Current.CancellationToken);
        }
        for (var page = 1; page <= 5; page++) await RunAsync(database, Actor);

        Assert.Equal(["1", "2", "3", "4", "5"],
            prdb.AskedFor(Videos).Select(request => Query(request, "Page")));

        await using var after = database.Scope();
        var loads = after.ServiceProvider.GetRequiredService<ActorVideoLoads>();
        var state = await after.ServiceProvider.GetRequiredService<FabDbContext>()
            .ActorVideoLoadStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(500, state.VideosSeen);
        Assert.NotNull(state.CompletedAt);

        var restarted = await loads.StartAsync(Actor, TestContext.Current.CancellationToken);
        Assert.Equal(ActorVideoLoadStartOutcome.Started, restarted.Outcome);
        Assert.Equal(0, restarted.Load!.VideosSeen);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb)
    {
        var database = await TestDatabase.CreateAsync(prdb: prdb, also: services => services.AddFabSync());
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.PrdbApiKey, ApiKey),
            TestContext.Current.CancellationToken);
        context.CatalogueActors.Add(new CatalogueActorRow { PrdbId = Actor, Name = "An Actor" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return database;
    }

    private static async Task RunAsync(TestDatabase database, Guid actor)
    {
        await using var scope = database.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<ActorVideoLoadRoutine>()
            .RunAsync(actor.ToString("D"), TestContext.Current.CancellationToken);
        Assert.Equal(RunOutcome.Succeeded, result.Outcome);
    }

    private static string? Query(Uri uri, string name) => HttpUtility.ParseQueryString(uri.Query)[name];

    private static string Page(IReadOnlyList<Guid> videos) =>
        $$"""
        {
          "items": [{{string.Join(',', videos.Select(id => $$"""{"id":"{{id}}","title":"Video","createdAtUtc":"{{Stamp(Noon)}}","actors":[]}"""))}}],
          "page": 1, "pageSize": 100, "totalCount": {{videos.Count}},
          "sortBy": "releaseDate", "sortDirection": "desc"
        }
        """;

    private static string Details(Guid video) =>
        $$"""
        [{
          "id":"{{video}}", "title":"A loaded Video", "createdAtUtc":"{{Stamp(Noon)}}", "updatedAtUtc":"{{Stamp(Noon)}}",
          "site":{"id":"{{Site}}","title":"A Site"},
          "actors":[{"id":"{{Actor}}","name":"An Actor","images":[]}], "images":[], "preNames":[]
        }]
        """;

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
