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
    /// <remarks>
    /// ADR 0061 put a submitted user preview here too, before there was one to
    /// weigh. ADR 0064 takes it back out: this reserve is sized for what is in
    /// it — a report and a hash submission, both small, both rare, both over in
    /// one request — and a publication backlog sitting inside it would drain a
    /// Fulfilment queue behind megabytes. See <see cref="Publications"/>.
    /// </remarks>
    Writes,

    /// <summary>
    /// ADR 0060: the detail read a Preview asks for when the Catalogue holds no
    /// picture of the Video somebody has just opened.
    /// </summary>
    /// <remarks>
    /// Here and not higher, and here and not lower. Somebody is sitting in
    /// front of it, which is the argument the two at the top make and the
    /// reason this outranks every feed. It is also the only kind a person can
    /// cause by clicking, which is why it gives up before a write does: a write
    /// is a queued obligation and this is a glance.
    /// </remarks>
    Preview,

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
    /// ADR 0061: the user previews read for a filed Video File, and the change
    /// feed that follows what moderation does to them.
    /// </summary>
    /// <remarks>
    /// Below every feed and above the repair pass, which is where background
    /// work belongs that nobody is waiting on and that nothing else waits on
    /// either. A Library preview arriving an hour late costs nothing, and a
    /// withdrawal observed an hour late costs a picture shown an hour too long
    /// — neither is worth one request the Catalogue's own feeds would have
    /// spent. The <em>interactive</em> half of the same population is not here:
    /// somebody opening a Preview is <see cref="Preview"/>, because it is the
    /// same act, the same waiting person and the same argument.
    /// </remarks>
    UserPreviews,

    /// <summary>
    /// ADR 0064: a generated Sprite Sheet and its paired WebVTT, submitted to
    /// prdb for a Video File this installation filed.
    /// </summary>
    /// <remarks>
    /// A queued obligation like a <see cref="Writes"/> is, and nothing like one
    /// to send. It carries megabytes, it takes seconds on the wire rather than
    /// milliseconds, and after a Library backfill there can be thousands of
    /// them — so it is held back well below the reserve that exists to keep the
    /// small obligations moving, and below every feed, because a preview
    /// published an hour late costs nothing at all. Above repair, which spends
    /// only what is left above half the limit.
    /// </remarks>
    Publications,

    /// <summary>
    /// ADR 0013's repair pass. Last, and the one ADR 0014 gives a number to:
    /// it may spend whatever holds hourly usage under half of the limit.
    /// </summary>
    Repair,
}
