using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Reporting;

public sealed class ReportingSettingsRouteTests
{
    [Fact]
    public async Task The_two_channels_are_independent_and_default_on()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var initial = await client.GetFromJsonAsync<State>(
            "/api/settings/reporting",
            TestContext.Current.CancellationToken);
        Assert.True(initial!.ReportFulfilments);
        Assert.True(initial.ReportConfirmedAssignments);
        Assert.Equal(0, initial.FulfilmentBacklog);
        Assert.Equal(0, initial.ConfirmedAssignmentBacklog);

        using var saved = await client.PostAsJsonAsync(
            "/api/settings/reporting",
            new { reportFulfilments = false, reportConfirmedAssignments = true },
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();

        var after = await saved.Content.ReadFromJsonAsync<State>(TestContext.Current.CancellationToken);
        Assert.False(after!.ReportFulfilments);
        Assert.True(after.ReportConfirmedAssignments);

        var reread = await client.GetFromJsonAsync<State>(
            "/api/settings/reporting",
            TestContext.Current.CancellationToken);
        Assert.False(reread!.ReportFulfilments);
        Assert.True(reread.ReportConfirmedAssignments);
    }

    private sealed record State(
        bool ReportFulfilments,
        int FulfilmentBacklog,
        bool ReportConfirmedAssignments,
        int ConfirmedAssignmentBacklog);
}
