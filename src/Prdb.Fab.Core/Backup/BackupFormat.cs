namespace Prdb.Fab.Core.Backup;

/// <summary>
/// What version of the Backup document this build writes.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0009 keeps this apart from the EF schema version on purpose: migrations
/// run at startup and there are several per release, so a format tied to them
/// would break the document every time a column moved. What breaks the format is
/// a change to the document, which is a change to this file.
/// </para>
/// <para>
/// The independence is mechanical rather than promised. This constant lives in
/// <c>Core</c>, which ADR 0035 forbids from referencing anything at all — there
/// is no EF Core here to read a migration id from, and
/// <c>ArchitectureTests.Core_declares_no_dependencies</c> is what keeps it that
/// way.
/// </para>
/// </remarks>
public static class BackupFormat
{
    /// <summary>
    /// Raised when the document changes shape, never when the database does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>4</strong> adds ADR 0064's <c>previewPublications</c>: which
    /// generated previews this installation has submitted to prdb, and under
    /// which account. It is the one section that cannot be fetched again from
    /// anywhere — a submission in moderation is invisible to every read
    /// endpoint prdb offers — and a document written by version 3 has no
    /// section for it, which is the same state as an installation that has
    /// published nothing.
    /// </para>
    /// <para>
    /// <strong>3</strong> adds ADR 0064's two installation fields: the third
    /// Reporting switch and the stamp saying the explanation behind it has been
    /// in front of somebody. A document written by version 2 has neither, and
    /// an absent boolean would deserialise as <c>false</c> — which is neither
    /// the shipped default nor a decision anybody took — so this one needs a
    /// step rather than only tolerating the absence.
    /// </para>
    /// <para>
    /// <strong>2</strong> adds ADR 0062's <c>identificationFlags</c>: filed
    /// Video Files whose Video was named from evidence prdb has since
    /// withdrawn. A document written by version 1 still restores — the section
    /// is simply absent, which is the same state as an installation that has
    /// never flagged anything — so nothing here refuses one.
    /// </para>
    /// </remarks>
    public const int Version = 4;
}
