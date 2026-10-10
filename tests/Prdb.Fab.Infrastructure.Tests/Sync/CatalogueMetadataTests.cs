using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Sdk.Generated.Models;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class CatalogueMetadataTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Detail_corrections_refresh_the_same_site_and_reassign_video_without_changing_filed_identity()
    {
        await using var db = await TestDatabase.CreateAsync(also: s => s.AddFabSync());
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<VideoDetails>();
        var id = Guid.NewGuid();
        var site = Guid.NewGuid();
        var stamp = db.Time.GetUtcNow();
        var detail = new VideoDetailDto { Id = id, Title = "Classic", UpdatedAtUtc = stamp,
            Site = new VideoDetailSiteDto { Id = site, Title = "Studio", Network = new VideoDetailNetworkDto { Title = "Studio Network" } } };
        await writer.WriteAsync(detail, Token);
        var video = await context.CatalogueVideos.SingleAsync(Token);
        var local = video.Id;
        var siteLocal = video.SiteId;
        Assert.Null(video.Description);
        Assert.Null(video.StashdbSceneId);
        Assert.Null((await context.CatalogueSites.SingleAsync(Token)).PlatformId);
        context.LibraryEntries.Add(new LibraryEntryRow { VideoId = id, EntryDirectory = "original-directory" });
        context.VideoFiles.Add(new VideoFileRow { Id = Guid.NewGuid(), LibraryEntryVideoId = id, FiledPath = "original-file.mp4", QualityLabel = "1080p" });
        context.ConfirmedAssignments.Add(new ConfirmedAssignmentRow { VideoId = id, OsHash = "0123456789ABCDEF", UserHash = "fixture", SentAt = stamp });
        await context.SaveChangesAsync(Token);
        detail.Description = "<script>alert('description')</script>\nA second line";
        detail.StashdbSceneId = Guid.NewGuid();
        detail.UpdatedAtUtc = stamp.AddMinutes(1);
        detail.Site.Title = "Creator";
        detail.Site.Url = "https://example.test/creator";
        detail.Site.PlatformId = Guid.NewGuid();
        detail.Site.PlatformKey = "platform";
        detail.Site.PlatformTitle = "Creator Platform";
        detail.Site.AccountHandle = "creator";
        await writer.WriteAsync(detail, Token);
        video = await context.CatalogueVideos.SingleAsync(Token);
        Assert.Equal(siteLocal, video.SiteId);
        var corrected = await context.CatalogueSites.SingleAsync(Token);
        Assert.Equal("Creator", corrected.Title);
        Assert.Equal("Studio Network", corrected.Network);
        Assert.Equal("Creator Platform", corrected.PlatformTitle);
        detail.Site = new VideoDetailSiteDto { Id = Guid.NewGuid(), Title = "Reassigned" };
        detail.UpdatedAtUtc = stamp.AddMinutes(2);
        await writer.WriteAsync(detail, Token);
        video = await context.CatalogueVideos.SingleAsync(Token);
        Assert.Equal(local, video.Id);
        Assert.NotEqual(siteLocal, video.SiteId);
        Assert.Equal("original-directory", (await context.LibraryEntries.SingleAsync(Token)).EntryDirectory);
        Assert.Equal("original-file.mp4", (await context.VideoFiles.SingleAsync(Token)).FiledPath);
        Assert.Equal(id, (await context.ConfirmedAssignments.SingleAsync(Token)).VideoId);
        Assert.Equal(detail.Description, video.Description);
        Assert.Equal(detail.StashdbSceneId, video.StashdbSceneId);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//example.test/path")]
    [InlineData("https://user:secret@example.test/")]
    [InlineData("https://example.test/\npath")]
    [InlineData("")]
    public void Unsafe_or_relative_site_links_are_not_presented(string url) => Assert.Null(SiteMetadata.ExternalUrl(url));

    [Fact]
    public void Absolute_web_links_remain_usable() => Assert.Equal("https://example.test/path", SiteMetadata.ExternalUrl(" https://example.test/path "));
}
