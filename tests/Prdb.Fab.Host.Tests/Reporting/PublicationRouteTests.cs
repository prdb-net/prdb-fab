using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Reporting;

/// <summary>
/// ADR 0064's publishing surface through the routes a person's browser uses:
/// the explanation before the count, the explicit Library request, and the two
/// acts only a person may take on an upload nobody can account for.
/// </summary>
/// <remarks>
/// Nothing here reaches prdb. What these check is the half that decides whether
/// anything ever would — which is exactly where the ADR puts the weight, since
/// a publication is a picture in public that cannot be withdrawn.
/// </remarks>
public sealed class PublicationRouteTests
{
    private const string UserHash = "a-user-hash";

    /// <summary>
    /// A fresh installation publishes nothing, offers nothing, and refuses the
    /// request — because the explanation ADR 0064 gates on has not been read.
    /// </summary>
    [Fact]
    public async Task An_unexplained_installation_offers_no_request_and_refuses_one()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        await ALibraryAsync(application, files: 2);

        var state = await ReadAsync(client);

        Assert.True(state.Publishing);
        Assert.False(state.Explained);
        Assert.False(state.Offer.Askable);
        Assert.Null(state.Request);
        Assert.Equal(0, state.Tally.Waiting);

        var refused = await AskAsync(client);

