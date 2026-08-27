using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// What <c>dotnet ef migrations add</c> uses. It never opens the file it names —
/// a migration is generated from the model, not from a database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FabDbContext>
{
    public FabDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FabDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new FabDbContext(options);
    }
}
