using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
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

    [Theory]
    [InlineData("2026-11-17T12:00:00Z", "2026-10-31T23:59:59.9999999Z")]
    [InlineData("2026-01-01T00:00:00Z", "2025-12-31T23:59:59.9999999Z")]
    public void PriorMonthEnd_IsTheLastInstantOfThePreviousCalendarMonth(string asOf, string expected)
    {
        FundOperationsWorkspaceReadService.PriorMonthEnd(DateTimeOffset.Parse(asOf, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }
}
