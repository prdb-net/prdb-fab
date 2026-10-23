using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Sdk.Generated.Models;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// Synthetic demand at installation scale plus a real SDK/Governor exercise
/// across a rolling month. SQLite/checkpoint/coverage wiring has separate
/// integration fixtures; this fixture does not claim production latency.
/// </summary>
public sealed class SustainableSyncWorkloadTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Recurring_demand_and_shared_account_competition_remain_sustainable_over_a_rolling_month()
    {
        var rows = Setting("FAB_SYNC_RELEASES", 98872);
        var distinct = Math.Min(rows, Setting("FAB_SYNC_DISTINCT", 79150));
        var days = Setting("FAB_SYNC_DAYS", 35);
        var hourLimit = Setting("FAB_SYNC_HOURLY", 1000);
        var monthLimit = Setting("FAB_SYNC_MONTHLY", 200000);
        var otherDaily = Setting("FAB_SYNC_OTHER_DAILY", 4000);
        var batches = (int)Math.Ceiling(distinct / 200d);
        var beforeIdentify = (int)Math.Ceiling(rows / 200d);
        var beforeDetails = (int)Math.Ceiling(rows / 50d);
        var socket = new RollingAccountSocket(hourLimit, monthLimit);
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Prdb).ConfigurePrimaryHttpMessageHandler(() => socket));
        socket.Time = database.Time;
        // Existing shared consumption is distributed across seven old days.
        // Replenishment therefore frees cohorts, never the full allowance.
        for (var day = 0; day < 7; day++)
            socket.External((int)(monthLimit * .58 / 7), database.Time.GetUtcNow().AddDays(-21 + day));
        await using var scope = database.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var start = database.Time.GetUtcNow();
        var feeds = new[] { (15, PrdbWork.WhatsNew), (30, PrdbWork.CatalogueChanges), (30, PrdbWork.Images),
            (60, PrdbWork.UserFeeds), (60, PrdbWork.UserFeeds), (60, PrdbWork.UserFeeds), (60, PrdbWork.UserFeeds),
            (360, PrdbWork.Actors), (60, PrdbWork.Sites) };
        var inputs = Enumerable.Range(0, 200).Select(i => new IdentifyVideoFileDto
            { Ref = i.ToString(System.Globalization.CultureInfo.InvariantCulture), Filename = "Synthetic." + i }).ToList();
        var feedSent = 0;
        var feedWaited = 0;
        var backgroundSent = 0;
        var backgroundWaited = 0;
        var previousExternal = 0;
        var dailyWorkRemaining = batches;
        var initialMetered = socket.Metered;
        for (var minute = 0; minute < days * 1440; minute++)
        {
            // Absolute simulated minutes keep HTTP latency out of cadence math.
            database.Time.SetUtcNow(start.AddMinutes(minute));
            if (minute % 1440 == 0) dailyWorkRemaining += minute == 0 ? 0 : batches;
            var externalDue = (int)((minute + 1L) * otherDaily / 1440);
            socket.External(externalDue - previousExternal, database.Time.GetUtcNow());
            previousExternal = externalDue;
            foreach (var (cadence, work) in feeds.Where(feed => minute % feed.Item1 == 0))
            {
                try
                {
                    await gateway.AskAsync("synthetic-key", work,
                        (client, token) => client.UserIdentity.GetAsync(cancellationToken: token), TestContext.Current.CancellationToken);
                    feedSent++;
                }
                catch (PrdbDeferredException) { feedWaited++; }
            }
            // At most one bounded turn per minute, with all quota waits intact.
            if (dailyWorkRemaining > 0)
            {
                try
                {
                    await gateway.AskAsync("synthetic-key", PrdbWork.ReleaseIdentification,
                        (client, token) => client.Videos.Identify.PostAsync(new IdentifyVideosRequest
                            { Files = inputs, IncludeVideoDetails = true }, cancellationToken: token), TestContext.Current.CancellationToken);
                    backgroundSent++;
                    dailyWorkRemaining--;
                }
                catch (PrdbDeferredException) { backgroundWaited++; }
            }
        }
        var costs = database.Services.GetRequiredService<PrdbGovernor>().Costs.Snapshot();
        var indexer = IndexerDemand(Setting("FAB_SYNC_INDEXER_DAILY", 1000), rows);
        var report = new
        {
            fixture = new { rows, distinct, days, hourLimit, monthLimit, otherDaily },
            before = new { identifyRequestsPerDay = beforeIdentify, detailRequestsPerDay = beforeDetails,
                meteredRequestsPerDay = beforeIdentify + beforeDetails,
                idleCadenceDelaySeconds = Math.Max(0, beforeIdentify - 1) * 10 },
            after = new { identifyRequestsPerDay = batches, detailRequestsPerDay = 0,
                feedSent, feedWaited, backgroundSent, backgroundWaited, remainingBatches = dailyWorkRemaining,
                socket.Metered, socket.Readings, socket.PeakHourly, socket.PeakMonthly, socket.ExternalRefused,
                syntheticHttpLatencyMilliseconds = 100,
                payloadBytes = costs.Requests.Sum(r => r.PayloadBytes),
                httpMilliseconds = costs.Requests.Sum(r => r.ElapsedMilliseconds),
                oldestMonthCohortsExpired = socket.MonthExpired },
            indexer,
            ordinaryMonthlyDemand = IdleProfile.RequestsInThirtyDays,
            baselineMonthlyDemandIncludingCompetition = (beforeIdentify + beforeDetails + otherDaily) * 30 + IdleProfile.RequestsInThirtyDays,
            optimizedMonthlyDemandIncludingCompetition = (batches + otherDaily) * 30 + IdleProfile.RequestsInThirtyDays,
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        output.WriteLine(json);
        if (Environment.GetEnvironmentVariable("FAB_SYNC_REPORT") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        }
        Assert.True(socket.PeakHourly <= hourLimit);
        Assert.True(socket.PeakMonthly <= monthLimit);
        Assert.True(backgroundSent > 0 || !IdleProfile.CarriedByMonthly(monthLimit));
        Assert.True(socket.Metered > initialMetered);
        if (monthLimit >= 200000 && hourLimit >= 1000 && otherDaily <= 4000)
        {
            // Other clients can spend the protected reserve. With 58% already
            // used, transient feed waits before old cohorts expire are honest.
            Assert.Equal(0, socket.Refused);
            Assert.Equal(0, dailyWorkRemaining);
        }
        if (days > 30) Assert.True(socket.MonthExpired > 0);
        Assert.True(indexer.HeadMaxGapMinutes <= 15 || indexer.RequiredShortfall > 0);
        Assert.True(indexer.OptionalPages > 0 || indexer.RequiredShortfall > 0);
    }

    [Fact]
    public void A_smaller_plan_reports_a_capacity_shortfall_instead_of_claiming_full_freshness()
    {
        Assert.False(IdleProfile.CarriedByMonthly(1000));
        var demand = IndexerDemand(400, 98872);
        Assert.True(demand.RequiredShortfall > 0);
        Assert.Equal(0, demand.OptionalPages);
    }

    [Theory]
    [InlineData(100, 1000)]
    [InlineData(1000, 20000)]
    [InlineData(10000, 100000)]
    public async Task Published_plans_keep_polling_and_background_progress_across_31_day_expiry(int hourLimit, int monthLimit)
    {
        var socket = new RollingAccountSocket(hourLimit, monthLimit);
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Prdb).ConfigurePrimaryHttpMessageHandler(() => socket));
        socket.Time = database.Time;
        await using var scope = database.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var start = database.Time.GetUtcNow();
        var multiplier = IdleProfile.CadenceMultiplier(hourLimit, monthLimit);
        // The production cadences, plus an interested preview feed and account check.
        double[] intervals = [15, 30, 30, 60, 60, 60, 60, 360, 60, 60, 1440];
        var due = intervals.Select(_ => start).ToArray();
        var polls = new int[intervals.Length];
        var background = 0;
        var afterExpiry = 0;
        for (var minute = 0; minute < 40 * 1440; minute += 5)
        {
            var now = database.Time.GetUtcNow();
            for (var feed = 0; feed < intervals.Length; feed++)
            {
                if (due[feed] > now) continue;
                try
                {
                    await gateway.AskAsync("synthetic", PrdbWork.UserFeeds,
                        (client, token) => client.UserIdentity.GetAsync(cancellationToken: token), TestContext.Current.CancellationToken);
                    polls[feed]++;
                    due[feed] = now + TimeSpan.FromMinutes(intervals[feed] * multiplier);
                }
                catch (PrdbDeferredException) { }
            }
            try
            {
                await gateway.AskAsync("synthetic", PrdbWork.ReleaseIdentification,
                    (client, token) => client.UserIdentity.GetAsync(cancellationToken: token), TestContext.Current.CancellationToken);
                background++;
                if (minute >= 32 * 1440) afterExpiry++;
            }
            catch (PrdbDeferredException) { }
            database.Time.Advance(TimeSpan.FromMinutes(5));
        }
        Assert.All(polls, count => Assert.True(count >= 2));
        Assert.True(background > 0);
        Assert.True(afterExpiry > 0);
        Assert.True(socket.PeakHourly <= hourLimit * .8);
        Assert.True(socket.PeakMonthly <= monthLimit * .8);
        Assert.True(socket.MonthExpired > 0);
    }

    private static IndexerReport IndexerDemand(int dailyBudget, int rows)
    {
        var minimumRows = (int)Math.Round(rows * (37224d / 98872));
        var workingRows = (int)Math.Round(rows * (58644d / 98872));
        int Pages(int count) => count <= 100 ? 1 : 1 + (int)Math.Ceiling((count - 100) / 90d);
        var minimumPages = Pages(minimumRows);
        var workingPages = Pages(workingRows) - minimumPages;
        var first14 = -1;
        var first30 = -1;
        var headLast = 0;
        var headMax = 0;
        var optional = 0;
        var deficit = 0;
        // Persisted range and anchor behavior is exercised by StagedCoverageTests.
        // This demand model runs several days, with permanently oversized history.
        for (var day = 0; day < 4; day++)
        {
            var spent = 0;
            var minimum = minimumPages;
            var working = workingPages;
            for (var minute = 0; minute < 1440; minute++)
            {
                var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day).AddMinutes(minute);
                IndexerBudgetPlan Plan() => IndexerQueryBudget.Plan(dailyBudget, spent, now, 1, minimum, 1,
                    manualRemaining: minute < 720 ? 1 : 0);
                deficit = Math.Max(deficit, Plan().RequiredShortfall);
                if (minute % 15 == 0 && Plan().AvailableFor(IndexerQueryPurpose.Walk) > 0)
                {
                    spent++;
                    headMax = Math.Max(headMax, day * 1440 + minute - headLast);
                    headLast = day * 1440 + minute;
                }
                if (minute % 15 == 0 && Plan().AvailableFor(IndexerQueryPurpose.WantedSweep) > 0) spent++;
                if (minute == 720 && Plan().AvailableFor(IndexerQueryPurpose.ManualSearch) > 0) spent++;
                var purpose = minimum > 0 ? IndexerQueryPurpose.MinimumWindow : working > 0
                    ? IndexerQueryPurpose.WorkingWindow : IndexerQueryPurpose.OptionalHistory;
                if (Plan().AvailableFor(purpose) <= 0) continue;
                spent++;
                if (minimum > 0)
                {
                    minimum--;
                    if (minimum == 0 && first14 < 0) first14 = day * 1440 + minute;
                }
                else if (working > 0)
                {
                    working--;
                    if (working == 0 && first30 < 0) first30 = day * 1440 + minute;
                }
                else optional++;
                Assert.True(spent <= dailyBudget);
            }
        }
        return new(minimumPages, minimumPages + workingPages, first14, first30, headMax, optional, deficit);
    }

    private static int Setting(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
    private sealed record IndexerReport(int MinimumQueries, int InitialTargetQueries, int MinutesTo14Days,
        int MinutesTo30Days, int HeadMaxGapMinutes, int OptionalPages, int RequiredShortfall);

    private sealed class RollingAccountSocket(int hourlyLimit, int monthlyLimit) : HttpMessageHandler
    {
        private readonly Queue<DateTimeOffset> hour = new();
        private readonly Queue<DateTimeOffset> month = new();
        private readonly string identify = JsonSerializer.Serialize(new { results = Enumerable.Range(0, 200).Select(i => new
        {
            @ref = i.ToString(System.Globalization.CultureInfo.InvariantCulture), videoId = Guid.Empty, confidence = 4, matchedBy = 3,
            video = new { id = Guid.Empty, title = "Synthetic Video", actors = Array.Empty<object>(), images = Array.Empty<object>(), preNames = Array.Empty<object>() },
        }) });
        public FakeTimeProvider Time { get; set; } = new();
        public int Metered { get; private set; }
        public int Readings { get; private set; }
        public int PeakHourly { get; private set; }
        public int PeakMonthly { get; private set; }
        public int MonthExpired { get; private set; }
        public int ExternalRefused { get; private set; }
        public int Refused { get; private set; }
        public void External(int count, DateTimeOffset at)
        {
            for (var i = 0; i < count; i++)
            {
                Expire(Time.GetUtcNow());
                if (at >= Time.GetUtcNow() && (hour.Count >= hourlyLimit || month.Count >= monthlyLimit))
                {
                    ExternalRefused++;
                    continue;
                }
                if (at > Time.GetUtcNow().AddHours(-1)) hour.Enqueue(at);
                month.Enqueue(at);
                PeakHourly = Math.Max(PeakHourly, hour.Count);
                PeakMonthly = Math.Max(PeakMonthly, month.Count);
            }
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var now = Time.GetUtcNow();
            Expire(now);
            object Window(Queue<DateTimeOffset> queue, int limit, TimeSpan size) => new
                { limit, remaining = Math.Max(0, limit - queue.Count), resetsInSeconds = queue.TryPeek(out var at) ? Math.Max(0, (int)Math.Ceiling((at + size - now).TotalSeconds)) : 0 };
            if (request.RequestUri!.AbsolutePath == "/rate-limit")
            {
                Readings++;
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                    { hourly = Window(hour, hourlyLimit, TimeSpan.FromHours(1)), monthly = Window(month, monthlyLimit, TimeSpan.FromDays(31)) })));
            }
            var refused = hour.Count >= hourlyLimit || month.Count >= monthlyLimit;
            if (!refused) { hour.Enqueue(now); month.Enqueue(now); Metered++; }
            PeakHourly = Math.Max(PeakHourly, hour.Count);
            PeakMonthly = Math.Max(PeakMonthly, month.Count);
            var response = Json(request.RequestUri.AbsolutePath == "/videos/identify" ? identify : "{\"userHash\":\"synthetic-account\"}");
            if (refused)
            {
                Refused++;
                response.StatusCode = HttpStatusCode.TooManyRequests;
                response.Headers.TryAddWithoutValidation("Retry-After", "60");
            }
            foreach (var (suffix, queue, limit, size) in new[] { ("Hour", hour, hourlyLimit, TimeSpan.FromHours(1)), ("Month", month, monthlyLimit, TimeSpan.FromDays(31)) })
            {
                response.Headers.TryAddWithoutValidation("X-RateLimit-Limit-" + suffix, limit.ToString());
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining-" + suffix, Math.Max(0, limit - queue.Count).ToString());
                response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-" + suffix, (queue.TryPeek(out var at) ? Math.Max(0, (int)(at + size - now).TotalSeconds) : 0).ToString());
            }
            return Task.FromResult(response);
        }
        private void Expire(DateTimeOffset now)
        {
            while (hour.TryPeek(out var at) && at <= now.AddHours(-1)) hour.Dequeue();
            while (month.TryPeek(out var at) && at <= now.AddDays(-31)) { month.Dequeue(); MonthExpired++; }
        }
        private HttpResponseMessage Json(string body)
        {
            Time.Advance(TimeSpan.FromMilliseconds(100));
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
