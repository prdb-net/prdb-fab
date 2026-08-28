using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Filing;

public sealed class IdentificationSettingsRouteTests
{
    [Fact]
    public async Task The_route_exposes_only_the_active_after_download_gate()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var initial = await client.GetFromJsonAsync<State>(
            "/api/settings/identification",
            TestContext.Current.CancellationToken);
        Assert.Equal("ExactAndStrong", initial!.AfterDownload);

        using var saved = await client.PostAsJsonAsync(
            "/api/settings/identification",
            new { afterDownload = "ExactOnly" },
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();
        var verdict = await saved.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken);
        Assert.Equal("ExactOnly", verdict!.AfterDownload);
        Assert.Equal(0, verdict.Reconsidered);

        var after = await client.GetFromJsonAsync<State>(
            "/api/settings/identification",
            TestContext.Current.CancellationToken);
        Assert.Equal("ExactOnly", after!.AfterDownload);
    }

    private sealed record State(string AfterDownload);
    private sealed record Verdict(string AfterDownload, int Reconsidered);
}
