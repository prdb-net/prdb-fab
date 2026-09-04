using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;
using Prdb.Sdk.Generated.Videos;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Reads one page of a requested Actor Catalogue Fill per turn.</summary>
public sealed class ActorVideoLoadRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    VideoDetails details,
    IRoutineStore routines,
    TimeProvider time) : IRoutine, ITargetedRoutine, IOneShot
{
    public const string RoutineName = "prdb.actor-videos";
    private const int PageSize = 100;
    private const int LastPage = ActorVideoLoads.Limit / PageSize;

    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        (await context.ActorVideoLoadStates
            .Where(row => row.CompletedAt == null)
            .Select(row => row.Actor!.PrdbId)
            .ToListAsync(cancellationToken))
        .Select(ActorVideoLoads.Target)
        .ToList();

    public Task<bool> StartsAsync(CancellationToken cancellationToken) =>
        context.ActorVideoLoadStates.AnyAsync(row => row.CompletedAt == null, cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var actorPrdbId)) return RunResult.NothingToDo;

        var state = await context.ActorVideoLoadStates.AsTracking()
            .Include(row => row.Actor)
            .SingleOrDefaultAsync(row => row.Actor != null && row.Actor.PrdbId == actorPrdbId, cancellationToken);
        if (state is null || state.CompletedAt is not null)
        {
            await routines.RetireAsync(Name, target, cancellationToken);
            return RunResult.NothingToDo;
        }

        var apiKey = await context.Installation.Select(row => row.PrdbApiKey).SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey)) return RunResult.NothingToDo;

        var answer = await prdb.AskAsync(
            apiKey,
            PrdbWork.WhatsNew,
            (client, token) => client.Videos.GetAsync(request =>
            {
                request.QueryParameters.ActorId = actorPrdbId;
                request.QueryParameters.SortBy = GetSortByQueryParameterType.ReleaseDate;
                request.QueryParameters.SortDirection = GetSortDirectionQueryParameterType.Desc;
                request.QueryParameters.Page = state.ResumePage;
                request.QueryParameters.PageSize = PageSize;
            }, token),
            cancellationToken);

        var ids = (answer?.Items ?? [])
            .Where(video => video.Id.HasValue)
            .Select(video => video.Id!.Value)
            .Distinct()
            .ToList();
        var written = 0;
        foreach (var batch in ids.Chunk(50))
        {
            var actorsVideos = await prdb.AskAsync(
                apiKey,
                PrdbWork.WhatsNew,
                (client, token) => client.Videos.Batch.PostAsync(
                    new GetVideosByIdsRequest { Ids = [.. batch.Select(id => (Guid?)id)] },
                    cancellationToken: token),
                cancellationToken);
            foreach (var video in actorsVideos ?? [])
            {
                await details.WriteAsync(video, cancellationToken);
                written++;
            }
        }

        var localVideoIds = await context.CatalogueVideos
            .Where(video => ids.Contains(video.PrdbId))
            .Select(video => video.Id)
            .ToListAsync(cancellationToken);
        var alreadyHeld = await context.ActorVideoLoadVideos
            .Where(row => row.ActorId == state.ActorId && localVideoIds.Contains(row.VideoId))
            .Select(row => row.VideoId)
            .ToListAsync(cancellationToken);
        foreach (var videoId in localVideoIds.Except(alreadyHeld))
        {
            context.ActorVideoLoadVideos.Add(new ActorVideoLoadVideoRow
            {
                ActorId = state.ActorId,
                VideoId = videoId,
                LoadedAt = state.RequestedAt,
            });
        }

        state.VideosSeen += ids.Count;
        var completed = ids.Count < PageSize || state.ResumePage >= LastPage;
        if (completed)
        {
            state.CompletedAt = time.GetUtcNow();
        }
        else
        {
            state.ResumePage++;
        }
        await context.SaveChangesAsync(cancellationToken);

        if (completed)
        {
            await routines.RetireAsync(Name, target, cancellationToken);
        }

        return RunResult.Discovered(ids.Count, written, completed ? Cadence : TimeSpan.Zero);
    }
}
