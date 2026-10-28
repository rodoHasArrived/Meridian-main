using System.Globalization;
using System.Reflection;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using static Meridian.Tests.SecurityMaster.Schedules.ScheduleParityGrids;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Proves the frozen copies in <see cref="LegacyScheduleOracles"/> of the Security Master,
/// asset-operations, obligation-timeline, fund-structure and <c>FaceValueLot</c> generators still
/// behave like the production generators they were copied from, by invoking those generators through
/// reflection on a
/// deterministic 5,000-case sample of each parity grid and, for all but <c>FaceValueLot</c>, at the end
/// of the date range, where the legacy month and day arithmetic threw. The direct-lending copies are
/// proven against the kernel only, until their generators move. A generator that delegates to the kernel is checked the same
/// way, so its check also proves kernel equals oracle through the production method: the Security
/// Master period roll (increment 1), and the asset-operations bond roll, the obligation-timeline steps
/// and the fund-structure forecast roll (increment 3). The asset-operations roll has no date-only
/// method, so its check reads the coupon periods back from
/// <c>AssetOperationsProjectionBuilder.ProjectFixedIncomeCashFlows</c>, which both of its production
/// paths run. The coupon cadence reader, merged into the public
/// <see cref="SecurityTermConventions.LegacyCouponPaymentsPerYear"/>, is called directly.
/// </summary>
public sealed class LegacyOracleFidelityTests
{
    // Prime strides walk every combination of the grids' inner offsets.
    private const int ForwardStride = 31;
    private const int FaceValueLotStride = 23;

    // Coprime to the fund-structure grid's 1,760 cases per anchor, and wide enough to reach 2024-02-29.
    private const int FundStructureStride = 97;

    private static readonly Guid SecurityId = Guid.Parse("6f1d3c2a-5b4e-4d7f-9a81-0c2e4b6d8f10");
    private static readonly Guid ProjectionRunId = Guid.Parse("6f1d3c2a-5b4e-4d7f-9a81-0c2e4b6d8f11");

    // The obligation timeline's frequency spellings: blank, named, period counts and unknown.
    private static readonly string?[] ObligationSpellings =
    [
        null, "", "  ", "Monthly", "monthly", "Quarterly", "Semi-Annual", "semi_annually", "SemiYearly", "Half-Yearly",
        "Annual", "annually", "Yearly", "Bullet", "12", "4", "2", "1", "5", "24", "0", "-4", "weekly", "3M",
    ];

    [Fact]
    public void SmBuildPaymentPeriods_MatchesTheIssueAnchoredOracle_OnTheSample()
    {
        var buildPaymentPeriods = PrivateStatic<Func<DateOnly, DateOnly, int, IEnumerable<(DateOnly AccrualStart, DateOnly PaymentDate)>>>(
            typeof(SecurityMasterCashFlowService),
            "BuildPaymentPeriods");
        var sample = Sample(ForwardCases(), ForwardStride);

        var mismatches = sample
            .Where(c => !buildPaymentPeriods(c.Start, c.Maturity, c.PeriodMonths)
                .SequenceEqual(LegacyScheduleOracles.SmIssueAnchored(c.Start, c.Maturity, c.PeriodMonths)))
            .Take(20)
            .ToArray();

        sample.Should().HaveCount(FidelitySampleSize);
        sample.Select(static c => c.PeriodMonths).Distinct().Should().BeEquivalentTo(PeriodMonths);
        mismatches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(2020, 1, 15, 9999, 12, 31, 3, true)]
    [InlineData(2020, 1, 15, 9999, 10, 15, 3, false)]
    [InlineData(2023, 6, 30, 9999, 12, 31, 12, true)]
    [InlineData(2023, 12, 31, 9999, 12, 31, 12, false)]
    [InlineData(2023, 6, 1, 9999, 12, 31, 1, true)]
    public void SmBuildPaymentPeriods_FailsExactlyWhereTheOracleFails_AtTheEndOfTheDateRange(
        int issueYear, int issueMonth, int issueDay, int maturityYear, int maturityMonth, int maturityDay, int periodMonths, bool legacyThrows)
    {
        var buildPaymentPeriods = PrivateStatic<Func<DateOnly, DateOnly, int, IEnumerable<(DateOnly AccrualStart, DateOnly PaymentDate)>>>(
            typeof(SecurityMasterCashFlowService),
            "BuildPaymentPeriods");
        var issue = new DateOnly(issueYear, issueMonth, issueDay);
        var maturity = new DateOnly(maturityYear, maturityMonth, maturityDay);

        Func<object> oracle = () => LegacyScheduleOracles.SmIssueAnchored(issue, maturity, periodMonths).ToArray();
        Func<object> production = () => buildPaymentPeriods(issue, maturity, periodMonths).ToArray();

        if (legacyThrows)
        {
            oracle.Should().Throw<ArgumentOutOfRangeException>("the legacy loop evaluates a closing grid date past DateOnly.MaxValue");
            production.Should().Throw<ArgumentOutOfRangeException>("the wired kernel keeps the legacy failure (SCH-DEF-010)");
        }
        else
        {
            production().Should().BeEquivalentTo(oracle(), options => options.WithStrictOrdering());
        }
    }

