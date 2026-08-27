using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// ADR 0020's Connections routes: the same forms and the same checks, wrapped
/// in <em>save</em> instead of <em>continue</em>.
/// </summary>
/// <remarks>
/// What is new here is the two rules that only a second visit can have: a key
/// is write-only, so an empty field means the one that is stored; and an
/// indexer that is already a row is corrected rather than added again.
/// </remarks>
public sealed class ConnectionSettingsRouteTests : IDisposable
{
    private const string AKey = "0123456789abcdef0123456789abcdef";

    private const string SabnzbdAddress = "http://sabnzbd.invalid:8080";

    private const string IndexerAddress = "https://indexer.invalid/api";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "prdb-fab-host-tests", Guid.NewGuid().ToString("n"));

    public ConnectionSettingsRouteTests() => Directory.CreateDirectory(directory);

    /// <summary>
    /// ADR 0020: nothing is ever returned to the browser, so the field is empty
    /// with a marker saying one is set — and saving it empty means unchanged.
    /// It still re-runs the check, because that is what saving a connection is.
    /// </summary>
    [Fact]
    public async Task An_empty_prdb_key_keeps_the_stored_one_and_checks_it_again()
    {
        var prdb = new FakePrdb();
        await using var application = new FabApplication().Answering(FabTransports.Prdb, prdb);
        var client = await application.SignedInClientAsync();

        Assert.Equal("Saved", (await SavePrdbAsync(client, AKey)).Outcome);

        var again = await SavePrdbAsync(client, string.Empty);

        Assert.Equal("Saved", again.Outcome);
        Assert.Equal(AKey, prdb.LastKey);
        Assert.Equal(AKey, await StoredPrdbKeyAsync(application));
    }

    /// <summary>
    /// ADR 0020: nothing saves past a failure, and a Gap is not raised while the
    /// user is standing in front of the form. So the stored key is the one that
    /// was working a moment ago.
    /// </summary>
    [Fact]
    public async Task A_prdb_key_that_fails_its_check_leaves_the_stored_one_alone()
    {
        var prdb = new FakePrdb();
        await using var application = new FabApplication().Answering(FabTransports.Prdb, prdb);
        var client = await application.SignedInClientAsync();

        await SavePrdbAsync(client, AKey);

        prdb.Answers = HttpStatusCode.Unauthorized;

        Assert.Equal("WrongKey", (await SavePrdbAsync(client, "fedcba9876543210fedcba9876543210")).Outcome);
        Assert.Equal(AKey, await StoredPrdbKeyAsync(application));
    }

    [Fact]
    public async Task An_empty_sabnzbd_key_keeps_the_stored_one()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Sabnzbd, new FakeSabnzbd());

        var client = await application.SignedInClientAsync();

        Assert.Equal("Saved", (await SaveSabnzbdAsync(client, FakeSabnzbd.RightKey, "xxx")).Outcome);

        // The list is a read that carries the key, so it is the first thing an
        // empty field has to work for.
        using var listed = await client.PostAsJsonAsync(
            "/api/connections/sabnzbd/categories",
            new { url = SabnzbdAddress, apiKey = string.Empty },
            TestContext.Current.CancellationToken);

        listed.EnsureSuccessStatusCode();

        Assert.Equal(
            "Saved",
            (await listed.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!.Outcome);

        var again = await SaveSabnzbdAsync(client, string.Empty, "archive");

        Assert.Equal("Saved", again.Outcome);

        var state = await StateAsync(client);

        Assert.True(state.SabnzbdConfigured);
        Assert.Equal("archive", state.SabnzbdCategory);
    }

    [Fact]
    public async Task A_sabnzbd_key_that_fails_its_check_changes_nothing()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Sabnzbd, new FakeSabnzbd());

        var client = await application.SignedInClientAsync();

        await SaveSabnzbdAsync(client, FakeSabnzbd.RightKey, "xxx");

        Assert.Equal("WrongKey", (await SaveSabnzbdAsync(client, "not the key", "archive")).Outcome);

        var state = await StateAsync(client);

        Assert.Equal("xxx", state.SabnzbdCategory);
    }

    /// <summary>
    /// ADR 0020's indexer route. The check is the same one that added it, which
    /// is the point of there being one form.
    /// </summary>
    [Fact]
    public async Task An_indexer_is_corrected_through_its_own_route()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Indexers, new FakeIndexer());

        var client = await application.SignedInClientAsync();

        await AddIndexerAsync(client, "First name", IndexerAddress, FakeIndexer.RightKey);

        var indexer = (await ListAsync(client))[0];

        var verdict = await EditAsync(client, indexer.Id, "Another name", IndexerAddress, string.Empty);

        Assert.Equal("Saved", verdict.Outcome);

        var corrected = (await ListAsync(client))[0];

        Assert.Equal(indexer.Id, corrected.Id);
        Assert.Equal("Another name", corrected.Name);

        // The key came back empty and stayed what it was, which the next check
        // is what proves: a stored key that had been cleared would be refused.
        Assert.Equal("Saved", (await EditAsync(client, indexer.Id, "Another name", IndexerAddress, string.Empty)).Outcome);
    }

    [Fact]
    public async Task An_indexer_edit_that_fails_its_check_leaves_the_row_alone()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Indexers, new FakeIndexer());

        var client = await application.SignedInClientAsync();

        await AddIndexerAsync(client, "First name", IndexerAddress, FakeIndexer.RightKey);

        var indexer = (await ListAsync(client))[0];

        var verdict = await EditAsync(client, indexer.Id, "Another name", IndexerAddress, "not the key");

        Assert.Equal("WrongKey", verdict.Outcome);

        var untouched = (await ListAsync(client))[0];

        Assert.Equal("First name", untouched.Name);
    }

    /// <summary>
    /// ADR 0002 identifies a release by the indexer together with that indexer's
    /// own id for it, so two rows for one address is still refused — and a row
    /// keeping its own address is not two rows.
    /// </summary>
    [Fact]
    public async Task An_address_that_is_another_rows_is_refused_and_its_own_is_not()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Indexers, new FakeIndexer());

        var client = await application.SignedInClientAsync();

        await AddIndexerAsync(client, "One", IndexerAddress, FakeIndexer.RightKey);
        await AddIndexerAsync(client, "Two", "https://other.invalid/api", FakeIndexer.RightKey);

        var rows = await ListAsync(client);
        var one = rows.Single(row => row.Name == "One");

        Assert.Equal(
            "AlreadyAdded",
            (await EditAsync(client, one.Id, "One", "https://other.invalid/api", string.Empty)).Outcome);

        Assert.Equal("Saved", (await EditAsync(client, one.Id, "One", IndexerAddress, string.Empty)).Outcome);
    }

    /// <summary>
    /// An id that is not there is the request being wrong rather than a verdict,
    /// which ADR 0040 reserves the status code for.
    /// </summary>
    [Fact]
    public async Task An_indexer_that_is_not_there_is_a_404()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Indexers, new FakeIndexer());

        var client = await application.SignedInClientAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/connections/indexers/{Guid.NewGuid()}",
            new { name = "Nothing", url = IndexerAddress, apiKey = FakeIndexer.RightKey },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_who_is_not_signed_in_may_correct_an_indexer()
    {
        await using var application = new FabApplication()
            .Answering(FabTransports.Indexers, new FakeIndexer());

        _ = await application.SignedInClientAsync();

        using var refused = await application.CreateClient().PostAsJsonAsync(
            $"/api/connections/indexers/{Guid.NewGuid()}",
            new { name = "Nothing", url = IndexerAddress, apiKey = FakeIndexer.RightKey },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static async Task<Verdict> SavePrdbAsync(HttpClient client, string apiKey)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/prdb",
            new { apiKey, confirmAnotherAccount = false },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private async Task<Verdict> SaveSabnzbdAsync(HttpClient client, string apiKey, string category)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/sabnzbd",
            new { url = SabnzbdAddress, apiKey, category, downloadDirectory = directory },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task AddIndexerAsync(HttpClient client, string name, string url, string apiKey)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/indexers",
            new { name, url, apiKey },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<Verdict> EditAsync(
        HttpClient client,
        Guid id,
        string name,
        string url,
        string apiKey)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/connections/indexers/{id}",
            new { name, url, apiKey },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<Row>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Row>>(
            "/api/connections/indexers", TestContext.Current.CancellationToken))!;

    private static async Task<State> StateAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<State>("/api/connections", TestContext.Current.CancellationToken))!;

    private static async Task<string?> StoredPrdbKeyAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();

        return (await scope.ServiceProvider
            .GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken)).PrdbApiKey;
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

    private sealed record Verdict(string Outcome, string Detail);

    private sealed record Row(Guid Id, string Name, string Url, string Categories);

    private sealed record State(bool SabnzbdConfigured, string? SabnzbdCategory);
}
