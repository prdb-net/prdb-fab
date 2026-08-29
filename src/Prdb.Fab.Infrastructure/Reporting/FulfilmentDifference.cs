using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Reporting;

/// <summary>One Fulfilment whose desired and last-reported states differ.</summary>
public sealed record PendingFulfilment(
    Guid VideoId,
    FulfilmentQuality? Quality,
    DateTimeOffset FulfilledAt);

/// <summary>
/// ADR 0019's computed difference. The desired side is always read from the
/// Library Entry and its Video Files; nothing probes or stats a filed path.
/// </summary>
public sealed class FulfilmentDifference(FabDbContext context)
{
    public async Task<IReadOnlyList<PendingFulfilment>> PendingAsync(
        string userHash,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        var heldWanted = await (
            from entry in context.LibraryEntries
            join video in context.CatalogueVideos on entry.VideoId equals video.PrdbId
            join wanted in context.WantedVideos on video.Id equals wanted.VideoId
            orderby entry.FiledAt, entry.VideoId
            select new { entry.VideoId, entry.FiledAt })
            .ToListAsync(cancellationToken);

        if (heldWanted.Count == 0)
        {
            return [];
        }

        // Keep this as joins rather than an IN over the held ids. A mature
        // library can easily exceed SQLite's bound-parameter ceiling, while
        // both source columns are already indexed for this relationship.
        var files = await (
            from file in context.VideoFiles
            join entry in context.LibraryEntries
                on file.LibraryEntryVideoId equals entry.VideoId
            select new { file.LibraryEntryVideoId, file.QualityLabel })
            .ToListAsync(cancellationToken);
        var qualities = files
            .GroupBy(file => file.LibraryEntryVideoId)
            .ToDictionary(
                group => group.Key,
                group => FulfilmentQualities.HighestTruthfullyReportable(group.Select(file => file.QualityLabel)));

        var reported = await context.ReportedStates
            .Where(row => row.UserHash == userHash)
            .ToDictionaryAsync(row => row.VideoId, cancellationToken);

        IEnumerable<PendingFulfilment> pending = heldWanted
            .Select(entry => new PendingFulfilment(
                entry.VideoId,
                qualities.GetValueOrDefault(entry.VideoId),
                entry.FiledAt))
            .Where(desired => IsPending(desired, reported.GetValueOrDefault(desired.VideoId)));

        if (take is { } count)
        {
            pending = pending.Take(count);
        }

        return [.. pending];
    }

    public async Task<int> CountAsync(string userHash, CancellationToken cancellationToken = default) =>
        (await PendingAsync(userHash, cancellationToken: cancellationToken)).Count;

    private static bool IsPending(PendingFulfilment desired, ReportedStateRow? reported) =>
        reported is null
        || (reported.TerminalOutcome is null
            && (!reported.IsFulfilled
                || reported.Quality != desired.Quality
                || reported.FulfilledAt != desired.FulfilledAt));
}
