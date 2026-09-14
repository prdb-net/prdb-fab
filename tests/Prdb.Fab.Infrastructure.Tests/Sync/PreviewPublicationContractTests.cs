using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Sdk.Generated.Models;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0064's contract, asserted against the SDK this repository references
/// rather than against the document it was generated from.
/// </summary>
/// <remarks>
/// <para>
/// Fixtures rather than behaviour, the same shape as
/// <see cref="UserPreviewContractTests"/>: no routine runs, nothing is written,
/// and what is being checked is that the one operation this direction rests on
/// exists in <c>Prdb.Sdk</c> 0.13.0, is addressed where the document says,
/// accepts the body the design builds, and answers the way the outcomes are
/// read. Everything above the socket is real — Kiota builds the multipart body,
/// the governor's handler sees the request, and the deserialisation is the
/// SDK's own.
/// </para>
/// <para>
/// The two that matter most are the ones that cannot be read off the document:
/// that a refusal and an unanswered request are distinguishable at all, and
/// that the request carries no idempotency key to make a retry safe. Both are
/// what the uncertain state is built on.
/// </para>
/// </remarks>
public sealed class PreviewPublicationContractTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string Submissions = "/video-user-images";

    private const string Hash = "A1B2C3D4E5F60718";

    private static readonly Guid AVideo = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid ASubmission = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid AModerationTarget = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    /// <summary>
    /// The submission is a <c>POST</c> to <c>/video-user-images</c> carrying
    /// every part ADR 0064 enumerates and no others, and the <c>201</c> hands
    /// back the id and the moderation signature it entered under.
    /// </summary>
    [Fact]
    public async Task A_submission_carries_the_six_parts_and_answers_with_an_id()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await TestDatabase.CreateAsync(prdb: prdb);

        var answer = await SubmitAsync(database, ABody());

        Assert.Equal(ASubmission, answer!.VideoUserImageId);
        Assert.Equal(AModerationTarget, answer.ModerationTargetId);

        var sent = Assert.Single(prdb.AskingFor(Submissions));

        Assert.Contains($"name=\"VideoId\"", sent.Body, StringComparison.Ordinal);
        Assert.Contains(AVideo.ToString("D"), sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"BasedOnFileWithOsHash\"", sent.Body, StringComparison.Ordinal);
        Assert.Contains(Hash, sent.Body, StringComparison.Ordinal);
        Assert.Contains("name=\"PreviewImageType\"", sent.Body, StringComparison.Ordinal);
        Assert.Contains(PreviewPublicationContract.PreviewImageType, sent.Body, StringComparison.Ordinal);
        Assert.Contains("name=\"DisplayOrder\"", sent.Body, StringComparison.Ordinal);
        Assert.Contains("name=\"File\"", sent.Body, StringComparison.Ordinal);
        Assert.Contains("name=\"VttFile\"", sent.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing in the request names the file it was made from.
    /// </summary>
    /// <remarks>
    /// ADR 0064's rule that the source Video File never leaves, including its
    /// name — so the multipart filenames are fixed strings. This is the fixture
    /// that would fail if somebody later passed the real filename to make the
    /// parts "more informative".
    /// </remarks>
    [Fact]
    public async Task A_submission_names_the_parts_and_never_the_file()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await TestDatabase.CreateAsync(prdb: prdb);

        await SubmitAsync(database, ABody());

        var sent = Assert.Single(prdb.AskingFor(Submissions));

        Assert.Contains(PreviewPublicationContract.SheetFilename, sent.Body, StringComparison.Ordinal);
        Assert.Contains(PreviewPublicationContract.VttFilename, sent.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Scene.Name.2026", sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/library", sent.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The request carries nothing that would let prdb recognise a repeat, and
    /// this is the assertion the whole uncertain state rests on.
    /// </summary>
    /// <remarks>
    /// If this ever fails because a key turned up, the proposal in
    /// <c>docs/prdb-api-proposals.md</c> has been answered and ADR 0064's
    /// refusal to retry can be reopened.
    /// </remarks>
    [Fact]
    public async Task A_submission_carries_no_idempotency_key()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await TestDatabase.CreateAsync(prdb: prdb);

        await SubmitAsync(database, ABody());

        var sent = Assert.Single(prdb.AskingFor(Submissions));

        Assert.DoesNotContain("Idempotency", sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", sent.Uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A refusal prdb considered and gave arrives as the generated
    /// <c>ProblemDetails</c> — an <c>ApiException</c> carrying the status — which
    /// is what separates the final outcomes from each other and from a deferral.
    /// </summary>
    /// <remarks>
    /// The exception is the SDK's error-mapped body rather than a bare
    /// <c>ApiException</c>, and it is asserted that way round on purpose: the
    /// delivering routine reads <c>ResponseStatusCode</c>, so what matters is
    /// that every refusal is an <c>ApiException</c>, not which subclass the
    /// generator chose for it.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task A_refusal_names_its_status(HttpStatusCode status)
    {
        var prdb = new FakePrdbApi().Refuses(Submissions, status, "Refused.");

        await using var database = await TestDatabase.CreateAsync(prdb: prdb);

        var refused = await Assert.ThrowsAnyAsync<ApiException>(() => SubmitAsync(database, ABody()));

        Assert.Equal((int)status, refused.ResponseStatusCode);
        Assert.IsType<ProblemDetails>(refused);
    }

    /// <summary>
    /// A request that leaves and is never answered is not a refusal, and the
    /// difference is readable: ADR 0041 says a timeout is <em>the request
    /// failed</em> rather than an answer, and ADR 0064 turns that into the one
    /// outcome nothing is retried from.
    /// </summary>
    [Fact]
    public async Task An_unanswered_submission_is_not_a_refusal()
    {
        var prdb = new FakePrdbApi().IsUnreachable(Submissions);

        await using var database = await TestDatabase.CreateAsync(prdb: prdb);

        var failed = await Assert.ThrowsAnyAsync<Exception>(() => SubmitAsync(database, ABody()));

        Assert.IsNotType<ApiException>(failed);
        Assert.True(failed is HttpRequestException or TaskCanceledException);

        // And the request did leave, which is precisely why its outcome cannot
        // be assumed to be nothing.
        Assert.Single(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// The governor holds an upload back before it holds a feed back, and long
    /// before it holds a Fulfilment back — ADR 0064's whole argument for taking
    /// uploads out of <c>Writes</c>.
    /// </summary>
    [Fact]
    public async Task A_short_budget_defers_an_upload_while_a_write_still_goes()
    {
        // Thirty-eight per cent of the hour left: above the five a write is
        // held back at, above the thirty-five the read half of this population
        // is held back at, and below the forty an upload is.
        var prdb = new FakePrdbApi { Hourly = (Limit: 100, Remaining: 38, ResetInSeconds: 60) }
            .AnswersCreated(Submissions, Accepted());

        await using var database = await TestDatabase.CreateAsync(prdb: prdb);

        // One answered request, so that the governor has a reading at all.
        await SubmitAsync(database, ABody());

        var deferred = await Assert.ThrowsAsync<PrdbDeferredException>(
            () => SubmitAsync(database, ABody(), PrdbWork.Publications));

        Assert.Equal(PrdbWork.Publications, deferred.Work);

        // The small queued obligations are untouched, which is the point.
        Assert.True(new PrdbBudget(100, 38, TimeSpan.FromSeconds(60)).Admits(PrdbWork.Writes));
        Assert.True(new PrdbBudget(100, 38, TimeSpan.FromSeconds(60)).Admits(PrdbWork.Identification));
    }

    private static Task<SubmitVideoUserImageResponse?> SubmitAsync(
        TestDatabase database,
        MultipartBody body,
        PrdbWork work = PrdbWork.Publications) => AskAsync(
            database,
            work,
            (client, token) => client.VideoUserImages.PostAsync(body, cancellationToken: token));

    private static async Task<TAnswer?> AskAsync<TAnswer>(
        TestDatabase database,
        PrdbWork work,
        Func<Prdb.Sdk.Generated.PrdbClient, CancellationToken, Task<TAnswer?>> ask)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<PrdbGateway>()
            .AskAsync(ApiKey, work, ask, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The body ADR 0064 enumerates, built by the code that builds the real
    /// one.
    /// </summary>
    /// <remarks>
    /// <see cref="PreviewPublicationBody"/> rather than a copy of it, which is
    /// what makes these fixtures worth anything: a part added or renamed there
    /// is caught here, and a fixture asserting a reconstruction would only
    /// assert that the reconstruction still matched itself. The bytes are a
    /// token rather than a real sheet — what is under test is the shape of the
    /// request.
    /// </remarks>
    private static MultipartBody ABody() => PreviewPublicationBody.For(
        AVideo,
        Hash,
        new MemoryStream(Encoding.ASCII.GetBytes("a sprite sheet")),
        new MemoryStream(Encoding.ASCII.GetBytes("WEBVTT")));

    private static string Accepted() =>
        $$"""
        {
          "videoUserImageId": "{{ASubmission:D}}",
          "moderationTargetId": "{{AModerationTarget:D}}",
          "moderationStatus": "Pending",
          "moderationVisibility": "Hidden"
        }
        """;
}
