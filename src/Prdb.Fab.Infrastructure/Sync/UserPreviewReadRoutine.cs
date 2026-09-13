using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Reads one Video's user previews because somebody opened a Preview of it or
/// filed a file of it (ADR 0061).
/// </summary>
/// <remarks>
/// <para>
/// One request, one Video, and then the row retires — ADR 0060's shape, applied
/// to the other population. What differs between the two subclasses below is
/// which lane turns it and which share of the budget it may spend, and nothing
/// else: the same list, written the same way.
/// </para>
/// <para>
/// <strong>It establishes the feed's position before it reads.</strong> The
/// change feed is what carries a later withdrawal, and it starts at prdb's own
/// clock rather than at the beginning of history. Taking that clock
/// <em>after</em> a snapshot would leave every change between the two in a gap
/// neither covers — permanently, and invisibly, because a withdrawal that falls
/// into it is simply never heard about. So the clock is taken first, in a
/// one-row request whose rows are thrown away, and only once per installation.
/// </para>
/// </remarks>
public abstract class UserPreviewReadRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    UserPreviews previews,
    UserPreviewWrites writes,
    UserPreviewFeed feed,
    FeedCursors cursors,
    IRoutineStore routines) : IRoutine, ITargetedRoutine, IOneShot
{
    /// <summary>
    /// Both routine names, for the queries that ask whether a read of some kind
    /// is outstanding.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
        [PreviewUserPreviewRoutine.RoutineName, LibraryUserPreviewRoutine.RoutineName];

    public abstract string Name { get; }

    public abstract Lane Lane { get; }

    public abstract TimeSpan Cadence { get; }

    /// <summary>Where in ADR 0014's order this routine's one request stands.</summary>
    protected abstract PrdbWork Work { get; }

    public static string NameFor(UserPreviewDemand demand) => demand == UserPreviewDemand.Preview
        ? PreviewUserPreviewRoutine.RoutineName
        : LibraryUserPreviewRoutine.RoutineName;

    public static Lane LaneFor(UserPreviewDemand demand) => demand == UserPreviewDemand.Preview
        ? PreviewUserPreviewRoutine.ItsLane
        : LibraryUserPreviewRoutine.ItsLane;

    /// <summary>
    /// None, ever. Every row of these routines is created by somebody opening a
    /// Preview or by a file being filed, and the row is the record of that — so
    /// there is nothing for the registrar to create at startup, and a row that
    /// survived a restart is already there to be found.
    /// </summary>
    public Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>Never, for the same reason.</summary>
    public Task<bool> StartsAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var videoPrdbId))
        {
            await routines.RetireAsync(Name, target, cancellationToken);

            return RunResult.NothingToDo;
        }

        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Not an installation that failed; one that has not been set up.
            // The row waits.
            return RunResult.NothingToDo;
        }

        // Nothing durable is written before this, so a deferral leaves every
        // row byte-for-byte as it was — including the interest, which still
        // says the read is due.
        if (!await cursors.StartedAsync(Feed.VideoUserImages, cancellationToken))
        {
            await StartTheFeedAsync(apiKey, cancellationToken);
        }

        var answer = await prdb.AskAsync(
            apiKey,
            Work,
            (client, token) => client.Videos[videoPrdbId].UserImages.GetAsync(cancellationToken: token),
            cancellationToken);

        var applied = await writes.SnapshotAsync(videoPrdbId, answer ?? [], cancellationToken);

        // Whatever came back, prdb has now answered for this Video — which is
        // what stops the next Preview of it from asking again, including when
        // the answer was none.
        await previews.ReadAsync(videoPrdbId, cancellationToken);
        await routines.RetireAsync(Name, target, cancellationToken);

        return RunResult.Discovered(applied, applied, TimeSpan.Zero);
    }

    /// <summary>
    /// Puts the change feed at prdb's own clock, before anything is written.
    /// </summary>
    private async Task StartTheFeedAsync(string apiKey, CancellationToken cancellationToken)
    {
        var page = await feed.ReadAsync(apiKey, FeedPosition.TheBeginning, pageSize: 1, cancellationToken);

        if (page.ServerTimeUtc is { } clock)
        {
            await cursors.SaveAsync(Feed.VideoUserImages, FeedPosition.CaughtUpAt(clock), cancellationToken);
        }

        // No clock is prdb not being itself. The snapshot still goes ahead —
        // holding it back would mean an empty gallery over a service that is
        // answering — and the next read tries the handoff again. What is lost
        // in the meantime is a withdrawal arriving before the feed starts, and
        // the next snapshot of the Video is what catches that.
    }
}

/// <summary>
/// The interactive read: somebody has a Preview open and is looking at an empty
/// strip.
/// </summary>
/// <remarks>
/// In the Sync lane rather than the Bulk one for the reason ADR 0049 put manual
/// search there — somebody is waiting, and the Bulk lane is where work goes that
/// nothing waits on — and on ADR 0060's <see cref="PrdbWork.Preview"/> share for
/// the reason ADR 0061 gives: it is the same act, the same person and the same
/// sheet.
/// </remarks>
public sealed class PreviewUserPreviewRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    UserPreviews previews,
    UserPreviewWrites writes,
    UserPreviewFeed feed,
    FeedCursors cursors,
    IRoutineStore routines)
    : UserPreviewReadRoutine(context, prdb, previews, writes, feed, cursors, routines), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.user-previews.preview";

    internal const Lane ItsLane = Lane.Sync;

    public override string Name => RoutineName;

    public override Lane Lane => ItsLane;

    /// <summary>
    /// Short, because the row exists only while somebody is looking at the
    /// sheet that asked for it. It retires after one turn either way.
    /// </summary>
    public override TimeSpan Cadence => TimeSpan.FromSeconds(15);

    public PrdbWork Spends => PrdbWork.Preview;

    protected override PrdbWork Work => PrdbWork.Preview;
}

/// <summary>
/// The enrichment read: a Video File has been filed, and what its Library entry
/// and ADR 0062's evidence need is the list of previews made from files of that
/// Video.
/// </summary>
/// <remarks>
/// The Bulk lane, because nothing waits on it: filing has already finished, and
/// ADR 0061 requires that it never waited. A minute's cadence rather than
/// fifteen seconds for the same reason — a Library preview arriving a minute
/// later costs nothing at all.
/// </remarks>
public sealed class LibraryUserPreviewRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    UserPreviews previews,
    UserPreviewWrites writes,
    UserPreviewFeed feed,
    FeedCursors cursors,
    IRoutineStore routines)
    : UserPreviewReadRoutine(context, prdb, previews, writes, feed, cursors, routines), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.user-previews.library";

    internal const Lane ItsLane = Lane.Bulk;

    public override string Name => RoutineName;

    public override Lane Lane => ItsLane;

    public override TimeSpan Cadence => TimeSpan.FromMinutes(1);

    public PrdbWork Spends => PrdbWork.UserPreviews;

    protected override PrdbWork Work => PrdbWork.UserPreviews;
}
