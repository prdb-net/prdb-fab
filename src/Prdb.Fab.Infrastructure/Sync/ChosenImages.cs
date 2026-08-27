using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Which of a video's images is <em>the</em> image: the first entry of
/// <c>images[]</c> carrying a URL, as ADR 0027 chose it and ADR 0030 caches it.
/// </summary>
/// <remarks>
/// <para>
/// Not a fresh choice and not a ranking. prdb documents the array as ordered
/// oldest first with the image id breaking ties, stable across requests, and
/// expressly says the order is not a judgement about which image is best. The
/// oldest is taken because two runs take the same one — reproducibility is the
/// property a filing decision needs, and the grid showing what filing wrote is
/// the property a person notices.
/// </para>
/// <para>
/// A clause rather than a column, for the reason ADR 0033 gave about the pin: a
/// stored <em>this is the chosen one</em> would have two writers — a detail read
/// and the images feed — and no reader that would notice the two disagreeing.
/// The order is on the row (<see cref="CatalogueImageRow.Position"/>), and which
/// row wins is arithmetic over it.
/// </para>
/// <para>
/// The tie is broken by the image id, which is what prdb says it breaks its own
/// by. In practice there is no tie: a position is an index into one array. It is
/// written down so that two installations reading the same payload cannot
/// choose differently, which is the whole of why the oldest is chosen at all.
/// </para>
/// </remarks>
public static class ChosenImages
{
    /// <summary>
    /// Narrows <paramref name="images"/> to the ones that are their video's
    /// choice.
    /// </summary>
    /// <param name="context">
    /// The table the comparison is made against, which is the whole table rather
    /// than <paramref name="images"/> — an image is the choice of its video, and
    /// a caller that has already narrowed to the uncached ones would otherwise
    /// be told that the second image is the first.
    /// </param>
    public static IQueryable<CatalogueImageRow> In(
        FabDbContext context,
        IQueryable<CatalogueImageRow> images) =>
        images
            .Where(image => image.Url != string.Empty)
            .Where(image => !context.CatalogueImages.Any(other =>
                other.VideoId == image.VideoId
                && other.Url != string.Empty
                && (other.Position < image.Position
                    || (other.Position == image.Position
                        && other.PrdbId.CompareTo(image.PrdbId) < 0))));

    /// <summary>The video's chosen image, or null where it publishes none.</summary>
    public static Task<CatalogueImageRow?> OfAsync(
        FabDbContext context,
        long videoId,
        CancellationToken cancellationToken) =>
        In(context, context.CatalogueImages.Where(image => image.VideoId == videoId))
            .SingleOrDefaultAsync(cancellationToken);
}
