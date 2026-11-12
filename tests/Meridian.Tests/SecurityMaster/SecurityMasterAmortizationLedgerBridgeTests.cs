using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;
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
        => new(
            SecurityId,
            StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base,
            DateTimeOffset.UtcNow,
            schedule,
            Staleness: StructuredCashFlowStaleness.Fresh);

    private static StructuredCashFlowProjectionDto Projection(StructuredCashFlowTerms terms, params StructuredCashFlowScheduleEntry[] schedule)
        => new(
            SecurityId,
            StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base,
            DateTimeOffset.UtcNow,
            schedule,
            Staleness: StructuredCashFlowStaleness.Fresh,
            TermsUsed: terms);

    private static StructuredCashFlowTerms TermsWith(DateOnly maturity, string dayCount = "ACT/365")
        => StructuredCashFlowTerms.Empty with { MaturityDate = maturity, DayCountConvention = dayCount };

    private static LedgerTaxLot Lot(decimal quantity, decimal unitCost, int year = 2026, int month = 1, int day = 1)
        => new("lot-1", new DateOnly(year, month, day), quantity, unitCost, SecurityId) { UnitKind = SecurityLotUnitKind.FaceValue, Multiplier = 100m };

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
        var projection = Projection(TermsWith(maturity: new DateOnly(2026, 6, 30)), Period(2026, 6, 30, interest: 20m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        // A lot of 10 par-100 units bought at 102 -> 20 of premium, fully amortized by the single
        // posted period because that period is the maturity date.
        var context = new AmortizationLedgerPostingContext(OpenLots: [Lot(quantity: 10m, unitCost: 102m)]);
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
        var projection = Projection(TermsWith(maturity: new DateOnly(2026, 6, 30)), Period(2026, 6, 30, interest: 0m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var context = new AmortizationLedgerPostingContext(OpenLots: [Lot(quantity: 10m, unitCost: 98m)]);
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
        // A tiny lot (0.01 par-100 units at 102) -> 0.02 total premium spread over 4 quarterly
        // periods. Naive per-period rounding would over-allocate the first periods and flip a later
        // period into a discount accretion; the cumulative-target distribution over monotone
        // day-count weights keeps every share a premium write-down.
        var projection = Projection(
            TermsWith(maturity: new DateOnly(2026, 12, 31), dayCount: "30/360"),
            Period(2026, 3, 31, interest: 5m),
            Period(2026, 6, 30, interest: 5m),
            Period(2026, 9, 30, interest: 5m),
            Period(2026, 12, 31, interest: 5m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var context = new AmortizationLedgerPostingContext(OpenLots: [Lot(quantity: 0.01m, unitCost: 102m)]);
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
    public async Task PostProjectedCashFlowsAsync_WeighsAmortizationByDayCountYearFraction()
    {
        // 20 of premium over two semiannual periods under ACT/365: the first period covers
        // 181/365 of the year (Jan 1 -> Jul 1), so it amortizes round(20 x 181/365) = 9.92 and the
        // second takes the 10.08 remainder. An equal per-period split would post 10.00/10.00 — this
        // pins the same year-fraction method the cost-basis relief engine uses.
        var projection = Projection(
            TermsWith(maturity: new DateOnly(2027, 1, 1), dayCount: "ACT/365"),
            Period(2026, 7, 1, interest: 0m),
            Period(2027, 1, 1, interest: 0m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var context = new AmortizationLedgerPostingContext(OpenLots: [Lot(quantity: 10m, unitCost: 102m)]);
        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, context);

        posted.Should().Be(2);
        var securities = LedgerAccounts.Securities(Ticker);
        var credits = ledger.Journal
            .Select(entry => entry.Lines.Where(line => line.Account == securities).Sum(line => line.Credit))
            .ToList();
        credits.Should().Equal(9.92m, 10.08m);
        ledger.GetBalance(securities).Should().Be(-20m);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_DoesNotAmortizeBeforeTheLotIsAcquired()
    {
        // The lot is acquired on the first period date, so that period accrues nothing for it; the
        // full 20 of premium amortizes across the lot's actual holding window only.
        var projection = Projection(
            TermsWith(maturity: new DateOnly(2026, 12, 31)),
            Period(2026, 6, 30, interest: 0m),
            Period(2026, 12, 31, interest: 0m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var context = new AmortizationLedgerPostingContext(
            OpenLots: [Lot(quantity: 10m, unitCost: 102m, year: 2026, month: 6, day: 30)]);
        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, context);

        // Only the second period posts: the first has no coupon and no amortization yet.
        posted.Should().Be(1);
        ledger.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-20m);
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
    public async Task PostProjectedCashFlowsAsync_WithUnknownFreshness_PostsNothing()
    {
        var projection = ScenarioProjection(
            StructuredCashFlowScenario.Base,
            StructuredCashFlowStaleness.Unknown,
            Period(2026, 6, 30, interest: 30m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var ledger = new DomainLedger();

        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger);

        posted.Should().Be(0);
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_WithDeclaredSchedule_PostsNothing()
    {
        // A schedule generated from declared conventions (ADR-025) is analysis only until journal
        // identity keys on schedule period identity.
        var projection = Projection(Period(2026, 6, 30, interest: 30m)) with
        {
            ScheduleEvidence = new StructuredCashFlowScheduleEvidence(
                "contractual-schedule-kernel-v1",
                ScheduleSpecFactory.DeclaredMappingVersion,
                [],
                [])
        };
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

    [Fact]
    public async Task PostProjectedCashFlowsAsync_AmortizedThrough_ExcludesAmortizationAlreadyRecognized()
    {
        // 365-day life from 2026-01-01; 20 of premium. Posting only the second half must not
        // repeat the amortization already recognized through 2026-07-02 (182 of 365 days).
        var projection = Projection(TermsWith(maturity: new DateOnly(2027, 1, 1)), Period(2027, 1, 1, interest: 20m));
        var bridge = BuildBridge(CashFlowServiceWith(projection));
        var whole = new DomainLedger();
        var remainder = new DomainLedger();

        await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, whole,
            new AmortizationLedgerPostingContext(OpenLots: [Lot(quantity: 10m, unitCost: 102m)]));
        await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, remainder,
            new AmortizationLedgerPostingContext(OpenLots: [Lot(quantity: 10m, unitCost: 102m)],
                AmortizedThrough: new DateOnly(2026, 7, 2)));

        whole.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-20m);
        remainder.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-10.03m);
        remainder.Journal.Should().OnlyContain(entry => entry.IsBalanced);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_ProjectionAsOf_RequestsTheProjectionForThatInstant()
    {
        var asOf = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var service = Substitute.For<ISecurityMasterCashFlowService>();
        service.GetProjectionAsOfAsync(SecurityId, StructuredCashFlowScenario.Base, asOf, Arg.Any<CancellationToken>())
            .Returns(Projection(Period(2026, 3, 31, interest: 30m)));
        var ledger = new DomainLedger();

        var posted = await BuildBridge(service).PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger,
            new AmortizationLedgerPostingContext(ProjectionAsOf: asOf));

        posted.Should().Be(1);
        await service.DidNotReceive().GetProjectionAsync(SecurityId, StructuredCashFlowScenario.Base, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_AccrualWindow_AccruesEachMonthsShareOfASemiAnnualCoupon()
    {
        // A 181-day coupon period of 1,810 accrues 10 a day: 310 for January, 280 for February,
        // and the six monthly windows together recognize the coupon exactly once.
        var period = Period(2026, 6, 30, interest: 1_810m) with
        {
            AccrualStart = new DateOnly(2025, 12, 31),
            AccrualEnd = new DateOnly(2026, 6, 30)
        };
        var bridge = BuildBridge(CashFlowServiceWith(Projection(TermsWith(maturity: new DateOnly(2026, 6, 30)), period)));
        var monthly = new List<decimal>();

        for (var month = 1; month <= 6; month++)
        {
            var start = new DateOnly(2026, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var ledger = new DomainLedger();
            var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger,
                new AmortizationLedgerPostingContext(AccrualWindow: new AmortizationAccrualWindow(start, end)));
            posted.Should().Be(1);
            ledger.Journal.Should().ContainSingle().Which.Metadata.EffectiveDate.Should().Be(end);
            monthly.Add(ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)));
        }

        monthly.Should().Equal(310m, 280m, 310m, 300m, 310m, 300m);
        monthly.Sum().Should().Be(1_810m);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_AccrualWindow_AmortizesOnlyTheWindowsDays()
    {
        // 365-day life from 2026-01-01 with 20 of premium; February is 28 of those days.
        var period = Period(2027, 1, 1, interest: 0m);
        var bridge = BuildBridge(CashFlowServiceWith(Projection(TermsWith(maturity: new DateOnly(2027, 1, 1)), period)));
        var ledger = new DomainLedger();

        await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, new AmortizationLedgerPostingContext(
            OpenLots: [Lot(quantity: 10m, unitCost: 102m)],
            AccrualWindow: new AmortizationAccrualWindow(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))));

        // Cumulative 3.18 through 28 February less 1.64 through 31 January.
        ledger.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-1.54m);
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_ReadsLotFaceFromTheSharedLotShape()
    {
        // The same 1,000,000 face at a 2-point premium, booked once as a bond (10,000 units of 100
        // of par at 102) and once one unit per currency unit of face (as a loan, deposit or CD
        // would be, at 1.02). Both must earn the same coupon share and amortize the same premium.
        var period = Period(2026, 6, 30, interest: 1_810m) with
        {
            AccrualStart = new DateOnly(2025, 12, 31),
            AccrualEnd = new DateOnly(2026, 6, 30)
        };
        var bridge = BuildBridge(CashFlowServiceWith(Projection(TermsWith(maturity: new DateOnly(2026, 6, 30)), period)));
        var perHundred = Lot(quantity: 10_000m, unitCost: 102m, year: 2025, month: 12, day: 31);
        var perUnit = new LedgerTaxLot("lot-1", new DateOnly(2025, 12, 31), 1_000_000m, 1.02m, SecurityId)
        {
            UnitKind = SecurityLotUnitKind.FaceValue,
            Multiplier = 1m
        };

        async Task<DomainLedger> PostAsync(LedgerTaxLot lot)
        {
            var ledger = new DomainLedger();
            await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, ledger, new AmortizationLedgerPostingContext(
                OpenLots: [lot],
                AccrualWindow: new AmortizationAccrualWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
                CouponPrincipalBasis: 2_000_000m));
            return ledger;
        }

        var bond = await PostAsync(perHundred);
        var loan = await PostAsync(perUnit);

        // Half the security's face is held, so January earns half of 310.
        bond.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(155m);
        loan.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(155m);
        loan.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(bond.GetBalance(LedgerAccounts.Securities(Ticker)));
        bond.GetBalance(LedgerAccounts.Securities(Ticker)).Should().BeNegative();
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_RefusesALotThatIsNotFaceDenominated()
    {
        var period = Period(2026, 6, 30, interest: 1_810m) with
        {
            AccrualStart = new DateOnly(2025, 12, 31),
            AccrualEnd = new DateOnly(2026, 6, 30)
        };
        var bridge = BuildBridge(CashFlowServiceWith(Projection(TermsWith(maturity: new DateOnly(2026, 6, 30)), period)));
        var units = new LedgerTaxLot("lot-1", new DateOnly(2025, 12, 31), 100m, 10m, SecurityId)
        {
            UnitKind = SecurityLotUnitKind.Units,
            Multiplier = 1m
        };

        var act = () => bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, new DomainLedger(), new AmortizationLedgerPostingContext(
            OpenLots: [units],
            AccrualWindow: new AmortizationAccrualWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CouponPrincipalBasis: 1_000_000m));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not face value*");
    }

    [Fact]
    public async Task PostProjectedCashFlowsAsync_BlockedProjectionWithNoRows_IsReportedAsBlocked()
    {
        var blocked = Projection() with { BlockedReason = "Negative coupon rate." };
        var logger = new RecordingLogger();
        var bridge = new SecurityMasterAmortizationLedgerBridge(CashFlowServiceWith(blocked), logger);

        var posted = await bridge.PostProjectedCashFlowsAsync(SecurityId, Ticker, new DomainLedger());

        posted.Should().Be(0);
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("Negative coupon rate.", StringComparison.Ordinal));
    }

    private sealed class RecordingLogger : ILogger<SecurityMasterAmortizationLedgerBridge>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
