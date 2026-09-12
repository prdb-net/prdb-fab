using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

using Prdb.Hashing;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>
/// ADR 0009's background pass: it goes and looks whether the library is still
/// where the library says it is.
/// </summary>
/// <remarks>
/// <para>
/// A work set rather than a schedule (ADR 0032): the set is the Video Files
/// with no answer yet, so a Restore makes the whole library due at once and an
/// ordinary installation only ever has the file it just filed. When the set
/// empties the routine stops finding work, which is what a Restore's
/// verification finishing looks like from here.
/// </para>
/// <para>
/// <strong>It decides nothing.</strong> ADR 0009 is explicit and it is the
/// whole reason this is safe to run unattended: nothing is deleted, nothing is
/// re-fetched, no Fulfilment is retracted, and no Download is started. What is
/// missing or mismatched is counted on the status page and left to the person —
/// because a library mounted somewhere else looks exactly like a library that
/// is gone, and under ADR 0007 the second reading would be a standing
/// instruction to download the collection again.
/// </para>
/// <para>
/// The Library Entry stays exactly where it is either way, which is what keeps
/// an unconfirmed Entry <em>held</em>: <c>AutomaticEligibility</c> reads held
/// off the existence of the row, so leaving the row alone is the mechanism, not
/// a flag that has to be remembered.
/// </para>
/// <para>
/// Bulk, because it reads files and nothing waits on it. It is the cheap hash —
/// ADR 0021's osHash reads 64 KiB from each end — so it does not belong in the
/// File lane, where it would sit behind a copy that may take hours.
/// </para>
/// </remarks>
public sealed class LibraryVerificationRoutine(
    FabDbContext context,
    TimeProvider time,
    ILogger<LibraryVerificationRoutine> logger) : IRoutine
{
    public const string RoutineName = "Library verification";

    /// <summary>
    /// Enough that a restored library of a few thousand files is done in
    /// minutes, small enough that the lane takes turns (ADR 0032) rather than
    /// disappearing into one run.
    /// </summary>
    private const int BatchSize = 100;

    public string Name => RoutineName;

    public Lane Lane => Lane.Bulk;

    /// <summary>
    /// Short, because the cadence only decides how quickly the next batch
    /// starts. What decides whether there is a next batch is the work set.
    /// </summary>
    public TimeSpan Cadence => TimeSpan.FromMinutes(1);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var unanswered = await context.VideoFiles
            .AsNoTracking()
            .Where(file => !context.LibraryVerifications.Any(answer => answer.VideoFileId == file.Id))
            .OrderBy(file => file.Id)
            .Take(BatchSize)
            .Select(file => new { file.Id, file.FiledPath, file.OsHash })
            .ToListAsync(cancellationToken);

        if (unanswered.Count == 0)
        {
            return RunResult.NothingToDo;
        }

        var now = time.GetUtcNow();
        var unconfirmed = 0;

        foreach (var file in unanswered)
        {
            var verification = LibraryVerifications.Of(file.OsHash, Read(file.FiledPath));

            context.LibraryVerifications.Add(new LibraryVerificationRow
            {
                VideoFileId = file.Id,
                Outcome = verification,
                At = now,
            });

            if (LibraryVerifications.IsUnconfirmed(verification))
            {
                unconfirmed++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        if (unconfirmed > 0)
        {
            // ADR 0043: said once per batch rather than once per file, because
            // a mis-mounted library is every file at once and a log line each
            // would bury the one sentence worth reading.
            logger.LogWarning(
                "{Unconfirmed} of {Checked} library file(s) could not be confirmed. Nothing has "
                + "been deleted or fetched; the status page carries the count.",
                unconfirmed,
                unanswered.Count);
        }

        return RunResult.Handled(unanswered.Count);
    }

    /// <summary>
    /// The file's osHash: null where there is no file, empty where there is one
    /// that cannot be read.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole of what the two failing answers are made
    /// of, and it is a question about the filesystem — so it is asked here
    /// rather than in Core (ADR 0035).
    /// </remarks>
    private static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            return OsHash.TryCompute(path, out var computed) ? computed : string.Empty;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
