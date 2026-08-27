using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0014's cadence for the site list: twenty-four hours, in the sync lane.
/// </summary>
/// <remarks>
/// <para>
/// The whole list fits one request at a page size of a thousand, which is why
/// ADR 0013 gives sites no feed, no cursor and no diff: what is held is replaced
/// wholesale, or it is not touched at all. The <c>ETag</c> is what decides
/// which, so the ordinary day costs one request and one <c>304</c>.
/// </para>
/// <para>
/// <strong>A site row is never deleted, only marked as no longer offered.</strong>
/// ADR 0005 builds a filed path out of the site's title and ADR 0017 makes the
/// recorded path the truth from then on, so a library entry has to keep being
/// able to name the site it was built from — whether or not prdb still offers
/// it.
/// </para>
/// <para>
/// The network is the only thing this can fail at, so it is ordinary backoff and
/// at three an ordinary Gap, both of which the schedule already does. There is
/// no partial state to recover from: either the list was replaced or it was not.
/// </para>
/// </remarks>
public sealed class SiteListRoutine(
    FabDbContext context,
    FeedCursors cursors,
    PrdbGateway prdb,
    ILogger<SiteListRoutine> logger) : IRoutine, ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.sites";

    /// <summary>
    /// <c>GET /sites</c>'s largest page, and the size the endpoint's own
    /// description says the whole list fits in.
    /// </summary>
    public const int TheWholeList = 1000;

    public string Name => RoutineName;

    public Lane Lane => Lane.Sync;

    public TimeSpan Cadence => TimeSpan.FromHours(24);

    /// <summary>
    /// Last of ADR 0014's order before repair, and the smallest share of the
    /// idle profile there is — one request a day, usually answered <c>304</c>.
    /// </summary>
    public PrdbWork Spends => PrdbWork.Sites;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return RunResult.NothingToDo;
        }

        var held = await cursors.TokenAsync(Feed.Sites, cancellationToken);

        var status = new ResponseStatusOption();
        var headers = new HeadersInspectionHandlerOption { InspectResponseHeaders = true };

        var answer = await prdb.AskAsync(
            apiKey,
            PrdbWork.Sites,
            (client, token) => client.Sites.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = TheWholeList;

                    if (held is { Length: > 0 })
                    {
                        request.Headers.Add("If-None-Match", held);
                    }

                    request.Options.Add(status);
                    request.Options.Add(headers);
                },
                token),
            cancellationToken);

        if (status.StatusCode == HttpStatusCode.NotModified)
        {
            // Nothing changed. The rows and the validator are both left where
            // they are, and the run is a success that handled nothing — which
            // is what an unchanged list is, rather than an empty tick.
            logger.LogDebug("prdb's site list has not changed.");

            return RunResult.Handled(0);
        }

        var offered = EntityTagIn(headers);

        if (held is { Length: > 0 } && offered == held)
        {
            // The API document calls this expected rather than an error: the
            // shared read-only cache does not vary by If-None-Match, so a
            // request that hits it is answered 200 with a body even while the
            // validator still matches. Reading that as a change would replace
            // the whole table daily for nothing.
            logger.LogDebug("prdb answered the site list with a body and the validator this tool already had.");

            return RunResult.Handled(0);
        }

        if (answer?.Items is not { } sites)
        {
            // A 200 with nothing in it. Not a change and not a failure, and
            // above all not a reason to mark every site as no longer offered.
            logger.LogWarning("prdb answered the site list without a list.");

            return RunResult.Handled(0);
        }

        var replaced = await ReplaceAsync(sites, cancellationToken);

        if (offered is { Length: > 0 })
        {
            await cursors.SaveAsync(Feed.Sites, offered, cancellationToken);
        }

        logger.LogInformation(
            "prdb's site list was replaced: {Count} row(s) added, corrected or retired out of {Total}.",
            replaced,
            sites.Count);

        return RunResult.Handled(replaced);
    }

    /// <summary>
    /// The list, wholesale. What is not in it stops being offered rather than
    /// being deleted.
    /// </summary>
    /// <returns>How many rows this actually changed.</returns>
    private async Task<int> ReplaceAsync(List<SiteSummaryDto> sites, CancellationToken cancellationToken)
    {
        var offered = sites.Where(site => site.Id is not null).ToList();
        var ids = offered.Select(site => site.Id!.Value).ToHashSet();

        // The whole table, which is a request's worth of rows plus whatever has
        // been retired since — small enough to hold, and the alternative is one
        // query per site to find the ones that are missing from a list.
        var held = await context.CatalogueSites
            .AsTracking()
            .ToDictionaryAsync(row => row.PrdbId, cancellationToken);

        var changed = 0;

        foreach (var site in offered)
        {
            var prdbId = site.Id!.Value;
            var title = site.Title ?? string.Empty;

            if (!held.TryGetValue(prdbId, out var row))
            {
                context.CatalogueSites.Add(new CatalogueSiteRow
                {
                    PrdbId = prdbId,
                    Title = title,
                    Network = site.NetworkTitle,
                });

                changed++;
                continue;
            }

            if (string.Equals(row.Title, title, StringComparison.Ordinal)
                && string.Equals(row.Network, site.NetworkTitle, StringComparison.Ordinal)
                && row.StillOffered)
            {
                continue;
            }

            row.Title = title;
            row.Network = site.NetworkTitle;

            // A site that has come back is offered again, which is the one way
            // this flag is ever cleared.
            row.StillOffered = true;

            changed++;
        }

        foreach (var gone in held.Values.Where(row => row.StillOffered && !ids.Contains(row.PrdbId)))
        {
            gone.StillOffered = false;
            changed++;
        }

        await context.SaveChangesAsync(cancellationToken);

        return changed;
    }

    /// <summary>
    /// The validator the answer carried, or null where it carried none.
    /// </summary>
    /// <remarks>
    /// Read off the response rather than through a typed field, because an
    /// <c>ETag</c> is a header and the generated client returns a body. The
    /// inspection handler sits inside the one that presents a <c>304</c> as a
    /// bodyless success, so the headers reach here untouched either way.
    /// </remarks>
    private static string? EntityTagIn(HeadersInspectionHandlerOption headers) =>
        headers.ResponseHeaders.TryGetValue("ETag", out var values)
            ? values.FirstOrDefault()
            : null;
}
