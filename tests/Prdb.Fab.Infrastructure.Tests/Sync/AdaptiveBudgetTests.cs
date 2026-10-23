using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class AdaptiveBudgetTests
{
    [Fact]
    public async Task Existing_account_headroom_allows_a_useful_start_then_resumes_at_the_plan_rate()
    {
        var socket = new AccountSocket();
        await using var db = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Prdb).ConfigurePrimaryHttpMessageHandler(() => socket));
        await using var scope = db.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var sent = 0;
        while (sent < 300)
        {
            try { await Background(gateway); sent++; }
            catch (PrdbDeferredException) { break; }
        }
        // 18,700 distinct inputs need 94 full batches. They must not spend
        // hours amortising that first useful block across another month.
        Assert.InRange(sent, 100, 200);
        var deferred = await Assert.ThrowsAsync<PrdbDeferredException>(() => Background(gateway));
        Assert.InRange(deferred.Wait.TotalSeconds, 1, 30);
        db.Time.Advance(deferred.Wait + TimeSpan.FromMilliseconds(1));
        await Background(gateway);
        Assert.Equal(sent + 1, socket.Metered);
    }

    [Fact]
    public async Task Shared_consumption_and_changed_keys_cannot_inherit_old_account_capacity_or_subscription()
    {
        var socket = new AccountSocket();
        await using var db = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Prdb).ConfigurePrimaryHttpMessageHandler(() => socket));
        await using var scope = db.Scope();
        var gateway = scope.ServiceProvider.GetRequiredService<PrdbGateway>();
        var governor = db.Services.GetRequiredService<PrdbGovernor>();
        await gateway.CheckAsync("first", TestContext.Current.CancellationToken);
        Assert.Equal("Synthetic plan", governor.Subscriptions);
        Assert.Equal(200000, governor.Snapshot().Monthly!.Limit); // not the package's default quota
        socket.Month = 40000;
        await Assert.ThrowsAsync<PrdbDeferredException>(() => Background(gateway, "second"));
        Assert.Null(governor.Subscriptions);
        Assert.Equal(40000, governor.Snapshot().Monthly!.Remaining);
        Assert.Equal(1, socket.Metered); // only explicit verification
    }

    private static Task<Prdb.Sdk.Generated.Models.UserIdentityResponse?> Background(PrdbGateway gateway, string key = "first") =>
        gateway.AskAsync(key, PrdbWork.ReleaseIdentification,
            (client, token) => client.UserIdentity.GetAsync(cancellationToken: token), TestContext.Current.CancellationToken);

    private sealed class AccountSocket : HttpMessageHandler
    {
        private int hour = 805;
        public int Month { get; set; } = 80000;
        public int Metered { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var reading = request.RequestUri!.AbsolutePath == "/rate-limit";
            if (!reading) { Metered++; hour--; Month--; }
            var body = reading
                ? JsonSerializer.Serialize(new { hourly = new { limit = 1000, remaining = hour, resetsInSeconds = 60 },
                    monthly = new { limit = 200000, remaining = Month, resetsInSeconds = 60 } })
                : """{"userHash":"synthetic","activeSubscriptions":[{"packageIdentifier":"synthetic","packageTitle":"Synthetic plan"}]}""";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in new[] { ("Limit-Hour", 1000), ("Remaining-Hour", hour), ("Reset-Hour", 60),
                ("Limit-Month", 200000), ("Remaining-Month", Month), ("Reset-Month", 60) })
                response.Headers.TryAddWithoutValidation("X-RateLimit-" + name, value.ToString());
            return Task.FromResult(response);
        }
    }
}
