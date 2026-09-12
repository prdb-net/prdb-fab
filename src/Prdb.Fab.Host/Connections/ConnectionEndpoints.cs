using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Host.Connections;

/// <summary>
/// ADR 0010's four connection forms, seen from the API.
/// </summary>
/// <remarks>
/// Every one of these answers <c>200</c> with a typed verdict, including the
/// refusals — ADR 0040: a wrong key is something the tool checked and can
/// answer, and a status code is reserved for the request itself having failed.
/// Everything here is behind the password, by the fallback policy the host is
/// composed with rather than by anything said here.
/// </remarks>
public static class ConnectionEndpoints
{
    public static void MapConnections(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/connections").WithTags("Connections");

        // What is configured, and nothing that is a credential. ADR 0037 keeps
        // the keys in the clear in the database and that is still no reason to
        // hand one back out over the network.
        group.MapGet("/", async (
            FabDbContext context,
            CancellationToken cancellationToken) =>
        {
            var installation = await context.Installation.SingleAsync(cancellationToken);
            var indexers = await context.Indexers.CountAsync(cancellationToken);

            return TypedResults.Ok(new ConnectionsState(
                PrdbConfigured: installation.PrdbApiKey is { Length: > 0 },
                SabnzbdConfigured: installation.SabnzbdApiKey is { Length: > 0 },
                SabnzbdSkipped: installation.SabnzbdSkipped,
                SabnzbdUrl: installation.SabnzbdUrl,
                SabnzbdCategory: installation.SabnzbdCategory,
                CompletedRoot: installation.PathMappingFrom,
                DownloadDirectory: installation.PathMappingTo,
                IndexerCount: indexers,
                IndexersSkipped: installation.IndexersSkipped,
                LibraryRoot: installation.LibraryRoot));
        });

        group.MapPost("/prdb", async (
            PrdbConnectionRequest request,
            PrdbConnections connections,
            CancellationToken cancellationToken) =>
        {
            var save = await connections.SaveAsync(
                request.ApiKey,
                request.ConfirmAnotherAccount,
                cancellationToken);

            return TypedResults.Ok(new PrdbConnectionVerdict(
                save.Outcome,
                PrdbConnection.Sentence(save.Outcome),
                save.RetryAfterSeconds));
        });

        // A read rather than a write, and a POST because the credential it needs
        // has no business in an address bar or in anybody's access log.
        group.MapPost("/sabnzbd/categories", async (
            SabnzbdCategoriesRequest request,
            SabnzbdConnections connections,
            CancellationToken cancellationToken) =>
        {
            var categories = await connections.CategoriesAsync(
                request.Url,
                request.ApiKey,
                cancellationToken);

            return TypedResults.Ok(new SabnzbdCategoriesVerdict(
                categories.Outcome,
                SabnzbdConnection.Sentence(categories.Outcome),
                categories.Categories));
        });

        group.MapPost("/sabnzbd", async (
            SabnzbdConnectionRequest request,
            SabnzbdConnections connections,
            CancellationToken cancellationToken) =>
        {
            var save = await connections.SaveAsync(
                request.Url,
                request.ApiKey,
                request.Category,
                request.DownloadDirectory,
                cancellationToken);

            return TypedResults.Ok(new SabnzbdConnectionVerdict(
                save.Outcome,
                SabnzbdConnection.Sentence(save.Outcome),
                save.CompletedRoot));
        });

        group.MapGet("/indexers", async (
            Indexers indexers,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await indexers.ListAsync(cancellationToken)));

        group.MapPost("/indexers", async (
            IndexerConnectionRequest request,
            Indexers indexers,
            CancellationToken cancellationToken) =>
        {
            var save = await indexers.AddAsync(
                request.Name,
                request.Url,
                request.ApiKey,
                cancellationToken);

            return TypedResults.Ok(new IndexerConnectionVerdict(
                save.Outcome,
                IndexerConnection.Sentence(save.Outcome, save.Said),
                save.Categories));
        });

        // ADR 0020: every indexer has its own route, because ADR 0018's Brakes
        // point at one row rather than at a list. An id that is not there is
        // the request being wrong rather than a verdict, so it is a 404.
        group.MapPost("/indexers/{id:guid}", async Task<Results<Ok<IndexerConnectionVerdict>, NotFound>> (
            Guid id,
            IndexerConnectionRequest request,
            Indexers indexers,
            CancellationToken cancellationToken) =>
        {
            var save = await indexers.EditAsync(
                id,
                request.Name,
                request.Url,
                request.ApiKey,
                cancellationToken);

            // Declared as the two it answers with, so the document the frontend
            // is built from carries the verdict's shape rather than an untyped
            // 200 (ADR 0040).
            return save is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(new IndexerConnectionVerdict(
                    save.Outcome,
                    IndexerConnection.Sentence(save.Outcome, save.Said),
                    save.Categories));
        });

        // ADR 0020's three row settings, as an act of their own. Not fields on
        // the edit request: changing a rank or a budget is not a reason to run
        // a real search against somebody's indexer, and an edit that re-checked
        // would make disabling a broken indexer impossible — the check would
        // fail and the change would be refused with it.
        group.MapPost("/indexers/{id:guid}/settings", async Task<Results<Ok<IndexerSettingsVerdict>, NotFound>> (
            Guid id,
            IndexerSettingsRequest request,
            Indexers indexers,
            CancellationToken cancellationToken) =>
        {
            var save = await indexers.SetAsync(
                id,
                request.Enabled,
                request.DailyQueryBudget,
                cancellationToken);

            return save is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(new IndexerSettingsVerdict(
                    save.Saved,
                    save.Enabled,
                    save.DailyQueryBudget,
                    save.Saved
                        ? "Stored. It takes effect from the next time the schedule reaches this indexer."
                        : "A daily query budget is between 1 and 100,000 requests, or empty for unbounded."));
        });

        // ADR 0020 made the rank a list position rather than a typed number, so
        // the act is a move rather than a value.
        group.MapPost("/indexers/{id:guid}/move", async Task<Results<Ok<IndexerMoveVerdict>, NotFound>> (
            Guid id,
            IndexerMoveRequest request,
            Indexers indexers,
            CancellationToken cancellationToken) =>
        {
            var moved = await indexers.MoveAsync(id, request.Up, cancellationToken);

            return moved
                ? TypedResults.Ok(new IndexerMoveVerdict(id, "The order has been stored."))
                : TypedResults.NotFound();
        });

        // The shape ADR 0040 already set for a destructive act with something
        // to say: a preview that names what goes and what stays, and the act
        // afterwards. A named POST rather than DELETE, because ADR 0040 gives
        // every act that is not a field update an endpoint of its own and the
        // log has to know which one happened.
        group.MapPost("/indexers/{id:guid}/delete/preview", async Task<Results<Ok<IndexerDeletePreview>, NotFound>> (
            Guid id,
            Indexers indexers,
            CancellationToken cancellationToken) =>
        {
            var preview = await indexers.PreviewDeleteAsync(id, cancellationToken);

            return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
        });

        group.MapPost("/indexers/{id:guid}/delete", async Task<Results<Ok<IndexerDeleteVerdict>, NotFound>> (
            Guid id,
            Indexers indexers,
            CancellationToken cancellationToken) =>
        {
            var verdict = await indexers.DeleteAsync(id, cancellationToken);

            return verdict is null ? TypedResults.NotFound() : TypedResults.Ok(verdict);
        });

        group.MapPost("/library-root", async (
            LibraryRootRequest request,
            LibraryRoots roots,
            CancellationToken cancellationToken) =>
        {
            var save = await roots.SaveAsync(request.Path, cancellationToken);

            return TypedResults.Ok(new LibraryRootVerdict(
                save.Outcome,
                LibraryRoot.Sentence(save.Outcome)));
        });
    }
}

