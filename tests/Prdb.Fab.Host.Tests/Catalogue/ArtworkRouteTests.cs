using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Host.Catalogue;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

/// <summary>
/// ADR 0059's half of the artwork route: how long a browser may keep what it
/// was given, and the two different things an empty answer can mean.
/// </summary>
public sealed class ArtworkRouteTests
{
    private const string Url = "https://cdn.example/artwork.png";

    /// <summary>
    /// A year and <c>immutable</c>. The bytes under one address change only
    /// when prdb publishes a different first image, which is stale rather than
    /// wrong — and the day this replaced expired before a person who logs in
    /// every two or three days came back.
    /// </summary>
    [Fact]
    public async Task An_image_may_be_kept_for_a_year()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Artwork, new OneImage());

        using var client = await application.SignedInClientAsync();

        var videoId = await SeedAsync(application, Url);

        using var answer = await client.GetAsync(
            $"/api/artwork/{videoId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(ArtworkEndpoints.CacheSeconds, MaxAge(answer));
        Assert.True(answer.Headers.CacheControl!.Private);
        Assert.Contains(answer.Headers.CacheControl.Extensions, one => one.Name == "immutable");
    }

    /// <summary>
    /// prdb publishing no image for a Video is the Catalogue's answer rather
    /// than this minute's, and it is the answer for thousands of Videos at a
    /// time — so the tile stops reaching the server for a week.
    /// </summary>
    [Fact]
    public async Task An_absence_that_stands_is_remembered_for_a_week()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var videoId = await SeedAsync(application, url: null);

        using var answer = await client.GetAsync(
            $"/api/artwork/{videoId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
        Assert.Equal(ArtworkEndpoints.SettledAbsenceSeconds, MaxAge(answer));
    }

    /// <summary>
    /// A CDN that did not answer is about this minute, and nothing about the
    /// Video. Collapsing it into the one above would remember one bad minute
    /// for a week.
    /// </summary>
    [Fact]
    public async Task A_cdn_that_did_not_answer_is_remembered_for_five_minutes()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Artwork, new NothingWorks());

        using var client = await application.SignedInClientAsync();

        var videoId = await SeedAsync(application, Url);

        using var answer = await client.GetAsync(
            $"/api/artwork/{videoId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
        Assert.Equal(ArtworkEndpoints.AbsentCacheSeconds, MaxAge(answer));
    }

    /// <summary>
    /// ADR 0060's gallery route: the same machine addressed by the picture
    /// rather than by the Video, because a gallery was told which pictures
    /// there are and a position moves when prdb publishes another.
    /// </summary>
    [Fact]
    public async Task A_named_picture_is_served_and_may_be_kept_for_a_year()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Artwork, new OneImage());

        using var client = await application.SignedInClientAsync();

        var imageId = Guid.NewGuid();

        await SeedAsync(application, Url, imageId);

        using var answer = await client.GetAsync(
            $"/api/artwork/images/{imageId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(ArtworkEndpoints.CacheSeconds, MaxAge(answer));
    }

    /// <summary>
    /// An id no image row carries. It is the Catalogue's answer rather than
    /// this minute's — a picture prdb hard-deleted, or a Video evicted out from
    /// under a link — so the tile stops asking for a week.
    /// </summary>
    [Fact]
    public async Task A_picture_the_catalogue_does_not_name_is_an_absence_that_stands()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        using var answer = await client.GetAsync(
            $"/api/artwork/images/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
        Assert.Equal(ArtworkEndpoints.SettledAbsenceSeconds, MaxAge(answer));
    }

    /// <summary>
    /// Read off the parsed header rather than the string, because the writer
    /// normalises the order of the directives and the order is not what any of
    /// this is about.
    /// </summary>
    private static int? MaxAge(HttpResponseMessage answer) =>
        (int?)answer.Headers.CacheControl?.MaxAge?.TotalSeconds;

    /// <summary>One Catalogue Video, with an image row or without one.</summary>
    private static async Task<long> SeedAsync(
        FabApplication application,
        string? url,
        Guid? imageId = null)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var video = new CatalogueVideoRow
        {
            PrdbId = Guid.NewGuid(),
            Title = "A scene",
            NormalisedTitle = "a scene",
        };

        context.CatalogueVideos.Add(video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (url is not null)
        {
            context.CatalogueImages.Add(new CatalogueImageRow
            {
                PrdbId = imageId ?? Guid.NewGuid(),
                VideoId = video.Id,
                Url = url,
                Position = 0,
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return video.Id;
    }

    /// <summary>A CDN with one picture on it.</summary>
    private sealed class OneImage : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var image = new byte[64];

            ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            signature.CopyTo(image);

            var answer = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(image),
            };

            answer.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

            return Task.FromResult(answer);
        }
    }

    /// <summary>
    /// A CDN having a bad minute. ADR 0030 is explicit that this is not a dead
    /// URL, so no row is marked and the answer is only about this attempt.
    /// </summary>
    private sealed class NothingWorks : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
