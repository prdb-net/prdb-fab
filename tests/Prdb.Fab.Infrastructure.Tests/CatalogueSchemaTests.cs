using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// The catalogue half of ADR 0033's schema: what every table has to declare,
/// what may not be indexed, and that an installation from the release before it
/// arrives at it.
/// </summary>
public sealed class CatalogueSchemaTests
{
    /// <summary>
    /// The last migration 0.1.0 shipped. A data directory that has been through
    /// it is what an upgrading installation actually has on disk.
    /// </summary>
    private const string TheFirstRelease = "TheGapsASkipLeavesBehind";

    /// <summary>
    /// ADR 0033 makes the account cut a property each table declares, so that
    /// changing the prdb key is a list of deletes read off the schema rather
    /// than a procedure somebody keeps in step with new tables. This is what
    /// makes that true rather than intended: the model is walked, so a table
    /// added later fails here until it answers.
    /// </summary>
    [Fact]
    public void Every_table_declares_an_account_class()
    {
        foreach (var entity in TheModel().GetEntityTypes())
        {
            Assert.True(
                AccountClasses.DeclaredBy(entity) is not null,
                $"{entity.GetTableName()} does not say what becomes of it when the prdb key "
                + "belongs to a different account (ADR 0033).");
        }
    }

    /// <summary>
    /// And the one table that answers <see cref="AccountClass.PerRow"/> has to
    /// have somewhere the answer actually lives. <c>Feeds.AccountClassOf</c>
    /// throws over a feed it has not been told about, so this is the assertion
    /// that the per-row answer covers every row the table can hold.
    /// </summary>
    [Fact]
    public void A_table_classified_per_row_classifies_every_row_it_can_hold()
    {
        var perRow = TheModel().GetEntityTypes()
            .Where(entity => AccountClasses.DeclaredBy(entity) == AccountClass.PerRow)
            .Select(entity => entity.GetTableName())
            .ToArray();

        Assert.Equal(["feed_cursor"], perRow);

        foreach (var feed in Feeds.All)
        {
            Assert.Contains(feed.AccountClassOf(), (AccountClass[])[AccountClass.AccountFree, AccountClass.AccountScoped]);
        }
    }

    /// <summary>
    /// ADR 0025's measurement, restated in index terms: its query is
    /// <c>LIKE '%needle%'</c>, which no B-tree can serve, which is why an
    /// indexless pass beat a trigram index costing +119 % on the most
    /// continuously written table in the schema. The stored normalised columns
    /// exist so that one function writes both sides of that comparison — not so
    /// that something can be looked up by them.
    /// </summary>
    [Fact]
    public void No_normalised_column_is_indexed()
    {
        var indexed = TheModel().GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes())
            .SelectMany(index => index.Properties)
            .Where(property => property.Name.Contains("Normalised", StringComparison.Ordinal))
            .Select(property => $"{property.DeclaringType.GetTableName()}.{property.Name}")
            .ToArray();

        Assert.Empty(indexed);
    }

    /// <summary>
    /// The other half of ADR 0025's answer, and the one the model cannot state:
    /// an FTS5 table is a virtual table nothing in the model would mention.
    /// </summary>
    [Fact]
    public async Task The_database_carries_no_full_text_index()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using var connection = new SqliteConnection(database.Location.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT group_concat(name) FROM sqlite_master WHERE lower(coalesce(sql, '')) LIKE '%fts%';";

        var found = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.True(found is DBNull or null, $"the schema carries a full-text index: {found}");
    }

    /// <summary>
    /// The upgrade path, run the way a container runs it: an installation that
    /// has been through 0.1.0, migrated forward by the migrator that runs before
    /// the listener. What it keeps matters as much as what it adds — migrations
    /// only go forward, so an installation that arrives here without its
    /// answers has lost them for good.
    /// </summary>
    [Fact]
    public async Task The_schema_moves_forward_from_the_first_release()
    {
        await using var database = await TestDatabase.CreateAsync(migratedTo: TheFirstRelease);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            await context.Installation.ExecuteUpdateAsync(
                row => row.SetProperty(installation => installation.LibraryRoot, "/library"),
                TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>()
                .PrepareAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("/library", installation.LibraryRoot);

            // Reading each of the ten is what proves they are there: the query
            // is against the table, so a missing one is an error rather than a
            // zero.
            Assert.Equal(0, await context.CatalogueVideos.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.CatalogueVideoPreNames.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.CatalogueVideoActors.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.CatalogueSites.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.CatalogueActors.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.CatalogueImages.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.FeedCursors.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.WantedVideos.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.FavouriteSites.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.FavouriteActors.CountAsync(TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// The model as the application builds it. It opens nothing: a model is
    /// built from the code, and every question above is about the code.
    /// </summary>
    private static IModel TheModel()
    {
        var options = new DbContextOptionsBuilder<FabDbContext>()
            .UseSqlite("Data Source=schema-only.db")
            .Options;

        using var context = new FabDbContext(options);

        return context.Model;
    }
}
