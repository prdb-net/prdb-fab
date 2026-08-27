using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Host.Catalogue;

/// <summary>
/// The browse surfaces. ADR 0012 makes five of them and this slice builds two:
/// What's New, which ADR 0013 calls the landing page and which is what the
/// catalogue exists for, and the wanted list, which is where onboarding ends.
/// </summary>
/// <remarks>
/// ADR 0040's shape, and ADR 0036's rule about the address: the page is a query
/// parameter because it is what a person would link to, and it is counted from
/// one because that is what they would read.
/// </remarks>
public static class CatalogueEndpoints
{
    public static void MapCatalogue(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/catalogue").WithTags("Catalogue");

        group.MapGet("/whats-new", async (
            CatalogueBrowse browse,
            CancellationToken cancellationToken,
            int page = 1) =>
            TypedResults.Ok(await browse.WhatsNewAsync(page, cancellationToken)));

        // ADR 0007's only source of intent, read. There is no route beside this
        // one to add to it: wanting happens in prdb, and the feed is how it
        // arrives.
        group.MapGet("/wanted", async (
            CatalogueBrowse browse,
            CancellationToken cancellationToken,
            int page = 1) =>
            TypedResults.Ok(await browse.WantedAsync(page, cancellationToken)));
    }
}
