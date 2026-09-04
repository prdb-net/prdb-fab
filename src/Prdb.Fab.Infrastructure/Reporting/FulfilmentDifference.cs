using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Reporting;

/// <summary>One Fulfilment whose desired and last-reported states differ.</summary>
public sealed record PendingFulfilment(
    Guid VideoId,
    bool IsFulfilled,
    FulfilmentQuality? Quality,
    DateTimeOffset? FulfilledAt);

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

        var wantedIds = await (
            from wanted in context.WantedVideos
            join video in context.CatalogueVideos on wanted.VideoId equals video.Id
            select video.PrdbId)
            .ToHashSetAsync(cancellationToken);
        var heldIds = heldWanted.Select(entry => entry.VideoId).ToHashSet();

        IEnumerable<PendingFulfilment> pending = heldWanted
            .Select(entry => new PendingFulfilment(
                entry.VideoId,
                true,
                qualities.GetValueOrDefault(entry.VideoId),
                entry.FiledAt))
            .Where(desired => IsPending(desired, reported.GetValueOrDefault(desired.VideoId)))
            .Concat(reported.Values
                .Where(state => state.IsFulfilled
                    && state.TerminalOutcome is null
                    && wantedIds.Contains(state.VideoId)
                    && !heldIds.Contains(state.VideoId))
                .Select(state => new PendingFulfilment(state.VideoId, false, null, null)))
            .OrderBy(desired => desired.FulfilledAt ?? DateTimeOffset.MinValue)
            .ThenBy(desired => desired.VideoId);

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
            && (reported.IsFulfilled != desired.IsFulfilled
                || reported.Quality != desired.Quality
                || reported.FulfilledAt != desired.FulfilledAt));
}
