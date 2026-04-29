using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AdvancedAnalyticsViewModelTests
{
    [Fact]
    public void NewViewModel_DisablesInvalidActionsAndShowsComparisonGuidance()
    {
        var viewModel = new AdvancedAnalyticsViewModel(new FakeAdvancedAnalyticsService());

        viewModel.CanCompareProviders.Should().BeFalse();
        viewModel.CompareProvidersCommand.CanExecute(null).Should().BeFalse();
        viewModel.CompareGuidanceText.Should().Contain("Enter a symbol");
        viewModel.CanRepairGaps.Should().BeFalse();
        viewModel.RequestRepairGapsCommand.CanExecute(null).Should().BeFalse();
        viewModel.IsRepairConfirmationVisible.Should().BeFalse();

        viewModel.CompareSymbol = "aapl";

        viewModel.CanCompareProviders.Should().BeTrue();
        viewModel.CompareProvidersCommand.CanExecute(null).Should().BeTrue();
        viewModel.CompareGuidanceText.Should().Contain("AAPL");
    }

    [Fact]
    public async Task AnalyzeGaps_WithRepairableGaps_ShouldExposeInlineRepairConfirmation()
    {
        var service = new FakeAdvancedAnalyticsService();
        var viewModel = new AdvancedAnalyticsViewModel(service)
        {
            GapSymbol = "msft"
        };

        await viewModel.AnalyzeGapsAsync();

        viewModel.CanRepairGaps.Should().BeTrue();
        viewModel.RequestRepairGapsCommand.CanExecute(null).Should().BeTrue();
        viewModel.IsRepairConfirmationVisible.Should().BeFalse();
        viewModel.RepairConfirmationDetail.Should().Contain("1 repairable gap");
        viewModel.RepairConfirmationDetail.Should().Contain("MSFT");

        viewModel.RequestRepairGapsCommand.Execute(null);

        viewModel.IsRepairConfirmationVisible.Should().BeTrue();
        viewModel.ConfirmRepairGapsCommand.CanExecute(null).Should().BeTrue();

        viewModel.CancelRepairGapsCommand.Execute(null);

        viewModel.IsRepairConfirmationVisible.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmRepairGapsCommand_ShouldRunRepairAndRefreshGapSummary()
    {
        var service = new FakeAdvancedAnalyticsService();
        var viewModel = new AdvancedAnalyticsViewModel(service);

        await viewModel.AnalyzeGapsAsync();
        viewModel.RequestRepairGapsCommand.Execute(null);

        await viewModel.ConfirmRepairGapsCommand.ExecuteAsync(null);

        service.RepairCalls.Should().Be(1);
        service.AnalyzeCalls.Should().Be(2, "the repair path refreshes the visible gap summary");
        viewModel.IsRepairConfirmationVisible.Should().BeFalse();
        viewModel.StatusTitle.Should().Be("Success");
        viewModel.StatusMessage.Should().Contain("2 gaps repaired");
        viewModel.StatusMessage.Should().Contain("5 records recovered");
    }

    [Fact]
    public async Task CompareProvidersCommand_WithSymbol_ShouldPopulateComparisonSummary()
    {
        var service = new FakeAdvancedAnalyticsService();
        var viewModel = new AdvancedAnalyticsViewModel(service)
        {
            CompareSymbol = "spy",
            CompareDate = new DateTime(2026, 4, 28)
        };

        await viewModel.CompareProvidersCommand.ExecuteAsync(null);

        service.LastCompareSymbol.Should().Be("SPY");
        service.LastCompareDate.Should().Be(new DateOnly(2026, 4, 28));
        viewModel.IsComparisonResultsVisible.Should().BeTrue();
        viewModel.ConsistencyScoreText.Should().Be("98.4%");
        viewModel.DiscrepancyCountText.Should().Be("1");
        viewModel.DiscrepancyItems.Should().ContainSingle();
    }

    [Fact]
    public void AdvancedAnalyticsPageSource_ShouldBindActionsThroughViewModelCommands()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\AdvancedAnalyticsPage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\AdvancedAnalyticsPage.xaml.cs"));

        xaml.Should().Contain("Command=\"{Binding RefreshAllCommand}\"");
        xaml.Should().Contain("Command=\"{Binding DismissStatusCommand}\"");
        xaml.Should().Contain("Command=\"{Binding AnalyzeGapsCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RequestRepairGapsCommand}\"");
        xaml.Should().Contain("Advanced analytics repair confirmation");
        xaml.Should().Contain("{Binding RepairConfirmationTitle}");
        xaml.Should().Contain("{Binding RepairConfirmationDetail}");
        xaml.Should().Contain("Command=\"{Binding ConfirmRepairGapsCommand}\"");
        xaml.Should().Contain("Command=\"{Binding CancelRepairGapsCommand}\"");
        xaml.Should().Contain("Command=\"{Binding CompareProvidersCommand}\"");
        xaml.Should().Contain("{Binding CompareGuidanceText}");
        xaml.Should().Contain("Command=\"{Binding GenerateReportCommand}\"");
        xaml.Should().NotContain("Click=\"Refresh_Click\"");
        xaml.Should().NotContain("Click=\"RepairGaps_Click\"");
        xaml.Should().NotContain("Click=\"CompareProviders_Click\"");

        codeBehind.Should().NotContain("MessageBox.Show");
        codeBehind.Should().NotContain("RepairGaps_Click");
        codeBehind.Should().NotContain("CompareProviders_Click");
        codeBehind.Should().NotContain("GenerateReport_Click");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }

    private sealed class FakeAdvancedAnalyticsService : AdvancedAnalyticsServiceBase
    {
        public int AnalyzeCalls { get; private set; }
        public int RepairCalls { get; private set; }
        public string? LastCompareSymbol { get; private set; }
        public DateOnly? LastCompareDate { get; private set; }

        public override Task<GapAnalysisResult> AnalyzeGapsAsync(GapAnalysisOptions options, CancellationToken ct = default)
        {
            AnalyzeCalls++;

            var now = new DateTime(2026, 4, 28, 14, 0, 0, DateTimeKind.Utc);
            return Task.FromResult(new GapAnalysisResult
            {
                Success = true,
                TotalGaps = 2,
                TotalGapDuration = TimeSpan.FromHours(3),
                Gaps =
                [
                    new AnalyticsDataGap
                    {
                        Symbol = options.Symbol ?? "AAPL",
                        EventType = "trade",
                        StartTime = now.AddHours(-3),
                        EndTime = now.AddHours(-1),
                        Duration = TimeSpan.FromHours(2),
                        IsRepairable = true
                    },
                    new AnalyticsDataGap
                    {
                        Symbol = options.Symbol ?? "AAPL",
                        EventType = "quote",
                        StartTime = now.AddHours(-1),
                        EndTime = now,
                        Duration = TimeSpan.FromHours(1),
                        IsRepairable = false
                    }
                ]
            });
        }

        public override Task<AnalyticsGapRepairResult> RepairGapsAsync(GapRepairOptions options, CancellationToken ct = default)
        {
            RepairCalls++;
            return Task.FromResult(new AnalyticsGapRepairResult
            {
                Success = true,
                GapsRepaired = 2,
                RecordsRecovered = 5
            });
        }

        public override Task<CrossProviderComparisonResult> CompareProvidersAsync(
            CrossProviderComparisonOptions options,
            CancellationToken ct = default)
        {
            LastCompareSymbol = options.Symbol;
            LastCompareDate = options.Date;

            return Task.FromResult(new CrossProviderComparisonResult
            {
                Success = true,
                OverallConsistencyScore = 98.4,
                Discrepancies =
                [
                    new DataDiscrepancy
                    {
                        Timestamp = new DateTime(2026, 4, 28, 14, 30, 0, DateTimeKind.Utc),
                        DiscrepancyType = "price",
                        Provider1 = "Alpaca",
                        Provider2 = "Polygon",
                        Value1 = "512.10",
                        Value2 = "512.14",
                        Difference = 0.01
                    }
                ]
            });
        }
    }
}
