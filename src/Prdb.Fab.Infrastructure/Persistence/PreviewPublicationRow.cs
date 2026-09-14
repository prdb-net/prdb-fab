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
/// <strong>Not exported, for now.</strong> ADR 0033's boundary runs between
/// tables and ADR 0064 puts this one on the exported side — but for what it
/// will carry once there is a delivery to record: <em>which submission was
/// accepted</em>, which cannot be fetched again and without which a Restore
/// would republish everything ever sent. What the table holds until then is an
/// intent and some disposable bytes, and an intent that does not survive a
/// Restore costs one preview that was never sent. The row crosses the boundary
/// with the history it is there to protect.
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
    public DateTimeOffset? SettledAt { get; set; }
}
