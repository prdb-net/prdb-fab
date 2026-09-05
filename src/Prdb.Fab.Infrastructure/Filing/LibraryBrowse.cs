using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>The held-only Library grid and one Library Entry.</summary>
public sealed class LibraryBrowse(FabDbContext context, OperationLogBrowse operations)
{
    public const int APage = 48;

    public async Task<LibraryPage> ReadAsync(
        string? search,
        Guid? siteId,
        Guid? actorId,
        string? quality,
        int page,
        LibraryEntrySort sort = LibraryEntrySort.FiledAtDescending,
        CancellationToken cancellationToken = default)
    {
        var wanted = Paging.Wanted(page);
        var entries = context.LibraryEntries
            .AsNoTracking()
            .Join(
                context.CatalogueVideos,
                entry => entry.VideoId,
                video => video.PrdbId,
                (entry, video) => new HeldRow { Entry = entry, Video = video });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPattern.Containing(search);
            entries = entries.Where(row => EF.Functions.Like(row.Video.Title, pattern, SearchPattern.Escape));
        }
        if (siteId is not null)
        {
            entries = entries.Where(row => row.Video.Site != null && row.Video.Site.PrdbId == siteId);
        }
        if (actorId is not null)
        {
            entries = entries.Where(row => context.CatalogueVideoActors.Any(link =>
                link.VideoId == row.Video.Id && link.Actor != null && link.Actor.PrdbId == actorId));
        }
        if (!string.IsNullOrWhiteSpace(quality))
        {
            entries = entries.Where(row => context.VideoFiles.Any(file =>
                file.LibraryEntryVideoId == row.Entry.VideoId && file.QualityLabel == quality));
        }

