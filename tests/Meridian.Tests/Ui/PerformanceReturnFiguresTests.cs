using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// W10-PERF-001 brokerage-seam disposition: the existing performance endpoint carries labeled
/// figures from the shared return kernel instead of a parallel API being added beside it.
/// </summary>
public sealed class PerformanceReturnFiguresTests
{
    private static readonly (DateOnly, decimal, string)[] Q3Valuations =
    [
        (new DateOnly(2026, 7, 1), 1_000_000m, "USD"),
        (new DateOnly(2026, 8, 15), 1_230_000m, "USD"),
        (new DateOnly(2026, 9, 30), 1_242_300m, "USD"),
    ];

    private static BrokerageCashFlowEntryDto Cash(string id, string category, decimal amount, DateOnly date, string currency = "USD")
        => new(id, category.ToUpperInvariant(), category, amount, currency, new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));

    [Fact]
    public void MeasureBrokerage_DepositIsExternalCapital_NotPerformance()
    {
        var figures = PerformanceReturnFigures.MeasureBrokerage(
            Q3Valuations,
            [Cash("dep-1", "Deposit", 200_000m, new DateOnly(2026, 8, 15))],
            "USD");

        var twr = figures.Should().ContainSingle(f => f.Convention == "TimeWeighted").Subject;
        twr.Status.Should().Be("Reported");
        twr.Value.Should().Be(0.0403000000m, "1.03 × 1.01 − 1; the deposit is not return");
        twr.Source.Should().Be(PerformanceReturnFigures.BrokerageSource);
        twr.Currency.Should().Be("USD");
        twr.PeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        twr.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
        twr.Reasons.Should().Contain(r => r.Contains("not from Meridian's posted ledger"));

        var mwr = figures.Should().ContainSingle(f => f.Convention == "MoneyWeighted").Subject;
        mwr.Status.Should().Be("Reported");
        mwr.Value.Should().NotBeNull();
        mwr.AnnualizedValue.Should().NotBeNull();
    }

    [Fact]
    public void MeasureBrokerage_DividendIsIncome_AndStaysInTheReturn()
    {
        // 1,000,000 -> 1,012,500 with a 2,500 dividend paid in cash: the return is 1.25%, and the
        // dividend must not be subtracted as if it were capital withdrawn or added.
        var figures = PerformanceReturnFigures.MeasureBrokerage(
            [(new DateOnly(2026, 7, 1), 1_000_000m, "USD"), (new DateOnly(2026, 9, 30), 1_012_500m, "USD")],
            [Cash("div-1", "Dividend", 2_500m, new DateOnly(2026, 8, 3))],
            "USD");

        figures.Single(f => f.Convention == "TimeWeighted").Value.Should().Be(0.0125000000m);
    }

    [Fact]
    public void MeasureBrokerage_UnclassifiedCashBlocksEveryFigure()
    {
        var figures = PerformanceReturnFigures.MeasureBrokerage(
            Q3Valuations,
            [Cash("odd-7", "Other", 200_000m, new DateOnly(2026, 8, 15))],
            "USD");

        figures.Should().HaveCount(2).And.OnlyContain(f =>
            f.Status == "ReviewRequired" && f.Value == null && f.AnnualizedValue == null);
        figures.Should().OnlyContain(f => f.Reasons.Any(r => r.Contains("odd-7")));
    }

    [Fact]
    public void MeasureBrokerage_ForeignCurrencyDepositIsNeverSummedIntoAUsdReturn()
    {
        var figures = PerformanceReturnFigures.MeasureBrokerage(
            Q3Valuations,
            [Cash("dep-eur", "Deposit", 160_000m, new DateOnly(2026, 8, 15), "EUR")],
            "USD");

        figures.Should().OnlyContain(f => f.Status == "ReviewRequired" && f.Value == null);
        figures.Should().OnlyContain(f => f.Reasons.Any(r => r.Contains("EUR")));
    }
}
