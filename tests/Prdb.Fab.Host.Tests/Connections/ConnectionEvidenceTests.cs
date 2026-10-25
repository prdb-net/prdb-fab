using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prdb.Fab.Host.Connections;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Core.Connections;
using Xunit;

namespace Prdb.Fab.Host.Tests.Connections;

public sealed class ConnectionEvidenceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string Key = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Failed_recheck_keeps_last_success_but_rejected_replacement_does_not_change_evidence()
    {
        var remote = new FakePrdb();
        await using var app = new FabApplication().Answering(FabTransports.Prdb, remote);
        var client = await app.SignedInClientAsync();
        var token = TestContext.Current.CancellationToken;
        await client.PostAsJsonAsync("/api/connections/prdb", new { apiKey = Key }, token);
        var first = (await client.GetFromJsonAsync<ConnectionsState>("/api/connections", Json, token))!;
        Assert.NotNull(first.PrdbLastSuccessAt);
        Assert.Equal(first.PrdbLastCheckedAt, first.PrdbLastSuccessAt);
        Assert.Equal(PrdbConnectionOutcome.Saved, first.PrdbLastCheckOutcome);

        remote.Answers = HttpStatusCode.Unauthorized;
        await client.PostAsJsonAsync("/api/connections/prdb", new { apiKey = "rejected-replacement" }, token);
        var unchanged = (await client.GetFromJsonAsync<ConnectionsState>("/api/connections", Json, token))!;
        Assert.Equal(first.PrdbLastCheckedAt, unchanged.PrdbLastCheckedAt);
        Assert.Equal(PrdbConnectionOutcome.Saved, unchanged.PrdbLastCheckOutcome);

        await client.PostAsJsonAsync("/api/connections/prdb", new { apiKey = "" }, token);
        var failed = (await client.GetFromJsonAsync<ConnectionsState>("/api/connections", Json, token))!;
        Assert.True(failed.PrdbConfigured);
        Assert.Equal(PrdbConnectionOutcome.WrongKey, failed.PrdbLastCheckOutcome);
        Assert.Equal(first.PrdbLastSuccessAt, failed.PrdbLastSuccessAt);
        Assert.True(failed.PrdbLastCheckedAt >= first.PrdbLastCheckedAt);
    }

    [Fact]
    public async Task Key_is_only_revealed_explicitly_to_a_signed_in_person_and_is_not_cacheable()
    {
        await using var app = new FabApplication().Answering(FabTransports.Prdb, new FakePrdb());
        var client = await app.SignedInClientAsync();
        var token = TestContext.Current.CancellationToken;
        await client.PostAsJsonAsync("/api/connections/prdb", new { apiKey = Key }, token);
        var summary = await client.GetStringAsync("/api/connections", token);
        Assert.DoesNotContain(Key, summary);
        var state = (await client.GetFromJsonAsync<ConnectionsState>("/api/connections", Json, token))!;
        Assert.EndsWith("cdef", state.PrdbKeyHint);

        using var revealed = await client.PostAsJsonAsync("/api/connections/keys/prdb", new { }, token);
        revealed.EnsureSuccessStatusCode();
        Assert.True(revealed.Headers.CacheControl!.NoStore);
        Assert.Equal(Key, (await revealed.Content.ReadFromJsonAsync<ConnectionKey>(token))!.ApiKey);

        using var refused = await app.CreateClient().PostAsJsonAsync("/api/connections/keys/prdb", new { }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task Sabnzbd_check_failure_retains_last_success()
    {
        var remote = new FakeSabnzbd();
        await using var app = new FabApplication().Answering(FabTransports.Sabnzbd, remote);
        var client = await app.SignedInClientAsync();
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("fab-connection-");
        try
        {
            var request = new { url = "http://sabnzbd.invalid:8080", apiKey = FakeSabnzbd.RightKey, category = "xxx", downloadDirectory = directory.FullName };
            await client.PostAsJsonAsync("/api/connections/sabnzbd", request, token);
            var first = (await client.GetFromJsonAsync<ConnectionsState>("/api/connections", Json, token))!;
            Assert.NotNull(first.SabnzbdLastSuccessAt);
            remote.RefusesThisNetwork = true;
            await client.PostAsJsonAsync("/api/connections/sabnzbd", request, token);
            var failed = (await client.GetFromJsonAsync<ConnectionsState>("/api/connections", Json, token))!;
            Assert.Equal(SabnzbdConnectionOutcome.AccessDenied, failed.SabnzbdLastCheckOutcome);
            Assert.Equal(first.SabnzbdLastSuccessAt, failed.SabnzbdLastSuccessAt);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Indexer_failed_check_retains_last_success()
    {
        var remote = new FakeIndexer();
        await using var app = new FabApplication().Answering(FabTransports.Indexers, remote);
        var client = await app.SignedInClientAsync();
        var token = TestContext.Current.CancellationToken;
        var request = new { name = "Example", url = "https://indexer.invalid/api", apiKey = FakeIndexer.RightKey };
        await client.PostAsJsonAsync("/api/connections/indexers", request, token);
        var first = Assert.Single((await client.GetFromJsonAsync<List<ConfiguredIndexer>>("/api/connections/indexers", Json, token))!);
        Assert.NotNull(first.LastSuccessAt);
        remote.SearchStatus = HttpStatusCode.Unauthorized;
        await client.PostAsJsonAsync($"/api/connections/indexers/{first.Id}", request, token);
        var failed = Assert.Single((await client.GetFromJsonAsync<List<ConfiguredIndexer>>("/api/connections/indexers", Json, token))!);
        Assert.Equal(IndexerConnectionOutcome.WrongKey, failed.LastVerdict);
        Assert.Equal(first.LastSuccessAt, failed.LastSuccessAt);
    }

    [Fact]
    public async Task No_configuration_does_not_invent_a_successful_check()
    {
        await using var app = new FabApplication();
        var client = await app.SignedInClientAsync();
        var state = (await client.GetFromJsonAsync<ConnectionsState>("/api/connections", Json, TestContext.Current.CancellationToken))!;
        Assert.False(state.PrdbConfigured);
        Assert.Null(state.PrdbLastSuccessAt);
        Assert.Null(state.PrdbLastCheckOutcome);
        Assert.Null(state.PrdbKeyHint);
    }
}
