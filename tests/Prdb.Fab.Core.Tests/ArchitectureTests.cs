using System.Text.RegularExpressions;
using System.Xml.Linq;

using Xunit;

namespace Prdb.Fab.Core.Tests;

/// <summary>
/// ADR 0042's architecture tests: five rules, all of the same kind — violations
/// that compile, run, and look fine.
/// </summary>
/// <remarks>
/// They read the source rather than the compiled assemblies, which ADR 0035
/// settled and ADR 0042 extended to the clock. A declared-but-unused reference
/// compiles away, and that is exactly the one that would otherwise slip through.
/// </remarks>
public sealed partial class ArchitectureTests
{
    /// <summary>
    /// ADR 0035, and ADR 0043's reason for keeping it: a rule in <c>Core</c>
    /// cannot log, so it has to return its reason — and a returned reason is a
    /// value a test can read.
    /// </summary>
    [Fact]
    public void Core_declares_no_dependencies()
    {
        var core = Project("src/Prdb.Fab.Core/Prdb.Fab.Core.csproj");

        Assert.Empty(References(core, "PackageReference"));
        Assert.Empty(References(core, "ProjectReference"));
    }

    /// <summary>
    /// ADR 0035: a test project may drive the composition root — that is the
    /// only way to check the wiring itself — but no library may depend on it.
    /// </summary>
    [Fact]
    public void Nothing_in_src_references_the_host()
    {
        foreach (var project in SourceProjectsExcept("Prdb.Fab.Host.csproj"))
        {
            var references = References(XDocument.Load(project.FullName), "ProjectReference");

            Assert.DoesNotContain(references, reference =>
                reference.EndsWith("Prdb.Fab.Host.csproj", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// ADR 0035: nothing outside <c>Infrastructure</c> reaches a socket, a
    /// database or a file. <c>Path</c> is the exception — it manipulates strings
    /// and touches nothing.
    /// </summary>
    [Fact]
    public void Core_reaches_no_filesystem()
    {
        foreach (var file in SourceFilesUnder("src/Prdb.Fab.Core"))
        {
            var text = CodeIn(file);

            Assert.False(
                FilesystemCall().IsMatch(text),
                $"{file.Name} reaches the filesystem, which ADR 0035 keeps inside Infrastructure.");
        }
    }

    /// <summary>
    /// ADR 0042: nothing reads the clock directly. The argument is a measurement
    /// rather than a principle — <c>prdb-ordeno</c> injects <c>TimeProvider</c>
    /// into every worker and every service it has, and still calls
    /// <c>DateTimeOffset.UtcNow</c> once. One place, in a project that means the
    /// rule, is what a rule without a test is worth.
    /// </summary>
    [Fact]
    public void Nothing_reads_the_clock_directly()
    {
        foreach (var file in SourceFilesUnder("src"))
        {
            var text = CodeIn(file);

            Assert.False(
                ClockCall().IsMatch(text),
                $"{file.Name} reads the clock directly. Inject TimeProvider (ADR 0042).");
        }
    }

    /// <summary>
    /// ADR 0043: a URL is never logged whole, because ADR 0015 records that a
    /// download URL carries the indexer key and ADR 0043 made the log a file
    /// people send to strangers. The redaction lives where the URL is built, so
    /// what this checks is that no message template interpolates a whole one.
    /// </summary>
    [Fact]
    public void No_log_message_carries_a_whole_url()
    {
        foreach (var file in SourceFilesUnder("src"))
        {
            var text = CodeIn(file);

            Assert.False(
                LoggedUrl().IsMatch(text),
                $"{file.Name} logs something named like a whole URL. Log the transport and the "
                + "host instead (ADR 0043).");
        }
    }

    /// <summary>
    /// ADR 0041, and what ADR 0014 rests on: the governor is a handler on the
    /// prdb transport, so it only governs requests made through a client built
    /// on that transport. One place builds one, and a second one built anywhere
    /// else would be a bypass that spends the rate limit without ever appearing
    /// to.
    /// </summary>
    [Fact]
    public void Only_the_gateway_builds_a_prdb_client()
    {
        foreach (var file in SourceFilesUnder("src"))
        {
            if (file.Name == "PrdbGateway.cs")
            {
                continue;
            }

            Assert.False(
                PrdbClient().IsMatch(CodeIn(file)),
                $"{file.Name} builds its own prdb client, which is a request the governor "
                + "never sees (ADR 0014, ADR 0041). Go through PrdbGateway.");
        }
    }

    /// <summary>
    /// A file with its comments taken out.
    /// </summary>
    /// <remarks>
    /// Not a parser, and it does not need to be. What it prevents is the failure
    /// these tests are most likely to die of: a comment that <em>explains</em>
    /// one of these rules, quoting the very call it forbids, turning the rule
    /// into a nuisance and then into a <c>[Fact(Skip)]</c>. Every ADR here
    /// argues in prose, so the prose has to be allowed to name what it argues
    /// about.
    /// </remarks>
    private static string CodeIn(FileInfo file) =>
        Comment().Replace(File.ReadAllText(file.FullName), string.Empty);

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    // Anything that names the filesystem, except Path, which only manipulates
    // strings. Matched on the type name so that a using directive is not needed
    // to be caught.
    [GeneratedRegex(@"\b(File|Directory|FileInfo|DirectoryInfo|FileStream|DriveInfo)\.")]
    private static partial Regex FilesystemCall();

    [GeneratedRegex(@"\b(DateTime|DateTimeOffset)\.(Now|UtcNow|Today)\b")]
    private static partial Regex ClockCall();

    // Anything that builds a client for prdb: the SDK's factory, and the client
    // type itself. Matched on the name so that a using directive is not needed
    // to be caught.
    [GeneratedRegex(@"\bPrdbClient\w*\b")]
    private static partial Regex PrdbClient();

    // A logging call whose template interpolates something whose name ends in
    // Url or Uri. {Host} and {Transport} are what ADR 0043 asks for instead.
    [GeneratedRegex(@"Log(Trace|Debug|Information|Warning|Error|Critical)\([^;]*\{\w*(Url|Uri)\}")]
    private static partial Regex LoggedUrl();

    private static IEnumerable<string> References(XDocument project, string kind) =>
        project.Descendants(kind)
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', '/'));

    private static XDocument Project(string relativePath) =>
        XDocument.Load(Path.Combine(RepositoryRoot().FullName, relativePath));

    private static IEnumerable<FileInfo> SourceProjectsExcept(string fileName) =>
        new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "src"))
            .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .Where(project => project.Name != fileName);

    /// <summary>
    /// The hand-written source under <paramref name="relativePath"/>. Generated
    /// migrations are skipped: they are output, and nobody edits them into a
    /// violation.
    /// </summary>
    private static IEnumerable<FileInfo> SourceFilesUnder(string relativePath) =>
        new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, relativePath))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Prdb.Fab.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"No repository root above {AppContext.BaseDirectory}.");
    }
}
