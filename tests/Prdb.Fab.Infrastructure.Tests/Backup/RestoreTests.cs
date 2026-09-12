using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Backup;

/// <summary>
/// ADR 0009's Restore: a populated installation becomes a file, and the file
/// becomes an empty installation somewhere else — with its two mounts in
/// different places, which is the case the whole root-relative scheme exists
/// for.
/// </summary>
public sealed class RestoreTests : IDisposable
{
    /// <summary>
    /// Real directories, because <c>Restores</c> asks the filesystem whether a
    /// root can be used by opening it. ADR 0034 runs this as a specific user and
    /// ADR 0010 answers "writable" by writing, so a root that does not exist is
    /// not a root a test may pretend about.
    /// </summary>
    private readonly string library = NewDirectory();
    private readonly string downloads = NewDirectory();

    public void Dispose()
    {
        Remove(library);
        Remove(downloads);
    }

    /// <summary>
    /// The round trip, and the one assertion that matters: the same logical
    /// records, with both mounts somewhere else entirely.
    /// </summary>
    [Fact]
    public async Task Both_mounts_move_and_the_records_are_the_same_ones()
    {
        var document = await AnExportedInstallationAsync();

        await using var target = await TestDatabase.CreateAsync();

        var act = await RestoreAsync(target, document, new RestoreRoots(library, downloads));

        Assert.Equal(RestoreOutcome.Restored, act.Outcome);

        await using var scope = target.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);

        // The two answered roots are what the database holds, absolute
        // (ADR 0033). The far half of the path mapping is SABnzbd's own view of
        // its filesystem and is no more ours to re-root than its category is.
        Assert.Equal(library, installation.LibraryRoot);
        Assert.Equal(downloads, installation.PathMappingTo);
        Assert.Equal("/remote/complete", installation.PathMappingFrom);

        // Everything else about the installation came back as it was.
        Assert.Equal("prdb-key", installation.PrdbApiKey);
        Assert.Equal("sabnzbd-key", installation.SabnzbdApiKey);
        Assert.Equal("Müller & Söhne", installation.SabnzbdCategory);
        Assert.Equal("hashed", installation.PasswordHash);
        Assert.Equal(OnboardingStep.Complete, installation.OnboardingStep);

        // The Catalogue is cache and this container has none, so the What's New
        // marker points at nothing and What's New counts everything as new —
        // which is true of a fresh installation.
        Assert.Null(installation.WhatsNewObservedVideoId);

