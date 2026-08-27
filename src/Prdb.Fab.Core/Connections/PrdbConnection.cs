namespace Prdb.Fab.Core.Connections;

/// <summary>
/// What happened when the prdb API key was checked and stored. ADR 0010 asks
/// for four distinct verdicts rather than one message, because two of them ask
/// for a correction and two of them only ask for patience.
/// </summary>
public enum PrdbConnectionOutcome
{
    /// <summary>The key answered, and it is now this installation's key.</summary>
    Saved,

    /// <summary><c>401</c>. Nothing is stored.</summary>
    WrongKey,

    /// <summary>
    /// <c>403</c>: the key is real, and the account behind it has no tier that
    /// includes API access. A different sentence and a different fix.
    /// </summary>
    NoApiAccess,

    /// <summary><c>429</c>. Worth retrying rather than correcting.</summary>
    QuotaSpent,

    /// <summary><c>503</c>, a timeout, or a connection that never opened.</summary>
    NotRightNow,

    /// <summary>
    /// The key works and belongs to a different prdb account than the one this
    /// installation has been running as. ADR 0010 does not block this — people
    /// do move accounts — but it demands an explicit confirmation first.
    /// </summary>
    AnotherAccount,
}

/// <summary>
/// ADR 0043: a rule in this project cannot log, so a rule that refuses returns
/// its reason and the reason is a value. These are those values — one sentence
/// per verdict, so that the form has nothing to invent.
/// </summary>
public static class PrdbConnection
{
    public static string Sentence(PrdbConnectionOutcome outcome) => outcome switch
    {
        PrdbConnectionOutcome.Saved =>
            "prdb answered, and this installation is now using that key.",

        PrdbConnectionOutcome.WrongKey =>
            "prdb does not know that key. Copy it again from your prdb account; "
            + "nothing has been stored.",

        PrdbConnectionOutcome.NoApiAccess =>
            "The key is real, but the prdb account behind it has no subscription "
            + "tier that includes API access. The key is not the thing to change.",

        PrdbConnectionOutcome.QuotaSpent =>
            "The key is spent for now: prdb's rate limit refused the request. "
            + "Nothing is wrong with the key, so this is worth trying again "
            + "rather than correcting.",

        PrdbConnectionOutcome.NotRightNow =>
            "prdb did not answer. That is prdb or the network rather than the "
            + "key, and it is worth trying again.",

        PrdbConnectionOutcome.AnotherAccount =>
            "That key belongs to a different prdb account than the one this "
            + "installation has been using. The wanted list is that account's, "
            + "so it is swapped out underneath; and what has already been "
            + "reported stays recorded against the old account rather than this "
            + "one. Confirm and the key is stored.",

        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
