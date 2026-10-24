using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Automation;

/// <summary>A supplied permission with durable, account-scoped admission; never a refillable queue.</summary>
public sealed class WantedAutopilot(FabDbContext context, TimeProvider time, AutomationRuleSettings rules)
{
    public const int InitialLimit = 100;
    public const int RecentDates = 90;
    public const string PolicyName = "Recent Wanted autopilot";

    public async Task SetEnabledAsync(bool enabled, CancellationToken token = default)
    {
        var row = await context.AutomationRules.AsTracking().SingleOrDefaultAsync(row => row.SuppliedWanted, token);
        if (row is null)
        {
            row = new() { Id = Guid.CreateVersion7(time.GetUtcNow()), Name = PolicyName, SuppliedWanted = true };
            context.AutomationRules.Add(row);
        }
        row.Enabled = enabled;
        await context.SaveChangesAsync(token);
        await rules.QueueCatchUpAsync(token);
    }

    public IQueryable<CatalogueVideoRow> AdmittedVideos(string? userHash)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var from = today.AddDays(1 - RecentDates);
        return context.CatalogueVideos.Where(video => video.ReleaseDate >= from && video.ReleaseDate <= today
            && context.AutopilotMemberships.Any(member => member.UserHash == userHash && member.VideoId == video.PrdbId && member.Admitted));
    }

    public async Task<int> ReconcileAsync(CancellationToken token)
    {
        var installation = await context.Installation.SingleAsync(token);
        if (installation.OnboardingStep != OnboardingStep.Complete || string.IsNullOrWhiteSpace(installation.PrdbUserHash)
            || string.IsNullOrWhiteSpace(installation.PrdbApiKey) || string.IsNullOrWhiteSpace(installation.SabnzbdApiKey)
            || !await context.Indexers.AnyAsync(row => row.Enabled, token)
            || !await context.AutomationRules.AnyAsync(row => row.SuppliedWanted && row.Enabled, token)
            || !await context.CollectionPreparations.AnyAsync(row => row.Phase == "Ready", token)) return 0;
        var account = installation.PrdbUserHash;
        var baseline = await context.AutopilotBaselines.SingleOrDefaultAsync(row => row.UserHash == account, token);
        // An empty authoritative page proves the initial account feed is complete.
        // Nonempty final pages retain their tie-breaker and are followed once more.
        var position = await new FeedCursors(context, time).PositionAsync(Feed.WantedVideos, token);
        if (position is null || position.Unfinished != null || position.At == DateTimeOffset.MinValue) return 0;
        var wanted = await context.WantedVideos.Include(row => row.Video).ThenInclude(video => video!.Site).ToListAsync(token);
        if (baseline is null && wanted.Any(row => row.Video!.Available && row.Video.LastReadAt == default)) return 0;
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = today.AddDays(1 - RecentDates);
        var held = (await context.LibraryEntries.Select(row => row.VideoId).ToListAsync(token)).ToHashSet();
        var work = (await context.Downloads.Where(row => row.State == DownloadState.Outstanding || row.State == DownloadState.Completed)
            .Select(row => row.VideoId).ToListAsync(token)).ToHashSet();
        var reviews = (await context.ArrivingFiles.Where(row => row.State != ArrivingFileState.Filed && row.VideoId != null)
            .Select(row => row.VideoId!.Value).ToListAsync(token)).ToHashSet();
        reviews.UnionWith(await context.ArrivingFileCandidates.Select(row => row.VideoId).ToListAsync(token));
        var ignored = (await context.IgnoredSites.Where(row => row.Ignored).Select(row => row.SitePrdbId).ToListAsync(token)).ToHashSet();
        bool Eligible(CatalogueVideoRow video) => video.Available && video.LastReadAt != default
            && video.ReleaseDate >= from && video.ReleaseDate <= today && !held.Contains(video.PrdbId)
            && !work.Contains(video.PrdbId) && !reviews.Contains(video.PrdbId)
            && (video.Site == null || !ignored.Contains(video.Site.PrdbId));
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var members = await context.AutopilotMemberships.AsTracking().Where(row => row.UserHash == account).ToDictionaryAsync(row => row.VideoId, token);
        var initial = baseline is null;
        var chosen = initial ? wanted.Where(row => Eligible(row.Video!)).OrderByDescending(row => row.Video!.ReleaseDate)
            .ThenBy(row => row.Video!.PrdbId).Take(InitialLimit).Select(row => row.Video!.PrdbId).ToHashSet() : [];
        var changed = 0;
        foreach (var row in wanted)
        {
            var video = row.Video!;
            if (!initial && video.LastReadAt == default && video.Available) continue;
            members.TryGetValue(video.PrdbId, out var member);
            if (!initial && member is not null && (row.SinceAt <= member.WantedSinceAt || row.SinceAt <= baseline!.BaselineAt)) continue;
            var admitted = initial ? chosen.Contains(video.PrdbId)
                : row.SinceAt > baseline!.BaselineAt && Eligible(video);
            if (member is null)
            {
                member = new() { UserHash = account, VideoId = video.PrdbId };
                context.AutopilotMemberships.Add(member);
            }
            member.WantedSinceAt = row.SinceAt;
            member.Admitted = admitted;
            if (initial) member.InitialBatch = admitted;
            changed++;
        }
        if (initial) context.AutopilotBaselines.Add(new() { UserHash = account, BaselineAt = position.At });
        await context.SaveChangesAsync(token);
        if (changed > 0 || initial) await rules.QueueCatchUpAsync(token);
        await transaction.CommitAsync(token);
        return changed + (initial ? 1 : 0);
    }
}

public sealed class WantedAutopilotRoutine(WantedAutopilot autopilot) : IRoutine
{
    public const string RoutineName = "Wanted autopilot";
    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);
    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var changed = await autopilot.ReconcileAsync(cancellationToken);
        return changed == 0 ? RunResult.NothingToDo : RunResult.Handled(changed);
    }
}
