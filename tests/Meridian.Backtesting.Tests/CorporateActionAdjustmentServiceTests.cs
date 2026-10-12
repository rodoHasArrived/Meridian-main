using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Unit tests for <see cref="CorporateActionAdjustmentService"/>.
/// </summary>
public sealed class CorporateActionAdjustmentServiceTests
{
    private readonly CorporateActionAdjustmentService _service;
    private readonly MockSecurityResolver _mockResolver;
    private readonly MockSecurityMasterQueryService _mockQueryService;

    public CorporateActionAdjustmentServiceTests()
    {
        _mockResolver = new MockSecurityResolver();
        _mockQueryService = new MockSecurityMasterQueryService();
        _service = new CorporateActionAdjustmentService(_mockQueryService, _mockResolver, NullLogger<CorporateActionAdjustmentService>.Instance);
    }

    [Fact]
    public async Task AdjustAsync_EmptyBars_ReturnsOriginalBars()
    {
        var result = await _service.AdjustAsync([], "SPY");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AdjustAsync_SecurityNotFound_ReturnsOriginalBars()
    {
        _mockResolver.SetResolveResult(null);
        var bars = new[] { CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m) };

        var result = await _service.AdjustAsync(bars, "SPY");

        result.Should().HaveCount(1);
        result[0].Close.Should().Be(105m);
    }

    [Fact]
    public async Task PrepareAsync_AnnouncedAction_IsNotAuthoritativeForAdjustment()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        var asOf = new DateTimeOffset(2024, 2, 15, 23, 59, 59, TimeSpan.Zero);
        var announced = new CorporateActionDto(
            Guid.NewGuid(), securityId, "StockSplit", new DateOnly(2024, 2, 1), null,
            null, null, 2m, null, null, null, null, null, null,
            LifecycleState: CorporateActionLifecycleStates.Announced);
        _mockQueryService.SetCorporateActions([announced]);
        var bar = CreateBar("SPY", new DateOnly(2024, 1, 2), 100m, 100m, 100m, 100m, 1_000);

        var plan = await _service.PrepareAsync([bar], "SPY", asOf);

