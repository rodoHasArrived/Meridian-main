using FluentAssertions;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Strategies.Services;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Tests for <see cref="AggregatePortfolioService"/> — netting logic, exposure
/// calculations, and per-symbol position queries.
/// </summary>
public sealed class AggregatePortfolioServiceTests
{
    // ─── GetAggregatedPositions ──────────────────────────────────────────────

    [Fact]
    public void GetAggregatedPositions_EmptyRegistry_ReturnsEmpty()
    {
        var (service, _) = Build();

        service.GetAggregatedPositions().Should().BeEmpty();
    }

    [Fact]
    public void GetAggregatedPositions_SingleRun_ProjectsPositions()
    {
        var (service, registry) = Build();

        var portfolio = BuildPortfolioWithPosition("AAPL", qty: 10m, cost: 150m);
        registry.Register("run-1", portfolio);

        var result = service.GetAggregatedPositions();

        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("AAPL");
        result[0].TotalQuantity.Should().Be(10m);
        result[0].LongQuantity.Should().Be(10m);
        result[0].ShortQuantity.Should().Be(0m);
    }

    [Fact]
    public void GetAggregatedPositions_MultipleRuns_NetsSameSymbol()
    {
        var (service, registry) = Build();

        var p1 = BuildPortfolioWithPosition("AAPL", qty: 10m, cost: 150m);
        var p2 = BuildPortfolioWithPosition("AAPL", qty: 5m,  cost: 155m);
        registry.Register("run-1", p1);
        registry.Register("run-2", p2);

        var result = service.GetAggregatedPositions();

        result.Should().HaveCount(1);
        result[0].TotalQuantity.Should().Be(15m);
    }

    [Fact]
    public void GetAggregatedPositions_LongAndShortSameSymbol_NetsCorrectly()
    {
        var (service, registry) = Build();

        var p1 = BuildPortfolioWithPosition("MSFT", qty:  20m, cost: 300m);
        var p2 = BuildPortfolioWithPosition("MSFT", qty: -8m,  cost: 310m);
        registry.Register("run-1", p1);
        registry.Register("run-2", p2);

        var result = service.GetAggregatedPositions();

        result[0].TotalQuantity.Should().Be(12m);
        result[0].LongQuantity.Should().Be(20m);
        result[0].ShortQuantity.Should().Be(8m);
    }

