using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Reporting;

public sealed class ReportingSettingsRouteTests
{
    [Fact]
    public async Task The_three_channels_are_independent_and_default_on()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var initial = await client.GetFromJsonAsync<State>(
            "/api/settings/reporting",
            TestContext.Current.CancellationToken);
        Assert.True(initial!.ReportFulfilments);
        Assert.True(initial.ReportConfirmedAssignments);
        Assert.True(initial.PublishGeneratedPreviews);
        Assert.Equal(0, initial.FulfilmentBacklog);
        Assert.Equal(0, initial.ConfirmedAssignmentBacklog);

        // ADR 0064: on, and not yet explained, which is the state that
        // publishes nothing.
        Assert.False(initial.PreviewPublicationExplained);

        using var saved = await client.PostAsJsonAsync(
            "/api/settings/reporting",
            new
            {
                reportFulfilments = false,
                reportConfirmedAssignments = true,
                publishGeneratedPreviews = true,
            },
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();

        var after = await saved.Content.ReadFromJsonAsync<State>(TestContext.Current.CancellationToken);
        Assert.False(after!.ReportFulfilments);
        Assert.True(after.ReportConfirmedAssignments);
        Assert.True(after.PublishGeneratedPreviews);

        // Saving is the act that records the explanation, whichever switches
        // moved — here none of the publishing one's did.
        Assert.True(after.PreviewPublicationExplained);

        var reread = await client.GetFromJsonAsync<State>(
            "/api/settings/reporting",
            TestContext.Current.CancellationToken);
        Assert.False(reread!.ReportFulfilments);
        Assert.True(reread.ReportConfirmedAssignments);
        Assert.True(reread.PublishGeneratedPreviews);
        Assert.True(reread.PreviewPublicationExplained);
    }

    /// <summary>
    /// Turning the third channel off is still an answer to it: the explanation
    /// was in front of somebody, and the stamp says so whatever they decided.
    /// </summary>
    [Fact]
    public async Task Turning_publication_off_still_counts_as_having_been_told()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        using var saved = await client.PostAsJsonAsync(
            "/api/settings/reporting",
            new
            {
                reportFulfilments = true,
                reportConfirmedAssignments = true,
                publishGeneratedPreviews = false,
            },
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();

        var after = await saved.Content.ReadFromJsonAsync<State>(TestContext.Current.CancellationToken);

        Assert.False(after!.PublishGeneratedPreviews);
        Assert.True(after.PreviewPublicationExplained);
    }

    private sealed record State(
        bool ReportFulfilments,
        int FulfilmentBacklog,
        bool ReportConfirmedAssignments,
        int ConfirmedAssignmentBacklog,
        bool PublishGeneratedPreviews,
        bool PreviewPublicationExplained);
}
