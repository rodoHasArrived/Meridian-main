using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Xunit;
using System.Text.Json;

namespace Meridian.Contracts.UnitTests;

public sealed class ContractualCashFlowEngineTests
{
    private static readonly DateOnly Issue = new(2026, 1, 1);
    private static readonly DateOnly Maturity = new(2027, 1, 1);
    private static readonly ContractualCashFlowRequest Request = new(Issue, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private static ContractualCashFlowStream Stream => new("main", 1000, 3, DayCountConvention.Actual365, .1m);
    private static ContractualCashFlowContract Contract(ContractualCashFlowStream? stream = null) => new(Issue, Maturity, "USD", [stream ?? Stream]);

    [Fact]
    public void Bullet_conserves_principal_and_actual365_coupon_cash()
    {
        var result = ContractualCashFlowEngine.Calculate(Contract(), Request);
        result.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.ScheduledPrincipal).Should().Be(1000);
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(100, .00000002m);
        result.Events[^1].ClosingBalance.Should().Be(0);
        result.IsPostable.Should().BeFalse();
    }

    [Fact]
    public void Midperiod_asof_preserves_the_full_unpaid_coupon()
    {
        var full = ContractualCashFlowEngine.Calculate(Contract(), Request);
        var mid = ContractualCashFlowEngine.Calculate(Contract(), Request with { ValuationDate = Issue.AddDays(45) });
        mid.Events.Should().BeEquivalentTo(full.Events);
    }

