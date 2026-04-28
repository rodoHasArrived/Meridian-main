using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AdminMaintenanceViewModelTests
{
    [Fact]
    public async Task PreviewCleanupAsync_WithCandidates_ShouldExposeReadinessAndConfirmationState()
    {
        var service = new FakeAdminMaintenanceService
        {
            PreviewResult = new CleanupPreviewResult
            {
                Success = true,
                TotalFiles = 2,
                TotalBytes = 2048,
                FilesToDelete =
                [
                    new CleanupFileInfo { Path = "artifacts/tmp/a.bin", Reason = "temp", SizeBytes = 1024 },
                    new CleanupFileInfo { Path = "artifacts/tmp/b.bin", Reason = "empty directory", SizeBytes = 1024 }
                ]
            }
        };
        var viewModel = new AdminMaintenanceViewModel(service);

        viewModel.RequestExecuteCleanupCommand.CanExecute(null).Should().BeFalse();

        await viewModel.PreviewCleanupCommand.ExecuteAsync(null);

        service.PreviewCalls.Should().Be(1);
        service.LastPreviewOptions.Should().NotBeNull();
        service.LastPreviewOptions!.DeleteTempFiles.Should().BeTrue();
        service.LastPreviewOptions.DeleteEmptyDirectories.Should().BeTrue();
        viewModel.CleanupItems.Should().HaveCount(2);
        viewModel.CleanupFilesText.Should().Be("2");
        viewModel.CleanupSizeText.Should().Be("2.0 KB");
        viewModel.CleanupReadinessTitle.Should().Be("Cleanup preview ready");
        viewModel.CleanupReadinessScope.Should().Contain("2 file(s) staged");
        viewModel.CanExecuteCleanup.Should().BeTrue();
        viewModel.RequestExecuteCleanupCommand.CanExecute(null).Should().BeTrue();

        viewModel.RequestExecuteCleanupCommand.Execute(null);

        viewModel.IsCleanupConfirmationVisible.Should().BeTrue();
        viewModel.ConfirmExecuteCleanupCommand.CanExecute(null).Should().BeTrue();

        viewModel.CancelExecuteCleanupCommand.Execute(null);

        viewModel.IsCleanupConfirmationVisible.Should().BeFalse();
        viewModel.CleanupReadinessDetail.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ConfirmExecuteCleanupCommand_AfterPreview_ShouldExecuteAndResetPreview()
    {
        var service = new FakeAdminMaintenanceService
        {
            PreviewResult = new CleanupPreviewResult
            {
                Success = true,
                TotalFiles = 1,
                TotalBytes = 4096,
                FilesToDelete =
                [
                    new CleanupFileInfo { Path = "artifacts/tmp/orphan.tmp", Reason = "temp", SizeBytes = 4096 }
                ]
            },
            ExecuteResult = new MaintenanceCleanupResult
            {
                Success = true,
                FilesDeleted = 1,
                BytesFreed = 4096
            }
        };
        var viewModel = new AdminMaintenanceViewModel(service);

        await viewModel.PreviewCleanupAsync();
        viewModel.RequestExecuteCleanupCommand.Execute(null);
        await viewModel.ConfirmExecuteCleanupCommand.ExecuteAsync(null);

        service.ExecuteCalls.Should().Be(1);
        service.LastExecuteOptions.Should().NotBeNull();
        service.LastExecuteOptions!.DeleteTempFiles.Should().BeTrue();
        viewModel.IsCleanupResultsVisible.Should().BeFalse();
        viewModel.IsCleanupConfirmationVisible.Should().BeFalse();
        viewModel.CanExecuteCleanup.Should().BeFalse();
        viewModel.CleanupItems.Should().BeEmpty();
        viewModel.CleanupReadinessTitle.Should().Be("Cleanup complete");
        viewModel.CleanupReadinessDetail.Should().Contain("1 file(s) were deleted");
        viewModel.StatusTitle.Should().Be("Success");
    }

    [Fact]
    public async Task PreviewCleanupAsync_WithNoCandidates_ShouldBlockExecution()
    {
        var viewModel = new AdminMaintenanceViewModel(new FakeAdminMaintenanceService
        {
            PreviewResult = new CleanupPreviewResult
            {
                Success = true,
                TotalFiles = 0,
                TotalBytes = 0
            }
        });

        await viewModel.PreviewCleanupAsync();

        viewModel.CleanupItems.Should().BeEmpty();
        viewModel.IsCleanupResultsVisible.Should().BeTrue();
        viewModel.CanExecuteCleanup.Should().BeFalse();
        viewModel.RequestExecuteCleanupCommand.CanExecute(null).Should().BeFalse();
        viewModel.CleanupReadinessTitle.Should().Be("No cleanup files found");
        viewModel.CleanupReadinessDetail.Should().Contain("found no temp files");

        await viewModel.ExecuteCleanupAsync();

        viewModel.CleanupReadinessTitle.Should().Be("Preview cleanup first");
    }

    [Fact]
    public void AdminMaintenancePageSource_ShouldBindCleanupActionsThroughViewModel()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\AdminMaintenancePage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\AdminMaintenancePage.xaml.cs"));

        xaml.Should().Contain("AutomationProperties.Name=\"Admin maintenance cleanup readiness\"");
        xaml.Should().Contain("Text=\"{Binding CleanupReadinessTitle}\"");
        xaml.Should().Contain("Text=\"{Binding CleanupReadinessDetail}\"");
        xaml.Should().Contain("Text=\"{Binding CleanupReadinessScope}\"");
        xaml.Should().Contain("Command=\"{Binding PreviewCleanupCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RequestExecuteCleanupCommand}\"");
        xaml.Should().Contain("Visibility=\"{Binding IsCleanupConfirmationVisible");
        xaml.Should().Contain("Command=\"{Binding ConfirmExecuteCleanupCommand}\"");
        xaml.Should().Contain("Command=\"{Binding CancelExecuteCleanupCommand}\"");
        xaml.Should().NotContain("Click=\"PreviewCleanup_Click\"");
        xaml.Should().NotContain("Click=\"ExecuteCleanup_Click\"");

        codeBehind.Should().Contain("new AdminMaintenanceViewModel");
        codeBehind.Should().Contain("DataContext = _viewModel");
        codeBehind.Should().NotContain("PreviewCleanup_Click");
        codeBehind.Should().NotContain("ExecuteCleanup_Click");
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

    private sealed class FakeAdminMaintenanceService : IAdminMaintenanceService
    {
        public CleanupPreviewResult PreviewResult { get; set; } = new() { Success = true };
        public MaintenanceCleanupResult ExecuteResult { get; set; } = new() { Success = true };
        public int PreviewCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public CleanupOptions? LastPreviewOptions { get; private set; }
        public CleanupOptions? LastExecuteOptions { get; private set; }

        public Task<MaintenanceScheduleResult> GetMaintenanceScheduleAsync(CancellationToken ct = default) => NotUsed<MaintenanceScheduleResult>();

        public Task<OperationResult> UpdateMaintenanceScheduleAsync(MaintenanceScheduleConfig schedule, CancellationToken ct = default) =>
            NotUsed<OperationResult>();

        public Task<MaintenanceRunResult> RunMaintenanceNowAsync(MaintenanceRunOptions? options = null, CancellationToken ct = default) =>
            NotUsed<MaintenanceRunResult>();

        public Task<MaintenanceRunResult> GetMaintenanceRunStatusAsync(string runId, CancellationToken ct = default) =>
            NotUsed<MaintenanceRunResult>();

        public Task<MaintenanceHistoryResult> GetMaintenanceHistoryAsync(int limit = 20, CancellationToken ct = default) =>
            NotUsed<MaintenanceHistoryResult>();

        public Task<TierConfigResult> GetTierConfigurationAsync(CancellationToken ct = default) => NotUsed<TierConfigResult>();

        public Task<OperationResult> UpdateTierConfigurationAsync(
            List<StorageTierConfig> tiers,
            bool autoMigrationEnabled,
            string? migrationSchedule = null,
            CancellationToken ct = default) =>
            NotUsed<OperationResult>();

        public Task<TierMigrationResult> MigrateToTierAsync(
            string targetTier,
            TierMigrationOptions? options = null,
            CancellationToken ct = default) =>
            NotUsed<TierMigrationResult>();

        public Task<TierUsageResult> GetTierUsageAsync(CancellationToken ct = default) => NotUsed<TierUsageResult>();

        public Task<RetentionPoliciesResult> GetRetentionPoliciesAsync(CancellationToken ct = default) =>
            NotUsed<RetentionPoliciesResult>();

        public Task<OperationResult> SaveRetentionPolicyAsync(StorageRetentionPolicy policy, CancellationToken ct = default) =>
            NotUsed<OperationResult>();

        public Task<OperationResult> DeleteRetentionPolicyAsync(string policyId, CancellationToken ct = default) =>
            NotUsed<OperationResult>();

        public Task<RetentionApplyResult> ApplyRetentionPoliciesAsync(bool dryRun = false, CancellationToken ct = default) =>
            NotUsed<RetentionApplyResult>();

        public Task<CleanupPreviewResult> PreviewCleanupAsync(CleanupOptions options, CancellationToken ct = default)
        {
            PreviewCalls++;
            LastPreviewOptions = options;
            return Task.FromResult(PreviewResult);
        }

        public Task<MaintenanceCleanupResult> ExecuteCleanupAsync(CleanupOptions options, CancellationToken ct = default)
        {
            ExecuteCalls++;
            LastExecuteOptions = options;
            return Task.FromResult(ExecuteResult);
        }

        public Task<PermissionValidationResult> ValidatePermissionsAsync(CancellationToken ct = default) =>
            NotUsed<PermissionValidationResult>();

        public Task<SelfTestResult> RunSelfTestAsync(SelfTestOptions? options = null, CancellationToken ct = default) =>
            NotUsed<SelfTestResult>();

        public Task<ErrorCodesResult> GetErrorCodesAsync(CancellationToken ct = default) => NotUsed<ErrorCodesResult>();

        public Task<ShowConfigResult> ShowConfigAsync(CancellationToken ct = default) => NotUsed<ShowConfigResult>();

        public Task<QuickCheckResult> RunQuickCheckAsync(CancellationToken ct = default) => NotUsed<QuickCheckResult>();

        private static Task<T> NotUsed<T>() =>
            Task.FromException<T>(new NotSupportedException("This fake only supports cleanup calls."));
    }
}
