using FluentAssertions;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Guards the cached aggregate views on <see cref="SimulatedPortfolio"/>.
/// </summary>
/// <remarks>
/// <para>
/// Equity and position views are rebuilt from every account, position, and lot. The engine exposes
/// them through <c>IBacktestContext.PortfolioValue</c> and <c>.Positions</c>, so a strategy reading
/// its own equity once per bar previously paid a full rebuild per bar. They are now cached against
/// a state version.
/// </para>
/// <para>
/// The failure mode that matters is not a slow rebuild but a missed invalidation: a stale equity is
/// a silently wrong answer that no downstream assertion would catch. Each test below mutates the
/// portfolio through one of the four mutating members and asserts the views moved with it.
/// </para>
/// </remarks>
public sealed class SimulatedPortfolioCacheTests
{
    private static SimulatedPortfolio CreatePortfolio(decimal initialCash = 100_000m)
        => new(initialCash, new FixedCommissionModel(0m), annualMarginRate: 0.05, annualShortRebateRate: 0.02);

    private static FillEvent Fill(string symbol, long qty, decimal price)
        => new(Guid.NewGuid(), Guid.NewGuid(), symbol, qty, price, 0m, DateTimeOffset.UtcNow);

    // ── Invalidation on each mutating member ─────────────────────────────────

    [Fact]
    public void ProcessFill_InvalidatesPositions()
    {
        var portfolio = CreatePortfolio();
        portfolio.GetCurrentPositions().Should().BeEmpty();

        portfolio.ProcessFill(Fill("SPY", 10, 400m));

        portfolio.GetCurrentPositions().Should().ContainKey("SPY");
        portfolio.GetCurrentPositions()["SPY"].Quantity.Should().Be(10);
    }

    [Fact]
    public void ProcessFill_InvalidatesEquity()
    {
        var portfolio = CreatePortfolio(100_000m);
        portfolio.UpdateLastPrice("SPY", 400m);
        var before = portfolio.ComputeCurrentEquity();

        // Buying at the mark is cash-neutral in equity terms; selling above it is not.
        portfolio.ProcessFill(Fill("SPY", 10, 400m));
        portfolio.UpdateLastPrice("SPY", 500m);

        portfolio.ComputeCurrentEquity().Should().BeGreaterThan(before);
    }

    [Fact]
    public void UpdateLastPrice_InvalidatesEquity()
    {
        var portfolio = CreatePortfolio();
        portfolio.ProcessFill(Fill("SPY", 10, 400m));
        portfolio.UpdateLastPrice("SPY", 400m);
        var atCost = portfolio.ComputeCurrentEquity();

        portfolio.UpdateLastPrice("SPY", 450m);

        portfolio.ComputeCurrentEquity().Should().Be(atCost + 10 * 50m);
    }

    [Fact]
    public void UpdateLastPrice_InvalidatesAccountSnapshots()
    {
        var portfolio = CreatePortfolio();
        portfolio.ProcessFill(Fill("SPY", 10, 400m));
        portfolio.UpdateLastPrice("SPY", 400m);
        var before = portfolio.GetAccountSnapshots().Values.Sum(s => s.LongMarketValue);

        portfolio.UpdateLastPrice("SPY", 450m);

        portfolio.GetAccountSnapshots().Values.Sum(s => s.LongMarketValue).Should().BeGreaterThan(before);
    }

    [Fact]
    public void AccrueDailyInterest_InvalidatesEquity()
    {
        // Borrow against the account so there is a margin balance to accrue against.
        var portfolio = CreatePortfolio(10_000m);
        portfolio.ProcessFill(Fill("SPY", 100, 200m));
        portfolio.UpdateLastPrice("SPY", 200m);
        var before = portfolio.ComputeCurrentEquity();

        for (var day = 0; day < 30; day++)
        {
            portfolio.AccrueDailyInterest(new DateOnly(2026, 1, 1).AddDays(day));
        }

        // Margin interest is a cost, so equity must fall — and must not be served from cache.
        portfolio.ComputeCurrentEquity().Should().BeLessThan(before);
    }

