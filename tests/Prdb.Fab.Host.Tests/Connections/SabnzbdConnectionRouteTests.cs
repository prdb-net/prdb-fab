using System.Net.Http.Json;

using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// ADR 0010's skippable downloader step, end to end: a check that actually
/// carries the key, a category taken from SABnzbd's own list, and a path
/// mapping that is verified rather than collected.
/// </summary>
public sealed class SabnzbdConnectionRouteTests : IDisposable
{
    private const string Address = "http://sabnzbd.invalid:8080";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "prdb-fab-host-tests", Guid.NewGuid().ToString("n"));

    public SabnzbdConnectionRouteTests() => Directory.CreateDirectory(directory);

    /// <summary>
    /// The reason ADR 0010 spells out which call the check has to be. This
    /// SABnzbd answers <c>version</c> and <c>auth</c> to anybody, exactly as the
    /// real one does — so a check built on either would confirm this key.
    /// </summary>
    [Fact]
    public async Task A_wrong_key_is_rejected_by_a_service_that_answers_without_one()
    {
        var sabnzbd = new FakeSabnzbd();
        await using var application = Answering(sabnzbd);
        var client = await application.SignedInClientAsync();

        var verdict = await CategoriesAsync(client, "not the key");

        Assert.Equal("WrongKey", verdict.Outcome);
        Assert.Empty(verdict.Categories);

        // And the check did not go anywhere near the two that would have said yes.
        Assert.DoesNotContain("version", sabnzbd.Modes);
        Assert.DoesNotContain("auth", sabnzbd.Modes);
    }

    /// <summary>
    /// The category is chosen from SABnzbd's own list and never typed, because a
    /// category SABnzbd does not know is not an error there: it quietly becomes
    /// Default and the downloads land where nothing is looking.
    /// </summary>
    [Fact]
    public async Task The_categories_are_sabnzbds_own_each_with_where_it_finishes()
    {
        await using var application = Answering(new FakeSabnzbd());
        var client = await application.SignedInClientAsync();

        var verdict = await CategoriesAsync(client, FakeSabnzbd.RightKey);

        Assert.Equal("Saved", verdict.Outcome);
        Assert.Equal(["*", "xxx", "archive"], verdict.Categories.Select(category => category.Name));

        // A category with no folder of its own, and one with a relative folder,
        // both finish under the completed folder — SABnzbd creates the
        // subfolder when the first download for the category finishes.
        Assert.Equal(FakeSabnzbd.CompletedFolder, Root(verdict, "*"));
        Assert.Equal(FakeSabnzbd.CompletedFolder, Root(verdict, "xxx"));

        // An absolute one replaces it entirely, which is why the category has
        // to be answered before the mapping can be.
        Assert.Equal("/mnt/tank/archive", Root(verdict, "archive"));
    }

    [Fact]
    public async Task A_verified_mapping_is_stored_whole()
    {
        await using var application = Answering(new FakeSabnzbd());
        var client = await application.SignedInClientAsync();

        var verdict = await SaveAsync(client, "xxx", directory);

        Assert.Equal("Saved", verdict.Outcome);
        Assert.Equal(FakeSabnzbd.CompletedFolder, verdict.CompletedRoot);

        var state = await StateAsync(client);

        Assert.True(state.SabnzbdConfigured);
        Assert.Equal("xxx", state.SabnzbdCategory);
        Assert.Equal(FakeSabnzbd.CompletedFolder, state.CompletedRoot);

        // ADR 0010 asks no separate question for the download directory: it is
        // this, derived from the mapping that was just verified.
        Assert.Equal(directory, state.DownloadDirectory);
    }

    /// <summary>
    /// ADR 0010's order within the form. The category decides which folder is
    /// being resolved, so a category SABnzbd has stopped having is answered
    /// before the mapping is looked at — even when the mapping is wrong too.
    /// </summary>
    [Fact]
    public async Task The_category_is_answered_before_the_mapping()
    {
        await using var application = Answering(new FakeSabnzbd());
        var client = await application.SignedInClientAsync();

        var verdict = await SaveAsync(client, "gone", Path.Combine(directory, "not-mounted"));

        Assert.Equal("UnknownCategory", verdict.Outcome);
    }

    [Fact]
    public async Task A_mapping_that_resolves_to_nothing_here_is_refused()
    {
        await using var application = Answering(new FakeSabnzbd());
        var client = await application.SignedInClientAsync();

        var verdict = await SaveAsync(client, "xxx", Path.Combine(directory, "not-mounted"));

        Assert.Equal("DownloadDirectoryMissing", verdict.Outcome);

        // Nothing is stored behind a failure, so nothing is left half written.
        Assert.False((await StateAsync(client)).SabnzbdConfigured);
    }

    /// <summary>
    /// The step is skippable, and this is what skipping costs: nothing. Ticket
    /// 09 builds the act; what this holds is that not taking it leaves no
    /// half-written connection behind.
    /// </summary>
    [Fact]
    public async Task A_step_nobody_took_leaves_nothing_behind()
    {
        await using var application = Answering(new FakeSabnzbd());
        var client = await application.SignedInClientAsync();

        var state = await StateAsync(client);

        Assert.False(state.SabnzbdConfigured);
        Assert.Null(state.SabnzbdUrl);
        Assert.Null(state.SabnzbdCategory);
        Assert.Null(state.CompletedRoot);
        Assert.Null(state.DownloadDirectory);
    }

    /// <summary>
    /// A 403 that is about the network rather than about the key. SABnzbd checks
    /// where a request came from before it looks at a key at all, and the two
    /// have different fixes.
    /// </summary>
    [Fact]
    public async Task A_refusal_about_the_network_is_not_a_refusal_about_the_key()
    {
        await using var application = Answering(new FakeSabnzbd { RefusesThisNetwork = true });
        var client = await application.SignedInClientAsync();

        Assert.Equal("AccessDenied", (await CategoriesAsync(client, FakeSabnzbd.RightKey)).Outcome);
    }

    [Fact]
    public async Task Nothing_answering_is_not_a_wrong_key()
    {
        await using var application = Answering(new FakeSabnzbd
        {
            Throws = new HttpRequestException("Connection refused"),
        });

        var client = await application.SignedInClientAsync();

        Assert.Equal("NotRightNow", (await CategoriesAsync(client, FakeSabnzbd.RightKey)).Outcome);
    }

    [Fact]
    public async Task Something_that_is_not_sabnzbd_says_so()
    {
        await using var application = Answering(new NotSabnzbd());
        var client = await application.SignedInClientAsync();

        Assert.Equal("NotSabnzbd", (await CategoriesAsync(client, FakeSabnzbd.RightKey)).Outcome);
    }

    private static FabApplication Answering(HttpMessageHandler sabnzbd) =>
        new FabApplication().Answering(FabTransports.Sabnzbd, sabnzbd);

    private static string? Root(CategoriesVerdict verdict, string name) =>
        verdict.Categories.Single(category => category.Name == name).CompletedRoot;

    private static async Task<CategoriesVerdict> CategoriesAsync(HttpClient client, string apiKey)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/sabnzbd/categories",
            new { url = Address, apiKey },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CategoriesVerdict>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<SaveVerdict> SaveAsync(HttpClient client, string category, string downloadDirectory)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/sabnzbd",
            new { url = Address, apiKey = FakeSabnzbd.RightKey, category, downloadDirectory },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SaveVerdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<State> StateAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<State>("/api/connections", TestContext.Current.CancellationToken))!;

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

    /// <summary>Something else answering at that address, with a page.</summary>
    private sealed class NotSabnzbd : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><body>It works!</body></html>",
                    System.Text.Encoding.UTF8,
                    "text/html"),
            });
    }

    private sealed record Category(string Name, string CompletedRoot);

    private sealed record CategoriesVerdict(string Outcome, string Detail, IReadOnlyList<Category> Categories);

    private sealed record SaveVerdict(string Outcome, string Detail, string? CompletedRoot);

    private sealed record State(
        bool SabnzbdConfigured,
        string? SabnzbdUrl,
        string? SabnzbdCategory,
        string? CompletedRoot,
        string? DownloadDirectory);
}