    [Fact]
    public void LegacyCouponPaymentsPerYear_MatchesTheCadenceOracle()
    {
        string?[] spellings =
        [
            null, "", " ", "\t", "Monthly", "BiMonthly", "Quarterly", "quarterly", "SemiAnnual", "Semi-Annual",
            "semi_annually", "semi-annually", "HALF_YEARLY", "half-yearly", "Annual", "Yearly", "AtMaturity", "Bullet",
            "Zero coupon", "Daily", "Weekly", "BiWeekly", "fortnightly", "1M", "2M", "3M", "4M", "6M", "12M", "1Y", "2W",
            "1D", "Irregular", "unknown", "+5", " 4 ", "00004", "1.5", "1.2", "99999999999",
        ];

        var cases = spellings
            .Concat(Enumerable.Range(-2, 400).Select(static value => value.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        cases.Select(SecurityTermConventions.LegacyCouponPaymentsPerYear)
            .Should().Equal(cases.Select(LegacyScheduleOracles.LegacyCouponPaymentsPerYear));
    }

    [Fact]
    public void AssetOpsProjectFixedIncomeCashFlows_MatchesTheCompoundedOracle_OnTheSample()
    {
        var project = AssetOpsProjector();
        var sample = Sample(ForwardCases(), ForwardStride);

        var mismatches = sample
            .Where(c => CouponPeriods(project(AssetOpsBond(c.Start, c.Maturity, c.PeriodMonths), ProjectionRunId), c.Maturity) is not { } periods
                || !periods.SequenceEqual(LegacyScheduleOracles.AssetOpsCompounded(c.Start, c.Maturity, c.PeriodMonths)))
            .Take(20)
            .ToArray();

        sample.Should().HaveCount(FidelitySampleSize);
        sample.Select(static c => c.PeriodMonths).Distinct().Should().BeEquivalentTo(PeriodMonths);
        mismatches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(9999, 1, 31, 9999, 12, 31, 1, true)]
    [InlineData(9999, 1, 28, 9999, 12, 28, 1, false)]
    [InlineData(9999, 1, 31, 9999, 12, 31, 3, true)]
    [InlineData(9999, 10, 31, 9999, 12, 31, 2, false)]
    [InlineData(9999, 6, 30, 9999, 12, 31, 6, true)]
    [InlineData(9999, 7, 1, 9999, 12, 31, 4, true)]
    [InlineData(9999, 7, 1, 9999, 11, 1, 4, false)]
    [InlineData(9998, 12, 31, 9999, 12, 31, 12, false)]
    [InlineData(9999, 12, 30, 9999, 12, 31, 1, true)]
    [InlineData(9999, 12, 31, 9999, 12, 31, 1, false)]
    [InlineData(9999, 12, 31, 9999, 12, 30, 1, false)]
    [InlineData(2023, 6, 30, 9999, 12, 31, 12, true)]
    [InlineData(2023, 12, 31, 9999, 12, 31, 12, false)]
    public void AssetOpsProjectFixedIncomeCashFlows_FailsExactlyWhereTheOracleFails_AtTheEndOfTheDateRange(
        int issueYear, int issueMonth, int issueDay, int maturityYear, int maturityMonth, int maturityDay, int periodMonths, bool legacyThrows)
    {
        // Compounding 9999-01-31 monthly drifts to the 28th, so the closing period starts on
        // 9999-12-28 and the walk throws where the issue-anchored one lands on maturity. The
        // 2023-12-31 annual case runs 7,976 periods, past the kernel's default 1,200-period limit, so
        // it also pins that the compounded factory sets no limit.
        var project = AssetOpsProjector();
        var issue = new DateOnly(issueYear, issueMonth, issueDay);
        var maturity = new DateOnly(maturityYear, maturityMonth, maturityDay);

        Func<object> oracle = () => LegacyScheduleOracles.AssetOpsCompounded(issue, maturity, periodMonths);
        Func<object> production = () => project(AssetOpsBond(issue, maturity, periodMonths), ProjectionRunId);

        if (legacyThrows)
        {
            oracle.Should().ThrowExactly<ArgumentOutOfRangeException>("the legacy loop steps the closing period's start past DateOnly.MaxValue");
            production.Should().ThrowExactly<ArgumentOutOfRangeException>("the wired kernel keeps the legacy failure (SCH-DEF-010)");
        }
        else
        {
            CouponPeriods(project(AssetOpsBond(issue, maturity, periodMonths), ProjectionRunId), maturity)
                .Should().Equal(LegacyScheduleOracles.AssetOpsCompounded(issue, maturity, periodMonths));
        }
    }

    [Fact]
    public void AssetOpsProjectFixedIncomeCashFlows_AnEarlierPeriodFailureStillWins_OverTheRangeEndFailure()
    {
        // 9999-01-31 monthly to 9999-12-31 fails at the range end (SCH-DEF-010), but a principal of
        // decimal.MaxValue at a 200% coupon overflows in the first period's coupon first. The legacy
        // loop threw OverflowException there; evaluating the closing grid date before walking the
        // periods would throw ArgumentOutOfRangeException instead.
        var project = AssetOpsProjector();
        var plain = AssetOpsBond(new DateOnly(9999, 1, 31), new DateOnly(9999, 12, 31), 1);
        var bond = plain with
        {
            Lifecycle = plain.Lifecycle! with { Par = decimal.MaxValue },
            AccrualConvention = plain.AccrualConvention! with { FixedCouponRate = 200m },
        };

        Func<object> production = () => project(bond, ProjectionRunId);

        production.Should().ThrowExactly<OverflowException>("the legacy loop failed in the first period before it reached the range end");
    }

    [Fact]
    public void ObligationSteps_MatchTheirOracles_OnEveryForwardStart()
    {
        var steps = ObligationSteps.Bind();
        int[] asOfOffsets = [-1, 0, 1, 30, 365, 1000];
        var mismatches = new List<string>();
        var cases = 0;
        for (var asOf = ForwardStartFrom; asOf <= ForwardStartTo; asOf = asOf.AddDays(1))
        {
            Compare($"next month after {Format(asOf)}", LegacyScheduleOracles.ObligationNextMonthlyDate(asOf), steps.NextMonthly(asOf));
            Compare($"next quarter after {Format(asOf)}", LegacyScheduleOracles.ObligationNextQuarterDate(asOf), steps.NextQuarter(asOf));
            foreach (var spelling in ObligationSpellings)
            {
                Compare($"'{spelling}' months", LegacyScheduleOracles.ObligationNormalizeFrequencyMonths(spelling), steps.NormalizeFrequencyMonths(spelling));
                Compare($"'{spelling}' step from {Format(asOf)}", LegacyScheduleOracles.ObligationNextFrequencyDate(asOf, spelling), steps.NextFrequency(asOf, spelling));
            }

            foreach (var months in PeriodMonths)
            {
                foreach (var offset in asOfOffsets)
                {
                    var after = asOf.AddDays(offset);
                    Compare(
                        $"{months}M from {Format(asOf)} after {Format(after)}",
                        LegacyScheduleOracles.ObligationAdvanceAfter(asOf, after, months),
                        steps.AdvanceAfter(asOf, after, months));
                }
            }
        }

        cases.Should().Be(ForwardStartCount * (2 + (2 * ObligationSpellings.Length) + (PeriodMonths.Length * asOfOffsets.Length)));
        mismatches.Should().BeEmpty("the obligation-timeline step functions must reproduce their frozen copies");

        void Compare<T>(string label, T expected, T actual)
        {
            cases++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual) && mismatches.Count < 20)
            {
                mismatches.Add($"{label}: oracle {expected}, production {actual}");
            }
        }
    }

