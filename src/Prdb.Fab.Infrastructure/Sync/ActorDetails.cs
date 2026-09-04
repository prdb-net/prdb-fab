using Microsoft.EntityFrameworkCore;
using Microsoft.Kiota.Abstractions;

using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Projects prdb's complete current Actor document into the Catalogue.</summary>
public sealed class ActorDetails(FabDbContext context, TimeProvider time)
{
    public Task<int> WriteChangesAsync(
        IEnumerable<ActorChangeActorDto> actors,
        CancellationToken cancellationToken) =>
        WriteAsync(actors.Select(ActorDocument.From), cancellationToken);

    public Task<int> WriteDetailsAsync(
        IEnumerable<ActorDetailDto> actors,
        CancellationToken cancellationToken) =>
        WriteAsync(actors.Select(ActorDocument.From), cancellationToken);

    private async Task<int> WriteAsync(
        IEnumerable<ActorDocument> source,
        CancellationToken cancellationToken)
    {
        var documents = source
            .GroupBy(actor => actor.PrdbId)
            .Select(group => group.Last())
            .ToList();
        if (documents.Count == 0) return 0;

        var ids = documents.Select(actor => actor.PrdbId).ToList();
        var held = await context.CatalogueActors
            .AsTracking()
            .Where(row => ids.Contains(row.PrdbId))
            .ToDictionaryAsync(row => row.PrdbId, cancellationToken);

        foreach (var document in documents)
        {
            held.TryGetValue(document.PrdbId, out var row);
            if (document.Deleted)
            {
                if (row is not null)
                {
                    context.CatalogueActors.Remove(row);
                    held.Remove(document.PrdbId);
                }
                continue;
            }

            if (row is null)
            {
                row = new CatalogueActorRow { PrdbId = document.PrdbId, Name = string.Empty };
                context.CatalogueActors.Add(row);
                held.Add(document.PrdbId, row);
            }

            Apply(row, document);
        }

        // New Actor rows need their local keys before their child documents can
        // be reconciled. No remote request sits inside either transaction.
        await context.SaveChangesAsync(cancellationToken);

        var active = documents.Where(document => !document.Deleted).ToList();
        var actorIds = active.Select(document => held[document.PrdbId].Id).ToList();
        var aliases = await context.CatalogueActorAliases.AsTracking()
            .Where(row => actorIds.Contains(row.ActorId)).ToListAsync(cancellationToken);
        var bios = await context.CatalogueActorBios.AsTracking()
            .Where(row => actorIds.Contains(row.ActorId)).ToListAsync(cancellationToken);
        var links = await context.CatalogueActorLinks.AsTracking()
            .Where(row => actorIds.Contains(row.ActorId)).ToListAsync(cancellationToken);
        var images = await context.CatalogueActorImages.AsTracking()
            .Where(row => actorIds.Contains(row.ActorId)).ToListAsync(cancellationToken);

        context.RemoveRange(aliases);
        context.RemoveRange(bios);
        context.RemoveRange(links);
        context.RemoveRange(images);

        foreach (var document in active)
        {
            var actorId = held[document.PrdbId].Id;
            context.CatalogueActorAliases.AddRange(document.Aliases.Select(alias =>
                new CatalogueActorAliasRow
                {
                    ActorId = actorId,
                    Name = alias.Name,
                    SitePrdbId = alias.SitePrdbId,
                }));
            context.CatalogueActorBios.AddRange(document.Bios.Select(bio =>
                new CatalogueActorBioRow
                {
                    ActorId = actorId,
                    PrdbId = bio.PrdbId,
                    Text = bio.Text,
                }));
            context.CatalogueActorLinks.AddRange(document.Links.Select(link =>
                new CatalogueActorLinkRow
                {
                    ActorId = actorId,
                    ExternalSite = link.ExternalSite,
                    ExternalSiteLabel = link.ExternalSiteLabel,
                    Url = link.Url,
                }));
            context.CatalogueActorImages.AddRange(document.Images.Select((image, position) =>
                new CatalogueActorImageRow
                {
                    ActorId = actorId,
                    PrdbId = image.PrdbId,
                    Position = position,
                    ImageType = image.ImageType,
                    ImageTypeLabel = image.ImageTypeLabel,
                    Url = image.Url,
                }));
        }

        await context.SaveChangesAsync(cancellationToken);
        return documents.Count;
    }

