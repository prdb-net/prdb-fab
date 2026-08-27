using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Prdb.Fab.Host.Tests;

/// <summary>
/// The application as <c>Program.cs</c> composes it, pointed at a temporary
/// data directory.
/// </summary>
/// <remarks>
/// ADR 0035 allows a test project to drive the composition root and forbids
/// replacing a service to get past the wiring — so the only things changed here
/// are settings the container sets too: where the database lives, and whether
/// this start is ADR 0010's password reset.
/// </remarks>
public sealed class FabApplication : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string> settings;

    /// <summary>
    /// Whoever holds this deletes the directory when it is done. A restart
    /// hands it over, so that the first application can be stopped without
    /// taking the data with it.
    /// </summary>
    private bool ownsDataDirectory;

    public FabApplication()
        : this(NewDataDirectory(), ownsDataDirectory: true, settings: null)
    {
    }

    private FabApplication(
        string dataDirectory,
        bool ownsDataDirectory,
        IReadOnlyDictionary<string, string>? settings)
    {
        DataDirectory = dataDirectory;
        this.ownsDataDirectory = ownsDataDirectory;
        this.settings = settings ?? new Dictionary<string, string>();
    }

    public string DataDirectory { get; }

    /// <summary>
    /// A second start against the same <c>/data</c>, the way an image is
    /// restarted with a variable added — which is the whole of ADR 0010's
    /// recovery path.
    /// </summary>
    /// <remarks>
    /// The data directory is handed over, so this application can be disposed —
    /// stopping its server and closing its database — without deleting the
    /// installation the restart is supposed to find.
    /// </remarks>
    public FabApplication RestartWith(string setting, string value)
    {
        var successor = new FabApplication(
            DataDirectory,
            ownsDataDirectory,
            new Dictionary<string, string> { [setting] = value });

        ownsDataDirectory = false;

        return successor;
    }

    /// <summary>
    /// A client that has been through ADR 0010's window: the password is set,
    /// and the cookie that came back is on this handler.
    /// </summary>
    public async Task<HttpClient> SignedInClientAsync(string password = "a long enough password")
    {
        var client = CreateClient();

        using (var set = await client.PostAsJsonAsync(
            "/api/access/password", new { password }, TestContext.Current.CancellationToken))
        {
            set.EnsureSuccessStatusCode();

            var verdict = await set.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken);

            // The window closes after the first one, so a second client against
            // the same installation signs in the ordinary way.
            if (verdict?.Outcome == "Set")
            {
                return client;
            }
        }

        using var signIn = await client.PostAsJsonAsync(
            "/api/access/sign-in", new { password }, TestContext.Current.CancellationToken);

        signIn.EnsureSuccessStatusCode();

        return client;
    }

    private sealed record Verdict(string Outcome);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(DataDirectory);
        builder.UseSetting("FAB_DATA_DIRECTORY", DataDirectory);

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || !ownsDataDirectory)
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private static string NewDataDirectory() =>
        Path.Combine(Path.GetTempPath(), "prdb-fab-host-tests", Guid.NewGuid().ToString("n"));
}
