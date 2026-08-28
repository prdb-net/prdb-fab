using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class ReleaseRows(FabDbContext context)
{
    public async Task<ReleaseWrite> UpsertAsync(
        Guid indexerId,
        IReadOnlyList<NewznabRelease> releases,
        DateTimeOffset firstSeen,
        ReleaseSource source,
        CancellationToken cancellationToken)
    {
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
                    IdentificationState = source == ReleaseSource.WantedSweep
                        ? IdentificationState.Awaiting
                        : IdentificationState.Unexamined,
                    SearchWasReason = source == ReleaseSource.WantedSweep,
                };
                context.Releases.Add(row);
                stored.Add(row.DerivedReleaseId, row);
                added++;
            }
            else if (source == ReleaseSource.WantedSweep)
            {
                row.SearchWasReason = true;
                if (row.IdentificationState == IdentificationState.Unexamined)
                {
                    row.IdentificationState = IdentificationState.Awaiting;
                }
            }

            row.RawGuid = release.RawGuid;
            row.Title = release.Title;
            row.NormalisedTitle = release.NormalisedTitle;
            row.Size = release.Size;
            row.Categories = JsonSerializer.Serialize(release.Categories);
            row.PostDate = release.PostDate;
            row.PubDate = release.PubDate;
            row.DownloadUrl = release.DownloadUrl;
        }

        await context.SaveChangesAsync(cancellationToken);
        return new(releases.Count, added);
    }
}

public enum ReleaseSource
{
    IndexerWalk,
    WantedSweep,
}

public sealed record ReleaseWrite(int Seen, int Added);
