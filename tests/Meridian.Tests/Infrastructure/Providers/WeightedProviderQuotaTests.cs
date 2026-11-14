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

    [Theory]
    [InlineData(2, 30000)]
    [InlineData(8, 7500)]
    [InlineData(100, 1091)]
    public void RequestPacing_ComposesWithWeightedCreditCapacityWithoutChangingAccountLimits(int requests, int milliseconds)
    {
        var runtime = new BackfillProviderRuntimeOptions(7, requests, TimeSpan.FromMinutes(1));
        var budget = ProviderQuotaBudgets.TwelveData(Paid with { DailyCredits = 800, NoDailyLimit = false }, runtime);

        Assert.Equal(55, budget.Maximum);
        Assert.Equal(800, budget.DailyMaximum);
        Assert.Equal("credits", budget.Unit);
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), budget.MinimumDelay);
    }

    [Fact]
    public void IncompleteRequestPacingDeclaration_IsRefusedRatherThanDroppingTheDeclaredControl()
    {
        Assert.Throws<ArgumentException>(() => ProviderQuotaBudgets.TwelveData(Paid, new(7, Requests: 2)));
        Assert.Throws<ArgumentException>(() => ProviderQuotaBudgets.TwelveData(Paid, new(7, Window: TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public void SharedAccount_RejectsCapabilitiesWithDifferentRequestPacingProfiles()
    {
        using var registry = new RateLimiterRegistry();
        var ordinary = ProviderQuotaBudgets.TwelveData(Paid);
        var slower = ProviderQuotaBudgets.TwelveData(Paid, new(7, 2, TimeSpan.FromMinutes(1)));
        registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", ordinary);

        Assert.Throws<InvalidOperationException>(() =>
            registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", slower));
    }

    [Fact]
    public void ConfigurationSchema_RetainsConditionallyOmittedQuotaAndOwnershipInputsButExcludesAlwaysIgnoredAliases()
    {
        var schema = new ConfigJsonSchemaGenerator().GenerateSchema();
        Assert.NotNull(schema["properties"]![nameof(AppConfig.ProviderQuotas)]);
        Assert.NotNull(schema["$defs"]![nameof(ProviderConnectionsConfig)]!["properties"]![nameof(ProviderConnectionsConfig.RuntimeCredentials)]);
        Assert.NotNull(schema["$defs"]![nameof(ProviderConnectionsConfig)]!["properties"]![nameof(ProviderConnectionsConfig.OwnershipRequiredProviders)]);
        Assert.False(schema["$defs"]![nameof(AlpacaOptions)]!["properties"]!.AsObject().ContainsKey(nameof(AlpacaOptions.EquitiesFeed)));
        Assert.NotNull(schema["$defs"]![nameof(TwelveDataQuotaConfig)]!["properties"]![nameof(TwelveDataQuotaConfig.FundamentalsEntitled)]);
        Assert.NotNull(schema["$defs"]![nameof(TwelveDataQuotaConfig)]!["properties"]![nameof(TwelveDataQuotaConfig.NoDailyLimit)]);
    }

    [Fact]
    public void UnconfiguredQuotaSection_IsOmittedToPreserveLegacyConfigurationSerialization()
    {
        var json = JsonSerializer.Serialize(new AppConfig(), AppConfigJsonOptions.Write);
        Assert.DoesNotContain("providerQuotas", json);
        Assert.Null(JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read)!.ProviderQuotas);
    }

    [Fact]
    public void DiagnosticSnapshot_RemainsCoherentWhenAnotherCapabilityChargesDuringObservation()
    {
        var clock = new ChargingTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        var profile = new TwelveDataQuotaConfig(100, 3);
        var budget = registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", ProviderQuotaBudgets.TwelveData(profile));
        using var handler = new RecordingHandler();
        using var history = new TwelveDataHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry, quotaConfig: profile);
        // Another capability can reserve at any clock observation. This deterministic interleaving
        // exposes a second shared-budget read without relying on thread scheduling.
        clock.OnRead = budget.RecordRequest;

        var snapshot = history.GetRateLimitDiagnosticsSnapshot();

        clock.OnRead = null;
        Assert.Equal(snapshot.RequestsInWindow, snapshot.DailyUnitsUsed);
        Assert.Equal(snapshot.DailyUnitsUsed >= snapshot.DailyMaximum, snapshot.IsRateLimited);
        Assert.Equal(snapshot.IsRateLimited ? "daily-quota" : null, snapshot.Reason);
        Assert.Equal(Math.Max(0, 3 - snapshot.DailyUnitsUsed!.Value), snapshot.RemainingRequests);
        Assert.Equal((double)snapshot.DailyUnitsUsed.Value / 3, snapshot.UsageRatio);
    }

    [Fact]
    public void ExplicitUnlimitedPaidProfiles_SurviveCoreAndDesktopConfigurationRoundTrips()
    {
        // Desktop configuration models compression as a non-nullable boolean.
        var config = new AppConfig(Compress: false) { ProviderQuotas = new(Paid, new(75, NoDailyLimit: true)) };
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
    [InlineData(false, 8)]
    [InlineData(false, 2)]
    [InlineData(true, 8)]
    [InlineData(true, 2)]
    public async Task TypedFactoryAndDirectConfiguration_UseTheSameEffectivePaidAccountBudget(bool throughFactory, int requests)
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderQuotas:TwelveData:CreditsPerMinute"] = throughFactory ? "8" : "55",
            ["ProviderQuotas:TwelveData:NoDailyLimit"] = throughFactory ? "false" : "true",
            ["ProviderQuotas:TwelveData:FundamentalsEntitled"] = throughFactory ? "false" : "true",
            ["Backfill:Providers:TwelveData:Priority"] = "7",
            ["Backfill:Providers:TwelveData:RateLimitPerMinute"] = throughFactory ? (requests == 8 ? "2" : "8") : requests.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).Build();
        var runtime = new BackfillProviderRuntimeOptions(7, requests, TimeSpan.FromMinutes(1));
        var composedBudget = ProviderQuotaBudgets.TwelveData(Paid, runtime);
        ICorporateActionProvider provider;
        TwelveDataHistoricalDataProvider? history = null;
        TwelveDataSymbolSearchProvider? search = null;
        ServiceProvider? services = null;
        if (throughFactory)
        {
            services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration)
                .AddSingleton(registry).AddSingleton<IHttpClientFactory>(handler).BuildServiceProvider();
            var factory = new ProviderFactory(new AppConfig(Backfill: new(Providers: new(
                TwelveData: new(Priority: 7, RateLimitPerMinute: requests))))
            { ProviderQuotas = new(Paid) }, new Resolver(), services: services);
            history = Assert.IsType<TwelveDataHistoricalDataProvider>(factory.CreateTwelveDataBackfillProvider());
            Assert.Equal(55, history.MaxRequestsPerWindow);
            Assert.Equal(7, history.Priority);
            Assert.Equal(composedBudget.MinimumDelay, history.RateLimitDelay);
            search = Assert.IsType<TwelveDataSymbolSearchProvider>(factory.CreateTwelveDataSearchProvider());
            Assert.Equal(7, search.Priority);
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
            clock.Advance(composedBudget.MinimumDelay - TimeSpan.FromMilliseconds(1));
            Assert.Single(handler.Requests);
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(CorporateActionFetchStatus.Complete, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
            var budget = registry.GetOrCreate("twelvedata", "synthetic-key", "api-requests", composedBudget);
            Assert.Equal(40, budget.GetBudgetStatus().UnitsInWindow);
            Assert.Null(budget.GetBudgetStatus().DailyMaximum);
            var retainedAfterSearch = requests == 2 ? 21 : 41;
            if (search is not null)
            {
                var searching = search.SearchAsync("AAPL", 10, CancellationToken.None);
                clock.Advance(composedBudget.MinimumDelay);
                Assert.Empty(await searching.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(retainedAfterSearch, budget.GetBudgetStatus().UnitsInWindow);
            }
            if (history is not null)
                Assert.Equal(retainedAfterSearch, history.GetRateLimitDiagnosticsSnapshot().RequestsInWindow);
        }
        finally
        {
            history?.Dispose();
            search?.Dispose();
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
    public async Task FractionalMillisecondWait_SchedulesPositiveDelayRatherThanSpinning()
    {
        var clock = new RequestBudgetTimeProvider();
        using var limiter = new RateLimiter(10, TimeSpan.FromMinutes(1), TimeSpan.FromTicks(9090), timeProvider: clock);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await limiter.WaitForSlotAsync(cancellation.Token);

        var pending = limiter.WaitForSlotAsync(cancellation.Token);

        Assert.False(pending.IsCompleted);
        Assert.True(clock.HasTimerDueWithin(TimeSpan.FromMilliseconds(1)));
        clock.Advance(TimeSpan.FromTicks(9090));
        Assert.False(pending.IsCompleted, "a positive fractional wait must never charge early");
        clock.Advance(TimeSpan.FromTicks(910));
        Assert.Equal(TimeSpan.FromMilliseconds(1), await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, limiter.GetBudgetStatus().UnitsInWindow);
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
        Assert.Equal(0, history.GetRateLimitDiagnosticsSnapshot().RemainingRequests);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, budget.GetBudgetStatus().DailyUnitsUsed);
    }

    [Theory]
    [InlineData(5, 12000, 3)]
    [InlineData(2, 30000, 2)]
    public async Task AlphaVantageCapabilitiesAndReplacementInstances_ShareBothAccountLimits(int requests, int delayMilliseconds, int retainedInWindow)
    {
        var clock = new RequestBudgetTimeProvider();
        using var registry = new RateLimiterRegistry(timeProvider: clock);
        using var handler = new RecordingHandler();
        var runtime = new BackfillProviderRuntimeOptions(7, requests, TimeSpan.FromMinutes(1));
        var delay = TimeSpan.FromMilliseconds(delayMilliseconds);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backfill:Providers:AlphaVantage:RateLimitPerMinute"] = requests.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).Build();
        using var history = new AlphaVantageHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry, runtimeOptions: runtime);
        using var search = new AlphaVantageSymbolSearchProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry, runtimeOptions: runtime);
        var actions = new AlphaVantageCorporateActionProvider(handler, configuration,
            NullLogger<AlphaVantageCorporateActionProvider>.Instance, new Credentials("synthetic-key"), registry);
        var work = Task.WhenAll(
            history.GetDailyBarsAsync("AAPL", new(2026, 1, 1), new(2026, 1, 2)),
            search.SearchAsync("AAPL", 10, CancellationToken.None),
            actions.FetchWithOutcomeAsync("AAPL", Guid.NewGuid()));
        for (var count = 1; count < 3; count++)
        {
            var expected = count;
            await RequestBudgetTimeProvider.WaitUntilAsync(() => handler.Requests.Count == expected && clock.HasTimerDueWithin(delay));
            clock.Advance(delay);
        }
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(handler.Requests, uri => uri.Query.Contains("function=SYMBOL_SEARCH", StringComparison.Ordinal));
        Assert.Equal(2, handler.Requests.Count(uri => uri.Query.Contains("function=TIME_SERIES_DAILY_ADJUSTED", StringComparison.Ordinal)));
        history.Dispose();
        using var replacement = new AlphaVantageHistoricalDataProvider("synthetic-key", handler.CreateClient(), rateLimiterRegistry: registry, runtimeOptions: runtime);
        var status = replacement.GetRateLimitDiagnosticsSnapshot();
        Assert.Equal("requests", status.QuotaUnit);
        Assert.Equal(retainedInWindow, status.RequestsInWindow);
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

    private sealed class ChargingTimeProvider : TimeProvider
    {
        private int _reading;
        internal Action? OnRead;
        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref _reading, 1) == 0)
            {
                try
                { OnRead?.Invoke(); }
                finally { Volatile.Write(ref _reading, 0); }
            }
            return new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        }
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
