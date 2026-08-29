using System.Reflection;
using System.Text.Json.Serialization;

using Microsoft.OpenApi;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Host.Access;
using Prdb.Fab.Host.Acquisition;
using Prdb.Fab.Host.Catalogue;
using Prdb.Fab.Host.Connections;
using Prdb.Fab.Host.Filing;
using Prdb.Fab.Host.Logging;
using Prdb.Fab.Host.ReleaseDiscovery;
using Prdb.Fab.Host.Scheduling;
using Prdb.Fab.Host.Skeleton;
using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ADR 0040: the build-time generator loads this application to read its
// endpoints and stops it where it would start listening. Everything that
// prepares or runs a real installation is skipped there — a build has no
// business creating a database, and no business turning a lane against one.
var readingTheEndpoints = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

// ADR 0034: the container environment carries only what has to exist before the
// application starts. Everything the user answers lives in the database this
// points at, and /data is where the image mounts it.
var dataDirectory = builder.Configuration["FAB_DATA_DIRECTORY"] ?? "/data";

// ADR 0043. Before anything else, so that a failure in the lines below is
// written the way every other line is.
builder.UseFabLogging(dataDirectory);

// ADR 0042: nothing reads the clock directly. An architecture test fails the
// build over a direct call, which is why this is the only registration of it.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddFabPersistence(dataDirectory);
builder.Services.AddFabScheduling();
builder.Services.AddFabAccess();
builder.Services.AddFabConnections();
builder.Services.AddFabReleaseDiscovery();
builder.Services.AddFabAcquisition();
builder.Services.AddFabFiling();
builder.Services.AddFabSync();

// ADR 0010: a browser session is the only credential, and an unauthenticated
// request gets 401 rather than a redirect.
builder.Services
    .AddAuthentication(SessionAuthentication.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthentication.Scheme,
        configureOptions: null);

// Everything is behind the password unless it says otherwise. Stated as a
// fallback rather than added route by route, because the failure mode of the
// other way round is a route somebody forgot — and ADR 0010 spent a paragraph
// on how narrow the anonymous surface is.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ADR 0038: one hosted service per lane. Sync carries everything that talks to
// prdb (ADR 0014), bulk carries discovery, live carries connection reachability,
// and file serialises content moves that may take hours.
//
// Registered as IHostedService rather than through AddHostedService, which adds
// its registration with TryAddEnumerable and therefore keeps one per
// implementation type. Every lane is the same class, so the second call would be
// dropped and one lane would simply never turn — with nothing anywhere saying
// so, which is the shape of failure ADR 0018 cannot draw.
//
// Not registered at all while the endpoints are being read: a lane turns the
// moment the host starts, and the document generator starts one. Four lanes
// querying a schedule under FAB_DATA_DIRECTORY is a build reaching into a real
// installation's data — the same reason the migrations below are skipped.
if (!readingTheEndpoints)
{
    builder.Services.AddSingleton<IHostedService>(provider =>
        ActivatorUtilities.CreateInstance<LaneWorker>(provider, Lane.Sync));

    builder.Services.AddSingleton<IHostedService>(provider =>
        ActivatorUtilities.CreateInstance<LaneWorker>(provider, Lane.Bulk));

    builder.Services.AddSingleton<IHostedService>(provider =>
        ActivatorUtilities.CreateInstance<LaneWorker>(provider, Lane.Live));

    builder.Services.AddSingleton<IHostedService>(provider =>
        ActivatorUtilities.CreateInstance<LaneWorker>(provider, Lane.File));
}

// ADR 0040: an outcome crosses the contract as its name rather than as its
// position in a C# enum. The number would be stable only for as long as nobody
// reorders the declaration, and it is unreadable in exactly the place a person
// looks — a run log, and the generated types the frontend is built against.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ADR 0040: this describes the API for the build that turns it into the
// frontend's types. Nothing maps it as an endpoint — the document is written to
// a file at build time and committed, and the browser never asks for it.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "prdb-fab",
        Version = "v1",
        Description =
            "The API the browser side of prdb-fab talks to. Generated from the code "
            + "at build time and committed: change an endpoint, not this file.",
    };

    return Task.CompletedTask;
}));

var app = builder.Build();

if (!readingTheEndpoints)
{
    var startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prdb.Fab.Startup");

    // ADR 0044: the first line says which build this is, because ADR 0043 made
    // the log a file people send to strangers and a log without its version is
    // a guessing exercise for whoever reads it.
    startup.LogInformation(
        "prdb-fab {Version} starting. Data directory {DataDirectory}.",
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown",
        dataDirectory);

    // ADR 0039: migrations run before the listener and before the lanes. A
    // migration that cannot be applied stops the tool rather than letting it
    // run against a schema it does not understand (ADR 0004).
    try
    {
        await app.Services.PrepareFabDatabaseAsync();
    }
    catch (DatabaseMigrationException)
    {
        // The migrator has already logged what happened, at critical level.
        // Adding a stack trace on top would only bury that message in the log,
        // which is the one place the user will look.
        await Log.CloseAndFlushAsync();
        return 1;
    }

    // ADR 0038: a routine with no row never runs, so the rows are created with
    // the code rather than by hand.
    await app.Services.PrepareFabReleaseDiscoveryAsync();
    await app.Services.PrepareFabScheduleAsync();

    // ADR 0010: the way back in when the password is lost, taken at the host
    // because a second way in over the network is a second way to configure
    // wrongly.
    await app.Services.ResetPasswordIfAskedAsync(
        builder.Configuration.GetValue<bool>("FAB_RESET_PASSWORD"));
}

// ADR 0043: one line per request, at Debug. Turning Prdb.Fab up turns these on
// with it, so ADR 0034's second documented instruction folds into its first.
app.UseSerilogRequestLogging(options => options.GetLevel = (_, _, _) => Serilog.Events.LogEventLevel.Debug);

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// The one anonymous read. ADR 0010 accepts that nothing mechanical can reach
// the tool, and this reaches nothing: it says the process is answering, which
// is what the container's own smoke test asks and all it is told.
app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .WithTags("Health")
    .AllowAnonymous();

app.MapAccess();

app.MapOnboarding();

app.MapConnections();

app.MapArtwork();

app.MapCatalogue();

app.MapReleaseDiscovery();

app.MapAcquisition();

app.MapFiling();

app.MapSkeleton();

// ADR 0036: routing happens in the browser, so unknown paths return index.html
// and let the frontend decide. Unknown API paths must not — a caller that asked
// a question the API does not have gets that answer, not a page.
app.MapFallback("/api/{*rest}", () => Results.NotFound());

// Anonymous, and it has to be: this is the page that shows the sign-in form.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

await Log.CloseAndFlushAsync();

return 0;

internal sealed record HealthResponse(string Status);

/// <summary>
/// Exposed so that the tests can host the application exactly as it is composed
/// here — ADR 0042: the wiring is the part worth testing, not a copy of it.
/// </summary>
public partial class Program;
