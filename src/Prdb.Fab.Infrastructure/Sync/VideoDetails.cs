using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// What a detail read leaves behind: one catalogue video, its site, its
/// pre-names, its credits and its artwork.
/// </summary>
/// <remarks>
/// <para>
/// The only way a catalogue row is created. ADR 0013 rests an argument on that
/// — <c>VideoSummaryDto</c> carries no image field, so a row born from a
/// summary would be a row with no artwork and no way to know it was missing —
/// and both routes that produce a <c>VideoDetailDto</c> come through here:
/// What's New, discovering ids and reading them back at fifty a request, and
/// ADR 0013's repair pass doing the same thing in the other direction.
/// </para>
/// <para>
/// A write is a reconcile rather than an insert, because the payload is the
/// authority on all four lists. That is what closes the second of ADR 0013's
/// two holes: image rows are hard-deleted upstream and the images feed cannot
/// report it, so an image that has stopped being in <c>images[]</c> is one that
/// has been removed — and this is where that is noticed.
/// </para>
/// </remarks>
public sealed class VideoDetails(FabDbContext context, CatalogueRows catalogue, TimeProvider time)
{
    /// <summary>
    /// Writes one video whole, and says whether the catalogue had it before.
    /// </summary>
    public async Task<bool> WriteAsync(VideoDetailDto detail, CancellationToken cancellationToken)
    {
        if (detail.Id is not { } prdbId)
        {
            return false;
        }

        var video = await context.CatalogueVideos
            .AsTracking()
            .SingleOrDefaultAsync(row => row.PrdbId == prdbId, cancellationToken);

        var arrived = video is null;

        if (video is null)
        {
            video = new CatalogueVideoRow { PrdbId = prdbId, Title = string.Empty };
            context.CatalogueVideos.Add(video);
        }

        var title = detail.Title ?? string.Empty;

        if (!string.Equals(video.Title, title, StringComparison.Ordinal))
        {
            // ADR 0023: a title that has changed is a new needle, and a needle
            // that arrives after the cache was written has to be looked for
            // backwards. Leaving the flag set is ADR 0015's silently skipped
            // row — no error, no Gap, and a release that is never matched.
            video.Title = title;
            video.NormalisedTitle = ComparisonForm.Of(title);
            video.TitleSearchedBackwards = false;
        }

        video.ReleaseDate = detail.ReleaseDate is { } released
            ? DateOnly.FromDateTime(released.DateTime)
            : null;

        // ADR 0031's three figures, stored in prdb's own spelling and read by
        // nothing that decides anything.
        video.DurationMs = detail.DurationMs;
        video.DurationSpreadMs = detail.DurationSpreadMs;
        video.DurationFileCount = detail.DurationFileCount;

        video.CreatedAtUtc = detail.CreatedAtUtc ?? default;
        video.UpdatedAtUtc = detail.UpdatedAtUtc ?? default;
        video.LastReadAt = time.GetUtcNow();

        if (detail.Site?.Id is { } siteId)
        {
            video.SiteId = await catalogue.SiteAsync(
                siteId,
                detail.Site.Title,
                detail.Site.Network?.Title,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        await WritePreNamesAsync(video.Id, detail.PreNames, cancellationToken);
        await WriteCreditsAsync(video.Id, detail.Actors, cancellationToken);
        await WriteImagesAsync(video.Id, detail.Images, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return arrived;
    }

    /// <summary>
    /// The pre-names, whole. A new one lands unsearched, which is ADR 0023's
    /// new fact arriving from the other direction and ADR 0032's flag rather
    /// than a resumable position — a needle added while a pass was running would
    /// otherwise sit behind that position and never be searched.
    /// </summary>
    private async Task WritePreNamesAsync(
        long videoId,
        List<VideoDetailPreNameDto>? preNames,
        CancellationToken cancellationToken)
    {
        var wanted = (preNames ?? [])
            .Select(preName => preName.Title)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var held = await context.CatalogueVideoPreNames
            .AsTracking()
            .Where(row => row.VideoId == videoId)
            .ToListAsync(cancellationToken);

        foreach (var gone in held.Where(row => !wanted.Contains(row.PreName, StringComparer.Ordinal)))
        {
            context.CatalogueVideoPreNames.Remove(gone);
        }

        foreach (var title in wanted.Where(title =>
                     !held.Any(row => string.Equals(row.PreName, title, StringComparison.Ordinal))))
        {
            context.CatalogueVideoPreNames.Add(new CatalogueVideoPreNameRow
            {
                VideoId = videoId,
                PreName = title,
                NormalisedPreName = ComparisonForm.Of(title),
            });
        }
    }

    /// <summary>
    /// The credits. Actors are referenced rather than copied (ADR 0013), since
    /// the actors feed already holds them whole — so what a detail read
    /// contributes is a row for one the feed has not reached yet, and the join.
    /// </summary>
    private async Task WriteCreditsAsync(
        long videoId,
        List<VideoDetailActorDto>? actors,
        CancellationToken cancellationToken)
    {
        var credited = new List<long>();

        foreach (var actor in actors ?? [])
        {
            if (actor.Id is { } prdbId)
            {
                credited.Add(await catalogue.ActorAsync(prdbId, actor.Name, cancellationToken));
            }
        }

        var held = await context.CatalogueVideoActors
            .AsTracking()
            .Where(row => row.VideoId == videoId)
            .ToListAsync(cancellationToken);

        foreach (var gone in held.Where(row => !credited.Contains(row.ActorId)))
        {
            context.CatalogueVideoActors.Remove(gone);
        }

        foreach (var actorId in credited.Where(actorId => held.All(row => row.ActorId != actorId)))
        {
            context.CatalogueVideoActors.Add(new CatalogueVideoActorRow
            {
                VideoId = videoId,
                ActorId = actorId,
            });
        }
    }

    /// <summary>
    /// The artwork, and the half of ADR 0013's repair that no feed can do:
    /// <c>images[]</c> is authoritative, so an image that is not in it has been
    /// hard-deleted upstream and simply stopped being returned.
    /// </summary>
    private async Task WriteImagesAsync(
        long videoId,
        List<VideoDetailImageDto>? images,
        CancellationToken cancellationToken)
    {
        // The payload's own order, kept: ADR 0027 chooses the first entry with a
        // URL, and nothing else on the row can say which that was. An entry
        // without one still takes its place in the count, because the position
        // quotes images[] rather than the subset that happens to be usable.
        var published = (images ?? [])
            .Where(image => image.Id is not null)
            .GroupBy(image => image.Id!.Value)
            .Select((group, position) => new Published(
                group.Key,
                group.First().Url ?? string.Empty,
                position))
            .ToList();

        var ids = published.Select(image => image.PrdbId).ToList();

        // The video's own images, and any row elsewhere carrying one of the ids
        // this payload claims. The second half is not hypothetical arithmetic:
        // an image id is unique across the whole table, so a row prdb has moved
        // to another video would otherwise be inserted a second time and refused
        // by the schema — a routine failing on a correction it was reading.
        var held = await context.CatalogueImages
            .AsTracking()
            .Where(row => row.VideoId == videoId || ids.Contains(row.PrdbId))
            .ToListAsync(cancellationToken);

        foreach (var gone in held.Where(row =>
                     row.VideoId == videoId && !ids.Contains(row.PrdbId)))
        {
            // ADR 0030 names the cached file by the image id, so the bytes on
            // disk are the artwork routine's to sweep up rather than this one's:
            // what is gone here is prdb's record of the image.
            context.CatalogueImages.Remove(gone);
        }

        foreach (var (prdbId, url, position) in published)
        {
            var row = held.FirstOrDefault(held => held.PrdbId == prdbId);

            if (row is null)
            {
                context.CatalogueImages.Add(new CatalogueImageRow
                {
                    PrdbId = prdbId,
                    VideoId = videoId,
                    Url = url,
                    Position = position,
                });
            }
            else if (!string.Equals(row.Url, url, StringComparison.Ordinal))
            {
                // The same image id over different bytes. What is cached is no
                // longer what prdb publishes, and a URL found dead is worth
                // another look now that it has changed.
                row.Url = url;
                row.VideoId = videoId;
                row.Position = position;
                row.Cached = false;
                row.FoundDead = false;
            }
            else
            {
                row.VideoId = videoId;

                // The order is the payload's every time, so a row that has moved
                // within images[] moves here — that is how the chosen image
                // becomes a different one without anything comparing bytes
                // (ADR 0027). The cached file is named by the image id, so
                // nothing on disk is invalidated by it.
                row.Position = position;
            }
        }
    }

    /// <summary>One image of the payload, at the place the payload put it.</summary>
    private sealed record Published(Guid PrdbId, string Url, int Position);
}
