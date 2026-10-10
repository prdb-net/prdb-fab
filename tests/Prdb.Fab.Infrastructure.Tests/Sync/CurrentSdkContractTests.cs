using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Sdk.Generated.Models;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class CurrentSdkContractTests
{
    [Fact]
    public async Task New_catalogue_feeds_deserialize_payloads_and_exact_cursors_through_the_governed_sdk()
    {
        var id = Guid.NewGuid();
        const string stamp = "2026-10-10T10:00:00.1234567Z";
        var prdb = new FakePrdbApi()
            .Answers("/videos/changes", $$$"""{"items":[{"eventType":"deleted","video":{"id":"{{{id}}}","isDeleted":true,"mergedIntoId":"{{{id}}}"}}],"hasMore":false,"nextCursor":{"updatedAtUtc":"{{{stamp}}}","id":"{{{id}}}"}}""")
            .Answers("/sites/changes", $$$"""{"items":[{"eventType":"updated","site":{"id":"{{{id}}}","platformKey":"onlyfans","accountHandle":"creator"}}]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await using var scope = database.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var videos = await gateway.AskAsync("fixture", PrdbWork.Repair,
            (client, token) => client.Videos.Changes.GetAsync(r => r.QueryParameters.Since = DateTimeOffset.MinValue, token), TestContext.Current.CancellationToken);
        var video = Assert.Single(videos!.Items!).Video!;
        Assert.True(video.IsDeleted);
        Assert.Equal(id, video.MergedIntoId);
        Assert.Equal(DateTimeOffset.Parse(stamp), videos.NextCursor!.UpdatedAtUtc);
        Assert.Equal(id, videos.NextCursor.Id);
        var sites = await gateway.AskAsync("fixture", PrdbWork.Repair,
            (client, token) => client.Sites.Changes.GetAsync(r => r.QueryParameters.Since = DateTimeOffset.MinValue, token), TestContext.Current.CancellationToken);
        Assert.Equal("creator", Assert.Single(sites!.Items!).Site!.AccountHandle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_problem_code_survives_both_generated_conflict_schemas(bool wanted)
    {
        var id = Guid.NewGuid();
        var path = wanted ? $"/wanted-videos/{id}" : $"/favorite-sites/{id}";
        var prdb = new FakePrdbApi().RefusesWithBody(path, HttpStatusCode.Conflict, """{"status":409,"code":"site_ignored"}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await using var scope = database.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var error = await Assert.ThrowsAnyAsync<ApiException>(() => gateway.ActAsync("fixture", PrdbWork.Writes,
            (client, token) => wanted ? client.WantedVideos[id].PostAsync(cancellationToken: token) : client.FavoriteSites[id].PostAsync(cancellationToken: token), TestContext.Current.CancellationToken));
        Assert.Equal("site_ignored", error is WantedVideoLimitProblemDetails limit ? limit.Code : Assert.IsType<SiteIgnoredProblemDetails>(error).Code);
    }
}