    [Fact]
    public void ObligationSteps_FailExactlyWhereTheirOraclesFail_AtTheEndOfTheDateRange()
    {
        // Every day of the last thirteen months of the date range, where the legacy month arithmetic
        // starts to throw. The statement cadence starts from each day and runs past eight as-of dates.
        // Its failures cannot be isolated through the public seam: it can fail only for an as-of on or
        // after 9999-10-01, and every such read also fails the quarterly review with the same
        // exception type.
        var steps = ObligationSteps.Bind();
        DateOnly[] statementAsOfs =
        [
            new(9999, 6, 30), new(9999, 9, 29), new(9999, 9, 30), new(9999, 10, 1),
            new(9999, 10, 31), new(9999, 11, 30), new(9999, 12, 30), DateOnly.MaxValue,
        ];
        var stepCases = new List<(string Label, Func<DateOnly> Oracle, Func<DateOnly> Production)>();
        var statementCases = new List<(string Label, Func<DateOnly> Oracle, Func<DateOnly> Production)>();

        // DateOnly.MaxValue has no next day, so the sweep walks day numbers up to and including it.
        for (var dayNumber = new DateOnly(9998, 12, 1).DayNumber; dayNumber <= DateOnly.MaxValue.DayNumber; dayNumber++)
        {
            var date = DateOnly.FromDayNumber(dayNumber);
            stepCases.Add(($"next month after {Format(date)}", () => LegacyScheduleOracles.ObligationNextMonthlyDate(date), () => steps.NextMonthly(date)));
            stepCases.Add(($"next quarter after {Format(date)}", () => LegacyScheduleOracles.ObligationNextQuarterDate(date), () => steps.NextQuarter(date)));
            foreach (var spelling in ObligationSpellings)
            {
                stepCases.Add(($"'{spelling}' step from {Format(date)}", () => LegacyScheduleOracles.ObligationNextFrequencyDate(date, spelling), () => steps.NextFrequency(date, spelling)));
            }

            foreach (var asOf in statementAsOfs)
            {
                foreach (var months in PeriodMonths)
                {
                    statementCases.Add((
                        $"{months}M from {Format(date)} after {Format(asOf)}",
                        () => LegacyScheduleOracles.ObligationAdvanceAfter(date, asOf, months),
                        () => steps.AdvanceAfter(date, asOf, months)));
                }
            }
        }

        var stepFailures = AssertSameOutcomes(stepCases);
        var statementFailures = AssertSameOutcomes(statementCases);

        stepCases.Should().HaveCount(10_296);
        stepFailures.Should().Be(3_789, "the sweep must reach every legacy month, quarter and frequency failure");
        statementCases.Should().HaveCount(19_008);
        statementFailures.Should().Be(8_754, "the sweep must reach every legacy statement-cadence failure");
    }

