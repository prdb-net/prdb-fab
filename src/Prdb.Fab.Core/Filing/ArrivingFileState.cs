namespace Prdb.Fab.Core.Filing;

/// <summary>Where one Arriving File stands in ADR 0026's durable work sets.</summary>
public enum ArrivingFileState
{
    AwaitingIdentification,
    AwaitingFiling,
    Filing,
    Filed,
}

/// <summary>The first reason an Arriving File stopped before it was filed.</summary>
public enum ArrivingFileReason
{
    IdenticalFile,
    UnreadableQuality,
    Unidentified,
    Duplicate,
    EntryMissing,
}
