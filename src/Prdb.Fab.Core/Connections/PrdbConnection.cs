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
            "prdb API key verified and saved.",

        PrdbConnectionOutcome.WrongKey =>
            "prdb rejected this API key. Copy the key from your prdb account and try again. The rejected "
            + "key was not stored.",

        PrdbConnectionOutcome.NoApiAccess =>
            "This prdb account does not have API access. Check your subscription tier on prdb.",

        PrdbConnectionOutcome.QuotaSpent =>
            "prdb’s request limit has been reached. Wait for the limit to reset, then try again.",

        PrdbConnectionOutcome.NotRightNow =>
            "Could not reach prdb. Check network access and try again later.",

        PrdbConnectionOutcome.AnotherAccount =>
            "This key belongs to a different prdb account. Switching replaces the local Wanted list with "
            + "that account’s list. Previous reports remain associated with the old account. Confirm to "
            + "save the new key.",

        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
