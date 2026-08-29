using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Access;

/// <summary>
/// ADR 0020's Account route, end to end: ADR 0010's password change, which
/// requires the current password and ends every other session.
/// </summary>
public sealed class ChangePasswordRouteTests
{
    private const string Password = "a long enough password";

    private const string Replacement = "a replacement password";

    /// <summary>
    /// The lever, pulled: the browser that pulled it stays signed in and every
    /// other one is out at once rather than at its expiry.
    /// </summary>
    [Fact]
    public async Task It_ends_the_other_sessions_and_keeps_this_one()
    {
        await using var application = new FabApplication();

        var mine = await application.SignedInClientAsync(Password);
        var elsewhere = await application.SignedInClientAsync(Password);

        var verdict = await ChangeAsync(mine, Password, Replacement);

        Assert.Equal("Changed", verdict.Outcome);
        Assert.Equal(1, verdict.SessionsEnded);

        // This one is still in.
        using var still = await mine.GetAsync("/api/connections", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);

        // ADR 0010: an unauthenticated request gets 401, never a redirect — so
        // this is exactly what the other browser sees on its next request.
        using var out401 = await elsewhere.GetAsync("/api/connections", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, out401.StatusCode);
    }

    [Fact]
    public async Task The_new_password_is_the_one_that_signs_in_afterwards()
    {
        await using var application = new FabApplication();

        var client = await application.SignedInClientAsync(Password);
        await ChangeAsync(client, Password, Replacement);

        var fresh = application.CreateClient();

        Assert.Equal("WrongPassword", (await SignInAsync(fresh, Password)).Outcome);
        Assert.Equal("SignedIn", (await SignInAsync(fresh, Replacement)).Outcome);
    }

    /// <summary>
    /// A session left open on a borrowed machine must not be a way to lock the
    /// owner out of their own installation.
    /// </summary>
    [Fact]
    public async Task A_wrong_current_password_changes_nothing()
    {
        await using var application = new FabApplication();

        var mine = await application.SignedInClientAsync(Password);
        var elsewhere = await application.SignedInClientAsync(Password);

        var verdict = await ChangeAsync(mine, "not the password", Replacement);

        Assert.Equal("WrongPassword", verdict.Outcome);
        Assert.Equal(0, verdict.SessionsEnded);

        using var untouched = await elsewhere.GetAsync(
            "/api/connections", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_current_passwords_are_throttled_before_the_hash()
    {
        await using var application = new FabApplication();

        var client = await application.SignedInClientAsync(Password);
        Verdict verdict;
        var attempts = 0;

        do
        {
            verdict = await ChangeAsync(client, "not the password", Replacement);
            attempts++;
        }
        while (verdict.Outcome == "WrongPassword" && attempts < 50);

        Assert.Equal("TooManyAttempts", verdict.Outcome);
        Assert.True(verdict.RetryAfterSeconds is >= 1 and <= 300);

        var correct = await ChangeAsync(client, Password, Replacement);

        Assert.Equal("TooManyAttempts", correct.Outcome);
        Assert.True(correct.RetryAfterSeconds is >= 1 and <= 300);
    }

    [Fact]
    public async Task A_new_password_the_rule_refuses_carries_its_reason()
    {
        await using var application = new FabApplication();

        var client = await application.SignedInClientAsync(Password);
        var verdict = await ChangeAsync(client, Password, "short");

        Assert.Equal("Refused", verdict.Outcome);
        Assert.NotNull(verdict.Refusal);
        Assert.Equal(0, verdict.SessionsEnded);
    }

    /// <summary>
    /// ADR 0010's window is one condition wide and this is not in it: changing
    /// a password is not one of the two unauthenticated writes.
    /// </summary>
    [Fact]
    public async Task Nobody_who_is_not_signed_in_may_change_it()
    {
        await using var application = new FabApplication();
        _ = await application.SignedInClientAsync(Password);

        using var refused = await application.CreateClient().PostAsJsonAsync(
            "/api/access/change-password",
            new { current = Password, next = Replacement },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static async Task<Verdict> ChangeAsync(HttpClient client, string current, string next)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/access/change-password",
            new { current, next },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<SignIn> SignInAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/access/sign-in",
            new { password },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SignIn>(TestContext.Current.CancellationToken))!;
    }

    private sealed record Verdict(
        string Outcome,
        string? Refusal,
        int SessionsEnded,
        int? RetryAfterSeconds);

    private sealed record SignIn(string Outcome);
}
