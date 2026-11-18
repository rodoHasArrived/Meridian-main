#if WINDOWS
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.Comparisons;
using Meridian.Wpf.ViewModels.Comparisons;

namespace Meridian.Wpf.Tests.ViewModels.Comparisons;

/// <summary>The desktop prior-period comparison section and NAV trend section (UX Phase E parity).</summary>
public sealed class PeriodComparisonSectionViewModelTests
{
    internal static PeriodComparisonDto Comparison(string status = PeriodComparisonStatus.Ready, string? reason = null) =>
        new(status, reason, "Alpha Fund", "USD",
            new PeriodComparisonWindowDto("As of Nov 17, 2026", null, new DateOnly(2026, 11, 17), true),
            new PeriodComparisonWindowDto("As of Oct 31, 2026", null, new DateOnly(2026, 10, 31), false),
            status == PeriodComparisonStatus.Ready
                ? [PeriodComparisonLineDto.Create("total", "Total NAV", 1_450_000m, 1_100_000m, isTotal: true), PeriodComparisonLineDto.Create("cash", "Cash", 10m, 0m)]
                : [],
            ["NAV is rebuilt from the posted ledger as of each date."],
            new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));

    private static PeriodComparisonSectionViewModel Section() => new("NAV vs prior period", "Description", "Choose a fund profile.");

    [Fact]
    public async Task LoadAsync_ShowsTheProjectedRowsWindowsAndNotes()
    {
        var section = Section();

        await section.LoadAsync(_ => Task.FromResult(Comparison()));

        section.HasRows.Should().BeTrue();
        section.IsMessageVisible.Should().BeFalse();
        section.Heading.Should().Be("Alpha Fund · USD");
        section.Windows.Should().Be("Current: As of Nov 17, 2026. Prior: As of Oct 31, 2026.");
        section.Rows.Select(static row => (row.Label, row.Change, row.ChangePercent, row.MaterialLabel)).Should().Equal(
            ("Total NAV", "350,000.00", "31.82%", "Material"),
            ("Cash", "10.00", PeriodComparisonProjection.NotComparablePriorZero, ""));
        section.Notes.Should().ContainSingle();
        section.Table.Columns.Select(static column => column.Header).Should().Equal("Line", "Now", "Prior", "Variance", "Variance %", "Flag");
    }

    [Fact]
    public async Task LoadAsync_SaysWhyWhenThereIsNothingToCompare()
    {
        var section = Section();

        await section.LoadAsync(_ => Task.FromResult(Comparison(PeriodComparisonStatus.Unavailable, "The ledger has no NAV components as of Dec 31, 2025.")));

        section.HasRows.Should().BeFalse();
        section.IsMessageVisible.Should().BeTrue();
        section.Message.Should().Be("The ledger has no NAV components as of Dec 31, 2025.");
        section.HasFailed.Should().BeFalse();
    }

    [Fact]
    public async Task AFailedLoadOffersARetryThatReloadsFromTheSameSource()
    {
        var section = Section();
        var calls = 0;

        await section.LoadAsync(_ => ++calls == 1
            ? Task.FromException<PeriodComparisonDto>(new InvalidOperationException("Ledger unavailable"))
            : Task.FromResult(Comparison()));

        section.HasFailed.Should().BeTrue();
        section.Message.Should().Be("Ledger unavailable");
        section.RetryCommand.CanExecute(null).Should().BeTrue();

        await section.RetryCommand.ExecuteAsync(null);

        calls.Should().Be(2);
        section.HasFailed.Should().BeFalse();
        section.HasRows.Should().BeTrue();
    }

    [Fact]
    public async Task ResetClearsTheSectionAndDisablesRetry()
    {
        var section = Section();
        await section.LoadAsync(_ => Task.FromResult(Comparison()));

        section.Reset("Choose a fund profile.");

        section.HasRows.Should().BeFalse();
        section.Rows.Should().BeEmpty();
        section.Message.Should().Be("Choose a fund profile.");
        section.RetryCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task NavTrend_DrawsNavAndExposureWithATableAndNeedsTwoPoints()
    {
        var trend = new NavTrendSectionViewModel();
        FundNavTrendDto Trend(params FundNavTrendPointDto[] points) =>
            new("fund-alpha", "Alpha Fund", "USD", points, ["Exposure is at the last posted mark."], DateTimeOffset.UnixEpoch);

        await trend.LoadAsync(_ => Task.FromResult(Trend(
            new FundNavTrendPointDto(new DateOnly(2026, 10, 31), 1_000m, 3, 700m, 100m, 800m, 600m),
            new FundNavTrendPointDto(new DateOnly(2026, 11, 17), 1_100m, 3, 800m, 150m, 950m, 650m))));

        trend.HasTrend.Should().BeTrue();
        trend.HasExposure.Should().BeTrue();
        trend.NavPlot!.Series.Should().HaveCount(2);
        trend.ExposurePlot!.MultiSeries!.Select(static series => series.Label).Should().Equal("Long", "Short", "Gross");
        trend.Rows[1].Should().Be(new NavTrendRow("2026-11-17", "1,100.00", "800.00", "150.00", "950.00", "650.00"));
        trend.Notes.Should().ContainSingle();

        await trend.LoadAsync(_ => Task.FromResult(Trend(new FundNavTrendPointDto(new DateOnly(2026, 11, 17), 1_100m, 3))));

        trend.HasTrend.Should().BeFalse();
        trend.NavPlot.Should().BeNull();
        trend.Message.Should().Be("At least two dates with ledger NAV are needed for a trend.");
    }

    [Fact]
    public async Task NavTrend_HidesExposureWhenNoDateHoldsSecurities()
    {
        var trend = new NavTrendSectionViewModel();

        await trend.LoadAsync(_ => Task.FromResult(new FundNavTrendDto("fund-alpha", "Alpha Fund", "USD",
            [new FundNavTrendPointDto(new DateOnly(2026, 10, 31), 1_000m, 1), new FundNavTrendPointDto(new DateOnly(2026, 11, 17), 1_100m, 1)],
            [], DateTimeOffset.UnixEpoch)));

        trend.HasTrend.Should().BeTrue();
        trend.HasExposure.Should().BeFalse();
        trend.ExposurePlot.Should().BeNull();
    }
}

/// <summary>Answers the cash ladder comparison with a fixed response.</summary>
internal sealed class FakePeriodComparisonApiClient(ApiResponse<PeriodComparisonDto> response) : IPeriodComparisonApiClient
{
    public int Calls { get; private set; }

    public Task<ApiResponse<PeriodComparisonDto>> GetCashLadderComparisonAsync(DateOnly? baselineAsOf = null, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(response);
    }
}
#endif
