using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// ADR 0041 puts the governor in a handler on the prdb transport, and this is
/// it: every request that reaches prdb passes through here, including one a
/// person asked for.
/// </summary>
/// <remarks>
/// The seam is the point. <see cref="PrdbGateway"/> is the one place a prdb
/// client is built, so this handler is on the path of everything the tool ever
/// sends — a call site built somewhere else would be a bypass nobody would
/// notice, which is what the architecture test guards and what this relies on.
/// </remarks>
internal sealed class PrdbGovernorHandler(PrdbGovernor governor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var work = governor.Current;
        var verdict = governor.Ask(work);

        if (!verdict.Sends)
        {
            throw new PrdbDeferredException(work, verdict.Wait, verdict.Reason);
        }

        var response = await base.SendAsync(request, cancellationToken);

        governor.Observe(response);

        if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests
            || work == PrdbWork.Verification)
        {
            // Verification is handed the 429 rather than a deferral: ADR 0010
            // has a verdict for a spent quota, and a person who has just typed
            // a key is owed the answer rather than silence.
            return response;
        }

        // A 429 is the governor having been wrong about the budget, not the
        // routine having failed. Nothing is broken — the plan is smaller than
        // the schedule, which ADR 0014 makes a named condition of its own — so
        // it becomes a deferral carrying prdb's own Retry-After, which that ADR
        // says overrides the backoff exactly.
        var wait = governor.HeldFor() ?? PrdbBudget.ShortestWait;

        response.Dispose();

        throw new PrdbDeferredException(work, wait, "prdb answered 429");
    }
}
