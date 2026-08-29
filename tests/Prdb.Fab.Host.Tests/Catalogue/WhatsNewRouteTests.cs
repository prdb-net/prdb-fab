using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

/// <summary>
/// What's New, the landing page: a grid over the catalogue that reaches prdb
/// for nothing at all.
/// </summary>
public sealed class WhatsNewRouteTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// ADR 0013's order, which is prdb's own <c>createdAtUtc</c>. Not the order
    /// this installation happened to see them in: the backfill reads backwards,
    /// so an older video discovered later would otherwise sit at the top of a
    /// page calling itself What's New.
    /// </summary>
    [Fact]
    public async Task Whats_new_answers_with_the_catalogue_newest_first()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        // Written in the wrong order on purpose, so the answer is a sort rather
        // than the order the rows were inserted in.
        await FillAsync(application, ("The middle one", 2), ("The oldest", 3), ("The newest", 1));

        var page = await ReadAsync(client, page: 1);

        Assert.Equal(
            ["The newest", "The middle one", "The oldest"],
            page.Videos.Select(video => video.Title));
    }

    /// <summary>
    /// ADR 0036: what is worth linking to is in the address, which for a grid is
    /// where in it the user is. So the page is a parameter, and asking for it
    /// twice answers the same — which is the whole of a reload landing where it
    /// left off.
    /// </summary>
    [Fact]
    public async Task A_page_is_in_the_address_and_answers_the_same_twice()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        await FillAsync(
            application,
            [.. Enumerable.Range(1, 60).Select(number => ($"Video {number}", 61 - number))]);

        var second = await ReadAsync(client, page: 2);
        var again = await ReadAsync(client, page: 2);

        Assert.Equal(2, second.Page);
        Assert.Equal(60, second.Total);

        // Forty-eight to a page, so the second holds the remaining twelve.
        Assert.Equal(12, second.Videos.Count);

        Assert.Equal(
            again.Videos.Select(video => video.Title),
            second.Videos.Select(video => video.Title));
    }

    /// <summary>
    /// ADR 0027 has the library grid never read the library; the counterpart
    /// here is that a browse grid never reads prdb. Asserted against a socket
    /// that refuses everything, so a request would fail loudly rather than being
    /// counted somewhere.
    /// </summary>
    [Fact]
    public async Task The_surface_makes_no_request_to_prdb()
    {
        var prdb = new RefusesEverything();

        await using var application = new FabApplication()
            .Answering(FabTransports.Prdb, prdb);

        using var client = await application.SignedInClientAsync();

        await FillAsync(application, ("A video", 1));

        var page = await ReadAsync(client, page: 1);

        Assert.Single(page.Videos);
        Assert.Equal(0, prdb.Requests);
    }

    /// <summary>
    /// A card whose video publishes no image. ADR 0030 answers nothing, and the
    /// grid draws the frame it was going to draw anyway — the layout is the box,
    /// not the picture.
    /// </summary>
    [Fact]
    public async Task A_video_with_no_image_answers_nothing_rather_than_failing()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        await FillAsync(application, ("A video with no artwork", 1));

        var page = await ReadAsync(client, page: 1);

        using var artwork = await client.GetAsync(
            $"/api/artwork/{page.Videos[0].Id}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, artwork.StatusCode);
    }

    /// <summary>
    /// ADR 0010: everything is behind the password unless it says otherwise, and
    /// this does not say otherwise.
    /// </summary>
    [Fact]
    public async Task The_surface_is_behind_the_password()
    {
        await using var application = new FabApplication();

        using var client = application.CreateClient();

        using var answer = await client.GetAsync(
            "/api/catalogue/whats-new",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }

    private static async Task<Answer> ReadAsync(HttpClient client, int page)
    {
        using var answer = await client.GetAsync(
            $"/api/catalogue/whats-new?page={page}",
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();

        return (await answer.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Catalogue rows written straight. What is under test is the order a page
    /// comes back in, and a fixture that spent a prdb request per row would say
    /// nothing more about it.
    /// </summary>
    private static async Task FillAsync(
        FabApplication application,
        params (string Title, int DaysOld)[] videos)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        foreach (var (title, daysOld) in videos)
        {
            context.CatalogueVideos.Add(new CatalogueVideoRow
            {
                PrdbId = Guid.NewGuid(),
                Title = title,
                CreatedAtUtc = Noon.AddDays(-daysOld),
                UpdatedAtUtc = Noon.AddDays(-daysOld),
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record Card(long Id, string Title, string? Site, DateOnly? ReleaseDate);

    private sealed record Answer(IReadOnlyList<Card> Videos, int Page, int PageSize, int Total);

    /// <summary>
    /// A socket that answers nothing. ADR 0042 puts the fake here rather than at
    /// an interface, so a request that should not have been made fails where it
    /// would really be made.
    /// </summary>
    private sealed class RefusesEverything : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;

            throw new HttpRequestException("Nothing here may reach prdb.");
        }
    }
}
