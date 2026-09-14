using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0064's <c>preview_backfill</c>: one person's request that the previews
/// of the Library this installation already holds be generated and published.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The request exists because nothing else may start this.</strong>
/// ADR 0064 makes newly filed identified Video Files the only automatic scope
/// and says in as many words that turning the switch off and on again is not a
/// request for the rest — an installation with five thousand entries would
/// otherwise begin a five-thousand-file decode on the day it upgraded. So the
/// existing Library is published exactly when there is a row here saying
/// somebody asked, and never otherwise.
/// </para>
/// <para>
/// <strong>It carries no list and no cursor</strong>, and that is what makes it
/// restart-safe without any bookkeeping to keep in step. What is still to be
/// taken up is a query — an eligible Video File with no publication row for
/// this account and output version — and taking one up writes the row that
/// removes it from that query's answer. A restart re-asks the question; a
/// second request asks the same one and finds only what the first did not
/// reach; and a decode given up on leaves a <c>Dropped</c> row, which is an
/// answer too, so nothing is offered twice.
/// </para>
/// <para>
/// <strong>Not exported</strong>, deliberately, and it is the one table in this
/// channel where that is an argument rather than an omission. ADR 0064's list
/// of things that must not start a backfill is startup, upgrade and enabling
/// the switch; restoring a document is the same kind of event and the most
/// dangerous of them, because the Library a document is restored onto may not
/// be the Library it was written from. A Restore therefore arrives having
/// published exactly what <c>preview_publication</c> says it published and with
/// nobody's request outstanding — which is the state a person can ask out of in
/// one click, knowing what they are asking for. The progress is lost with it,
/// and the progress is the cheap half: the count that matters is recomputed
/// from the publication rows, which do cross.
/// </para>
/// </remarks>
public sealed class PreviewBackfillRow
{
    public Guid Id { get; set; }

    /// <summary>
    /// The prdb account the request was made under.
    /// </summary>
    /// <remarks>
    /// Part of the request rather than beside it, for the reason a publication
    /// row carries one: a backfill is a decision to put this Library's pictures
    /// in one account's public gallery, and a key change is a different person
    /// as far as anything published is concerned. A request of another account
    /// is given up rather than carried over.
    /// </remarks>
    public required string UserHash { get; set; }

    public PreviewBackfillState State { get; set; }

    /// <summary>
    /// How many eligible Library files there were when the request was made.
    /// </summary>
    /// <remarks>
    /// The number that was shown before the request was made, kept so that the
    /// progress a person reads afterwards is measured against what they agreed
    /// to rather than against a figure that moves under them. It is a snapshot
    /// and says so: a file filed while the request runs is taken up by Filing
    /// in the ordinary way, and the eligible count can therefore end up larger
    /// than this.
    /// </remarks>
    public int Selected { get; set; }

    /// <summary>How many files this request has taken up so far.</summary>
    public int TakenUp { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>When this request stopped taking up files, whyever it did.</summary>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>
    /// What a person reading the publication surface is owed: why the request
    /// stands where it does.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a code, and never read for control flow
    /// (ADR 0016, ADR 0043).
    /// </remarks>
    public string? Note { get; set; }
}
