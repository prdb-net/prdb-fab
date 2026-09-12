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
    /// The first format. Raised when the document changes shape, never when the
    /// database does.
    /// </summary>
    public const int Version = 1;
}
