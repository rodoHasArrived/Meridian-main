using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// The period parser is the fail-open core of the restatement period narrowing: it must resolve
/// unambiguous ISO labels exactly and return null (so the caller keeps the candidate) for anything it
/// cannot confidently interpret.
/// </summary>
public sealed class ReportPeriodRangeTests
{
    [Fact]
    public void BareYear_CoversWholeYear()
        => ReportPeriodRange.TryParse("2026").Should().Be((new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

    [Fact]
    public void YearMonth_CoversWholeMonth()
        => ReportPeriodRange.TryParse("2026-05").Should().Be((new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)));

    [Fact]
    public void YearMonth_UsesActualMonthLength()
        => ReportPeriodRange.TryParse("2026-02").Should().Be((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));

    [Fact]
    public void SingleDigitMonth_IsAccepted()
        => ReportPeriodRange.TryParse("2026-5").Should().Be((new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)));

    [Fact]
    public void FullDate_IsASingleDay()
        => ReportPeriodRange.TryParse("2026-05-31").Should().Be((new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 31)));

    [Theory]
    [InlineData("2026-P03")]
    [InlineData("2026-Q1")]
    [InlineData("FY2026")]
    [InlineData("2026-13")]      // invalid month
    [InlineData("2026-02-30")]   // invalid day
    [InlineData("Q1-2026")]      // year not first
    [InlineData("")]
    [InlineData(null)]
    public void AmbiguousOrInvalidLabels_ReturnNull(string? period)
        => ReportPeriodRange.TryParse(period).Should().BeNull();

    [Theory]
    [InlineData("2025-12", true)]      // entirely before
    [InlineData("2026-02", true)]      // month before
    [InlineData("2026-03-14", true)]   // day before
    [InlineData("2026-03", false)]     // covers the effective date
    [InlineData("2026-03-15", false)]  // exactly the effective date
    [InlineData("2026-06", false)]     // after
    [InlineData("2026-P03", false)]    // unparseable → fail open (kept)
    [InlineData("not-a-period", false)]
    public void IsEntirelyBefore_OnlyExcludesConfidentlyEarlierPeriods(string period, bool expected)
        => ReportPeriodRange.IsEntirelyBefore(period, new DateOnly(2026, 3, 15)).Should().Be(expected);
}
