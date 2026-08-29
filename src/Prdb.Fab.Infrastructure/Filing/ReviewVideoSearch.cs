using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Live prdb search for the Video picker in the Review Queue.</summary>
public sealed class ReviewVideoSearch(FabDbContext context, PrdbGateway prdb)
{
    public async Task<ReviewVideoSearchPage> SearchAsync(
        string? search,
        Guid? siteId,
        int page,
        CancellationToken cancellationToken = default)
    {
        var query = search?.Trim();
        if (siteId is null && (query is null || query.Length < 2))
        {
            return new ReviewVideoSearchPage([], Paging.Wanted(page), 20, 0);
        }

        var apiKey = await context.Installation
            .AsNoTracking()
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken)
            ?? throw new InvalidOperationException("A prdb connection is required to search Videos.");
        var wanted = Paging.Wanted(page);
        var answer = await prdb.AskAsync(
            apiKey,
            PrdbWork.Identification,
            (client, token) => client.Videos.GetAsync(request =>
            {
                request.QueryParameters.Search = query;
                request.QueryParameters.SiteId = siteId;
                request.QueryParameters.Page = wanted;
                request.QueryParameters.PageSize = 20;
            }, token),
            cancellationToken);

        return new ReviewVideoSearchPage(
            [.. (answer?.Items ?? [])
                .Where(video => video.Id is not null)
                .Select(video => new ReviewVideo(
                    video.Id!.Value,
                    video.Title ?? string.Empty,
                    video.SiteTitle,
                    video.ReleaseDate is { } released ? DateOnly.FromDateTime(released.DateTime) : null,
                    video.DurationMs,
                    video.DurationFileCount))],
            wanted,
            20,
            answer?.TotalCount ?? 0);
    }
}

public sealed record ReviewVideoSearchPage(
    IReadOnlyList<ReviewVideo> Videos,
    int Page,
    int PageSize,
    int Total);
