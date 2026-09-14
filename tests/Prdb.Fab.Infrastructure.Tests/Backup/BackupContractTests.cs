using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Backup;

/// <summary>
/// What the portable contract promises, held against a real document rather
/// than described: every supported format still restores, a newer one does not,
/// nothing that can be fetched again is in the file, and what is in the file is
/// in it readably.
/// </summary>
/// <remarks>
/// ADR 0057 inverts the half of this that used to be about secrecy. There are no
/// encrypted fields to prove unreadable; the credentials are in the document by
/// decision, and the assertion runs the other way — they are present and plain,
/// so a future build cannot quietly start wrapping what the documentation calls
/// a file a person can open.
/// </remarks>
public sealed class BackupContractTests : IDisposable
{
    /// <summary>
    /// The recorded document of every format this build still reads. A new
    /// format adds a file here rather than a branch anywhere — and the old one
    /// stays, because what this asserts is that a document written before the
    /// change still restores.
    /// </summary>
    public static TheoryData<string> SupportedFormats() =>
        ["format-1.json", "format-2.json", "format-3.json", "format-4.json"];

    /// <summary>The newest of them, which is the shape this build writes.</summary>
    private const string TheCurrentFormat = "format-4.json";

    private readonly string library = NewDirectory();
    private readonly string downloads = NewDirectory();

    public void Dispose()
    {
        Remove(library);
        Remove(downloads);
    }

    /// <summary>
    /// A file written by an older build still restores, whole. This is the one
    /// test that would notice a DTO renamed without the format being thought
    /// about — everything else in the suite writes the document it then reads.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedFormats))]
    public async Task Every_supported_format_still_restores(string fixture)
    {
        var recorded = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Backup", "Recorded", fixture),
            TestContext.Current.CancellationToken);

        await using var target = await TestDatabase.CreateAsync();
        await using var scope = target.Scope();

