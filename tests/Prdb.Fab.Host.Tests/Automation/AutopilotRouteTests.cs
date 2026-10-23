using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Xunit;

namespace Prdb.Fab.Host.Tests.Automation;

public sealed class AutopilotRouteTests
{
    [Fact]
    public async Task Adoption_is_explicit_and_replayed_setup_preserves_an_opt_out()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = new FabApplication();
        using var client = await app.SignedInClientAsync();
        var state = await client.GetFromJsonAsync<AutopilotState>("/api/settings/automation/autopilot", token);
        Assert.False(state!.Present);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var library = Path.Combine(app.DataDirectory, "library");
            Directory.CreateDirectory(library);
            await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.LibraryRoot, library), token);
            context.AutomationRules.Add(new() { Id = Guid.NewGuid(), Name = "Existing rule", Enabled = false });
            await context.SaveChangesAsync(token);
        }
        using var start = await client.PostAsJsonAsync("/api/settings/automation/autopilot/start", new { enabled = false, directories = Array.Empty<string>() }, token);
        Assert.True((await start.Content.ReadFromJsonAsync<CollectionAct>(token))!.Accepted);
        using var replay = await client.PostAsJsonAsync("/api/settings/automation/autopilot/start", new { enabled = true, directories = Array.Empty<string>() }, token);
        Assert.True((await replay.Content.ReadFromJsonAsync<CollectionAct>(token))!.Accepted);
        state = await client.GetFromJsonAsync<AutopilotState>("/api/settings/automation/autopilot", token);
        Assert.True(state!.Present);
        Assert.False(state.Enabled);
        await using var read = app.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal(2, await db.AutomationRules.CountAsync(token));
        Assert.False((await db.AutomationRules.SingleAsync(row => !row.SuppliedWanted, token)).Enabled);
        Assert.Single(await db.CollectionPreparations.ToListAsync(token));
        Assert.Empty(await db.Downloads.ToListAsync(token));
    }
}