    private void Apply(CatalogueActorRow row, ActorDocument actor)
    {
        row.Name = actor.Name;
        row.Gender = actor.Gender;
        row.GenderLabel = actor.GenderLabel;
        row.Birthday = actor.Birthday;
        row.BirthdayType = actor.BirthdayType;
        row.BirthdayTypeLabel = actor.BirthdayTypeLabel;
        row.Deathday = actor.Deathday;
        row.Birthplace = actor.Birthplace;
        row.Haircolor = actor.Haircolor;
        row.HaircolorLabel = actor.HaircolorLabel;
        row.Eyecolor = actor.Eyecolor;
        row.EyecolorLabel = actor.EyecolorLabel;
        row.BreastType = actor.BreastType;
        row.BreastTypeLabel = actor.BreastTypeLabel;
        row.Height = actor.Height;
        row.BraSize = actor.BraSize;
        row.BraSizeLabel = actor.BraSizeLabel;
        row.WaistSize = actor.WaistSize;
        row.HipSize = actor.HipSize;
        row.Nationality = actor.Nationality;
        row.NationalityLabel = actor.NationalityLabel;
        row.Ethnicity = actor.Ethnicity;
        row.EthnicityLabel = actor.EthnicityLabel;
        row.CareerStart = actor.CareerStart;
        row.CareerEnd = actor.CareerEnd;
        row.Tattoos = actor.Tattoos;
        row.Piercings = actor.Piercings;
        row.CreatedAtUtc = actor.CreatedAtUtc;
        row.UpdatedAtUtc = actor.UpdatedAtUtc;
        row.ProfileCheckedAt = time.GetUtcNow();

        var profileUrl = actor.Images.FirstOrDefault(image => image.Url is not null)?.Url;
        if (!string.Equals(row.ProfileImageUrl, profileUrl, StringComparison.Ordinal))
        {
            row.ProfileImageUrl = profileUrl;
            row.ArtworkCacheKey = profileUrl is null ? null : ActorArtworkKey.Of(row.PrdbId);
            row.ArtworkCached = false;
            row.ArtworkFoundDead = false;
            row.ArtworkLastServedAt = null;
        }
    }

