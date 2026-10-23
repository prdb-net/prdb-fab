using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Infrastructure.Persistence;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Automation;

public sealed class CollectionModelTests
{
    [Fact]
    public async Task Upgrading_preserves_rules_without_starting_collection_preparation()
    {
        await using var database = await TestDatabase.CreateAsync(migratedTo: "IdentificationFreshness");
        var id = Guid.NewGuid();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO automation_rule (Id, Name, Enabled, MinimumSize, MaximumSize) VALUES ({id}, 'Existing permission', 0, NULL, NULL)",
                TestContext.Current.CancellationToken);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }
        await using var reading = database.Scope();
        var held = reading.ServiceProvider.GetRequiredService<FabDbContext>();
        var rule = await held.AutomationRules.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(id, rule.Id);
        Assert.False(rule.Enabled);
        Assert.False(rule.SuppliedWanted);
        Assert.Empty(await held.CollectionPreparations.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await held.AutopilotBaselines.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Backup_carries_approval_and_excluded_membership_but_not_enumeration_positions()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var root = Guid.NewGuid();
        var file = Guid.NewGuid();
        var now = database.Time.GetUtcNow();
        context.CollectionPreparations.Add(new() { StartedAt = now });
        context.CollectionRoots.Add(new() { Id = root, Path = "/existing" });
        context.CollectionWalks.Add(new() { Path = "/existing", RootId = root, AfterName = "first.mkv" });
        context.CollectionFiles.Add(new()
        {
            Id = file, RootId = root, SourcePath = "/existing/first.mkv", LastWriteAt = now,
            ApprovedAt = now, ApprovedEvidence = "the reviewed evidence", PlannedPath = "/library/first.mkv",
        });
        context.AutopilotBaselines.Add(new() { UserHash = "account", BaselineAt = now });
        context.AutopilotMemberships.Add(new()
        {
            UserHash = "account", VideoId = Guid.NewGuid(), WantedSinceAt = now, Admitted = false,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var document = await scope.ServiceProvider.GetRequiredService<Backups>()
            .ReadAsync(TestContext.Current.CancellationToken);
        var read = BackupReader.Read(BackupJson.Write(document)).Document!;
        Assert.Equal("Inventory", Assert.Single(read.CollectionPreparations!).Phase);
        Assert.Equal("the reviewed evidence", Assert.Single(read.CollectionFiles!).ApprovedEvidence);
        Assert.Equal(file, Assert.Single(read.CollectionFiles!).Id);
        Assert.False(Assert.Single(read.AutopilotMemberships!).Admitted);
        Assert.DoesNotContain("afterName", BackupJson.Write(document), StringComparison.Ordinal);
    }
}
