using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Host.Catalogue;

/// <summary>
/// The route a grid asks for a picture on. ADR 0030: the grid asks the tool,
/// never the CDN.
/// </summary>
/// <remarks>
/// <para>
/// The one route in this application that is not ADR 0040's shape, and
/// deliberately: it answers with bytes rather than with a typed verdict, because
/// what asks for it is an <c>&lt;img&gt;</c> tag rather than the generated
/// client. So there is nothing here for the frontend's types to carry — the
/// address is the contract, and an empty answer tells the browser to draw the
/// no-artwork tile without recording a failed resource request.
/// </para>
/// <para>
/// <strong>Named by the video and not by the image.</strong> A caller listing a
/// grid holds video ids; which image is that video's is ADR 0027's choice and
/// belongs on this side of the line. It also means a changed choice needs no
/// change in the browser — the same address answers with the new picture.
/// </para>
/// <para>
/// <strong>It may fetch, and that is the first page request in this tool that
/// does network I/O.</strong> What keeps it from being the first that can hang
/// is the artwork transport's short timeout and this returning nothing rather
/// than waiting. It spends no prdb budget, so ADR 0018's rule that refreshing
/// never causes work is intact.
/// </para>
/// </remarks>
public static class ArtworkEndpoints
{
    /// <summary>
    /// How long a browser may keep an image before asking again.
    /// </summary>
    /// <remarks>
    /// A year, and <c>immutable</c> with it. The answer under one address
    /// changes only when prdb publishes a different first image, which for a CDN
    /// image already published is vanishingly rare and stale rather than wrong
    /// when it happens. ADR 0059 measured what the day this replaced was buying,
    /// which was nothing: it holds a grid for one session and expires before a
    /// person who logs in every two or three days comes back, so every visit
    /// re-fetched every tile through the server. A horizon that does not outlive
    /// the gap between two visits is a horizon nobody is on the near side of.
    /// </remarks>
    public const int CacheSeconds = 365 * 24 * 60 * 60;

    /// <summary>
    /// How long a browser may remember that prdb has no image for a Video.
    /// </summary>
    /// <remarks>
    /// A week. This is the Catalogue's answer rather than this minute's — prdb
    /// publishes no image, or publishes one whose URL was found dead — and it is
    /// the answer for thousands of Videos at a time on a Catalogue this size.
    /// Not forever, because a repair pass may yet read an image onto a row that
    /// had none; a week is long enough that the tiles stop reaching the server
    /// and short enough that one eventually shows up.
    /// </remarks>
    public const int SettledAbsenceSeconds = 7 * 24 * 60 * 60;

    /// <summary>
    /// How long a browser may remember an absence that is about this attempt
    /// rather than about the Video: a CDN that did not answer inside the
    /// artwork transport's timeout, or bytes that were not on disk after all.
    /// </summary>
    public const int AbsentCacheSeconds = 5 * 60;

    public static void MapArtwork(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/artwork/{videoId:long}", async (
            long videoId,
            ArtworkCache cache,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var answer = await cache.ServeAsync(videoId, cancellationToken);

            if (answer.Served is not { } served)
            {
                // The same thing to a grid — draw the tile — and two different
                // things to the browser, which is the whole of why the cache
                // says which of them this is.
                http.Response.Headers.CacheControl = Absence(answer);
                return Results.NoContent();
            }

            http.Response.Headers.CacheControl = Present;

            return Results.Stream(served.Bytes, served.MediaType);
        })
        .WithTags("Artwork");

        // One named picture, for a Preview's gallery (ADR 0060). Addressed by
        // the image rather than by the Video, because a gallery was told which
        // pictures there are and a position moves when prdb publishes another.
        routes.MapGet("/api/artwork/images/{imageId:guid}", async (
            Guid imageId,
            ArtworkCache cache,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var answer = await cache.ServeImageAsync(imageId, cancellationToken);

            if (answer.Served is not { } served)
            {
                http.Response.Headers.CacheControl = Absence(answer);
                return Results.NoContent();
            }

            http.Response.Headers.CacheControl = Present;

            return Results.Stream(served.Bytes, served.MediaType);
        })
        .WithTags("Artwork");

        routes.MapGet("/api/artwork/actors/{actorId:guid}", async (
            Guid actorId,
            ActorArtworkCache cache,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var answer = await cache.ServeAsync(actorId, cancellationToken);
            if (answer.Served is not { } served)
            {
                http.Response.Headers.CacheControl = Absence(answer);
                return Results.NoContent();
            }

            http.Response.Headers.CacheControl = Present;
            return Results.Stream(served.Bytes, served.MediaType);
        }).WithTags("Artwork");
    }

    /// <summary>What a browser may do with an image it has been given.</summary>
    private static string Present => $"private, max-age={CacheSeconds}, immutable";

    /// <summary>What a browser may do with a tile it has not been given.</summary>
    private static string Absence(ArtworkAnswer answer) =>
        $"private, max-age={(answer.AbsenceStands ? SettledAbsenceSeconds : AbsentCacheSeconds)}";
}