    // ── Repeated reads stay consistent ───────────────────────────────────────

    [Fact]
    public void RepeatedReads_WithoutMutation_AreEqual()
    {
        var portfolio = CreatePortfolio();
        portfolio.ProcessFill(Fill("SPY", 10, 400m));
        portfolio.UpdateLastPrice("SPY", 415m);

        var first = portfolio.ComputeCurrentEquity();
        var second = portfolio.ComputeCurrentEquity();
        var third = portfolio.ComputeCurrentEquity();

        second.Should().Be(first);
        third.Should().Be(first);
    }

    [Fact]
    public void CachedViewsAgreeWithSnapshot()
    {
        // TakeSnapshot and the cached accessors derive from the same builders; if the cache served
        // a stale view to one and not the other, a run's reported equity would disagree with the
        // equity its own strategy observed.
        var portfolio = CreatePortfolio();
        portfolio.ProcessFill(Fill("SPY", 10, 400m));
        portfolio.ProcessFill(Fill("QQQ", 5, 300m));
        portfolio.UpdateLastPrice("SPY", 420m);
        portfolio.UpdateLastPrice("QQQ", 310m);

        var equity = portfolio.ComputeCurrentEquity();
        var positions = portfolio.GetCurrentPositions();
        var snapshot = portfolio.TakeSnapshot(DateTimeOffset.UtcNow, new DateOnly(2026, 1, 2));

        snapshot.TotalEquity.Should().Be(equity);
        snapshot.Positions.Should().HaveCount(positions.Count);
        snapshot.Positions["SPY"].Quantity.Should().Be(positions["SPY"].Quantity);
        snapshot.Positions["QQQ"].Quantity.Should().Be(positions["QQQ"].Quantity);
    }

    [Fact]
    public void InterleavedMutationAndReads_TrackEveryStep()
    {
        // A read between each mutation is the pattern a per-bar strategy produces, and the one most
        // likely to expose a version that fails to advance.
        var portfolio = CreatePortfolio();
        var observed = new List<decimal>();

        for (var i = 1; i <= 5; i++)
        {
            portfolio.ProcessFill(Fill("SPY", 1, 100m));
            portfolio.UpdateLastPrice("SPY", 100m + (i * 10m));
            observed.Add(portfolio.ComputeCurrentEquity());
        }

        observed.Should().OnlyHaveUniqueItems();
        observed.Should().BeInAscendingOrder();
    }

    [Fact]
    public void PositionsView_ReflectsPositionBeingClosed()
    {
        var portfolio = CreatePortfolio();
        portfolio.ProcessFill(Fill("SPY", 10, 400m));
        portfolio.GetCurrentPositions().Should().ContainKey("SPY");

        portfolio.ProcessFill(Fill("SPY", -10, 410m));

        portfolio.GetCurrentPositions().Should().NotContainKey("SPY");
    }

    [Fact]
    public void CachedPortfolioViews_RejectCallerMutation()
    {
        var portfolio = CreatePortfolio();
        portfolio.ProcessFill(Fill("AAPL", 100, 150m));

        var positions = portfolio.GetCurrentPositions();
        var accounts = portfolio.GetAccountSnapshots();

        // Strategy code is user-authored (plugins, QuantScript lambdas), so a cast-and-mutate is
        // reachable. Caching made the returned instance shared with every later reader and with
        // TakeSnapshot, so the view has to be genuinely immutable rather than merely typed as such.
        ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, Position>>)positions)
            .Invoking(collection => collection.Clear())
            .Should().Throw<NotSupportedException>();

        ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, FinancialAccountSnapshot>>)accounts)
            .Invoking(collection => collection.Clear())
            .Should().Throw<NotSupportedException>();

        portfolio.GetCurrentPositions().Should().ContainKey("AAPL");
    }
}
