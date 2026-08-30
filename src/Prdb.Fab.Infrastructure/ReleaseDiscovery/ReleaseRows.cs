using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class ReleaseRows(
    FabDbContext context,
    ReleaseEviction eviction,
    ReleaseWriteGate writes)
{
    public async Task<ReleaseWrite> UpsertAsync(
        Guid indexerId,
        IReadOnlyList<NewznabRelease> releases,
        DateTimeOffset firstSeen,
        ReleaseSource source,
        CancellationToken cancellationToken)
    {
        await using var held = await writes.EnterAsync(cancellationToken);
        var identities = releases.Select(release => release.DerivedReleaseId).Distinct().ToArray();
        var stored = await context.Releases.AsTracking()
            .Where(row => row.IndexerId == indexerId && identities.Contains(row.DerivedReleaseId))
            .ToDictionaryAsync(row => row.DerivedReleaseId, cancellationToken);
        var added = 0;

        foreach (var release in releases.GroupBy(item => item.DerivedReleaseId).Select(group => group.Last()))
        {
            if (!stored.TryGetValue(release.DerivedReleaseId, out var row))
            {
                row = new ReleaseRow
                {
                    IndexerId = indexerId,
                    DerivedReleaseId = release.DerivedReleaseId,
                    FirstSeenAt = firstSeen,
                    IdentificationState = source is ReleaseSource.WantedSweep or ReleaseSource.ManualSearch
                        || RecentWindow.Contains(release.PostDate, firstSeen)
                        ? IdentificationState.Awaiting
                        : IdentificationState.Unexamined,
                    SearchWasReason = source is ReleaseSource.WantedSweep or ReleaseSource.ManualSearch,
                };
                context.Releases.Add(row);
                stored.Add(row.DerivedReleaseId, row);
                added++;
            }
            // A sweep result that already exists is the same Release under a
            // louder question. Its state and reason flag are both left alone:
            // provenance is never evidence, and settled answers are not asked
            // again merely because a title search found the row again.

            row.RawGuid = release.RawGuid;
            row.Title = release.Title;
            row.NormalisedTitle = release.NormalisedTitle;
            row.Size = release.Size;
            row.Categories = JsonSerializer.Serialize(release.Categories);
            row.PostDate = release.PostDate;
            row.PubDate = release.PubDate;
            row.DownloadUrl = release.DownloadUrl;
            row.Password = release.Password;
        }

        await context.SaveChangesAsync(cancellationToken);
        if (source == ReleaseSource.ManualSearch)
        {
            // The Manual Search result rows are the pin. Its routine writes
            // those immediately after this returns and only then evicts.
            return new(releases.Count, added, 0, [.. stored.Values.Select(row => row.Id)]);
        }
        var bounded = await eviction.EvictAsync(indexerId, cancellationToken: cancellationToken);
        return new(releases.Count, added, bounded.OverBy, [.. stored.Values.Select(row => row.Id)]);
    }
}

public enum ReleaseSource
{
    IndexerWalk,
    WantedSweep,
    ManualSearch,
}

public sealed record ReleaseWrite(int Seen, int Added, int CacheOverBy, IReadOnlyList<long> ReleaseIds);
