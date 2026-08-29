namespace Prdb.Fab.Core.Scheduling;

public enum RunNowOutcome
{
    Accepted,
    Deferred,
    Refused,
}

public sealed record RunNowVerdict(RunNowOutcome Outcome, string Detail)
{
    public bool Accepted => Outcome == RunNowOutcome.Accepted;
}
