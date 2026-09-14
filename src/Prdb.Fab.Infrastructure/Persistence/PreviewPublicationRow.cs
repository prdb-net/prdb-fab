using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0064's <c>preview_publication</c>: one generated Sprite Sheet this
/// installation owes prdb, or has made, or has finished with.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One row per file, hash and output version</strong> — the unique key
/// below — because that is what ADR 0064 means by one deterministic output per
/// eligible file. Two Library entries holding the same bytes are the same
/// publication, and the second filing finds the first row rather than making a
/// second picture of the same file.
/// </para>
/// <para>
/// <strong>It carries the account it was made under</strong>, as a Fulfilment
/// and a Confirmed Assignment do, and for the stronger version of their reason:
/// a publication goes out under somebody's key and into a public gallery, so
/// sending one account's queue under another's is the failure this prevents.
/// The account is part of the key rather than a column beside it, so a second
/// account starts having published nothing.
/// </para>
/// <para>
/// <strong>Exported</strong>, which is what it became once there was a delivery
/// to record. What earns the boundary is <em>which submission was accepted</em>:
/// that cannot be fetched again — a row in moderation is invisible to every
/// read endpoint prdb offers — so a Restore without it would republish a whole
/// Library's worth of previews as though none had ever been sent. The generated
/// bytes do not cross with it. They are disposable by construction and
/// remakeable from the file and the output version, so the four columns
/// describing them and the two counters beside them stay behind, each named in
/// <c>BackupSections.Omitted</c> with the reason.
/// </para>
/// </remarks>
public sealed class PreviewPublicationRow
{
    /// <summary>
    /// This publication's own id, which is also what its two files are named.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The Video File the sheet is made from, as the Library knows it.
    /// </summary>
    /// <remarks>
    /// Where to find the bytes, and nothing more: the file may be moved,
    /// replaced or deleted between the intent and the decode, and the hash
    /// below is what says whether what is there now is still what was promised.
    /// </remarks>
    public Guid VideoFileId { get; set; }

    /// <summary>The prdb Video the file is filed under.</summary>
    public Guid VideoPrdbId { get; set; }

    /// <summary>
    /// The osHash the Probe recorded, in the spelling this tool compares by.
    /// </summary>
    public required string OsHash { get; set; }

    /// <summary>The prdb account this publication belongs to.</summary>
    public required string UserHash { get; set; }

    /// <summary>
    /// The Library backfill that took this file up, or null where Filing did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things read it, and neither is about the publication itself. A
    /// person pausing or cancelling a request needs to know which rows the
    /// request selected, and the generating and delivering routines need to
    /// know which rows are somebody's backlog rather than the file that has
    /// just arrived — because ADR 0064's automatic scope must not queue behind
    /// five thousand of them.
    /// </para>
    /// <para>
    /// <strong>It stays behind at the Backup boundary</strong>, as
    /// <see cref="PreviewBackfillRow"/> does: it names a request that is not in
    /// the document, and a row restored without it is one Filing could have
    /// written, which is exactly what it is once the request is gone.
    /// </para>
    /// </remarks>
    public Guid? BackfillId { get; set; }

    /// <summary>
    /// Which generation of this tool's output this is
    /// (<see cref="PreviewPublicationContract.OutputVersion"/>).
    /// </summary>
    public int OutputVersion { get; set; }

    public PreviewPublicationState State { get; set; }

    /// <summary>
    /// What a person reading the publication surface is owed: why this row
    /// stopped where it did.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a code, and never read for control flow
    /// (ADR 0016, ADR 0043). The state says what happened; this says what it
    /// was about.
    /// </remarks>
    public string? Note { get; set; }

    /// <summary>How many tiles the generated sheet carries, once there is one.</summary>
    public int? Tiles { get; set; }

    public int? Columns { get; set; }

    public int? Rows { get; set; }

    /// <summary>What the generated sheet weighs, once there is one.</summary>
    public long? SheetBytes { get; set; }

    /// <summary>
    /// How many decodes have been attempted and not finished.
    /// </summary>
    /// <remarks>
    /// The counter <see cref="PreviewPublicationContract.MostAttempts"/> bounds.
    /// It counts failures rather than runs: a successful generation never reads
    /// it again, and a deferral never increments it.
    /// </remarks>
    public int Attempts { get; set; }

    public DateTimeOffset IntendedAt { get; set; }

    /// <summary>
    /// When the validated pair was committed to disk, which is what makes this
    /// row something to send.
    /// </summary>
    public DateTimeOffset? GeneratedAt { get; set; }

    /// <summary>When this row reached a state nothing moves it out of.</summary>
    /// <remarks>
    /// Written for every ending, <see cref="PreviewPublicationState.Uncertain"/>
    /// included — which is the one a person can still move, so what this says
    /// is when the answer stopped being owed rather than when the row became
    /// immutable.
    /// </remarks>
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>
    /// prdb's <c>videoUserImageId</c> for the accepted submission, or null
    /// where nothing has been accepted.
    /// </summary>
    /// <remarks>
    /// The one thing in this table that cannot be worked out again from
    /// anything local, and the reason the row is exported at all: it names a
    /// picture this installation put in a public gallery and cannot ask about
    /// until moderation has made it visible.
    /// </remarks>
    public Guid? PrdbImageId { get; set; }

    /// <summary>prdb's <c>moderationTargetId</c> for the accepted submission.</summary>
    public Guid? ModerationTargetId { get; set; }

    /// <summary>
    /// The moderation signature the submission entered under, as the <c>201</c>
    /// reported it.
    /// </summary>
    /// <remarks>
    /// Quoted, never parsed. ADR 0061 established that prdb documents no
    /// vocabulary for its two moderation strings, and this is kept for the
    /// reason that population keeps <c>ShownUnder</c>: so that a later change
    /// is recognisable as a change rather than read as a verdict.
    /// </remarks>
    public string? SubmittedUnder { get; set; }
}
