namespace Prdb.Fab.Core.Catalogue;

/// <summary>The fixed request sizes shared by Catalogue discovery and repair.</summary>
public static class CatalogueRead
{
    /// <summary><c>GET /videos</c>'s largest page.</summary>
    public const int APage = 100;

    /// <summary><c>POST /videos/batch</c>'s limit.</summary>
    public const int ABatch = 50;
}
