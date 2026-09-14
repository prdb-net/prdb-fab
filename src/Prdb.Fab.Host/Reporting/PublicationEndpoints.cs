using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Host.Reporting;

/// <summary>
/// The publishing side of ADR 0064, as the one page that makes it operable:
/// where every generated preview stands, the explicit request for the existing
/// Library, and the two acts only a person may take on an uncertain upload.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0040's rule holds throughout: every act is an endpoint of its own rather
/// than a state written into a field, because <em>pause</em>, <em>cancel</em>,
/// <em>send it again</em> and <em>leave it</em> are four different sentences
/// and three of them are irreversible in one direction or another.
/// </para>
/// <para>
/// <strong>What this does not do is ask the frontend what it saw.</strong>
/// ADR 0040 has an act carry the identifiers it was shown, and the reason is
/// that a re-run selection could delete something that was never named. The
/// request below names nothing and deletes nothing: it records an intent, and
/// every file it later takes up passes the same gates — the switch, the
/// explanation, the account, the hash against the bytes on disk — that a filed
/// file does. The count is a snapshot on both sides, and the page says so.
/// </para>
/// </remarks>
public static class PublicationEndpoints
{
    public static IEndpointRouteBuilder MapPublications(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/publications").WithTags("Publishing");

        group.MapGet("/", async (
            PublicationProgress progress,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await progress.ReadAsync(cancellationToken)));

        // ADR 0064's explicit bounded request, and the only thing in this tool
        // that ever publishes a file the Library already held. A refusal is a
        // sentence in a 200 rather than a status, which is ADR 0040's rule: the
        // three conditions it can fail are all states the form can show, and
        // none of them is a failed request.
        group.MapPost("/backfill", async (
            PreviewBackfill backfill,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await backfill.AskAsync(cancellationToken)));

        group.MapPost("/backfill/{id:guid}/pause", async Task<Results<Ok<PublicationProgressState>, NotFound>> (
            Guid id,
            PreviewBackfill backfill,
            PublicationProgress progress,
            CancellationToken cancellationToken) =>
            await backfill.PauseAsync(id, cancellationToken)
                ? TypedResults.Ok(await progress.ReadAsync(cancellationToken))
                : TypedResults.NotFound());

        group.MapPost("/backfill/{id:guid}/resume", async Task<Results<Ok<PublicationProgressState>, NotFound>> (
            Guid id,
            PreviewBackfill backfill,
            PublicationProgress progress,
            CancellationToken cancellationToken) =>
            await backfill.ResumeAsync(id, cancellationToken)
                ? TypedResults.Ok(await progress.ReadAsync(cancellationToken))
                : TypedResults.NotFound());

        // What it gives up and what it cannot, both in the answer: ADR 0064 is
        // explicit that a publication prdb accepted stays at prdb, and somebody
        // cancelling is owed that in the same breath. The request's own note
        // carries it, so the page says it without being told twice.
        group.MapPost("/backfill/{id:guid}/cancel", async Task<Results<Ok<PublicationProgressState>, NotFound>> (
            Guid id,
            PreviewBackfill backfill,
            PublicationProgress progress,
            CancellationToken cancellationToken) =>
            await backfill.CancelAsync(id, cancellationToken) is not null
                ? TypedResults.Ok(await progress.ReadAsync(cancellationToken))
                : TypedResults.NotFound());

        // The one act in this tool that deliberately risks a duplicate, and the
        // reason it is a route rather than a retry counter: ADR 0064 weighs the
        // asymmetry and leaves the decision to a person.
        group.MapPost("/{id:guid}/send-again", async Task<Results<Ok<PublicationProgressState>, NotFound>> (
            Guid id,
            PublicationProgress progress,
            CancellationToken cancellationToken) =>
            await progress.SendAgainAsync(id, cancellationToken)
                ? TypedResults.Ok(await progress.ReadAsync(cancellationToken))
                : TypedResults.NotFound());

        group.MapPost("/{id:guid}/leave", async Task<Results<Ok<PublicationProgressState>, NotFound>> (
            Guid id,
            PublicationProgress progress,
            CancellationToken cancellationToken) =>
            await progress.LeaveAsync(id, cancellationToken)
                ? TypedResults.Ok(await progress.ReadAsync(cancellationToken))
                : TypedResults.NotFound());

        return endpoints;
    }
}
