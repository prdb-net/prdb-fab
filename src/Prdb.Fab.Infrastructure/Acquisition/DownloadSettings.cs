using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Acquisition;

public sealed record DownloadSettingsState(PreferredDownloadQuality PreferredQuality);

/// <summary>The person's global choice for one-click Catalogue Downloads.</summary>
public sealed class DownloadSettings(FabDbContext context)
{
    public async Task<DownloadSettingsState> ReadAsync(CancellationToken cancellationToken = default) =>
        new(await context.Installation
            .Select(row => row.PreferredDownloadQuality)
            .SingleAsync(cancellationToken));

    public async Task<DownloadSettingsState> SaveAsync(
        PreferredDownloadQuality preferredQuality,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.AsTracking().SingleAsync(cancellationToken);
        installation.PreferredDownloadQuality = preferredQuality;
        await context.SaveChangesAsync(cancellationToken);
        return new(preferredQuality);
    }
}