    [Fact]
    public void GetAggregatedPositions_FilterByRunIds_OnlySelectedRuns()
    {
        var (service, registry) = Build();

        registry.Register("run-1", BuildPortfolioWithPosition("AAPL", 10m, 150m));
        registry.Register("run-2", BuildPortfolioWithPosition("GOOG", 5m, 2800m));

        var result = service.GetAggregatedPositions(["run-1"]);

        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void GetAggregatedPositions_ContributionCount_MatchesRunCount()
    {
        var (service, registry) = Build();

        registry.Register("run-1", BuildPortfolioWithPosition("TSLA", 3m, 200m));
        registry.Register("run-2", BuildPortfolioWithPosition("TSLA", 7m, 205m));

        var result = service.GetAggregatedPositions();

        result[0].Contributions.Should().HaveCount(2);
    }

    // ─── GetCrossStrategyExposure ────────────────────────────────────────────

    [Fact]
    public void GetCrossStrategyExposure_NoPositions_ReturnsZeroExposure()
    {
        var (service, _) = Build();

        var report = service.GetCrossStrategyExposure();

        report.GrossExposure.Should().Be(0m);
        report.NetExposure.Should().Be(0m);
        report.Top5Concentrations.Should().BeEmpty();
    }

    [Fact]
    public void GetCrossStrategyExposure_LongOnly_GrossEqualsNet()
    {
        var (service, registry) = Build();
        registry.Register("run-1", BuildPortfolioWithPosition("SPY", 100m, 450m));

        var report = service.GetCrossStrategyExposure();

        report.GrossExposure.Should().Be(100m * 450m);
        report.NetExposure.Should().Be(100m * 450m);
    }

    [Fact]
    public void GetCrossStrategyExposure_Top5Concentrations_LimitedToFive()
    {
        var (service, registry) = Build();

        var symbols = new[] { "A", "B", "C", "D", "E", "F" };
        foreach (var (s, i) in symbols.Select((s, i) => (s, i)))
            registry.Register($"run-{i}", BuildPortfolioWithPosition(s, 100m, 100m + i));

        var report = service.GetCrossStrategyExposure();

        report.Top5Concentrations.Should().HaveCount(5);
    }

    // ─── GetNetPositionForSymbol ─────────────────────────────────────────────

    [Fact]
    public void GetNetPositionForSymbol_UnknownSymbol_ReturnsZero()
    {
        var (service, _) = Build();

        var pos = service.GetNetPositionForSymbol("UNKNOWN");

        pos.NetQuantity.Should().Be(0m);
        pos.GrossQuantity.Should().Be(0m);
    }

    [Fact]
    public void GetNetPositionForSymbol_NetFromMultipleRuns()
    {
        var (service, registry) = Build();
        registry.Register("run-1", BuildPortfolioWithPosition("NVDA", 20m, 800m));
        registry.Register("run-2", BuildPortfolioWithPosition("NVDA", -5m, 820m));

        var pos = service.GetNetPositionForSymbol("NVDA");

        pos.NetQuantity.Should().Be(15m);
        pos.GrossQuantity.Should().Be(25m);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (AggregatePortfolioService Service, PortfolioRegistry Registry) Build()
    {
        var registry = new PortfolioRegistry();
        return (new AggregatePortfolioService(registry), registry);
    }

    private static StubPortfolio BuildPortfolioWithPosition(string symbol, decimal qty, decimal cost)
    {
        var pos = new ExecutionPosition(symbol, (long)qty, cost, 0m, 0m);

        var account = new StubAccountPortfolio("acc-1", "Default", AccountKind.Brokerage,
            positions: new Dictionary<string, ExecutionPosition>(StringComparer.OrdinalIgnoreCase)
            {
                [symbol] = pos,
            });

        return new StubPortfolio([account]);
    }

    // ─── Stubs ────────────────────────────────────────────────────────────────

    private sealed class StubPortfolio : IMultiAccountPortfolioState
    {
        private readonly IReadOnlyList<IAccountPortfolio> _accounts;

        public StubPortfolio(IReadOnlyList<IAccountPortfolio> accounts) => _accounts = accounts;

        public decimal Cash => _accounts.Sum(static a => a.Cash);
        public decimal PortfolioValue => Cash;
        public decimal UnrealisedPnl => _accounts.Sum(static a => a.UnrealisedPnl);
        public decimal RealisedPnl => _accounts.Sum(static a => a.RealisedPnl);
        public IReadOnlyDictionary<string, ExecutionPosition> Positions =>
            _accounts.SelectMany(static a => a.Positions).ToDictionary(static kv => kv.Key, static kv => kv.Value);
        public IReadOnlyList<IAccountPortfolio> Accounts => _accounts;
        public IAccountPortfolio? GetAccount(string id) =>
            _accounts.FirstOrDefault(a => string.Equals(a.AccountId, id, StringComparison.OrdinalIgnoreCase));
        public MultiAccountPortfolioSnapshot GetAggregateSnapshot() =>
            MultiAccountPortfolioSnapshot.FromAccounts(_accounts.Select(static a => a.TakeSnapshot()).ToArray());
    }

    private sealed class StubAccountPortfolio : IAccountPortfolio
    {
        public StubAccountPortfolio(
            string accountId,
            string displayName,
            AccountKind kind,
            IReadOnlyDictionary<string, ExecutionPosition> positions)
        {
            AccountId = accountId;
            DisplayName = displayName;
            Kind = kind;
            Positions = positions;
        }

        public string AccountId { get; }
        public string DisplayName { get; }
        public AccountKind Kind { get; }
        public decimal Cash => 0m;
        public decimal MarginBalance => 0m;
        public IReadOnlyDictionary<string, ExecutionPosition> Positions { get; }
        public decimal UnrealisedPnl => Positions.Values.Sum(static p => p.UnrealisedPnl);
        public decimal RealisedPnl => 0m;
        public decimal LongMarketValue => Positions.Values.Where(static p => p.Quantity > 0).Sum(static p => (decimal)p.AbsoluteQuantity * p.AverageCostBasis);
        public decimal ShortMarketValue => Positions.Values.Where(static p => p.Quantity < 0).Sum(static p => (decimal)p.AbsoluteQuantity * p.AverageCostBasis);
        public ExecutionAccountDetailSnapshot TakeSnapshot() =>
            new(AccountId, DisplayName, Kind, Cash, MarginBalance,
                LongMarketValue, ShortMarketValue,
                LongMarketValue + ShortMarketValue, LongMarketValue - ShortMarketValue,
                UnrealisedPnl, RealisedPnl,
                Positions.Values.ToArray(), DateTimeOffset.UtcNow);
    }
}
