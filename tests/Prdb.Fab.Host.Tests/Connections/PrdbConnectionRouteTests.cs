using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// ADR 0010's mandatory step, end to end: the key is checked against prdb
/// through the SDK, and the four verdicts a check can come back as are four
/// different sentences rather than one.
/// </summary>
public sealed class PrdbConnectionRouteTests
{
    private const string AKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task A_key_prdb_accepts_is_stored()
    {
        var prdb = new FakePrdb();
        await using var application = Answering(prdb);
        var client = await application.SignedInClientAsync();

        var verdict = await SubmitAsync(client, AKey);

        Assert.Equal("Saved", verdict.Outcome);
        Assert.Equal(AKey, prdb.LastKey);
        Assert.True(await IsConfiguredAsync(client));
    }

    /// <summary>
    /// ADR 0041: the key travels in a header on this transport, and the tool
    /// says what it is on every one of them. Both are properties of the request
    /// that leaves, so the socket is where they can be seen.
    /// </summary>
    [Fact]
    public async Task The_request_carries_the_key_in_a_header_and_says_what_it_is()
    {
        var prdb = new FakePrdb();
        await using var application = Answering(prdb);
        var client = await application.SignedInClientAsync();

        await SubmitAsync(client, AKey);

        Assert.Equal(AKey, prdb.LastKey);
        // ADR 0041: the tool says what it is, and says it first — the SDK's own
        // marker has already been put there by the time the header is rewritten,
        // and it is kept behind rather than dropped.
        Assert.StartsWith("prdb-fab/", prdb.LastUserAgent);
        Assert.Contains("kiota", prdb.LastUserAgent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The four ADR 0010 names. Two ask for a correction and two ask for
    /// patience, which is the whole reason they are four.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "WrongKey")]
    [InlineData(HttpStatusCode.Forbidden, "NoApiAccess")]
    [InlineData(HttpStatusCode.TooManyRequests, "QuotaSpent")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "NotRightNow")]
    public async Task Each_refusal_is_its_own_verdict(HttpStatusCode status, string expected)
    {
        await using var application = Answering(new FakePrdb { Answers = status });
        var client = await application.SignedInClientAsync();

        var verdict = await SubmitAsync(client, AKey);

        Assert.Equal(expected, verdict.Outcome);
        Assert.NotEmpty(verdict.Detail);

        // ADR 0010: there is no way past a failure, and nothing is stored
        // behind one.
        Assert.False(await IsConfiguredAsync(client));
    }

    [Fact]
    public async Task No_two_refusals_say_the_same_thing()
    {
        var said = new List<string>();

        foreach (var status in new[]
        {
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.ServiceUnavailable,
        })
        {
            await using var application = Answering(new FakePrdb { Answers = status });
            var client = await application.SignedInClientAsync();

            said.Add((await SubmitAsync(client, AKey)).Detail);
        }

        Assert.Equal(said.Count, said.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task The_quota_carries_what_prdb_asked_for()
    {
        await using var application = Answering(new FakePrdb
        {
            Answers = HttpStatusCode.TooManyRequests,
            RetryAfterSeconds = 42,
        });

        var client = await application.SignedInClientAsync();

        Assert.Equal(42, (await SubmitAsync(client, AKey)).RetryAfterSeconds);
    }

    /// <summary>
    /// ADR 0041 makes this a rule at the transport: a timeout is
    /// <em>the request failed</em> and never a genuine answer. Here that is the
    /// difference between telling somebody their key is wrong and telling them
    /// to try again in a minute.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_request_that_never_arrives_is_not_a_wrong_key(bool timedOut)
    {
        await using var application = Answering(new FakePrdb
        {
            Throws = timedOut
                ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")
                : new HttpRequestException("Connection refused"),
        });

        var client = await application.SignedInClientAsync();

        Assert.Equal("NotRightNow", (await SubmitAsync(client, AKey)).Outcome);
    }

    /// <summary>
    /// ADR 0010: a key belonging to a different prdb account does not block —
    /// people do move accounts — but it demands a confirmation that names what
    /// stops lining up.
    /// </summary>
    [Fact]
    public async Task A_key_from_another_account_is_recognised_and_asks_first()
    {
        var prdb = new FakePrdb();
        await using var application = Answering(prdb);
        var client = await application.SignedInClientAsync();

        await SubmitAsync(client, AKey);

        prdb.UserHash = FakePrdb.AnotherAccount;
        var asked = await SubmitAsync(client, "fedcba9876543210fedcba9876543210");

        Assert.Equal("AnotherAccount", asked.Outcome);
        Assert.Contains("account", asked.Detail, StringComparison.OrdinalIgnoreCase);

        // Not stored: the confirmation is the act, and it has not happened.
        Assert.Equal(AKey, await StoredKeyAsync(application));

        var confirmed = await SubmitAsync(client, "fedcba9876543210fedcba9876543210", confirm: true);

        Assert.Equal("Saved", confirmed.Outcome);
        Assert.Equal("fedcba9876543210fedcba9876543210", await StoredKeyAsync(application));
    }

    /// <summary>
    /// The same account twice is not a change of account, however many times the
    /// key is re-entered.
    /// </summary>
    [Fact]
    public async Task The_same_account_never_asks()
    {
        var prdb = new FakePrdb();
        await using var application = Answering(prdb);
        var client = await application.SignedInClientAsync();

        await SubmitAsync(client, AKey);

        Assert.Equal("Saved", (await SubmitAsync(client, "another key for the same account")).Outcome);
    }

    [Fact]
    public async Task An_empty_key_is_answered_without_asking_prdb()
    {
        var prdb = new FakePrdb();
        await using var application = Answering(prdb);
        var client = await application.SignedInClientAsync();

        Assert.Equal("WrongKey", (await SubmitAsync(client, string.Empty)).Outcome);
        Assert.Equal(0, prdb.Requests);
    }

    [Fact]
    public async Task Nobody_who_is_not_signed_in_may_configure_anything()
    {
        await using var application = Answering(new FakePrdb());
        var client = application.CreateClient();

        // The window ADR 0010 opens is one condition wide and this is not in it.
        using var refused = await client.PostAsJsonAsync(
            "/api/connections/prdb",
            new { apiKey = AKey, confirmAnotherAccount = false },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static FabApplication Answering(FakePrdb prdb) =>
        new FabApplication().Answering(FabTransports.Prdb, prdb);

    private static async Task<Verdict> SubmitAsync(HttpClient client, string apiKey, bool confirm = false)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/prdb",
            new { apiKey, confirmAnotherAccount = confirm },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> IsConfiguredAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<State>("/api/connections", TestContext.Current.CancellationToken))!
            .PrdbConfigured;

    private static async Task<string?> StoredKeyAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();

        return (await scope.ServiceProvider
            .GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken)).PrdbApiKey;
    }

    private sealed record Verdict(string Outcome, string Detail, int? RetryAfterSeconds);

    private sealed record State(bool PrdbConfigured);
}
