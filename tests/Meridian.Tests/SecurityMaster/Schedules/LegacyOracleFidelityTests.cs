using System.Globalization;
using System.Reflection;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using static Meridian.Tests.SecurityMaster.Schedules.ScheduleParityGrids;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Proves the frozen copies in <see cref="LegacyScheduleOracles"/> still behave like the private
/// production generators they were copied from, by invoking those generators through reflection on a
/// deterministic 5,000-case sample of each parity grid. Once
/// <c>SecurityMasterCashFlowService.BuildPaymentPeriods</c> delegates to the kernel, its check also
/// proves kernel equals oracle through the production method.
/// </summary>
public sealed class LegacyOracleFidelityTests
{
    // Prime strides walk every combination of the grids' inner offsets.
    private const int ForwardStride = 31;
    private const int FaceValueLotStride = 23;

    private static readonly Guid SecurityId = Guid.Parse("6f1d3c2a-5b4e-4d7f-9a81-0c2e4b6d8f10");

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
    public void SmResolvePaymentFrequencyPerYear_MatchesTheCadenceOracle()
    {
        var resolve = PrivateStatic<Func<string?, int>>(typeof(SecurityMasterCashFlowService), "ResolvePaymentFrequencyPerYear");
        string?[] spellings =
        [
            null, "", " ", "Monthly", "BiMonthly", "Quarterly", "quarterly", "SemiAnnual", "Semi-Annual", "semi_annually",
            "half-yearly", "Annual", "Yearly", "AtMaturity", "Bullet", "Daily", "Weekly", "BiWeekly", "1M", "3M", "6M",
            "1Y", "2W", "Irregular", "unknown",
        ];

        var cases = spellings
            .Concat(Enumerable.Range(-2, 30).Select(static value => value.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        cases.Select(spelling => resolve(spelling))
            .Should().Equal(cases.Select(LegacyScheduleOracles.LegacyCouponPaymentsPerYear));
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

    private static TDelegate PrivateStatic<TDelegate>(Type owner, string name)
        where TDelegate : Delegate
        => Method(owner, name, BindingFlags.NonPublic | BindingFlags.Static).CreateDelegate<TDelegate>();

    private static TDelegate PrivateInstance<TDelegate>(Type owner, string name)
        where TDelegate : Delegate
        => Method(owner, name, BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate<TDelegate>();

    private static MethodInfo Method(Type owner, string name, BindingFlags flags)
        => owner.GetMethod(name, flags)
           ?? throw new InvalidOperationException($"{owner.Name}.{name} was not found; the fidelity check must follow the generator it pins.");
}
