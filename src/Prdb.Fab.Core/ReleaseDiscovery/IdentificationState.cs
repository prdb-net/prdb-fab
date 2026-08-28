namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>The seven mutually exclusive places a discovered release can be.</summary>
public enum IdentificationState
{
    Unexamined,
    Unremarkable,
    Awaiting,
    Matched,
    SiteOnly,
    Ambiguous,
    Unknown,
}
