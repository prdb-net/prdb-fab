using Serilog;
using Serilog.Events;

namespace Prdb.Fab.Host.Logging;

/// <summary>
/// ADR 0043: Serilog with two sinks — the container's stdout, and a bounded
/// rolling file on the data volume the user already mounts.
/// </summary>
public static class FabLogging
{
    /// <summary>Ten megabytes a file, ten files: a hard ceiling near a hundred.</summary>
    private const long BytesPerFile = 10L * 1024 * 1024;

    private const int FilesKept = 10;

    /// <summary>
    /// One template for both sinks. Plain text, because ADR 0043's reader is a
    /// person skimming a file they are about to attach to a message — and a
    /// person who can therefore see what is in it before they send it.
    /// </summary>
    private const string Template =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Replaces the default logging providers with Serilog, keeping the levels
    /// where ADR 0034 published them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mechanism ADR 0043 left to the skeleton, and it has to be this one.
    /// The obvious shortcut — register Serilog as an ordinary provider and let
    /// the platform filter — does not work: Serilog's own
    /// <c>AddSerilog</c> installs <c>AddFilter&lt;SerilogLoggerProvider&gt;(null,
    /// Trace)</c>, and a provider-specific rule beats the rules read from
    /// configuration. Everything arrives at Verbose and
    /// <c>Logging__LogLevel__Prdb.Fab</c> does nothing. That is exactly the
    /// silent failure ADR 0034 spent its <c>bash</c> paragraph on, so the levels
    /// are read out of the platform's own section here and handed to Serilog.
    /// </para>
    /// <para>
    /// <c>Default</c> becomes the pipeline minimum and every other key becomes an
    /// override, so <c>Logging__LogLevel__Prdb.Fab=Debug</c> keeps working
    /// untouched and means what it has always meant. Serilog's overrides apply
    /// below the minimum as well as above it — the sub-logger a source context
    /// produces carries the override's own minimum — which is what lets the one
    /// knob turn a single category up without turning the whole pipeline up
    /// with it.
    /// </para>
    /// <para>
    /// Unbuffered, and no <c>Serilog.Sinks.Async</c>: the lines immediately
    /// before a crash are the reason the file exists.
    /// </para>
    /// </remarks>
    public static void UseFabLogging(this WebApplicationBuilder builder, string dataDirectory)
    {
        var levels = builder.Configuration.GetSection("Logging:LogLevel");

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(LevelOf(levels["Default"]) ?? LogEventLevel.Information);

        foreach (var level in levels.GetChildren())
        {
            if (level.Key == "Default" || LevelOf(level.Value) is not { } configured)
            {
                continue;
            }

            configuration.MinimumLevel.Override(level.Key, configured);
        }

        var logger = configuration
            // Serilog's request logging writes through Serilog rather than
            // through the platform, so it is not reached by anything above.
            // ADR 0043 puts requests at Debug: ADR 0036 has TanStack Query
            // polling, so at Information an open browser tab is a steady drip.
            .MinimumLevel.Override("Serilog.AspNetCore", RequestLoggingLevel(builder.Configuration))
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: Template)
            .WriteTo.File(
                path: Path.Combine(dataDirectory, "logs", "prdb-fab-.log"),
                outputTemplate: Template,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: BytesPerFile,
                retainedFileCountLimit: FilesKept,
                shared: false)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(logger, dispose: true);
    }

    /// <summary>
    /// Debug when the application's own category is turned up, and Information
    /// otherwise — which suppresses the Debug-level request lines rather than
    /// filtering them one at a time.
    /// </summary>
    private static LogEventLevel RequestLoggingLevel(IConfiguration configuration)
    {
        var configured = LevelOf(configuration["Logging:LogLevel:Prdb.Fab"])
            ?? LevelOf(configuration["Logging:LogLevel:Default"]);

        return configured <= LogEventLevel.Debug ? LogEventLevel.Debug : LogEventLevel.Information;
    }

    /// <summary>
    /// The platform's level names, in Serilog's vocabulary. <c>None</c> has no
    /// counterpart, so it becomes the highest level there is, which is the
    /// nearest thing to off.
    /// </summary>
    private static LogEventLevel? LevelOf(string? name) => name?.ToLowerInvariant() switch
    {
        "trace" => LogEventLevel.Verbose,
        "debug" => LogEventLevel.Debug,
        "information" => LogEventLevel.Information,
        "warning" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "critical" => LogEventLevel.Fatal,
        "none" => LogEventLevel.Fatal,
        _ => null,
    };
}
