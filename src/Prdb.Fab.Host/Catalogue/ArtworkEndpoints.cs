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
    /// A day, and it is safe at any length because the answer under one address
    /// changes only when prdb publishes a different first image — at which point
    /// the picture is stale rather than wrong. What this actually buys is the
    /// scroll back up the grid not reaching the server at all.
    /// </remarks>
    public const int CacheSeconds = 24 * 60 * 60;

    /// <summary>How long a browser may remember that no image is available.</summary>
    public const int AbsentCacheSeconds = 5 * 60;

    public static void MapArtwork(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/artwork/{videoId:long}", async (
            long videoId,
            ArtworkCache cache,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var served = await cache.ServeAsync(videoId, cancellationToken);

            if (served is null)
            {
                // No image, a URL found dead, or a CDN that did not answer in
                // time. All three are the same thing to a grid: draw the tile.
                http.Response.Headers.CacheControl = $"private, max-age={AbsentCacheSeconds}";
                return Results.NoContent();
            }

            http.Response.Headers.CacheControl = $"private, max-age={CacheSeconds}";

            return Results.Stream(served.Bytes, served.MediaType);
        })
        .WithTags("Artwork");
    }
}
