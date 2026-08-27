using System.Net.Http.Json;

using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// ADR 0010's second mandatory step, end to end: one path, three checks, and
/// two of the three refuse.
/// </summary>
public sealed class LibraryRootRouteTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "prdb-fab-host-tests", Guid.NewGuid().ToString("n"));

    public LibraryRootRouteTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task A_writable_directory_is_stored()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var library = Named("library");

        Assert.Equal("Saved", (await SubmitAsync(client, library)).Outcome);
        Assert.Equal(library, await StoredAsync(client));
    }

    [Fact]
    public async Task A_directory_that_is_not_mounted_here_is_refused()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var verdict = await SubmitAsync(client, Path.Combine(directory, "not-mounted"));

        Assert.Equal("Missing", verdict.Outcome);
        Assert.NotEmpty(verdict.Detail);
        Assert.Null(await StoredAsync(client));
    }

    [Fact]
    public async Task A_relative_path_is_refused() =>
        await WithSignedInClientAsync(async client =>
            Assert.Equal("NotAbsolute", (await SubmitAsync(client, "library")).Outcome));

    /// <summary>
    /// The overlap, in both directions, through the mapping SABnzbd's step
    /// verified — which is the only place a download directory comes from.
    /// </summary>
    [Theory]
    [InlineData("downloads", "downloads/library", "InsideDownloadDirectory")]
    [InlineData("library/downloads", "library", "ContainsDownloadDirectory")]
    public async Task An_overlap_with_the_downloads_is_refused(
        string downloads,
        string library,
        string expected)
    {
        await using var application = new FabApplication().Answering(FabTransports.Sabnzbd, new FakeSabnzbd());
        var client = await application.SignedInClientAsync();

        var downloadDirectory = Named(downloads);

        using (var mapped = await client.PostAsJsonAsync(
            "/api/connections/sabnzbd",
            new
            {
                url = "http://sabnzbd.invalid:8080",
                apiKey = FakeSabnzbd.RightKey,
                category = "xxx",
                downloadDirectory,
            },
            TestContext.Current.CancellationToken))
        {
            mapped.EnsureSuccessStatusCode();
        }

        var verdict = await SubmitAsync(client, Named(library));

        Assert.Equal(expected, verdict.Outcome);
        Assert.Null(await StoredAsync(client));
    }

    private string Named(string relative)
    {
        var path = Path.Combine(directory, relative);
        Directory.CreateDirectory(path);

        return path;
    }

    private static async Task WithSignedInClientAsync(Func<HttpClient, Task> body)
    {
        await using var application = new FabApplication();

        await body(await application.SignedInClientAsync());
    }

    private static async Task<Verdict> SubmitAsync(HttpClient client, string path)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/library-root",
            new { path },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<string?> StoredAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<State>("/api/connections", TestContext.Current.CancellationToken))!
            .LibraryRoot;

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private sealed record Verdict(string Outcome, string Detail);

    private sealed record State(string? LibraryRoot);
}
