using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// ADR 0010's second mandatory step: one path, and three checks on it.
/// </summary>
public sealed class LibraryRoots(
    FabDbContext context,
    ILogger<LibraryRoots> logger)
{
    /// <summary>
    /// Checks the path and stores it. Two of the three checks refuse; the third
    /// warns and stores anyway.
    /// </summary>
    public async Task<LibraryRootSave> SaveAsync(
        string? libraryRoot,
        CancellationToken cancellationToken = default)
    {
        var path = (libraryRoot ?? string.Empty).Trim();
        var installation = await context.Installation.SingleAsync(cancellationToken);

        // ADR 0010: the download directory is the one derived from the verified
        // path mapping, and when SABnzbd was skipped there is none — in which
        // case the second and third checks have nothing to compare against.
        var downloads = installation.PathMappingTo;

        if (LibraryRoot.Refuse(path, downloads) is { } refusal)
        {
            return new LibraryRootSave(refusal);
        }

        if (!Directories.Exists(path))
        {
            return new LibraryRootSave(LibraryRootOutcome.Missing);
        }

        // ADR 0034 runs this container as PUID:PGID, so "writable" is a question
        // about that user and it is answered by writing.
        if (!Directories.IsWritable(path))
        {
            return new LibraryRootSave(LibraryRootOutcome.NotWritable);
        }

        var outcome = downloads is { Length: > 0 } && Directories.OnTheSameFilesystem(path, downloads) is false
            ? LibraryRootOutcome.SavedWithWarning
            : LibraryRootOutcome.Saved;

        installation.LibraryRoot = path;

        context.Installation.Update(installation);
        await context.SaveChangesAsync(cancellationToken);

        if (outcome is LibraryRootOutcome.SavedWithWarning)
        {
            // ADR 0026 puts the choice between a rename and a copy in Core as a
            // rule and both executions in Infrastructure. This is the moment the
            // installation is known to be on the expensive branch, and saying so
            // once here is cheaper than a person discovering it from a filing
            // routine that is unaccountably slow.
            logger.LogWarning(
                "The library root and the download directory are on different filesystems, so "
                + "filing will copy and delete rather than rename.");
        }

        logger.LogInformation("The library root has been stored.");

        return new LibraryRootSave(outcome);
    }
}

/// <summary>What happened to the library root that was submitted.</summary>
public sealed record LibraryRootSave(LibraryRootOutcome Outcome);
