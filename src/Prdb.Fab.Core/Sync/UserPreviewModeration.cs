namespace Prdb.Fab.Core.Sync;

/// <summary>
/// Whether prdb is currently showing a user preview — decided by which endpoint
/// delivered the row rather than by parsing a string prdb never documented.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The vocabulary is not in the API document.</strong>
/// <c>moderationStatus</c> and <c>moderationVisibility</c> are open strings with
/// no enumeration and no listed values, which rules out the obvious design: an
/// allow-list of the words that mean visible would be a guess, and a wrong guess
/// in one direction shows nothing forever while a wrong guess in the other shows
/// a picture a moderator removed.
/// </para>
/// <para>
/// <strong>What the document does say is which endpoints filter.</strong>
/// <c>GET /videos/{id}/user-images</c>, <c>GET /video-user-images/by-os-hash</c>
/// and <c>GET /video-user-images/{id}</c> each return only publicly visible
/// rows and exclude hidden, denied and soft-deleted ones. So a row that arrives
/// through one of those is visible <em>by construction</em>, whatever it calls
/// itself — and the pair of strings it carried while it was visible is a
/// signature that can be compared against later.
/// </para>
/// <para>
/// The change feed is the other half. It carries every row, visible or not, so
/// a page reporting a signature that differs from the one a filtered endpoint
/// last delivered is a moderation change away from visible, and one reporting
/// the same signature again is a restoration. A row this installation has only
/// ever seen on the feed has no signature to compare against and is not shown,
/// which is the conservative direction and self-correcting: the next snapshot
/// of its Video either includes it or does not.
/// </para>
/// <para>
/// A false negative costs a picture not shown until the next snapshot. A false
/// positive would be this tool publishing what prdb withdrew, which is the one
/// outcome ADR 0061 refuses to risk.
/// </para>
/// </remarks>
public static class UserPreviewModeration
{
    /// <summary>
    /// The comparable form of one moderation state: the two strings prdb
    /// reports, in one value.
    /// </summary>
    /// <remarks>
    /// Case-folded and separated by a character neither field can contain, so
    /// that a service which changes the casing of its own vocabulary does not
    /// read as a withdrawal. Null becomes the empty string rather than an
    /// absence, because a row prdb answered with no status at all is a state
    /// like any other and has to compare equal to itself.
    /// </remarks>
    public static string Signature(string? status, string? visibility) =>
        $"{(status ?? string.Empty).Trim().ToLowerInvariant()}|"
        + $"{(visibility ?? string.Empty).Trim().ToLowerInvariant()}";

    /// <summary>
    /// Whether a row in this state may be served, shown, cached or written into
    /// the Library.
    /// </summary>
    /// <param name="deleted">prdb's <c>isDeleted</c>, which overrides everything.</param>
    /// <param name="signature">The state prdb reports now.</param>
    /// <param name="shownUnder">
    /// The state the row carried the last time a filtered endpoint delivered it,
    /// or null where none ever has.
    /// </param>
    /// <remarks>
    /// One question with one answer, asked by the gallery, the asset cache, the
    /// Library reconciliation and the Identification evidence alike — so that a
    /// withdrawal cannot stop one of them and not another.
    /// </remarks>
    public static bool Shows(bool deleted, string signature, string? shownUnder) =>
        !deleted
        && shownUnder is not null
        && string.Equals(signature, shownUnder, StringComparison.Ordinal);
}
