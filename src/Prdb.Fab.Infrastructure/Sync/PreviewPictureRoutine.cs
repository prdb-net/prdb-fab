using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Reads one Video whole because somebody opened a Preview of it and the
/// Catalogue had no picture to show (ADR 0060).
/// </summary>
/// <remarks>
/// <para>
/// One request, one Video, and then the row retires. It is in the Sync lane
/// rather than the Bulk one for the reason ADR 0049 put manual search there:
/// somebody is waiting, and the Bulk lane is where work goes that nothing waits
/// on.
/// </para>
/// <para>
/// <strong>Not in the request's path.</strong> The sheet never waits on prdb —
/// it renders what it has, and the gallery fills in when this lands. ADR 0018
/// is intact: what that rule protects is the governor and the indexers' daily
/// budgets against a person pressing reload, and this is not a reload. ADR 0022
/// drew the line, and ADR 0054 walked it first.
/// </para>
/// </remarks>
public sealed class PreviewPictureRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    VideoDetails details,
    IRoutineStore routines) : IRoutine, ITargetedRoutine, IOneShot
{
    public const string RoutineName = "prdb.preview-pictures";

    internal const Lane ItsLane = Lane.Sync;

    public string Name => RoutineName;

    public Lane Lane => ItsLane;

    /// <summary>
    /// Short, because the row exists only while somebody is looking at the
    /// sheet that asked for it. It retires after one turn either way.
    /// </summary>
    public TimeSpan Cadence => TimeSpan.FromSeconds(15);

    /// <summary>
    /// None, ever. Every row of this routine is created by a person opening a
    /// Preview, and the row is the record of that — so there is nothing for the
    /// registrar to create at startup, and a row that survived a restart is
    /// already there to be found.
    /// </summary>
    public Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>
    /// Never, for the same reason. Asked only where there is no row, and where
    /// there is no row there is nobody waiting.
    /// </summary>
    public Task<bool> StartsAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var videoPrdbId))
        {
            await routines.RetireAsync(Name, target, cancellationToken);
            return RunResult.NothingToDo;
        }

        var video = await context.CatalogueVideos
            .Where(row => row.PrdbId == videoPrdbId)
            .Select(row => new { row.Id })
            .SingleOrDefaultAsync(cancellationToken);

        // Evicted out from under the sheet, or the images arrived by another
        // road while this waited its turn. Either way there is nothing to ask.
        if (video is null
            || await context.CatalogueImages.AnyAsync(row => row.VideoId == video.Id, cancellationToken))
        {
            await routines.RetireAsync(Name, target, cancellationToken);
            return RunResult.NothingToDo;
        }

        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No key is an installation that is not set up rather than a
            // request that failed. The row waits; nothing else can be done with
            // it here.
            return RunResult.NothingToDo;
        }

        var read = await prdb.AskAsync(
            apiKey,
            PrdbWork.Preview,
            (client, token) => client.Videos.Batch.PostAsync(
                new GetVideosByIdsRequest { Ids = [videoPrdbId] },
                cancellationToken: token),
            cancellationToken);

        var written = 0;

        foreach (var detail in read ?? [])
        {
            await details.WriteAsync(detail, cancellationToken);
            written++;
        }

        // Whatever came back, this Video has now been read in detail and its
        // LastReadAt says so — which is what stops the next Preview of it from
        // asking again, including when the answer was *prdb publishes none*.
        await routines.RetireAsync(Name, target, cancellationToken);

        return RunResult.Discovered(written, written, TimeSpan.Zero);
    }
}
