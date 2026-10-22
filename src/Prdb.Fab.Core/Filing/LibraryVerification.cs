namespace Prdb.Fab.Core.Filing;

/// <summary>
/// What the background pass found when it went looking for one Video File.
/// </summary>
/// <remarks>
/// ADR 0009 makes this a background pass rather than part of a Restore —
/// <em>a restore that hashes a whole library before it finishes is a restore
/// people interrupt</em> — and makes what it finds a count on the status page
/// rather than an act. Nothing here deletes, re-fetches or retracts anything.
/// </remarks>
public enum LibraryVerification
{
    /// <summary>
    /// The file is where the record says, and it is the file the record means.
    /// </summary>
    /// <remarks>
    /// Also the answer where nothing was ever hashed: the osHash column is
    /// nullable, and for such a row the presence of a file at the recorded path
    /// is the whole of what the record ever claimed. Saying <em>mismatched</em>
    /// about a claim that was never made would put a Gap on the status page
    /// that no act could ever clear.
    /// </remarks>
    Confirmed,

    /// <summary>
    /// There is nothing at the recorded path. Under ADR 0009 this is a count
    /// and not a conclusion: a mis-mounted library looks exactly like this, and
    /// under ADR 0007 treating it as an empty library would be a standing
    /// instruction to download the collection again.
    /// </summary>
    Missing,

    /// <summary>
    /// A file is there and it is a different file. Kept apart from
    /// <see cref="Missing"/> because the two have different causes — the second
    /// is a mount, the first is something having written into the library,
    /// which ADR 0027 says only this tool does.
    /// </summary>
    Mismatched,

    /// <summary>
    /// A file is there and could not be read. A permission, a stalled network
    /// mount, a device error — none of which is evidence about the content, so
    /// it counts with the unconfirmed rather than with the missing.
    /// </summary>
    Unreadable,
}

/// <summary>The rule the pass applies, and the sentence each answer reads as.</summary>
public static class LibraryVerifications
{
    /// <summary>
    /// What one Video File's reading means.
    /// </summary>
    /// <param name="recorded">The osHash the record carries, or null where none was.</param>
    /// <param name="computed">
    /// What was read from the file just now: null where nothing is there, empty
    /// where something is there and could not be read.
    /// </param>
    public static LibraryVerification Of(string? recorded, string? computed) => computed switch
    {
        null => LibraryVerification.Missing,
        "" => LibraryVerification.Unreadable,
        _ when string.IsNullOrEmpty(recorded) => LibraryVerification.Confirmed,
        _ => string.Equals(recorded, computed, StringComparison.OrdinalIgnoreCase)
            ? LibraryVerification.Confirmed
            : LibraryVerification.Mismatched,
    };

    /// <summary>Whether this answer leaves the Entry counted as unconfirmed.</summary>
    /// <remarks>
    /// ADR 0009: <em>until an entry is verified it counts as held</em>, and so
    /// does one the pass could not confirm. Held is what
    /// <c>AutomaticEligibility</c> reads off the existence of the Library
    /// Entry, so what this has to be true of is the counting and the filter —
    /// the row stays, which is what keeps automation off it.
    /// </remarks>
    public static bool IsUnconfirmed(LibraryVerification verification) =>
        verification is not LibraryVerification.Confirmed;

    /// <summary>ADR 0043: the sentences are values, so a test can read them.</summary>
    public static string Sentence(LibraryVerification verification) => verification switch
    {
        LibraryVerification.Confirmed =>
            "The file was verified at its recorded Library path.",

        LibraryVerification.Missing =>
            "No file was found at the recorded Library path. Check the storage mount. The Library record "
            + "is kept, and this does not trigger a replacement Download.",

        LibraryVerification.Mismatched =>
            "The file at the recorded Library path differs from the filed file. Check whether it was "
            + "replaced or modified outside fab.",

        LibraryVerification.Unreadable =>
            "The file exists at the recorded Library path but cannot be read. Check file permissions and "
            + "the storage mount.",

        _ => throw new ArgumentOutOfRangeException(nameof(verification)),
    };
}
