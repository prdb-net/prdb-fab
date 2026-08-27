using Prdb.Fab.Core.Access;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>Installation</c>: one row, typed columns, and deliberately not
/// a key–value table — ADR 0020 admitted each of these against a test, and a
/// key–value table is an invitation to settings nobody argued and an untyped
/// backup.
/// </summary>
/// <remarks>
/// <para>
/// Only what this slice reads or writes. The retry budget, the automation cap,
/// the leftover switch and the two reporting switches are ADR 0033's too, and
/// arrive with the features that read them: a column nothing reads is a column
/// nothing tests.
/// </para>
/// <para>
/// The key is the constant <see cref="TheOnlyRow"/> rather than ADR 0033's
/// UUIDv7. That rule exists so a restored row carries its own identity and no
/// restore can depend on ordinal values; a table whose whole content is one row
/// has neither problem, and the constant is what lets the schema itself refuse a
/// second one.
/// </para>
/// </remarks>
public sealed class InstallationRow
{
    /// <summary>There is one installation, and this is its row.</summary>
    public const int TheOnlyRow = 1;

    public int Id { get; set; } = TheOnlyRow;

    /// <summary>
    /// ADR 0010's single secret, hashed with <c>PasswordHasher</c>. Null until
    /// it is set, which is the one condition ADR 0010 gates its two
    /// unauthenticated writes on — and the state <c>FAB_RESET_PASSWORD</c>
    /// returns the installation to.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>ADR 0037: stored as it was typed. There is nowhere to put a key.</summary>
    public string? PrdbApiKey { get; set; }

    /// <summary>
    /// Stable per prdb account, so a key entered later that belongs to a
    /// different one can be recognised (ADR 0010) and scopes what was reported
    /// (ADR 0019).
    /// </summary>
    public string? PrdbUserHash { get; set; }

    /// <summary>
    /// Absolute (ADR 0033). ADR 0020 calls this the one setting that is history
    /// rather than present, because filed paths are recorded against it.
    /// </summary>
    public string? LibraryRoot { get; set; }

    public string? SabnzbdUrl { get; set; }

    public string? SabnzbdApiKey { get; set; }

    /// <summary>Chosen from SABnzbd's own list rather than typed (ADR 0020).</summary>
    public string? SabnzbdCategory { get; set; }

    /// <summary>
    /// The path as SABnzbd reports it, and the path this container can open.
    /// Verified rather than collected (ADR 0010), and the download directory is
    /// derived from it rather than asked for a second time.
    /// </summary>
    public string? PathMappingFrom { get; set; }

    public string? PathMappingTo { get; set; }

    /// <summary>
    /// ADR 0010: the state is <em>which step is next</em>, so each step commits
    /// when it completes and a closed tab costs nothing.
    /// </summary>
    public OnboardingStep OnboardingStep { get; set; } = OnboardingStep.Password;
}
