using System.Net;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.TwelveData;

namespace Meridian.Tests.Infrastructure.Providers;

[Trait("Category", "Unit")]
public sealed class SymbolSearchProviderLifetimeTests
{
    private const string ApiKey = "synthetic-search-lifetime-key";

    [Theory]
    [InlineData(true, "search")]
    [InlineData(true, "authoritative")]
    [InlineData(true, "details")]
    [InlineData(false, "search")]
    [InlineData(false, "authoritative")]
    [InlineData(false, "details")]
    public async Task Dispose_CancelsDailyQuotaWait_AndPreservesCapacityForReplacement(bool alphaVantage, string operation)
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        var profile = alphaVantage
            ? ProviderQuotaBudgets.AlphaVantage(new(5, 1))
            : ProviderQuotaBudgets.TwelveData(new(8, 1));
        var providerName = alphaVantage ? "alphavantage" : "twelvedata";
        var budget = registry.GetOrCreate(providerName, ApiKey, "api-requests", profile);
        await budget.WaitForSlotAsync();
        using var handler = new SearchHandler();
        using var provider = CreateProvider(alphaVantage, handler, registry);

        var pending = StartLookup(provider, operation);
        await RequestBudgetTimeProvider.WaitUntilAsync(() => clock.PendingTimers == 1);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, handler.CallCount);

        provider.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, clock.PendingTimers);
        Assert.Equal(1, budget.GetBudgetStatus().DailyUnitsUsed);
        Assert.Equal(0, handler.CallCount);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, budget.GetBudgetStatus().DailyUnitsUsed);
        using var replacement = CreateProvider(alphaVantage, handler, registry);
        Assert.Empty(await replacement.SearchAsync("MSFT").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, budget.GetBudgetStatus().DailyUnitsUsed);
        Assert.Same(budget, registry.GetOrCreate(providerName, ApiKey, "api-requests", profile));
    }

    [Theory]
    [InlineData(true, "search")]
    [InlineData(true, "authoritative")]
    [InlineData(true, "details")]
    [InlineData(false, "search")]
    [InlineData(false, "authoritative")]
    [InlineData(false, "details")]
    public async Task Dispose_CancelsInFlightHttp_WithoutReturningAnEmptySuccess(bool alphaVantage, string operation)
    {
        using var registry = new RateLimiterRegistry();
        using var handler = new PendingSearchHandler();
        using var provider = CreateProvider(alphaVantage, handler, registry);
        var pending = StartLookup(provider, operation);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        provider.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(handler.RequestCancellation.IsCancellationRequested);
    }

    private static BaseSymbolSearchProvider CreateProvider(
        bool alphaVantage,
        HttpMessageHandler handler,
        RateLimiterRegistry registry)
        => alphaVantage
            ? new AlphaVantageSymbolSearchProvider(ApiKey, new HttpClient(handler, disposeHandler: false),
                rateLimiterRegistry: registry, quotaConfig: new(5, 1))
            : new TwelveDataSymbolSearchProvider(ApiKey, new HttpClient(handler, disposeHandler: false),
                rateLimiterRegistry: registry, quotaConfig: new(8, 1));

    private static Task StartLookup(BaseSymbolSearchProvider provider, string operation)
        => operation switch
        {
            "search" => provider.SearchAsync("MSFT"),
            "authoritative" => provider.SearchAuthoritativeAsync("MSFT"),
            "details" => provider.GetDetailsAsync(new SymbolId("MSFT")),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private sealed class SearchHandler : HttpMessageHandler
    {
        internal int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"bestMatches":[],"data":[],"status":"ok"}""")
            });
        }
    }

    private sealed class PendingSearchHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken RequestCancellation { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCancellation = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
