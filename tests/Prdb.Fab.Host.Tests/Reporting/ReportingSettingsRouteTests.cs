using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Reporting;

public sealed class ReportingSettingsRouteTests
{
    [Fact]
    public async Task The_two_channels_are_independent_and_default_off()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var initial = await client.GetFromJsonAsync<State>(
            "/api/settings/reporting",
            TestContext.Current.CancellationToken);
        Assert.False(initial!.ReportFulfilments);
        Assert.False(initial.ReportConfirmedAssignments);
        Assert.Equal(0, initial.FulfilmentBacklog);
        Assert.Equal(0, initial.ConfirmedAssignmentBacklog);

        using var saved = await client.PostAsJsonAsync(
            "/api/settings/reporting",
            new { reportFulfilments = true, reportConfirmedAssignments = false },
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();

        var after = await saved.Content.ReadFromJsonAsync<State>(TestContext.Current.CancellationToken);
        Assert.True(after!.ReportFulfilments);
        Assert.False(after.ReportConfirmedAssignments);

        var reread = await client.GetFromJsonAsync<State>(
            "/api/settings/reporting",
            TestContext.Current.CancellationToken);
        Assert.True(reread!.ReportFulfilments);
        Assert.False(reread.ReportConfirmedAssignments);
    }

    private sealed record State(
        bool ReportFulfilments,
        int FulfilmentBacklog,
        bool ReportConfirmedAssignments,
        int ConfirmedAssignmentBacklog);
}
