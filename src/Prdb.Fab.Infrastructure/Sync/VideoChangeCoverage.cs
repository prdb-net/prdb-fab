using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Freshness proof belongs to completed baseline and post-apply feed progress.</summary>
public sealed class VideoChangeCoverage(FabDbContext context, TimeProvider time)
{
    public async Task<bool> FreshAsync(CancellationToken token)
    {
        var state = await context.VideoChangeState.SingleOrDefaultAsync(token);
        var cursor = FeedPosition.Read(await context.FeedCursors.Where(c => c.Feed == Feed.VideoChanges).Select(c => c.Cursor).SingleOrDefaultAsync(token));
        return cursor is not null && cursor.At >= state?.LowerBoundAt
            && state?.BaselineCompletedAt is not null
            && state.CaughtUpObservedAt >= time.GetUtcNow() - RecentWindow.RevalidateAfter;
    }

    public async Task StartAsync(DateTimeOffset bound, CancellationToken token)
    {
        var state = await context.VideoChangeState.AsTracking().SingleOrDefaultAsync(token);
        if (state is null)
        {
            state = new VideoChangeStateRow();
            context.VideoChangeState.Add(state);
        }
        state.LowerBoundAt = bound;
        state.BaselineCompletedAt = null;
        state.CaughtUpAt = null;
        state.CaughtUpObservedAt = null;
        // Reset each row's proof even if the restarted server supplied the same bound.
        await context.CatalogueVideos.ExecuteUpdateAsync(u => u.SetProperty(v => v.ChangesBaselineAt, (DateTimeOffset?)null), token);
        await context.SaveChangesAsync(token);
    }

    public async Task CaughtUpAsync(DateTimeOffset bound, CancellationToken token)
    {
        var state = await context.VideoChangeState.AsTracking().SingleAsync(token);
        state.CaughtUpAt = bound;
        state.CaughtUpObservedAt = time.GetUtcNow();
        await context.SaveChangesAsync(token);
    }

    public async Task CompleteBaselineAsync(CancellationToken token)
    {
        var state = await context.VideoChangeState.AsTracking().SingleOrDefaultAsync(token);
        if (state is null || state.BaselineCompletedAt is not null) return;
        state.BaselineCompletedAt = time.GetUtcNow();
        await context.SaveChangesAsync(token);
    }
}