        var entry = await context.LibraryEntries.SingleAsync(TestContext.Current.CancellationToken);
        var file = await context.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        var arriving = await context.ArrivingFiles.SingleAsync(TestContext.Current.CancellationToken);
        var logged = await context.OperationLogEntries.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(library, "A Site", "An Entry"), entry.EntryDirectory);
        Assert.Equal(Path.Combine(library, "A Site", "An Entry", "video.mkv"), file.FiledPath);

        // One Operation Log entry crosses both roots, which is why a path names
        // its own root rather than inheriting one from its column.
        Assert.Equal(
            Path.Combine(downloads, "A.Release.1080p", "video.mkv"),
            arriving.SourcePath);
        Assert.Equal(
            Path.Combine(library, "A Site", "An Entry", "video.mkv"),
            arriving.IntendedPath);
        Assert.Equal(
            Path.Combine(downloads, "A.Release.1080p", "video.mkv"),
            logged.PathBefore);
        Assert.Equal(
            Path.Combine(library, "A Site", "An Entry", "video.mkv"),
            logged.PathAfter);

        // ADR 0009 names two of these as load-bearing: without the consumed
        // Release the ranking offers what already failed, and without the
        // reported state a restored installation re-reports every Fulfilment.
        Assert.Equal("release-1", (await context.Downloads.SingleAsync(TestContext.Current.CancellationToken)).DerivedReleaseId);
        Assert.True((await context.ReportedStates.SingleAsync(TestContext.Current.CancellationToken)).IsFulfilled);
        Assert.Equal("os-hash", (await context.ConfirmedAssignments.SingleAsync(TestContext.Current.CancellationToken)).OsHash);
        Assert.Equal("A Rule", (await context.DownloadOriginRules.SingleAsync(TestContext.Current.CancellationToken)).RuleName);
        Assert.Single(await context.ArrivingFileCandidates.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.AccountPreferenceWrites.ToListAsync(TestContext.Current.CancellationToken));

        // The document's admission set replaces this build's five seeded rows
        // rather than joining them: the table is the setting.
        Assert.Equal(
            document.GateAdmissions.Count,
            await context.GateAdmissions.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0010's window: the restored login credential closes both
    /// unauthenticated writes, and it closes them by being the same condition
    /// the first one is gated on.
    /// </summary>
    [Fact]
    public async Task The_restored_credential_closes_the_window()
    {
        var document = await AnExportedInstallationAsync();

        await using var target = await TestDatabase.CreateAsync();

        await RestoreAsync(target, document, new RestoreRoots(library, downloads));

        var again = await RestoreAsync(target, document, new RestoreRoots(library, downloads));

        Assert.Equal(RestoreOutcome.NotOffered, again.Outcome);
    }

    /// <summary>
    /// The first call reads the file and says what it needs; ADR 0009 wants the
    /// question prefilled from what the file recorded rather than asked blind.
    /// </summary>
    [Fact]
    public async Task The_roots_are_asked_for_once_and_prefilled_from_the_file()
    {
        var document = await AnExportedInstallationAsync();

        await using var target = await TestDatabase.CreateAsync();

        var act = await RestoreAsync(target, document, answered: null);

        Assert.Equal(RestoreOutcome.RootsNeeded, act.Outcome);
        Assert.NotNull(act.Summary);
        Assert.True(act.Summary.NeedsLibraryRoot);
        Assert.True(act.Summary.NeedsDownloadDirectory);
        Assert.Equal("/library", act.Summary.RecordedLibraryRoot);
        Assert.Equal("/downloads", act.Summary.RecordedDownloadDirectory);
        Assert.Equal(1, act.Summary.Indexers);
        Assert.Equal(1, act.Summary.LibraryEntries);

        // Reading is not writing.
        await using var scope = target.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Empty(await context.Indexers.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null((await context.Installation.SingleAsync(TestContext.Current.CancellationToken)).PasswordHash);
    }

    /// <summary>
    /// ADR 0009 refuses a non-empty installation and <em>names what it found</em>,
    /// because "not empty" on its own is a dead end for whoever holds the file.
    /// </summary>
    [Fact]
    public async Task A_target_that_holds_something_is_refused_by_name()
    {
        var document = await AnExportedInstallationAsync();

        await using var target = await TestDatabase.CreateAsync();

        await using (var scope = target.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            context.Add(new AutomationRuleRow { Id = Guid.NewGuid(), Name = "Somebody's rule" });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var act = await RestoreAsync(target, document, new RestoreRoots(library, downloads));

        Assert.Equal(RestoreOutcome.NotEmpty, act.Outcome);
        Assert.NotNull(act.Found);
        Assert.Contains(act.Found, line => line.Contains("automation rule", StringComparison.Ordinal));

        await using var after = target.Scope();

        Assert.Null((await after.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken)).PasswordHash);
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_changes_nothing()
    {
        await using var target = await TestDatabase.CreateAsync();
        await using var scope = target.Scope();
        var restores = scope.ServiceProvider.GetRequiredService<Restores>();

        foreach (var text in new[] { "", "not json at all", "{}", "[1, 2, 3]", "{\"formatVersion\": \"one\"}" })
        {
            var act = await restores.ApplyAsync(
                text, new RestoreRoots(library, downloads), TestContext.Current.CancellationToken);

            Assert.Equal(RestoreOutcome.NotABackup, act.Outcome);
        }

        Assert.Null((await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken)).PasswordHash);
    }

    /// <summary>
    /// ADR 0009: a file from a newer tool is refused <em>naming that version</em>
    /// rather than read for the parts this build recognises.
    /// </summary>
    [Fact]
    public async Task A_document_from_a_newer_tool_is_refused_and_names_it()
    {
        var document = await AnExportedInstallationAsync();
        var newer = BackupJson.Write(document)
            .Replace(
                $"\"formatVersion\": {BackupFormat.Version}",
                $"\"formatVersion\": {BackupFormat.Version + 1}",
                StringComparison.Ordinal)
            .Replace("\"toolVersion\": \"", "\"toolVersion\": \"99.0.0-", StringComparison.Ordinal);

        await using var target = await TestDatabase.CreateAsync();
        await using var scope = target.Scope();

        var act = await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            newer, new RestoreRoots(library, downloads), TestContext.Current.CancellationToken);

        Assert.Equal(RestoreOutcome.FromANewerTool, act.Outcome);
        Assert.StartsWith("99.0.0-", act.WrittenBy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The re-rooting refusal. Re-rooting is the one step that must not be able to
    /// place a restored path outside the root the person just pointed at, so a
    /// segment climbing out of it stops the whole Restore rather than that one
    /// row.
    /// </summary>
    [Fact]
    public async Task A_path_climbing_out_of_its_root_stops_the_whole_restore()
    {
        var document = await AnExportedInstallationAsync();
        var tampered = document with
        {
            VideoFiles =
            [
                document.VideoFiles[0] with
                {
                    FiledPath = new BackupPath(BackupRoot.Library, "../elsewhere/video.mkv"),
                },
            ],
        };

        await using var target = await TestDatabase.CreateAsync();

        var act = await RestoreAsync(target, tampered, new RestoreRoots(library, downloads));

        Assert.Equal(RestoreOutcome.PathOutsideItsRoot, act.Outcome);
        Assert.NotNull(act.Found);
        Assert.Contains(act.Found, line => line.Contains("VideoFiles.FiledPath", StringComparison.Ordinal));

        await using var scope = target.Scope();

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .LibraryEntries.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A root the document needs and nobody answered is refused, rather than
    /// leaving a path to be placed by guesswork.
    /// </summary>
    [Fact]
    public async Task A_root_the_document_needs_and_nobody_answered_is_refused()
    {
        var document = await AnExportedInstallationAsync();

        await using var target = await TestDatabase.CreateAsync();

        var act = await RestoreAsync(target, document, new RestoreRoots(library, Downloads: null));

        Assert.Equal(RestoreOutcome.DownloadDirectoryRefused, act.Outcome);
        Assert.Equal(RootRefusal.Unanswered, act.Refusal);
        Assert.Equal(BackupRoot.Downloads, act.RefusedRoot);
    }

    [Fact]
    public async Task A_root_that_is_not_there_is_refused_before_anything_is_written()
    {
        var document = await AnExportedInstallationAsync();

        await using var target = await TestDatabase.CreateAsync();

        var act = await RestoreAsync(
            target,
            document,
            new RestoreRoots(Path.Combine(library, "not-mounted"), downloads));

        Assert.Equal(RestoreOutcome.LibraryRootRefused, act.Outcome);
        Assert.Equal(RootRefusal.Missing, act.Refusal);
    }

    /// <summary>
    /// ADR 0010 refuses a Library inside the Download Directory, and a Restore
    /// is the one moment both are answered at once — which makes it the only
    /// place the pair can be got wrong together.
    /// </summary>
    [Fact]
    public async Task Two_roots_that_overlap_are_refused()
    {
        var document = await AnExportedInstallationAsync();
        var nested = Path.Combine(downloads, "library");

        Directory.CreateDirectory(nested);

        await using var target = await TestDatabase.CreateAsync();

        var act = await RestoreAsync(target, document, new RestoreRoots(nested, downloads));

        Assert.Equal(RestoreOutcome.LibraryRootRefused, act.Outcome);
        Assert.Equal(RootRefusal.OverlapsTheOther, act.Refusal);
    }

    private static async Task<BackupDocument> AnExportedInstallationAsync()
    {
        await using var source = await TestDatabase.CreateAsync();
        await APopulatedInstallation.SeedAsync(source);
        await using var scope = source.Scope();

        return await scope.ServiceProvider.GetRequiredService<Backups>()
            .ReadAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<RestoreAct> RestoreAsync(
        TestDatabase target,
        BackupDocument document,
        RestoreRoots? answered)
    {
        await using var scope = target.Scope();

        return await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            BackupJson.Write(document), answered, TestContext.Current.CancellationToken);
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "prdb-fab-restore", Guid.NewGuid().ToString("n"));

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