    private sealed record ActorDocument(
        Guid PrdbId,
        bool Deleted,
        string Name,
        int? Gender,
        string? GenderLabel,
        DateOnly? Birthday,
        int? BirthdayType,
        string? BirthdayTypeLabel,
        DateOnly? Deathday,
        string? Birthplace,
        int? Haircolor,
        string? HaircolorLabel,
        int? Eyecolor,
        string? EyecolorLabel,
        int? BreastType,
        string? BreastTypeLabel,
        int? Height,
        int? BraSize,
        string? BraSizeLabel,
        int? WaistSize,
        int? HipSize,
        int? Nationality,
        string? NationalityLabel,
        int? Ethnicity,
        string? EthnicityLabel,
        int? CareerStart,
        int? CareerEnd,
        string? Tattoos,
        string? Piercings,
        DateTimeOffset? CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        IReadOnlyList<ActorAlias> Aliases,
        IReadOnlyList<ActorBio> Bios,
        IReadOnlyList<ActorLink> Links,
        IReadOnlyList<ActorImage> Images)
    {
        public static ActorDocument From(ActorChangeActorDto actor) => new(
            actor.Id!.Value,
            actor.IsDeleted is true,
            actor.Name ?? string.Empty,
            actor.Gender,
            actor.GenderLabel,
            DateOnlyOf(actor.Birthday),
            actor.BirthdayType,
            actor.BirthdayTypeLabel,
            DateOnlyOf(actor.Deathday),
            actor.Birthplace,
            actor.Haircolor,
            actor.HaircolorLabel,
            actor.Eyecolor,
            actor.EyecolorLabel,
            actor.BreastType,
            actor.BreastTypeLabel,
            actor.Height,
            actor.BraSize,
            actor.BraSizeLabel,
            actor.WaistSize,
            actor.HipSize,
            actor.Nationality,
            actor.NationalityLabel,
            actor.Ethnicity,
            actor.EthnicityLabel,
            actor.CareerStart,
            actor.CareerEnd,
            actor.Tattoos,
            actor.Piercings,
            actor.CreatedAtUtc,
            actor.UpdatedAtUtc,
            (actor.Aliases ?? []).Where(item => item.Name is not null)
                .Select(item => new ActorAlias(item.Name!, item.SiteId)).ToList(),
            (actor.Bios ?? []).Where(item => item.Id.HasValue)
                .Select(item => new ActorBio(item.Id!.Value, item.Text ?? string.Empty)).ToList(),
            (actor.Links ?? []).Where(item => item.Url is not null)
                .Select(item => new ActorLink(item.ExternalSite, item.ExternalSiteLabel ?? string.Empty, item.Url!)).ToList(),
            (actor.Images ?? []).Where(item => item.Id.HasValue)
                .Select(item => new ActorImage(item.Id!.Value, item.ImageType, item.ImageTypeLabel ?? string.Empty, item.Url)).ToList());

        public static ActorDocument From(ActorDetailDto actor) => new(
            actor.Id!.Value,
            false,
            actor.Name ?? string.Empty,
            actor.Gender,
            actor.GenderLabel,
            DateOnlyOf(actor.Birthday),
            actor.BirthdayType,
            actor.BirthdayTypeLabel,
            DateOnlyOf(actor.Deathday),
            actor.Birthplace,
            actor.Haircolor,
            actor.HaircolorLabel,
            actor.Eyecolor,
            actor.EyecolorLabel,
            actor.BreastType,
            actor.BreastTypeLabel,
            actor.Height,
            actor.BraSize,
            actor.BraSizeLabel,
            actor.WaistSize,
            actor.HipSize,
            actor.Nationality,
            actor.NationalityLabel,
            actor.Ethnicity,
            actor.EthnicityLabel,
            actor.CareerStart,
            actor.CareerEnd,
            actor.Tattoos,
            actor.Piercings,
            actor.CreatedAtUtc,
            actor.UpdatedAtUtc,
            (actor.Aliases ?? []).Where(item => item.Name is not null)
                .Select(item => new ActorAlias(item.Name!, item.SiteId)).ToList(),
            (actor.Bios ?? []).Where(item => item.Id.HasValue)
                .Select(item => new ActorBio(item.Id!.Value, item.Text ?? string.Empty)).ToList(),
            (actor.Links ?? []).Where(item => item.Url is not null)
                .Select(item => new ActorLink(item.ExternalSite, item.ExternalSiteLabel ?? string.Empty, item.Url!)).ToList(),
            (actor.Images ?? []).Where(item => item.Id.HasValue)
                .Select(item => new ActorImage(item.Id!.Value, item.ImageType, item.ImageTypeLabel ?? string.Empty, item.Url)).ToList());

        private static DateOnly? DateOnlyOf(Date? value) => value is null
            ? null
            : new DateOnly(value.Value.Year, value.Value.Month, value.Value.Day);
    }

    private sealed record ActorAlias(string Name, Guid? SitePrdbId);
    private sealed record ActorBio(Guid PrdbId, string Text);
    private sealed record ActorLink(int? ExternalSite, string ExternalSiteLabel, string Url);
    private sealed record ActorImage(Guid PrdbId, int? ImageType, string ImageTypeLabel, string? Url);
}
