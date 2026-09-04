using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Acquisition;

public sealed class DownloadSettingsRouteTests
{
    [Fact]
    public async Task The_preferred_quality_defaults_to_2160p_and_is_persisted()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var initial = await client.GetFromJsonAsync<State>(
            "/api/settings/downloads",
            TestContext.Current.CancellationToken);
        Assert.Equal("P2160", initial!.PreferredQuality);

        using var saved = await client.PostAsJsonAsync(
            "/api/settings/downloads",
            new { preferredQuality = "P1080" },
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();
        Assert.Equal(
            "P1080",
            (await saved.Content.ReadFromJsonAsync<State>(TestContext.Current.CancellationToken))!.PreferredQuality);

        var reread = await client.GetFromJsonAsync<State>(
            "/api/settings/downloads",
            TestContext.Current.CancellationToken);
        Assert.Equal("P1080", reread!.PreferredQuality);
    }

    private sealed record State(string PreferredQuality);
}
