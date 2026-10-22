using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class AccountStartTests
{
    [Fact]
    public async Task First_key_makes_account_feeds_due_and_an_ordinary_save_leaves_their_schedule_alone()
    {
        var socket = new FakePrdbApi().Answers("/user-identity", "{\"userHash\":\"test-account\"}");
        await using var database = await TestDatabase.CreateAsync(prdb: socket, also: services => services.AddFabSync());
        await database.Services.PrepareFabScheduleAsync(TestContext.Current.CancellationToken);
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Routines.Where(r => r.Name == "prdb.wanted-videos")
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.DueAt, database.Time.GetUtcNow().AddHours(1)), TestContext.Current.CancellationToken);
        var connections = scope.ServiceProvider.GetRequiredService<PrdbConnections>();
        await connections.SaveAsync("test-key", false, TestContext.Current.CancellationToken);
        var first = await context.Routines.SingleAsync(r => r.Name == "prdb.wanted-videos", TestContext.Current.CancellationToken);
        Assert.Equal(database.Time.GetUtcNow(), first.DueAt);
        var later = database.Time.GetUtcNow().AddHours(1);
        await context.Routines.Where(r => r.Id == first.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.DueAt, later), TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        await connections.SaveAsync("test-key", false, TestContext.Current.CancellationToken);
        Assert.Equal(later, (await context.Routines.SingleAsync(r => r.Id == first.Id, TestContext.Current.CancellationToken)).DueAt);
    }
}
