using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.Comparisons;
using Xunit;

namespace Meridian.Ui.Tests.Services.Comparisons;

public sealed class PeriodComparisonProjectionTests
{
    private static PeriodComparisonDto Comparison(string status = PeriodComparisonStatus.Ready, string? reason = null, params PeriodComparisonLineDto[] lines) =>
        new(status, reason, "Alpha Fund", "USD",
            new PeriodComparisonWindowDto("As of Nov 17, 2026", null, new DateOnly(2026, 11, 17), true),
            new PeriodComparisonWindowDto("As of Oct 31, 2026", null, new DateOnly(2026, 10, 31), false),
            lines,
            ["NAV is rebuilt from the posted ledger as of each date."],
            new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void FormatsAmountsCountsAndTheServersPercent()
    {
        var view = PeriodComparisonProjection.Project(
            Comparison(lines:
            [
                PeriodComparisonLineDto.Create("total", "Total NAV", 1_450_000m, 1_100_000m, isTotal: true),
                PeriodComparisonLineDto.Create("asset-class:Equity", "Equity", 500m, 0m),
                PeriodComparisonLineDto.Create("cash", "Cash", 1_250_000m, 1_610_000m),
                PeriodComparisonLineDto.Create("breaches", "Buckets below the minimum", 2m, 1m)
            ]),
            countKeys: ["breaches"]);

        view.IsReady.Should().BeTrue();
        view.Heading.Should().Be("Alpha Fund · USD");
        view.Windows.Should().Be("Current: As of Nov 17, 2026. Prior: As of Oct 31, 2026.");
        view.Rows[0].Should().Be(new PeriodComparisonRow("total", "Total NAV", "1,450,000.00", "1,100,000.00", "350,000.00", "31.82%", true, true, false));
        view.Rows[1].ChangePercent.Should().Be(PeriodComparisonProjection.NotComparablePriorZero);
        view.Rows[1].IsPriorZero.Should().BeTrue();
        view.Rows[1].IsMaterial.Should().BeFalse();
        view.Rows[2].Change.Should().Be("(360,000.00)");
        view.Rows[2].ChangePercent.Should().Be("-22.36%");
        view.Rows[2].IsMaterial.Should().BeTrue();
        view.Rows[3].Should().Be(new PeriodComparisonRow("breaches", "Buckets below the minimum", "2", "1", "1", "100.00%", false, false, false));
        view.Notes.Should().ContainSingle();
    }

    [Fact]
    public void SaysWhyWhenThereIsNothingToCompare()
    {
        var unavailable = PeriodComparisonProjection.Project(
            Comparison(PeriodComparisonStatus.Unavailable, "The ledger has no NAV components as of Dec 31, 2025."));

        unavailable.IsReady.Should().BeFalse();
        unavailable.Message.Should().Be("The ledger has no NAV components as of Dec 31, 2025.");
        unavailable.Rows.Should().BeEmpty();
        unavailable.Notes.Should().ContainSingle();

        PeriodComparisonProjection.Project(null).Message.Should().Be("Loading the comparison…");
        PeriodComparisonProjection.Project(Comparison()).Message.Should().Be("There is nothing to compare for these dates.");
    }

    [Theory]
    [InlineData(501, null, "This comparison is not available in this host.")]
    [InlineData(403, null, "You do not have permission to see this comparison.")]
    [InlineData(400, "baselineAsOf must be yyyy-MM-dd.", "baselineAsOf must be yyyy-MM-dd.")]
    [InlineData(500, null, "The comparison could not be loaded.")]
    public void DescribesFailedRequests(int status, string? error, string expected)
    {
        var response = new ApiResponse<PeriodComparisonDto> { Success = false, StatusCode = status, ErrorMessage = error };

        PeriodComparisonProjection.DescribeFailure(response).Should().Be(expected);
    }

    [Fact]
    public void BuildsTheCashLadderComparisonRoute()
    {
        PeriodComparisonApiClient.BuildCashLadderComparisonRoute(null).Should().Be(UiApiRoutes.PortfolioCashLadderComparison);
        PeriodComparisonApiClient.BuildCashLadderComparisonRoute(new DateOnly(2026, 10, 31))
            .Should().Be($"{UiApiRoutes.PortfolioCashLadderComparison}?baselineAsOf=2026-10-31");
    }
}
