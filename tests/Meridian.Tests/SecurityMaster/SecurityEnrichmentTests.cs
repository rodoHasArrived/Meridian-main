using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Tests for Security Master enrichment wired into <see cref="PortfolioReadService"/> and
/// <see cref="LedgerReadService"/>, covering fully-resolved, partially-unresolved, and
/// fully-unresolved symbol cases.
/// </summary>
public sealed class SecurityEnrichmentTests
{
    // -----------------------------------------------------------------------
    // PortfolioReadService — resolved
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PortfolioReadService_BuildSummaryAsync_PopulatesSecurityReference_WhenSymbolResolved()
    {
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", MakeReference("AAPL", "Equity"));
        var service = new PortfolioReadService(lookup);

        var summary = await service.BuildSummaryAsync(BuildRunWithPosition("AAPL"));

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(1);
        summary.SecurityMissingCount.Should().Be(0);
        summary.Positions.Should().ContainSingle();
        summary.Positions[0].Security.Should().NotBeNull();
        summary.Positions[0].Security!.DisplayName.Should().Be("Apple Inc.");
        summary.Positions[0].Security.AssetClass.Should().Be("Equity");
    }

    [Fact]
    public async Task PortfolioReadService_BuildSummaryAsync_SetsSubType_WhenPresent()
    {
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("SPY", MakeReference("SPY", "Equity", subType: null));
        lookup.Register("TLT", MakeReference("TLT", "Bond", subType: "Bond"));
        var service = new PortfolioReadService(lookup);

        var summary = await service.BuildSummaryAsync(BuildRunWithTwoPositions("SPY", "TLT"));

        summary.Should().NotBeNull();
        var spy = summary!.Positions.Single(static p => p.Symbol == "SPY");
        var tlt = summary.Positions.Single(static p => p.Symbol == "TLT");

        spy.Security!.SubType.Should().BeNull();
        tlt.Security!.SubType.Should().Be("Bond");
    }

    // -----------------------------------------------------------------------
    // PortfolioReadService — unresolved
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PortfolioReadService_BuildSummaryAsync_ReturnsNullSecurity_WhenSymbolNotInLookup()
    {
        var lookup = new StubSecurityReferenceLookup(); // empty — nothing registered
        var service = new PortfolioReadService(lookup);

        var summary = await service.BuildSummaryAsync(BuildRunWithPosition("GHOST"));

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(0);
        summary.SecurityMissingCount.Should().Be(1);
        summary.Positions.Should().ContainSingle();
        summary.Positions[0].Security.Should().BeNull();
    }

    [Fact]
    public async Task PortfolioReadService_BuildSummaryAsync_CountsPartialResolution_WhenOnlySubsetResolved()
    {
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", MakeReference("AAPL", "Equity"));
        // "MSFT" is intentionally not registered
        var service = new PortfolioReadService(lookup);

        var summary = await service.BuildSummaryAsync(BuildRunWithTwoPositions("AAPL", "MSFT"));

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(1);
        summary.SecurityMissingCount.Should().Be(1);
        summary.Positions.Single(static p => p.Symbol == "AAPL").Security.Should().NotBeNull();
        summary.Positions.Single(static p => p.Symbol == "MSFT").Security.Should().BeNull();
    }

