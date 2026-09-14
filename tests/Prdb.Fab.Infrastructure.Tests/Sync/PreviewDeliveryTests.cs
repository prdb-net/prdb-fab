using System.Net;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0064's delivery: what leaves, what each answer makes of the row, and the
/// one outcome nothing is sent twice from.
/// </summary>
/// <remarks>
/// <para>
/// The network is replaced at the socket (ADR 0042) and nothing else is: the
/// SDK builds the multipart body, the handler chain carries it, the governor
/// sees it, and the routine and the store are the code that ships. What the
/// fake decides is what prdb answered — which is the only thing these tests are
/// about.
/// </para>
/// <para>
/// The cases that matter most are the three that cannot be read off the API
/// document. A request that leaves and is never answered is not a refusal; a
/// row found mid-flight after a restart is the same question; and neither is
/// ever sent again, because there is no idempotency key and no way to ask
/// whether the first one arrived.
/// </para>
/// </remarks>
public sealed class PreviewDeliveryTests
{
    private const string Submissions = "/video-user-images";

    private const string UserHash = "a-user-hash";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Hash = "A1B2C3D4E5F60718";

    private static readonly Guid Video = Guid.Parse("cccc2222-0000-4000-8000-000000000001");
    private static readonly Guid File = Guid.Parse("dddd2222-0000-4000-8000-000000000001");
    private static readonly Guid Publication = Guid.Parse("eeee2222-0000-4000-8000-000000000001");

    private static readonly Guid Submitted = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Target = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    /// <summary>
    /// The whole path: a generated pair, the six parts ADR 0064 enumerates, a
    /// <c>201</c>, and a row that keeps what came back and lets the bytes go.
    /// </summary>
    [Fact]
    public async Task An_accepted_submission_keeps_its_id_and_drops_its_bytes()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        var result = await RunAsync(database);

