using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Unit tests for <see cref="SecurityMasterAmortizationLedgerBridge"/> — posting structured cash
/// flow / accrual / amortization projections into the ledger instead of leaving them display-only.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterAmortizationLedgerBridgeTests
{
    private static readonly Guid SecurityId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private const string Ticker = "CORP2030";

    private static SecurityMasterAmortizationLedgerBridge BuildBridge(ISecurityMasterCashFlowService cashFlowService)
        => new(cashFlowService, NullLogger<SecurityMasterAmortizationLedgerBridge>.Instance);

    private static ISecurityMasterCashFlowService CashFlowServiceWith(
        StructuredCashFlowProjectionDto? projection,
        StructuredCashFlowScenario scenario = StructuredCashFlowScenario.Base)
    {
        var service = Substitute.For<ISecurityMasterCashFlowService>();
        service.GetProjectionAsync(SecurityId, scenario, Arg.Any<CancellationToken>()).Returns(projection);
        return service;
    }

    private static StructuredCashFlowProjectionDto Projection(params StructuredCashFlowScheduleEntry[] schedule)
        => new(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base, DateTimeOffset.UtcNow, schedule);

    private static StructuredCashFlowProjectionDto ScenarioProjection(
        StructuredCashFlowScenario scenario,
        StructuredCashFlowStaleness staleness,
        params StructuredCashFlowScheduleEntry[] schedule)
        => new(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, scenario, DateTimeOffset.UtcNow, schedule, staleness);

    private static StructuredCashFlowScheduleEntry Period(int year, int month, int day, decimal interest, decimal principal = 0m)
        => new(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero), principal, interest, 1m);

    [Fact]
    public async Task PostProjectedCashFlowsAsync_PostsCouponAccrualPerPeriod()
    {
        var projection = Projection(
            Period(2026, 6, 30, interest: 30m),
            Period(2026, 12, 31, interest: 30m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger);

        posted.Should().Be(2);
        ledger.Journal.Should().HaveCount(2);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(60m);
        ledger.GetBalance(LedgerAccounts.CouponIncome).Should().Be(60m);

        var first = ledger.Journal[0];
        first.Metadata.SecurityId.Should().Be(SecurityId);
        first.Metadata.Symbol.Should().Be(Ticker);
        first.Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);
        first.Metadata.ActivityType.Should().Be("FixedIncomeAmortization");
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_AmortizesPremiumTowardPar()
    {
        var projection = Projection(Period(2026, 6, 30, interest: 20m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        // 1,000 face bought at 102% -> 20 of premium amortized over the single posted period.
        var context = new AmortizationLedgerPostingContext(PositionFace: 1000m, PurchasePricePercentOfPar: 102m);
        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, context);

        posted.Should().Be(1);
        ledger.Journal[0].IsBalanced.Should().BeTrue();
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(20m);
        ledger.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-20m); // carrying value written down
        ledger.GetBalance(LedgerAccounts.CouponIncome).Should().Be(0m); // coupon 20 offset by premium 20
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_AccretesDiscountTowardPar()
    {
        var projection = Projection(Period(2026, 6, 30, interest: 0m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var context = new AmortizationLedgerPostingContext(PositionFace: 1000m, PurchasePricePercentOfPar: 98m);
        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, context);

        posted.Should().Be(1);
        ledger.Journal[0].IsBalanced.Should().BeTrue();
        ledger.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(20m); // carrying value written up
        ledger.GetBalance(LedgerAccounts.CouponIncome).Should().Be(20m);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_PostsPrincipalPaydownSeparately()
    {
        var projection = Projection(Period(2026, 6, 30, interest: 30m, principal: 500m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger);

        posted.Should().Be(2); // coupon accrual + principal paydown
        ledger.Journal.Should().HaveCount(2);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(500m);
        ledger.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-500m);

        var paydown = ledger.Journal.Single(entry => entry.Metadata.ActivityType == "PrincipalPaydown");
        paydown.Metadata.SecurityId.Should().Be(SecurityId);
        paydown.Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_IsIdempotent()
    {
        var projection = Projection(Period(2026, 6, 30, interest: 30m, principal: 500m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var firstPass = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger);
        var secondPass = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger);

        firstPass.Should().Be(2);
        secondPass.Should().Be(0);
        ledger.Journal.Should().HaveCount(2);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_SmallPremiumOverManyPeriods_NeverBooksReversedSign()
    {
        // 1 face at 102% -> 0.02 total premium spread over 4 periods. Naive per-period rounding
        // would over-allocate the first periods and flip the last period into a discount accretion;
        // the cumulative-target distribution keeps every share a premium write-down.
        var projection = Projection(
            Period(2026, 3, 31, interest: 5m),
            Period(2026, 6, 30, interest: 5m),
            Period(2026, 9, 30, interest: 5m),
            Period(2026, 12, 31, interest: 5m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var context = new AmortizationLedgerPostingContext(PositionFace: 1m, PurchasePricePercentOfPar: 102m);
        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, context);

        posted.Should().Be(4);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);

        // A premium position must only ever credit (write down) the carrying account; a debit would
        // mean an erroneous discount accretion on a premium bond.
        var securities = LedgerAccounts.Securities(Ticker);
        ledger.Journal.SelectMany(entry => entry.Lines)
            .Where(line => line.Account == securities)
            .Should().OnlyContain(line => line.Debit == 0m);
        ledger.GetBalance(securities).Should().Be(-0.02m);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_RespectsMaxPeriods()
    {
        var projection = Projection(
            Period(2026, 3, 31, interest: 10m),
            Period(2026, 6, 30, interest: 10m),
            Period(2026, 9, 30, interest: 10m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var posted = await bridge.PostProjectedCashFlowsAsync(
            SecurityId, Ticker, ledger, new AmortizationLedgerPostingContext(MaxPeriods: 2));

        posted.Should().Be(2);
        ledger.Journal.Should().HaveCount(2);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_WithNoProjection_PostsNothing()
    {
        var bridge = BuildBridge(CashFlowServiceWith(projection: null));
        var ledger = new DomainLedger();

        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger);

        posted.Should().Be(0);
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_ScopesAccountsToFinancialAccount()
    {
        var projection = Projection(Period(2026, 6, 30, interest: 40m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var context = new AmortizationLedgerPostingContext(FinancialAccountId: "broker-9");
        await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, context);

        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker, "broker-9")).Should().Be(40m);
        ledger.GetBalance(LedgerAccounts.CouponIncomeFor("broker-9")).Should().Be(40m);
        ledger.Journal[0].Metadata.FinancialAccountId.Should().Be("broker-9");
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_WithStaleSource_PostsNothing()
    {
        // A stale cash flow source must be gated out of the ledger, not accrued from.
        var projection = ScenarioProjection(
            StructuredCashFlowScenario.Base,
            StructuredCashFlowStaleness.Stale,
            Period(2026, 6, 30, interest: 30m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger);

        posted.Should().Be(0);
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_WithNonBaseScenario_PostsNothing()
    {
        // Rate-shocked what-if projections are analytics only and must never reach the ledger.
        var projection = ScenarioProjection(
            StructuredCashFlowScenario.Up200,
            StructuredCashFlowStaleness.Fresh,
            Period(2026, 6, 30, interest: 30m));
        var bridge = BuildBridge(CashFlowServiceWith(projection, StructuredCashFlowScenario.Up200));
        var ledger = new DomainLedger();

        var posted = await bridge.PostProjectedCashFlowsAsync(
            SecurityId, Ticker, ledger, new AmortizationLedgerPostingContext(Scenario: StructuredCashFlowScenario.Up200));

        posted.Should().Be(0);
        ledger.Journal.Should().BeEmpty();
    }
}
