using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Upstream disappearance is metadata, never an act on Library content.</summary>
public sealed class CatalogueAvailability(FabDbContext context, TimeProvider time)
{
    public async Task UnavailableAsync(Guid prdbId, DateTimeOffset? updatedAt, DateTimeOffset? deletedAt,
        Guid? mergedIntoId, CancellationToken token)
    {
        var row = await context.CatalogueVideos.AsTracking().SingleOrDefaultAsync(v => v.PrdbId == prdbId, token);
        if (row is null || (updatedAt is { } stamp && row.UpdatedAtUtc > stamp)) return;
        row.Available = false;
        row.LastReadAt = time.GetUtcNow();
        if (updatedAt is { } newer)
        {
            row.UpdatedAtUtc = newer;
            row.DeletedAtUtc = deletedAt ?? newer;
            row.MergedIntoId = mergedIntoId;
        }
        await context.SaveChangesAsync(token);
    }

    public static string? Notice(bool available, Guid? mergedIntoId) => available ? null
        : mergedIntoId is { } survivor
            ? $"prdb merged this Video into {survivor}. Local files and history remain under their original identity."
            : "This Video is unavailable at prdb. Local files and history are retained.";
}
