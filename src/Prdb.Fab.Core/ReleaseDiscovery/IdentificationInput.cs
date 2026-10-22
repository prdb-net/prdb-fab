namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>The complete evidence submitted to prdb, compared exactly.</summary>
public sealed record IdentificationInput(string Filename, long? Filesize = null,
    string? OsHash = null, string? PHash = null, string? Md5 = null);
