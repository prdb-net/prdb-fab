using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Account restrictions observed at prdb, never an Ignore write path.</summary>
public sealed class IgnoredSites(FabDbContext context)
{
    public const string Explanation = "This Site is ignored in your prdb account. Unignore it in prdb before adding a Favourite or Wanted Video.";

    public async Task<bool> RestrictsAsync(AccountPreferenceKind kind, Guid entityId, CancellationToken token)
    {
        Guid? siteId = kind switch
        {
            AccountPreferenceKind.FavouriteSite => entityId,
            AccountPreferenceKind.WantedVideo => await context.CatalogueVideos.Where(v => v.PrdbId == entityId)
                .Select(v => v.Site == null ? (Guid?)null : v.Site.PrdbId).SingleOrDefaultAsync(token),
            _ => null,
        };
        return siteId is { } id && await context.IgnoredSites.AnyAsync(s => s.SitePrdbId == id && s.Ignored, token);
    }

    public Task<bool> RejectsSiteFeedAsync(Guid siteId, DateTimeOffset stamp, CancellationToken token) =>
        context.IgnoredSites.AnyAsync(s => s.SitePrdbId == siteId && (s.Ignored || s.UpdatedAtUtc >= stamp), token);

    public async Task<bool> RejectsWantedFeedAsync(Guid videoId, DateTimeOffset stamp, CancellationToken token)
    {
        var siteId = await context.CatalogueVideos.Where(v => v.PrdbId == videoId)
            .Select(v => v.Site == null ? (Guid?)null : v.Site.PrdbId).SingleOrDefaultAsync(token);
        return siteId is { } id && await RejectsSiteFeedAsync(id, stamp, token);
    }

    public async Task ApplyAsync(Guid siteId, bool ignored, DateTimeOffset stamp, CancellationToken token)
    {
        var row = await context.IgnoredSites.AsTracking().SingleOrDefaultAsync(s => s.SitePrdbId == siteId, token);
        if (row is not null && row.UpdatedAtUtc > stamp) return;
        if (row is null)
        {
            row = new IgnoredSiteRow { SitePrdbId = siteId };
            context.IgnoredSites.Add(row);
        }
        row.Ignored = ignored;
        row.UpdatedAtUtc = stamp;
        await context.SaveChangesAsync(token);
        if (ignored) await ReconcileAsync(siteId, token);
    }

    /// <summary>Idempotent removal of intent; held content and manual Downloads survive.</summary>
    public async Task ReconcileAsync(Guid siteId, CancellationToken token)
    {
        var videos = context.CatalogueVideos.Where(v => v.Site != null && v.Site.PrdbId == siteId);
        var ids = await videos.Select(v => v.PrdbId).ToListAsync(token);
        var localIds = await videos.Select(v => v.Id).ToListAsync(token);
        await context.AccountPreferenceWrites.Where(w => w.Desired &&
            ((w.Kind == AccountPreferenceKind.FavouriteSite && w.EntityId == siteId) ||
             (w.Kind == AccountPreferenceKind.WantedVideo && ids.Contains(w.EntityId))))
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.Blocked, true).SetProperty(w => w.LastFailure, Explanation), token);
        await context.FavouriteSites.Where(f => f.Site != null && f.Site.PrdbId == siteId).ExecuteDeleteAsync(token);
        await context.WantedVideos.Where(w => localIds.Contains(w.VideoId)).ExecuteDeleteAsync(token);
        await context.Downloads.Where(d => ids.Contains(d.VideoId) && !d.OriginIsPerson &&
            (d.State == DownloadState.Outstanding || d.State == DownloadState.Completed))
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.State, DownloadState.Abandoned)
                .SetProperty(d => d.Cause, (DownloadCause?)null), token);
        await context.Releases.Where(r => r.VideoId != null && localIds.Contains(r.VideoId.Value))
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.AutomationPending, false)
                .SetProperty(r => r.AutomationDecisionReason, AutomationDecisionReason.SiteIgnored), token);
    }
}
