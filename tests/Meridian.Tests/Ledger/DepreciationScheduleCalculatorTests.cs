using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests the depreciation schedule math: straight-line, declining-balance (with the auto-switch to
/// straight-line and salvage floor), and usage-driven units-of-production. Every schedule must
/// depreciate exactly to salvage value with no rounding drift.
/// </summary>
public sealed class DepreciationScheduleCalculatorTests
{
    private static readonly Guid AssetId = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly InService = new(2026, 1, 15);

    private readonly DepreciationScheduleCalculator _calculator = new();

    private static FixedAssetRecord Asset(
        decimal cost,
        decimal salvage,
        int months,
        DepreciationMethod method,
        decimal factor = 2.0m,
        long? totalUnits = null)
        => new(AssetId, "Test Asset", "USD", cost, salvage, InService, months, method, factor, totalUnits);

    [Fact]
    public void StraightLine_FullLife_SumsToCostMinusSalvageAndEndsAtSalvage()
    {
        var schedule = _calculator.BuildSchedule(Asset(12_000m, 2_000m, 10, DepreciationMethod.StraightLine));

        schedule.Should().HaveCount(10);
        schedule.Should().OnlyContain(period => period.DepreciationAmount == 1_000m);
        schedule.Sum(period => period.DepreciationAmount).Should().Be(10_000m);
        schedule[^1].ClosingNetBookValue.Should().Be(2_000m);
    }

    [Fact]
    public void StraightLine_FinalPeriodAbsorbsRounding()
    {
        var schedule = _calculator.BuildSchedule(Asset(1_000m, 0m, 3, DepreciationMethod.StraightLine));

        schedule[0].DepreciationAmount.Should().Be(333.33m);
        schedule[1].DepreciationAmount.Should().Be(333.33m);
        schedule[2].DepreciationAmount.Should().Be(333.34m);
        schedule.Sum(period => period.DepreciationAmount).Should().Be(1_000m);
        schedule[^1].ClosingNetBookValue.Should().Be(0m);
    }

    [Fact]
    public void StraightLine_PeriodEndsAreMonthEndsFromInServiceDate()
    {
        var schedule = _calculator.BuildSchedule(Asset(300m, 0m, 3, DepreciationMethod.StraightLine));

        schedule[0].PeriodEnd.Should().Be(new DateOnly(2026, 1, 31));
        schedule[1].PeriodEnd.Should().Be(new DateOnly(2026, 2, 28));
        schedule[2].PeriodEnd.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void DecliningBalance_SwitchesToStraightLineAndEndsAtSalvage()
    {
        var schedule = _calculator.BuildSchedule(Asset(10_000m, 0m, 5, DepreciationMethod.DecliningBalance, factor: 2.0m));

        // 40% declining rate: 4000, 2400, 1440, then straight-line takes over (1080 > 864), 1080.
        schedule.Select(period => period.DepreciationAmount)
            .Should().Equal(4_000m, 2_400m, 1_440m, 1_080m, 1_080m);
        schedule.Sum(period => period.DepreciationAmount).Should().Be(10_000m);
        schedule[^1].ClosingNetBookValue.Should().Be(0m);
    }

    [Fact]
    public void DecliningBalance_NeverDepreciatesBelowSalvage()
    {
        var schedule = _calculator.BuildSchedule(Asset(1_000m, 100m, 4, DepreciationMethod.DecliningBalance, factor: 2.0m));

        schedule.Should().OnlyContain(period => period.ClosingNetBookValue >= 100m);
        schedule.Sum(period => period.DepreciationAmount).Should().Be(900m);
        schedule[^1].ClosingNetBookValue.Should().Be(100m);
    }

    [Fact]
    public void UnitsOfProduction_ProratesByUsageAndEndsAtSalvage()
    {
        var asset = Asset(11_000m, 1_000m, 4, DepreciationMethod.UnitsOfProduction, totalUnits: 1_000);

        var schedule = _calculator.BuildSchedule(asset, new long[] { 100, 200, 300, 400 });

        // $10 per unit over a 10,000 depreciable base.
        schedule.Select(period => period.DepreciationAmount)
            .Should().Equal(1_000m, 2_000m, 3_000m, 4_000m);
        schedule[^1].ClosingNetBookValue.Should().Be(1_000m);
    }

    [Fact]
    public void UnitsOfProduction_CapsAtTotalUnits()
    {
        var asset = Asset(11_000m, 1_000m, 2, DepreciationMethod.UnitsOfProduction, totalUnits: 1_000);

        // Reported usage (1,200) exceeds the lifetime total (1,000); depreciation is capped.
        var schedule = _calculator.BuildSchedule(asset, new long[] { 600, 600 });

        schedule.Sum(period => period.DepreciationAmount).Should().Be(10_000m);
        schedule[^1].ClosingNetBookValue.Should().Be(1_000m);
    }

    [Fact]
    public void UnitsOfProduction_WithoutProjectedUnits_Throws()
    {
        var asset = Asset(11_000m, 1_000m, 4, DepreciationMethod.UnitsOfProduction, totalUnits: 1_000);

        var act = () => _calculator.BuildSchedule(asset, projectedUnitsPerPeriod: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UnitsOfProduction_WithoutTotalUnits_Throws()
    {
        var asset = Asset(11_000m, 1_000m, 4, DepreciationMethod.UnitsOfProduction, totalUnits: null);

        var act = () => _calculator.BuildSchedule(asset, new long[] { 100 });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildSchedule_NonPositiveUsefulLife_Throws()
    {
        var act = () => _calculator.BuildSchedule(Asset(1_000m, 0m, 0, DepreciationMethod.StraightLine));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildSchedule_SalvageAboveCost_Throws()
    {
        var act = () => _calculator.BuildSchedule(Asset(1_000m, 2_000m, 12, DepreciationMethod.StraightLine));

        act.Should().Throw<ArgumentException>();
    }
}
