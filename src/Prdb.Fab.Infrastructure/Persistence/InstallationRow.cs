using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Acquisition;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>Installation</c>: one row, typed columns, and deliberately not
/// a key–value table — ADR 0020 admitted each of these against a test, and a
/// key–value table is an invitation to settings nobody argued and an untyped
/// backup.
/// </summary>
/// <remarks>
/// <para>
/// Only what the implemented slices need. The leftover switch arrives with the
/// filing schema whose later writer will consume it; the automation cap and
/// reporting switches arrive with the features that read them.
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

    /// <summary>
    /// Whether ADR 0010's downloader step was passed by deliberately. What it
    /// leaves behind is a Gap, and this is where the Gap is recorded: it says
    /// the consequence was named and accepted, which is a different thing from
    /// a step nobody has reached yet. Cleared by configuring SABnzbd, whenever
    /// that happens.
    /// </summary>
    public bool SabnzbdSkipped { get; set; }

    /// <summary>
    /// The same, for the search step. There is no indexer row to carry it —
    /// skipping is precisely the absence of one — which is why both of these
    /// sit on the installation rather than on the connection each names.
    /// </summary>
    public bool IndexersSkipped { get; set; }

    /// <summary>
    /// Since when the prdb plan has been too small to carry the schedule, and
    /// null while it carries it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0014's named condition, recorded here for the same reason the two
    /// skips above are: there is no row of its own to carry it. It is not a
    /// property of any one routine — every routine is running exactly as it
    /// should, more slowly — so a counter on a routine row could not say it,
    /// and three consecutive failures would be the wrong shape entirely.
    /// </para>
    /// <para>
    /// A stamp rather than a flag, because <em>since when</em> is what makes it
    /// worth reading: a plan that has not carried the schedule for a fortnight
    /// is a different sentence from one that stopped carrying it a minute ago,
    /// and ADR 0018's page is what turns either into words. Written once when
    /// the condition arrives and cleared once when it lifts, never on every run.
    /// </para>
    /// </remarks>
    public DateTimeOffset? PlanShortSince { get; set; }

    /// <summary>ADR 0008's per-Video Download budget.</summary>
    public int RetryBudget { get; set; } = 3;

    /// <summary>The highest named Quality a Catalogue-card Download may choose.</summary>
    public PreferredDownloadQuality PreferredDownloadQuality { get; set; } = PreferredDownloadQuality.P2160;

    /// <summary>ADR 0007's ceiling on unfinished automatic Downloads.</summary>
    public int AutomaticDownloadCap { get; set; } = 20;

    /// <summary>Whether filing may delete non-video leftovers after it succeeds.</summary>
    public bool DeleteLeftovers { get; set; }

    /// <summary>Whether held Wanted Videos may be reported as Fulfilments.</summary>
    public bool ReportFulfilments { get; set; } = true;

    /// <summary>Whether a person's Confirmed Assignments may be sent to prdb.</summary>
    public bool ReportConfirmedAssignments { get; set; } = true;

    /// <summary>
    /// The newest What's New row observed by a loaded page, as a stable tuple
    /// because several Videos may share prdb's creation timestamp.
    /// </summary>
    public DateTimeOffset? WhatsNewObservedAt { get; set; }

    public long? WhatsNewObservedVideoId { get; set; }
}
