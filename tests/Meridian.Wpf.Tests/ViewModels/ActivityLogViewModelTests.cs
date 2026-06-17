using Meridian.Contracts.Api;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
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
            viewModel.CanClearLog.Should().BeTrue();
            viewModel.CanExportVisibleLogs.Should().BeTrue();
            viewModel.ClearLogTooltip.Should().Contain("3 entries");
            viewModel.ExportVisibleLogsTooltip.Should().Contain("3 entries");
            viewModel.ClearCommand.CanExecute(null).Should().BeTrue();
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
    public void StartAsync_LoadsRemoteLogsThroughRemoteWorkstationClient()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient
            {
                ActivityLogs =
                [
                    new ActivityLogEntryDto(
                        new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc),
                        "Warning",
                        "Storage",
                        "Remote lease recovered")
                ]
            };
            using var viewModel = CreateViewModel(remoteClient);

            await viewModel.StartAsync();
            viewModel.Stop();

            remoteClient.ConfiguredServiceUrl.Should().NotBeNullOrWhiteSpace();
            remoteClient.LastGetEndpoint.Should().Be("/api/logs?limit=500");
            viewModel.FilteredLogs.Should().ContainSingle();
            viewModel.FilteredLogs[0].Message.Should().Be("Remote lease recovered");
            viewModel.ActivityPostureTitle.Should().Be("Warnings present");
        });
    }

    [Fact]
    public void StartAsync_WhenRemoteClientReturnsNonSuccess_FailsClosedToLocalOfflineIndicator()
    {
        WpfTestThread.Run(async () =>
        {
            var notifications = WpfServices.NotificationService.Instance;
            notifications.ClearHistory();
            var remoteClient = new FakeRemoteWorkstationClient { IsSuccess = false };
            using var viewModel = CreateViewModel(remoteClient);

            await viewModel.StartAsync();
            viewModel.Stop();

            remoteClient.LastGetEndpoint.Should().Be("/api/logs?limit=500");
            viewModel.FilteredLogs.Should().ContainSingle();
            viewModel.FilteredLogs[0].Message.Should().Contain("[Offline]");
            viewModel.ActivityPostureTitle.Should().Be("Warnings present");

            notifications.ClearHistory();
        });
    }

    [Fact]
    public void StartAsync_ForwardsCancellationToRemoteWorkstationClient()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient();
            using var viewModel = CreateViewModel(remoteClient);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await viewModel.StartAsync(cts.Token);
            viewModel.Stop();

            remoteClient.LastCancellationWasRequested.Should().BeTrue();
            viewModel.FilteredLogs.Should().BeEmpty();
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
            viewModel.CanClearLog.Should().BeTrue();
            viewModel.CanExportVisibleLogs.Should().BeFalse();
            viewModel.ClearLogTooltip.Should().Contain("2 entries");
            viewModel.ExportVisibleLogsTooltip.Should().Be("No visible activity log entries to export. Reset filters or adjust the current filter.");
            viewModel.ClearCommand.CanExecute(null).Should().BeTrue();
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
            viewModel.CanClearLog.Should().BeFalse();
            viewModel.CanExportVisibleLogs.Should().BeFalse();
            viewModel.ClearLogTooltip.Should().Be("No retained activity log entries to clear.");
            viewModel.ExportVisibleLogsTooltip.Should().Be("No activity log entries to export. Connect the backend or trigger a desktop workflow first.");
            viewModel.ClearCommand.CanExecute(null).Should().BeFalse();
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
            viewModel.CanClearLog.Should().BeFalse();
            viewModel.CanExportVisibleLogs.Should().BeFalse();
            viewModel.ClearLogTooltip.Should().Be("No retained activity log entries to clear.");
            viewModel.ExportVisibleLogsTooltip.Should().Be("No activity log entries to export. Connect the backend or trigger a desktop workflow first.");
            viewModel.ClearCommand.CanExecute(null).Should().BeFalse();

            notifications.ClearHistory();
        });
    }

    [Fact]
    public void ClearConfirmationCommands_GuardDestructiveClear()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();

            viewModel.RequestClearCommand.CanExecute(null).Should().BeFalse();
            viewModel.ConfirmClearCommand.CanExecute(null).Should().BeFalse();
            viewModel.CancelClearCommand.CanExecute(null).Should().BeFalse();

            viewModel.AddLocalLogEntry(LogLevel.Warning, "Provider feed delayed", "Connection");

            viewModel.RequestClearCommand.CanExecute(null).Should().BeTrue();
            viewModel.ConfirmClearCommand.CanExecute(null).Should().BeFalse();

            viewModel.RequestClearCommand.Execute(null);

            viewModel.IsClearConfirmationVisible.Should().BeTrue();
            viewModel.RequestClearCommand.CanExecute(null).Should().BeFalse();
            viewModel.ConfirmClearCommand.CanExecute(null).Should().BeTrue();
            viewModel.CancelClearCommand.CanExecute(null).Should().BeTrue();
            viewModel.ClearConfirmationTitle.Should().Be("Clear retained activity log?");
            viewModel.ClearConfirmationDetail.Should().Contain("1 entry");

            viewModel.CancelClearCommand.Execute(null);

            viewModel.IsClearConfirmationVisible.Should().BeFalse();
            viewModel.RequestClearCommand.CanExecute(null).Should().BeTrue();
            viewModel.ConfirmClearCommand.CanExecute(null).Should().BeFalse();

            viewModel.RequestClearCommand.Execute(null);
            viewModel.ConfirmClearCommand.Execute(null);

            viewModel.IsClearConfirmationVisible.Should().BeFalse();
            viewModel.FilteredLogs.Should().BeEmpty();
            viewModel.CanClearLog.Should().BeFalse();
            viewModel.RequestClearCommand.CanExecute(null).Should().BeFalse();
            viewModel.ConfirmClearCommand.CanExecute(null).Should().BeFalse();
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
        xaml.Should().Contain("ActivityLogExportButton");
        xaml.Should().Contain("ActivityLogClearButton");
        xaml.Should().Contain("ActivityLogActionStrip");
        xaml.Should().Contain("ActivityLogClearConfirmationPanel");
        xaml.Should().Contain("ActivityLogConfirmClearButton");
        xaml.Should().Contain("ActivityLogCancelClearButton");
        xaml.Should().Contain("ActivityLogResetFiltersButton");
        xaml.Should().Contain("{Binding CanExportVisibleLogs}");
        xaml.Should().Contain("{Binding CanClearLog}");
        xaml.Should().Contain("ToolTip=\"{Binding ExportVisibleLogsTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding ClearLogTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("{Binding RequestClearCommand}");
        xaml.Should().Contain("{Binding ConfirmClearCommand}");
        xaml.Should().Contain("{Binding CancelClearCommand}");
        xaml.Should().Contain("{Binding IsClearConfirmationVisible");
        xaml.Should().Contain("{Binding ClearConfirmationTitle}");
        xaml.Should().Contain("{Binding ClearConfirmationDetail}");
        xaml.Should().Contain("{Binding LevelFilter, Mode=TwoWay");
        xaml.Should().Contain("{Binding CategoryFilter, Mode=TwoWay");
        xaml.Should().Contain("{Binding SearchText, Mode=TwoWay");
        xaml.Should().Contain("{Binding ClearFiltersCommand}");
        xaml.Should().Contain("{Binding HasFilterRecoveryAction");
        xaml.Should().Contain("{Binding EmptyStateTitle}");
        xaml.Should().Contain("{Binding EmptyStateDetail}");
        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("SelectionChanged=\"Filter_Changed\"");
        xaml.Should().NotContain("TextChanged=\"Search_Changed\"");
        xaml.Should().NotContain("Click=\"Clear_Click\"");
        codeBehind.Should().NotContain("Filter_Changed");
        codeBehind.Should().NotContain("Search_Changed");
        codeBehind.Should().NotContain("Clear_Click");
        codeBehind.Should().NotContain("MessageBox.Show");
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

    private static ActivityLogViewModel CreateViewModel(IRemoteWorkstationClient? remoteClient = null)
        => new(
            WpfServices.StatusService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            remoteClient);

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

    private sealed class FakeRemoteWorkstationClient : IRemoteWorkstationClient
    {
        public string BaseUrl { get; private set; } = "http://localhost:8080";
        public string? ConfiguredServiceUrl { get; private set; }
        public string? LastGetEndpoint { get; private set; }
        public bool LastCancellationWasRequested { get; private set; }
        public bool IsSuccess { get; init; } = true;
        public ActivityLogEntryDto[]? ActivityLogs { get; init; }

        public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
        {
            ConfiguredServiceUrl = serviceUrl;
            BaseUrl = serviceUrl;
        }

        public Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new ServiceHealthResult { IsReachable = true, IsConnected = true });

        public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult<StatusResponse?>(null);

        public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiResponse<StatusResponse> { Success = true });

        public Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
            => Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
        {
            LastGetEndpoint = endpoint;
            LastCancellationWasRequested = ct.IsCancellationRequested;
            if (ct.IsCancellationRequested)
            {
                return Task.FromException<ApiResponse<T>>(new OperationCanceledException(ct));
            }

            if (!IsSuccess)
            {
                return Task.FromResult(new ApiResponse<T> { Success = false });
            }

            return ActivityLogs is T typed
                ? Task.FromResult(ApiResponse<T>.Ok(typed))
                : Task.FromResult(new ApiResponse<T> { Success = false });
        }

        public Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default)
            where T : class
            => Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> PostWithResponseAsync<T>(
            string endpoint,
            object? body = null,
            CancellationToken ct = default)
            where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public void Dispose()
        {
        }
    }
}
