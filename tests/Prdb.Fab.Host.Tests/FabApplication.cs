using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prdb.Fab.Host.Tests;

/// <summary>
/// The application as <c>Program.cs</c> composes it, pointed at a temporary
/// data directory.
/// </summary>
/// <remarks>
/// ADR 0035 allows a test project to drive the composition root and forbids
/// replacing a service to get past the wiring — so the only thing changed here
/// is where the database lives, which is a setting the container sets too.
/// </remarks>
public sealed class FabApplication : WebApplicationFactory<Program>
{
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "prdb-fab-host-tests", Guid.NewGuid().ToString("n"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(dataDirectory);
        builder.UseSetting("FAB_DATA_DIRECTORY", dataDirectory);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
