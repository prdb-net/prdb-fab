namespace Prdb.Fab.Core.Filing;

/// <summary>The reason-bound action an open Review Queue entry may offer.</summary>
public enum ReviewQueueAction
{
    FileAs,
    Replace,
    FileAsOnlyCopy,
}

public static class ReviewQueueActions
{
    public static ReviewQueueAction? For(ArrivingFileReason reason) => reason switch
    {
        ArrivingFileReason.Unidentified => ReviewQueueAction.FileAs,
        ArrivingFileReason.Duplicate => ReviewQueueAction.Replace,
        ArrivingFileReason.EntryMissing => ReviewQueueAction.FileAsOnlyCopy,
        _ => null,
    };
}