        Assert.Equal(RunOutcome.Succeeded, result.Outcome);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Sent, row.State);
        Assert.Equal(Submitted, row.PrdbImageId);
        Assert.Equal(Target, row.ModerationTargetId);
        Assert.Equal(UserPreviewModeration.Signature("Pending", "Hidden"), row.SubmittedUnder);
        Assert.NotNull(row.SettledAt);

        // Submitted, never published: ADR 0064 keeps those words apart until a
        // read endpoint has returned the row.
        Assert.Contains("Submitted", row.Note!, StringComparison.Ordinal);
        Assert.DoesNotContain("published", row.Note!, StringComparison.OrdinalIgnoreCase);

        Assert.False(Store(database).Holds(row.Id), "an accepted submission left its bytes on disk.");

        var sent = Assert.Single(prdb.AskingFor(Submissions));

        foreach (var part in (string[])["File", "VttFile", "VideoId", "BasedOnFileWithOsHash", "PreviewImageType", "DisplayOrder"])
        {
            Assert.Contains($"name=\"{part}\"", sent.Body, StringComparison.Ordinal);
        }

        Assert.Contains(Video.ToString("D"), sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Hash, sent.Body, StringComparison.Ordinal);
        Assert.Contains(PreviewPublicationContract.PreviewImageType, sent.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing in the request names the file, the path or the Library. The
    /// filenames are the contract's fixed strings, which is the rule this
    /// fixture exists to hold.
    /// </summary>
    [Fact]
    public async Task Nothing_that_leaves_names_the_file_it_was_made_from()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);
        await RunAsync(database);

        var sent = Assert.Single(prdb.AskingFor(Submissions));

        Assert.Contains(PreviewPublicationContract.SheetFilename, sent.Body, StringComparison.Ordinal);
        Assert.Contains(PreviewPublicationContract.VttFilename, sent.Body, StringComparison.Ordinal);

        Assert.DoesNotContain("a scene", sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".mkv", sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("library", sent.Body, StringComparison.OrdinalIgnoreCase);

        // And nothing that would let a retry be recognised as one, which is
        // what the uncertain state is built on.
        Assert.DoesNotContain("idempotency", sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", sent.Uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A refusal prdb considered and gave is final: recorded with its status,
    /// its bytes released, and never sent or generated again.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_refusal_is_final(HttpStatusCode status)
    {
        var prdb = new FakePrdbApi().Refuses(Submissions, status, "Refused.");

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        Assert.Equal(RunOutcome.Succeeded, (await RunAsync(database)).Outcome);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Refused, row.State);
        Assert.Contains(((int)status).ToString(), row.Note!, StringComparison.Ordinal);
        Assert.NotNull(row.SettledAt);
        Assert.False(Store(database).Holds(row.Id), "a refused submission left its bytes on disk.");

        // And nothing comes back for it: not this run, and not the next one.
        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
        Assert.Single(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// A <c>409</c> is read as <em>this already exists</em> rather than as a
    /// transient collision, which is the safe reading whichever it means.
    /// </summary>
    [Fact]
    public async Task A_conflict_is_read_as_something_that_already_exists()
    {
        var prdb = new FakePrdbApi().Refuses(Submissions, HttpStatusCode.Conflict, "Already there.");

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);
        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Refused, row.State);
        Assert.Contains("already been submitted", row.Note!, StringComparison.Ordinal);

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
        Assert.Single(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// A request that leaves and is never answered is the one outcome nothing
    /// is retried from: the bytes are kept, the reason is recorded, and the
    /// next run does not send it again.
    /// </summary>
    /// <remarks>
    /// The assertion that matters is the second run. ADR 0064's whole argument
    /// is that absence from prdb's read endpoints proves nothing, so a second
    /// POST would be a coin-flip between one public picture and two.
    /// </remarks>
    [Fact]
    public async Task An_unanswered_submission_is_uncertain_and_is_not_sent_again()
    {
        var prdb = new FakePrdbApi().IsUnreachable(Submissions);

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        Assert.Equal(RunOutcome.Failed, (await RunAsync(database)).Outcome);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Uncertain, row.State);
        Assert.Contains("cannot be established", row.Note!, StringComparison.Ordinal);
        Assert.NotNull(row.SettledAt);

        // The bytes stay, because a person deciding to send it is still
        // possible and a decision without them would be a decision to
        // regenerate first.
        Assert.True(Store(database).Holds(row.Id), "an uncertain upload lost its bytes.");

        // And the request did leave, which is exactly why its outcome cannot be
        // assumed to be nothing.
        Assert.Single(prdb.AskingFor(Submissions));

        await RunAsync(database);

        Assert.Equal(PreviewPublicationState.Uncertain, (await RowAsync(database)).State);
        Assert.Single(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// A <c>500</c> is a server saying it may or may not have done the work, so
    /// it is uncertain rather than refused.
    /// </summary>
    [Fact]
    public async Task A_server_error_is_uncertain_rather_than_refused()
    {
        var prdb = new FakePrdbApi().Refuses(Submissions, HttpStatusCode.InternalServerError, "Sorry.");

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);
        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Uncertain, row.State);
        Assert.Contains("500", row.Note!, StringComparison.Ordinal);
        Assert.True(Store(database).Holds(row.Id), "an uncertain upload lost its bytes.");
    }

    /// <summary>
    /// The state the whole design rests on: a row committed as <c>Sending</c>
    /// and a process that never came back. The next run reads it as a question
    /// rather than as something to send.
    /// </summary>
    [Fact]
    public async Task A_row_left_in_flight_by_a_restart_becomes_uncertain()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        // What the container stopping in the middle of SendAsync leaves.
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .PreviewPublications
                .Where(row => row.Id == Publication)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.State, PreviewPublicationState.Sending),
                    TestContext.Current.CancellationToken);
        }

        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Uncertain, row.State);
        Assert.Contains("in flight", row.Note!, StringComparison.Ordinal);

        // Nothing was sent, which is the point: prdb was never asked a second
        // time about a request it may already have accepted.
        Assert.Empty(prdb.AskingFor(Submissions));
        Assert.True(Store(database).Holds(row.Id), "an uncertain upload lost its bytes.");
    }

    /// <summary>
    /// The request is marked as being in flight <em>before</em> it leaves, so a
    /// container that stops between the two is found rather than assumed.
    /// </summary>
    /// <remarks>
    /// The whole of ADR 0064's durability argument in one fixture. The run is
    /// cancelled as the request reaches the socket, which means the routine
    /// cannot write anything afterwards — the token it would write under is the
    /// one that was cancelled. So the row is whatever it said before the POST,
    /// and the next run's first pass is what turns that into a question.
    /// </remarks>
    [Fact]
    public async Task A_run_stopped_as_the_request_leaves_finds_the_row_in_flight()
    {
        using var stopping = new CancellationTokenSource();

        var prdb = new FakePrdbApi().IsStopped(Submissions, stopping);

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        await using (var scope = database.Scope())
        {
            var stopped = await scope.ServiceProvider.GetRequiredService<PreviewUploadRoutine>()
                .RunAsync(target: null, stopping.Token);

            // Interrupted rather than failed: the tool is shutting down, which
            // ADR 0032 keeps apart from something going wrong.
            Assert.Equal(RunOutcome.Interrupted, stopped.Outcome);
        }

        // The request left, and the row says so — which is the state that
        // exists to be found.
        Assert.Single(prdb.AskingFor(Submissions));
        Assert.Equal(PreviewPublicationState.Sending, (await RowAsync(database)).State);

        // And the next run reads it as the question it is, without sending
        // anything a second time.
        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Uncertain, row.State);
        Assert.Contains("in flight", row.Note!, StringComparison.Ordinal);
        Assert.Single(prdb.AskingFor(Submissions));
        Assert.True(Store(database).Holds(row.Id), "an uncertain upload lost its bytes.");
    }

    /// <summary>
    /// ADR 0064's opportunistic resolution: the ordinary per-Video read brings
    /// back a sprite carrying this installation's own osHash, which is proof
    /// enough that the submission landed.
    /// </summary>
    [Fact]
    public async Task An_uncertain_upload_prdb_now_shows_is_settled_as_sent()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        var shown = Guid.NewGuid();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            await context.PreviewPublications
                .Where(row => row.Id == Publication)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(row => row.State, PreviewPublicationState.Uncertain)
                        .SetProperty(row => row.Note, "Nobody knows."),
                    TestContext.Current.CancellationToken);

            context.UserPreviews.Add(ASprite(shown, database.Time.GetUtcNow()));

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Sent, row.State);
        Assert.Equal(shown, row.PrdbImageId);
        Assert.Contains("did arrive", row.Note!, StringComparison.Ordinal);

        // Settled from evidence rather than from a request: nothing was sent,
        // and the bytes are finished with.
        Assert.Empty(prdb.AskingFor(Submissions));
        Assert.False(Store(database).Holds(row.Id), "a settled upload kept its bytes.");
    }

    /// <summary>
    /// And the resolution runs one way only. A row prdb is not showing has told
    /// nobody anything, because absence is the ordinary state of everything in
    /// moderation.
    /// </summary>
    [Fact]
    public async Task An_uncertain_upload_prdb_is_silent_about_stays_uncertain()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .PreviewPublications
                .Where(row => row.Id == Publication)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.State, PreviewPublicationState.Uncertain),
                    TestContext.Current.CancellationToken);
        }

        await RunAsync(database);

        Assert.Equal(PreviewPublicationState.Uncertain, (await RowAsync(database)).State);
        Assert.Empty(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// The governor holds an upload back before it holds anything a person
    /// notices back, and a deferred run leaves the row byte for byte as it was.
    /// </summary>
    [Fact]
    public async Task A_short_budget_defers_the_upload_and_writes_nothing()
    {
        // Thirty-eight per cent of the hour left: above the five a write is held
        // back at and below the forty an upload is.
        var prdb = new FakePrdbApi { Hourly = (Limit: 100, Remaining: 38, ResetInSeconds: 60) }
            .AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        // The first run sends, and its answer is what gives the governor a
        // reading at all.
        await RunAsync(database);
        await GiveAsync(database, second: true);

        var deferred = await RunAsync(database);

        // ADR 0014's fourth case: not a run at all, so there is no outcome to
        // record and a wait instead.
        Assert.Null(deferred.Outcome);
        Assert.NotNull(deferred.DueIn);

        var waiting = await RowAsync(database, second: true);

        Assert.Equal(PreviewPublicationState.Ready, waiting.State);
        Assert.Null(waiting.SettledAt);

        // One request, not two: the deferral happened before anything left.
        Assert.Single(prdb.AskingFor(Submissions));

        // And what the reserve exists to protect is untouched.
        Assert.True(new PrdbBudget(100, 38, TimeSpan.FromSeconds(60)).Admits(PrdbWork.Writes));
        Assert.True(new PrdbBudget(100, 38, TimeSpan.FromSeconds(60)).Admits(PrdbWork.Identification));
    }

    /// <summary>
    /// The switch off stops unsent uploads and drops nothing: what is generated
    /// waits, and the thirty-day expiry is what settles it if it stays off.
    /// </summary>
    [Fact]
    public async Task Nothing_is_sent_while_the_switch_is_off()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);
        await SwitchOffAsync(database);

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Ready, row.State);
        Assert.Empty(prdb.AskingFor(Submissions));
        Assert.True(Store(database).Holds(row.Id), "switching the channel off dropped a generated pair.");
    }

    /// <summary>
    /// ADR 0064's gate holds the sending half too, not only the generating one.
    /// </summary>
    [Fact]
    public async Task Nothing_is_sent_before_the_explanation()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);
        await UnexplainAsync(database);

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));

        Assert.Equal(PreviewPublicationState.Ready, (await RowAsync(database)).State);
        Assert.Empty(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// A pair generated under another account is never sent under this one's
    /// key. What is <em>not</em> touched is the history, which is what stops a
    /// file from being published twice.
    /// </summary>
    [Fact]
    public async Task A_pair_generated_under_another_account_is_dropped_and_the_history_is_not()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        var history = Guid.NewGuid();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            context.PreviewPublications.Add(new PreviewPublicationRow
            {
                Id = history,
                VideoFileId = Guid.NewGuid(),
                VideoPrdbId = Guid.NewGuid(),
                OsHash = "0000000000000001",
                UserHash = UserHash,
                OutputVersion = PreviewPublicationContract.OutputVersion,
                State = PreviewPublicationState.Sent,
                PrdbImageId = Guid.NewGuid(),
                IntendedAt = database.Time.GetUtcNow(),
                SettledAt = database.Time.GetUtcNow(),
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await SignInAsAsync(database, "somebody-else");

        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Dropped, row.State);
        Assert.Contains("account changed", row.Note!, StringComparison.Ordinal);
        Assert.False(Store(database).Holds(row.Id), "a dropped pair left its bytes on disk.");
        Assert.Empty(prdb.AskingFor(Submissions));

        await using var reading = database.Scope();

        var kept = await reading.ServiceProvider.GetRequiredService<FabDbContext>()
            .PreviewPublications.AsNoTracking()
            .SingleAsync(item => item.Id == history, TestContext.Current.CancellationToken);

        Assert.Equal(PreviewPublicationState.Sent, kept.State);
    }

    /// <summary>
    /// What a Restore arrives as: the row crossed the Backup boundary and the
    /// generated bytes deliberately did not. It goes back to its intent, which
    /// is safe because a pair waiting to be sent has never left the machine.
    /// </summary>
    [Fact]
    public async Task A_generated_pair_whose_bytes_are_gone_is_generated_again()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);

        Store(database).Delete(Publication);

        Assert.Equal(RunOutcome.Succeeded, (await RunAsync(database)).Outcome);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Intended, row.State);
        Assert.Null(row.GeneratedAt);
        Assert.Null(row.Tiles);
        Assert.Empty(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// An installation with nothing generated and nothing in doubt spends
    /// nothing on this routine, which is what <c>IWorkSetPaced</c> means.
    /// </summary>
    [Fact]
    public async Task An_installation_with_nothing_to_send_does_no_work()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
        Assert.Empty(prdb.AskingFor(Submissions));
    }

    /// <summary>
    /// One upload per run. The pacing is ADR 0064's, and it is a property of
    /// the routine rather than of the governor: a publication is megabytes on
    /// somebody's connection.
    /// </summary>
    [Fact]
    public async Task One_upload_leaves_per_run()
    {
        var prdb = new FakePrdbApi().AnswersCreated(Submissions, Accepted());

        await using var database = await CreateAsync(prdb);
        await GiveAsync(database);
        await GiveAsync(database, second: true);

        await RunAsync(database);

        Assert.Single(prdb.AskingFor(Submissions));

        await RunAsync(database);

        Assert.Equal(2, prdb.AskingFor(Submissions).Count);

        // And the older pair went first, which is the order a bounded backlog
        // has to drain in.
        Assert.Equal(PreviewPublicationState.Sent, (await RowAsync(database)).State);
    }

    private static async Task<RunResult> RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<PreviewUploadRoutine>()
            .RunAsync(target: null, TestContext.Current.CancellationToken);
    }

    private static async Task<PreviewPublicationRow> RowAsync(TestDatabase database, bool second = false)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .PreviewPublications.AsNoTracking()
            .SingleAsync(row => row.Id == (second ? Second : Publication), TestContext.Current.CancellationToken);
    }

    private static PublicationStore Store(TestDatabase database) =>
        database.Services.CreateScope().ServiceProvider.GetRequiredService<PublicationStore>();

    private static readonly Guid Second = Guid.Parse("eeee2222-0000-4000-8000-000000000002");

    /// <summary>
    /// A connected, explained installation and one generated pair on disk,
    /// which is exactly the state the generating routine leaves behind.
    /// </summary>
    private static async Task GiveAsync(TestDatabase database, bool second = false)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        if (!second)
        {
            var installation = await context.Installation
                .AsTracking()
                .SingleAsync(TestContext.Current.CancellationToken);

            installation.PrdbApiKey = ApiKey;
            installation.PrdbUserHash = UserHash;
            installation.PublishGeneratedPreviews = true;
            installation.PreviewPublicationExplainedAt = database.Time.GetUtcNow();
        }

        var id = second ? Second : Publication;

        context.PreviewPublications.Add(new PreviewPublicationRow
        {
            Id = id,
            VideoFileId = second ? Guid.NewGuid() : File,
            VideoPrdbId = second ? Guid.NewGuid() : Video,
            OsHash = second ? "0F0E0D0C0B0A0908" : Hash,
            UserHash = UserHash,
            OutputVersion = PreviewPublicationContract.OutputVersion,
            State = PreviewPublicationState.Ready,
            Tiles = 120,
            Columns = 11,
            Rows = 11,
            SheetBytes = 14,
            IntendedAt = database.Time.GetUtcNow() - (second ? TimeSpan.Zero : TimeSpan.FromMinutes(5)),
            GeneratedAt = database.Time.GetUtcNow() - (second ? TimeSpan.Zero : TimeSpan.FromMinutes(5)),
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await scope.ServiceProvider.GetRequiredService<PublicationStore>().WritePairAsync(
            id,
            Encoding.ASCII.GetBytes("a sprite sheet"),
            Encoding.ASCII.GetBytes("WEBVTT\n"),
            TestContext.Current.CancellationToken);
    }

    private static async Task SwitchOffAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PublishGeneratedPreviews, false),
                TestContext.Current.CancellationToken);
    }

    private static async Task UnexplainAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PreviewPublicationExplainedAt, (DateTimeOffset?)null),
                TestContext.Current.CancellationToken);
    }

    private static async Task SignInAsAsync(TestDatabase database, string userHash)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PrdbUserHash, userHash),
                TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A publicly visible sprite prdb holds for this installation's own bytes —
    /// the shape ADR 0061's per-Video read writes, and the evidence that
    /// settles an uncertain upload.
    /// </summary>
    private static UserPreviewRow ASprite(Guid prdbId, DateTimeOffset at) => new()
    {
        PrdbId = prdbId,
        VideoPrdbId = Video,
        OsHash = UserPreviewHash.Normalise(Hash)!,
        Kind = UserPreviewAsset.SpriteSheet,
        Url = "https://cdn.example/ours.jpg",
        VttUrl = "https://cdn.example/ours.vtt",
        TileCount = 120,
        ModerationStatus = "Approved",
        ModerationVisibility = "Visible",
        Shown = true,
        UpdatedAtUtc = at,
        CreatedAtUtc = at,
    };

    private static Task<TestDatabase> CreateAsync(FakePrdbApi prdb) =>
        TestDatabase.CreateAsync(prdb: prdb, also: services => services.AddFabSync());

    private static string Accepted() =>
        $$"""
        {
          "videoUserImageId": "{{Submitted:D}}",
          "moderationTargetId": "{{Target:D}}",
          "moderationStatus": "Pending",
          "moderationVisibility": "Hidden"
        }
        """;
}
