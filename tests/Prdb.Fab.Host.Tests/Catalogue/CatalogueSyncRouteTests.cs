using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Prdb.Fab.Infrastructure.Persistence;
using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

public sealed class CatalogueSyncRouteTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("wanted", new[] { "prdb.wanted-videos" })]
    [InlineData("actors", new[] { "prdb.actors.drain", "prdb.favourite-actors" })]
    [InlineData("sites", new[] { "prdb.sites.changes", "prdb.favourite-sites", "prdb.ignored-sites" })]
    public async Task A_sync_schedules_only_the_relevant_feeds_and_duplicate_clicks_do_not_add_work(string surface, string[] names)
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var app = new FabApplication().WithTime(time);
        using var client = await app.SignedInClientAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var later = time.GetUtcNow().AddHours(12);
        await context.Routines.ExecuteUpdateAsync(u => u.SetProperty(row => row.DueAt, later), Token);
        var before = await context.Routines.CountAsync(Token);

        using var read = await client.GetAsync("/api/catalogue/sync", Token);
        read.EnsureSuccessStatusCode();
        Assert.False(await context.Routines.AnyAsync(row => row.RunNowPending, Token));
        using var first = await client.PostAsJsonAsync($"/api/catalogue/{surface}/sync", new { }, Token);
        first.EnsureSuccessStatusCode();
        var pending = await context.Routines.Where(row => row.RunNowPending).Select(row => row.Name).ToArrayAsync(Token);
        Assert.Equal(names.Order(), pending.Order());
        using var second = await client.PostAsJsonAsync($"/api/catalogue/{surface}/sync", new { }, Token);
        second.EnsureSuccessStatusCode();
        Assert.Equal(before, await context.Routines.CountAsync(Token));
        Assert.Equal(names.Length, await context.Routines.CountAsync(row => row.RunNowPending, Token));
        Assert.All(await context.Routines.Where(row => !row.RunNowPending).ToArrayAsync(Token), row => Assert.Equal(later, row.DueAt));
        using var unknown = await client.PostAsJsonAsync("/api/catalogue/unknown/sync", new { }, Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Sync_reads_and_requests_require_sign_in()
    {
        await using var app = new FabApplication();
        using var client = app.CreateClient();
        using var read = await client.GetAsync("/api/catalogue/sync", Token);
        using var request = await client.PostAsJsonAsync("/api/catalogue/wanted/sync", new { }, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, request.StatusCode);
    }
}
