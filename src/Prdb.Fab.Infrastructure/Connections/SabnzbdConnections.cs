using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// ADR 0010's skippable downloader step: SABnzbd, the category chosen from its
/// own list, and the path mapping that is verified rather than collected.
/// </summary>
public sealed class SabnzbdConnections(
    FabDbContext context,
    SabnzbdGateway sabnzbd,
    ILogger<SabnzbdConnections> logger)
{
    /// <summary>
    /// SABnzbd's own categories, each with the folder its downloads finish
    /// under. The first half of the form, and the key check at the same time.
    /// </summary>
    public async Task<SabnzbdCategories> CategoriesAsync(
        string? url,
        string? apiKey,
        CancellationToken cancellationToken = default) =>
        await sabnzbd.CategoriesAsync(
            url,
            await KeptOrSubmittedAsync(apiKey, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Checks all of it again and stores it. The order is ADR 0010's and it
    /// matters: the category is answered first, because it decides which of
    /// SABnzbd's folders the mapping is being verified against.
    /// </summary>
    /// <param name="downloadDirectory">
    /// The other half of the mapping — the same folder, as this container can
    /// open it. ADR 0010 asks no separate question for the download directory
    /// because this is it: asking twice for one fact is how two answers end up
    /// disagreeing.
    /// </param>
    public async Task<SabnzbdSave> SaveAsync(
        string? url,
        string? apiKey,
        string? category,
        string? downloadDirectory,
        CancellationToken cancellationToken = default)
    {
        var key = await KeptOrSubmittedAsync(apiKey, cancellationToken);

        var categories = await sabnzbd.CategoriesAsync(url, key, cancellationToken);

        if (categories.Outcome is not SabnzbdConnectionOutcome.Saved)
        {
            return new SabnzbdSave(categories.Outcome, CompletedRoot: null);
        }

        var chosen = categories.Categories.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, category, StringComparison.Ordinal));

        if (chosen is null)
        {
            return new SabnzbdSave(SabnzbdConnectionOutcome.UnknownCategory, null);
        }

        var local = (downloadDirectory ?? string.Empty).Trim();

        if (local.Length == 0 || !Path.IsPathRooted(local) || !Directories.Exists(local))
        {
            return new SabnzbdSave(SabnzbdConnectionOutcome.DownloadDirectoryMissing, chosen.CompletedRoot);
        }

        if (!Directories.IsReadable(local))
        {
            return new SabnzbdSave(SabnzbdConnectionOutcome.DownloadDirectoryUnreadable, chosen.CompletedRoot);
        }

        var installation = await context.Installation.SingleAsync(cancellationToken);

        installation.SabnzbdUrl = url!.Trim();
        installation.SabnzbdApiKey = key!;
        installation.SabnzbdCategory = chosen.Name;
        installation.PathMappingFrom = chosen.CompletedRoot;
        installation.PathMappingTo = local;

        // Configuring it is what closes the Gap a skip left behind, and the
        // same write does it. ADR 0018 reads whether a connection was skipped
        // rather than working it out from an empty credential.
        installation.SabnzbdSkipped = false;

        context.Installation.Update(installation);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "SABnzbd is configured on category {Category}, and its completed folder is mapped into "
            + "this container.",
            chosen.Name);

        return new SabnzbdSave(SabnzbdConnectionOutcome.Saved, chosen.CompletedRoot);
    }

    /// <summary>
    /// The key to check with: the one that was typed, or the one that is
    /// already stored when the field came back empty.
    /// </summary>
    /// <remarks>
    /// ADR 0020: keys are write-only, so the field is empty with a marker
    /// saying one is set and saving it empty means unchanged. It is kept across
    /// a changed address on purpose — SABnzbd moving to another port is
    /// ordinary, and making the key be dug out again for it is the friction
    /// that gets keys pasted into text files.
    /// </remarks>
    private async Task<string?> KeptOrSubmittedAsync(string? apiKey, CancellationToken cancellationToken)
    {
        var submitted = (apiKey ?? string.Empty).Trim();

        if (submitted.Length > 0)
        {
            return submitted;
        }

        return await context.Installation
            .Select(row => row.SabnzbdApiKey)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// The download directory, or null when SABnzbd was never configured.
    /// ADR 0010 derives it from the verified mapping rather than asking for it.
    /// </summary>
    public async Task<string?> DownloadDirectoryAsync(CancellationToken cancellationToken = default) =>
        (await context.Installation.SingleAsync(cancellationToken)).PathMappingTo;
}

/// <summary>What happened to the SABnzbd connection that was submitted.</summary>
/// <param name="CompletedRoot">
/// The folder SABnzbd finishes this category's downloads in, as SABnzbd sees it
/// — carried back even on a refusal, because it is what the person is being
/// asked to map.
/// </param>
public sealed record SabnzbdSave(SabnzbdConnectionOutcome Outcome, string? CompletedRoot);
