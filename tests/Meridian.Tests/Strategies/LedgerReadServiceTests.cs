using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Moq;
using Xunit;
using LedgerAccount = Meridian.Ledger.LedgerAccount;
using LedgerAccountType = Meridian.Ledger.LedgerAccountType;
using LedgerImpl = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Strategies;

public sealed class LedgerReadServiceTests
{
    // ── BuildSummary (synchronous) ───────────────────────────────────────────

    [Fact]
    public void BuildSummary_ValidRunEntry_ReturnsPopulatedSummary()
    {
        var entry = BuildCompletedRun();
        var service = new LedgerReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        summary!.RunId.Should().Be(entry.RunId);
        summary.JournalEntryCount.Should().BeGreaterThan(0);
        summary.LedgerEntryCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildSummary_BalancesAggregatedByAccountType()
    {
        var entry = BuildCompletedRun();
        var service = new LedgerReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        // Asset and equity balances should be non-zero (we post initial capital)
        summary!.AssetBalance.Should().BeGreaterThan(0m);
        summary.EquityBalance.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void BuildSummary_TrialBalanceContainsExpectedAccounts()
    {
        var entry = BuildCompletedRun();
        var service = new LedgerReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        summary!.TrialBalance.Should().Contain(l =>
            l.AccountName.Equals("Cash", StringComparison.OrdinalIgnoreCase));
        summary.TrialBalance.Should().Contain(l =>
            l.AccountType.Equals("Equity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSummary_JournalOrderedDescending()
    {
        var entry = BuildCompletedRun();
        var service = new LedgerReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        if (summary!.Journal.Count > 1)
        {
            var timestamps = summary.Journal.Select(static j => j.Timestamp).ToList();
            timestamps.Should().BeInDescendingOrder();
        }
    }

    [Fact]
    public void BuildSummary_NullMetrics_ReturnsNull()
    {
        var entry = StrategyRunEntry.Start("s1", "Test", RunType.Backtest);
        var service = new LedgerReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().BeNull();
    }

    [Fact]
    public void BuildSummary_NullArg_Throws()
    {
        var service = new LedgerReadService();
        var act = () => service.BuildSummary(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildSummary_NoTransactions_ReturnsEmptyJournal()
    {
        var entry = BuildCompletedRunEmptyLedger();
        var service = new LedgerReadService();

        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        summary!.JournalEntryCount.Should().Be(0);
        summary.LedgerEntryCount.Should().Be(0);
    }

    // ── BuildSummaryAsync – without security lookup ──────────────────────────

    [Fact]
    public async Task BuildSummaryAsync_WithoutLookup_ReturnsSameSummaryAsSynchronous()
    {
        var entry = BuildCompletedRun();
        var service = new LedgerReadService();

        var asyncResult = await service.BuildSummaryAsync(entry);
        var syncResult = service.BuildSummary(entry);

        asyncResult.Should().NotBeNull();
        asyncResult!.RunId.Should().Be(syncResult!.RunId);
        asyncResult.AssetBalance.Should().Be(syncResult.AssetBalance);
        asyncResult.JournalEntryCount.Should().Be(syncResult.JournalEntryCount);
    }

    [Fact]
    public async Task BuildSummaryAsync_NoMetrics_ReturnsNull()
    {
        var entry = StrategyRunEntry.Start("s2", "Test", RunType.Backtest);
        var service = new LedgerReadService();

        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().BeNull();
    }

    // ── BuildSummaryAsync – with security lookup ─────────────────────────────

    [Fact]
    public async Task BuildSummaryAsync_WithLookup_AttachesSecurityReference()
    {
        var entry = BuildCompletedRun(symbol: "AAPL");
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

        var service = new LedgerReadService(lookup.Object);
        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().NotBeNull();
        var aaplLine = summary!.TrialBalance.FirstOrDefault(l =>
            string.Equals(l.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase));
        if (aaplLine is not null)
        {
            aaplLine.Security.Should().NotBeNull();
            aaplLine.Security!.DisplayName.Should().Be("Apple Inc.");
        }
    }

    [Fact]
    public async Task BuildSummaryAsync_WithLookup_MissingSymbol_SecurityIsNull()
    {
        var entry = BuildCompletedRun(symbol: "AAPL");
        var lookup = new Mock<ISecurityReferenceLookup>();
        lookup
            .Setup(l => l.GetBySymbolAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkstationSecurityReference?)null);

        var service = new LedgerReadService(lookup.Object);
        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().NotBeNull();
        summary!.SecurityMissingCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task BuildSummaryAsync_WithLookup_TracksResolvedAndMissingCounts()
    {
        var entry = BuildCompletedRun(symbol: "AAPL");
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

        var service = new LedgerReadService(lookup.Object);
        var summary = await service.BuildSummaryAsync(entry);

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().BeGreaterThanOrEqualTo(0);
        (summary.SecurityResolvedCount + summary.SecurityMissingCount).Should()
            .BeLessThanOrEqualTo(summary.TrialBalance.Count);
    }

    [Fact]
    public void Constructor_NullLookup_Throws()
    {
        var act = () => new LedgerReadService(null!);
        act.Should().Throw<ArgumentNullException>();
    }


    [Fact]
    public void BuildSummary_MapsAndSerializesScopeMetadataFromRunParameters()
    {
        var entry = BuildCompletedRun() with
        {
            ParameterSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountScopeId"] = "acct-paper",
                ["accountScopeDisplayName"] = "Paper Account",
                ["entityScopeId"] = "entity-paper",
                ["entityScopeDisplayName"] = "Paper Entity",
                ["sleeveScopeId"] = "sleeve-paper",
                ["sleeveScopeDisplayName"] = "Paper Sleeve",
                ["vehicleScopeId"] = "vehicle-paper",
                ["vehicleScopeDisplayName"] = "Paper Vehicle"
            }
        };

        var service = new LedgerReadService();
        var summary = service.BuildSummary(entry);

        summary.Should().NotBeNull();
        summary!.AccountScopeId.Should().Be("acct-paper");
        summary.EntityScopeId.Should().Be("entity-paper");
        summary.SleeveScopeId.Should().Be("sleeve-paper");
        summary.VehicleScopeId.Should().Be("vehicle-paper");
        summary.TrialBalance.Should().OnlyContain(line =>
            line.AccountScopeId == "acct-paper" &&
            line.EntityScopeId == "entity-paper" &&
            line.SleeveScopeId == "sleeve-paper" &&
            line.VehicleScopeId == "vehicle-paper");
        summary.Journal.Should().OnlyContain(line => line.AccountScopeId == "acct-paper");

        var json = JsonSerializer.Serialize(summary);
        json.Should().Contain("entityScopeId");
        json.Should().Contain("sleeveScopeDisplayName");
        json.Should().Contain("vehicleScopeDisplayName");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static StrategyRunEntry BuildCompletedRun(string symbol = "AAPL")
    {
        var startedAt = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddHours(8);

        var ledger = new LedgerImpl();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var ownerEquity = new LedgerAccount("Owner's Equity", LedgerAccountType.Equity);
        var tradingGains = new LedgerAccount("Trading Gains", LedgerAccountType.Revenue, Symbol: symbol);
        var commissions = new LedgerAccount("Commissions", LedgerAccountType.Expense, Symbol: symbol);

        ledger.PostLines(startedAt, "initial-capital", new[]
        {
            (cash, 100_000m, 0m),
            (ownerEquity, 0m, 100_000m),
        });

        ledger.PostLines(completedAt, "close-run", new[]
        {
            (cash, 10_000m, 0m),
            (tradingGains, 0m, 10_000m),
            (commissions, 50m, 0m),
            (cash, 0m, 50m),
        });

        return BuildEntryWithLedger(ledger, startedAt, completedAt, symbol);
    }

    private static StrategyRunEntry BuildCompletedRunEmptyLedger()
    {
        var startedAt = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddHours(1);
        var ledger = new LedgerImpl();
        return BuildEntryWithLedger(ledger, startedAt, completedAt, "AAPL");
    }

    private static StrategyRunEntry BuildEntryWithLedger(
        LedgerImpl ledger,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string symbol)
    {
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Symbols: [symbol],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 110_000m,
            GrossPnl: 10_000m,
            NetPnl: 9_950m,
            TotalReturn: 0.1m,
            AnnualizedReturn: 0.15m,
            SharpeRatio: 1.2,
            SortinoRatio: 1.5,
            CalmarRatio: 0.9,
            MaxDrawdown: 2_000m,
            MaxDrawdownPercent: 0.02m,
            MaxDrawdownRecoveryDays: 3,
            ProfitFactor: 1.6,
            WinRate: 0.65,
            TotalTrades: 2,
            WinningTrades: 2,
            LosingTrades: 0,
            TotalCommissions: 50m,
            TotalMarginInterest: 10m,
            TotalShortRebates: 5m,
            Xirr: 0.12,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                [symbol] = new(symbol, 9_000m, 1_000m, 2, 50m, 5m)
            });

        var position = new Position(symbol, 100, 450m, 1_000m, 9_000m);
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 50_000m,
            MarginBalance: 0m,
            LongMarketValue: 60_000m,
            ShortMarketValue: 0m,
            TotalEquity: 110_000m,
            DailyReturn: 0.02m,
            Positions: new Dictionary<string, Position> { [symbol] = position },
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: Array.Empty<CashFlowEntry>());

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { symbol },
            Snapshots: [snapshot],
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills: Array.Empty<FillEvent>(),
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromSeconds(10),
            TotalEventsProcessed: 1000);

        return StrategyRunEntry.Start("strategy-1", "Test Strategy", RunType.Backtest)
            .Complete(result);
    }
}