        Assert.Null(refused.Id);
        Assert.Contains("has not been read", refused.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole on-by-default path: the channel ships on, saving the Reporting
    /// form is the act that explains it, and only then does the Library become
    /// something that can be asked for.
    /// </summary>
    [Fact]
    public async Task Explaining_the_channel_is_what_makes_the_library_askable()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        await ALibraryAsync(application, files: 3);
        await ExplainAsync(client, publishing: true);

        var offered = await ReadAsync(client);

        Assert.True(offered.Explained);
        Assert.Equal(3, offered.Offer.Eligible);
        Assert.True(offered.Offer.Askable);

        // Nothing has started on its own. The count is the offer, not a queue.
        Assert.Null(offered.Request);
        Assert.Equal(0, offered.Tally.Waiting);

        var asked = await AskAsync(client);

        Assert.NotNull(asked.Id);

        var running = await ReadAsync(client);

        Assert.Equal("Running", running.Request!.State);
        Assert.Equal(3, running.Request.Selected);
        Assert.Equal(0, running.Request.TakenUp);

        // And a second one is refused while the first is under way.
        Assert.Contains(
            "already under way",
            (await AskAsync(client)).Refusal!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Switching the channel off closes the request route, which is the switch
    /// meaning what it says rather than a separate rule.
    /// </summary>
    [Fact]
    public async Task A_switched_off_channel_refuses_the_request()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        await ALibraryAsync(application, files: 2);
        await ExplainAsync(client, publishing: false);

        var state = await ReadAsync(client);

        Assert.False(state.Publishing);
        Assert.True(state.Explained);
        Assert.False(state.Offer.Askable);

        Assert.Contains(
            "switched off",
            (await AskAsync(client)).Refusal!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A request is paused, resumed and cancelled through routes of their own,
    /// and each answers with the state the server holds afterwards.
    /// </summary>
    [Fact]
    public async Task A_request_is_paused_resumed_and_cancelled_by_name()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        await ALibraryAsync(application, files: 2);
        await ExplainAsync(client, publishing: true);

        var id = (await AskAsync(client)).Id!.Value;

        Assert.Equal("Paused", (await ActAsync(client, $"backfill/{id:D}/pause")).Request!.State);
        Assert.Equal("Running", (await ActAsync(client, $"backfill/{id:D}/resume")).Request!.State);

        var cancelled = await ActAsync(client, $"backfill/{id:D}/cancel");

        Assert.Equal("Cancelled", cancelled.Request!.State);

        // A cancelled request cannot be paused again, and the route says so
        // rather than pretending.
        using var again = await client.PostAsync(
            $"/api/publications/backfill/{id:D}/pause",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        // And the Library is offered again, because nothing of it was
        // published: a cancellation gives up what has not left.
        Assert.Equal(2, (await ReadAsync(client)).Offer.Eligible);
    }

    /// <summary>
    /// The two acts on an upload nobody can account for, which ADR 0064 leaves
    /// to a person because no amount of retrying settles it.
    /// </summary>
    [Fact]
    public async Task An_uncertain_upload_is_sent_again_or_left_by_hand()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        await ExplainAsync(client, publishing: true);

        var first = await UncertainAsync(application, "A1B2C3D4E5F60718");
        var second = await UncertainAsync(application, "0F0E0D0C0B0A0908");

        var waiting = await ReadAsync(client);

        Assert.Equal(2, waiting.Tally.Uncertain);
        Assert.Equal(2, waiting.Uncertain.Count);

        // Sending one again puts it back where the delivering routine takes its
        // work from, under the ordinary governor and the ordinary late reads.
        var resent = await ActAsync(client, $"{first:D}/send-again");

        Assert.Equal(1, resent.Tally.Uncertain);
        Assert.Equal(1, resent.Tally.Ready);

        // Leaving the other settles it without sending anything, and the file
        // is never offered again.
        var left = await ActAsync(client, $"{second:D}/leave");

        Assert.Equal(0, left.Tally.Uncertain);
        Assert.Equal(1, left.Tally.Dropped);

        // Neither act works twice: the row is no longer a question.
        using var twice = await client.PostAsync(
            $"/api/publications/{second:D}/leave",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, twice.StatusCode);
    }

    private static async Task<State> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<State>(
            "/api/publications",
            TestContext.Current.CancellationToken))!;

    private static async Task<Verdict> AskAsync(HttpClient client)
    {
        using var answer = await client.PostAsync(
            "/api/publications/backfill",
            content: null,
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();

        return (await answer.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<State> ActAsync(HttpClient client, string act)
    {
        using var answer = await client.PostAsync(
            $"/api/publications/{act}",
            content: null,
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();

        return (await answer.Content.ReadFromJsonAsync<State>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Saving the Reporting form, which is the act ADR 0064 records the
    /// explanation on.
    /// </summary>
    private static async Task ExplainAsync(HttpClient client, bool publishing)
    {
        using var saved = await client.PostAsJsonAsync(
            "/api/settings/reporting",
            new
            {
                reportFulfilments = true,
                reportConfirmedAssignments = true,
                publishGeneratedPreviews = publishing,
            },
            TestContext.Current.CancellationToken);

        saved.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A Library of filed, identified Video Files that nothing is owed for —
    /// which is what an installation looks like after upgrading into ADR 0064.
    /// </summary>
    private static async Task ALibraryAsync(FabApplication application, int files)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var installation = await context.Installation
            .AsTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        installation.PrdbUserHash = UserHash;
        installation.PrdbApiKey = "0123456789abcdef0123456789abcdef";

        for (var index = 0; index < files; index++)
        {
            var video = Guid.NewGuid();

            context.LibraryEntries.Add(new LibraryEntryRow
            {
                VideoId = video,
                EntryDirectory = $"/library/scene {index}",
                FiledAt = DateTimeOffset.UnixEpoch,
            });

            context.VideoFiles.Add(new VideoFileRow
            {
                Id = Guid.NewGuid(),
                LibraryEntryVideoId = video,
                FiledPath = $"/library/scene {index}/scene.mkv",
                QualityLabel = "1080p",
                SizeBytes = 200_000,
                RuntimeSeconds = 1200,
                Width = 1920,
                Height = 1080,
                VideoCodec = "h264",
                OsHash = $"{index:x16}",
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One upload that left and was never answered, which is the state a
    /// timeout or a container stopped mid-flight leaves behind.
    /// </summary>
    private static async Task<Guid> UncertainAsync(FabApplication application, string osHash)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var installation = await context.Installation
            .AsTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        installation.PrdbUserHash = UserHash;
        installation.PrdbApiKey = "0123456789abcdef0123456789abcdef";

        var id = Guid.NewGuid();

        context.PreviewPublications.Add(new PreviewPublicationRow
        {
            Id = id,
            VideoFileId = Guid.NewGuid(),
            VideoPrdbId = Guid.NewGuid(),
            OsHash = osHash,
            UserHash = UserHash,
            OutputVersion = PreviewPublicationContract.OutputVersion,
            State = PreviewPublicationState.Uncertain,
            Note = "The upload was not answered.",
            IntendedAt = DateTimeOffset.UnixEpoch,
            GeneratedAt = DateTimeOffset.UnixEpoch,
            SettledAt = DateTimeOffset.UnixEpoch,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return id;
    }

    private sealed record State(
        bool Publishing,
        bool Explained,
        bool Connected,
        Tally Tally,
        IReadOnlyList<Uncertain> Uncertain,
        Request? Request,
        Offer Offer);

    private sealed record Tally(
        int Waiting,
        int Ready,
        int Sending,
        int Submitted,
        int Shown,
        int Refused,
        int Uncertain,
        int Dropped);

    private sealed record Uncertain(Guid Id, Guid VideoId, string OsHash, string? Note, bool Holds);

    private sealed record Request(Guid Id, string State, int Selected, int TakenUp, int Remaining, string? Note);

    private sealed record Offer(int Eligible, bool Publishing, bool Explained, bool Connected, bool Askable);

    private sealed record Verdict(Guid? Id, string? Refusal);
}
