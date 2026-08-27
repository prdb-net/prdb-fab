namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// A routine that exists to finish something and then stop.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014: <em>bootstrap is not a state of the application</em>. The first
/// walk of an indexer, the What's New backfill and the actors drain are
/// routines that carry a position, run beside the recurring ones from the first
/// minute, and retire when they are done. That is a row rather than a flag
/// somewhere, so the run log shows the bootstrap as its own thing and a person
/// can see it working.
/// </para>
/// <para>
/// A row that retires is deleted, and the one thing that has to be arranged
/// around that is the registrar: it creates a row for every routine the build
/// knows about, so without this interface a retired bootstrap would come back
/// on the next restart and start over. <see cref="StartsAsync"/> is what a
/// routine answers <em>no</em> to when it has already finished — asked only
/// where there is no row to find, which is both the first start and every start
/// after the retirement.
/// </para>
/// </remarks>
public interface IOneShot
{
    /// <summary>
    /// Whether this routine still has to be given a row.
    /// </summary>
    /// <remarks>
    /// Answered from the position the routine keeps rather than from a column
    /// of its own, so there is one truth about how far it has come and it is
    /// the same one the routine resumes from.
    /// </remarks>
    Task<bool> StartsAsync(CancellationToken cancellationToken);
}
