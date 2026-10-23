using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.SecurityMaster.Schedules;
using static Meridian.Tests.SecurityMaster.Schedules.ScheduleParityGrids;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Proves each legacy schedule generator is a named mode of the contractual schedule kernel by
/// comparing its frozen copy in <see cref="LegacyScheduleOracles"/> with the kernel over an
/// exhaustive deterministic grid. Each grid is one fact: a plain loop that counts its cases, collects
/// the first mismatches and asserts once, so the run stays fast and a failure names the inputs.
/// </summary>
public sealed class ScheduleKernelLegacyParityTests
{
    private const int MaxReportedMismatches = 20;

    // The Postgres projection's interval switch, stated independently: Bullet (and anything else)
    // rolls annually.
    private static readonly IReadOnlyDictionary<PaymentFrequency, int> LoanIntervalMonths = new Dictionary<PaymentFrequency, int>
    {
        [PaymentFrequency.Monthly] = 1,
        [PaymentFrequency.Quarterly] = 3,
        [PaymentFrequency.SemiAnnual] = 6,
        [PaymentFrequency.Annual] = 12,
        [PaymentFrequency.Bullet] = 12,
    };

    // The fund-structure forecast cadences and the tenor each one compounds by.
    private static readonly (string Frequency, ScheduleTenor Tenor)[] FundStructureCadences =
    [
        ("daily", ScheduleTenor.Days(1)),
        ("weekly", ScheduleTenor.Weeks(1)),
        ("biweekly", ScheduleTenor.Weeks(2)),
        ("monthly", ScheduleTenor.Months(1)),
        ("quarterly", ScheduleTenor.Months(3)),
        ("semiannual", ScheduleTenor.Months(6)),
        ("annual", ScheduleTenor.Years(1)),
    ];

    // Obligation-step frequency spellings and the months NormalizeFrequencyMonths reads them as.
    private static readonly (string? Spelling, int Months)[] ObligationFrequencies =
    [
        (null, 3), ("", 3), ("  ", 3), ("Monthly", 1), ("monthly", 1), ("Quarterly", 3),
        ("Semi-Annual", 6), ("semi_annually", 6), ("SemiYearly", 6), ("Half-Yearly", 3),
        ("Annual", 12), ("annually", 12), ("Yearly", 12), ("Bullet", 12),
        ("12", 1), ("4", 3), ("2", 6), ("1", 12), ("5", 2), ("24", 1), ("0", 3), ("-4", 3),
        ("weekly", 3), ("3M", 3),
    ];

    [Fact]
    public void SmIssueAnchored_MatchesIssueAnchoredForward_OnEverySchedule()
    {
        var mismatches = new List<string>();
        var cases = 0;
        var periods = 0;
        foreach (var c in ForwardCases())
        {
            cases++;
            var expected = LegacyScheduleOracles.SmIssueAnchored(c.Start, c.Maturity, c.PeriodMonths).ToArray();
            var result = ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(c.Start, c.Maturity, ScheduleTenor.Months(c.PeriodMonths)));
            periods += expected.Length;
            if (!result.Succeeded
                || !IsUnadjusted(result)
                || !result.Periods.Select(static period => (period.AccrualStart, period.PaymentDate)).SequenceEqual(expected))
            {
                Record(mismatches, c, expected, result);
            }
        }

