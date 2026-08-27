namespace Prdb.Fab.Core.Sync;

/// <summary>
/// What a prdb request is for, and — as the order of the members — which
/// request is given up first when the budget is short.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014 fixes the order of precedence and this is it, written whole rather
/// than only as far as this slice reaches: <c>POST /videos/identify</c> first,
/// because a file that has arrived is waiting on it to be filed; then writes,
/// which are rare and already queued; then the user feeds, What's New, images,
/// actors, sites; then repair. Writing the two that have no caller yet into the
/// same list is what keeps them from being <em>inserted</em> later by whoever
/// builds them, which is how an order becomes a matter of opinion.
/// </para>
/// <para>
/// The declaration order is the precedence, so a member added in the middle
/// changes what is given up before what. That is deliberate: the alternative is
/// a number beside each name, which is the same fact written twice.
/// </para>
/// </remarks>
public enum PrdbWork
{
    /// <summary>
    /// Checking a key somebody has just typed. Not one of ADR 0014's eight, and
    /// above all of them, because it is the only prdb request a person is
    /// sitting in front of and the only way back from a key that no longer
    /// works. It spends one request and cannot be deferred without making a
    /// spent budget unfixable from the inside.
    /// </summary>
    Verification,

    /// <summary>
    /// <c>POST /videos/identify</c>. First of ADR 0014's order: an arrived file
    /// is waiting on it, and everything downstream of filing waits with it.
    /// </summary>
    Identification,

    /// <summary>
    /// Fulfilment reports and hash submissions. Rare, and ADR 0013 already
    /// queues them rather than dropping them.
    /// </summary>
    Writes,

    /// <summary>The wanted list and the two favourites feeds.</summary>
    UserFeeds,

    /// <summary>The newest videos, and the pass reading backwards from them.</summary>
    WhatsNew,

    /// <summary>The video images feed.</summary>
    Images,

    /// <summary>The actors feed.</summary>
    Actors,

    /// <summary>The site list, under its ETag.</summary>
    Sites,

    /// <summary>
    /// ADR 0013's repair pass. Last, and the one ADR 0014 gives a number to:
    /// it may spend whatever holds hourly usage under half of the limit.
    /// </summary>
    Repair,
}
