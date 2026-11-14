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

public sealed class CanonicalBacktestActionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"canonical-backtest-{Guid.NewGuid():N}");
    private static readonly Guid SecurityId = Guid.Parse("2924f20a-65cc-443d-98b9-9d12a46ccc7a");
    private static readonly DateOnly Start = new(2024, 1, 2);

    [Fact]
    public async Task MissingRawTape_FailsBeforeInitialization_InsteadOfClaimingCanonicalEvidence()
    {
        Directory.CreateDirectory(_root);
        var strategy = new TradingStrategy();
        var run = () => Engine(new Query([])).RunAsync(Request(Start.AddDays(1)), strategy);
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nonempty historical-bar universe*");
        strategy.Initialized.Should().BeFalse();
    }

    [Theory]
    [InlineData(3, 10, 0)]
    [InlineData(2, 0, 10)]
    public async Task Dividend_FrozenEntitlementSurvivesSaleAndPreservesEquity(int lastDayOffset, decimal settledCash, decimal outstanding)
    {
        WriteBars((100m, false), (99m, false), (99m, false), (99m, false));
        var query = new Query([Dividend(payDate: Start.AddDays(3))]);
        var result = await Engine(query).RunAsync(Request(Start.AddDays(lastDayOffset)), new TradingStrategy(sellOnExDate: true));

        result.Snapshots.Should().OnlyContain(snapshot => snapshot.TotalEquity == 10_000m);
        result.Snapshots[^1].Cash.Should().Be(9_990m + settledCash);
        result.Snapshots[^1].DividendReceivable.Should().Be(outstanding);
        result.Ledger.GetBalance(LedgerAccounts.CashAccount(BacktestDefaults.DefaultBrokerageAccountId))
            .Should().Be(result.Snapshots[^1].Cash);
        result.Ledger.GetBalance(LedgerAccounts.DividendReceivable("XYZ", BacktestDefaults.DefaultBrokerageAccountId))
            .Should().Be(outstanding);
        result.Metrics.NetPnl.Should().Be(0m);
        result.Metrics.SymbolAttribution["XYZ"].RealizedPnl.Should().Be(0m);
        result.Ledger.Journal.Should().OnlyContain(journal => journal.IsBalanced);
        result.LedgerIntegrityBreaks.Should().BeEmpty();
        result.Ledger.Journal.Count(journal => journal.Metadata.ActivityType == "canonical_dividend_declaration").Should().Be(1);
        result.Ledger.Journal.Count(journal => journal.Metadata.ActivityType == "canonical_dividend_settlement").Should().Be(settledCash == 0m ? 0 : 1);
        query.ActionReads.Should().Be(1);
        result.CanonicalCorporateActions!.ContentHash.Should().HaveLength(64);
        result.ToResolvedRealismDescriptor().ToCanonicalString().Should().Contain(result.CanonicalCorporateActions.ContentHash);
    }

    [Fact]
    public async Task Split_PreservesBasisAndEquity_AndDuplicateCanonicalRowsApplyOnce()
    {
        WriteBars((100m, false), (50m, false));
        var split = Dividend() with { EventType = CorporateActionEventTypes.StockSplit, SplitRatio = 2m, DividendPerShare = null };
        var query = new Query([split, split]);
        var result = await Engine(query).RunAsync(Request(Start.AddDays(1)), new TradingStrategy());

        result.Snapshots[^1].Positions["XYZ"].Quantity.Should().Be(20);
        result.Snapshots[^1].Positions["XYZ"].AverageCostBasis.Should().Be(50m);
        result.Snapshots.Should().OnlyContain(snapshot => snapshot.TotalEquity == 10_000m);
        result.CanonicalCorporateActions!.Actions.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task AdjustedOrUnknownBasisAnywhereInTape_FailsBeforeStrategyInitialization(bool? adjusted)
    {
        WriteBars((100m, false), (99m, adjusted));
        var strategy = new TradingStrategy();
        var action = () => Engine(new Query([])).RunAsync(Request(Start.AddDays(1)), strategy);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicitly unadjusted*");
        strategy.Initialized.Should().BeFalse();
    }

    [Fact]
    public async Task MissingPayDateAndManualCorporateAction_FailBeforeInitialization()
    {
        WriteBars((100m, false), (99m, false));
        var strategy = new TradingStrategy();
        var missingPayDate = () => Engine(new Query([Dividend() with { PayDate = null }]))
            .RunAsync(Request(Start.AddDays(1)), strategy);
        await missingPayDate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pay date*");
        strategy.Initialized.Should().BeFalse();
        var manual = () => Engine(new Query([])).RunAsync(Request(Start.AddDays(1)) with
        {
            AssetEvents = [new AssetEvent(new DateTimeOffset(Start, TimeOnly.MinValue, TimeSpan.Zero), "XYZ", AssetEventType.Dividend, CashPerShare: 1m)]
        }, strategy);
        await manual.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only manual Fee*");
    }

    [Fact]
    public async Task ConflictingDuplicateIds_FailInsteadOfLastRowWinning()
    {
        WriteBars((100m, false), (99m, false));
        var dividend = Dividend();
        var run = () => Engine(new Query([dividend, dividend with { DividendPerShare = 2m }]))
            .RunAsync(Request(Start.AddDays(1)), new TradingStrategy());
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Conflicting corporate-action ID*");
    }

    [Fact]
    public async Task AgreeingEconomicsWithDistinctIds_RetainEvidenceButBookOnlyOnce()
    {
        WriteBars((100m, false), (99m, false), (99m, false), (99m, false));
        var dividend = Dividend();
        var agreeing = dividend with { CorpActId = Guid.NewGuid() };
        var query = new Query([agreeing, dividend]);
        var result = await Engine(query).RunAsync(Request(Start.AddDays(3)), new TradingStrategy());
        result.CanonicalCorporateActions!.Actions.Should().HaveCount(2, "both source IDs remain inspectable");
        result.Ledger.Journal.Count(item => item.Metadata.ActivityType == "canonical_dividend_declaration").Should().Be(1);
        result.CashFlows.OfType<DividendCashFlow>().Should().ContainSingle().Which.Amount.Should().Be(10m);
        result.Snapshots[^1].TotalEquity.Should().Be(10_000m);
        result.LedgerIntegrityBreaks.Should().BeEmpty();
        var reversed = new Query([dividend, agreeing]);
        var replay = await CanonicalBacktestActions.PrepareAsync(Request(Start.AddDays(3)), new HashSet<string> { "XYZ" }, reversed, reversed, default);
        replay.Snapshot.ContentHash.Should().Be(result.CanonicalCorporateActions.ContentHash);
    }

    [Fact]
    public async Task ConflictingSameDateEconomicsAndMultipleTransformations_FailBeforeReplay()
    {
        var dividend = Dividend();
        var query = new Query([dividend, dividend with { CorpActId = Guid.NewGuid(), DividendPerShare = 2m }]);
        var conflicting = () => CanonicalBacktestActions.PrepareAsync(Request(Start.AddDays(3)), new HashSet<string> { "XYZ" }, query, query, default);
        await conflicting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Conflicting corporate-action economics*");
        var split = dividend with { EventType = CorporateActionEventTypes.StockSplit, SplitRatio = 2m, DividendPerShare = null };
        query = new Query([split, split with { CorpActId = Guid.NewGuid(), EventType = CorporateActionEventTypes.ReverseStockSplit, SplitRatio = 0.5m }]);
        var transformations = () => CanonicalBacktestActions.PrepareAsync(Request(Start.AddDays(3)), new HashSet<string> { "XYZ" }, query, query, default);
        await transformations.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Multiple same-day corporate-action transformations*");
    }

    [Fact]
    public async Task RawMode_BypassesMappedSources_AndRejectsDualExplicitPopulation()
    {
        WriteBars((100m, false), (99m, false));
        var query = new Query([Dividend()]);
        var source = new RefusingMappedSource();
        var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance,
            new StorageCatalogService(_root, new StorageOptions()), securityMasterQueryService: null,
            securityIdentityQueryService: query, assetEventSource: source)
        {
            AssetEventSource = source,
            CanonicalCorporateActionQueries = query
        };
        await engine.RunAsync(Request(Start.AddDays(1)), new TradingStrategy());
        source.Reads.Should().Be(0);
        query.ActionReads.Should().Be(1);
        var dual = () => engine.RunAsync(Request(Start.AddDays(1)) with { PopulateAssetEventsFromSecurityMaster = true }, new TradingStrategy());
        await dual.Should().ThrowAsync<InvalidOperationException>().WithMessage("*select one source accounting mode*");
        source.Reads.Should().Be(0);
    }

    [Fact]
    public async Task ReceivableIntegrity_DetectsAnOrphanAccrualWithoutFabricatingACashBreak()
    {
        WriteBars((100m, false), (99m, false));
        var result = await Engine(new Query([Dividend()])).RunAsync(Request(Start.AddDays(1)), new TradingStrategy());
        var snapshot = result.Snapshots[^1];
        var ledger = (Meridian.Ledger.Ledger)result.Ledger;
        ledger.PostLines(snapshot.Timestamp, "Synthetic erroneous accrual",
            [(LedgerAccounts.DividendReceivable("XYZ", BacktestDefaults.DefaultBrokerageAccountId), 1m, 0m),
             (LedgerAccounts.DividendIncomeFor(BacktestDefaults.DefaultBrokerageAccountId), 0m, 1m)]);
        var difference = BacktestLedgerIntegrityValidator.Validate(ledger, snapshot).Should().ContainSingle().Subject;
        difference.Check.Should().Be("DividendReceivable");
        difference.Expected.Should().Be(10m);
        difference.Actual.Should().Be(11m);
        difference.Symbol.Should().BeNull();
    }

    [Fact]
    public async Task FractionalSplitAndOversell_AreRejected()
    {
        WriteBars((100m, false), (300m, false));
        var split = Dividend() with { EventType = CorporateActionEventTypes.ReverseStockSplit, SplitRatio = 0.3m, DividendPerShare = null };
        var fractional = () => Engine(new Query([split])).RunAsync(Request(Start.AddDays(1)), new TradingStrategy(quantity: 9));
        await fractional.Should().ThrowAsync<NotSupportedException>().WithMessage("*integral long-share*");
        var oversell = () => Engine(new Query([])).RunAsync(Request(Start.AddDays(1)), new TradingStrategy(quantity: -10));
        await oversell.Should().ThrowAsync<NotSupportedException>().WithMessage("*long positions*");
    }

    [Fact]
    public async Task Split_WithPendingOrder_FailsRatherThanFillingOldQuantityAndPrice()
    {
        WriteBars((100m, false), (50m, false));
        var split = Dividend() with { EventType = CorporateActionEventTypes.StockSplit, SplitRatio = 2m, DividendPerShare = null };
        var run = () => Engine(new Query([split])).RunAsync(Request(Start.AddDays(1)), new TradingStrategy(pendingAcrossSplit: true));
        await run.Should().ThrowAsync<NotSupportedException>().WithMessage("*pending orders across a split*");
    }

    [Fact]
    public async Task Dividend_WithStraySplitTerm_DoesNotApplyASplit()
    {
        WriteBars((100m, false), (99m, false));
        var dividend = Dividend() with { SplitRatio = 2m };
        var result = await Engine(new Query([dividend])).RunAsync(Request(Start.AddDays(1)), new TradingStrategy());
        result.Snapshots[^1].Positions["XYZ"].Quantity.Should().Be(10);
        result.Snapshots[^1].DividendReceivable.Should().Be(10m);
        result.Snapshots[^1].TotalEquity.Should().Be(10_000m);
    }

    [Fact]
    public async Task AmendedAndCancelledChains_UseEffectiveTerms_AndHashIsDeterministic()
    {
        var original = Dividend();
        var amended = original with { CorpActId = Guid.NewGuid(), SupersedesCorpActId = original.CorpActId, DividendPerShare = 2m };
        var request = Request(Start.AddDays(3));
        var query = new Query([amended, original]);
        var first = await CanonicalBacktestActions.PrepareAsync(request, new HashSet<string> { "XYZ" }, query, query, default);
        var second = await CanonicalBacktestActions.PrepareAsync(request, new HashSet<string> { "XYZ" }, query, query, default);
        first.Snapshot.ContentHash.Should().Be(second.Snapshot.ContentHash);
        first.Snapshot.Actions.Should().ContainSingle().Which.Action.DividendPerShare.Should().Be(2m);
        var cancelled = amended with { CorpActId = Guid.NewGuid(), SupersedesCorpActId = amended.CorpActId, LifecycleState = CorporateActionLifecycleStates.Cancelled };
        query = new Query([original, amended, cancelled]);
        var empty = await CanonicalBacktestActions.PrepareAsync(request, new HashSet<string> { "XYZ" }, query, query, default);
        empty.Snapshot.Actions.Should().BeEmpty();
        empty.Snapshot.ContentHash.Should().NotBe(first.Snapshot.ContentHash);
    }

    [Fact]
    public void LegacyDescriptor_OmitsNewFields_AndRawModeHasSeparateIdentity()
    {
        var legacy = Request(Start) with { CorporateActionMode = BacktestCorporateActionMode.Legacy };
        legacy.ToRealismDescriptor().ToCanonicalString().Should().NotContain("CorporateActionMode").And.NotContain("CorporateActionSnapshotHash");
        Request(Start).ToRealismDescriptor().ToCanonicalString().Should().Contain("CorporateActionMode=RawPricesWithCanonicalEvents");
        JsonSerializer.Serialize(legacy, BacktestSweepFingerprintJsonContext.Default.BacktestRequest)
            .Should().NotContain("CorporateActionMode", "sweep-v2 fingerprints retain the preexisting JSON contract");
        JsonSerializer.Serialize(Request(Start), BacktestSweepFingerprintJsonContext.Default.BacktestRequest)
            .Should().Contain("\"CorporateActionMode\":1");
        JsonSerializer.Serialize(legacy.ToRealismDescriptor()).Should().NotContain("CorporateActionMode").And.NotContain("CorporateActionSnapshotHash");
    }

    private BacktestEngine Engine(Query query) => new(
        NullLogger<BacktestEngine>.Instance, new StorageCatalogService(_root, new StorageOptions()),
        securityMasterQueryService: null, securityIdentityQueryService: query)
    {
        CanonicalCorporateActionQueries = query
    };

    private BacktestRequest Request(DateOnly to) => new(Start, to, Symbols: ["XYZ"], InitialCash: 10_000m,
        DataRoot: _root, CommissionKind: BacktestCommissionKind.Free, SlippageBasisPoints: 0m,
        FillTiming: FillTiming.SameBar)
    {
        CorporateActionMode = BacktestCorporateActionMode.RawPricesWithCanonicalEvents
    };

    private static CorporateActionDto Dividend(DateOnly? payDate = null) => new(
        Guid.NewGuid(), SecurityId, CorporateActionEventTypes.Dividend, Start.AddDays(1), payDate ?? Start.AddDays(3),
        1m, "USD", null, null, null, null, null, null, null);

    private void WriteBars(params (decimal Price, bool? Adjusted)[] bars)
    {
        var directory = Path.Combine(_root, "XYZ");
        Directory.CreateDirectory(directory);
        using var writer = new StreamWriter(Path.Combine(directory, "XYZ_bars_2024-01-02.jsonl"));
        for (var index = 0; index < bars.Length; index++)
        {
            var bar = new HistoricalBar("XYZ", Start.AddDays(index), bars[index].Price, bars[index].Price,
                bars[index].Price, bars[index].Price, 1_000_000L, "synthetic", index + 1, IsAdjusted: bars[index].Adjusted);
            writer.WriteLine(JsonSerializer.Serialize(MarketEvent.HistoricalBar(bar.ToTimestampUtc(), "XYZ", bar, "synthetic", index + 1),
                MarketDataJsonContext.HighPerformanceOptions));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private sealed class Query(IReadOnlyList<CorporateActionDto> actions) : ISecurityIdentityQueryService, ISecurityCorporateActionsQueryService
    {
        public int ActionReads { get; private set; }
        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue,
            string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null) => Task.FromResult<SecurityDetailDto?>(new(
                SecurityId, "Equity", SecurityStatusDto.Active, "Synthetic equity", "USD", JsonSerializer.SerializeToElement(new { }),
                JsonSerializer.SerializeToElement(new { }), [], [], 1, DateTimeOffset.MinValue, null));

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        {
            ActionReads++;
            return Task.FromResult(actions);
        }
    }

    private sealed class RefusingMappedSource : IBacktestAssetEventSource
    {
        public int Reads { get; private set; }
        public Task<IReadOnlyList<AssetEvent>> GetAssetEventsAsync(IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Reads++;
            throw new InvalidOperationException("The mapped action pipeline must not execute in canonical settlement mode.");
        }
    }

    private sealed class TradingStrategy(bool sellOnExDate = false, long quantity = 10, bool pendingAcrossSplit = false) : IBacktestStrategy
    {
        public string Name => "Synthetic canonical actions";
        public bool Initialized { get; private set; }
        public void Initialize(IBacktestContext ctx) => Initialized = true;
        public void OnBar(HistoricalBar bar, IBacktestContext ctx)
        {
            if (bar.SessionDate == Start)
            {
                ctx.PlaceMarketOrder("XYZ", quantity);
                if (pendingAcrossSplit)
                    ctx.PlaceOrder(new OrderRequest("XYZ", 10, OrderType.Limit, LimitPrice: 1m, TimeInForce: TimeInForce.GoodTilCancelled));
            }
            else if (sellOnExDate && bar.SessionDate == Start.AddDays(1))
                ctx.PlaceMarketOrder("XYZ", -quantity);
        }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }
}