        plan.Apply(bar).Should().Be(bar);
    }

    [Fact]
    public async Task PrepareAsync_ActionEffectiveAfterBoundary_DoesNotAdjust()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        var asOf = new DateTimeOffset(2024, 1, 15, 23, 59, 59, TimeSpan.Zero);
        var futureSplit = new CorporateActionDto(
            Guid.NewGuid(), securityId, "StockSplit", new DateOnly(2024, 2, 1), null,
            null, null, 2m, null, null, null, null, null, null,
            LifecycleState: CorporateActionLifecycleStates.Confirmed);
        _mockQueryService.SetCorporateActions([futureSplit]);
        var bar = CreateBar("SPY", new DateOnly(2024, 1, 2), 100m, 100m, 100m, 100m, 1_000);

        var plan = await _service.PrepareAsync([bar], "SPY", asOf);

        plan.Apply(bar).Should().Be(bar);
    }

    [Fact]
    public async Task PrepareAsync_LegacyOnlyImplementationFailsClosedAtEffectiveBoundary()
    {
        ICorporateActionAdjustmentService legacy = new LegacyOnlyAdjustmentService();
        var bar = CreateBar("SPY", new DateOnly(2024, 1, 2), 100m, 100m, 100m, 100m, 1_000);

        Func<Task> act = async () =>
            await legacy.PrepareAsync(
                [bar],
                "SPY",
                new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero));

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*must implement PrepareAsync*effective-through boundary*");
    }

    [Fact]
    public async Task AdjustAsync_NoCorporateActions_ReturnsOriginalBars()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        _mockQueryService.SetCorporateActions([]);

        var bars = new[] { CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m) };

        var result = await _service.AdjustAsync(bars, "SPY");

        result.Should().HaveCount(1);
        result[0].Close.Should().Be(105m);
    }

    [Fact]
    public async Task AdjustAsync_StockSplit_AdjustsPrices()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);

        var split = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "StockSplit",
            ExDate: new DateOnly(2024, 2, 1),
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: 2m,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

        _mockQueryService.SetCorporateActions([split]);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 1000);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Open.Should().Be(50m);      // 100 / 2
        result[0].High.Should().Be(55m);      // 110 / 2
        result[0].Low.Should().Be(45m);       // 90 / 2
        result[0].Close.Should().Be(52.5m);   // 105 / 2
        result[0].Volume.Should().Be(2000);   // 1000 * 2
    }

    [Fact]
    public async Task AdjustAsync_Dividend_AdjustsPrices()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);

        var dividend = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "Dividend",
            ExDate: new DateOnly(2024, 2, 1),
            PayDate: null,
            DividendPerShare: 1m,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

        _mockQueryService.SetCorporateActions([dividend]);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m);

        var result = await _service.AdjustAsync([bar], "SPY");

        var factor = 1m - 1m / 105m;
        result.Should().HaveCount(1);
        result[0].Open.Should().Be(100m * factor);
        result[0].High.Should().Be(110m * factor);
        result[0].Low.Should().Be(90m * factor);
        result[0].Close.Should().Be(105m * factor);
        result[0].Volume.Should().Be(bar.Volume);
    }

    [Fact]
    public async Task AdjustAsync_MultipleSplits_CombinesFactors()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);

        var splits = new[]
        {
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "StockSplit",
                ExDate: new DateOnly(2024, 2, 1),
                PayDate: null,
                DividendPerShare: null,
                Currency: null,
                SplitRatio: 2m,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null),
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "StockSplit",
                ExDate: new DateOnly(2024, 3, 1),
                PayDate: null,
                DividendPerShare: null,
                Currency: null,
                SplitRatio: 3m,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null)
        };

        _mockQueryService.SetCorporateActions(splits);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 600m, 660m, 540m, 630m, 1000);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Open.Should().Be(100m);     // 600 / 6
        result[0].High.Should().Be(110m);     // 660 / 6
        result[0].Low.Should().Be(90m);       // 540 / 6
        result[0].Close.Should().Be(105m);    // 630 / 6
        result[0].Volume.Should().Be(6000);   // 1000 * 6
    }



    [Fact]
    public async Task AdjustAsync_MultipleWindows_RefreshesCorporateActionsPerCall()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        _mockQueryService.SetCorporateActions([
            new CorporateActionDto(Guid.NewGuid(), securityId, "Dividend", new DateOnly(2024, 2, 1), null, 0.5m, "USD", null, null, null, null, null, null, null)
        ]);

        var bars = new[] { CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m) };

        _ = await _service.AdjustAsync(bars, "SPY");
        _ = await _service.AdjustAsync(bars, "SPY");
        _ = await _service.AdjustAsync(bars, "SPY");

        _mockResolver.ResolveCallCount.Should().Be(3);
        _mockQueryService.GetCorporateActionsCallCount.Should().Be(3);
    }

    [Fact]
    public async Task AdjustBarAsync_RefreshesCorporateActionsPerCall()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        _mockQueryService.SetCorporateActions([]);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m);

        _ = await _service.AdjustBarAsync(bar, "SPY");
        _ = await _service.AdjustBarAsync(bar, "SPY");
        _ = await _service.AdjustBarAsync(bar, "SPY");

        _mockResolver.ResolveCallCount.Should().Be(3);
        _mockQueryService.GetCorporateActionsCallCount.Should().Be(3);
    }

    [Fact]
    public async Task AdjustAsync_ActionAppendedAfterFirstTouch_IsVisibleWithoutRestart()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m);
        _mockQueryService.SetCorporateActions([]);
        var first = await _service.AdjustAsync([bar], "SPY");

        _mockQueryService.SetCorporateActions([
            new CorporateActionDto(Guid.NewGuid(), securityId, "StockSplit", new DateOnly(2024, 2, 1), null, null, null, 2m, null, null, null, null, null, null)
        ]);
        var second = await _service.AdjustAsync([bar], "SPY");

        first[0].Close.Should().Be(105m);
        second[0].Close.Should().Be(52.5m);
    }

    [Fact]
    public async Task AdjustAsync_StreamedLargeHistory_IsEquivalentToBatch()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        _mockQueryService.SetCorporateActions([
            new CorporateActionDto(Guid.NewGuid(), securityId, "StockSplit", new DateOnly(2024, 6, 1), null, null, null, 2m, null, null, null, null, null, null),
            new CorporateActionDto(Guid.NewGuid(), securityId, "Dividend", new DateOnly(2024, 9, 1), null, 1m, "USD", null, null, null, null, null, null, null)
        ]);

        var bars = Enumerable.Range(0, 5_000)
            .Select(i => CreateBar("SPY", new DateOnly(2020, 1, 1).AddDays(i), 100m + i, 101m + i, 99m + i, 100.5m + i, 1_000 + i))
            .ToArray();

        var batch = await _service.AdjustAsync(bars, "SPY");
        var streamed = await CollectAsync(_service.AdjustAsync(ToAsync(bars), "SPY"));

        streamed.Should().BeEquivalentTo(batch, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task AdjustAsync_StreamedLargeHistory_IsIncremental()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        _mockQueryService.SetCorporateActions([]);

        var produced = 0;
        async IAsyncEnumerable<HistoricalBar> Source()
        {
            for (var i = 0; i < 100_000; i++)
            {
                produced++;
                yield return CreateBar("SPY", new DateOnly(2020, 1, 1).AddDays(i), 100m, 101m, 99m, 100m);
                await Task.Yield();
            }
        }

        var consumed = 0;
        await foreach (var _ in _service.AdjustAsync(Source(), "SPY"))
        {
            consumed++;
            if (consumed == 250)
            {
                break;
            }
        }

        produced.Should().BeLessThan(500);
    }
    [Fact]
    public async Task AdjustAsync_LegacySplitAlias_AdjustsPricesLikeStockSplit()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);

        // Splits ingested before canonical normalization were stored as the raw provider
        // string "Split"; the alias-tolerant read path must still apply them.
        var legacySplit = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "Split",
            ExDate: new DateOnly(2024, 2, 1),
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: 2m,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

        _mockQueryService.SetCorporateActions([legacySplit]);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 1000);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Close.Should().Be(52.5m);
        result[0].Volume.Should().Be(2000);
    }

    [Fact]
    public async Task AdjustAsync_CancelledSplit_IsNotApplied()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);

        var split = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "StockSplit",
            ExDate: new DateOnly(2024, 2, 1),
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: 2m,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
        var cancellation = split with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = split.CorpActId,
            LifecycleState = CorporateActionLifecycleStates.Cancelled
        };

        _mockQueryService.SetCorporateActions([split, cancellation]);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 1000);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Close.Should().Be(105m, "a cancelled split must not adjust prices");
        result[0].Volume.Should().Be(1000);
    }

    [Fact]
    public async Task AdjustAsync_AmendedSplit_UsesLatestRatioOnly()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);

        var original = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "StockSplit",
            ExDate: new DateOnly(2024, 2, 1),
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: 2m,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
        var amendment = original with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = original.CorpActId,
            SplitRatio = 4m
        };

        _mockQueryService.SetCorporateActions([original, amendment]);

        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 1000);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Close.Should().Be(26.25m, "only the amended 4:1 ratio applies, never both versions");
        result[0].Volume.Should().Be(4000);
    }

    [Fact]
    public async Task PrepareAsync_FullHistoryProducesStableContentVersionAndSingleDividendFactor()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        var exDate = new DateOnly(2024, 2, 1);
        _mockQueryService.SetCorporateActions([
            new CorporateActionDto(Guid.NewGuid(), securityId, "Dividend", exDate, null, 1m, "USD", null, null, null, null, null, null, null),
            new CorporateActionDto(Guid.NewGuid(), securityId, "Dividend", exDate, null, 1m, "USD", null, null, null, null, null, null, null)
        ]);
        var bars = new[]
        {
            CreateBar("SPY", new DateOnly(2024, 1, 30), 90m, 90m, 90m, 90m),
            CreateBar("SPY", new DateOnly(2024, 1, 31), 100m, 100m, 100m, 100m),
            CreateBar("SPY", exDate, 98m, 98m, 98m, 98m)
        };
        var asOf = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var first = await _service.PrepareAsync(bars, "SPY", asOf);
        var second = await _service.PrepareAsync(bars.ToArray(), "spy", asOf);

        first.EffectiveThroughUtc.Should().Be(asOf);
        first.BarCount.Should().Be(3);
        first.ContentVersion.Should().StartWith("sha256:");
        second.ContentVersion.Should().Be(first.ContentVersion);
        first.Apply(bars[0]).Close.Should().Be(88.2m,
            "the combined dividend uses one factor based on the immediate pre-ex close");
        first.Apply(bars[1]).Close.Should().Be(98m);
        first.Apply(bars[2]).Should().Be(bars[2]);
    }

    private static HistoricalBar CreateBar(
        string symbol,
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume = 1000000)
    {
        return new HistoricalBar(symbol, date, open, high, low, close, volume);
    }


    private static async IAsyncEnumerable<HistoricalBar> ToAsync(IEnumerable<HistoricalBar> bars)
    {
        foreach (var bar in bars)
        {
            yield return bar;
            await Task.Yield();
        }
    }

    private static async Task<List<HistoricalBar>> CollectAsync(IAsyncEnumerable<HistoricalBar> source)
    {
        var list = new List<HistoricalBar>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    // Mock implementations
    private sealed class MockSecurityResolver : ISecurityResolver
    {
        private Guid? _result;

        public int ResolveCallCount { get; private set; }

        public void SetResolveResult(Guid? result) => _result = result;

        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
        {
            ResolveCallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class MockSecurityMasterQueryService : ISecurityMasterQueryService
    {
        private IReadOnlyList<CorporateActionDto> _actions = [];

        public int GetCorporateActionsCallCount { get; private set; }

        public void SetCorporateActions(IReadOnlyList<CorporateActionDto> actions) => _actions = actions;

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        {
            GetCorporateActionsCallCount++;
            return Task.FromResult(_actions);
        }

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class LegacyOnlyAdjustmentService : ICorporateActionAdjustmentService
    {
        public Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
            IReadOnlyList<HistoricalBar> bars,
            string ticker,
            CancellationToken ct = default) =>
            Task.FromResult(bars);
    }
}
