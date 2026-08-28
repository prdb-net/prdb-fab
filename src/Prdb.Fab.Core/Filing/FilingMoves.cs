namespace Prdb.Fab.Core.Filing;

/// <summary>How ADR 0026 puts a Video File into its final name.</summary>
public enum FilingMove
{
    Rename,
    CopyVerifyDelete,
}

public static class FilingMoves
{
    /// <summary>
    /// An unknown device comparison takes the cheap path and lets the operating
    /// system answer; a known second filesystem takes the verified copy path.
    /// </summary>
    public static FilingMove For(bool? sameFilesystem) =>
        sameFilesystem is false ? FilingMove.CopyVerifyDelete : FilingMove.Rename;
}
