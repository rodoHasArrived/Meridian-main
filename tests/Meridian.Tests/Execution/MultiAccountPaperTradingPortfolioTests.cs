using FluentAssertions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

/// <summary>
/// Tests for the multi-account refactor of <see cref="PaperTradingPortfolio"/>.
/// Verifies per-account P&amp;L isolation, fill routing, and aggregate projections.
/// </summary>
public sealed class MultiAccountPaperTradingPortfolioTests
{
    // ─── Construction ────────────────────────────────────────────────────────

    [Fact]
    public void SingleAccount_Ctor_BackwardCompatible_CashSetCorrectly()
    {
        var portfolio = new PaperTradingPortfolio(50_000m);

        portfolio.Cash.Should().Be(50_000m);
        portfolio.Accounts.Should().HaveCount(1);
        portfolio.Accounts[0].AccountId.Should().Be(PaperTradingPortfolio.DefaultAccountId);
    }

    [Fact]
    public void MultiAccount_Ctor_RegistersAllAccounts()
    {
        var accounts = new[]
        {
            new AccountDefinition("broker-1", "IB Account",      AccountKind.Brokerage, 100_000m),
            new AccountDefinition("cash-1",   "Cash Sweep",       AccountKind.Bank,       20_000m),
        };

        var portfolio = new PaperTradingPortfolio(accounts);

        portfolio.Accounts.Should().HaveCount(2);
        portfolio.Cash.Should().Be(120_000m);
        portfolio.GetAccount("broker-1").Should().NotBeNull();
        portfolio.GetAccount("cash-1").Should().NotBeNull();
    }

    [Fact]
    public void MultiAccount_Ctor_EmptyAccounts_Throws()
    {
        var act = () => new PaperTradingPortfolio(Array.Empty<AccountDefinition>());
        act.Should().Throw<ArgumentException>();
    }

    // ─── Fill routing ────────────────────────────────────────────────────────

    [Fact]
    public void ApplyFill_DefaultAccount_UpdatesCashAndPosition()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var fill = BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 150m);

        portfolio.ApplyFill(fill);

        portfolio.Cash.Should().Be(100_000m - 1_500m);
        portfolio.Positions.Should().ContainKey("AAPL");
        portfolio.Positions["AAPL"].AverageCostBasis.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ApplyFill_SpecificAccount_IsolatesFromOtherAccounts()
    {
        var accounts = new[]
        {
            new AccountDefinition("acc-a", "Account A", AccountKind.Brokerage, 50_000m),
            new AccountDefinition("acc-b", "Account B", AccountKind.Brokerage, 50_000m),
        };
        var portfolio = new PaperTradingPortfolio(accounts);

        var fill = BuildFill("MSFT", OrderSide.Buy, qty: 5, price: 300m);
        portfolio.ApplyFill("acc-a", fill);

        var accA = portfolio.GetAccount("acc-a")!;
        var accB = portfolio.GetAccount("acc-b")!;

        accA.Positions.Should().ContainKey("MSFT");
        accB.Positions.Should().NotContainKey("MSFT");
        accA.Positions["MSFT"].Quantity.Should().Be(5);
    }

    [Fact]
    public void ApplyFill_UnknownAccount_NoOp()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        var fill = BuildFill("TSLA", OrderSide.Buy, qty: 1, price: 200m);

        portfolio.ApplyFill("nonexistent", fill);

        portfolio.Cash.Should().Be(10_000m);
        portfolio.Positions.Should().BeEmpty();
    }

    // ─── Aggregate projections ───────────────────────────────────────────────

    [Fact]
    public void Positions_AggregateSetsNetQuantityAcrossAccounts()
    {
        var accounts = new[]
        {
            new AccountDefinition("acc-1", "A1", AccountKind.Brokerage, 100_000m),
            new AccountDefinition("acc-2", "A2", AccountKind.Brokerage, 100_000m),
        };
        var portfolio = new PaperTradingPortfolio(accounts);

        portfolio.ApplyFill("acc-1", BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 150m));
        portfolio.ApplyFill("acc-2", BuildFill("AAPL", OrderSide.Buy, qty: 5,  price: 155m));

        portfolio.Positions["AAPL"].Quantity.Should().Be(15);
    }

    [Fact]
    public void GetAggregateSnapshot_ContainsAllAccounts()
    {
        var accounts = new[]
        {
            new AccountDefinition("acc-a", "Brokerage", AccountKind.Brokerage, 80_000m),
            new AccountDefinition("acc-b", "Cash Sweep", AccountKind.Bank,     20_000m),
        };
        var portfolio = new PaperTradingPortfolio(accounts);

        var snapshot = portfolio.GetAggregateSnapshot();

        snapshot.Accounts.Should().HaveCount(2);
        snapshot.TotalCash.Should().Be(100_000m);
    }

    // ─── AccountKind isolation ───────────────────────────────────────────────

    [Fact]
    public void BankAccount_Kind_IsPreservedInSnapshot()
    {
        var accounts = new[]
        {
            new AccountDefinition("mmf-1", "Money Market", AccountKind.Bank, 10_000m),
        };
        var portfolio = new PaperTradingPortfolio(accounts);

        var snap = portfolio.GetAllAccountSnapshots()[0];

        snap.Kind.Should().Be(AccountKind.Bank);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ExecutionReport BuildFill(
        string symbol,
        OrderSide side,
        decimal qty,
        decimal price,
        decimal commission = 0m) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        Symbol = symbol,
        Side = side,
        ReportType = ExecutionReportType.Fill,
        FilledQuantity = qty,
        FillPrice = price,
        Commission = commission,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