        cases.Should().Be(ForwardScheduleCount);
        periods.Should().BePositive();
        mismatches.Should().BeEmpty("the issue-anchored kernel mode must reproduce SecurityMasterCashFlowService.BuildPaymentPeriods tuple for tuple");
    }

    [Fact]
    public void AssetOpsCompounded_MatchesCompoundedForward_OnEverySchedule()
    {
        var mismatches = new List<string>();
        var cases = 0;
        foreach (var c in ForwardCases())
        {
            cases++;
            var expected = LegacyScheduleOracles.AssetOpsCompounded(c.Start, c.Maturity, c.PeriodMonths);
            var result = ScheduleGenerator.Generate(ScheduleSpec.CompoundedForward(c.Start, c.Maturity, ScheduleTenor.Months(c.PeriodMonths)));
            if (!result.Succeeded
                || !IsUnadjusted(result)
                || !result.Periods.Select(static period => (period.AccrualStart, period.AccrualEnd)).SequenceEqual(expected))
            {
                Record(mismatches, c, expected, result);
            }
        }

        cases.Should().Be(ForwardScheduleCount);
        mismatches.Should().BeEmpty("the compounded kernel mode must reproduce the asset-operations bond coupon roll");
    }

    [Fact]
    public void LoanPostgresAsOf_MatchesCompoundedForward_ForEveryPaymentFrequency()
    {
        Enum.GetValues<PaymentFrequency>().Should().BeEquivalentTo(LoanIntervalMonths.Keys, "every payment frequency must be on the grid");

        var mismatches = new List<string>();
        var cases = 0;
        for (var asOf = ForwardStartFrom; asOf <= ForwardStartTo; asOf = asOf.AddDays(1))
        {
            foreach (var (frequency, months) in LoanIntervalMonths)
            {
                foreach (var maturity in Maturities(asOf))
                {
                    cases++;
                    var expected = LegacyScheduleOracles.LoanPostgresAsOf(asOf, maturity, frequency);
                    var result = ScheduleGenerator.Generate(ScheduleSpec.CompoundedForward(asOf, maturity, ScheduleTenor.Months(months)));
                    if (!result.Succeeded
                        || !IsUnadjusted(result)
                        || !result.Periods.Select(static period => (period.AccrualStart, period.PaymentDate)).SequenceEqual(expected))
                    {
                        Record(mismatches, $"{frequency} {Format(asOf)}→{Format(maturity)}", expected, result);
                    }
                }
            }
        }

        cases.Should().Be(ForwardStartCount * LoanIntervalMonths.Count * MaturityMonthOffsets.Length * MaturityDayOffsets.Length);
        mismatches.Should().BeEmpty("the compounded kernel mode must reproduce the Postgres direct-lending projection roll");
    }

    [Fact]
    public void LoanInMemoryMonthly_MatchesCompoundedMonthly_UpToTheProjectionAsOf()
    {
        int[] asOfOffsets = [0, 1, 45, 400];
        var mismatches = new List<string>();
        var cases = 0;
        for (var start = ForwardStartFrom; start <= ForwardStartTo; start = start.AddDays(1))
        {
            foreach (var maturity in Maturities(start))
            {
                var result = ScheduleGenerator.Generate(ScheduleSpec.CompoundedForward(start, maturity, ScheduleTenor.Months(1)));
                foreach (var offset in asOfOffsets)
                {
                    cases++;
                    var asOf = start.AddDays(offset);
                    var expected = LegacyScheduleOracles.LoanInMemoryMonthly(start, maturity, asOf);
                    if (!result.Succeeded
                        || !IsUnadjusted(result)
                        || !result.Periods
                            .TakeWhile(period => period.UnadjustedStart < asOf)
                            .Select(static period => (period.AccrualStart, period.PaymentDate))
                            .SequenceEqual(expected))
                    {
                        Record(mismatches, $"{Format(start)}→{Format(maturity)} as of {Format(asOf)}", expected, result);
                    }
                }
            }
        }

        cases.Should().Be(ForwardStartCount * MaturityMonthOffsets.Length * MaturityDayOffsets.Length * asOfOffsets.Length);
        mismatches.Should().BeEmpty("the compounded monthly kernel mode, cut at the projection as-of, must reproduce the in-memory direct-lending roll");
    }

    [Fact]
    public void FaceValueLotBackward_MatchesBackwardFromTermination_OnEveryCase()
    {
        var mismatches = new List<string>();
        var cases = 0;
        foreach (var c in FaceValueLotCases())
        {
            cases++;
            var expected = LegacyScheduleOracles.FaceValueLotBackward(c.AcquiredDate, c.Maturity, c.PaymentsPerYear);
            var result = ScheduleGenerator.Generate(BackwardSpec(c));
            if (!result.Succeeded || !IsUnadjusted(result) || !BackwardCouponDates(result).SequenceEqual(expected) || !HasBackwardShape(result, c, expected))
            {
                Record(mismatches, c, expected, result);
            }
        }

        cases.Should().Be(FaceValueLotCaseCount);
        mismatches.Should().BeEmpty("the backward end-of-month kernel mode must reproduce FaceValueLot.BackwardCouponDates, quasi-coupon date included");
    }

    [Fact]
    public void FaceValueLotBackward_RefusesExactlyWhereTheOracleThrows_AtTheTwelveHundredPeriodLimit()
    {
        DateOnly[] months = [new(2024, 1, 1), new(2024, 2, 1), new(2025, 2, 1), new(2025, 8, 1)];
        int[] dayOffsets = [-1, 0, 1];
        var mismatches = new List<string>();
        var (refusals, successes) = (0, 0);
        foreach (var month in months)
        {
            for (var maturity = month; maturity.Month == month.Month; maturity = maturity.AddDays(1))
            {
                foreach (var paymentsPerYear in PaymentsPerYear)
                {
                    foreach (var days in dayOffsets)
                    {
                        var c = new FaceValueLotCase(maturity.AddMonths(-1200 * (12 / paymentsPerYear)).AddDays(days), maturity, paymentsPerYear);
                        var result = ScheduleGenerator.Generate(BackwardSpec(c));
                        DateOnly[]? expected;
                        try
                        {
                            expected = LegacyScheduleOracles.FaceValueLotBackward(c.AcquiredDate, c.Maturity, c.PaymentsPerYear);
                        }
                        catch (ArgumentException)
                        {
                            expected = null;
                        }

                        if (expected is null)
                        {
                            refusals++;
                            if (result.Failure?.Code != ScheduleFailureCode.PeriodLimitExceeded)
                            {
                                Record(mismatches, c, Array.Empty<DateOnly>(), result);
                            }
                        }
                        else
                        {
                            successes++;
                            if (!result.Succeeded || !BackwardCouponDates(result).SequenceEqual(expected))
                            {
                                Record(mismatches, c, expected, result);
                            }
                        }
                    }
                }
            }
        }

        refusals.Should().BePositive("the grid must cross the limit");
        successes.Should().BePositive("the grid must reach the limit");
        mismatches.Should().BeEmpty("MaxPeriods 1200 must refuse exactly where BackwardCouponDates throws");
    }

    [Fact]
    public void FaceValueLotRegular_MatchesForwardRequireRegular_OnEveryCase()
    {
        var mismatches = new List<string>();
        var (cases, regular) = (0, 0);
        foreach (var c in FaceValueLotCases())
        {
            cases++;
            var expected = LegacyScheduleOracles.FaceValueLotRegular(c.AcquiredDate, c.Maturity, c.PaymentsPerYear, throwWhenIrregular: false);
            var result = ScheduleGenerator.Generate(RegularSpec(c));
            var matches = expected is null
                ? !result.Succeeded
                : result.Succeeded
                    && IsUnadjusted(result)
                    && result.Periods.All(static period => period.Kind == SchedulePeriodKind.Regular)
                    && RegularCouponDates(result).SequenceEqual(expected);
            regular += expected is null ? 0 : 1;
            if (!matches)
            {
                Record(mismatches, c, expected ?? Array.Empty<DateOnly>(), result);
            }
        }

        cases.Should().Be(FaceValueLotCaseCount);
        regular.Should().BePositive("the grid must contain regular schedules");
        (cases - regular).Should().BePositive("the grid must contain irregular schedules");
        mismatches.Should().BeEmpty("the forward require-regular kernel mode must reproduce FaceValueLot.RegularCouponDates, refusing exactly where it returns null");
    }

    [Fact]
    public void FundStructureCompounded_MatchesCompoundedRollDates_InsideTheWindow()
    {
        int[] windowStartOffsets = [-400, -1, 0, 1, 45];
        int[] windowLengths = [1, 31, 400, 3660];
        var mismatches = new List<string>();
        var (cases, guarded) = (0, 0);
        for (var anchor = ForwardStartFrom; anchor <= ForwardStartTo; anchor = anchor.AddDays(1))
        {
            foreach (var (frequency, tenor) in FundStructureCadences)
            {
                foreach (var startOffset in windowStartOffsets)
                {
                    foreach (var length in windowLengths)
                    {
                        cases++;
                        var windowStart = LegacyScheduleOracles.FundStructureToDateTimeOffset(anchor.AddDays(startOffset));
                        var windowEnd = windowStart.AddDays(length);
                        var expected = LegacyScheduleOracles.FundStructureCompounded(anchor, frequency, windowStart, windowEnd);
                        var rolled = ScheduleGenerator.RollDates(anchor, tenor, ScheduleAnchorMode.Compounded).Take(256).ToArray();
                        var actual = rolled
                            .Select(LegacyScheduleOracles.FundStructureToDateTimeOffset)
                            .Where(date => date >= windowStart && date < windowEnd)
                            .ToArray();
                        guarded += LegacyScheduleOracles.FundStructureToDateTimeOffset(rolled[^1]) < windowEnd ? 1 : 0;
                        if (!actual.SequenceEqual(expected))
                        {
                            Record(mismatches, $"{frequency} from {Format(anchor)} in [{windowStart:yyyy-MM-dd}, {windowEnd:yyyy-MM-dd})", expected, actual);
                        }
                    }
                }
            }
        }

        cases.Should().Be(ForwardStartCount * FundStructureCadences.Length * windowStartOffsets.Length * windowLengths.Length);
        guarded.Should().BePositive("the grid must reach the 256-iteration guard");
        mismatches.Should().BeEmpty("the compounded roll dates, first 256 only, must reproduce the fund-structure income forecast");
    }

    [Fact]
    public void ObligationSteps_MatchRollDates()
    {
        int[] asOfOffsets = [-1, 0, 1, 30, 365, 1000];
        var mismatches = new List<string>();
        var cases = 0;
        for (var asOf = ForwardStartFrom; asOf <= ForwardStartTo; asOf = asOf.AddDays(1))
        {
            var yearStart = new DateOnly(asOf.Year, 1, 1);
            Compare($"next month after {Format(asOf)}", LegacyScheduleOracles.ObligationNextMonthlyDate(asOf), FirstAfter(yearStart, ScheduleTenor.Months(1), ScheduleAnchorMode.Anchored, asOf));
            Compare($"next quarter after {Format(asOf)}", LegacyScheduleOracles.ObligationNextQuarterDate(asOf), FirstAfter(yearStart, ScheduleTenor.Months(3), ScheduleAnchorMode.Anchored, asOf));
            foreach (var (spelling, months) in ObligationFrequencies)
            {
                Compare($"'{spelling}' months", LegacyScheduleOracles.ObligationNormalizeFrequencyMonths(spelling), months);
                Compare(
                    $"'{spelling}' step from {Format(asOf)}",
                    LegacyScheduleOracles.ObligationNextFrequencyDate(asOf, spelling),
                    ScheduleGenerator.RollDates(asOf, ScheduleTenor.Months(months), ScheduleAnchorMode.Anchored).ElementAt(1));
            }

            foreach (var months in PeriodMonths)
            {
                foreach (var offset in asOfOffsets)
                {
                    var after = asOf.AddDays(offset);
                    Compare(
                        $"{months}M from {Format(asOf)} after {Format(after)}",
                        LegacyScheduleOracles.ObligationAdvanceAfter(asOf, after, months),
                        FirstAfter(asOf, ScheduleTenor.Months(months), ScheduleAnchorMode.Compounded, after));
                }
            }
        }

        cases.Should().Be(ForwardStartCount * (2 + (2 * ObligationFrequencies.Length) + (PeriodMonths.Length * asOfOffsets.Length)));
        mismatches.Should().BeEmpty("the obligation-timeline step functions must be roll-date lookups on the kernel grid");

        void Compare<T>(string label, T expected, T actual)
        {
            cases++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual) && mismatches.Count < MaxReportedMismatches)
            {
                mismatches.Add($"{label}: legacy {expected}, kernel {actual}");
            }
        }
    }

    [Fact]
    public void SmPeriodMonths_DerivedFromEveryCadence_IsAKernelTenorOnTheGrid()
    {
        string?[] spellings =
        [
            null, "", "Monthly", "BiMonthly", "Quarterly", "SemiAnnual", "Semi-Annual", "half-yearly", "Annual",
            "Yearly", "AtMaturity", "Bullet", "Zero coupon", "Daily", "Weekly", "BiWeekly", "fortnightly",
            "1M", "2M", "3M", "4M", "6M", "1Y", "2W", "1D", "Irregular", "unknown",
        ];
        var derived = spellings
            .Concat(Enumerable.Range(-2, 400).Select(static value => value.ToString(CultureInfo.InvariantCulture)))
            .Select(static spelling => Math.Max(1, 12 / LegacyScheduleOracles.LegacyCouponPaymentsPerYear(spelling)))
            .Distinct()
            .ToArray();

        derived.Should().OnlyContain(months => PeriodMonths.Contains(months), "both Security Master call sites can only hand the kernel Months(1, 2, 3, 4, 6 or 12)");
    }

    private static ScheduleSpec BackwardSpec(FaceValueLotCase c)
        => ScheduleSpec.BackwardFromTermination(
            c.AcquiredDate,
            c.Maturity,
            ScheduleTenor.Months(12 / c.PaymentsPerYear),
            ScheduleRoll.ForAnchor(c.Maturity, endOfMonthRule: true),
            ScheduleStubPolicy.Short);

    private static ScheduleSpec RegularSpec(FaceValueLotCase c)
    {
        var bothMonthEnds = IsMonthEnd(c.AcquiredDate) && IsMonthEnd(c.Maturity);
        var roll = bothMonthEnds ? ScheduleRoll.EndOfMonth : ScheduleRoll.FixedDay(Math.Max(c.AcquiredDate.Day, c.Maturity.Day));
        return new ScheduleSpec(
            c.AcquiredDate,
            c.Maturity,
            ScheduleTenor.Months(12 / c.PaymentsPerYear),
            ScheduleDirection.Forward,
            ScheduleAnchorMode.Anchored,
            roll,
            ScheduleStubPolicy.RequireRegular);
    }

    // FaceValueLot's backward array: the quasi-coupon date on or before acquisition, then each period end.
    private static DateOnly[] BackwardCouponDates(ScheduleGenerationResult result)
    {
        var first = result.Periods[0];
        var dates = new DateOnly[result.Periods.Count + 1];
        dates[0] = first.Kind == SchedulePeriodKind.Regular ? first.UnadjustedStart : first.ReferencePeriods[0].Start;
        for (var i = 0; i < result.Periods.Count; i++)
        {
            dates[i + 1] = result.Periods[i].UnadjustedEnd;
        }

        return dates;
    }

    // An acquisition on the grid starts a regular period; one between grid dates starts a short stub.
    private static bool HasBackwardShape(ScheduleGenerationResult result, FaceValueLotCase c, DateOnly[] expected)
    {
        var first = result.Periods[0];
        var expectedKind = expected[0] == c.AcquiredDate ? SchedulePeriodKind.Regular : SchedulePeriodKind.ShortInitialStub;
        return first.UnadjustedStart == c.AcquiredDate
            && first.Kind == expectedKind
            && result.Periods.Skip(1).All(static period => period.Kind == SchedulePeriodKind.Regular);
    }

    private static DateOnly[] RegularCouponDates(ScheduleGenerationResult result)
        => [result.Periods[0].UnadjustedStart, .. result.Periods.Select(static period => period.UnadjustedEnd)];

    private static DateOnly FirstAfter(DateOnly anchor, ScheduleTenor tenor, ScheduleAnchorMode mode, DateOnly after)
        => ScheduleGenerator.RollDates(anchor, tenor, mode).First(date => date > after);

    // The legacy factories set no adjustments, so every accrual and payment date is the unadjusted one.
    private static bool IsUnadjusted(ScheduleGenerationResult result)
        => result.Periods.All(static period => period.AccrualStart == period.UnadjustedStart
                                               && period.AccrualEnd == period.UnadjustedEnd
                                               && period.PaymentDate == period.UnadjustedEnd);

    private static bool IsMonthEnd(DateOnly date) => date.Day == DateTime.DaysInMonth(date.Year, date.Month);

    private static void Record<TCase, TItem>(List<string> mismatches, TCase c, IEnumerable<TItem> expected, ScheduleGenerationResult result)
        where TCase : notnull
        => Record(
            mismatches,
            c.ToString()!,
            expected,
            result.Succeeded
                ? result.Periods.Select(static period => $"{period.Kind} {Format(period.UnadjustedStart)}→{Format(period.UnadjustedEnd)}")
                : [$"{result.Failure.Code}: {result.Failure.Message}"]);

    private static void Record<TExpected, TActual>(List<string> mismatches, string label, IEnumerable<TExpected> expected, IEnumerable<TActual> actual)
    {
        if (mismatches.Count < MaxReportedMismatches)
        {
            mismatches.Add($"{label}: legacy [{string.Join(", ", expected)}], kernel [{string.Join(", ", actual)}]");
        }
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
