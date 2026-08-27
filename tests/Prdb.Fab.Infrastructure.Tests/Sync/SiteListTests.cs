using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0013's site list: one request, replaced wholesale, and never a deletion.
/// </summary>
public sealed class SiteListTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string Sites = "/sites";

    private const string OneVersion = "W/\"sites-1\"";
    private const string AnotherVersion = "W/\"sites-2\"";

    private static readonly Guid First = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>
    /// The ordinary day: one conditional request, and prdb saying there is
    /// nothing to say.
    /// </summary>
    [Fact]
    public async Task A_not_modified_leaves_the_rows_and_the_validator_alone()
    {
        var prdb = new FakePrdbApi()
            .Answers(Sites, SitePage([(First, "One", "A Network")]), OneVersion)
            .AnswersNotModified(Sites, OneVersion);

        await using var database = await CreateAsync(prdb);

        var first = await RunAsync(database);
        var second = await RunAsync(database);

        Assert.Equal(1, first.ItemsHandled);

        // Nothing changed, so nothing was handled — and it is still a run, so
        // the routine's last success moves and no Gap appears out of a quiet
        // day.
        Assert.Equal(0, second.ItemsHandled);
        Assert.NotNull(second.Outcome);

        // The second request carried what the first was given.
        Assert.Null(prdb.AskingFor(Sites)[0].IfNoneMatch);
        Assert.Equal(OneVersion, prdb.AskingFor(Sites)[1].IfNoneMatch);

        await using var scope = database.Scope();

        Assert.Equal(
            OneVersion,
            await scope.ServiceProvider
                .GetRequiredService<FeedCursors>()
                .TokenAsync(Feed.Sites, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The API document calls this expected rather than an error: the shared
    /// read-only cache does not vary by <c>If-None-Match</c>, so a request that
    /// hits it is answered <c>200</c> with a body even while the validator still
    /// matches. Reading that as a change would replace the whole table daily for
    /// nothing.
    /// </summary>
    [Fact]
    public async Task A_body_under_the_validator_this_tool_already_had_is_not_a_change()
    {
        var page = SitePage([(First, "One", "A Network")]);

        var prdb = new FakePrdbApi().Answers(Sites, page, OneVersion).Answers(Sites, page, OneVersion);

        await using var database = await CreateAsync(prdb);

        await RunAsync(database);
        var second = await RunAsync(database);

        Assert.Equal(0, second.ItemsHandled);
    }

    /// <summary>
    /// ADR 0013: a site row is never deleted, only marked as no longer offered.
    /// ADR 0005 builds a filed path out of the site's title and ADR 0017 makes
    /// the recorded path the truth from then on, so a library entry has to keep
    /// being able to name the site it was built from.
    /// </summary>
    [Fact]
    public async Task A_site_that_has_left_the_list_is_still_readable()
    {
        var prdb = new FakePrdbApi()
            .Answers(Sites, SitePage([(First, "One", "A Network"), (Second, "Two", null)]), OneVersion)
            .Answers(Sites, SitePage([(First, "One", "A Network")]), AnotherVersion);

        await using var database = await CreateAsync(prdb);

        await RunAsync(database);
        await RunAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var sites = await context.CatalogueSites
            .OrderBy(row => row.Title)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, sites.Count);
        Assert.True(sites[0].StillOffered);

        Assert.Equal("Two", sites[1].Title);
        Assert.False(sites[1].StillOffered);
    }

    /// <summary>
    /// And it comes back if prdb offers it again, which is the one way that flag
    /// is ever cleared.
    /// </summary>
    [Fact]
    public async Task A_site_that_comes_back_is_offered_again()
    {
        var prdb = new FakePrdbApi()
            .Answers(Sites, SitePage([(First, "One", null), (Second, "Two", null)]), OneVersion)
            .Answers(Sites, SitePage([(First, "One", null)]), AnotherVersion)
            .Answers(Sites, SitePage([(First, "One", null), (Second, "Two", null)]), OneVersion);

        await using var database = await CreateAsync(prdb);

        await RunAsync(database);
        await RunAsync(database);
        await RunAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.All(
            await context.CatalogueSites.ToListAsync(TestContext.Current.CancellationToken),
            site => Assert.True(site.StillOffered));
    }

    /// <summary>
    /// The whole list in one request, which is what makes a feed, a cursor and a
    /// diff unnecessary.
    /// </summary>
    [Fact]
    public async Task The_whole_list_is_asked_for_at_once()
    {
        var prdb = new FakePrdbApi().Answers(Sites, SitePage([(First, "One", null)]), OneVersion);

        await using var database = await CreateAsync(prdb);

        await RunAsync(database);

        Assert.Equal(
            SiteListRoutine.TheWholeList.ToString(),
            System.Web.HttpUtility.ParseQueryString(prdb.AskedFor(Sites).Single().Query)["PageSize"]);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb)
    {
        var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddFabSync());

        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
            row => row.SetProperty(installation => installation.PrdbApiKey, ApiKey),
            TestContext.Current.CancellationToken);

        return database;
    }

    private static async Task<Core.Scheduling.RunResult> RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<SiteListRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    private static string SitePage(IReadOnlyList<(Guid Id, string Title, string? Network)> sites) =>
        $$"""
        {
          "items": [
            {{string.Join(",", sites.Select(site => $$"""
            {
              "id": "{{site.Id}}",
              "title": "{{site.Title}}",
              "url": "https://example.invalid",
              {{(site.Network is null ? "" : $"\"networkTitle\": \"{site.Network}\",")}}
              "createdAtUtc": "2026-08-27T12:00:00.0000000Z",
              "updatedAtUtc": "2026-08-27T12:00:00.0000000Z"
            }
            """))}}
          ],
          "page": 1,
          "pageSize": 1000,
          "totalCount": {{sites.Count}},
          "sortBy": "title",
          "sortDirection": "asc"
        }
        """;
}
