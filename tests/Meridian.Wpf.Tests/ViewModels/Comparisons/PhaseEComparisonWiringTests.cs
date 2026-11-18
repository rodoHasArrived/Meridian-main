#if WINDOWS
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels.Comparisons;

/// <summary>Where the desktop shows the Phase E comparisons: run risk, run cash flow and fund operations.</summary>
public sealed class PhaseEComparisonWiringTests
{
    private static StrategyRunWorkspaceService RunService(StrategyRunStore store) =>
        new(store, new PortfolioReadService(), new LedgerReadService());

    [Fact]
    public async Task CashFlow_PortfolioLadderComparisonLoadsOnRequest()
    {
        var client = new FakePeriodComparisonApiClient(new ApiResponse<PeriodComparisonDto>
        {
            Success = true,
            StatusCode = 200,
            Data = PeriodComparisonSectionViewModelTests.Comparison()
        });
        var vm = new CashFlowViewModel(RunService(new StrategyRunStore()), NavigationService.Instance, periodComparisonClient: client);

        client.Calls.Should().Be(0);
        vm.PortfolioLadderComparison.Message.Should().Contain("end of last month");

        await vm.LoadPortfolioLadderComparisonCommand.ExecuteAsync(null);

        client.Calls.Should().Be(1);
        vm.PortfolioLadderComparison.HasRows.Should().BeTrue();
    }

    [Fact]
    public async Task CashFlow_SaysWhenTheHostCannotBuildThePortfolioLadder()
    {
        var client = new FakePeriodComparisonApiClient(new ApiResponse<PeriodComparisonDto> { Success = false, StatusCode = 501 });
        var vm = new CashFlowViewModel(RunService(new StrategyRunStore()), NavigationService.Instance, periodComparisonClient: client);

        await vm.LoadPortfolioLadderComparisonCommand.ExecuteAsync(null);

        vm.PortfolioLadderComparison.HasFailed.Should().BeTrue();
        vm.PortfolioLadderComparison.Message.Should().Be("This comparison is not available in this host.");
    }

    [Fact]
    public async Task RunRisk_LoadsTheRunsMonthComparisonAfterTheRun()
    {
        var store = new StrategyRunStore();
        var readService = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var vm = new RunRiskViewModel(new StrategyRunWorkspaceService(store, readService), readService, NavigationService.Instance);
        await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-risk-month-comparison"));

        vm.AttributionComparison.Message.Should().Contain("Select a retained strategy run");

        await vm.LoadRunAsync("run-risk-month-comparison");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.AttributionComparison.IsLoading && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        vm.AttributionComparison.IsLoading.Should().BeFalse();
        vm.AttributionComparison.HasFailed.Should().BeFalse();
        (vm.AttributionComparison.HasRows || vm.AttributionComparison.Message.Length > 0).Should().BeTrue();

        await vm.LoadRunAsync(null);

        vm.AttributionComparison.HasRows.Should().BeFalse();
        vm.AttributionComparison.Message.Should().Contain("Select a retained strategy run");
    }

    [Fact]
    public void Pages_HostTheComparisonPanels()
    {
        string Read(string relative)
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, relative)))
            {
                current = current.Parent;
            }

            return File.ReadAllText(Path.Combine(current?.FullName ?? throw new DirectoryNotFoundException(relative), relative));
        }

        Read(@"src\Meridian.Wpf\Views\FundLedgerPage.xaml").Should()
            .Contain("controls:NavTrendPanel DataContext=\"{Binding NavTrend}\"")
            .And.Contain("controls:PeriodComparisonPanel DataContext=\"{Binding NavComparison}\"")
            .And.Contain("SelectedDate=\"{Binding NavBaselineDate, Mode=TwoWay}\"");
        Read(@"src\Meridian.Wpf\Views\RunRiskPage.xaml").Should().Contain("DataContext=\"{Binding AttributionComparison}\"");
        Read(@"src\Meridian.Wpf\Views\RunCashFlowPage.xaml").Should()
            .Contain("Command=\"{Binding LoadPortfolioLadderComparisonCommand}\"")
            .And.Contain("DataContext=\"{Binding PortfolioLadderComparison}\"");
    }
}
#endif
