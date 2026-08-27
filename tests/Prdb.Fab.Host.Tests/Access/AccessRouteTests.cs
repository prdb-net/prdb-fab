using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Access;

/// <summary>
/// ADR 0010's window, its cookie and its 401, against the application as
/// <c>Program.cs</c> composes it.
/// </summary>
/// <remarks>
/// Every test builds its own installation. Setting the password is a
/// one-way door, so a shared fixture would make these tests depend on the order
/// they happen to run in.
/// </remarks>
public sealed class AccessRouteTests
{
    private const string Password = "a long enough password";

    [Fact]
    public async Task An_unauthenticated_request_is_refused_rather_than_redirected()
    {
        using var application = new FabApplication();
        using var client = application.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/api/skeleton/items", TestContext.Current.CancellationToken);

        // ADR 0010: 401, never a redirect. The browser side is one page that
        // decides for itself what to show.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task The_password_can_be_set_once_and_then_the_window_is_shut()
    {
        using var application = new FabApplication();
        using var client = application.CreateClient();

        var first = await PostAsync<SetPasswordVerdict>(client, "/api/access/password", new { password = Password });
        Assert.Equal("Set", first.Outcome);

        var second = await PostAsync<SetPasswordVerdict>(
            client, "/api/access/password", new { password = "a different long password" });

        Assert.Equal("AlreadySet", second.Outcome);
    }

    /// <summary>
    /// Whoever just set the password is signed in by having set it. Asking them
    /// to type it again immediately would be a step that proves nothing.
    /// </summary>
    [Fact]
    public async Task Setting_the_password_signs_you_in()
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync(Password);

        using var response = await client.GetAsync("/api/skeleton/items", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_cookie_is_httponly_and_strict()
    {
        using var application = new FabApplication();
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/access/password", new { password = Password }, TestContext.Current.CancellationToken);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        // ADR 0010: Secure only over https, because a cookie marked Secure on a
        // plain-http home network is one the browser never sends back — which
        // would present as a sign-in that does nothing.
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_password_the_rule_refuses_comes_back_as_a_reason()
    {
        using var application = new FabApplication();
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/access/password", new { password = "short" }, TestContext.Current.CancellationToken);

        // ADR 0040: a verdict is a success with a typed body.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var verdict = await response.Content.ReadFromJsonAsync<SetPasswordVerdict>(
            TestContext.Current.CancellationToken);

        Assert.Equal("Refused", verdict!.Outcome);
        Assert.NotNull(verdict.Refusal);
    }

    [Fact]
    public async Task Signing_in_needs_the_password()
    {
        using var application = new FabApplication();
        using var client = application.CreateClient();

        // Before one exists there is nothing to be wrong about, and that is its
        // own answer rather than a wrong password.
        var beforehand = await PostAsync<SignInVerdict>(client, "/api/access/sign-in", new { password = Password });
        Assert.Equal("NoPasswordYet", beforehand.Outcome);

        await PostAsync<SetPasswordVerdict>(client, "/api/access/password", new { password = Password });

        var wrong = await PostAsync<SignInVerdict>(client, "/api/access/sign-in", new { password = "wrong" });
        Assert.Equal("WrongPassword", wrong.Outcome);

        var right = await PostAsync<SignInVerdict>(client, "/api/access/sign-in", new { password = Password });
        Assert.Equal("SignedIn", right.Outcome);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_at_once()
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync(Password);

        using (var response = await client.PostAsync(
            "/api/access/sign-out", content: null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using var afterwards = await client.GetAsync("/api/skeleton/items", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    /// <summary>
    /// ADR 0010: one password with no username is the easiest thing in the
    /// world to try repeatedly.
    /// </summary>
    [Fact]
    public async Task Guessing_is_throttled()
    {
        using var application = new FabApplication();
        using var client = application.CreateClient();

        await PostAsync<SetPasswordVerdict>(client, "/api/access/password", new { password = Password });

        SignInVerdict verdict;
        var attempts = 0;

        do
        {
            verdict = await PostAsync<SignInVerdict>(client, "/api/access/sign-in", new { password = "wrong" });
            attempts++;
        }
        while (verdict.Outcome == "WrongPassword" && attempts < 50);

        Assert.Equal("TooManyAttempts", verdict.Outcome);
        Assert.NotNull(verdict.RetryAfterSeconds);

        // And the throttle is in front of the password, not behind it: the
        // right one waits too, which is what stops it being a way to tell a
        // wrong password from a rate limit.
        var correct = await PostAsync<SignInVerdict>(client, "/api/access/sign-in", new { password = Password });
        Assert.Equal("TooManyAttempts", correct.Outcome);
    }

    /// <summary>
    /// ADR 0010's recovery path, taken the way it is taken in the field: the
    /// container is started once with the variable set.
    /// </summary>
    [Fact]
    public async Task The_reset_variable_clears_the_password_and_keeps_the_rest()
    {
        var original = new FabApplication();
        FabApplication? restarted = null;

        try
        {
            using (var client = await original.SignedInClientAsync(Password))
            {
                using var response = await client.GetAsync(
                    "/api/skeleton/items", TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            restarted = original.RestartWith("FAB_RESET_PASSWORD", "true");
            original.Dispose();

            using var afterwards = restarted.CreateClient();

            // Back in front of the window: there is no password, so the one
            // that was set is not merely wrong.
            var signIn = await PostAsync<SignInVerdict>(
                afterwards, "/api/access/sign-in", new { password = Password });

            Assert.Equal("NoPasswordYet", signIn.Outcome);

            // And the window is open again, which is the whole point.
            var set = await PostAsync<SetPasswordVerdict>(
                afterwards, "/api/access/password", new { password = "a replacement password" });

            Assert.Equal("Set", set.Outcome);
        }
        finally
        {
            restarted?.Dispose();
            original.Dispose();
        }
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private sealed record SetPasswordVerdict(string Outcome, string? Refusal);

    private sealed record SignInVerdict(string Outcome, int? RetryAfterSeconds);
}
