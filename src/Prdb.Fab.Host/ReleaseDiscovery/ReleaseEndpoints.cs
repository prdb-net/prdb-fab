using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

namespace Prdb.Fab.Host.ReleaseDiscovery;

/// <summary>The one local Release table, reached through one browse context.</summary>
public static class ReleaseEndpoints
{
    public static void MapReleaseDiscovery(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/releases", ReadAsync).WithTags("Release discovery");
    }

    private static async Task<Results<Ok<ReleasePage>, BadRequest<ProblemDetails>, NotFound>> ReadAsync(
        ReleaseBrowse browse,
        CancellationToken cancellationToken,
        Guid? video = null,
        Guid? site = null,
        Guid? actor = null,
        IdentificationState? state = null,
        Guid? indexer = null,
        int page = 1)
    {
        if (new[] { video, site, actor }.Count(value => value is not null) != 1)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Choose one Release context.",
                Detail = "Exactly one of video, site, or actor is required.",
            });
        }

        var answer = video is not null
            ? await browse.VideoAsync(video.Value, state, indexer, page, cancellationToken)
            : site is not null
                ? await browse.SiteAsync(site.Value, state, indexer, page, cancellationToken)
                : await browse.ActorAsync(actor!.Value, state, indexer, page, cancellationToken);

        return answer is null ? TypedResults.NotFound() : TypedResults.Ok(answer);
    }
}
