namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// One kind of scheduled work. ADR 0038 left it to the skeleton to decide how a
/// routine's row finds its code, given that a row carries a name <em>and</em> a
/// target: the indexer walk and the wanted sweep exist once per indexer, and
/// the one-shot routines are created at runtime.
/// </summary>
/// <remarks>
/// The answer this project settled on is the simple half of the split. The
/// <see cref="Name"/> binds the row to the code, once, at composition; the
/// target is not a second lookup but an <em>argument</em>, handed to
/// <see cref="RunAsync"/> as the row carries it. So twenty indexer rows share
/// one implementation, and a row created at runtime needs nothing registered
/// for it — only a name that already exists.
/// </remarks>
public interface IRoutine
{
    /// <summary>
    /// What the row says, and the only thing that binds it to this code. Stable
    /// across releases: it is stored.
    /// </summary>
    string Name { get; }

    /// <summary>Which lane's worker turns this.</summary>
    Lane Lane { get; }

    /// <summary>
    /// How long after a run the routine may next be due. ADR 0014's cadence,
    /// and the floor rather than a promise — the lane may be busy.
    /// </summary>
    TimeSpan Cadence { get; }

    /// <summary>
    /// Does the work, or reports that there was none.
    /// </summary>
    /// <param name="target">
    /// What the row named beside the routine, or <see langword="null"/> for a
    /// routine that exists once.
    /// </param>
    Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken);
}
