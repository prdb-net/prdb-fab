using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

public sealed class LibrarySettings(FabDbContext context)
{
    public Task<LibrarySettingsState> ReadAsync(CancellationToken cancellationToken = default) =>
        context.Installation
            .AsNoTracking()
            .Select(row => new LibrarySettingsState(row.LibraryRoot, row.DeleteLeftovers))
            .SingleAsync(cancellationToken);

    public async Task<LibrarySettingsState> SaveAsync(
        bool deleteLeftovers,
        CancellationToken cancellationToken = default)
    {
        await context.Installation.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.DeleteLeftovers, deleteLeftovers),
            cancellationToken);

        return await ReadAsync(cancellationToken);
    }
}

public sealed record LibrarySettingsState(string? LibraryRoot, bool DeleteLeftovers);
