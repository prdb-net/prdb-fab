using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Sdk.Generated.Models;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class CatalogueAvailabilityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Merge_tombstones_keep_both_Library_identities_files_confirmations_and_backup_history()
    {
        await using var db = await TestDatabase.CreateAsync(also: s => s.AddFabSync());
        var first = Guid.NewGuid();
        var survivor = Guid.NewGuid();
        var stamp = db.Time.GetUtcNow();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        foreach (var id in new[] { first, survivor })
        {
            context.CatalogueVideos.Add(new CatalogueVideoRow { PrdbId = id, Title = id.ToString(), UpdatedAtUtc = stamp });
            context.LibraryEntries.Add(new LibraryEntryRow { VideoId = id, EntryDirectory = id.ToString() });
            context.VideoFiles.Add(new VideoFileRow { Id = Guid.NewGuid(), LibraryEntryVideoId = id, FiledPath = id + ".mp4", QualityLabel = "1080p" });
        }
        context.ConfirmedAssignments.Add(new ConfirmedAssignmentRow { VideoId = first, OsHash = "0123456789ABCDEF", UserHash = "fixture", SentAt = stamp });
        context.ReportedStates.Add(new ReportedStateRow { VideoId = first, UserHash = "fixture", IsFulfilled = true });
        await context.SaveChangesAsync(Token);
        var availability = scope.ServiceProvider.GetRequiredService<CatalogueAvailability>();
        await availability.UnavailableAsync(first, stamp.AddHours(1), null, survivor, Token);
        await availability.UnavailableAsync(first, stamp.AddHours(1), null, survivor, Token);
        await scope.ServiceProvider.GetRequiredService<VideoDetails>().WriteAsync(new VideoDetailDto
        {
            Id = first, Title = "Stale", UpdatedAtUtc = stamp,
        }, Token);
        var firstRow = await context.CatalogueVideos.SingleAsync(v => v.PrdbId == first, Token);
        Assert.False(firstRow.Available);
        Assert.Equal(first.ToString(), firstRow.Title);
        Assert.Equal(survivor, firstRow.MergedIntoId);
        Assert.Equal(2, await context.LibraryEntries.CountAsync(Token));
        Assert.Equal(2, await context.VideoFiles.CountAsync(Token));
        Assert.Equal(first, (await context.ConfirmedAssignments.SingleAsync(Token)).VideoId);
        Assert.Equal(first, (await context.ReportedStates.SingleAsync(Token)).VideoId);
        var backup = await scope.ServiceProvider.GetRequiredService<Backups>().ReadAsync(Token);
        var json = BackupJson.Write(backup);
        Assert.Contains(first.ToString(), json);
        Assert.Contains(survivor.ToString(), json);
        Assert.Contains(first + ".mp4", json);
        Assert.Contains("0123456789ABCDEF", json);
        var choices = new[] { new ReleaseChoice(1, Guid.NewGuid(), "Fixture", "release", "Release", 1, null, null, null) };
        var eligibility = await scope.ServiceProvider.GetRequiredService<AutomaticEligibility>().ForVideoAsync(first, choices, Token);
        Assert.Equal(AutomationDecisionReason.VideoUnavailable, eligibility[1].Reason);
        Assert.Contains(survivor.ToString(), CatalogueAvailability.Notice(false, survivor));
        await scope.ServiceProvider.GetRequiredService<VideoDetails>().WriteAsync(new VideoDetailDto
        {
            Id = first, Title = "Restored", UpdatedAtUtc = stamp.AddHours(2),
        }, Token);
        Assert.True((await context.CatalogueVideos.SingleAsync(v => v.PrdbId == first, Token)).Available);
        Assert.Null((await context.CatalogueVideos.SingleAsync(v => v.PrdbId == first, Token)).MergedIntoId);
        Assert.Equal(2, await context.LibraryEntries.CountAsync(Token));
    }

    [Fact]
    public async Task Unknown_global_deletions_create_no_cache_row_and_minimal_known_tombstones_preserve_metadata()
    {
        await using var db = await TestDatabase.CreateAsync(also: s => s.AddFabSync());
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var id = Guid.NewGuid();
        context.CatalogueVideos.Add(new CatalogueVideoRow { PrdbId = id, Title = "Last title" });
        await context.SaveChangesAsync(Token);
        var availability = scope.ServiceProvider.GetRequiredService<CatalogueAvailability>();
        await availability.UnavailableAsync(Guid.NewGuid(), db.Time.GetUtcNow(), null, null, Token);
        await availability.UnavailableAsync(id, db.Time.GetUtcNow(), null, null, Token);
        var row = await context.CatalogueVideos.SingleAsync(Token);
        Assert.Equal("Last title", row.Title);
        Assert.False(row.Available);
        Assert.NotNull(row.DeletedAtUtc);
        Assert.Null(row.MergedIntoId);
    }
}
