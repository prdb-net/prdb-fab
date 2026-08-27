using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;
using Prdb.Sdk.Generated.Videos;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The one prdb entity with no change feed, in the two directions ADR 0013
/// gives it: forwards from a high-water mark, and backwards by the page.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the two routines the same way <see cref="ChangeFeed"/> is shared,
/// and for the same reason: what differs between reading forwards every quarter
/// of an hour and reading backwards until a ceiling is scheduling, and none of
/// it is the request.
/// </para>
/// <para>
/// Discovering and writing are two requests deliberately. <c>GET /videos</c>
/// hands back <c>VideoSummaryDto</c>, which carries no image field at all — so
/// ADR 0013's rule that no catalogue row arrives without a detail read means the
/// summary is only ever a list of ids, and the row is written from
/// <c>POST /videos/batch</c>. That is what a page of a hundred actually costs:
/// one request to discover and two to read back.
/// </para>
/// </remarks>
public sealed class WhatsNew(FabDbContext context, PrdbGateway prdb, VideoDetails details)
{
    /// <summary>
    /// The videos on one page that the catalogue does not hold, and how far the
    /// page reached.
    /// </summary>
    /// <param name="Unknown">The ids worth spending a detail read on.</param>
    /// <param name="Newest">
    /// The latest <c>createdAtUtc</c> anywhere on the page, which is what the
    /// high-water mark moves to. Null for a page with nothing on it.
    /// </param>
    /// <param name="Returned">
    /// How many summaries came back. A page short of what was asked for is the
    /// end of what prdb has, which is how the backfill knows to stop early.
    /// </param>
    public sealed record Page(IReadOnlyList<Guid> Unknown, DateTimeOffset? Newest, int Returned)
    {
        public static Page Nothing { get; } = new([], null, 0);
    }

    /// <summary>
    /// One page of <c>GET /videos</c>, sorted by when prdb created the row.
    /// </summary>
    /// <param name="createdAfter">
    /// The high-water mark, already set back by ADR 0013's overlap, or null to
    /// read from the newest end instead. <c>CreatedAfter</c> is documented as
    /// strictly exclusive, which is exactly why the overlap is there.
    /// </param>
    /// <param name="page">
    /// Which page of the descending order to take, for the pass reading
    /// backwards. Null for the forwards one, which pages by the mark instead.
    /// </param>
    /// <remarks>
    /// The two directions are two sort orders, and that is not a detail.
    /// Descending is the natural way to ask what is new, and it cannot be walked
    /// by a mark: page one is the newest hundred of what is new, so a mark
    /// advanced to the top of it steps over everything behind — permanently,
    /// which is the failure the mark exists to prevent. Ascending from the mark
    /// has the page end where the next one begins, so a run reads a hundred, the
    /// mark moves to the last of them, and nothing is between. The one run with
    /// no mark to walk from is the first, and it takes the newest hundred
    /// because that is what What's New means.
    /// </remarks>
    public async Task<Page> ReadAsync(
        string apiKey,
        DateTimeOffset? createdAfter,
        int? page,
        CancellationToken cancellationToken)
    {
        var answer = await prdb.AskAsync(
            apiKey,
            PrdbWork.WhatsNew,
            (client, token) => client.Videos.GetAsync(
                request =>
                {
                    request.QueryParameters.SortBy = GetSortByQueryParameterType.CreatedAtUtc;
                    request.QueryParameters.SortDirection = createdAfter is null
                        ? GetSortDirectionQueryParameterType.Desc
                        : GetSortDirectionQueryParameterType.Asc;
                    request.QueryParameters.CreatedAfter = createdAfter;
                    request.QueryParameters.Page = page;
                    request.QueryParameters.PageSize = Backfill.APage;
                },
                token),
            cancellationToken);

        if (answer?.Items is not { Count: > 0 } items)
        {
            return Page.Nothing;
        }

        var ids = items
            .Where(video => video.Id is not null)
            .Select(video => video.Id!.Value)
            .Distinct()
            .ToList();

        var held = await context.CatalogueVideos
            .Where(row => ids.Contains(row.PrdbId))
            .Select(row => row.PrdbId)
            .ToListAsync(cancellationToken);

        var newest = items
            .Select(video => video.CreatedAtUtc)
            .Where(created => created is not null)
            .Select(created => created!.Value)
            .DefaultIfEmpty()
            .Max();

        return new Page(
            [.. ids.Where(id => !held.Contains(id))],
            newest == default ? null : newest,
            items.Count);
    }

    /// <summary>
    /// Reads <paramref name="ids"/> back in detail, fifty a request, and writes
    /// what comes.
    /// </summary>
    /// <remarks>
    /// Unknown ids are silently omitted from the answer rather than refused,
    /// which is what makes this safe to call with whatever a summary page named:
    /// a video deleted between the two requests is one fewer row, not an error.
    /// </remarks>
    public async Task<int> FetchAsync(
        string apiKey,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var written = 0;

        foreach (var batch in ids.Chunk(Backfill.ABatch))
        {
            var read = await prdb.AskAsync(
                apiKey,
                PrdbWork.WhatsNew,
                (client, token) => client.Videos.Batch.PostAsync(
                    new GetVideosByIdsRequest { Ids = [.. batch.Select(id => (Guid?)id)] },
                    cancellationToken: token),
                cancellationToken);

            foreach (var detail in read ?? [])
            {
                await details.WriteAsync(detail, cancellationToken);
                written++;
            }
        }

        return written;
    }
}
