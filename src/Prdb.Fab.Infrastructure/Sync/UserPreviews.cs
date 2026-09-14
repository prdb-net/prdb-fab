using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Who wants a Video's user previews, and what that costs prdb (ADR 0061).
/// </summary>
/// <remarks>
/// <para>
/// Two acts create Interest and nothing else does: a person opening a Preview,
/// and a Video File being filed. Both take the same door — an explicit act, a
/// durable routine row, and a caller that does not wait — which is ADR 0060's
/// shape applied to a second population.
/// </para>
/// <para>
/// <strong>An ordinary GET never arrives here.</strong> Reading a Preview reads
/// local rows; asking for them is a request of its own. A GET that scheduled
/// work as a side effect would make ADR 0018's rule — refreshing never causes
/// work — a rule the code contradicts in the one place it is easiest to read.
/// </para>
/// <para>
/// <strong>Deduplication is the routine row, and freshness is the interest
/// row.</strong> The first stops a sheet opened a hundred times from asking a
/// hundred times, and survives a restart. The second stops the answer
/// <em>none</em> — the ordinary answer — from being asked for again inside a
/// week.
/// </para>
/// </remarks>
public sealed class UserPreviews(FabDbContext context, TimeProvider time)
{
    /// <summary>
    /// Registers interest in a Video's user previews and schedules the read if
    /// one is due.
    /// </summary>
    public async Task<UserPreviewAsk> AskAsync(
        Guid videoPrdbId,
        UserPreviewDemand demand,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var interest = await context.UserPreviewInterests
            .AsTracking()
            .SingleOrDefaultAsync(row => row.VideoPrdbId == videoPrdbId, cancellationToken);

        if (interest is null)
        {
            interest = new UserPreviewInterestRow { VideoPrdbId = videoPrdbId };
            context.UserPreviewInterests.Add(interest);
        }

        interest.TouchedAt = now;

        // Once a filed file is among the reasons it goes on being one: a file
        // on disk outlives a glance at a sheet, and the hash bindings read for
        // it are ADR 0062's evidence for as long as it is there.
        interest.ForTheLibrary |= demand == UserPreviewDemand.Library;

        var fresh = interest.LastReadAt is { } last && now - last < UserPreviewContract.Freshness;

        if (fresh)
        {
            await context.SaveChangesAsync(cancellationToken);

            return new(UserPreviewOutcome.Fresh);
        }

        var name = UserPreviewReadRoutine.NameFor(demand);
        var target = Target(videoPrdbId);

        // Either routine's row is an outstanding read of the same list, so a
        // Preview opened over a Video the Library is already enriching does not
        // schedule a second request. The interactive row is the one that wins a
        // race: it is the one somebody is sitting in front of.
        var pending = await context.Routines
            .AsTracking()
            .Where(row => UserPreviewReadRoutine.Names.Contains(row.Name) && row.Target == target)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            context.Routines.Add(new RoutineRow
            {
                Name = name,
                Target = target,
                Lane = UserPreviewReadRoutine.LaneFor(demand),
                DueAt = now,
            });
        }
        else if (demand == UserPreviewDemand.Preview
                 && pending.All(row => row.Name != PreviewUserPreviewRoutine.RoutineName))
        {
            // Somebody is now waiting on work that was queued behind the Bulk
            // lane. Moving the row rather than adding one keeps it deduplicated
            // and keeps the restart-safety the row is there for.
            var row = pending[0];

            row.Name = PreviewUserPreviewRoutine.RoutineName;
            row.Lane = PreviewUserPreviewRoutine.ItsLane;
            row.DueAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        return new(UserPreviewOutcome.Asked);
    }

    /// <summary>
    /// Whether a read of this Video's user previews is outstanding.
    /// </summary>
    /// <remarks>
    /// The routine row is the record of it, so there is no second place for it
    /// to be true (ADR 0033). What it is for is a gallery that can say
    /// something is being done rather than showing an empty strip.
    /// </remarks>
    public Task<bool> ComingAsync(Guid videoPrdbId, CancellationToken cancellationToken)
    {
        var target = Target(videoPrdbId);

        return context.Routines.AnyAsync(
            row => UserPreviewReadRoutine.Names.Contains(row.Name) && row.Target == target,
            cancellationToken);
    }

