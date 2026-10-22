using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>The durable work set used by scheduling and local Status counts.</summary>
public static class ReleaseIdentificationWork
{
    public static IQueryable<ReleaseRow> Current(FabDbContext context, DateTimeOffset now, bool includeDelayed = false)
    {
        var since = now.AddDays(-IndexerWindowPolicy.InitialDays);
        var dueBefore = now - RecentWindow.RevalidateAfter;
        return context.Releases.Where(row => row.IdentificationDetailsPending
                && (includeDelayed || row.DetailRetryAt == null || row.DetailRetryAt <= now)
            || !row.IdentificationDetailsPending && (row.IdentificationState == IdentificationState.Awaiting
                || (row.PostDate >= since || row.VideoId != null && context.WantedVideos.Any(wanted => wanted.VideoId == row.VideoId))
                    && (row.LastIdentifiedAt == null || row.LastIdentifiedAt <= dueBefore)));
    }
}
