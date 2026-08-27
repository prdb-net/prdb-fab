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
                SabnzbdUrl: installation.SabnzbdUrl,
                SabnzbdCategory: installation.SabnzbdCategory,
                CompletedRoot: installation.PathMappingFrom,
                DownloadDirectory: installation.PathMappingTo,
                IndexerCount: indexers,
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
public sealed record ConnectionsState(
    bool PrdbConfigured,
    bool SabnzbdConfigured,
    string? SabnzbdUrl,
    string? SabnzbdCategory,
    string? CompletedRoot,
    string? DownloadDirectory,
    int IndexerCount,
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

public sealed record IndexerConnectionVerdict(
    IndexerConnectionOutcome Outcome,
    string Detail,
    IReadOnlyList<string> Categories);

public sealed record LibraryRootRequest(string? Path);

public sealed record LibraryRootVerdict(LibraryRootOutcome Outcome, string Detail);
