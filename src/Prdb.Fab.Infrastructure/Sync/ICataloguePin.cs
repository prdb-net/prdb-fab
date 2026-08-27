using System.Linq.Expressions;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// One thing that may point at a catalogue video, as the <c>EXISTS</c> clause
/// that finds it.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0033 made pinning a query rather than a column, and this is the shape
/// that keeps the query from becoming a rewrite every time a table arrives:
/// each source contributes one clause and one index, and
/// <see cref="CataloguePins"/> is what puts them together. A source that is
/// registered is asked; a source that is not does not exist, which is the whole
/// of what has to be true for <em>adding one</em> to be a small change.
/// </para>
/// <para>
/// An expression rather than a method, because the answer is needed inside a
/// query over rows eviction is already walking. Asking per row would be the
/// scan the performance objection to a query assumed, and ADR 0033 rejected the
/// column on the grounds that it does not have to be.
/// </para>
/// </remarks>
public interface ICataloguePin
{
    /// <summary>Which clause this is, for the question <em>why is this row pinned</em>.</summary>
    PinReason Reason { get; }

    /// <summary>Whether this source points at the video.</summary>
    Expression<Func<CatalogueVideoRow, bool>> PointsAt { get; }
}

/// <summary>
/// The wanted list, and the one pinning source this slice has.
/// </summary>
/// <remarks>
/// <c>CONTEXT.md</c> lists six things that may point at a catalogue video and
/// five of their tables do not exist yet. The column this reads is
/// <c>wanted_video</c>'s primary key, so it is indexed by being what the row is
/// — which is the index every source is expected to bring with it.
/// </remarks>
public sealed class WantedVideoPin(FabDbContext context) : ICataloguePin
{
    public PinReason Reason => PinReason.WantedVideo;

    public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
        video => context.WantedVideos.Any(wanted => wanted.VideoId == video.Id);
}
