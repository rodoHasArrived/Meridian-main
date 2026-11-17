using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed partial class FundOperationsWorkspaceReadServiceTests
{
    private static FundOperationsWorkspaceReadService CreateNavComparisonService(StrategyRunStore repository)
    {
        var securityMaster = new NullSecurityMasterQueryService();
        return new FundOperationsWorkspaceReadService(
            new InMemoryFundAccountService(),
            repository,
            new PortfolioReadService(),
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));
    }

    [Fact]
    public async Task GetNavComparisonAsync_ComparesLedgerNavRebuiltAtEachDate()
    {
        var fundProfileId = $"fund-nav-{Guid.NewGuid():N}";
        var repository = new StrategyRunStore();
        await repository.RecordRunAsync(BuildRun("run-nav-001", "nav-1", "Nav Strategy", fundProfileId, "Nav Fund", realizedPnl: 40m, unrealizedPnl: 60m));
        var service = CreateNavComparisonService(repository);

        var comparison = await service.GetNavComparisonAsync(
            fundProfileId,
            asOf: new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            baselineAsOf: new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero),
            currency: "USD");

        comparison.Status.Should().Be(PeriodComparisonStatus.Ready);
        comparison.Subject.Should().Be("Nav Fund");
        comparison.Current.End.Should().Be(new DateOnly(2026, 5, 15));
        comparison.Baseline!.End.Should().Be(new DateOnly(2026, 4, 30));
        var total = comparison.Lines.Should().Contain(line => line.IsTotal).Which;
        total.Key.Should().Be("total");
        total.Change.Should().Be(total.Current - total.Prior);
        comparison.Lines.Should().OnlyContain(line => line.Change == line.Current - line.Prior);
        comparison.Notes.Should().Contain(note => note.Contains("rebuilt from the posted ledger", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetNavComparisonAsync_IsUnavailableWhenTheLedgerHadNothingAtThePriorDate()
    {
        var fundProfileId = $"fund-nav-{Guid.NewGuid():N}";
        var repository = new StrategyRunStore();
        await repository.RecordRunAsync(BuildRun("run-nav-002", "nav-2", "Nav Strategy", fundProfileId, "Nav Fund", realizedPnl: 40m));
        var service = CreateNavComparisonService(repository);

        var comparison = await service.GetNavComparisonAsync(
            fundProfileId,
            asOf: new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            baselineAsOf: new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero),
            currency: "USD");

        comparison.Status.Should().Be(PeriodComparisonStatus.Unavailable);
        comparison.Reason.Should().Contain("no NAV components as of Dec 31, 2025");
        comparison.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNavComparisonAsync_RejectsAPriorDateOnOrAfterTheCurrentDate()
    {
        var service = CreateNavComparisonService(new StrategyRunStore());
        var asOf = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);

        var comparison = await service.GetNavComparisonAsync("fund-nav-x", asOf, asOf, "USD");

        comparison.Status.Should().Be(PeriodComparisonStatus.Incompatible);
        comparison.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNavTrendAsync_ListsMonthEndsAscendingAndEndsWithTheCurrentNav()
    {
        var fundProfileId = $"fund-nav-{Guid.NewGuid():N}";
        var repository = new StrategyRunStore();
        await repository.RecordRunAsync(BuildRun("run-nav-trend-1", "nav-t", "Nav Strategy", fundProfileId, "Nav Fund", realizedPnl: 40m, unrealizedPnl: 60m));
        var service = CreateNavComparisonService(repository);
        var asOf = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

        var trend = await service.GetNavTrendAsync(fundProfileId, months: 6, asOf: asOf, currency: "USD");
        var comparison = await service.GetNavComparisonAsync(fundProfileId, asOf, new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero), "USD");

        trend.Subject.Should().Be("Nav Fund");
        trend.Points.Should().NotBeEmpty();
        trend.Points.Select(static point => point.AsOf).Should().BeInAscendingOrder();
        trend.Points[^1].AsOf.Should().Be(new DateOnly(2026, 7, 15));
        trend.Points[^1].TotalNav.Should().Be(comparison.Lines.Single(static line => line.IsTotal).Current);
        // The run's ledger starts in April 2026, so February and March are left out with a note.
        trend.Points.Select(static point => point.AsOf).Should().Equal(new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 15));
        trend.Notes.Should().Contain(static note => note.Contains("left out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetNavTrendAsync_ReportsLongShortGrossAndNetSecuritiesExposureAtEachDate()
    {
        var fundProfileId = $"fund-nav-{Guid.NewGuid():N}";
        var run = BuildRun("run-nav-exposure", "nav-x", "Nav Strategy", fundProfileId, "Nav Fund", realizedPnl: 40m, unrealizedPnl: 60m);
        // AAPL (400 long since April) is marked up 50 in June, and MSFT is sold short for 150 in June.
        var ledger = (Meridian.Ledger.Ledger)run.Metrics!.Ledger!;
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 6, 10, 14, 0, 0, TimeSpan.Zero), "Short MSFT",
        [
            (LedgerAccounts.Cash, 150m, 0m),
            (LedgerAccounts.ShortSecuritiesPayable("MSFT"), 0m, 150m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero), "Mark AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 50m, 0m),
            (LedgerAccounts.UnrealizedGain, 0m, 50m)
        ]);
        var repository = new StrategyRunStore();
        await repository.RecordRunAsync(run);
        var service = CreateNavComparisonService(repository);

        var trend = await service.GetNavTrendAsync(fundProfileId, months: 4, asOf: new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), currency: "USD");

        trend.Points.Select(static point => (point.AsOf, point.LongExposure, point.ShortExposure, point.GrossExposure, point.NetExposure))
            .Should().Equal(
                (new DateOnly(2026, 4, 30), 400m, 0m, 400m, 400m),
                (new DateOnly(2026, 5, 31), 400m, 0m, 400m, 400m),
                (new DateOnly(2026, 6, 30), 450m, 150m, 600m, 300m),
                (new DateOnly(2026, 7, 15), 450m, 150m, 600m, 300m));
        trend.Notes.Should().Contain(static note => note.Contains("last posted mark", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetNavTrendAsync_ClampsTheWindowAndReportsAnEmptyLedger()
    {
        var service = CreateNavComparisonService(new StrategyRunStore());

        var trend = await service.GetNavTrendAsync("fund-nav-empty", months: 500, asOf: new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero));

        trend.Points.Should().BeEmpty();
        trend.Notes.Should().Contain("The ledger has no NAV components in this window.");
        FundOperationsWorkspaceReadService.MaxNavTrendMonths.Should().Be(24);
    }

    [Theory]
    [InlineData("2026-11-17T12:00:00Z", "2026-10-31T23:59:59.9999999Z")]
    [InlineData("2026-01-01T00:00:00Z", "2025-12-31T23:59:59.9999999Z")]
    public void PriorMonthEnd_IsTheLastInstantOfThePreviousCalendarMonth(string asOf, string expected)
    {
        FundOperationsWorkspaceReadService.PriorMonthEnd(DateTimeOffset.Parse(asOf, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }
}
