namespace Prdb.Fab.Core.Filing;

/// <summary>What ADR 0021's one Probe learned about the file as a video.</summary>
public enum ProbeOutcome
{
    Read,
    SourceMissing,
    NoVideoStream,
    Unreadable,
    TimedOut,
}
