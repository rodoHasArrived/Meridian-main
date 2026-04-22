using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Moq;
using Xunit;

using LedgerImpl = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Strategies;

public sealed class PortfolioReadServiceTests
{
    // ── BuildSummary (synchronous) ───────────────────────────────────────────

    [Fact]
    public void BuildSummary_WithValidRunEntry_ReturnsPopulatedSummary()
    {
        var entry = BuildCompletedRun(
            finalEquity: 120_000m,
            realizedPnl: 15_000m,
            unrealizedPnl: 5_000m);

        var service = new PortfolioReadService();
        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        summary!.RunId.Should().Be(entry.RunId);
        summary.TotalEquity.Should().Be(120_000m);
        summary.Cash.Should().BeGreaterThan(0m);
        summary.Positions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildSummary_PositionsOrderedBySymbol()
    {
        var entry = BuildCompletedRunMultiPosition(
            symbols: ["MSFT", "AAPL", "GOOG"],
            finalEquity: 150_000m);

        var service = new PortfolioReadService();
        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        var symbols = summary!.Positions.Select(static p => p.Symbol).ToList();
        symbols.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_PnlAggregatedAcrossPositions()
    {
        var entry = BuildCompletedRun(
            finalEquity: 110_000m,
            realizedPnl: 6_000m,
            unrealizedPnl: 4_000m);

        var service = new PortfolioReadService();
        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        summary!.RealizedPnl.Should().Be(6_000m);
        summary.UnrealizedPnl.Should().Be(4_000m);
    }

    [Fact]
    public void BuildSummary_ExposureComputedFromMarketValues()
    {
        var entry = BuildCompletedRun(
            finalEquity: 110_000m,
            realizedPnl: 5_000m,
            unrealizedPnl: 5_000m);

        var service = new PortfolioReadService();
        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        // GrossExposure = long + |short|
        summary!.GrossExposure.Should().BeGreaterThanOrEqualTo(0m);
        // NetExposure = long + short (signed)
        summary.NetExposure.Should().Be(summary.LongMarketValue + summary.ShortMarketValue);
    }

    [Fact]
    public void BuildSummary_FinancingIsMarginInterestMinusShortRebates()
    {
        var entry = BuildCompletedRun(
            finalEquity: 105_000m,
            realizedPnl: 4_000m,
            unrealizedPnl: 1_000m);

        var service = new PortfolioReadService();
        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        // Financing = TotalMarginInterest - TotalShortRebates (from BacktestMetrics)
        // The helper builds with TotalMarginInterest=50m, TotalShortRebates=15m → net 35m
        summary!.Financing.Should().Be(35m);
    }

    [Fact]
    public void BuildSummary_NoSnapshots_ReturnsNull()
    {
        var entry = BuildRunWithNoSnapshots();
        var service = new PortfolioReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().BeNull();
    }

    [Fact]
    public void BuildSummary_NullMetrics_ReturnsNull()
    {
        var entry = StrategyRunEntry.Start("s1", "Test", RunType.Backtest);
        var service = new PortfolioReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().BeNull();
    }

    // ── BuildSummaryAsync – without security lookup ──────────────────────────

    [Fact]
    public async Task BuildSummaryAsync_WithoutLookup_ReturnsSameSummaryAsSynchronous()
    {
        var entry = BuildCompletedRun(
            finalEquity: 130_000m,
            realizedPnl: 20_000m,
            unrealizedPnl: 10_000m);

        var service = new PortfolioReadService();

        var async = await service.BuildSummaryAsync(entry);
        var sync = service.BuildSummary(entry);

        async.Should().NotBeNull();
        async!.TotalEquity.Should().Be(sync!.TotalEquity);
        async.RealizedPnl.Should().Be(sync.RealizedPnl);
        async.Positions.Count.Should().Be(sync.Positions.Count);
    }

    [Fact]
    public async Task BuildSummaryAsync_NoSnapshots_ReturnsNull()
    {
        var entry = BuildRunWithNoSnapshots();
        var service = new PortfolioReadService();

        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().BeNull();
    }

    // ── BuildSummaryAsync – with security lookup ─────────────────────────────

    [Fact]
    public async Task BuildSummaryAsync_WithLookup_AttachesSecurityReference()
    {
        var entry = BuildCompletedRun(
            finalEquity: 125_000m,
            realizedPnl: 12_000m,
            unrealizedPnl: 8_000m);

        var reference = new WorkstationSecurityReference(
            SecurityId: Guid.NewGuid(),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL");

        var lookup = new Mock<ISecurityReferenceLookup>();
        lookup
            .Setup(l => l.GetBySymbolAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reference);

        var service = new PortfolioReadService(lookup.Object);
        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().NotBeNull();
        var aaplPosition = summary!.Positions.FirstOrDefault(static p => p.Symbol == "AAPL");
        aaplPosition.Should().NotBeNull();
        aaplPosition!.Security.Should().NotBeNull();
        aaplPosition.Security!.DisplayName.Should().Be("Apple Inc.");
    }

    [Fact]
    public async Task BuildSummaryAsync_WithLookup_MissingSymbol_SecurityIsNull()
    {
        var entry = BuildCompletedRun(
            finalEquity: 115_000m,
            realizedPnl: 10_000m,
            unrealizedPnl: 5_000m);

        var lookup = new Mock<ISecurityReferenceLookup>();
        lookup
            .Setup(l => l.GetBySymbolAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkstationSecurityReference?)null);

        var service = new PortfolioReadService(lookup.Object);
        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().NotBeNull();
        summary!.SecurityMissingCount.Should().BeGreaterThan(0);
        foreach (var position in summary.Positions)
            position.Security.Should().BeNull();
    }

    [Fact]
    public async Task BuildSummaryAsync_WithLookup_TracksMissingAndResolvedCounts()
    {
        var entry = BuildCompletedRunMultiPosition(
            symbols: ["AAPL", "MSFT"],
            finalEquity: 200_000m);

        var reference = new WorkstationSecurityReference(
            SecurityId: Guid.NewGuid(),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL");

        var lookup = new Mock<ISecurityReferenceLookup>();
        lookup
            .Setup(l => l.GetBySymbolAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reference);
        lookup
            .Setup(l => l.GetBySymbolAsync("MSFT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkstationSecurityReference?)null);

        var service = new PortfolioReadService(lookup.Object);
        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(1);
        summary.SecurityMissingCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_NullLookup_Throws()
    {
        var act = () => new PortfolioReadService(null!);
        act.Should().Throw<ArgumentNullException>();
    }


    [Fact]
    public void BuildSummary_MapsAndSerializesScopeMetadataFromRunParameters()
    {
        var entry = BuildCompletedRun(
            finalEquity: 120_000m,
            realizedPnl: 15_000m,
            unrealizedPnl: 5_000m) with
        {
            ParameterSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountScopeId"] = "acct-backtest",
                ["accountScopeDisplayName"] = "Backtest Prime",
                ["entityScopeId"] = "entity-alpha",
                ["entityScopeDisplayName"] = "Alpha Management",
                ["sleeveScopeId"] = "sleeve-growth",
                ["sleeveScopeDisplayName"] = "Growth Sleeve",
                ["vehicleScopeId"] = "vehicle-lp",
                ["vehicleScopeDisplayName"] = "Alpha LP"
            }
        };

        var service = new PortfolioReadService();
        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        summary!.AccountScopeId.Should().Be("acct-backtest");
        summary.AccountScopeDisplayName.Should().Be("Backtest Prime");
        summary.EntityScopeId.Should().Be("entity-alpha");
        summary.SleeveScopeId.Should().Be("sleeve-growth");
        summary.VehicleScopeId.Should().Be("vehicle-lp");
        summary.Positions.Should().OnlyContain(position =>
            position.AccountScopeId == "acct-backtest" &&
            position.EntityScopeId == "entity-alpha" &&
            position.SleeveScopeId == "sleeve-growth" &&
            position.VehicleScopeId == "vehicle-lp");

        var json = JsonSerializer.Serialize(summary);
        json.Should().Contain("accountScopeId");
        json.Should().Contain("entityScopeDisplayName");
        json.Should().Contain("vehicleScopeDisplayName");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static StrategyRunEntry BuildCompletedRun(
        decimal finalEquity,
        decimal realizedPnl,
        decimal unrealizedPnl)
    {
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 100, 450m, unrealizedPnl, realizedPnl)
        };
        return BuildEntryWithPositions(positions, finalEquity);
    }

    private static StrategyRunEntry BuildCompletedRunMultiPosition(
        IReadOnlyList<string> symbols,
        decimal finalEquity)
    {
        var positions = symbols
            .Select(static s => new Position(s, 50, 100m, 500m, 200m))
            .ToDictionary(static p => p.Symbol, static p => p, StringComparer.OrdinalIgnoreCase);
        return BuildEntryWithPositions(positions, finalEquity);
    }

    private static StrategyRunEntry BuildEntryWithPositions(
        Dictionary<string, Position> positions,
        decimal finalEquity)
    {
        var startedAt = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddHours(8);

        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 40_000m,
            MarginBalance: 0m,
            LongMarketValue: finalEquity - 40_000m,
            ShortMarketValue: 0m,
            TotalEquity: finalEquity,
            DailyReturn: 0.02m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: Array.Empty<CashFlowEntry>());

        var totalRealizedPnl = positions.Values.Sum(static p => p.RealizedPnl);
        var totalUnrealizedPnl = positions.Values.Sum(static p => p.UnrealizedPnl);

        var attribution = positions.ToDictionary(
            static kv => kv.Key,
            static kv => new SymbolAttribution(kv.Key, kv.Value.RealizedPnl, kv.Value.UnrealizedPnl, 1, 10m, 5m));

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: finalEquity,
            GrossPnl: totalRealizedPnl + totalUnrealizedPnl,
            NetPnl: totalRealizedPnl + totalUnrealizedPnl - 10m,
            TotalReturn: (finalEquity - 100_000m) / 100_000m,
            AnnualizedReturn: 0.12m,
            SharpeRatio: 1.2,
            SortinoRatio: 1.5,
            CalmarRatio: 0.8,
            MaxDrawdown: 3_000m,
            MaxDrawdownPercent: 0.03m,
            MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 1.6,
            WinRate: 0.6,
            TotalTrades: positions.Count,
            WinningTrades: positions.Count,
            LosingTrades: 0,
            TotalCommissions: 10m,
            TotalMarginInterest: 50m,
            TotalShortRebates: 15m,
            Xirr: 0.15,
            SymbolAttribution: attribution);

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Symbols: [.. positions.Keys],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var ledger = new LedgerImpl();

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(positions.Keys, StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills: Array.Empty<FillEvent>(),
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 500);

        return StrategyRunEntry.Start("strategy-1", "Test Strategy", RunType.Backtest)
            .Complete(result);
    }

    private static StrategyRunEntry BuildRunWithNoSnapshots()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2026, 1, 1),
            To: new DateOnly(2026, 1, 2),
            Symbols: ["AAPL"],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 100_000m,
            GrossPnl: 0m, NetPnl: 0m, TotalReturn: 0m, AnnualizedReturn: 0m,
            SharpeRatio: 0, SortinoRatio: 0, CalmarRatio: 0,
            MaxDrawdown: 0m, MaxDrawdownPercent: 0m, MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 0, WinRate: 0,
            TotalTrades: 0, WinningTrades: 0, LosingTrades: 0,
            TotalCommissions: 0m, TotalMarginInterest: 0m, TotalShortRebates: 0m,
            Xirr: 0,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Snapshots: Array.Empty<PortfolioSnapshot>(),
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills: Array.Empty<FillEvent>(),
            Metrics: metrics,
            Ledger: new LedgerImpl(),
            ElapsedTime: TimeSpan.Zero,
            TotalEventsProcessed: 0);

        return StrategyRunEntry.Start("strategy-empty", "Empty Strategy", RunType.Backtest)
            .Complete(result);
    }
}
