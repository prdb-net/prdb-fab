using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Filing;

/// <summary>ADR 0055: the Library grid's order is chosen and linkable.</summary>
public sealed class LibraryGridRouteTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_default_is_the_most_recently_filed_entry_first()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        await SeedAsync(application);

        var page = await ReadAsync(client, string.Empty);

        Assert.Equal(["Bravo", "Charlie", "Alpha"], page.Entries.Select(entry => entry.Title));
    }

    [Theory]
    [InlineData("sort=FiledAtDescending", "Bravo,Charlie,Alpha")]
    [InlineData("sort=FiledAtAscending", "Alpha,Charlie,Bravo")]
    [InlineData("sort=TitleAscending", "Alpha,Bravo,Charlie")]
    [InlineData("sort=TitleDescending", "Charlie,Bravo,Alpha")]
    public async Task Every_order_is_reachable_from_the_address(string query, string expected)
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        await SeedAsync(application);

        var page = await ReadAsync(client, query);

        Assert.Equal(expected.Split(','), page.Entries.Select(entry => entry.Title));
    }

    [Fact]
    public async Task An_order_survives_a_filter_beside_it()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        await SeedAsync(application);

        var page = await ReadAsync(client, "search=a&sort=TitleAscending");

        Assert.Equal(["Alpha", "Bravo", "Charlie"], page.Entries.Select(entry => entry.Title));
    }

    private static async Task<Page> ReadAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<Page>(
            "/api/library" + (query.Length > 0 ? $"?{query}" : string.Empty),
            TestContext.Current.CancellationToken))!;

    private static async Task SeedAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        // Filed in an order no title order reproduces, so that each assertion
        // can only pass for the order it names.
        Hold(context, "Alpha", Noon);
        Hold(context, "Charlie", Noon.AddHours(1));
        Hold(context, "Bravo", Noon.AddHours(2));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static void Hold(FabDbContext context, string title, DateTimeOffset filedAt)
    {
        var video = Guid.NewGuid();
        context.CatalogueVideos.Add(new CatalogueVideoRow
        {
            PrdbId = video,
            Title = title,
            NormalisedTitle = title.ToLowerInvariant(),
            CreatedAtUtc = Noon,
            UpdatedAtUtc = Noon,
        });
        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = video,
            EntryDirectory = "/library/" + title,
            FiledAt = filedAt,
        });
    }

    private sealed record Card(string Title);
    private sealed record Page(IReadOnlyList<Card> Entries);
}
