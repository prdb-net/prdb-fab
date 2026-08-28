using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// The filing half of ADR 0033's schema: where the entry directory and the filed
/// path each live, and the indexes the ADR names rather than leaves to be
/// discovered. A count in a page header and a work set read every tick are the
/// two reasons those indexes exist, so they are asserted rather than assumed.
/// </summary>
public sealed class FilingSchemaTests
{
    /// <summary>
    /// ADR 0033 corrects the earlier prose: the entry directory sits on the
    /// library entry, because ADR 0012 fixes one entry per video and a second
    /// quality is a second video file under it. A directory column on the file
    /// would be the same value repeated with nothing keeping the copies equal.
    /// </summary>
    [Fact]
    public void The_entry_directory_lives_on_the_entry_and_the_filed_path_on_the_file()
    {
        Assert.NotNull(Table<LibraryEntryRow>().FindProperty(nameof(LibraryEntryRow.EntryDirectory)));
        Assert.Null(Table<VideoFileRow>().FindProperty("EntryDirectory"));

        Assert.NotNull(Table<VideoFileRow>().FindProperty(nameof(VideoFileRow.FiledPath)));
        Assert.Null(Table<LibraryEntryRow>().FindProperty("FiledPath"));
    }

    /// <summary>
    /// The library entry's identity is the prdb video id itself, and the two
    /// edge tables carry the membership they record. ADR 0033 spends a UUIDv7
    /// only where a row has no natural key, because a minted key that duplicates
    /// one is a restore bug waiting for a second installation.
    /// </summary>
    [Fact]
    public void A_row_with_a_natural_key_is_not_given_a_minted_one()
    {
        Assert.Equal(
            [nameof(LibraryEntryRow.VideoId)],
            KeyOf<LibraryEntryRow>());
        Assert.Equal(
            [nameof(ArrivingFileCandidateRow.ArrivingFileId), nameof(ArrivingFileCandidateRow.VideoId)],
            KeyOf<ArrivingFileCandidateRow>());
        Assert.Equal(
            [nameof(GateAdmissionRow.Gate), nameof(GateAdmissionRow.Confidence)],
            KeyOf<GateAdmissionRow>());
        Assert.Equal(
            [
                nameof(ConfirmedAssignmentRow.OsHash),
                nameof(ConfirmedAssignmentRow.VideoId),
                nameof(ConfirmedAssignmentRow.UserHash),
            ],
            KeyOf<ConfirmedAssignmentRow>());

        // The two that mint: an arriving file and a log entry are events, and an
        // event has no natural key that a restore could rely on.
        Assert.Equal([nameof(ArrivingFileRow.Id)], KeyOf<ArrivingFileRow>());
        Assert.Equal([nameof(OperationLogEntryRow.Id)], KeyOf<OperationLogEntryRow>());
    }

    /// <summary>
    /// ADR 0032 makes a routine due when its work set is not empty, which turns
    /// the state column into a `COUNT` on every tick.
    /// </summary>
    [Fact]
    public void The_work_set_state_column_is_indexed() =>
        Assert.True(IsIndexed<ArrivingFileRow>(nameof(ArrivingFileRow.State)));

    /// <summary>
    /// ADR 0022 puts the Review Queue count in the header of every page, and a
    /// queue entry is an arriving file that stopped with a reason. The index is
    /// partial because the rows without one are the overwhelming majority and
    /// none of them is ever counted.
    /// </summary>
    [Fact]
    public void The_review_queue_count_reads_a_partial_index()
    {
        var index = Table<ArrivingFileRow>()
            .GetIndexes()
            .SingleOrDefault(candidate => candidate.Properties
                .Select(property => property.Name)
                .SequenceEqual([nameof(ArrivingFileRow.Reason)]));

        Assert.NotNull(index);
        Assert.False(string.IsNullOrWhiteSpace(index.GetFilter()));
    }

    /// <summary>
    /// Every column a pinning anti-join reads, on the filing side. ADR 0033
    /// makes pinning a query rather than a column, and a query that has to scan
    /// is the reason people reach for the column instead.
    /// </summary>
    [Fact]
    public void Every_column_a_pinning_anti_join_reads_is_indexed()
    {
        Assert.True(IsIndexed<ArrivingFileRow>(nameof(ArrivingFileRow.VideoId)));
        Assert.True(IsIndexed<ArrivingFileCandidateRow>(nameof(ArrivingFileCandidateRow.VideoId)));

        // The library entry needs none: the video id is its key.
        Assert.Equal([nameof(LibraryEntryRow.VideoId)], KeyOf<LibraryEntryRow>());
    }

    /// <summary>ADR 0029: the library entry page reads the log by video.</summary>
    [Fact]
    public void The_operation_log_is_read_by_video() =>
        Assert.True(IsIndexed<OperationLogEntryRow>(nameof(OperationLogEntryRow.VideoId)));

    /// <summary>
    /// ADR 0006's two sets exist as rows rather than as a delimited column, and
    /// the after-download gate arrives admitting the two confidences a fresh
    /// installation acts on alone.
    /// </summary>
    [Fact]
    public async Task The_after_download_gate_arrives_with_its_two_admissions()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var admitted = await context.GateAdmissions
            .Where(row => row.Gate == AfterDownloadGate.Name)
            .Select(row => row.Confidence)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [IdentificationConfidence.Exact, IdentificationConfidence.Strong],
            admitted.OrderBy(value => value.ToString(), StringComparer.Ordinal).ToArray());
    }

    private static IEntityType Table<TRow>()
    {
        var options = new DbContextOptionsBuilder<FabDbContext>()
            .UseSqlite("Data Source=schema-only.db")
            .Options;

        using var context = new FabDbContext(options);

        return context.Model.FindEntityType(typeof(TRow))!;
    }

    private static string[] KeyOf<TRow>() =>
        Table<TRow>().FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray();

    private static bool IsIndexed<TRow>(string property) =>
        Table<TRow>().GetIndexes().Any(index => index.Properties[0].Name == property);
}
