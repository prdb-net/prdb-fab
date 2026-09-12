using System.Globalization;

namespace Prdb.Fab.Core.Backup;

/// <summary>
/// What the Backup is called when it leaves the tool, and what it is called on
/// the way back in.
/// </summary>
/// <remarks>
/// Here rather than at the endpoint because the name is a property of the
/// document — the moment it was written is in it — and because the sentence the
/// export screen shows about the file has to agree with the file.
/// </remarks>
public static class BackupFile
{
    /// <summary>
    /// It is JSON and says so. ADR 0009 wants a person to be able to open it,
    /// and an invented media type is how a file ends up opened by nothing.
    /// </summary>
    public const string MediaType = "application/json";

    /// <summary>
    /// <c>prdb-fab-backup-20260912-143000.json</c>: sortable, unambiguous, and
    /// different for every export, so that a directory of them reads as a
    /// history rather than as one file overwritten.
    /// </summary>
    /// <remarks>
    /// UTC, because the alternative is a directory whose names jump backwards
    /// twice a year. The stamp is the document's own <c>writtenAt</c> rather
    /// than the clock at the time of naming, so the name and the envelope agree.
    /// </remarks>
    public static string Name(DateTimeOffset writtenAt) =>
        $"prdb-fab-backup-{writtenAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json";
}