    [Fact]
    public void Interior_principal_and_rate_changes_split_interest_before_reduction()
    {
        var stream = Stream with
        {
            PaymentMonths = 0,
            PrincipalSchedule = [new(Issue.AddDays(100), 400)],
            RateSteps = [new(Issue.AddDays(200), .2m)]
        };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request);
        var expected = 1000m * .1m * 100 / 365 + 600m * .1m * 100 / 365 + 600m * .2m * 165 / 365;
        result.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(expected, .00000002m);
        result.Events.Sum(e => e.ScheduledPrincipal).Should().Be(1000);
    }

    [Fact]
    public void Cash_pik_split_capitalizes_before_next_period()
    {
        var stream = Stream with { DayCount = DayCountConvention.Thirty360, PikFraction = .5m };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request);
        result.Events[0].CashInterest.Should().Be(12.5m);
        result.Events[0].CapitalizedInterest.Should().Be(12.5m);
        result.Events[1].CashInterest.Should().Be(12.65625m);
        result.Events.Sum(e => e.ScheduledPrincipal).Should().Be(1000 + result.Events.Sum(e => e.CapitalizedInterest));
    }

    [Fact]
    public void Swap_notional_step_without_exchange_changes_balance_without_principal_cash()
    {
        var stream = Stream with { ExchangesPrincipal = false, NotionalSchedule = [new(Issue.AddDays(100), 500)] };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request);
        result.Events.Sum(e => e.ScheduledPrincipal).Should().Be(0);
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(1000m * .1m * 100 / 365 + 500m * .1m * 265 / 365, .00000002m);
    }

    [Fact]
    public void Cpr_over_a_year_matches_the_annual_assumption()
    {
        var assumption = new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Cpr, .12m);
        ContractualCashFlowEngine.PrepaymentFraction(assumption, Issue, Maturity).Should().BeApproximately(.12m, .0000000001m);
        var result = ContractualCashFlowEngine.Calculate(Contract(Stream with { FixedRate = 0, PrepaymentAllowed = true }), Request with { Prepayments = [assumption] });
        result.Events.Sum(e => e.ForecastPrepayment + e.ScheduledPrincipal).Should().BeApproximately(1000, .00000002m);
    }

    [Fact]
    public void Smm_calendar_month_splits_are_multiplicative()
    {
        var assumption = new CashFlowPrepaymentAssumption("main", Issue, Maturity, CashFlowPrepaymentMethod.Smm, .02m);
        ContractualCashFlowEngine.PrepaymentFraction(assumption, Issue, new(2026, 3, 1)).Should().BeApproximately(1m - .98m * .98m, .000000001m);
    }

    [Fact]
    public void Psa_seasons_from_point_two_percent_to_six_percent_annual_cpr()
    {
        var a = new CashFlowPrepaymentAssumption("main", Issue, new(2030, 1, 1), CashFlowPrepaymentMethod.Psa, 1, Issue);
        var first = ContractualCashFlowEngine.PrepaymentFraction(a, Issue, Issue.AddMonths(1));
        var mature = ContractualCashFlowEngine.PrepaymentFraction(a, Issue.AddMonths(30), Issue.AddMonths(31));
        first.Should().BeApproximately(1m - (decimal)Math.Pow(.998, 1d / 12), .000000001m);
        mature.Should().BeApproximately(1m - (decimal)Math.Pow(.94, 1d / 12), .000000001m);
    }

    [Fact]
    public void Zero_prepayment_is_identical_to_contractual_cash()
    {
        var contract = Contract(Stream with { PrepaymentAllowed = true });
        var no = ContractualCashFlowEngine.Calculate(contract, Request);
        var zero = ContractualCashFlowEngine.Calculate(contract, Request with { Prepayments = [new("main", Issue, Maturity, CashFlowPrepaymentMethod.Cpr, 0)] });
        zero.Events.Should().Equal(no.Events);
    }

    [Fact]
    public void Equal_principal_recast_reduces_remaining_installments()
    {
        var contract = Contract(Stream with { FixedRate = 0, PrepaymentAllowed = true, Amortization = CashFlowAmortization.EqualPrincipal });
        var recast = ContractualCashFlowEngine.Calculate(contract, Request with { Prepayments = [new("main", Issue.AddMonths(3), Issue.AddMonths(3).AddDays(1), CashFlowPrepaymentMethod.Amount, 300, Policy: CashFlowRecastPolicy.Recast)] });
        recast.Events[0].ScheduledPrincipal.Should().Be(250);
        recast.Events[0].ForecastPrepayment.Should().Be(300);
        recast.Events[1].ScheduledPrincipal.Should().Be(150);
        recast.Events[^1].ClosingBalance.Should().Be(0);
    }

    [Fact]
    public void Recast_on_arbitrary_principal_schedule_is_rejected()
    {
        var action = () => ContractualCashFlowEngine.Calculate(Contract(Stream with { PrepaymentAllowed = true }),
            Request with { Prepayments = [new("main", Issue, Maturity, CashFlowPrepaymentMethod.Cpr, .1m, Policy: CashFlowRecastPolicy.Recast)] });
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Contractual_principal_overpayment_is_not_silently_capped()
    {
        var action = () => ContractualCashFlowEngine.Calculate(Contract(Stream with { PrincipalSchedule = [new(Issue.AddMonths(3), 1200)] }), Request);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Make_whole_continuation_preserves_installments_after_an_earlier_scenario_prepayment()
    {
        var call = Issue.AddMonths(4);
        var stream = Stream with
        {
            FixedRate = 0,
            PrepaymentAllowed = true,
            PrincipalSchedule = [new(Issue.AddMonths(6), 400), new(Maturity, 600)]
        };
        var contract = Contract(stream) with
        {
            Exercises = [new(CashFlowExerciseSide.Call, call, null, CashFlowExerciseStyle.European, 100, "main",
            MakeWhole: new(MakeWholeFormula.BenchmarkCurvePvV1, "treasury", 0, 2, DayCountConvention.Actual365))]
        };
        var request = Request with
        {
            Prepayments = [new("main", Issue.AddMonths(3), Issue.AddMonths(3).AddDays(1), CashFlowPrepaymentMethod.Amount, 100)],
            Exercises = [new(CashFlowExerciseSide.Call, call, "main", call, call, 900)]
        };
        var result = ContractualCashFlowEngine.Calculate(contract, request, curves: [Curve(SecurityCurveKind.Discount, 1)]);
        result.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.NetCash).Should().Be(1000);
        result.ExerciseCalculations.Single().DirtyConsideration.Should().Be(900);
    }

    [Fact]
    public void American_calendar_and_notice_window_exclude_exercise_day()
    {
        var calendar = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var stream = Stream with { Conventions = ScheduleConventionTerms.Empty with { PaymentCalendars = ["WEEKENDS"] } };
        var contract = Contract(stream) with { Exercises = [new(CashFlowExerciseSide.Call, Issue, null, CashFlowExerciseStyle.American, 101, "main", MinimumNoticeDays: 2, MaximumNoticeDays: 4, NoticeDayType: "Business", PartialRedemption: "WholeOnly")] };
        var exercise = new SelectedCashFlowExercise(CashFlowExerciseSide.Call, Issue, "main", new(2026, 4, 6), new(2026, 4, 1), 1000);
        var result = ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [exercise] }, calendar);
        result.Blockers.Should().BeEmpty();
        var priced = result.ExerciseCalculations.Single();
        priced.NoticeWindow.Should().Be(new CashFlowNoticeWindow(new(2026, 3, 31), new(2026, 4, 2)));
        result.Events.Sum(e => e.RedemptionPremium).Should().Be(10);
        result.Events.Where(e => e.Date > exercise.ExerciseDate).Should().BeEmpty();
        result.ExerciseEligibility.Single().Dates.Should().BeEmpty();
    }

    [Fact]
    public void American_weekend_selection_is_rejected()
    {
        var stream = Stream with { Conventions = ScheduleConventionTerms.Empty with { PaymentCalendars = ["WEEKENDS"] } };
        var contract = Contract(stream) with { Exercises = [new(CashFlowExerciseSide.Call, Issue, null, CashFlowExerciseStyle.American, 100, "main")] };
        var action = () => ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, Issue, "main", new(2026, 4, 4), new(2026, 4, 1), 1000)] }, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Bermudan_without_frequency_uses_contractual_coupon_dates()
    {
        var contract = Contract() with { Exercises = [new(CashFlowExerciseSide.Put, Issue, null, CashFlowExerciseStyle.Bermudan, 100, "main")] };
        var result = ContractualCashFlowEngine.Calculate(contract, Request);
        result.ExerciseEligibility.Single().Dates.Should().Equal(Issue.AddMonths(3), Issue.AddMonths(6), Issue.AddMonths(9), Maturity);
    }

    [Fact]
    public void Ineligible_notice_or_missing_lot_selection_is_rejected()
    {
        var contract = Contract() with { Exercises = [new(CashFlowExerciseSide.Call, Issue.AddMonths(3), null, CashFlowExerciseStyle.European, 100, "main", PartialRedemption: "ByLot", MinimumNoticeDays: 30, NoticeDayType: "Calendar")] };
        var action = () => ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, Issue.AddMonths(3), "main", Issue.AddMonths(3), Issue.AddDays(80), 300)] });
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Coupon_on_exercise_date_is_paid_once_before_redemption()
    {
        var date = Issue.AddMonths(3);
        var contract = Contract() with { Exercises = [new(CashFlowExerciseSide.Call, date, null, CashFlowExerciseStyle.European, 100, "main")] };
        var result = ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, date, "main", date, date, 1000)] });
        result.Events.Should().ContainSingle();
        result.Events[0].CashInterest.Should().BeApproximately(1000m * .1m * 90 / 365, .00000001m);
        result.ExerciseCalculations[0].AccruedInterest.Should().Be(0);
    }

    [Theory]
    [InlineData(MakeWholeFormula.BenchmarkCurvePvV1)]
    [InlineData(MakeWholeFormula.BenchmarkYieldPvV1)]
    public void Make_whole_zero_discount_matches_remaining_contractual_cash(MakeWholeFormula formula)
    {
        var date = Issue.AddMonths(3);
        var mw = new ContractualMakeWholeTerms(formula, "treasury", 0, 2, DayCountConvention.Actual365);
        var contract = Contract() with { Exercises = [new(CashFlowExerciseSide.Call, date, null, CashFlowExerciseStyle.European, 100, "main", MakeWhole: mw)] };
        var curves = new[] { Curve(formula == MakeWholeFormula.BenchmarkCurvePvV1 ? SecurityCurveKind.Discount : SecurityCurveKind.Forward, formula == MakeWholeFormula.BenchmarkCurvePvV1 ? 1m : 0m) };
        var result = ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, date, "main", date, date, 1000)] }, curves: curves);
        result.Blockers.Should().BeEmpty();
        result.ExerciseCalculations[0].DirtyConsideration.Should().BeApproximately(1000m + 1000m * .1m * 275 / 365, .00000003m);
    }

    [Fact]
    public void Missing_make_whole_benchmark_blocks_the_entire_projection()
    {
        var date = Issue.AddMonths(3);
        var contract = Contract() with { Exercises = [new(CashFlowExerciseSide.Call, date, null, CashFlowExerciseStyle.European, 100, "main", MakeWhole: new(MakeWholeFormula.BenchmarkCurvePvV1, "missing", 0, 2, DayCountConvention.Actual365))] };
        var result = ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, date, "main", date, date, 1000)] });
        result.Events.Should().BeEmpty();
        result.Blockers.Single().Code.Should().Be("BenchmarkUnavailable");
    }

    [Fact]
    public void Fixed_cash_coupon_schedule_uses_explicit_amounts()
    {
        var stream = Stream with { FixedRate = null, CouponAmounts = Enumerable.Range(1, 4).Select(i => new ContractualCouponAmount(Issue.AddMonths(i * 3), i * 10m)).ToArray() };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request);
        result.Events.Select(e => e.CashInterest).Should().Equal(10m, 20m, 30m, 40m);
    }

    [Fact]
    public void Explicit_period_gap_is_validation_failure()
    {
        var stream = Stream with { CouponPeriods = [new(1, Issue.AddDays(1), Maturity, Issue, Maturity, Maturity, SchedulePeriodKind.Term, [], true)] };
        var action = () => ContractualCashFlowEngine.Calculate(Contract(stream), Request);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Replay_is_independent_of_current_calendars_and_curves()
    {
        var contract = Contract(Stream with { Conventions = ScheduleConventionTerms.Empty with { PaymentCalendars = ["WEEKENDS"], BusinessDayConvention = "Following" } });
        var result = ContractualCashFlowEngine.Calculate(contract, Request, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        var run = new ContractualCashFlowRun(Guid.NewGuid(), "Security", Guid.NewGuid(), Request.KnowledgeCutoff, contract, Request, result);
        var replayed = ContractualCashFlowEngine.Replay(run);
        replayed.Events.Should().Equal(result.Events);
        replayed.Evidence.InputHash.Should().Be(result.Evidence.InputHash);
        replayed.Evidence.OutputHash.Should().Be(result.Evidence.OutputHash);
    }

    [Theory]
    [InlineData("Bond", 4)]
    [InlineData("CertificateOfDeposit", 4)]
    [InlineData("StructuredCredit", 5)]
    [InlineData("Swap", 3)]
    [InlineData("DirectLoan", 2)]
    public void Companion_is_readable_only_when_its_document_declares_the_new_version(string assetClass, int version)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var payload = JsonSerializer.SerializeToElement(new { assetClassTermsVersion = version, cashFlowContract = Contract() }, json);
        var security = new SecurityDetailDto(Guid.NewGuid(), assetClass, SecurityStatusDto.Active, "Test", "USD",
            JsonSerializer.SerializeToElement(new { }), payload, [], [], 1, Request.KnowledgeCutoff, null);
        SecurityContractualCashFlowTerms.Read(security)!.Streams[0].Principal.Should().Be(1000);
        var old = JsonSerializer.SerializeToElement(new { assetClassTermsVersion = 1, cashFlowContract = Contract() }, json);
        SecurityContractualCashFlowTerms.Read(security with { AssetSpecificTerms = old }).Should().BeNull();
        var write = () => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, payload);
        write.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Independent_make_whole_mid_coupon_example_includes_accrued_exactly_once()
    {
        var date = Issue.AddDays(100);
        var contract = Contract(Stream with { PaymentMonths = 0 }) with
        {
            Exercises = [new(CashFlowExerciseSide.Call, date, null, CashFlowExerciseStyle.European, 100, "main",
                MakeWhole: new(MakeWholeFormula.BenchmarkCurvePvV1, "treasury", 0, 2, DayCountConvention.Actual365))]
        };
        var result = ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, date, "main", date, date, 1000)] }, curves: [Curve(SecurityCurveKind.Discount, 1)]);
        result.ExerciseCalculations.Single().DirtyConsideration.Should().BeApproximately(1100, .00000002m);
        result.Events.Sum(e => e.NetCash).Should().BeApproximately(1100, .00000002m);
        result.ExerciseCalculations.Single().AccruedInterest.Should().BeApproximately(1000m * .1m * 100 / 365, .00000001m);
    }

    [Fact]
    public void Independent_make_whole_floor_binds_under_high_yield()
    {
        var date = Issue.AddMonths(3);
        var contract = Contract() with
        {
            Exercises = [new(CashFlowExerciseSide.Call, date, null, CashFlowExerciseStyle.European, 102, "main",
            MakeWhole: new(MakeWholeFormula.BenchmarkYieldPvV1, "treasury", 0, 2, DayCountConvention.Actual365))]
        };
        var result = ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, date, "main", date, date, 1000)] }, curves: [Curve(SecurityCurveKind.Forward, .5m)]);
        result.ExerciseCalculations.Single().DirtyConsideration.Should().Be(1020);
        result.ExerciseCalculations.Single().DirtyPresentValue.Should().BeLessThan(1020);
    }

    [Fact]
    public void Independent_make_whole_partial_redemption_scales_contractual_continuation()
    {
        var date = Issue.AddMonths(3);
        var contract = Contract() with
        {
            Exercises = [new(CashFlowExerciseSide.Call, date, null, CashFlowExerciseStyle.European, 100, "main", PartialRedemption: "ProRata",
            MakeWhole: new(MakeWholeFormula.BenchmarkCurvePvV1, "treasury", 0, 2, DayCountConvention.Actual365))]
        };
        var result = ContractualCashFlowEngine.Calculate(contract, Request with { Exercises = [new(CashFlowExerciseSide.Call, date, "main", date, date, 400)] }, curves: [Curve(SecurityCurveKind.Discount, 1)]);
        result.ExerciseCalculations.Single().DirtyConsideration.Should().BeApproximately(400m + 400m * .1m * 275 / 365, .00000002m);
        result.Events.Sum(e => e.RedeemedPrincipal + e.ScheduledPrincipal).Should().Be(1000);
    }

    [Fact]
    public void Explicit_irregular_coupon_periods_use_reference_periods_and_preserve_dates()
    {
        var middle = new DateOnly(2026, 5, 17);
        var stream = Stream with
        {
            CouponPeriods = [
            new(1, Issue, middle, Issue, middle, middle, SchedulePeriodKind.ShortInitialStub, [new(Issue, Issue.AddMonths(6))], false),
            new(2, middle, Maturity, middle, Maturity, Maturity, SchedulePeriodKind.LongFinalStub, [new(Issue.AddMonths(3), Issue.AddMonths(9)), new(Issue.AddMonths(9), Issue.AddMonths(15))], true)]
        };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request);
        result.Blockers.Should().BeEmpty();
        result.Events.Select(e => e.Date).Should().Equal(middle, Maturity);
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(100, .00000002m);
    }

    [Fact]
    public void Floating_future_resets_use_pinned_curve_spread_cap_and_signed_direction()
    {
        var stream = Stream with { FixedRate = null, IndexName = "treasury", SpreadBps = 100, Floor = .01m, Cap = .04m, Direction = CashFlowLegDirection.Pay };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request, curves: [Curve(SecurityCurveKind.Forward, .08m)]);
        result.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(40, .00000002m);
        result.Events.Sum(e => e.NetCash).Should().BeApproximately(-1040, .00000002m);
    }

    [Fact]
    public void Historical_floating_reset_is_not_backfilled_from_a_forecast_curve()
    {
        var stream = Stream with { FixedRate = null, IndexName = "treasury" };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request with { ValuationDate = Issue.AddDays(10) }, curves: [Curve(SecurityCurveKind.Forward, .04m)]);
        result.Events.Should().BeEmpty();
        result.Blockers.Single().Code.Should().Be("HistoricalFixingMissing");
    }

    [Fact]
    public void Benchmark_role_uses_pinned_identity_without_rewriting_stored_curve_metadata()
    {
        var curve = Curve(SecurityCurveKind.Forward, .04m);
        curve = curve with { Definition = curve.Definition with { CurveKey = "USD-SOFR-3M" }, Version = curve.Version with { CurveKey = "USD-SOFR-3M" } };
        var request = Request with { CurvePins = [new("SOFR", curve.Definition.CurveId, curve.Version.CurveVersionId)] };
        var result = ContractualCashFlowEngine.Calculate(Contract(Stream with { FixedRate = null, IndexName = "SOFR" }), request, curves: [curve]);
        result.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(40m, .00000002m);
        result.Evidence.Curves.Single().Definition.CurveKey.Should().Be("USD-SOFR-3M");
    }

    [Fact]
    public void Coupon_lag_does_not_extend_accrual_or_erase_an_unpaid_coupon_after_payoff()
    {
        var stream = Stream with
        {
            Conventions = ScheduleConventionTerms.Empty with { PaymentCalendars = ["WEEKENDS"], PaymentLagBusinessDays = 2 },
            PrincipalSchedule = [new(Issue.AddMonths(3).AddDays(1), 1000)]
        };
        var result = ContractualCashFlowEngine.Calculate(Contract(stream), Request, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        result.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(1000m * .1m * 91 / 365, .00000002m);
        result.Events.Should().Contain(e => e.Date == new DateOnly(2026, 4, 3) && e.CashInterest > 0);
    }

    [Fact]
    public void Joint_calendar_replay_preserves_all_stream_calendar_queries()
    {
        var a = new HolidayBusinessDayCalendar("A", 1);
        var b = new HolidayBusinessDayCalendar("B", 1);
        var stream = Stream with { Conventions = ScheduleConventionTerms.Empty with { PaymentCalendars = ["B", "A"], BusinessDayConvention = "Following" } };
        var contract = Contract(stream) with { Streams = [stream, stream with { StreamId = "second", PaymentMonths = 6 }] };
        var result = ContractualCashFlowEngine.Calculate(contract, Request, new StaticBusinessDayCalendarSource([a, b]));
        var run = new ContractualCashFlowRun(Guid.NewGuid(), "Security", Guid.NewGuid(), Request.KnowledgeCutoff, contract, Request, result);
        ContractualCashFlowEngine.Verify(run).Should().BeTrue();
    }

    private static CashFlowCurveSnapshot Curve(SecurityCurveKind kind, decimal value)
    {
        var id = Guid.NewGuid();
        var definition = new SecurityCurveDefinitionDto(id, "treasury", kind, "USD", null, null, "Actual365", SecurityCurveInterpolation.Linear, "test", null, "test", Request.KnowledgeCutoff);
        var version = new SecurityCurveVersionDto(Guid.NewGuid(), id, "treasury", 1, Issue, Request.KnowledgeCutoff, null, "test", "test", [new(Issue, value), new(Maturity, value)], "fixture");
        return new(definition, version);
    }
}