/// <summary>What each of ADR 0010's connections holds, with no credential in it.</summary>
/// <remarks>
/// The two <c>Skipped</c> flags are the Gaps ADR 0010 leaves behind: what a
/// connection can answer for itself about whether it was configured or passed
/// by deliberately. Nothing displays them yet — ADR 0018's Status page is a
/// slice of its own, and it adds no column to these.
/// </remarks>
public sealed record ConnectionsState(
    bool PrdbConfigured,
    bool SabnzbdConfigured,
    bool SabnzbdSkipped,
    string? SabnzbdUrl,
    string? SabnzbdCategory,
    string? CompletedRoot,
    string? DownloadDirectory,
    int IndexerCount,
    bool IndexersSkipped,
    string? LibraryRoot);

public sealed record PrdbConnectionRequest(string? ApiKey, bool ConfirmAnotherAccount);

/// <summary>ADR 0040: a verdict is a success with a typed body saying what happened.</summary>
/// <param name="RetryAfterSeconds">
/// What prdb asked for, on the one verdict that carries it. Null everywhere
/// else, including where a retry is the right thing to offer and prdb said
/// nothing about when.
/// </param>
public sealed record PrdbConnectionVerdict(
    PrdbConnectionOutcome Outcome,
    string Detail,
    int? RetryAfterSeconds);

public sealed record SabnzbdCategoriesRequest(string? Url, string? ApiKey);

public sealed record SabnzbdCategoriesVerdict(
    SabnzbdConnectionOutcome Outcome,
    string Detail,
    IReadOnlyList<SabnzbdCategory> Categories);

public sealed record SabnzbdConnectionRequest(
    string? Url,
    string? ApiKey,
    string? Category,
    string? DownloadDirectory);

public sealed record SabnzbdConnectionVerdict(
    SabnzbdConnectionOutcome Outcome,
    string Detail,
    string? CompletedRoot);

public sealed record IndexerConnectionRequest(string? Name, string? Url, string? ApiKey);

/// <param name="DailyQueryBudget">
/// Null is ADR 0020's unbounded. The window is UTC midnight, and half of it —
/// up to <c>IndexerQueryBudget.SweepRequestsPerDay</c> — is reserved for the
/// Wanted Sweep, which is the only route by which an older wanted Video is ever
/// found.
/// </param>
public sealed record IndexerSettingsRequest(bool Enabled, int? DailyQueryBudget);

/// <summary>ADR 0040: a verdict is a success with a typed body saying what happened.</summary>
public sealed record IndexerSettingsVerdict(
    bool Saved,
    bool Enabled,
    int DailyQueryBudget,
    string Detail);

public sealed record IndexerMoveRequest(bool Up);

public sealed record IndexerMoveVerdict(Guid IndexerId, string Detail);

public sealed record IndexerConnectionVerdict(
    IndexerConnectionOutcome Outcome,
    string Detail,
    IReadOnlyList<string> Categories);

public sealed record LibraryRootRequest(string? Path);

public sealed record LibraryRootVerdict(LibraryRootOutcome Outcome, string Detail);
