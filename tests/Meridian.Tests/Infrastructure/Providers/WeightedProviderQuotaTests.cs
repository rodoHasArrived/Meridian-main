using System.Net;
using System.Text.Json;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class WeightedProviderQuotaTests
{
    private static readonly TwelveDataQuotaConfig Paid = new(55, FundamentalsEntitled: true, NoDailyLimit: true);
    private static readonly TimeSpan PaidDelay = ProviderQuotaBudgets.TwelveData(Paid).MinimumDelay;

    [Fact]
    public void ExplicitUnlimitedPaidProfiles_SurviveCoreAndDesktopConfigurationRoundTrips()
    {
        var config = new AppConfig { ProviderQuotas = new(Paid, new(75, NoDailyLimit: true)) };
        var json = JsonSerializer.Serialize(config, AppConfigJsonOptions.Write);
        var desktop = JsonSerializer.Deserialize<AppConfigDto>(json, Meridian.Ui.Services.DesktopJsonOptions.PrettyPrint)!;
        var desktopJson = JsonSerializer.Serialize(desktop, Meridian.Ui.Services.DesktopJsonOptions.PrettyPrint);
        var restored = JsonSerializer.Deserialize<AppConfig>(desktopJson, AppConfigJsonOptions.Read)!;
        Assert.Equal(config.ProviderQuotas, restored.ProviderQuotas);
        Assert.Null(ProviderQuotaBudgets.TwelveData(restored.ProviderQuotas!.TwelveData).DailyMaximum);
        Assert.Null(ProviderQuotaBudgets.AlphaVantage(restored.ProviderQuotas.AlphaVantage).DailyMaximum);
        Assert.Contains("noDailyLimit", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedFactoryAndDirectConfiguration_UseTheSameEffectivePaidAccountBudget(bool throughFactory)
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderQuotas:TwelveData:CreditsPerMinute"] = throughFactory ? "8" : "55",
            ["ProviderQuotas:TwelveData:NoDailyLimit"] = throughFactory ? "false" : "true",
            ["ProviderQuotas:TwelveData:FundamentalsEntitled"] = throughFactory ? "false" : "true"
        }).Build();
        ICorporateActionProvider provider;
        TwelveDataHistoricalDataProvider? history = null;
        ServiceProvider? services = null;
        if (throughFactory)
        {
            services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration)
                .AddSingleton(registry).AddSingleton<IHttpClientFactory>(handler).BuildServiceProvider();
            var factory = new ProviderFactory(new AppConfig { ProviderQuotas = new(Paid) }, new Resolver(), services: services);
            history = Assert.IsType<TwelveDataHistoricalDataProvider>(factory.CreateTwelveDataBackfillProvider());
            Assert.Equal(55, history.MaxRequestsPerWindow);
            provider = factory.CreateCorporateActionProvider<TwelveDataCorporateActionProvider, TwelveDataHistoricalDataProvider>();
        }
        else
        {
            provider = new TwelveDataCorporateActionProvider(handler, configuration,
                NullLogger<TwelveDataCorporateActionProvider>.Instance, new Credentials("synthetic-key"), registry);
        }
        try
        {
            var pending = provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid());
            Assert.Single(handler.Requests);
            clock.Advance(PaidDelay);
            Assert.Equal(CorporateActionFetchStatus.Complete, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
            var budget = registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", ProviderQuotaBudgets.TwelveData(Paid));
            Assert.Equal(40, budget.GetBudgetStatus().UnitsInWindow);
            Assert.Null(budget.GetBudgetStatus().DailyMaximum);
            if (history is not null)
                Assert.Equal(40, history.GetRateLimitDiagnosticsSnapshot().RequestsInWindow);
        }
        finally
        {
            history?.Dispose();
            services?.Dispose();
        }
    }

    [Fact]
    public async Task WeightedConcurrentReservations_AreAtomicAndCancellationChargesNeitherWindow()
    {
        var clock = new RequestBudgetTimeProvider();
        using var limiter = new RateLimiter(new(30, TimeSpan.FromMinutes(1), TimeSpan.Zero, 45), timeProvider: clock);
        await limiter.WaitForUnitsAsync(20);
        using var cancellation = new CancellationTokenSource();
        var cancelled = limiter.WaitForUnitsAsync(20, cancellation.Token);
        var survivor = limiter.WaitForUnitsAsync(10);
        Assert.False(cancelled.IsCompleted);
        Assert.False(survivor.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await survivor.WaitAsync(TimeSpan.FromSeconds(5));
        var status = limiter.GetBudgetStatus();
        Assert.Equal(30, status.UnitsInWindow);
        Assert.Equal(30, status.DailyUnitsUsed);
        Assert.True(status.IsLimited);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, limiter.GetBudgetStatus().UnitsInWindow);
        Assert.Equal(30, limiter.GetBudgetStatus().DailyUnitsUsed);
    }

    [Fact]
    public async Task DailyAndMinuteAdmission_AreOneReservationRatherThanTwoSequentialCharges()
    {
        var clock = new RequestBudgetTimeProvider();
        using var limiter = new RateLimiter(new(50, TimeSpan.FromMinutes(1), TimeSpan.Zero, 25), timeProvider: clock);
        await limiter.WaitForUnitsAsync(20);
        using var cancellation = new CancellationTokenSource();
        var pending = limiter.WaitForUnitsAsync(10, cancellation.Token);
        Assert.False(pending.IsCompleted);
        Assert.Equal(20, limiter.GetBudgetStatus().UnitsInWindow);
        Assert.Equal(20, limiter.GetBudgetStatus().DailyUnitsUsed);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await limiter.WaitForUnitsAsync(5);
        Assert.Equal(25, limiter.GetBudgetStatus().UnitsInWindow);
        Assert.Equal(25, limiter.GetBudgetStatus().DailyUnitsUsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task InvalidOrImpossibleRequestCost_FailsBeforeCharging(int cost)
    {
        using var limiter = new RateLimiter(new(50, TimeSpan.FromMinutes(1), TimeSpan.Zero, 20));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => limiter.WaitForUnitsAsync(cost));
        Assert.Equal(0, limiter.GetBudgetStatus().UnitsInWindow);
        Assert.Equal(0, limiter.GetBudgetStatus().DailyUnitsUsed);
    }

    [Fact]
    public async Task TwelveDataDailyCredits_ResetAtMidnightUtcEvenWhenTheShortWindowHasExpired()
    {
        var clock = new RequestBudgetTimeProvider();
        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        using var limiter = new RateLimiter(ProviderQuotaBudgets.TwelveData(new(8, 2)), timeProvider: clock);
        await limiter.WaitForUnitsAsync(2);
        var waiting = limiter.WaitForSlotAsync();
        Assert.Equal("credits", limiter.GetBudgetStatus().Unit);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.Date.AddDays(1), limiter.GetBudgetStatus().DailyResetsAt!.Value.UtcDateTime);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(waiting.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, limiter.GetBudgetStatus().UnitsInWindow);
        Assert.Equal(1, limiter.GetBudgetStatus().DailyUnitsUsed);
    }

    [Fact]
    public async Task AlphaVantageDailyLimit_UsesConservativeRolling24HoursRatherThanAnotherVendorsReset()
    {
        var clock = new RequestBudgetTimeProvider();
        clock.Advance(TimeSpan.FromHours(12));
        using var limiter = new RateLimiter(ProviderQuotaBudgets.AlphaVantage(new(5, 1)), timeProvider: clock);
        await limiter.WaitForSlotAsync();
        var waiting = limiter.WaitForSlotAsync();
        clock.Advance(TimeSpan.FromHours(12));
        Assert.False(waiting.IsCompleted, "UTC midnight is not an Alpha Vantage reset authority");
        Assert.Equal(1, limiter.GetBudgetStatus().DailyUnitsUsed);
        Assert.Equal(0, limiter.GetBudgetStatus().UnitsInWindow);
        clock.Advance(TimeSpan.FromHours(12));
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, limiter.GetBudgetStatus().DailyUnitsUsed);
    }

    [Fact]
    public async Task Registry_RecreationAndRotationRetainEachCredentialsDailyCharges_AndRejectDifferentProfiles()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        var profile = ProviderQuotaBudgets.TwelveData(Paid with { DailyCredits = 45, NoDailyLimit = false });
        var first = registry.GetOrCreate("TwelveData", "synthetic-old-key", "api-requests", profile);
        await first.WaitForUnitsAsync(20);
        var recreated = registry.GetOrCreate("twelvedata", "synthetic-old-key", "api-requests", profile with { });
        Assert.Same(first, recreated);
        var rotated = registry.GetOrCreate("twelvedata", "synthetic-new-key", "api-requests", profile);
        Assert.Equal(0, rotated.GetBudgetStatus().DailyUnitsUsed);
        Assert.Equal(20, recreated.GetBudgetStatus().DailyUnitsUsed);
        Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate("twelvedata", "synthetic-old-key", "api-requests", profile with { DailyMaximum = null }));
        Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate("twelvedata", "synthetic-old-key", "api-requests", profile with { Unit = "requests" }));
        Assert.All(registry.GetAllStatus().Keys, key => Assert.DoesNotContain("synthetic-", key));
    }

    [Theory]
    [InlineData(false, 8, "entitlement-unavailable")]
    [InlineData(false, 55, "entitlement-unavailable")]
    [InlineData(true, 8, "quota-capacity-insufficient")]
    public async Task TwelveDataFundamentals_RequireExplicitEntitlementAndCapacityBeforeHttp(bool entitled, int credits, string failure)
    {
        using var handler = new RecordingHandler();
        using var registry = new RateLimiterRegistry();
        var provider = new TwelveDataCorporateActionProvider(handler, new ConfigurationBuilder().Build(),
            NullLogger<TwelveDataCorporateActionProvider>.Instance, new Credentials("synthetic-key"), registry,
            new(credits, FundamentalsEntitled: entitled, NoDailyLimit: true));

        var result = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        Assert.Equal(CorporateActionFetchStatus.Failed, result.Status);
        Assert.Contains(result.Failures, issue => issue.Code == failure);
        Assert.Empty(result.Actions);
        Assert.Equal(0, handler.CreatedClients);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PaidTwelveDataFundamentals_ChargeTwentyCreditsForEachEndpointAndShareWithHistory()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler();
        var provider = new TwelveDataCorporateActionProvider(handler, new ConfigurationBuilder().Build(),
            NullLogger<TwelveDataCorporateActionProvider>.Instance, new Credentials("synthetic-key"), registry, Paid);
        var fetch = provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid());
        Assert.Single(handler.Requests);
        clock.Advance(PaidDelay);
        var result = await fetch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CorporateActionFetchStatus.Complete, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        var budget = registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", ProviderQuotaBudgets.TwelveData(Paid));
        Assert.Equal(40, budget.GetBudgetStatus().UnitsInWindow);
        using (var history = new TwelveDataHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry, quotaConfig: Paid))
        {
            var bars = history.GetDailyBarsAsync("AAPL", new(2026, 1, 1), new(2026, 1, 2));
            clock.Advance(PaidDelay);
            Assert.Empty(await bars.WaitAsync(TimeSpan.FromSeconds(5)));
            var status = history.GetRateLimitDiagnosticsSnapshot();
            Assert.Equal("credits", status.QuotaUnit);
            Assert.Equal(41, status.RequestsInWindow);
            Assert.Null(status.DailyMaximum);
        }
        using var replacement = new TwelveDataHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry, quotaConfig: Paid);
        Assert.Equal(41, replacement.GetRateLimitDiagnosticsSnapshot().RequestsInWindow);
    }

    [Fact]
    public async Task DailyQuotaWait_DoesNotStartHistoricalHttpTimeout_AndCancellationDoesNotCharge()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        var profile = new AlphaVantageQuotaConfig(5, 1);
        var budget = registry.GetOrCreate("alphavantage", "synthetic-key", "api-requests", ProviderQuotaBudgets.AlphaVantage(profile));
        await budget.WaitForSlotAsync();
        using var handler = new RecordingHandler();
        using var history = new AlphaVantageHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry, quotaConfig: profile);
        using var cancellation = new CancellationTokenSource();
        var pending = history.GetDailyBarsAsync("AAPL", new(2026, 1, 1), new(2026, 1, 2), cancellation.Token);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(pending.IsCompleted);
        Assert.Empty(handler.Requests);
        Assert.True(history.GetRateLimitDiagnosticsSnapshot().IsRateLimited);
        Assert.Equal("daily-quota", history.GetRateLimitDiagnosticsSnapshot().Reason);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, budget.GetBudgetStatus().DailyUnitsUsed);
    }

    [Fact]
    public async Task AlphaVantageCapabilitiesAndReplacementInstances_ShareBothAccountLimits()
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler();
        using var history = new AlphaVantageHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        using var search = new AlphaVantageSymbolSearchProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        var actions = new AlphaVantageCorporateActionProvider(handler, new ConfigurationBuilder().Build(),
            NullLogger<AlphaVantageCorporateActionProvider>.Instance, new Credentials("synthetic-key"), registry);
        var work = Task.WhenAll(
            history.GetDailyBarsAsync("AAPL", new(2026, 1, 1), new(2026, 1, 2)),
            search.SearchAsync("AAPL"),
            actions.FetchWithOutcomeAsync("AAPL", Guid.NewGuid()));
        for (var count = 1; count < 3; count++)
        {
            var expected = count;
            await RequestBudgetTimeProvider.WaitUntilAsync(() => handler.Requests.Count == expected && clock.HasTimerDueWithin(TimeSpan.FromSeconds(12)));
            clock.Advance(TimeSpan.FromSeconds(12));
        }
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(handler.Requests, uri => uri.Query.Contains("function=SYMBOL_SEARCH", StringComparison.Ordinal));
        Assert.Equal(2, handler.Requests.Count(uri => uri.Query.Contains("function=TIME_SERIES_DAILY_ADJUSTED", StringComparison.Ordinal)));
        history.Dispose();
        using var replacement = new AlphaVantageHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry);
        var status = replacement.GetRateLimitDiagnosticsSnapshot();
        Assert.Equal("requests", status.QuotaUnit);
        Assert.Equal(3, status.RequestsInWindow);
        Assert.Equal(3, status.DailyUnitsUsed);
        Assert.Equal(25, status.DailyMaximum);
        Assert.Equal("rolling-24-hours", status.DailyResetPolicy);
    }

    private sealed class Credentials(string key) : ICredentialContext
    {
        public bool IsAuthoritative => true;
        public string? Get(string name) => key;
        public bool IsConfigured(string name) => true;
    }

    private sealed class Resolver : IProviderCredentialResolver
    {
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
            => new Credentials("synthetic-key");
    }

    private sealed class RecordingHandler : HttpMessageHandler, IHttpClientFactory
    {
        internal List<Uri> Requests { get; } = [];
        internal int CreatedClients { get; private set; }
        internal HttpClient CreateClient() => new(this, disposeHandler: false);
        public HttpClient CreateClient(string name)
        {
            CreatedClients++;
            return CreateClient();
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"values\":[],\"data\":[],\"dividends\":[],\"splits\":[],\"Time Series (Daily)\":{}}")
            });
        }
    }
}