    [Fact]
    public void FundStructureRollProjectedScheduleDates_MatchesTheCompoundedOracle_OnTheSample()
    {
        var roll = FundStructureRoll();
        var sample = Sample(FundStructureCases(), FundStructureStride);

        var mismatches = sample
            .Where(c => !roll(c.Anchor, c.Frequency, c.WindowStart, c.WindowEnd)
                .SequenceEqual(LegacyScheduleOracles.FundStructureCompounded(c.Anchor, c.Frequency, c.WindowStart, c.WindowEnd)))
            .Take(20)
            .ToArray();

        sample.Should().HaveCount(FidelitySampleSize);
        sample.Select(static c => c.Frequency).Distinct().Should().BeEquivalentTo(FundStructureSpellings);
        sample.Should().Contain(static c => c.Anchor == null, "an absent anchor starts the roll on the window's start date");
        sample.Should().Contain(
            static c => LegacyScheduleOracles.FundStructureCompounded(c.Anchor, c.Frequency, c.WindowStart, c.WindowEnd).Count == 256,
            "the sample must reach the 256-date guard");
        mismatches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("weekly", "9999-12-25", "9999-12-21", "9999-12-31", true)]
    [InlineData("daily", "9999-12-30", "9999-12-21", "9999-12-31", false)]
    [InlineData("Daily", "9999-12-31", "9999-12-31", "max", true)]
    [InlineData("monthly", "9999-11-30", "9999-11-01", "9999-12-31", true)]
    [InlineData("quarterly", "9999-09-30", "9999-01-01", "max", true)]
    [InlineData("Semi-Annual", "9999-06-30", "9999-01-01", "9999-12-30", false)]
    [InlineData("semiannual", "9999-07-01", "9999-01-01", "max", true)]
    [InlineData("annual", "9999-01-01", "9999-01-01", "9999-01-02", true)]
    [InlineData("Yearly", "9998-12-31", "9998-01-01", "9999-01-01", false)]
    [InlineData("biweekly", "9999-12-18", "9999-12-01", "max", true)]
    [InlineData("BiMonthly", "9999-12-31", "9999-12-01", "max", false)]
    [InlineData("daily", "9999-04-20", "9999-04-20", "max", true)]
    [InlineData("daily", "9999-04-19", "9999-04-19", "max", false)]
    [InlineData("monthly", "9978-09-30", "9978-09-01", "max", true)]
    [InlineData("monthly", "9978-08-31", "9978-08-01", "max", false)]
    public void FundStructureRollProjectedScheduleDates_FailsExactlyWhereTheOracleFails_AtTheEndOfTheDateRange(
        string frequency, string anchor, string windowStart, string windowEnd, bool legacyThrows)
    {
        // The last four rows sit on the 256-date guard. The 256th step is evaluated although its date is
        // never used, so it throws from 9999-04-20 daily (the 256th date is 9999-12-31) and from
        // 9978-09-30 monthly (which drifts to 9999-12-28), but not one step earlier.
        var roll = FundStructureRoll();
        var anchorDate = ParseDate(anchor);
        var start = LegacyScheduleOracles.FundStructureToDateTimeOffset(ParseDate(windowStart));
        var end = windowEnd == "max" ? DateTimeOffset.MaxValue : LegacyScheduleOracles.FundStructureToDateTimeOffset(ParseDate(windowEnd));

        Func<object> oracle = () => LegacyScheduleOracles.FundStructureCompounded(anchorDate, frequency, start, end).ToArray();
        Func<object> production = () => roll(anchorDate, frequency, start, end).ToArray();

        if (legacyThrows)
        {
            oracle.Should().ThrowExactly<ArgumentOutOfRangeException>("the legacy step passes DateOnly.MaxValue before the window or the guard stops it");
            production.Should().ThrowExactly<ArgumentOutOfRangeException>("the kernel-backed roll keeps the legacy failure (SCH-DEF-011)");
        }
        else
        {
            production().Should().BeEquivalentTo(oracle(), options => options.WithStrictOrdering());
        }
    }

