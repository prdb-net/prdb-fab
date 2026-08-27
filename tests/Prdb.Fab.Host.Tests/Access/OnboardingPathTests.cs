using System.Net;
using System.Net.Http.Json;

using Prdb.Fab.Host.Tests.Connections;
using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Host.Tests.Access;

/// <summary>
/// ADR 0010's path, walked: from a container that has just started for the
/// first time to an installation that is ready, with the two skippable steps
/// taken both ways.
/// </summary>
public sealed class OnboardingPathTests : IDisposable
{
    private const string AKey = "0123456789abcdef0123456789abcdef";

    private const string SabnzbdAddress = "http://sabnzbd.invalid:8080";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "prdb-fab-host-tests", Guid.NewGuid().ToString("n"));

    public OnboardingPathTests() => Directory.CreateDirectory(directory);

    /// <summary>
    /// Everything answered. The order is ADR 0010's, and each step is only
    /// reachable because the one before it was taken.
    /// </summary>
    [Fact]
    public async Task Every_step_answered_ends_on_a_ready_installation()
    {
        await using var application = Fresh();
        var client = await application.SignedInClientAsync();

        Assert.Equal("PrdbKey", await NextStepAsync(client));

        await SavePrdbKeyAsync(client);
        Assert.Equal("Sabnzbd", (await TakeAsync(client, "PrdbKey")).NextStep);

        await SaveSabnzbdAsync(client);
        Assert.Equal("Indexers", (await TakeAsync(client, "Sabnzbd")).NextStep);

        await AddIndexerAsync(client);
        Assert.Equal("LibraryRoot", (await TakeAsync(client, "Indexers")).NextStep);

        await SaveLibraryRootAsync(client);
        Assert.Equal("Complete", (await TakeAsync(client, "LibraryRoot")).NextStep);

        var connections = await ConnectionsAsync(client);

        Assert.True(connections.SabnzbdConfigured);
        Assert.False(connections.SabnzbdSkipped);
        Assert.False(connections.IndexersSkipped);
    }

    /// <summary>
    /// ADR 0010: a tool that cannot download is still a tool that holds a
    /// library. Both skippable steps skipped, and the installation is usable.
    /// </summary>
    [Fact]
    public async Task Skipping_both_skippable_steps_still_completes()
    {
        await using var application = Fresh();
        var client = await application.SignedInClientAsync();

        await SavePrdbKeyAsync(client);
        await TakeAsync(client, "PrdbKey");

        Assert.Equal("Skipped", (await SkipAsync(client, "Sabnzbd")).Outcome);
        Assert.Equal("Skipped", (await SkipAsync(client, "Indexers")).Outcome);

        await SaveLibraryRootAsync(client);
        await TakeAsync(client, "LibraryRoot");

        Assert.Equal("Complete", await NextStepAsync(client));

        // The Gaps, on the connections that are missing. Nothing displays them
        // yet — ADR 0018's Status page is its own slice, and reads these.
        var connections = await ConnectionsAsync(client);

        Assert.True(connections.SabnzbdSkipped);
        Assert.True(connections.IndexersSkipped);
        Assert.False(connections.SabnzbdConfigured);
        Assert.Equal(0, connections.IndexerCount);
    }

    /// <summary>
    /// ADR 0010: each step commits when it completes, so a closed tab — or a
    /// container that was killed — costs nothing and resumes where it was.
    /// </summary>
    [Fact]
    public async Task A_container_killed_mid_path_comes_back_on_the_same_step()
    {
        var original = Fresh();
        FabApplication? restarted = null;

        try
        {
            var client = await original.SignedInClientAsync();

            await SavePrdbKeyAsync(client);
            await TakeAsync(client, "PrdbKey");
            Assert.Equal("Skipped", (await SkipAsync(client, "Sabnzbd")).Outcome);

            restarted = original.RestartWith("FAB_RESET_PASSWORD", "false");
            original.Dispose();

            var afterwards = await restarted.SignedInClientAsync();

            Assert.Equal("Indexers", await NextStepAsync(afterwards));

            // And what was answered before the restart is still answered.
            var connections = await ConnectionsAsync(afterwards);

            Assert.True(connections.PrdbConfigured);
            Assert.True(connections.SabnzbdSkipped);
        }
        finally
        {
            original.Dispose();
            restarted?.Dispose();
        }
    }

    /// <summary>
    /// ADR 0010: the loop stands still until the mandatory steps are done. A
    /// browser that asks anyway is answered rather than obeyed.
    /// </summary>
    [Fact]
    public async Task A_mandatory_step_can_be_neither_walked_past_nor_skipped()
    {
        await using var application = Fresh();
        var client = await application.SignedInClientAsync();

        Assert.Equal("NotConfigured", (await TakeAsync(client, "PrdbKey")).Outcome);
        Assert.Equal("NotSkippable", (await SkipAsync(client, "PrdbKey")).Outcome);
        Assert.Equal("PrdbKey", await NextStepAsync(client));
    }

    /// <summary>
    /// A second window, or one left open while the path moved on somewhere
    /// else. It is told where the path is rather than moving it from where it
    /// last looked.
    /// </summary>
    [Fact]
    public async Task A_window_that_is_a_step_behind_moves_nothing()
    {
        await using var application = Fresh();
        var client = await application.SignedInClientAsync();

        await SavePrdbKeyAsync(client);
        await TakeAsync(client, "PrdbKey");

        var stale = await TakeAsync(client, "PrdbKey");

        Assert.Equal("NotTheCurrentStep", stale.Outcome);
        Assert.Equal("Sabnzbd", stale.NextStep);
    }

    /// <summary>
    /// ADR 0010: onboarding completes and does not return. What fills a Gap
    /// afterwards is the settings, and filling one closes it.
    /// </summary>
    [Fact]
    public async Task Configuring_a_skipped_connection_afterwards_closes_its_gap()
    {
        await using var application = Fresh();
        var client = await application.SignedInClientAsync();

        await SavePrdbKeyAsync(client);
        await TakeAsync(client, "PrdbKey");
        await SkipAsync(client, "Sabnzbd");
        await SkipAsync(client, "Indexers");
        await SaveLibraryRootAsync(client);
        await TakeAsync(client, "LibraryRoot");

        await SaveSabnzbdAsync(client);
        await AddIndexerAsync(client);

        var connections = await ConnectionsAsync(client);

        Assert.False(connections.SabnzbdSkipped);
        Assert.False(connections.IndexersSkipped);

        // And the wizard is still finished: filling a Gap is not re-entering it.
        Assert.Equal("Complete", await NextStepAsync(client));
    }

    [Fact]
    public async Task Nobody_who_is_not_signed_in_may_move_the_path()
    {
        await using var application = Fresh();
        _ = await application.SignedInClientAsync();

        var anonymous = application.CreateClient();

        foreach (var route in new[] { "/api/onboarding/take", "/api/onboarding/skip" })
        {
            using var refused = await anonymous.PostAsJsonAsync(
                route,
                new { step = "Sabnzbd" },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
    }

    private FabApplication Fresh() =>
        new FabApplication()
            .Answering(FabTransports.Prdb, new FakePrdb())
            .Answering(FabTransports.Sabnzbd, new FakeSabnzbd())
            .Answering(FabTransports.Indexers, new FakeIndexer());

    private static async Task<string?> NextStepAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AccessStateBody>(
            "/api/access/state", TestContext.Current.CancellationToken))!.NextStep;

    private static async Task<ConnectionsBody> ConnectionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ConnectionsBody>(
            "/api/connections", TestContext.Current.CancellationToken))!;

    private static Task<OnboardingVerdictBody> TakeAsync(HttpClient client, string step) =>
        MoveAsync(client, "take", step);

    private static Task<OnboardingVerdictBody> SkipAsync(HttpClient client, string step) =>
        MoveAsync(client, "skip", step);

    private static async Task<OnboardingVerdictBody> MoveAsync(HttpClient client, string act, string step)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/onboarding/{act}",
            new { step },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OnboardingVerdictBody>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task SavePrdbKeyAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/prdb",
            new { apiKey = AKey, confirmAnotherAccount = false },
            TestContext.Current.CancellationToken);

        await StoredAsync(response, "Saved");
    }

    /// <summary>
    /// The downloads and the library are siblings, because ADR 0010 refuses a
    /// library root that lies inside the download directory — filing moves
    /// videos out of there.
    /// </summary>
    private async Task SaveSabnzbdAsync(HttpClient client)
    {
        var downloads = Path.Combine(directory, "downloads");
        Directory.CreateDirectory(downloads);

        using var response = await client.PostAsJsonAsync(
            "/api/connections/sabnzbd",
            new
            {
                url = SabnzbdAddress,
                apiKey = FakeSabnzbd.RightKey,
                category = "xxx",
                downloadDirectory = downloads,
            },
            TestContext.Current.CancellationToken);

        await StoredAsync(response, "Saved");
    }

    private static async Task AddIndexerAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/indexers",
            new { name = "An indexer", url = "https://indexer.invalid/api", apiKey = FakeIndexer.RightKey },
            TestContext.Current.CancellationToken);

        await StoredAsync(response, "Saved");
    }

    private async Task SaveLibraryRootAsync(HttpClient client)
    {
        var library = Path.Combine(directory, "library");
        Directory.CreateDirectory(library);

        using var response = await client.PostAsJsonAsync(
            "/api/connections/library-root",
            new { path = library },
            TestContext.Current.CancellationToken);

        // ADR 0010 warns rather than refuses when the two are on different
        // filesystems, and a temporary directory can be either.
        await StoredAsync(response, "Saved", "SavedWithWarning");
    }

    /// <summary>
    /// A connection form's own verdict, checked before the path is asked to
    /// move past it — ADR 0040 makes a refusal a 200, so a step that silently
    /// stored nothing would otherwise show up as a marker that would not move.
    /// </summary>
    private static async Task StoredAsync(HttpResponseMessage response, params string[] expected)
    {
        response.EnsureSuccessStatusCode();

        var verdict = await response.Content.ReadFromJsonAsync<OutcomeBody>(
            TestContext.Current.CancellationToken);

        Assert.Contains(verdict!.Outcome, expected);
    }

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

    private sealed record OutcomeBody(string Outcome);

    private sealed record AccessStateBody(bool PasswordSet, bool SignedIn, string? NextStep);

    private sealed record OnboardingVerdictBody(string Outcome, string Detail, string NextStep);

    private sealed record ConnectionsBody(
        bool PrdbConfigured,
        bool SabnzbdConfigured,
        bool SabnzbdSkipped,
        int IndexerCount,
        bool IndexersSkipped,
        string? LibraryRoot);
}