    /// <summary>
    /// The user previews this installation may show for a Video, in prdb's own
    /// order.
    /// </summary>
    public Task<List<UserPreviewRow>> OfAsync(Guid videoPrdbId, CancellationToken cancellationToken) =>
        context.UserPreviews
            .AsNoTracking()
            .Where(row => row.VideoPrdbId == videoPrdbId && row.Shown)
            .OrderBy(row => row.DisplayOrder)
            .ThenBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.PrdbId)
            .ToListAsync(cancellationToken);

    /// <summary>Records that prdb has answered for this Video, whatever it said.</summary>
    public Task ReadAsync(Guid videoPrdbId, CancellationToken cancellationToken) =>
        context.UserPreviewInterests
            .Where(row => row.VideoPrdbId == videoPrdbId)
            .ExecuteUpdateAsync(
                row => row.SetProperty(interest => interest.LastReadAt, time.GetUtcNow()),
                cancellationToken);

    /// <summary>
    /// Whether anything at all is interested, which is what makes the change
    /// feed due (ADR 0032).
    /// </summary>
    /// <remarks>
    /// Rows without an interest count too. An interest that has just expired
    /// leaves previews behind for exactly one pass, and that pass is the one
    /// that removes them — a work set that stopped being not-empty one step too
    /// early would leave them forever.
    /// </remarks>
    public async Task<bool> AnythingFollowedAsync(CancellationToken cancellationToken) =>
        await context.UserPreviewInterests.AnyAsync(cancellationToken)
        || await context.UserPreviews.AnyAsync(cancellationToken);

    /// <summary>
    /// Drops the interest nothing has touched for
    /// <see cref="UserPreviewContract.InterestExpiry"/>, and the previews left
    /// with no interest at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bounded per pass, like everything else that sweeps: a routine that
    /// drained an arbitrarily large backlog would hold its lane for as long as
    /// the backlog lasted (ADR 0032).
    /// </para>
    /// <para>
    /// <strong>A filed file's interest does not expire.</strong> A filed Video
    /// File goes on being a filed Video File, and the bindings read for it are
    /// what ADR 0062 names a later arrival from — including a withdrawal, which
    /// has to reach them for as long as the file is there. What expires is a
    /// browse: somebody looked at a Video three months ago.
    /// </para>
    /// </remarks>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken)
    {
        const int ABatch = 200;

        var stale = time.GetUtcNow() - UserPreviewContract.InterestExpiry;

        var expiring = await context.UserPreviewInterests
            .Where(row => !row.ForTheLibrary && row.TouchedAt < stale)
            .OrderBy(row => row.TouchedAt)
            .Take(ABatch)
            .Select(row => row.VideoPrdbId)
            .ToListAsync(cancellationToken);

        if (expiring.Count > 0)
        {
            await context.UserPreviewInterests
                .Where(row => expiring.Contains(row.VideoPrdbId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Everything left over: previews of a Video nothing is interested in
        // any more, and previews the feed brought for a Video whose interest
        // went while the page was in flight. The bytes are found by the sweep,
        // which reads the disk rather than the table.
        var orphans = await context.UserPreviews
            .Where(row => row.VideoPrdbId == null
                || !context.UserPreviewInterests.Any(interest =>
                    interest.VideoPrdbId == row.VideoPrdbId))
            .OrderBy(row => row.Id)
            .Take(ABatch)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        if (orphans.Count > 0)
        {
            await context.UserPreviews
                .Where(row => orphans.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return expiring.Count + orphans.Count;
    }

    /// <summary>The routine row's target: one Video, spelled the one way.</summary>
    public static string Target(Guid videoPrdbId) => videoPrdbId.ToString("D");
}

/// <summary>Who is asking, which decides the lane and the precedence.</summary>
public enum UserPreviewDemand
{
    /// <summary>
    /// A person opened a Preview. ADR 0061 gives this ADR 0060's precedence,
    /// because it is the same act and the same waiting person.
    /// </summary>
    Preview,

    /// <summary>
    /// A Video File was filed. Background work nothing is sitting in front of,
    /// and the input to ADR 0062's evidence — which is what makes the next
    /// arrival of the same Video nameable without asking prdb again.
    /// </summary>
    Library,
}

/// <summary>What became of an ask.</summary>
public enum UserPreviewOutcome
{
    /// <summary>A read is scheduled, or one already was.</summary>
    Asked,

    /// <summary>
    /// prdb answered for this Video inside the freshness window, so its answer
    /// — including <em>none</em> — still stands.
    /// </summary>
    Fresh,
}

/// <summary>ADR 0040's verdict: a refusal is an outcome rather than an error.</summary>
public sealed record UserPreviewAsk(UserPreviewOutcome Outcome);
