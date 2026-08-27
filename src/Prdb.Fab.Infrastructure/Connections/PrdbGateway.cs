using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;

using Prdb.Fab.Core.Connections;
using Prdb.Sdk;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// The one place prdb is reached from.
/// </summary>
/// <remarks>
/// <para>
/// That there is only one is the point rather than a convenience. ADR 0014
/// requires every prdb request to pass the governor, including one a person
/// asked for, and ADR 0041 puts the governor in a handler on this transport —
/// so this class is the seam it is inserted at, and a second call site built
/// somewhere else is a bypass nobody would notice. The governor itself is not
/// here: one verification request in front of a waiting user cannot exercise a
/// budget read off response headers, and the sync slice builds it where there
/// are routines to defer.
/// </para>
/// <para>
/// ADR 0041 also decides the two things that look like details and are not: the
/// SDK's own retrying is off, because ADR 0014 retries at the routine and a
/// swallowed <c>429</c> is exactly what the governor exists to see; and the
/// client is built per use over a pooled transport, because ADR 0020 makes the
/// key something a person changes in a form while the container runs.
/// </para>
/// </remarks>
public sealed class PrdbGateway(IHttpMessageHandlerFactory transports, ILogger<PrdbGateway> logger)
{
    /// <summary>
    /// ADR 0010's mandatory check: <c>GET /user-identity</c>, and the four
    /// verdicts it can come back as.
    /// </summary>
    public async Task<PrdbCheck> CheckAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // The SDK refuses an empty key at construction, and it is right to:
            // that is a configuration value that failed to resolve. Here it is
            // an empty field, which is the same answer prdb would give.
            return new PrdbCheck(PrdbConnectionOutcome.WrongKey, UserHash: null, RetryAfterSeconds: null);
        }

        var client = PrdbClientFactory.Create(
            apiKey,
            transport: transports.CreateHandler(FabTransports.Prdb),
            retry: PrdbRetryOptions.Disabled,
            timeout: FabTransports.PrdbTimeout);

        try
        {
            var identity = await client.UserIdentity.GetAsync(cancellationToken: cancellationToken);

            if (identity?.UserHash is not { Length: > 0 } userHash)
            {
                // prdb answered without the one field the check is for. Nothing
                // was refused, so this is not a wrong key; it is prdb not being
                // itself, which is the same thing to do about as a 503.
                logger.LogWarning("prdb answered GET /user-identity without a user hash.");

                return new PrdbCheck(PrdbConnectionOutcome.NotRightNow, null, null);
            }

            logger.LogInformation("The prdb key was checked and prdb accepted it.");

            return new PrdbCheck(PrdbConnectionOutcome.Saved, userHash, null);
        }
        catch (ApiException refused)
        {
            var outcome = refused.ResponseStatusCode switch
            {
                401 => PrdbConnectionOutcome.WrongKey,
                403 => PrdbConnectionOutcome.NoApiAccess,
                429 => PrdbConnectionOutcome.QuotaSpent,
                _ => PrdbConnectionOutcome.NotRightNow,
            };

            logger.LogInformation(
                "prdb refused the key that was checked: {Status}, read as {Outcome}.",
                refused.ResponseStatusCode,
                outcome);

            return new PrdbCheck(outcome, null, RetryAfterFrom(refused.ResponseHeaders));
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            // ADR 0041: a timeout is *the request failed* and never a genuine
            // answer. Here that distinction is the difference between telling
            // somebody their key is wrong and telling them to try again.
            logger.LogInformation(
                "prdb did not answer the key check: {Reason}.",
                unreachable.GetType().Name);

            return new PrdbCheck(PrdbConnectionOutcome.NotRightNow, null, null);
        }
    }

    /// <summary>
    /// The <c>Retry-After</c> prdb sends with a <c>429</c>, in seconds.
    /// </summary>
    /// <remarks>
    /// Only the delta-seconds form is read. The HTTP-date form is the other half
    /// of that header and turning one into a wait needs the clock, which
    /// ADR 0042 keeps behind <c>TimeProvider</c> — and prdb documents this one
    /// as a number of seconds.
    /// </remarks>
    private static int? RetryAfterFrom(IDictionary<string, IEnumerable<string>>? headers)
    {
        if (headers is null || !headers.TryGetValue("Retry-After", out var values))
        {
            return null;
        }

        return int.TryParse(values.FirstOrDefault(), out var seconds) && seconds > 0 ? seconds : null;
    }
}

/// <summary>
/// What <c>GET /user-identity</c> came back as.
/// </summary>
/// <param name="Outcome">
/// <see cref="PrdbConnectionOutcome.Saved"/> here means <em>prdb accepted this
/// key</em>. Whether it is stored is the caller's to decide, since a key
/// belonging to another account needs a confirmation first — so this value only
/// ever leaves the application after the caller has made it true.
/// </param>
/// <param name="UserHash">
/// Stable per prdb account, and set on exactly the accepting answer. ADR 0010
/// stores it so that a key entered later that belongs to somebody else is
/// recognised rather than silently swapping the wanted list out.
/// </param>
public sealed record PrdbCheck(PrdbConnectionOutcome Outcome, string? UserHash, int? RetryAfterSeconds);
