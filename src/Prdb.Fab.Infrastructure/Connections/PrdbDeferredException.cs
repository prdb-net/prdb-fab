using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// The governor turned a prdb request away. Thrown at the transport, where the
/// request would otherwise have been sent.
/// </summary>
/// <remarks>
/// An exception rather than a return value because of where it has to be
/// raised: the SDK builds and sends the request, so the only place the decision
/// can be made is inside the handler chain, and the only thing a handler can do
/// instead of answering is throw. What catches it is the lane, once, where it
/// becomes ADR 0014's deferral — not a failure, not a run.
/// </remarks>
public sealed class PrdbDeferredException(PrdbWork work, TimeSpan wait, string? reason)
    : Exception($"A prdb request for {work} was deferred: {reason ?? "the budget is short"}.")
{
    public PrdbWork Work { get; } = work;

    /// <summary>How long before it is worth trying again.</summary>
    public TimeSpan Wait { get; } = wait;

    /// <summary>For a person reading a log. Never read for control flow.</summary>
    public string? Deferral { get; } = reason;
}