        var total = await entries.CountAsync(cancellationToken);
        var rows = await Ordered(entries, sort)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .Select(row => new
            {
                ArtworkId = row.Video.Id,
                row.Video.PrdbId,
                row.Video.Title,
                Site = row.Video.Site == null ? null : row.Video.Site.Title,
                row.Video.ReleaseDate,
                Files = context.VideoFiles
                    .Where(file => file.LibraryEntryVideoId == row.Entry.VideoId)
                    .Select(file => new { file.QualityLabel, file.RuntimeSeconds })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        var cards = rows.Select(row => new LibraryCard(
            row.PrdbId,
            row.ArtworkId,
            row.Title,
            row.Site,
            row.ReleaseDate,
            row.Files.Select(file => file.QualityLabel).Distinct().Order(VideoQuality.BestFirst).ToArray(),
            row.Files.OrderBy(file => file.QualityLabel, VideoQuality.BestFirst).Select(file => file.RuntimeSeconds).FirstOrDefault()))
            .ToList();

        return new LibraryPage(
            cards,
            await FiltersAsync(cancellationToken),
            wanted,
            APage,
            total);
    }

    public async Task<LibraryEntry?> EntryAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var row = await context.LibraryEntries
            .AsNoTracking()
            .Where(entry => entry.VideoId == videoId)
            .Join(
                context.CatalogueVideos,
                entry => entry.VideoId,
                video => video.PrdbId,
                (entry, video) => new
                {
                    entry.EntryDirectory,
                    entry.FiledAt,
                    Video = video,
                    Site = video.Site == null ? null : video.Site.Title,
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var actorRows = await context.CatalogueVideoActors
            .AsNoTracking()
            .Where(link => link.VideoId == row.Video.Id)
            .Join(
                context.CatalogueActors,
                link => link.ActorId,
                actor => actor.Id,
                (_, actor) => new { actor.PrdbId, actor.Name })
            .OrderBy(actor => actor.Name)
            .ToListAsync(cancellationToken);
        var actors = actorRows
            .Select(actor => new LibraryActor(actor.PrdbId, actor.Name))
            .ToList();
        var files = (await context.VideoFiles
            .AsNoTracking()
            .Where(file => file.LibraryEntryVideoId == videoId)
            .Select(file => new LibraryFile(
                file.Id,
                file.FiledPath,
                file.QualityLabel,
                file.SizeBytes,
                file.RuntimeSeconds,
                file.Width,
                file.Height,
                file.VideoCodec))
            .ToListAsync(cancellationToken))
            // Ordered here rather than in SQL, because the ladder is a rule and
            // Core holds the rules (ADR 0035). An entry holds a handful of
            // files, so the sort costs nothing.
            .OrderBy(file => file.Quality, VideoQuality.BestFirst)
            .ThenBy(file => file.Id)
            .ToList();

        return new LibraryEntry(
            videoId,
            row.Video.Title,
            row.Site,
            row.Video.ReleaseDate,
            row.Video.DurationMs,
            row.Video.DurationSpreadMs,
            row.Video.DurationFileCount,
            row.EntryDirectory,
            row.FiledAt,
            actors,
            files,
            await operations.ReadAsync(null, null, videoId, 1, cancellationToken));
    }

    /// <summary>
    /// ADR 0055's four orders. Each one tiebreaks, because two Library Entries
    /// must not swap places between two requests for the same page.
    /// </summary>
    /// <remarks>
    /// The title orders read <see cref="CatalogueVideoRow.NormalisedTitle"/>
    /// rather than the title. SQLite's default collation is BINARY, which reads
    /// "Zebra" as coming before "apple"; ADR 0025's comparison form is already
    /// on the row, already required, and lower cased, so ordering by it is one
    /// order on every provider. It leaves accents alone and so does this —
    /// there is no collation here that would fold them.
    /// </remarks>
    private static IQueryable<HeldRow> Ordered(IQueryable<HeldRow> entries, LibraryEntrySort sort) =>
        sort switch
    {
        LibraryEntrySort.FiledAtAscending => entries
            .OrderBy(row => row.Entry.FiledAt)
            .ThenBy(row => row.Entry.VideoId),
        LibraryEntrySort.TitleAscending => entries
            .OrderBy(row => row.Video.NormalisedTitle)
            .ThenBy(row => row.Video.Id),
        LibraryEntrySort.TitleDescending => entries
            .OrderByDescending(row => row.Video.NormalisedTitle)
            .ThenByDescending(row => row.Video.Id),
        LibraryEntrySort.FiledAtDescending => entries
            .OrderByDescending(row => row.Entry.FiledAt)
            .ThenByDescending(row => row.Entry.VideoId),
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
    };

    /// <summary>
    /// One held Video, named rather than anonymous so that <see cref="Ordered"/>
    /// can take it — the orders differ in which side of the join they read.
    /// </summary>
    /// <remarks>
    /// Initialised member by member rather than through a constructor. The
    /// provider translates the members of a projection it can see into; a
    /// constructor call inside an <c>ORDER BY</c> over a join is an expression
    /// it refuses.
    /// </remarks>
    private sealed class HeldRow
    {
        public required LibraryEntryRow Entry { get; init; }

        public required CatalogueVideoRow Video { get; init; }
    }

    private async Task<LibraryFilters> FiltersAsync(CancellationToken cancellationToken)
    {
        var held = context.LibraryEntries.Select(entry => entry.VideoId);
        var siteRows = await context.CatalogueVideos
            .AsNoTracking()
            .Where(video => held.Contains(video.PrdbId) && video.Site != null)
            .Select(video => new { video.Site!.PrdbId, video.Site.Title })
            .Distinct()
            .OrderBy(site => site.Title)
            .ToListAsync(cancellationToken);
        var actorRows = await context.CatalogueVideoActors
            .AsNoTracking()
            .Where(link => link.Video != null && held.Contains(link.Video.PrdbId) && link.Actor != null)
            .Select(link => new { link.Actor!.PrdbId, link.Actor.Name })
            .Distinct()
            .OrderBy(actor => actor.Name)
            .ToListAsync(cancellationToken);
        var qualities = (await context.VideoFiles
            .AsNoTracking()
            .Select(file => file.QualityLabel)
            .Distinct()
            .ToListAsync(cancellationToken))
            .Order(VideoQuality.BestFirst)
            .ToList();
        return new LibraryFilters(
            siteRows.Select(site => new LibraryFilter(site.PrdbId, site.Title)).ToList(),
            actorRows.Select(actor => new LibraryFilter(actor.PrdbId, actor.Name)).ToList(),
            qualities);
    }
}

/// <summary>ADR 0055's orders for the Library grid.</summary>
public enum LibraryEntrySort
{
    FiledAtDescending,
    FiledAtAscending,
    TitleAscending,
    TitleDescending,
}

public sealed record LibraryFilter(Guid Id, string Name);
public sealed record LibraryFilters(
    IReadOnlyList<LibraryFilter> Sites,
    IReadOnlyList<LibraryFilter> Actors,
    IReadOnlyList<string> Qualities);
public sealed record LibraryCard(
    Guid Id,
    long ArtworkId,
    string Title,
    string? Site,
    DateOnly? ReleaseDate,
    IReadOnlyList<string> Qualities,
    long? RuntimeSeconds);
public sealed record LibraryPage(
    IReadOnlyList<LibraryCard> Entries,
    LibraryFilters Filters,
    int Page,
    int PageSize,
    int Total);
public sealed record LibraryActor(Guid Id, string Name);
public sealed record LibraryFile(
    Guid Id,
    string FiledPath,
    string Quality,
    long SizeBytes,
    long? RuntimeSeconds,
    int? Width,
    int? Height,
    string? VideoCodec);
public sealed record LibraryEntry(
    Guid Id,
    string Title,
    string? Site,
    DateOnly? ReleaseDate,
    long? ConsensusRuntimeMs,
    long? ConsensusRuntimeSpreadMs,
    int? ConsensusRuntimeFileCount,
    string EntryDirectory,
    DateTimeOffset FiledAt,
    IReadOnlyList<LibraryActor> Actors,
    IReadOnlyList<LibraryFile> Files,
    OperationLogPage Operations);
