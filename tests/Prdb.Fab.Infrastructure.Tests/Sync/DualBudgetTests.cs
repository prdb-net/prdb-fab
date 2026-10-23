using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class DualBudgetTests
{
    [Theory]
    [InlineData(0, 900, "hourly")]
    [InlineData(90, 0, "monthly")]
    public async Task Either_shared_window_can_hold_work(int hour, int month, string reason)
    {
        var socket = new AccountSocket { Hour = hour, Month = month };
        await using var database = await CreateAsync(socket);
        await using var scope = database.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var deferred = await Assert.ThrowsAsync<PrdbDeferredException>(() => AskAsync(gateway));
        Assert.Contains(reason, deferred.Deferral);
        Assert.Equal(0, socket.Metered);
        Assert.Equal(1, socket.Readings);
    }

    [Fact]
    public async Task Concurrent_lanes_reserve_capacity_and_a_rolling_expiry_does_not_reset_it()
    {
        var socket = new AccountSocket { Hour = 21, Month = 900 };
        await using var database = await CreateAsync(socket);
        await using var scope = database.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        async Task<bool> TryAsync()
        {
            try { await AskAsync(gateway); return true; }
            catch (PrdbDeferredException) { return false; }
        }
        var sent = await Task.WhenAll(TryAsync(), TryAsync(), TryAsync());
        Assert.Equal(1, sent.Count(value => value));
        Assert.Equal(1, socket.Metered);
        database.Time.Advance(TimeSpan.FromMinutes(1));
        // Another account consumer spent the available monthly capacity.
        socket.Hour = 90;
        socket.Month = 0;
        Assert.False(await TryAsync());
        Assert.Equal(2, socket.Readings);
        // Only one oldest monthly slot expires, not the whole allowance.
        database.Time.Advance(TimeSpan.FromMinutes(1));
        socket.Month = 1;
        Assert.False(await TryAsync());
        Assert.Equal(1, socket.Metered);
        // Arrived-file work protects the same 20% reserve.
        await Assert.ThrowsAsync<PrdbDeferredException>(() => gateway.AskAsync("test-key", PrdbWork.Identification,
            (client, token) => client.UserIdentity.GetAsync(cancellationToken: token), TestContext.Current.CancellationToken));
        Assert.Equal(1, socket.Metered);
    }

    [Fact]
    public async Task Restart_reads_remote_counters_and_missing_counters_never_open_the_budget()
    {
        var socket = new AccountSocket { Hour = 0, Month = 900 };
        await using (var first = await CreateAsync(socket))
        {
            await using var scope = first.Scope();
            await Assert.ThrowsAsync<PrdbDeferredException>(() => AskAsync(scope.ServiceProvider.GetRequiredService<PrdbGateway>()));
        }
        socket = new AccountSocket { Hour = 0, Month = 900 };
        await using var second = await CreateAsync(socket);
        await using var again = second.Scope();
        await Assert.ThrowsAsync<PrdbDeferredException>(() => AskAsync(again.ServiceProvider.GetRequiredService<PrdbGateway>()));
        Assert.Equal(0, socket.Metered);
        socket.Missing = true;
        second.Time.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<PrdbDeferredException>(() => AskAsync(again.ServiceProvider.GetRequiredService<PrdbGateway>()));
        Assert.Equal(0, socket.Metered);
    }

    [Fact]
    public async Task Retry_after_wins_over_pacing_and_no_refresh_busy_polls()
    {
        var socket = new AccountSocket { TooMany = true };
        await using var database = await CreateAsync(socket);
        await using var scope = database.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var first = await Assert.ThrowsAsync<PrdbDeferredException>(() => AskAsync(gateway));
        Assert.Equal(TimeSpan.FromSeconds(90), first.Wait);
        database.Time.Advance(TimeSpan.FromSeconds(60));
        var second = await Assert.ThrowsAsync<PrdbDeferredException>(() => AskAsync(gateway));
        Assert.Equal(TimeSpan.FromSeconds(30), second.Wait);
        Assert.Equal(1, socket.Readings);
        Assert.Equal(1, socket.Metered);
    }

    private static Task<TestDatabase> CreateAsync(AccountSocket socket) => TestDatabase.CreateAsync(
        also: services => services.AddHttpClient(FabTransports.Prdb).ConfigurePrimaryHttpMessageHandler(() => socket));
    private static Task<Prdb.Sdk.Generated.Models.UserIdentityResponse?> AskAsync(PrdbGateway gateway) =>
        gateway.AskAsync("test-key", PrdbWork.UserFeeds,
            (client, token) => client.UserIdentity.GetAsync(cancellationToken: token), TestContext.Current.CancellationToken);

    private sealed class AccountSocket : HttpMessageHandler
    {
        public int Hour { get; set; } = 90;
        public int Month { get; set; } = 900;
        public bool Missing { get; set; }
        public bool TooMany { get; set; }
        public int Readings { get; private set; }
        public int Metered { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/rate-limit")
            {
                Readings++;
                return Task.FromResult(Json(Missing ? "{}" : System.Text.Json.JsonSerializer.Serialize(new { hourly = new { limit = 100, remaining = Hour, resetsInSeconds = 1 }, monthly = new { limit = 1000, remaining = Month, resetsInSeconds = 1 } })));
            }
            Metered++;
            Hour--;
            Month--;
            var response = Json("{\"userHash\":\"synthetic\"}");
            if (TooMany)
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;
                response.Headers.TryAddWithoutValidation("Retry-After", "90");
            }
            foreach (var (name, value) in new[] { ("Limit-Hour",100),("Remaining-Hour",Hour),("Reset-Hour",1),
                ("Limit-Month",1000),("Remaining-Month",Month),("Reset-Month",1) })
                response.Headers.TryAddWithoutValidation("X-RateLimit-" + name, value.ToString());
            return Task.FromResult(response);
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
