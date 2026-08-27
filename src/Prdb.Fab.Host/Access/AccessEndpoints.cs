using Microsoft.AspNetCore.Authorization;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Access;

namespace Prdb.Fab.Host.Access;

/// <summary>
/// Setting the password, signing in, and signing out. ADR 0010's window is the
/// whole of the unauthenticated surface, and it is one condition wide.
/// </summary>
public static class AccessEndpoints
{
    public static void MapAccess(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/access").WithTags("Access");

        // ADR 0010: the browser side is one page that decides for itself what
        // to show, and this is what it decides from. Anonymous, because the
        // first thing it has to be able to answer is that nobody is signed in.
        group.MapGet("/state", async (
            Installations installations,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var signedIn = http.User.Identity?.IsAuthenticated == true;
            var passwordSet = !await installations.IsUnclaimedAsync(cancellationToken);

            // How far onboarding has got is told to whoever is entitled to act
            // on it. Before a password exists that is everyone, and the answer
            // is the password; afterwards it is whoever is signed in. ADR 0010
            // asks this endpoint for what the page needs to decide, and a page
            // showing the sign-in form needs nothing more than that it must.
            var nextStep = signedIn || !passwordSet
                ? await installations.NextStepAsync(cancellationToken)
                : (OnboardingStep?)null;

            return TypedResults.Ok(new AccessState(passwordSet, signedIn, nextStep));
        }).AllowAnonymous();

        // ADR 0010's first unauthenticated write, and in this slice its only
        // one. Restore is the second and joins on the same condition, which is
        // why the condition is asked of the installation rather than written
        // into this endpoint.
        group.MapPost("/password", async (
            SetPasswordRequest request,
            PasswordGate passwords,
            Sessions sessions,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var (outcome, refusal) = await passwords.SetInitialAsync(request.Password, cancellationToken);

            if (outcome is not SetPasswordOutcome.Set)
            {
                return TypedResults.Ok(new SetPasswordVerdict(outcome, refusal));
            }

            // Whoever just set the password is signed in by having set it.
            // Asking them to type it again immediately would be a step that
            // proves nothing, and ADR 0010 counts the steps.
            var (token, expiresAt) = await sessions.CreateAsync(cancellationToken);
            http.AppendSessionCookie(token, expiresAt);

            return TypedResults.Ok(new SetPasswordVerdict(outcome, Refusal: null));
        }).AllowAnonymous();

        group.MapPost("/sign-in", async (
            SignInRequest request,
            PasswordGate passwords,
            Sessions sessions,
            SignInThrottle throttle,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            // Asked before the password is checked, so a caller who is being
            // throttled never reaches the hash — which is the expensive half
            // and the half worth protecting.
            if (throttle.RetryAfter() is { } wait)
            {
                return TypedResults.Ok(new SignInVerdict(
                    SignInOutcome.TooManyAttempts,
                    RetryAfterSeconds: (int)Math.Ceiling(wait.TotalSeconds)));
            }

            var verified = await passwords.VerifyAsync(request.Password, cancellationToken);

            if (verified is null)
            {
                return TypedResults.Ok(new SignInVerdict(SignInOutcome.NoPasswordYet, RetryAfterSeconds: null));
            }

            if (verified is false)
            {
                throttle.RecordFailure();

                return TypedResults.Ok(new SignInVerdict(SignInOutcome.WrongPassword, RetryAfterSeconds: null));
            }

            throttle.RecordSuccess();

            var (token, expiresAt) = await sessions.CreateAsync(cancellationToken);
            http.AppendSessionCookie(token, expiresAt);

            return TypedResults.Ok(new SignInVerdict(SignInOutcome.SignedIn, RetryAfterSeconds: null));
        }).AllowAnonymous();

        // ADR 0040: a named action. The row goes, so the cookie it backed is
        // worthless from this moment rather than at its expiry.
        group.MapPost("/sign-out", async (
            Sessions sessions,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            http.Request.Cookies.TryGetValue(SessionAuthentication.CookieName, out var token);

            await sessions.RevokeAsync(token, cancellationToken);
            http.DeleteSessionCookie();

            return TypedResults.NoContent();
        });
    }
}

/// <summary>
/// ADR 0010's three questions: whether a password is set, whether this caller is
/// signed in, and which onboarding step is next.
/// </summary>
public sealed record AccessState(bool PasswordSet, bool SignedIn, OnboardingStep? NextStep);

public sealed record SetPasswordRequest(string? Password);

/// <summary>ADR 0040: a verdict is a success with a typed body saying what happened.</summary>
public sealed record SetPasswordVerdict(SetPasswordOutcome Outcome, string? Refusal);

public sealed record SignInRequest(string? Password);

/// <summary>
/// A wrong password is something the tool checked and can answer, so it is a
/// 200 with a name in it rather than a 401 — which ADR 0010 reserves for
/// "not signed in" and which TanStack Query would treat as a session that ended.
/// </summary>
public sealed record SignInVerdict(SignInOutcome Outcome, int? RetryAfterSeconds);