        var act = await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            recorded, new RestoreRoots(library, downloads), TestContext.Current.CancellationToken);

        Assert.Equal(RestoreOutcome.Restored, act.Outcome);

        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("prdb-key", installation.PrdbApiKey);
        Assert.Equal(OnboardingStep.Complete, installation.OnboardingStep);
        Assert.Equal("An Indexer", (await context.Indexers.SingleAsync(TestContext.Current.CancellationToken)).Name);
        Assert.Equal(1, await context.LibraryEntries.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.VideoFiles.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.ArrivingFiles.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.OperationLogEntries.CountAsync(TestContext.Current.CancellationToken));

        // The two ADR 0009 calls load-bearing, checked in the format that has
        // to keep carrying them.
        Assert.True((await context.ReportedStates.SingleAsync(TestContext.Current.CancellationToken)).IsFulfilled);
        Assert.Equal(
            "release-1",
            (await context.Downloads.SingleAsync(TestContext.Current.CancellationToken)).DerivedReleaseId);
    }

    /// <summary>
    /// The recorded document is still the shape this build writes. Not the same
    /// assertion as the one above: that one says an old file can be read, this
    /// one says the format has not moved without its version moving with it.
    /// </summary>
    [Fact]
    public async Task The_document_this_build_writes_is_the_recorded_shape()
    {
        var recorded = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Backup", "Recorded", TheCurrentFormat),
            TestContext.Current.CancellationToken));

        var written = JsonDocument.Parse(BackupJson.Write(await AnExportAsync()));

        Assert.Equal(BackupFormat.Version, written.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(Shape(recorded.RootElement), Shape(written.RootElement));
    }

    /// <summary>
    /// ADR 0064: a document written before there was a publication history
    /// restores as an installation that has published nothing.
    /// </summary>
    /// <remarks>
    /// The empty direction is the safe one and it is worth saying why, because
    /// the other direction is the one that cannot be undone. A restored row
    /// this installation never sent would silently suppress a preview prdb's
    /// population is short of; a missing row republishes one, and a duplicate
    /// picture in a public gallery has no retraction. So the step supplies an
    /// empty section — which is what a format-3 installation was — and the
    /// history begins from the Library it actually filed.
    /// </remarks>
    [Fact]
    public async Task A_document_from_before_the_publication_history_restores_with_none()
    {
        var recorded = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Backup", "Recorded", "format-3.json"),
            TestContext.Current.CancellationToken);

        await using var target = await TestDatabase.CreateAsync();
        await using var scope = target.Scope();

        var act = await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            recorded, new RestoreRoots(library, downloads), TestContext.Current.CancellationToken);

        Assert.Equal(RestoreOutcome.Restored, act.Outcome);

        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(0, await context.PreviewPublications.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0064: a document written before the third Reporting channel existed
    /// restores with it switched on and <em>unexplained</em>.
    /// </summary>
    /// <remarks>
    /// Both halves are the decision. On, because that is the shipped default
    /// and an absent boolean would otherwise deserialise as <c>false</c> — a
    /// value nobody chose. Unexplained, because the person restoring has never
    /// been shown what this channel publishes, which puts them exactly where an
    /// installation that upgraded stands: nothing is sent until they have.
    /// </remarks>
    [Fact]
    public async Task A_document_from_before_the_third_channel_restores_it_on_and_unexplained()
    {
        var recorded = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Backup", "Recorded", "format-2.json"),
            TestContext.Current.CancellationToken);

        await using var target = await TestDatabase.CreateAsync();
        await using var scope = target.Scope();

        var act = await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            recorded, new RestoreRoots(library, downloads), TestContext.Current.CancellationToken);

        Assert.Equal(RestoreOutcome.Restored, act.Outcome);

        var installation = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(installation.PublishGeneratedPreviews);
        Assert.Null(installation.PreviewPublicationExplainedAt);
    }

    /// <summary>
    /// And a document that <em>was</em> written by a build with the channel
    /// carries the stamp over, because the person restoring is the person it
    /// was explained to.
    /// </summary>
    [Fact]
    public async Task A_document_that_carries_the_stamp_restores_already_explained()
    {
        var recorded = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Backup", "Recorded", TheCurrentFormat),
            TestContext.Current.CancellationToken);

        await using var target = await TestDatabase.CreateAsync();
        await using var scope = target.Scope();

        await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            recorded, new RestoreRoots(library, downloads), TestContext.Current.CancellationToken);

        var installation = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(installation.PreviewPublicationExplainedAt);
    }

    /// <summary>
    /// ADR 0009: a document from a newer tool is refused <em>naming that
    /// version</em>, rather than read for the parts this build recognises.
    /// </summary>
    [Fact]
    public async Task A_newer_writer_is_refused_rather_than_partly_read()
    {
        var newer = BackupJson.Write(await AnExportAsync()).Replace(
            $"\"formatVersion\": {BackupFormat.Version}",
            $"\"formatVersion\": {BackupFormat.Version + 1}",
            StringComparison.Ordinal);

        await using var target = await TestDatabase.CreateAsync();
        await using var scope = target.Scope();

        var act = await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            newer, new RestoreRoots(library, downloads), TestContext.Current.CancellationToken);

        Assert.Equal(RestoreOutcome.FromANewerTool, act.Outcome);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Indexers.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The scan. Three things must not be in the file, and one class of thing
    /// must be.
    /// </summary>
    [Fact]
    public async Task The_document_holds_the_credentials_and_no_local_root_and_no_cache()
    {
        var written = BackupJson.Write(await AnExportAsync());

        // ADR 0057: present, plain, and a string rather than an object — an
        // encrypted field would need a nonce beside it, so the shape is what
        // says nothing has been wrapped.
        var installation = JsonDocument.Parse(written).RootElement.GetProperty("installation");

        foreach (var secret in new[] { "passwordHash", "prdbApiKey", "sabnzbdApiKey" })
        {
            Assert.Equal(JsonValueKind.String, installation.GetProperty(secret).ValueKind);
        }

        Assert.Contains("\"apiKey\": \"indexer-key\"", written, StringComparison.Ordinal);
        Assert.Contains("\"sabnzbdApiKey\": \"sabnzbd-key\"", written, StringComparison.Ordinal);

        // No cache row. The Catalogue title and the cached Release title are
        // the two the seed puts there so their absence is something rather
        // than nothing.
        Assert.DoesNotContain("A Catalogue Title", written, StringComparison.Ordinal);
        Assert.DoesNotContain("A Cached Release", written, StringComparison.Ordinal);
        Assert.DoesNotContain("https://indexer.invalid/nzb", written, StringComparison.Ordinal);

        // No local absolute root outside the two places one belongs: the
        // Installation's own two answers, which Restore replaces, and a path no
        // root covered, which ADR 0033 lets travel whole. Every other path in
        // the document is root-relative, so the roots themselves must not
        // appear in one.
        foreach (var (where, path) in Restore.Paths(BackupJson.Read(written)!))
        {
            Assert.False(
                path.Path.StartsWith("/library", StringComparison.Ordinal)
                || path.Path.StartsWith("/downloads", StringComparison.Ordinal),
                $"{where} carries a local absolute root: {path.Path}");
        }
    }

    /// <summary>
    /// ADR 0033's boundary is a property of the model, and the document has to
    /// keep up with it: a table that changes its export class fails a test until
    /// the format handles it.
    /// </summary>
    /// <remarks>
    /// The mechanism is <c>BackupSections</c>, which <c>BackupTests</c> exercises
    /// in both directions. What is asserted here is that it actually runs on the
    /// way out — a check nothing calls is a check that does not bite.
    /// </remarks>
    [Fact]
    public async Task The_boundary_is_checked_on_every_export()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Empty(BackupSections.Unaccounted(context.Model));

        // And the pairing covers the model this build actually runs against,
        // rather than a model assembled in a test.
        Assert.Equal(
            context.Model.GetEntityTypes().Count(entity => Equals(
                entity.FindAnnotation(ExportClassDeclarations.Annotation)?.Value,
                ExportClass.Exported)),
            BackupSections.Carried.Count);
    }

    private static async Task<BackupDocument> AnExportAsync()
    {
        await using var source = await TestDatabase.CreateAsync();
        await APopulatedInstallation.SeedAsync(source);
        await using var scope = source.Scope();

        return await scope.ServiceProvider.GetRequiredService<Backups>()
            .ReadAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The document's shape: every property path, without the values. What a
    /// format break looks like, and what a different installation does not.
    /// </summary>
    private static IReadOnlyList<string> Shape(JsonElement element, string at = "")
    {
        var found = new List<string>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var path = at.Length == 0 ? property.Name : $"{at}.{property.Name}";

                    found.Add(path);
                    found.AddRange(Shape(property.Value, path));
                }

                break;

            // One element is enough: ADR 0033 exports a table whole, so every
            // row of a section is the same shape as every other.
            case JsonValueKind.Array when element.GetArrayLength() > 0:
                found.AddRange(Shape(element[0], $"{at}[]"));
                break;
        }

        return [.. found.Distinct().Order(StringComparer.Ordinal)];
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "prdb-fab-contract", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(path);

        return path;
    }

    private static void Remove(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
