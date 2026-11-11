using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class SharedProviderRequestBudgetTests
{
    [Fact]
    public async Task Registry_CoordinatesConcurrentCallers_ExpiresAtExactWindowBoundary()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        var history = registry.GetOrCreate("TwelveData", "synthetic-key", "api-requests", 2, TimeSpan.FromSeconds(10));
        var search = registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", 2, TimeSpan.FromSeconds(10));
        Assert.Same(history, search);
        await history.WaitForSlotAsync();
        await search.WaitForSlotAsync();
        var pending = history.WaitForSlotAsync();
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(10));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, history.GetStatus().RequestsInWindow);
    }

    [Fact]
    public async Task Registry_SeparatesProviderCredentialAndQuotaGroup_AndDoesNotExposeSecrets()
    {
        using var registry = new RateLimiterRegistry();
        var first = registry.GetOrCreate("vendor", "synthetic-key", "requests", 2, TimeSpan.FromMinutes(1));
        var rotated = registry.GetOrCreate("vendor", "rotated-key", "requests", 2, TimeSpan.FromMinutes(1));
        var group = registry.GetOrCreate("vendor", "synthetic-key", "streaming", 2, TimeSpan.FromMinutes(1));
        var other = registry.GetOrCreate("other", "synthetic-key", "requests", 2, TimeSpan.FromMinutes(1));
        await first.WaitForSlotAsync();
        Assert.Equal(0, rotated.GetStatus().RequestsInWindow);
        Assert.Equal(0, group.GetStatus().RequestsInWindow);
        Assert.Equal(0, other.GetStatus().RequestsInWindow);
        Assert.All(registry.GetAllStatus().Keys, key => Assert.DoesNotContain("synthetic-key", key));
        Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate("vendor", "synthetic-key", "requests", 3, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task CancellingQueuedCaller_DoesNotChargeCapacity_OrCancelAnotherCaller()
    {
        var clock = new RequestBudgetTimeProvider();
        using var limiter = new RateLimiter(1, TimeSpan.FromSeconds(10), timeProvider: clock);
        await limiter.WaitForSlotAsync();
        using var cancellation = new CancellationTokenSource();
        var cancelled = limiter.WaitForSlotAsync(cancellation.Token);
        var surviving = limiter.WaitForSlotAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, limiter.GetStatus().RequestsInWindow);
        await RequestBudgetTimeProvider.WaitUntilAsync(() => clock.HasTimerDueWithin(TimeSpan.FromSeconds(10)));
        clock.Advance(TimeSpan.FromSeconds(10));
        await surviving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, limiter.GetStatus().RequestsInWindow);
    }

    [Fact]
    public async Task ConcurrentHistorySearchAndCorporateActions_SharePacingAndAttemptCounts()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler(clock);
        using var history = new TwelveDataHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        using var search = new TwelveDataSymbolSearchProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        var actions = CreateActions(handler, registry, new MutableCredentials("synthetic-key"));
        var work = Task.WhenAll(
            history.GetDailyBarsAsync("AAPL", null, null),
            search.SearchAsync("AAPL", 10, CancellationToken.None),
            actions.FetchAsync("AAPL", Guid.NewGuid()));

        for (var count = 1; count < 4; count++)
        {
            var expected = count;
            await RequestBudgetTimeProvider.WaitUntilAsync(() => handler.Requests.Count == expected && clock.HasTimerDueWithin(TimeSpan.FromSeconds(7.5)));
            clock.Advance(TimeSpan.FromSeconds(7.5));
        }
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        var requests = handler.Requests.ToArray();
        Assert.Equal(4, requests.Length);
        Assert.Contains(requests, request => request.Path == "/time_series");
        Assert.Contains(requests, request => request.Path == "/symbol_search");
        Assert.Contains(requests, request => request.Path == "/dividends");
        Assert.Contains(requests, request => request.Path == "/splits");
        for (var i = 1; i < requests.Length; i++)
            Assert.Equal(TimeSpan.FromSeconds(7.5), requests[i].Time - requests[i - 1].Time);
        Assert.Equal(4, registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", 8, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(7.5)).GetStatus().RequestsInWindow);

        history.Dispose();
        var afterDisposal = search.SearchAsync("MSFT", 10, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(7.5));
        await afterDisposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task SearchRetryAfter_BlocksHistoryUntilSharedCooldownExpires()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler(clock) { ThrottleSearch = true };
        using var search = new TwelveDataSymbolSearchProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        using var history = new TwelveDataHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        await Assert.ThrowsAsync<RateLimitException>(() => search.SearchAsync("AAPL", 10, CancellationToken.None));
        var pending = history.GetDailyBarsAsync("AAPL", null, null);
        clock.Advance(TimeSpan.FromSeconds(19));
        Assert.Equal(1, handler.Requests.Count);
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExplicitZeroOrExpiredRetryAfter_EnforcesOnlyConfiguredPacing(bool corporateActions, bool expiredDate)
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler(clock)
        {
            ThrottleSearch = !corporateActions,
            ThrottleDividends = corporateActions,
            RetryAfter = expiredDate
                ? new RetryConditionHeaderValue(clock.GetUtcNow() - TimeSpan.FromSeconds(10))
                : new RetryConditionHeaderValue(TimeSpan.Zero)
        };
        using var history = new TwelveDataHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        Task pending;
        if (corporateActions)
        {
            pending = CreateActions(handler, registry, new MutableCredentials("synthetic-key")).FetchAsync("AAPL", Guid.NewGuid());
        }
        else
        {
            using var search = new TwelveDataSymbolSearchProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
            var limited = await Assert.ThrowsAsync<RateLimitException>(() => search.SearchAsync("AAPL", 10, CancellationToken.None));
            Assert.Equal(TimeSpan.Zero, limited.RetryAfter);
            pending = history.GetDailyBarsAsync("AAPL", null, null);
        }
        await RequestBudgetTimeProvider.WaitUntilAsync(() => clock.HasTimerDueWithin(TimeSpan.FromSeconds(7.5)));
        clock.Advance(TimeSpan.FromSeconds(7.5));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CorporateActionCredentialRotatedWhileWaiting_UsesCurrentKeyAndItsBudget()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        var old = registry.GetOrCreate("twelvedata", "old-key", "api-requests", 8, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(7.5));
        await old.WaitForSlotAsync();
        using var handler = new RecordingHandler(clock);
        var credentials = new MutableCredentials("old-key");
        var provider = CreateActions(handler, registry, credentials);
        var pending = provider.FetchAsync("AAPL", Guid.NewGuid());
        credentials.Key = "new-key";
        clock.Advance(TimeSpan.FromSeconds(7.5));
        await RequestBudgetTimeProvider.WaitUntilAsync(() => handler.Requests.Count == 1 && clock.HasTimerDueWithin(TimeSpan.FromSeconds(7.5)));
        clock.Advance(TimeSpan.FromSeconds(7.5));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(handler.Requests, request => Assert.Contains("apikey=new-key", request.Query));
        Assert.Equal(2, registry.GetOrCreate("twelvedata", "new-key", "api-requests", 8, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(7.5)).GetStatus().RequestsInWindow);
    }

    [Fact]
    public async Task CorporateActionCredentialDeletedWhileWaiting_SendsNothing()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        await registry.GetOrCreate("twelvedata", "old-key", "api-requests", 8, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(7.5)).WaitForSlotAsync();
        using var handler = new RecordingHandler(clock);
        var credentials = new MutableCredentials("old-key");
        var pending = CreateActions(handler, registry, credentials).FetchAsync("AAPL", Guid.NewGuid());
        credentials.Key = null;
        clock.Advance(TimeSpan.FromSeconds(7.5));
        Assert.Empty(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(handler.Requests);
    }

    private static TwelveDataCorporateActionProvider CreateActions(RecordingHandler handler, RateLimiterRegistry registry, ICredentialContext credentials)
        => new(handler, new ConfigurationBuilder().Build(), NullLogger<TwelveDataCorporateActionProvider>.Instance, credentials, registry);

    private sealed class MutableCredentials(string? key) : ICredentialContext
    {
        public string? Key = key;
        public bool IsAuthoritative => true;
        public string? Get(string name) => Key;
        public bool IsConfigured(string name) => !string.IsNullOrEmpty(Key);
    }

    private sealed class RecordingHandler(RequestBudgetTimeProvider clock) : HttpMessageHandler, IHttpClientFactory
    {
        internal ConcurrentQueue<(string Path, string Query, DateTimeOffset Time)> Requests { get; } = new();
        internal bool ThrottleSearch;
        internal bool ThrottleDividends;
        internal RetryConditionHeaderValue RetryAfter = new(TimeSpan.FromSeconds(20));
        internal HttpClient CreateClient() => new(this, disposeHandler: false);
        public HttpClient CreateClient(string name) => CreateClient();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue((uri.AbsolutePath, uri.Query, clock.GetUtcNow()));
            if ((ThrottleSearch && uri.AbsolutePath == "/symbol_search") || (ThrottleDividends && uri.AbsolutePath == "/dividends"))
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = RetryAfter;
                return Task.FromResult(limited);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"values\":[],\"data\":[],\"dividends\":[],\"splits\":[]}")
            });
        }
    }
}
