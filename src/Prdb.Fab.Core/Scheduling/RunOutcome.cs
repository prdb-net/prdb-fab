namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// What the run log records about a run that happened. ADR 0038 settled that
/// there are three of these and not two.
/// </summary>
public enum RunOutcome
{
    /// <summary>The routine did its work.</summary>
    Succeeded,

    /// <summary>
    /// It failed. Three consecutive of these are ADR 0014's Gap, which is how a
    /// failure reaches ADR 0018's status page without a condition of its own.
    /// </summary>
    Failed,

    /// <summary>
    /// It was still working when the container was asked to stop. ADR 0038:
    /// neither a success nor a failure, moves no counter, and recorded anyway —
    /// a filing that ran for three hours and was interrupted by a restart is
    /// exactly what somebody goes to the run log to find.
    /// </summary>
    Interrupted,
}
