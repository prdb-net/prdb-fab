using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Finding a catalogue row by prdb's id, and creating the one that is not there
/// yet.
/// </summary>
/// <remarks>
/// <para>
/// The three feeds that belong to the user — the wanted list and the two
/// favourites — name a video, a site or an actor that the catalogue may not
/// hold. ADR 0033 keys each of those tables by the catalogue row, so the row has
/// to exist before the user's row can, and the feed's own payload is what
/// fills it: <c>videoTitle</c>, <c>siteTitle</c>, <c>videoReleaseDate</c>. That
/// is the reading ticket 10 asks for, and it is the only place a catalogue row
/// is written from something other than a detail read.
/// </para>
/// <para>
/// ADR 0013's rule survives it, because what makes that rule matter is the
/// artwork: <c>images[]</c> arrives with a detail read and nowhere else. A row
/// created here says so — its <em>last re-read</em> is the beginning of time —
/// so ADR 0013's repair pass, which takes pinned rows oldest-checked first,
/// takes it before anything else. A wanted video is pinned, so the row spends
/// one repair pass in that state and is then a row like any other.
/// </para>
/// <para>
/// Nothing here overwrites. A row the catalogue already holds was written by a
/// detail read or corrected by one, and a summary is not an improvement on it.
/// </para>
/// </remarks>
public sealed class CatalogueRows(FabDbContext context)
{
    /// <summary>The local id of the video prdb calls <paramref name="prdbId"/>, if it is held.</summary>
    public async Task<long?> FindVideoAsync(Guid prdbId, CancellationToken cancellationToken) =>
        await context.CatalogueVideos
            .Where(row => row.PrdbId == prdbId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The video, created from what a feed said about it where the catalogue
    /// does not hold it yet.
    /// </summary>
    /// <remarks>
    /// The site is left unset. A user feed carries the site's <em>title</em> and
    /// not its id, and ADR 0033 has this column reference the site row — so
    /// there is nothing here to point it at, and a video whose site is not known
    /// yet is a state the schema already allows for.
    /// </remarks>
    public async Task<long> VideoAsync(
        Guid prdbId,
        string? title,
        DateOnly? releaseDate,
        CancellationToken cancellationToken)
    {
        if (await FindVideoAsync(prdbId, cancellationToken) is { } held)
        {
            return held;
        }

        var row = new CatalogueVideoRow
        {
            PrdbId = prdbId,
            Title = title ?? string.Empty,
            NormalisedTitle = ComparisonForm.Of(title),
            ReleaseDate = releaseDate,

            // Never read from prdb in detail, which is exactly what these two
            // say. UpdatedAtUtc is prdb's stamp and this row carries none of
            // its own; LastReadAt is what puts it at the front of the repair
            // pass, where the artwork and the pre-names are waiting for it.
            UpdatedAtUtc = default,
            LastReadAt = default,
        };

        context.CatalogueVideos.Add(row);
        await context.SaveChangesAsync(cancellationToken);

        return row.Id;
    }

    /// <summary>The local id of the actor prdb calls <paramref name="prdbId"/>, if they are held.</summary>
    public async Task<long?> FindActorAsync(Guid prdbId, CancellationToken cancellationToken) =>
        await context.CatalogueActors
            .Where(row => row.PrdbId == prdbId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The local id of the site prdb calls <paramref name="prdbId"/>, if it is held.</summary>
    public async Task<long?> FindSiteAsync(Guid prdbId, CancellationToken cancellationToken) =>
        await context.CatalogueSites
            .Where(row => row.PrdbId == prdbId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The actor, created from a name where the actors feed has not reached them.</summary>
    public async Task<long> ActorAsync(Guid prdbId, string? name, CancellationToken cancellationToken)
    {
        if (await FindActorAsync(prdbId, cancellationToken) is { } id)
        {
            return id;
        }

        var row = new CatalogueActorRow { PrdbId = prdbId, Name = name ?? string.Empty };

        context.CatalogueActors.Add(row);
        await context.SaveChangesAsync(cancellationToken);

        return row.Id;
    }

    /// <summary>The site, created where the daily site list has not run yet.</summary>
    public async Task<long> SiteAsync(
        Guid prdbId,
        string? title,
        string? network,
        CancellationToken cancellationToken)
    {
        if (await FindSiteAsync(prdbId, cancellationToken) is { } id)
        {
            return id;
        }

        var row = new CatalogueSiteRow
        {
            PrdbId = prdbId,
            Title = title ?? string.Empty,
            Network = network,
        };

        context.CatalogueSites.Add(row);
        await context.SaveChangesAsync(cancellationToken);

        return row.Id;
    }
}