    [Fact]
    public void FaceValueLotRegularCouponDates_MatchesTheRegularOracle_OnTheSample()
    {
        var regularCouponDates = PrivateInstance<Func<FaceValueLot, DateOnly, int, bool, DateOnly[]?>>(typeof(FaceValueLot), "RegularCouponDates");
        var sample = Sample(FaceValueLotCases(), FaceValueLotStride);
        var regular = 0;

        var mismatches = new List<FaceValueLotCase>();
        foreach (var c in sample)
        {
            var actual = regularCouponDates(Lot(c), c.Maturity, c.PaymentsPerYear, false);
            var expected = LegacyScheduleOracles.FaceValueLotRegular(c.AcquiredDate, c.Maturity, c.PaymentsPerYear, throwWhenIrregular: false);
            regular += expected is null ? 0 : 1;
            if (expected is null ? actual is not null : actual is null || !actual.SequenceEqual(expected))
            {
                mismatches.Add(c);
            }
        }

        sample.Should().HaveCount(FidelitySampleSize);
        regular.Should().BePositive("the sample must contain regular schedules");
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void FaceValueLotBackwardCouponDates_MatchesTheBackwardOracle_OnTheSample()
    {
        var backwardCouponDates = PrivateInstance<Func<FaceValueLot, DateOnly, int, DateOnly[]>>(typeof(FaceValueLot), "BackwardCouponDates");
        var sample = Sample(FaceValueLotCases(), FaceValueLotStride);

        var mismatches = sample
            .Where(c => !backwardCouponDates(Lot(c), c.Maturity, c.PaymentsPerYear)
                .SequenceEqual(LegacyScheduleOracles.FaceValueLotBackward(c.AcquiredDate, c.Maturity, c.PaymentsPerYear)))
            .Take(20)
            .ToArray();

        sample.Should().HaveCount(FidelitySampleSize);
        sample.Select(static c => c.PaymentsPerYear).Distinct().Should().BeEquivalentTo(PaymentsPerYear);
        mismatches.Should().BeEmpty();
    }

    private static FaceValueLot Lot(FaceValueLotCase c) => new("fidelity-lot", SecurityId, c.AcquiredDate, 1_000m, 100m);

    private static Func<BondReferenceDto, Guid, IReadOnlyList<AssetProjectedCashFlowDto>> AssetOpsProjector()
        => PrivateStatic<Func<BondReferenceDto, Guid, IReadOnlyList<AssetProjectedCashFlowDto>>>(
            typeof(AssetOperationsProjectionBuilder),
            "ProjectFixedIncomeCashFlows");

    // A fixed-coupon bond whose cadence reads back as periodMonths ("12", "6", "4", "3", "2" or "1"
    // payments a year; "3" reaches the reader's numeric fallback) and whose Actual/360 basis gives
    // every period of a day or more a positive coupon, so no coupon row is suppressed and the coupon
    // rows are exactly the schedule's periods.
    private static BondReferenceDto AssetOpsBond(DateOnly issueDate, DateOnly maturity, int periodMonths)
        => new(
            SecurityId,
            "Fidelity bond",
            "USD",
            null,
            null,
            $"CUSIP:{SecurityId:N}",
            new BondLifecycleDto(
                SecurityId,
                BondLifecycleStat.Active,
                issueDate,
                null,
                maturity,
                false,
                1,
                Par: 100m,
                PaymentFrequency: (12 / periodMonths).ToString(CultureInfo.InvariantCulture)),
            new BondAccrualConventionDto(SecurityId, "Actual/360", null, null, "Fixed", 5m, null, null, 1),
            1);

    // The coupon periods of a projected bond, or null when the rows are not the shape the fidelity
    // check reads: consecutive sequence numbers, each coupon due on its accrual end, and one maturity
    // row last, on maturity.
    private static IReadOnlyList<(DateOnly AccrualStart, DateOnly AccrualEnd)>? CouponPeriods(
        IReadOnlyList<AssetProjectedCashFlowDto> flows,
        DateOnly maturity)
    {
        if (flows.Count == 0
            || flows[^1] is not { FlowType: "Maturity" } last
            || last.DueDate != maturity
            || !flows.Select(static flow => flow.SequenceNumber).SequenceEqual(Enumerable.Range(1, flows.Count)))
        {
            return null;
        }

        var coupons = flows.Take(flows.Count - 1).ToArray();
        return coupons.All(static flow => flow is { FlowType: "Coupon", AccrualStartDate: not null } && flow.AccrualEndDate == flow.DueDate)
            ? coupons.Select(static flow => (flow.AccrualStartDate!.Value, flow.DueDate)).ToArray()
            : null;
    }

    private static Func<DateOnly?, string?, DateTimeOffset, DateTimeOffset, IReadOnlyList<DateTimeOffset>> FundStructureRoll()
        => PrivateStatic<Func<DateOnly?, string?, DateTimeOffset, DateTimeOffset, IReadOnlyList<DateTimeOffset>>>(
            typeof(InMemoryFundStructureService),
            "RollProjectedScheduleDates");

    // Asserts each case's production outcome (its date, or the exception type it threw) equals the
    // oracle's and that every oracle failure is ArgumentOutOfRangeException; returns how many oracle
    // calls failed. The message is not compared: the kernel's step names its own parameter and text.
    private static int AssertSameOutcomes(IReadOnlyList<(string Label, Func<DateOnly> Oracle, Func<DateOnly> Production)> cases)
    {
        var outcomes = cases.Select(static c => (c.Label, Oracle: Outcome(c.Oracle), Production: Outcome(c.Production))).ToArray();
        outcomes.Where(static o => o.Oracle != o.Production)
            .Select(static o => $"{o.Label}: oracle {o.Oracle}, production {o.Production}")
            .Take(20)
            .Should().BeEmpty("production must return the oracle's date or throw the oracle's exception type");

        var oracleFailures = outcomes.Select(static o => o.Oracle.Failure).OfType<Type>().ToArray();
        oracleFailures.Should().OnlyContain(static failure => failure == typeof(ArgumentOutOfRangeException));
        return oracleFailures.Length;
    }

    private static (DateOnly? Date, Type? Failure) Outcome(Func<DateOnly> step)
    {
        try
        {
            return (step(), null);
        }
        catch (Exception exception)
        {
            return (null, exception.GetType());
        }
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TDelegate PrivateStatic<TDelegate>(Type owner, string name)
        where TDelegate : Delegate
        => Method(owner, name, BindingFlags.NonPublic | BindingFlags.Static).CreateDelegate<TDelegate>();

    private static TDelegate PrivateInstance<TDelegate>(Type owner, string name)
        where TDelegate : Delegate
        => Method(owner, name, BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate<TDelegate>();

    private static MethodInfo Method(Type owner, string name, BindingFlags flags)
        => owner.GetMethod(name, flags)
           ?? throw new InvalidOperationException($"{owner.Name}.{name} was not found; the fidelity check must follow the generator it pins.");

    // The obligation-timeline step functions, bound by reflection to the private production methods.
    private sealed record ObligationSteps(
        Func<DateOnly, DateOnly> NextMonthly,
        Func<DateOnly, DateOnly> NextQuarter,
        Func<DateOnly, string?, DateOnly> NextFrequency,
        Func<DateOnly, DateOnly, int, DateOnly> AdvanceAfter,
        Func<string?, int> NormalizeFrequencyMonths)
    {
        public static ObligationSteps Bind()
        {
            var owner = typeof(AssetObligationProjectionService);
            return new(
                PrivateStatic<Func<DateOnly, DateOnly>>(owner, "NextMonthlyDate"),
                PrivateStatic<Func<DateOnly, DateOnly>>(owner, "NextQuarterDate"),
                PrivateStatic<Func<DateOnly, string?, DateOnly>>(owner, "NextFrequencyDate"),
                PrivateStatic<Func<DateOnly, DateOnly, int, DateOnly>>(owner, "AdvanceAfter"),
                PrivateStatic<Func<string?, int>>(owner, "NormalizeFrequencyMonths"));
        }
    }
}
