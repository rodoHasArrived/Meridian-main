using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Backtesting.Tests;

public sealed class SecurityMasterBacktestAssetEventSourceTests
{
    private static readonly DateOnly From = new(2024, 1, 2);
    private static readonly DateOnly Through = new(2024, 1, 5);

    [Fact]
    public async Task LoadAsync_ConsumesOnlyConfirmedEffectiveRevisions_AndDeduplicatesEconomics()
    {
        var query = new Queries();
        var security = query.Add("ABC");
        var original = Action(security, "StockSplit", From.AddDays(1), ratio: 2m);
        query.Actions[security] =
        [
            original,
            original with { CorpActId = Guid.NewGuid(), SplitRatio = 3m, SupersedesCorpActId = original.CorpActId },
            Action(security, "Dividend", From.AddDays(2), cash: 1m),
            Action(security, "Dividend", From.AddDays(2), cash: 1m),
            Action(security, "Dividend", From.AddDays(3), cash: 9m) with { LifecycleState = "Announced" },
            Action(security, "Dividend", From.AddDays(3), cash: 8m) with { LifecycleState = "Cancelled" },
            Action(security, "Dividend", Through.AddDays(1), cash: 7m)
        ];

        var events = await new SecurityMasterBacktestAssetEventSource(query, query, query)
            .LoadAsync(["abc", "ABC"], From, Through);

        events.Should().HaveCount(2);
        events[0].PositionFactor.Should().Be(3m);
        events[1].CashPerShare.Should().Be(1m);
        query.ActionReads.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_ConflictingStoredEconomics_FailsBeforeReplay()
    {
        var query = new Queries();
        var security = query.Add("ABC");
        query.Actions[security] = [Action(security, "Dividend", From, cash: 1m), Action(security, "Dividend", From, cash: 2m)];
        var act = () => new SecurityMasterBacktestAssetEventSource(query, query).LoadAsync(["ABC"], From, Through);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Conflicting*ABC*");
    }

    [Fact]
    public void Merge_ManualDuplicateIsAppliedOnce_AndDifferentAmountIsRejected()
    {
        var dividend = new AssetEvent(new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero), "ABC", AssetEventType.Dividend, 1m);
        SecurityMasterBacktestAssetEventSource.Merge([dividend], [dividend with { Description = "manual" }])
            .Should().ContainSingle();
        var act = () => SecurityMasterBacktestAssetEventSource.Merge([dividend], [dividend with { CashPerShare = 2m }]);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task LoadAsync_RenameAndMerger_FollowSuccessorEconomics()
    {
        var query = new Queries();
        var original = query.Add("OLD");
        var successor = query.Add("NEW");
        query.Actions[original] = [Action(original, "Merger", From.AddDays(1)) with { AcquirerSecurityId = successor, ExchangeRatio = 2m }];
        query.Actions[successor] = [Action(successor, "Dividend", From.AddDays(2), cash: 1m)];

        var events = await new SecurityMasterBacktestAssetEventSource(query, query, query).LoadAsync(["OLD"], From, Through);

        events.Select(item => item.Symbol).Should().Equal("OLD", "NEW");
        events[0].TargetSymbol.Should().Be("NEW");
        query.ActionReads.Should().Be(2);
    }

    [Fact]
    public async Task LoadAsync_SameIdentityRename_ChangesSymbolOfSubsequentActions()
    {
        var query = new Queries();
        var security = query.Add("OLD");
        query.Details[security] = query.Details[security] with
        {
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "NEW", true,
                new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero))]
        };
        query.Actions[security] =
        [
            Action(security, "SymbolChange", From.AddDays(1)),
            Action(security, "Dividend", From.AddDays(2), cash: 1m)
        ];
        var events = await new SecurityMasterBacktestAssetEventSource(query, query, query).LoadAsync(["OLD"], From, Through);
        events.Select(item => item.Symbol).Should().Equal("OLD", "NEW");
    }

    [Fact]
    public async Task LoadAsync_ForeignCurrencyDistribution_RefusesImplicitFx()
    {
        var query = new Queries();
        var security = query.Add("ABC");
        query.Actions[security] = [Action(security, "Dividend", From, cash: 1m) with { Currency = "EUR" }];
        var act = () => new SecurityMasterBacktestAssetEventSource(query, query).LoadAsync(["ABC"], From, Through);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*USD distribution*");
    }

    [Fact]
    public async Task Engine_AutomaticallyAppliesStoredSplitAndDividend_WithoutAdjustingPricesAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-auto-actions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var query = new Queries();
            var security = query.Add("ABC");
            query.Actions[security] =
            [
                Action(security, "StockSplit", From.AddDays(1), ratio: 2m),
                Action(security, "Dividend", From.AddDays(2), cash: 1m)
            ];
            var events = Enumerable.Range(0, 4).Select(index =>
            {
                var date = From.AddDays(index);
                var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero);
                var price = index == 0 ? 100m : 50m;
                var bar = new HistoricalBar("ABC", date, price, price, price, price, 10_000, "synthetic", IsAdjusted: false);
                return JsonSerializer.Serialize(MarketEvent.HistoricalBar(at, "ABC", bar, "synthetic"), MarketDataJsonContext.HighPerformanceOptions);
            });
            Directory.CreateDirectory(Path.Combine(root, "ABC"));
            await File.WriteAllLinesAsync(Path.Combine(root, "ABC", "ABC_bars_2024-01-02.jsonl"), events);
            var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance,
                new StorageCatalogService(root, new StorageOptions()))
            {
                AssetEventSource = new SecurityMasterBacktestAssetEventSource(query, query)
            };
            var strategy = new BuyAndHold();
            var result = await engine.RunAsync(new BacktestRequest(From, Through,
                Symbols: ["ABC"], DataRoot: root, FillTiming: FillTiming.SameBar,
                SlippageBasisPoints: 0m, CommissionKind: BacktestCommissionKind.Free), strategy);

            strategy.Prices.Should().Equal(100m, 50m, 50m, 50m);
            result.Request.AssetEvents.Should().HaveCount(2);
            var last = result.Snapshots[^1];
            last.Positions["ABC"].Quantity.Should().Be(20);
            last.Cash.Should().Be(99_020m);
            result.Metrics.FinalEquity.Should().Be(100_020m);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StoredActionOption_IsPartOfNewRunIdentity_WithoutChangingOldDescriptorFingerprints()
    {
        var request = new BacktestRequest(From, Through);
        request.ToRealismDescriptor().ToCanonicalString().Should().NotBe(
            (request with { ApplyStoredCorporateActions = false }).ToRealismDescriptor().ToCanonicalString());
        (request.ToRealismDescriptor() with { ApplyStoredCorporateActions = null }).ToCanonicalString()
            .Should().NotContain("ApplyStoredCorporateActions=");
    }

    [Fact]
    public async Task ExplicitPopulation_RejectsPriceAdjustmentBeforeReadingEitherSource()
    {
        await WithRawReplayAsync(async root =>
        {
            var source = new CanonicalSource();
            var engine = Engine(root, source);
            var request = Request(root) with { PopulateAssetEventsFromSecurityMaster = true, AdjustForCorporateActions = true };

            Func<Task> run = () => engine.RunAsync(request, new BuyAndHold());

            await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires AdjustForCorporateActions = false*");
            source.Reads.Should().Be(0);
        });
    }

    [Fact]
    public async Task ExplicitPopulation_ManualEventsWinOverBothConfiguredSources()
    {
        await WithRawReplayAsync(async root =>
        {
            var source = new CanonicalSource();
            var engine = Engine(root, source);
            var request = Request(root) with
            {
                PopulateAssetEventsFromSecurityMaster = true,
                AdjustForCorporateActions = false,
                AssetEvents = [Distribution(1m)]
            };

            var result = await engine.RunAsync(request, new BuyAndHold());

            source.Reads.Should().Be(0);
            result.Snapshots[^1].Cash.Should().Be(99_010m);
            request.ToRealismDescriptor().PopulateAssetEventsFromSecurityMaster.Should().BeFalse();
        });
    }

    [Fact]
    public async Task ExplicitPopulation_ReadsCanonicalSourceOnceAndPreservesItsInputHashSetting()
    {
        await WithRawReplayAsync(async root =>
        {
            var source = new CanonicalSource();
            var request = Request(root) with { PopulateAssetEventsFromSecurityMaster = true, AdjustForCorporateActions = false };

            var result = await Engine(root, source).RunAsync(request, new BuyAndHold());

            source.Reads.Should().Be(1);
            result.Snapshots[^1].Cash.Should().Be(99_020m);
            result.Request.ToRealismDescriptor().PopulateAssetEventsFromSecurityMaster.Should().BeTrue();
        });
    }

    [Fact]
    public async Task AutomaticPopulation_AcceptsAnOriginalLoadOnlySourceWithoutDuplicatingTheInterface()
    {
        await WithRawReplayAsync(async root =>
        {
            var source = new LegacySource();
            var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance,
                new StorageCatalogService(root, new StorageOptions()))
            { AssetEventSource = source };

            var result = await engine.RunAsync(Request(root), new BuyAndHold());

            source.Reads.Should().Be(1);
            result.Snapshots[^1].Cash.Should().Be(99_020m);
            result.Request.AssetEvents.Should().HaveCount(1);
        });
    }

    [Fact]
    public async Task DisabledPopulation_DoesNotReadTheCompatibilityConstructorSource()
    {
        await WithRawReplayAsync(async root =>
        {
            var source = new CanonicalSource();
            var request = Request(root) with { ApplyStoredCorporateActions = false, AdjustForCorporateActions = false };

            var result = await Engine(root, source).RunAsync(request, new BuyAndHold());

            source.Reads.Should().Be(0);
            result.Snapshots[^1].Cash.Should().Be(99_000m);
        });
    }

    [Fact]
    public async Task ExplicitPopulation_AnEmptySuppliedListPreventsAutomaticFallback()
    {
        await WithRawReplayAsync(async root =>
        {
            var source = new CanonicalSource();
            var request = Request(root) with
            {
                PopulateAssetEventsFromSecurityMaster = true,
                AdjustForCorporateActions = false,
                AssetEvents = []
            };

            var result = await Engine(root, source).RunAsync(request, new BuyAndHold());

            source.Reads.Should().Be(0);
            result.Snapshots[^1].Cash.Should().Be(99_000m);
        });
    }

    [Fact]
    public async Task ExplicitPopulation_WithoutAConfiguredSourceFailsBeforeReplay()
    {
        await WithRawReplayAsync(async root =>
        {
            var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance,
                new StorageCatalogService(root, new StorageOptions()));
            var request = Request(root) with { PopulateAssetEventsFromSecurityMaster = true, AdjustForCorporateActions = false };

            Func<Task> run = () => engine.RunAsync(request, new BuyAndHold());

            await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no Security Master asset-event source*");
        });
    }

    private static BacktestEngine Engine(string root, IBacktestAssetEventSource source) =>
        new(NullLogger<BacktestEngine>.Instance, new StorageCatalogService(root, new StorageOptions()),
            (ISecurityTradingParametersQueryService?)null, assetEventSource: source)
        { AssetEventSource = source };

    private static BacktestRequest Request(string root) => new(From, Through, Symbols: ["ABC"], DataRoot: root,
        FillTiming: FillTiming.SameBar, SlippageBasisPoints: 0m, CommissionKind: BacktestCommissionKind.Free);

    private static AssetEvent Distribution(decimal cash) => new(
        new DateTimeOffset(From.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        "ABC", AssetEventType.Dividend, CashPerShare: cash);

    private static async Task WithRawReplayAsync(Func<string, Task> exercise)
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-population-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "ABC"));
        try
        {
            var events = Enumerable.Range(0, 4).Select(index =>
            {
                var date = From.AddDays(index);
                var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero);
                var bar = new HistoricalBar("ABC", date, 100m, 100m, 100m, 100m, 10_000, "synthetic", IsAdjusted: false);
                return JsonSerializer.Serialize(MarketEvent.HistoricalBar(at, "ABC", bar, "synthetic"), MarketDataJsonContext.HighPerformanceOptions);
            });
            await File.WriteAllLinesAsync(Path.Combine(root, "ABC", "ABC_bars_2024-01-02.jsonl"), events);
            await exercise(root);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CanonicalSource : IBacktestAssetEventSource
    {
        public int Reads { get; private set; }
        public Task<IReadOnlyList<AssetEvent>> GetAssetEventsAsync(
            IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<AssetEvent>>([Distribution(2m)]);
        }
    }

    private sealed class LegacySource : IBacktestAssetEventSource
    {
        public int Reads { get; private set; }
        public Task<IReadOnlyList<AssetEvent>> LoadAsync(
            IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<AssetEvent>>([Distribution(2m)]);
        }
    }

    private static CorporateActionDto Action(Guid security, string type, DateOnly date, decimal? cash = null, decimal? ratio = null) =>
        new(Guid.NewGuid(), security, type, date, null, cash, "USD", ratio, null, null, null, null, null, null,
            LifecycleState: CorporateActionLifecycleStates.Confirmed);

    private sealed class Queries : ISecurityMasterQueryService
    {
        public Dictionary<Guid, SecurityDetailDto> Details { get; } = [];
        public Dictionary<string, Guid> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<Guid, IReadOnlyList<CorporateActionDto>> Actions { get; } = [];
        public int ActionReads { get; private set; }
        public Guid Add(string symbol)
        {
            var id = Guid.NewGuid();
            Symbols[symbol] = id;
            Details[id] = new SecurityDetailDto(id, "Equity", SecurityStatusDto.Active, symbol, "USD",
                default, default, [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, symbol, true, DateTimeOffset.MinValue)],
                [], 1, DateTimeOffset.MinValue, null);
            return id;
        }
        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider,
            CancellationToken ct = default, DateTimeOffset? asOfUtc = null) =>
            Task.FromResult(Symbols.TryGetValue(value, out var id) ? Details[id] : null);
        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        {
            ActionReads++;
            return Task.FromResult(Actions.GetValueOrDefault(securityId) ?? []);
        }
        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) => Task.FromResult(Details.GetValueOrDefault(id));
        public Task<SecurityDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Details.GetValueOrDefault(id));
        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);
        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf,
            CancellationToken ct = default) => Task.FromResult<TradingParametersDto?>(null);
    }

    private sealed class BuyAndHold : IBacktestStrategy
    {
        public string Name => "Synthetic buy and hold";
        public List<decimal> Prices { get; } = [];
        public void Initialize(IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx)
        {
            Prices.Add(bar.Close);
            if (Prices.Count == 1)
                ctx.PlaceMarketOrder(bar.Symbol, 10);
        }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot book, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly day, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }
}