    [Fact]
    public async Task PortfolioReadService_BuildSummaryAsync_ReturnsBaseSummary_WhenNoLookupProvided()
    {
        var service = new PortfolioReadService(); // no lookup
        var summary = await service.BuildSummaryAsync(BuildRunWithPosition("AAPL"));

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(0);
        summary.SecurityMissingCount.Should().Be(0);
        summary.Positions[0].Security.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // LedgerReadService — resolved
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LedgerReadService_BuildSummaryAsync_PopulatesSecurityReference_WhenSymbolResolved()
    {
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", MakeReference("AAPL", "Equity"));
        var service = new LedgerReadService(lookup);

        var summary = await service.BuildSummaryAsync(BuildRunWithLedger("AAPL"));

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(1);
        summary.SecurityMissingCount.Should().Be(0);

        var symbolLine = summary.TrialBalance
            .SingleOrDefault(static line => string.Equals(line.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase));
        symbolLine.Should().NotBeNull();
        symbolLine!.Security.Should().NotBeNull();
        symbolLine.Security!.DisplayName.Should().Be("Apple Inc.");
    }

    // -----------------------------------------------------------------------
    // LedgerReadService — unresolved
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LedgerReadService_BuildSummaryAsync_ReturnsNullSecurity_WhenSymbolNotInLookup()
    {
        var lookup = new StubSecurityReferenceLookup(); // empty
        var service = new LedgerReadService(lookup);

        var summary = await service.BuildSummaryAsync(BuildRunWithLedger("GHOST"));

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(0);
        summary.SecurityMissingCount.Should().Be(1);

        var symbolLine = summary.TrialBalance
            .SingleOrDefault(static line => !string.IsNullOrWhiteSpace(line.Symbol));
        symbolLine?.Security.Should().BeNull();
    }

    [Fact]
    public async Task LedgerReadService_BuildSummaryAsync_ReturnsBaseSummary_WhenNoLookupProvided()
    {
        var service = new LedgerReadService(); // no lookup
        var summary = await service.BuildSummaryAsync(BuildRunWithLedger("AAPL"));

        summary.Should().NotBeNull();
        summary!.SecurityResolvedCount.Should().Be(0);
        summary.SecurityMissingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // WorkstationSecurityReference — SubType field
    // -----------------------------------------------------------------------

    [Fact]
    public void WorkstationSecurityReference_AcceptsNullSubType()
    {
        var reference = new WorkstationSecurityReference(
            SecurityId: Guid.NewGuid(),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL");

        reference.SubType.Should().BeNull();
    }

    [Fact]
    public void WorkstationSecurityReference_AcceptsExplicitSubType()
    {
        var reference = new WorkstationSecurityReference(
            SecurityId: Guid.NewGuid(),
            DisplayName: "US T-Bill",
            AssetClass: "TreasuryBill",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "TB-123",
            SubType: "TreasuryBill");

        reference.SubType.Should().Be("TreasuryBill");
    }

    // -----------------------------------------------------------------------
    // SecurityIdentityDrillInDto shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SecurityIdentityDrillInDto_ExposesAllIdentifiersAndAliases()
    {
        var securityId = Guid.NewGuid();
        var identifiers = new[]
        {
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "BOND1", true, DateTimeOffset.UtcNow.AddDays(-5), null, null),
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US1234567890", false, DateTimeOffset.UtcNow.AddDays(-5), null, null)
        };
        var aliases = new[]
        {
            new SecurityAliasDto(Guid.NewGuid(), securityId, "alias-ticker", "BOND1-OPS", "Operations", "ticker", "description", DateTimeOffset.UtcNow.AddDays(-1), null, true)
        };

        var drillIn = new SecurityIdentityDrillInDto(
            SecurityId: securityId,
            DisplayName: "Test Bond",
            AssetClass: "Bond",
            Status: SecurityStatusDto.Active,
            Version: 3,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: identifiers,
            Aliases: aliases);

        drillIn.SecurityId.Should().Be(securityId);
        drillIn.AssetClass.Should().Be("Bond");
        drillIn.Version.Should().Be(3);
        drillIn.Identifiers.Should().HaveCount(2);
        drillIn.Aliases.Should().HaveCount(1);
        drillIn.Aliases[0].AliasValue.Should().Be("BOND1-OPS");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static WorkstationSecurityReference MakeReference(string symbol, string assetClass, string? subType = null)
        => new(
            SecurityId: Guid.NewGuid(),
            DisplayName: symbol == "AAPL" ? "Apple Inc." : symbol,
            AssetClass: assetClass,
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: symbol,
            SubType: subType);

    private static StrategyRunEntry BuildRunWithPosition(string symbol)
    {
        var ts = new DateTimeOffset(2026, 1, 10, 9, 30, 0, TimeSpan.Zero);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = new(symbol, 100, 200m, 0m, 500m)
        };
        return BuildRun(ts, positions, symbol);
    }

    private static StrategyRunEntry BuildRunWithTwoPositions(string sym1, string sym2)
    {
        var ts = new DateTimeOffset(2026, 1, 10, 9, 30, 0, TimeSpan.Zero);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            [sym1] = new(sym1, 100, 200m, 0m, 500m),
            [sym2] = new(sym2, 50, 300m, 100m, -100m)
        };
        return BuildRun(ts, positions, sym1);
    }

    private static StrategyRunEntry BuildRunWithLedger(string symbol)
    {
        var ts = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
        var ledger = new Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Trading Revenue", LedgerAccountType.Revenue, Symbol: symbol);

        ledger.PostLines(ts, "entry-1", new[]
        {
            (cash, 1_000m, 0m),
            (revenue, 0m, 1_000m)
        });

        return new StrategyRunEntry(
            RunId: $"enrich-ledger-{symbol}",
            StrategyId: "enrich-strat",
            StrategyName: "Enrichment Strategy",
            RunType: RunType.Backtest,
            StartedAt: ts,
            EndedAt: ts.AddHours(1),
            Metrics: BuildBacktestResultWithLedger(ts, ledger));
    }

    private static StrategyRunEntry BuildRun(DateTimeOffset ts, Dictionary<string, Position> positions, string primarySymbol)
    {
        var snapshot = new PortfolioSnapshot(
            Timestamp: ts,
            Date: DateOnly.FromDateTime(ts.UtcDateTime),
            Cash: 50_000m,
            MarginBalance: 0m,
            LongMarketValue: 50_000m,
            ShortMarketValue: 0m,
            TotalEquity: 100_000m,
            DailyReturn: 0.005m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 100_500m, GrossPnl: 600m, NetPnl: 500m,
            TotalReturn: 0.005m, AnnualizedReturn: 0.06m, SharpeRatio: 1.2, SortinoRatio: 1.4,
            CalmarRatio: 0.9, MaxDrawdown: 200m, MaxDrawdownPercent: 0.002m, MaxDrawdownRecoveryDays: 1,
            ProfitFactor: 2.0, WinRate: 0.65, TotalTrades: 2, WinningTrades: 2, LosingTrades: 0,
            TotalCommissions: 10m, TotalMarginInterest: 2m, TotalShortRebates: 0m, Xirr: 0.06,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                [primarySymbol] = new(primarySymbol, 500m, 0m, 2, 10m, 2m)
            });

        var result = new BacktestResult(
            Request: new BacktestRequest(
                From: DateOnly.FromDateTime(ts.UtcDateTime),
                To: DateOnly.FromDateTime(ts.AddDays(1).UtcDateTime),
                Symbols: positions.Keys.ToArray(),
                InitialCash: 100_000m),
            Universe: new HashSet<string>(positions.Keys, StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 100);

        return new StrategyRunEntry(
            RunId: $"enrich-portfolio-{primarySymbol}",
            StrategyId: "enrich-strat",
            StrategyName: "Enrichment Strategy",
            RunType: RunType.Backtest,
            StartedAt: ts,
            EndedAt: ts.AddHours(1),
            Metrics: result);
    }

    private static BacktestResult BuildBacktestResultWithLedger(DateTimeOffset ts, Ledger ledger)
    {
        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 101_000m, GrossPnl: 1_100m, NetPnl: 1_000m,
            TotalReturn: 0.01m, AnnualizedReturn: 0.12m, SharpeRatio: 1.5, SortinoRatio: 1.7,
            CalmarRatio: 1.0, MaxDrawdown: 200m, MaxDrawdownPercent: 0.002m, MaxDrawdownRecoveryDays: 1,
            ProfitFactor: 2.5, WinRate: 0.70, TotalTrades: 1, WinningTrades: 1, LosingTrades: 0,
            TotalCommissions: 5m, TotalMarginInterest: 0m, TotalShortRebates: 0m, Xirr: 0.12,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(
            Request: new BacktestRequest(
                From: DateOnly.FromDateTime(ts.UtcDateTime),
                To: DateOnly.FromDateTime(ts.AddDays(1).UtcDateTime),
                Symbols: ["AAPL"],
                InitialCash: 100_000m),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromSeconds(3),
            TotalEventsProcessed: 50);
    }

    private sealed class StubSecurityReferenceLookup : ISecurityReferenceLookup
    {
        private readonly Dictionary<string, WorkstationSecurityReference> _refs = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string symbol, WorkstationSecurityReference reference)
            => _refs[symbol] = reference;

        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult(_refs.TryGetValue(symbol, out var r) ? r : null);
    }
}
