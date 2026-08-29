using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// <c>GET /wanted-videos/changes</c>: the list ADR 0007 makes the only source
/// of intent.
/// </summary>
/// <remarks>
/// Account-scoped, and so is its cursor: a position into another account's list
/// would resume a walk over answers this installation can no longer see. What a
/// key from a different account takes with it is ticket 11's list of deletes.
/// <para>
/// Read here and written nowhere. <c>CONTEXT.md</c> defines a Wanted Video as
/// one the user has marked <em>in prdb</em>, so wanting happens there and this
/// feed is how it arrives.
/// </para>
/// </remarks>
public sealed class WantedVideoFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.WantedVideos;

    public override PrdbWork Work => PrdbWork.UserFeeds;

    public override async Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(
            apiKey,
            Work,
            (client, token) => client.WantedVideos.Changes.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = pageSize;
                    request.QueryParameters.Since = from.Since;
                    request.QueryParameters.SinceId = from.SinceId;
                },
                token),
            cancellationToken);

        if (page is null)
        {
            return FeedPage.Nothing;
        }

        return new FeedPage(
            await ApplyAsync(page.Items, cancellationToken),
            page.HasMore ?? false,
            page.NextCursor?.UpdatedAtUtc,
            page.NextCursor?.Id,
            page.ServerTimeUtc);
    }

    private async Task<int> ApplyAsync(List<WantedVideoChangeDto>? items, CancellationToken cancellationToken)
    {
        var wanted = (items ?? [])
            .Select(change => change.WantedVideo)
            .Where(entry => entry?.VideoId is not null)
            .ToList();

        if (wanted.Count == 0)
        {
            return 0;
        }

        var applied = 0;
        var userHash = await Context.Installation
            .Select(row => row.PrdbUserHash)
            .SingleAsync(cancellationToken);

        foreach (var entry in wanted)
        {
            var prdbId = entry!.VideoId!.Value;

            if (entry.IsDeleted is true)
            {
                // Off the list. The catalogue row stays: it belongs to no
                // account and something else may still point at it, and losing
                // the pin is what eviction is for rather than what a delete is.
                // A tombstone for a video that was never held asks for nothing
                // at all, which is why this is looked up rather than created.
                if (await Catalogue.FindVideoAsync(prdbId, cancellationToken) is { } gone)
                {
                    await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
                    // Completed is included for the race where SABnzbd's poll
                    // observed completion while this feed transition was being
                    // applied. Whichever writer arrives first, an uncollected
                    // automatic Download converges on Abandoned and is never
                    // filed after intent was withdrawn.
                    await Context.Downloads
                        .Where(row => row.VideoId == prdbId
                            && !row.OriginIsPerson
                            && (row.State == DownloadState.Outstanding
                                || row.State == DownloadState.Completed))
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(row => row.State, DownloadState.Abandoned)
                            .SetProperty(row => row.Cause, (DownloadCause?)null),
                            cancellationToken);
                    await Context.Releases
                        .Where(row => row.VideoId == gone)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(row => row.AutomationPending, false)
                            .SetProperty(row => row.AutomationDecisionReason, AutomationDecisionReason.NotWanted),
                            cancellationToken);
                    await Context.WantedVideos
                        .Where(row => row.VideoId == gone)
                        .ExecuteDeleteAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }

                applied++;
                continue;
            }

            // The row the list points at, created from this payload where the
            // catalogue does not hold it yet. See CatalogueRows for why that is
            // not the summary ADR 0013 refuses to build a catalogue out of.
            var videoId = await Catalogue.VideoAsync(
                prdbId,
                entry.VideoTitle,
                entry.VideoReleaseDate is { } released ? DateOnly.FromDateTime(released.DateTime) : null,
                cancellationToken);

            var held = await Context.WantedVideos
                .AsTracking()
                .SingleOrDefaultAsync(row => row.VideoId == videoId, cancellationToken);
            var joined = held is null;

            // Since when prdb says it has been wanted, which is what ticket 10's
            // grid orders on. The fulfilment fields beside it are not stored:
            // ADR 0033 makes a WantedVideo a video and a date, and ADR 0019
            // keeps what was reported somewhere that survives a change of key.
            var since = entry.CreatedAtUtc ?? entry.UpdatedAtUtc ?? default;

            if (held is null)
            {
                // Becoming Wanted is itself a new local needle, even when the
                // catalogue row was already pinned for some other reason and
                // its title was searched before. Put that title back into the
                // backwards Screening work set with the pin that makes it
                // relevant.
                var video = await Context.CatalogueVideos
                    .AsTracking()
                    .SingleAsync(row => row.Id == videoId, cancellationToken);
                video.TitleSearchedBackwards = false;
                Context.WantedVideos.Add(new WantedVideoRow { VideoId = videoId, SinceAt = since });
            }
            else
            {
                held.SinceAt = since;
            }

            await Context.SaveChangesAsync(cancellationToken);

            if (joined)
            {
                await Context.Releases
                    .Where(row => row.VideoId == videoId
                        && row.IdentificationState == IdentificationState.Matched)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.AutomationPending, true)
                        .SetProperty(row => row.AutomationDecisionReason, (AutomationDecisionReason?)null),
                        cancellationToken);
            }

            // ADR 0019: NotWanted closes a report until prdb's feed says the
            // wanted row is alive again. NotFound never clears.
            if (!string.IsNullOrWhiteSpace(userHash))
            {
                await Context.ReportedStates
                    .Where(row => row.VideoId == prdbId
                        && row.UserHash == userHash
                        && row.TerminalOutcome == ReportingOutcome.NotWanted)
                    .ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.TerminalOutcome, (ReportingOutcome?)null),
                        cancellationToken);
            }

            applied++;
        }

        return applied;
    }
}
