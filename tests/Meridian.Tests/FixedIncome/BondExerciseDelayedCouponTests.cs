using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondExerciseDelayedCouponTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);

    [Theory]
    [InlineData("Call", "2026-07-15")]
    [InlineData("Call", "2026-07-16")]
    [InlineData("Put", "2026-07-15")]
    [InlineData("Put", "2026-07-16")]
    public async Task ExerciseDuringEarnedUnpaidCouponWindow_BlocksInsteadOfDroppingCash(string kind, string exerciseDate)
    {
        // July 15 is the accrual end; the retained two-business-day payment lag pays July 17.
        var security = Bond(kind, exerciseDate);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);

        var result = (await new BondAnalyticsService(query, Calendars).CalculateAsync(
            new(security.SecurityId, new(AsOf), Yield: 0.05m)))!;

        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("ExerciseScheduleRefused");
        result.Blockers[0].Reason.Should().Contain("earned but unpaid coupon");
        result.DirtyPrice.Should().BeNull();
        result.ExerciseYields.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Call")]
    [InlineData("Put")]
    public void ExerciseOnDelayedPaymentDate_KeepsCouponAndRedemptionOnIdenticalDiscountTime(string kind)
    {
        var security = Bond(kind, "2026-07-17");
        var (terms, declared, schedule, flows) = ContractualInputs(security);

        BondExerciseEconomics.TryResolveScenarios(security, terms, declared, schedule,
            DateOnly.FromDateTime(AsOf.UtcDateTime), 0.05m, BondYieldConvention.NominalPeriodic,
            2, flows, Calendars, out var scenarios, out var reason).Should().BeTrue(reason);

        var coupon = flows.Single(flow => flow.PaymentDate == new DateOnly(2026, 7, 17));
        coupon.Interest.Should().Be(2.5m);
        coupon.TimeInYears.Should().Be(0.5m + 2m / 365m);
        var exercise = scenarios.Should().ContainSingle().Subject;
        exercise.TimeInYears.Should().Be(coupon.TimeInYears);
        exercise.AccruedPer100.Should().Be(0m, "the full coupon is separately paid on the exercise date");
        exercise.RedemptionPrice.Should().Be(101m);
    }

    [Theory]
    [InlineData("Call")]
    [InlineData("Put")]
    public void ExerciseWithinAccrualPeriod_StillIncludesContractualAccruedInterest(string kind)
    {
        var security = Bond(kind, "2026-04-15");
        var (terms, declared, schedule, flows) = ContractualInputs(security);

        BondExerciseEconomics.TryResolveScenarios(security, terms, declared, schedule,
            DateOnly.FromDateTime(AsOf.UtcDateTime), 0.05m, BondYieldConvention.NominalPeriodic,
            2, flows, Calendars, out var scenarios, out var reason).Should().BeTrue(reason);

        var exercise = scenarios.Should().ContainSingle().Subject;
        exercise.TimeInYears.Should().Be(0.25m);
        exercise.AccruedPer100.Should().Be(1.25m, "90/180 of a 2.5-per-100 semiannual coupon has accrued");
        BondExerciseEconomics.RedemptionCash(100m, exercise.RedemptionPrice, exercise.AccruedPer100)
            .Should().Be(102.25m);
    }

    private static (StructuredCashFlowTerms Terms, DeclaredScheduleResolution Declared,
        ScheduleGenerationResult Schedule, IReadOnlyList<BondAnalyticsCashFlow> Flows) ContractualInputs(SecurityDetailDto security)
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var declared = ScheduleSpecFactory.Resolve(security, terms, Calendars);
        declared.Status.Should().Be(DeclaredScheduleStatus.Declared);
        var schedule = ScheduleGenerator.Generate(declared.Spec!);
        schedule.Succeeded.Should().BeTrue();
        BondContractualEconomics.TryBuild(security.SecurityId, terms, declared, schedule,
            DateOnly.FromDateTime(AsOf.UtcDateTime), 0.05m, false, BondYieldConvention.NominalPeriodic,
            out var flows, out _, out var reason).Should().BeTrue(reason);
        return (terms, declared, schedule, flows);
    }

    private static SecurityDetailDto Bond(string kind, string exerciseDate)
    {
        var payload = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 3,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponType"] = "Fixed",
            ["couponRate"] = 5m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["isCallable"] = kind == "Call",
            ["scheduleConventions"] = new
            {
                accrualAdjusted = false,
                couponRateUnit = "Percent",
                couponCashBasis = "Level",
                paymentLagBusinessDays = 2,
                paymentCalendars = new[] { "WEEKENDS" }
            }
        };
        var prefix = kind == "Call" ? "call" : "put";
        payload[prefix + "Schedule"] = new[]
        {
            new Dictionary<string, object?>
            {
                [prefix + "Date"] = exerciseDate, [prefix + "Price"] = 101m,
                ["exerciseStyle"] = "European", ["priceBasis"] = "PercentOfPar"
            }
        };
        if (kind == "Call")
            payload["callDate"] = exerciseDate;
        return new(Guid.NewGuid(), "Bond", SecurityStatusDto.Active, "Delayed-coupon bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(payload), [], [], 1, AsOf, null);
    }
}
