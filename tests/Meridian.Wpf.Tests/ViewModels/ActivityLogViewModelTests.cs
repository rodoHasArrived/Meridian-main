using Meridian.Ui.Services.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ActivityLogViewModelTests
{
    [Fact]
    public void LocalEntries_UpdateTriagePostureAndLatestSummary()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            var now = new DateTime(2026, 4, 26, 15, 0, 0, DateTimeKind.Utc);

            viewModel.AddLocalLogEntry(LogLevel.Info, "Storage target resolved", "Storage", now.AddMinutes(-2));
            viewModel.AddLocalLogEntry(LogLevel.Warning, "Provider feed delayed", "Connection", now.AddMinutes(-1));
            viewModel.AddLocalLogEntry(LogLevel.Error, "Backfill retry failed", "Backfill", now);

            viewModel.LogCount.Should().Be("3 entries");
            viewModel.VisibleLogCountText.Should().Be("3 visible");
            viewModel.ErrorLogCountText.Should().Be("1 error");
            viewModel.WarningLogCountText.Should().Be("1 warning");
            viewModel.ActivityPostureTitle.Should().Be("Errors need review");
            viewModel.ActivityPostureDetail.Should().Contain("1 retained error is visible");
            viewModel.LatestLogTimeText.Should().Be("15:00:00");
            viewModel.LatestLogSummary.Should().Be("Error / Backfill: Backfill retry failed");
            viewModel.ActiveFilterSummary.Should().Be("Showing every retained entry");
        });
    }

    [Fact]
    public void Filters_UpdateVisibleCountAndActiveFilterSummary()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            var now = new DateTime(2026, 4, 26, 15, 0, 0, DateTimeKind.Utc);

            viewModel.AddLocalLogEntry(LogLevel.Info, "Storage target resolved", "Storage", now.AddMinutes(-2));
            viewModel.AddLocalLogEntry(LogLevel.Warning, "Provider feed delayed", "Connection", now.AddMinutes(-1));
            viewModel.AddLocalLogEntry(LogLevel.Error, "Backfill retry failed", "Backfill", now);

            viewModel.UpdateLevelFilter("Warning");
            viewModel.UpdateCategoryFilter("Connection");
            viewModel.UpdateSearch("Provider");

            viewModel.FilteredLogs.Should().ContainSingle();
            viewModel.FilteredLogs[0].Message.Should().Be("Provider feed delayed");
            viewModel.LogCount.Should().Be("1 of 3 entries");
            viewModel.VisibleLogCountText.Should().Be("1 visible");
            viewModel.ErrorLogCountText.Should().Be("1 error");
            viewModel.WarningLogCountText.Should().Be("1 warning");
            viewModel.ActivityPostureTitle.Should().Be("Errors need review");
            viewModel.ActiveFilterSummary.Should().Be("Filters: Warning level | Connection category | search \"Provider\"");
            viewModel.HasActiveFilters.Should().BeTrue();
            viewModel.HasFilterRecoveryAction.Should().BeTrue();
        });
    }

    [Fact]
    public void ClearFiltersCommand_RestoresRetainedWindowWhenFiltersHideEntries()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            var now = new DateTime(2026, 4, 26, 15, 0, 0, DateTimeKind.Utc);

            viewModel.AddLocalLogEntry(LogLevel.Warning, "Provider feed delayed", "Connection", now.AddMinutes(-1));
            viewModel.AddLocalLogEntry(LogLevel.Error, "Backfill retry failed", "Backfill", now);

            viewModel.LevelFilter = "Info";
            viewModel.CategoryFilter = "Storage";
            viewModel.SearchText = "missing";

            viewModel.FilteredLogs.Should().BeEmpty();
            viewModel.NoLogsVisible.Should().BeTrue();
            viewModel.HasLogHistory.Should().BeTrue();
            viewModel.HasActiveFilters.Should().BeTrue();
            viewModel.HasFilterRecoveryAction.Should().BeTrue();
            viewModel.EmptyStateTitle.Should().Be("No log entries match the current filters");
            viewModel.ClearFiltersCommand.CanExecute(null).Should().BeTrue();

            viewModel.ClearFiltersCommand.Execute(null);

            viewModel.LevelFilter.Should().Be("All");
            viewModel.CategoryFilter.Should().Be("All");
            viewModel.SearchText.Should().BeEmpty();
            viewModel.FilteredLogs.Should().HaveCount(2);
            viewModel.NoLogsVisible.Should().BeFalse();
            viewModel.ActiveFilterSummary.Should().Be("Showing every retained entry");
            viewModel.HasActiveFilters.Should().BeFalse();
            viewModel.HasFilterRecoveryAction.Should().BeFalse();
            viewModel.ClearFiltersCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void EmptyState_WithoutRetainedLogs_DoesNotExposeFilterRecovery()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();

            viewModel.FilteredLogs.Should().BeEmpty();
            viewModel.NoLogsVisible.Should().BeTrue();
            viewModel.HasLogHistory.Should().BeFalse();
            viewModel.HasActiveFilters.Should().BeFalse();
            viewModel.HasFilterRecoveryAction.Should().BeFalse();
            viewModel.EmptyStateTitle.Should().Be("No log entries to display");
            viewModel.EmptyStateDetail.Should().Contain("Connect the backend");
            viewModel.ClearFiltersCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void ClearCommand_ResetsTriageState()
    {
        WpfTestThread.Run(() =>
        {
            var notifications = WpfServices.NotificationService.Instance;
            notifications.ClearHistory();

            using var viewModel = CreateViewModel();
            viewModel.AddLocalLogEntry(LogLevel.Error, "Backfill retry failed", "Backfill");

            viewModel.ClearCommand.Execute(null);

            viewModel.FilteredLogs.Should().BeEmpty();
            viewModel.LogCount.Should().Be("0 entries");
            viewModel.VisibleLogCountText.Should().Be("0 visible");
            viewModel.ErrorLogCountText.Should().Be("0 errors");
            viewModel.WarningLogCountText.Should().Be("0 warnings");
            viewModel.LatestLogTimeText.Should().Be("--");
            viewModel.LatestLogSummary.Should().Be("No activity captured yet.");
            viewModel.ActivityPostureTitle.Should().Be("Waiting for activity");

            notifications.ClearHistory();
        });
    }

    [Fact]
    public void ActivityLogPageSource_BindsFilterControlsAndRecoveryAction()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\ActivityLogPage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\ActivityLogPage.xaml.cs"));

        xaml.Should().Contain("ActivityLogLevelFilterCombo");
        xaml.Should().Contain("ActivityLogCategoryFilterCombo");
        xaml.Should().Contain("ActivityLogSearchBox");
        xaml.Should().Contain("ActivityLogResetFiltersButton");
        xaml.Should().Contain("{Binding LevelFilter, Mode=TwoWay");
        xaml.Should().Contain("{Binding CategoryFilter, Mode=TwoWay");
        xaml.Should().Contain("{Binding SearchText, Mode=TwoWay");
        xaml.Should().Contain("{Binding ClearFiltersCommand}");
        xaml.Should().Contain("{Binding HasFilterRecoveryAction");
        xaml.Should().Contain("{Binding EmptyStateTitle}");
        xaml.Should().Contain("{Binding EmptyStateDetail}");
        xaml.Should().NotContain("SelectionChanged=\"Filter_Changed\"");
        xaml.Should().NotContain("TextChanged=\"Search_Changed\"");
        codeBehind.Should().NotContain("Filter_Changed");
        codeBehind.Should().NotContain("Search_Changed");
    }

    [Fact]
    public void StopAndDispose_AreSafeBeforeStart()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            var stop = Record.Exception(viewModel.Stop);
            var dispose = Record.Exception(viewModel.Dispose);

            stop.Should().BeNull();
            dispose.Should().BeNull();
        });
    }

    private static ActivityLogViewModel CreateViewModel()
        => new(
            WpfServices.StatusService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance);

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
}
