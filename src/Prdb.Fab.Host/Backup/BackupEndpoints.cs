using System.Text;

using Microsoft.Net.Http.Headers;

using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Backup;

namespace Prdb.Fab.Host.Backup;

/// <summary>
/// ADR 0009's export: one named action that hands the caller a file.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0040 names the backup's export among the acts that get an endpoint of
/// their own, and a <c>POST</c> is what keeps it one. A <c>GET</c> producing
/// this file would be a link — and therefore something a prefetch, a crawler in
/// a bookmark manager or a mis-click can turn into a copy of every credential
/// this installation holds, sitting in a downloads folder nobody meant to put
/// it in.
/// </para>
/// <para>
/// Authenticated by omission: ADR 0010's fallback policy puts everything behind
/// the password unless it says otherwise, and this does not say otherwise.
/// Restore is the one that does, and for the reason that ADR gives — the
/// credential is inside the file.
/// </para>
/// </remarks>
public static class BackupEndpoints
{
    public static void MapBackup(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/backup").WithTags("Backup");

        group.MapPost("/export", async (
            Backups backups,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var document = await backups.ReadAsync(cancellationToken);

            // ADR 0057: the file is readable throughout, credentials included.
            // So the one thing this response must not do is linger anywhere it
            // was not asked to — a shared proxy, a disk cache, a history entry
            // that re-issues the request. `no-store` is the whole of that
            // instruction; `no-cache` would still permit the store.
            http.Response.Headers[HeaderNames.CacheControl] = "no-store";
            http.Response.Headers[HeaderNames.Pragma] = "no-cache";

            return TypedResults.File(
                Encoding.UTF8.GetBytes(BackupJson.Write(document)),
                BackupFile.MediaType,
                BackupFile.Name(document.WrittenAt));
        });

        // ADR 0010's second unauthenticated write, and the last one there will
        // be. It is anonymous for the reason that ADR gives — the login
        // credential is inside the file, so on a fresh container nobody can be
        // signed in to present it — and it is gated on the same single
        // condition as the first: no password exists yet. The condition is
        // asked of the installation inside `Restores` rather than written here,
        // so that there are not two notions of it.
        group.MapPost("/restore", async (
            RestoreRequest request,
            Restores restores,
            CancellationToken cancellationToken) =>
        {
            var act = await restores.ApplyAsync(
                request.Document ?? string.Empty,
                request.Roots is null ? null : new RestoreRoots(request.Roots.Library, request.Roots.Downloads),
                cancellationToken);

            return TypedResults.Ok(new RestoreVerdict(
                act.Outcome,
                act.Detail,
                act.Summary,
                act.Found ?? [],
                act.Refusal,
                act.RefusedRoot,
                act.WrittenBy));
        }).AllowAnonymous();
    }
}

/// <param name="Document">The file, as text.</param>
/// <param name="Roots">
/// Left out on the first call, which is what asks the tool to read the file and
/// say what it needs; carried on the second, which is the answer. One act in
/// ADR 0040's sense, asked twice — rather than a second anonymous endpoint, in
/// front of an installation that has no password yet.
/// </param>
public sealed record RestoreRequest(string? Document, RestoreRootsRequest? Roots);

/// <param name="Library">The Library root in <em>this</em> container.</param>
/// <param name="Downloads">
/// The Download Directory in this container — the local half of the SABnzbd
/// path mapping. Null where there is none, which ADR 0010 allows.
/// </param>
public sealed record RestoreRootsRequest(string? Library, string? Downloads);

/// <summary>ADR 0040: a verdict is a success with a typed body saying what happened.</summary>
/// <param name="Summary">
/// What the file says about itself, present as soon as it has been read — so the
/// browser can show what is about to be restored while it asks for the roots.
/// </param>
/// <param name="Found">
/// What made this installation non-empty, or which paths could not be placed.
/// Empty otherwise.
/// </param>
/// <param name="WrittenBy">
/// The tool version that wrote a file this build refuses, which is the half of
/// that refusal a person can act on: it names the image to run.
/// </param>
public sealed record RestoreVerdict(
    RestoreOutcome Outcome,
    string Detail,
    BackupSummary? Summary,
    IReadOnlyList<string> Found,
    RootRefusal? Refusal,
    BackupRoot? RefusedRoot,
    string? WrittenBy);
