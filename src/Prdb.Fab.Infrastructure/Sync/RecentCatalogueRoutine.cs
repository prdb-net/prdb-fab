using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Repeatedly proves and fills prdb's Catalogue side of the Recent Window.
/// One page is one bounded turn; a durable page resumes after a restart.
/// </summary>
public sealed class RecentCatalogueRoutine(
    WhatsNew whatsNew,
    PrdbGateway prdb,
    VideoDetails details,
    EntryFiles entryFiles,
    CatalogueRows catalogue,
    CatalogueAvailability availability,
    VideoChangeCoverage coverage,
    FeedCursors cursors,
    VideoSyncGate sync,
    FabDbContext context,
    TimeProvider time,
    ILogger<RecentCatalogueRoutine> logger) : IRoutine
{
    public const string RoutineName = "prdb.recent-window";

    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        await sync.Gate.WaitAsync(cancellationToken);
        try { return await RunCoreAsync(cancellationToken); }
        finally { sync.Gate.Release(); }
    }

    private async Task<RunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey)) return RunResult.NothingToDo;

        var state = await context.RecentWindowState
            .AsTracking()
            .SingleAsync(row => row.Id == RecentWindowStateRow.TheOnlyRow, cancellationToken);
        if (state.BaselinePhase is "catch-up" or "live") return RunResult.NothingToDo;
        var now = time.GetUtcNow();
        var started = state.CataloguePassStartedAt ?? now;
        var page = Math.Max(1, state.CatalogueResumePage);

        if (state.CataloguePassStartedAt is null)
        {
            state.CataloguePassStartedAt = started;
            state.CatalogueOldestCreatedAt = null;
        }

        if (state.BaselinePhase != "compatibility" || state.BaselineBefore is null
            || state.CatalogueResumePage == 1 && state.BaselineBefore <= now - RecentWindow.CompleteEvery)
        {
            if (state.BaselinePhase != "scan")
            {
                state.BaselinePhase = "scan";
                state.CataloguePassStartedAt = now;
                state.BaselineFrom = RecentWindow.BeginsAt(now);
                state.BaselineBefore = now;
                state.BaselineChangesSince = null;
                state.BaselineCursor = null;
                state.BaselineExceptionalVideoId = null;
                state.BaselineExceptionalCursor = null;
                state.BaselinePages = 0;
                state.BaselineDocuments = 0;
                state.CatalogueOldestCreatedAt = null;
            }
            // The fixed scope survives a lost first response or quota deferral.
            await context.SaveChangesAsync(cancellationToken);
            try { return await BaselineAsync(apiKey, state, cancellationToken); }
            catch (ApiException unsupported) when (unsupported.ResponseStatusCode is 404 or 405 or 501)
            {
                state.BaselinePhase = "compatibility";
                state.CatalogueResumePage = 1;
                page = 1;
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        var read = await whatsNew.ReadAsync(apiKey, createdAfter: null, page, cancellationToken);
        var written = await whatsNew.FetchAsync(apiKey, read.Unknown, cancellationToken);

        state.CatalogueOldestCreatedAt = Oldest(state.CatalogueOldestCreatedAt, read.Oldest);
        var reachedBoundary = read.Returned < CatalogueRead.APage
            || read.Oldest is { } oldest && oldest <= RecentWindow.BeginsAt(started);

        TimeSpan dueIn;
        if (reachedBoundary)
        {
            state.CatalogueCompletedAt = now;
            state.CatalogueResumePage = 1;
            state.CataloguePassStartedAt = null;
            dueIn = RecentWindow.NextPassIn(started, now);
            logger.LogInformation(
                "The prdb Recent Window completed page {Page}; {Written} new Catalogue Video(s) were written.",
                page,
                written);
        }
        else
        {
            state.CatalogueResumePage = page + 1;
            dueIn = TimeSpan.Zero;
        }

        await context.SaveChangesAsync(cancellationToken);
        return RunResult.Discovered(read.Returned, written, dueIn);
    }

    private async Task<RunResult> BaselineAsync(string key, RecentWindowStateRow state, CancellationToken token)
    {
        if (state.BaselineExceptionalVideoId is { } exceptionalId)
        {
            VideoDetailDto? detail;
            try
            {
                detail = await prdb.AskAsync(key, PrdbWork.Repair,
                    (client, ct) => client.Videos[exceptionalId].GetAsync(cancellationToken: ct), token);
                if (detail?.Id != exceptionalId) throw new InvalidOperationException("prdb omitted an exceptional baseline document.");
            }
            catch (ApiException missing) when (missing.ResponseStatusCode == 404) { detail = null; }
            await using var applying = await context.Database.BeginTransactionAsync(token);
            if (detail is not null) await WriteAsync(detail, token);
            else
            {
                var id = await catalogue.VideoAsync(exceptionalId, null, null, token);
                var row = await context.CatalogueVideos.AsTracking().SingleAsync(v => v.Id == id, token);
                row.CreatedAtUtc = state.BaselineFrom!.Value;
                await context.SaveChangesAsync(token);
                await availability.UnavailableAsync(exceptionalId, null, null, null, token);
            }
            state.BaselineCursor = state.BaselineExceptionalCursor;
            state.BaselineExceptionalVideoId = null;
            state.BaselineExceptionalCursor = null;
            if (detail is not null) state.BaselineDocuments++;
            await context.SaveChangesAsync(token);
            await applying.CommitAsync(token);
            return RunResult.Handled(1, TimeSpan.Zero);
        }

        VideoBaselineResponse page;
        try
        {
            page = await prdb.AskAsync(key, PrdbWork.Repair,
                (client, ct) => client.Videos.Baseline.GetAsync(r =>
                {
                    r.QueryParameters.CreatedFrom = state.BaselineFrom;
                    r.QueryParameters.CreatedBefore = state.BaselineBefore;
                    r.QueryParameters.Cursor = state.BaselineCursor;
                    r.QueryParameters.PageSize = 1000;
                }, ct), token) ?? throw new InvalidOperationException("prdb omitted the baseline envelope.");
        }
        catch (BaselineDocumentTooLargeResponse large)
        {
            if (large.VideoId is not { } id || string.IsNullOrWhiteSpace(large.NextCursor)
                || large.NextCursor == state.BaselineCursor || large.ChangesSince is not { } since)
                throw new InvalidOperationException("prdb omitted an exceptional baseline continuation.");
            await using var saving = await context.Database.BeginTransactionAsync(token);
            await BoundAsync(state, since, token);
            state.BaselineExceptionalVideoId = id;
            state.BaselineExceptionalCursor = large.NextCursor;
            state.BaselinePages++;
            await context.SaveChangesAsync(token);
            await saving.CommitAsync(token);
            return RunResult.Handled(0, TimeSpan.Zero);
        }
        if (page.Items is null || page.HasMore is null || page.ChangesSince is not { } bound
            || page.HasMore == true && (string.IsNullOrWhiteSpace(page.NextCursor) || page.NextCursor == state.BaselineCursor)
            || page.Items.Any(v => v.Id is null || v.UpdatedAtUtc is null || v.CreatedAtUtc is null
                || v.CreatedAtUtc < state.BaselineFrom || v.CreatedAtUtc >= state.BaselineBefore))
            throw new InvalidOperationException("prdb returned an incomplete baseline page.");

        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await BoundAsync(state, bound, token);
        var written = 0;
        foreach (var video in page.Items)
        {
            if (await WriteAsync(video, token)) written++;
            state.CatalogueOldestCreatedAt = Oldest(state.CatalogueOldestCreatedAt, video.CreatedAtUtc);
        }
        state.BaselinePages++;
        state.BaselineDocuments += page.Items.Count;
        state.BaselineCursor = page.NextCursor;
        if (page.HasMore == false)
        {
            // Only a finished inventory may retire missing recent rows. Metadata,
            // pinned Library identities and older cache content remain intact.
            await context.CatalogueVideos.Where(v => v.CreatedAtUtc >= state.BaselineFrom
                    && v.CreatedAtUtc < state.BaselineBefore && v.ChangesBaselineAt != bound)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.Available, false)
                    .SetProperty(v => v.LastReadAt, time.GetUtcNow())
                    .SetProperty(v => v.ChangesBaselineAt, bound), token);
            state.BaselinePhase = "catch-up";
            await context.SaveChangesAsync(token);
            await coverage.CompleteBaselineAsync(token);
            // Inclusive server watermark, deliberately without a row tie ID.
            await cursors.SaveAsync(Feed.VideoChanges, FeedPosition.CaughtUpAt(bound), token);
            await context.Routines.Where(r => r.Name == VideoFeedRoutine.RoutineName)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.DueAt, time.GetUtcNow()), token);
        }
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return RunResult.Discovered(page.Items.Count, written, TimeSpan.Zero);
    }

    private async Task<bool> WriteAsync(VideoDetailDto detail, CancellationToken token)
    {
        var previous = await entryFiles.ChosenImageIdAsync(detail.Id!.Value, token);
        var arrived = await details.WriteAsync(detail, token);
        var current = await entryFiles.ChosenImageIdAsync(detail.Id.Value, token);
        await entryFiles.RefreshAsync(detail.Id.Value, previous != current, token);
        return arrived;
    }

    private async Task BoundAsync(RecentWindowStateRow state, DateTimeOffset bound, CancellationToken token)
    {
        if (state.BaselineChangesSince is { } held && held != bound)
            throw new InvalidOperationException("prdb changed the baseline watermark during a scan.");
        if (state.BaselineChangesSince is null)
        {
            state.BaselineChangesSince = bound;
            await coverage.StartAsync(bound, token);
        }
    }

    private static DateTimeOffset? Oldest(DateTimeOffset? held, DateTimeOffset? read) =>
        held is null ? read : read is null || held <= read ? held : read;
}
