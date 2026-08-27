using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using Prdb.Fab.Infrastructure.Access;

namespace Prdb.Fab.Host.Access;

/// <summary>
/// What carries a sign-in: an HttpOnly cookie holding the token of a session
/// row, and a challenge that is <c>401</c> and never a redirect (ADR 0010).
/// </summary>
public static class SessionAuthentication
{
    public const string Scheme = "FabSession";

    public const string CookieName = "fab_session";

    /// <summary>The session row this request was authenticated by.</summary>
    public const string SessionIdClaim = "fab:sid";

    public static long? SessionId(this ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(SessionIdClaim), out var id) ? id : null;

    /// <summary>
    /// ADR 0010: HttpOnly, <c>SameSite=Strict</c>, and <c>Secure</c> whenever
    /// the request arrived over https.
    /// </summary>
    /// <remarks>
    /// <c>Secure</c> is conditional rather than always set, and the reason is
    /// the deployment ADR 0034 fixes: plain http on a home network is the
    /// ordinary case, and a cookie marked <c>Secure</c> there is a cookie the
    /// browser never sends back — which would present as a sign-in that does
    /// nothing. That http is unencrypted is documented rather than papered over
    /// (ADR 0010, and ticket 11).
    /// </remarks>
    public static void AppendSessionCookie(
        this HttpContext http,
        string token,
        DateTimeOffset expiresAt) =>
        http.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = "/",
            Expires = expiresAt,
        });

    public static void DeleteSessionCookie(this HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = "/",
        });
}

/// <summary>
/// Reads the cookie, finds the session row, and lets the request through when
/// the row is still usable. Everything about whether a session is alive lives on
/// that row, which is what makes revoking one take effect at once.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    Sessions sessions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionAuthentication.CookieName, out var token))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await sessions.AuthenticateAsync(token, Context.RequestAborted);

        if (session is null)
        {
            // Expired or revoked. The cookie is cleared so the browser stops
            // presenting something that will never work again.
            Context.DeleteSessionCookie();

            return AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity(
            [new Claim(SessionAuthentication.SessionIdClaim, session.Id.ToString())],
            SessionAuthentication.Scheme);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SessionAuthentication.Scheme));
    }

    /// <summary>
    /// ADR 0010: <em>an unauthenticated request gets 401, never a redirect.</em>
    /// The browser side is one page that decides for itself what to show, and a
    /// redirect would take that decision away from it.
    /// </summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        // ADR 0040: a status code keeps its own meaning and says it as
        // ProblemDetails. Nothing here is a verdict — the caller is not signed
        // in, which is not something the tool checked and can answer.
        await Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Not signed in.",
            detail: "This installation is reached by signing in with its password.")
            .ExecuteAsync(Context);
    }
}
