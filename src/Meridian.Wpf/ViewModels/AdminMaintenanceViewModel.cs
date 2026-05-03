using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Admin Maintenance page. Manages schedule configuration,
/// tier usage, retention policies, cleanup preview/execution, and maintenance history.
/// Exposes ObservableCollections that the View wires to its ItemsControl elements.
/// </summary>
public sealed class AdminMaintenanceViewModel : BindableBase
{
    private readonly IAdminMaintenanceService _adminService;

    // ---- Schedule config ----
    private bool _scheduleEnabled;
    private string _cronExpression = "0 2 * * *";
    private bool _runCompression = true;
    private bool _runCleanup = true;
    private bool _runIntegrityCheck = true;
    private bool _runTierMigration;
    private string _nextRunText = "Not scheduled";
    private string _lastRunText = "Never";
    private string _scheduleReadinessTitle = "Schedule paused";
    private string _scheduleReadinessDetail = "Scheduled maintenance is disabled. Enable it when routine lifecycle work should run automatically.";
    private string _scheduleOperationSummary = "Operations: Compress older files, clean up temp files, verify data integrity";
    private bool _canSaveSchedule = true;

    // ---- Quick-check state ----
    private bool _isQuickCheckBusy;
    private bool _isQuickCheckResultsVisible;
    private string _quickCheckIcon = "\uE73E";
    private Color _quickCheckIconColor = Color.FromRgb(72, 187, 120);
    private string _quickCheckStatusText = string.Empty;
    private string _quickCheckOverallText = string.Empty;

    // ---- Maintenance run ----
    private bool _isMaintenanceBusy;

    // ---- Cleanup ----
    private bool _isCleanupBusy;
    private bool _isCleanupResultsVisible;
    private string _cleanupFilesText = "0";
    private string _cleanupSizeText = "0 B";
    private bool _canExecuteCleanup;
    private bool _isCleanupConfirmationVisible;
    private string _cleanupReadinessTitle = "Preview cleanup before deleting files.";
    private string _cleanupReadinessDetail = "Run a cleanup preview to stage temp files and empty directories before execution is enabled.";
    private string _cleanupReadinessScope = "No cleanup preview loaded.";

    // ---- InfoBar ----
    private bool _isStatusVisible;
    private string _statusIcon = string.Empty;
    private Color _statusColor = Color.FromRgb(72, 187, 120);
    private string _statusTitle = string.Empty;
    private string _statusMessage = string.Empty;

    public AdminMaintenanceViewModel(IAdminMaintenanceService adminService)
    {
        _adminService = adminService;
        SaveScheduleCommand = new AsyncRelayCommand(SaveScheduleWithFeedbackAsync, () => CanSaveSchedule);
        PreviewCleanupCommand = new AsyncRelayCommand(PreviewCleanupAsync, () => !IsCleanupBusy);
        RequestExecuteCleanupCommand = new RelayCommand(RequestExecuteCleanup, () => CanExecuteCleanup && !IsCleanupBusy);
        ConfirmExecuteCleanupCommand = new AsyncRelayCommand(ConfirmExecuteCleanupAsync, () =>
            CanExecuteCleanup && IsCleanupConfirmationVisible && !IsCleanupBusy);
        CancelExecuteCleanupCommand = new RelayCommand(CancelExecuteCleanup, () =>
            IsCleanupConfirmationVisible && !IsCleanupBusy);
        RefreshSchedulePresentation();
    }

    // ---- Collections ----
    public ObservableCollection<QuickCheckDisplayItem> QuickCheckItems { get; } = new();
    public ObservableCollection<TierDisplayItem> TierItems { get; } = new();
    public ObservableCollection<RetentionPolicyDisplayItem> PolicyItems { get; } = new();
    public ObservableCollection<CleanupFileDisplayItem> CleanupItems { get; } = new();
    public ObservableCollection<MaintenanceHistoryItem> HistoryItems { get; } = new();

    // ---- Commands ----

    public IAsyncRelayCommand SaveScheduleCommand { get; }
    public IAsyncRelayCommand PreviewCleanupCommand { get; }
    public IRelayCommand RequestExecuteCleanupCommand { get; }
    public IAsyncRelayCommand ConfirmExecuteCleanupCommand { get; }
    public IRelayCommand CancelExecuteCleanupCommand { get; }

    // ---- Schedule config properties ----
    public bool ScheduleEnabled
    {
        get => _scheduleEnabled;
        set
        {
            if (SetProperty(ref _scheduleEnabled, value))
                RefreshSchedulePresentation();
        }
    }

    public string CronExpression
    {
        get => _cronExpression;
        set
        {
            if (SetProperty(ref _cronExpression, value))
                RefreshSchedulePresentation();
        }
    }

    public bool RunCompression
    {
        get => _runCompression;
        set
        {
            if (SetProperty(ref _runCompression, value))
                RefreshSchedulePresentation();
        }
    }

    public bool RunCleanup
    {
        get => _runCleanup;
        set
        {
            if (SetProperty(ref _runCleanup, value))
                RefreshSchedulePresentation();
        }
    }

    public bool RunIntegrityCheck
    {
        get => _runIntegrityCheck;
        set
        {
            if (SetProperty(ref _runIntegrityCheck, value))
                RefreshSchedulePresentation();
        }
    }

    public bool RunTierMigration
    {
        get => _runTierMigration;
        set
        {
            if (SetProperty(ref _runTierMigration, value))
                RefreshSchedulePresentation();
        }
    }

    public string NextRunText
    {
        get => _nextRunText;
        private set
        {
            if (SetProperty(ref _nextRunText, value))
                RefreshSchedulePresentation();
        }
    }

    public string LastRunText
    {
        get => _lastRunText;
        private set => SetProperty(ref _lastRunText, value);
    }

    public string ScheduleReadinessTitle
    {
        get => _scheduleReadinessTitle;
        private set => SetProperty(ref _scheduleReadinessTitle, value);
    }

    public string ScheduleReadinessDetail
    {
        get => _scheduleReadinessDetail;
        private set => SetProperty(ref _scheduleReadinessDetail, value);
    }

    public string ScheduleOperationSummary
    {
        get => _scheduleOperationSummary;
        private set => SetProperty(ref _scheduleOperationSummary, value);
    }

    public bool CanSaveSchedule
    {
        get => _canSaveSchedule;
        private set
        {
            if (SetProperty(ref _canSaveSchedule, value))
                SaveScheduleCommand.NotifyCanExecuteChanged();
        }
    }

    // ---- Quick-check properties ----
    public bool IsQuickCheckBusy
    {
        get => _isQuickCheckBusy;
        private set => SetProperty(ref _isQuickCheckBusy, value);
    }

    public bool IsQuickCheckResultsVisible
    {
        get => _isQuickCheckResultsVisible;
        private set => SetProperty(ref _isQuickCheckResultsVisible, value);
    }

    public string QuickCheckIcon
    {
        get => _quickCheckIcon;
        private set => SetProperty(ref _quickCheckIcon, value);
    }

    public Color QuickCheckIconColor
    {
        get => _quickCheckIconColor;
        private set
        {
            if (SetProperty(ref _quickCheckIconColor, value))
                RaisePropertyChanged(nameof(QuickCheckIconBrush));
        }
    }

    /// <summary>Brush derived from <see cref="QuickCheckIconColor"/> for direct XAML binding.</summary>
    public SolidColorBrush QuickCheckIconBrush => new(_quickCheckIconColor);

    public string QuickCheckStatusText
    {
        get => _quickCheckStatusText;
        private set => SetProperty(ref _quickCheckStatusText, value);
    }

    public string QuickCheckOverallText
    {
        get => _quickCheckOverallText;
        private set => SetProperty(ref _quickCheckOverallText, value);
    }

    // ---- Maintenance run properties ----
    public bool IsMaintenanceBusy
    {
        get => _isMaintenanceBusy;
        private set => SetProperty(ref _isMaintenanceBusy, value);
    }

    // ---- Cleanup properties ----
    public bool IsCleanupBusy
    {
        get => _isCleanupBusy;
        private set
        {
            if (SetProperty(ref _isCleanupBusy, value))
            {
                PreviewCleanupCommand.NotifyCanExecuteChanged();
                RequestExecuteCleanupCommand.NotifyCanExecuteChanged();
                ConfirmExecuteCleanupCommand.NotifyCanExecuteChanged();
                CancelExecuteCleanupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCleanupResultsVisible
    {
        get => _isCleanupResultsVisible;
        private set => SetProperty(ref _isCleanupResultsVisible, value);
    }

    public string CleanupFilesText
    {
        get => _cleanupFilesText;
        private set => SetProperty(ref _cleanupFilesText, value);
    }

    public string CleanupSizeText
    {
        get => _cleanupSizeText;
        private set => SetProperty(ref _cleanupSizeText, value);
    }

    public bool CanExecuteCleanup
    {
        get => _canExecuteCleanup;
        private set
        {
            if (SetProperty(ref _canExecuteCleanup, value))
            {
                RequestExecuteCleanupCommand.NotifyCanExecuteChanged();
                ConfirmExecuteCleanupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCleanupConfirmationVisible
    {
        get => _isCleanupConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isCleanupConfirmationVisible, value))
            {
                ConfirmExecuteCleanupCommand.NotifyCanExecuteChanged();
                CancelExecuteCleanupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CleanupReadinessTitle
    {
        get => _cleanupReadinessTitle;
        private set => SetProperty(ref _cleanupReadinessTitle, value);
    }

    public string CleanupReadinessDetail
    {
        get => _cleanupReadinessDetail;
        private set => SetProperty(ref _cleanupReadinessDetail, value);
    }

    public string CleanupReadinessScope
    {
        get => _cleanupReadinessScope;
        private set => SetProperty(ref _cleanupReadinessScope, value);
    }

    // ---- InfoBar properties ----
    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        private set => SetProperty(ref _isStatusVisible, value);
    }

    public string StatusIcon
    {
        get => _statusIcon;
        private set => SetProperty(ref _statusIcon, value);
    }

    public Color StatusColor
    {
        get => _statusColor;
        private set
        {
            if (SetProperty(ref _statusColor, value))
                RaisePropertyChanged(nameof(StatusBrush));
        }
    }

    /// <summary>Brush derived from <see cref="StatusColor"/> for direct XAML binding.</summary>
    public SolidColorBrush StatusBrush => new(_statusColor);

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // ---- Init ----

    private bool _isInitialized;

    /// <summary>
    /// True after <see cref="InitializeAsync"/> has completed. Code-behind uses this
    /// instead of a local <c>_isLoaded</c> flag to gate save handlers.
    /// </summary>
    public bool IsInitialized
    {
        get => _isInitialized;
        private set => SetProperty(ref _isInitialized, value);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadMaintenanceScheduleAsync(ct);
        await LoadTierUsageAsync(ct);
        await LoadRetentionPoliciesAsync(ct);
        await LoadMaintenanceHistoryAsync(ct);
        IsInitialized = true;
    }

    // ---- Quick check ----

    public async Task RunQuickCheckAsync(CancellationToken ct = default)
    {
        IsQuickCheckBusy = true;
        try
        {
            var result = await _adminService.RunQuickCheckAsync(ct);
            if (result.Success)
            {
                ApplyQuickCheckResult(result);
            }
            else
            {
                ShowError("Quick Check Failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Quick Check Failed", ex.Message);
        }
        finally
        {
            IsQuickCheckBusy = false;
        }
    }

    private void ApplyQuickCheckResult(QuickCheckResult result)
    {
        IsQuickCheckResultsVisible = true;

        var isOk = result.Overall == "OK" || result.Overall == "Healthy";
        QuickCheckIcon = isOk ? "\uE73E" : "\uE7BA";
        QuickCheckIconColor = isOk ? Color.FromRgb(72, 187, 120) : Color.FromRgb(237, 137, 54);
        QuickCheckStatusText = isOk ? "System Healthy" : "Issues Detected";
        QuickCheckOverallText = result.Overall;

        QuickCheckItems.Clear();
        foreach (var c in result.Checks)
        {
            var isPass = c.Status == "OK" || c.Status == "Pass";
            QuickCheckItems.Add(new QuickCheckDisplayItem
            {
                Name = c.Name,
                Details = c.Details ?? string.Empty,
                StatusIcon = isPass ? "\uE73E" : "\uE7BA",
                StatusColor = new SolidColorBrush(isPass
                    ? Color.FromRgb(72, 187, 120)
                    : Color.FromRgb(237, 137, 54))
            });
        }
    }

    // ---- Schedule ----

    public async Task LoadMaintenanceScheduleAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _adminService.GetMaintenanceScheduleAsync(ct);
            if (result.Success && result.Schedule != null)
            {
                ScheduleEnabled = result.Schedule.Enabled;
                CronExpression = result.Schedule.CronExpression ?? "0 2 * * *";
                NextRunText = result.Schedule.NextRunTime?.ToString("g") ?? "Not scheduled";
                LastRunText = result.Schedule.LastRunTime?.ToString("g") ?? "Never";

                var ops = result.Schedule.EnabledOperations;
                RunCompression = ops.Contains("compression");
                RunCleanup = ops.Contains("cleanup");
                RunIntegrityCheck = ops.Contains("integrity");
                RunTierMigration = ops.Contains("tiermigration");
            }
        }
        catch
        {
            // Use defaults
        }
    }

    public async Task SaveScheduleAsync(CancellationToken ct = default)
    {
        await SaveScheduleCoreAsync(showSuccess: false, ct);
    }

    private async Task SaveScheduleWithFeedbackAsync()
    {
        await SaveScheduleCoreAsync(showSuccess: true, CancellationToken.None);
    }

    private async Task SaveScheduleCoreAsync(bool showSuccess, CancellationToken ct)
    {
        try
        {
            if (!CanSaveSchedule)
            {
                ShowError(
                    "Schedule incomplete",
                    ScheduleEnabled && !HasSelectedScheduleOperations()
                        ? "Select at least one maintenance operation before saving an enabled schedule."
                        : "Choose a valid schedule frequency before saving.");
                return;
            }

            var config = new MaintenanceScheduleConfig
            {
                Enabled = ScheduleEnabled,
                CronExpression = CronExpression,
                RunCompression = RunCompression,
                RunCleanup = RunCleanup,
                RunIntegrityCheck = RunIntegrityCheck,
                RunTierMigration = RunTierMigration
            };

            var result = await _adminService.UpdateMaintenanceScheduleAsync(config, ct);
            if (result.Success)
            {
                if (showSuccess)
                    ShowSuccess("Schedule saved successfully.");
            }
            else
            {
                ShowError("Failed to save schedule", result.Error ?? result.Message ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Failed to save schedule", ex.Message);
        }
    }

    private void RefreshSchedulePresentation()
    {
        var hasOperations = HasSelectedScheduleOperations();
        var hasFrequency = !string.IsNullOrWhiteSpace(CronExpression);

        ScheduleOperationSummary = hasOperations
            ? $"Operations: {string.Join(", ", GetSelectedScheduleOperationLabels())}"
            : "Operations: none selected";

        CanSaveSchedule = !ScheduleEnabled || (hasFrequency && hasOperations);

        if (!ScheduleEnabled)
        {
            ScheduleReadinessTitle = "Schedule paused";
            ScheduleReadinessDetail = "Scheduled maintenance is disabled. Selected operations are retained for the next time automation is enabled.";
            return;
        }

        if (!hasOperations)
        {
            ScheduleReadinessTitle = "Schedule needs an operation";
            ScheduleReadinessDetail = "Select at least one maintenance operation before saving an enabled schedule.";
            return;
        }

        if (!hasFrequency)
        {
            ScheduleReadinessTitle = "Schedule needs a frequency";
            ScheduleReadinessDetail = "Choose a frequency before saving the maintenance schedule.";
            return;
        }

        ScheduleReadinessTitle = "Schedule ready";
        ScheduleReadinessDetail = $"Runs {FormatCronExpression(CronExpression)}. {FormatNextRunDetail()}";
    }

    private bool HasSelectedScheduleOperations() =>
        RunCompression || RunCleanup || RunIntegrityCheck || RunTierMigration;

    private IEnumerable<string> GetSelectedScheduleOperationLabels()
    {
        if (RunCompression)
            yield return "Compress older files";
        if (RunCleanup)
            yield return "clean up temp files";
        if (RunIntegrityCheck)
            yield return "verify data integrity";
        if (RunTierMigration)
            yield return "migrate to archive tier";
    }

    private string FormatNextRunDetail() =>
        string.IsNullOrWhiteSpace(NextRunText) || NextRunText == "Not scheduled"
            ? "Next run will update after save."
            : $"Next run: {NextRunText}.";

    private static string FormatCronExpression(string cronExpression) => cronExpression switch
    {
        "0 2 * * *" => "daily at 2 AM",
        "0 4 * * *" => "daily at 4 AM",
        "0 3 * * 0" => "weekly on Sunday at 3 AM",
        "0 3 * * 6" => "weekly on Saturday at 3 AM",
        "0 3 1 * *" => "monthly on the 1st at 3 AM",
        _ => cronExpression
    };

    // ---- Maintenance run ----

    public async Task RunMaintenanceNowAsync(CancellationToken ct = default)
    {
        IsMaintenanceBusy = true;
        try
        {
            var options = new MaintenanceRunOptions
            {
                RunCompression = RunCompression,
                RunCleanup = RunCleanup,
                RunIntegrityCheck = RunIntegrityCheck,
                RunTierMigration = RunTierMigration
            };

            var result = await _adminService.RunMaintenanceNowAsync(options, ct);

            if (result.Success)
            {
                ShowSuccess($"Maintenance started. Run ID: {result.RunId}");
                await LoadMaintenanceHistoryAsync(ct);
            }
            else
            {
                ShowError("Maintenance Failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Maintenance Failed", ex.Message);
        }
        finally
        {
            IsMaintenanceBusy = false;
        }
    }

    // ---- Tier usage ----

    public async Task LoadTierUsageAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _adminService.GetTierUsageAsync(ct);
            TierItems.Clear();
            if (result.Success)
            {
                foreach (var t in result.TierUsage)
                {
                    TierItems.Add(new TierDisplayItem
                    {
                        Name = t.TierName,
                        Path = string.Empty,
                        SizeText = FormatHelpers.FormatBytes(t.SizeBytes),
                        FileCountText = $"{t.FileCount:N0} files",
                        RetentionText = $"{t.PercentOfTotal:F1}% of total"
                    });
                }
            }
            else
            {
                SetDefaultTierItems();
            }
        }
        catch
        {
            SetDefaultTierItems();
        }
    }

    private void SetDefaultTierItems()
    {
        TierItems.Clear();
        TierItems.Add(new TierDisplayItem { Name = "Hot (Live)", SizeText = "-- GB", FileCountText = "-- files", RetentionText = "Real-time data" });
        TierItems.Add(new TierDisplayItem { Name = "Warm (Historical)", SizeText = "-- GB", FileCountText = "-- files", RetentionText = "Recent data" });
        TierItems.Add(new TierDisplayItem { Name = "Cold (Archive)", SizeText = "-- GB", FileCountText = "-- files", RetentionText = "Compressed archive" });
    }

    public async Task MigrateToArchiveAsync(CancellationToken ct = default)
    {
        try
        {
            var migrationResult = await _adminService.MigrateToTierAsync("archive", new TierMigrationOptions
            {
                OlderThan = DateOnly.FromDateTime(DateTime.Today.AddDays(-30))
            }, ct);

            if (migrationResult.Success)
            {
                ShowSuccess($"Migration complete. {migrationResult.FilesProcessed} files migrated, {FormatHelpers.FormatBytes(migrationResult.SpaceSavedBytes)} saved.");
                await LoadTierUsageAsync(ct);
            }
            else
            {
                ShowError("Migration Failed", migrationResult.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Migration Failed", ex.Message);
        }
    }

    // ---- Retention policies ----

    public async Task LoadRetentionPoliciesAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _adminService.GetRetentionPoliciesAsync(ct);
            PolicyItems.Clear();
            if (result.Success)
            {
                foreach (var p in result.Policies)
                {
                    PolicyItems.Add(new RetentionPolicyDisplayItem
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.SymbolPattern ?? "All symbols",
                        RetentionText = $"{p.RetentionDays} days",
                        Enabled = p.Enabled
                    });
                }
            }
            else
            {
                SetDefaultPolicyItems();
            }
        }
        catch
        {
            SetDefaultPolicyItems();
        }
    }

    private void SetDefaultPolicyItems()
    {
        PolicyItems.Clear();
        PolicyItems.Add(new RetentionPolicyDisplayItem { Name = "Default Policy", Description = "All symbols", RetentionText = "90 days", Enabled = true });
        PolicyItems.Add(new RetentionPolicyDisplayItem { Name = "Archive Policy", Description = "Archived data", RetentionText = "365 days", Enabled = true });
    }

    public void NotifyAddPolicy()
    {
        ShowInfo("Add Policy", "Policy editor dialog will be implemented here.");
    }

    public void NotifyEditPolicy(string policyId)
    {
        ShowInfo("Edit Policy", $"Editing policy: {policyId}");
    }

    public async Task DeletePolicyAsync(string policyId, CancellationToken ct = default)
    {
        try
        {
            var deleteResult = await _adminService.DeleteRetentionPolicyAsync(policyId, ct);
            if (deleteResult.Success)
            {
                await LoadRetentionPoliciesAsync(ct);
                ShowSuccess("Policy deleted.");
            }
            else
            {
                ShowError("Delete Failed", deleteResult.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Delete Failed", ex.Message);
        }
    }

    public async Task ApplyRetentionPoliciesAsync(CancellationToken ct = default)
    {
        try
        {
            var applyResult = await _adminService.ApplyRetentionPoliciesAsync(dryRun: false, ct);
            if (applyResult.Success)
            {
                ShowSuccess($"Retention applied. {applyResult.FilesDeleted} files deleted, {FormatHelpers.FormatBytes(applyResult.BytesFreed)} freed.");
            }
            else
            {
                ShowError("Apply Failed", applyResult.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Apply Failed", ex.Message);
        }
    }

    // ---- Cleanup ----

    public async Task PreviewCleanupAsync(CancellationToken ct = default)
    {
        IsCleanupBusy = true;
        IsCleanupConfirmationVisible = false;
        CleanupReadinessTitle = "Scanning cleanup candidates";
        CleanupReadinessDetail = "Checking temp files and empty directories before anything is deleted.";
        CleanupReadinessScope = "Preview in progress.";

        try
        {
            var result = await _adminService.PreviewCleanupAsync(new CleanupOptions
            {
                DeleteEmptyDirectories = true,
                DeleteTempFiles = true
            }, ct);

            if (result.Success)
            {
                IsCleanupResultsVisible = true;
                CleanupFilesText = result.TotalFiles.ToString();
                CleanupSizeText = FormatHelpers.FormatBytes(result.TotalBytes);

                CleanupItems.Clear();
                foreach (var f in result.FilesToDelete)
                {
                    CleanupItems.Add(new CleanupFileDisplayItem
                    {
                        Path = f.Path,
                        SizeText = FormatHelpers.FormatBytes(f.SizeBytes),
                        Reason = f.Reason
                    });
                }

                CanExecuteCleanup = result.TotalFiles > 0;
                if (CanExecuteCleanup)
                {
                    CleanupReadinessTitle = "Cleanup preview ready";
                    CleanupReadinessDetail =
                        $"{result.TotalFiles:N0} file(s) are staged for deletion. Review the list before executing cleanup.";
                    CleanupReadinessScope = $"{result.TotalFiles:N0} file(s) staged | {FormatHelpers.FormatBytes(result.TotalBytes)} to free";
                }
                else
                {
                    CleanupReadinessTitle = "No cleanup files found";
                    CleanupReadinessDetail = "The preview completed and found no temp files or empty directories to remove.";
                    CleanupReadinessScope = "0 files staged.";
                }
            }
            else
            {
                ClearCleanupPreview();
                CleanupReadinessTitle = "Cleanup preview failed";
                CleanupReadinessDetail = result.Error ?? "Unknown error";
                CleanupReadinessScope = "Preview failed.";
                ShowError("Preview Failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ClearCleanupPreview();
            CleanupReadinessTitle = "Cleanup preview failed";
            CleanupReadinessDetail = ex.Message;
            CleanupReadinessScope = "Preview failed.";
            ShowError("Preview Failed", ex.Message);
        }
        finally
        {
            IsCleanupBusy = false;
        }
    }

    public async Task ExecuteCleanupAsync(CancellationToken ct = default)
    {
        if (!CanExecuteCleanup)
        {
            CleanupReadinessTitle = "Preview cleanup first";
            CleanupReadinessDetail = "Run a cleanup preview and review staged files before executing cleanup.";
            CleanupReadinessScope = "Execution blocked until preview has files.";
            return;
        }

        await ExecuteCleanupCoreAsync(ct);
    }

    private void RequestExecuteCleanup()
    {
        if (!CanExecuteCleanup)
        {
            CleanupReadinessTitle = "Preview cleanup first";
            CleanupReadinessDetail = "Run a cleanup preview and review staged files before executing cleanup.";
            CleanupReadinessScope = "Execution blocked until preview has files.";
            return;
        }

        IsCleanupConfirmationVisible = true;
        CleanupReadinessTitle = "Confirm cleanup execution";
        CleanupReadinessDetail =
            $"{CleanupFilesText} file(s) are staged for permanent deletion. Confirm only after reviewing the file list.";
        CleanupReadinessScope = $"{CleanupFilesText} file(s) staged | {CleanupSizeText} to free";
    }

    private async Task ConfirmExecuteCleanupAsync(CancellationToken ct = default)
    {
        if (!CanExecuteCleanup)
            return;

        await ExecuteCleanupCoreAsync(ct);
    }

    private void CancelExecuteCleanup()
    {
        IsCleanupConfirmationVisible = false;
        CleanupReadinessTitle = "Cleanup preview ready";
        CleanupReadinessDetail = "Execution was cancelled. Review the staged files or run a fresh preview.";
        CleanupReadinessScope = $"{CleanupFilesText} file(s) staged | {CleanupSizeText} to free";
    }

    private async Task ExecuteCleanupCoreAsync(CancellationToken ct)
    {
        IsCleanupBusy = true;
        IsCleanupConfirmationVisible = false;
        CleanupReadinessTitle = "Executing cleanup";
        CleanupReadinessDetail = "Deleting the staged cleanup files and refreshing the cleanup state.";
        CleanupReadinessScope = $"{CleanupFilesText} file(s) staged | {CleanupSizeText} to free";

        try
        {
            var cleanupResult = await _adminService.ExecuteCleanupAsync(new CleanupOptions
            {
                DeleteEmptyDirectories = true,
                DeleteTempFiles = true
            }, ct);

            if (cleanupResult.Success)
            {
                ShowSuccess($"Cleanup complete. {cleanupResult.FilesDeleted} files deleted, {FormatHelpers.FormatBytes(cleanupResult.BytesFreed)} freed.");
                ClearCleanupPreview();
                CleanupReadinessTitle = "Cleanup complete";
                CleanupReadinessDetail =
                    $"{cleanupResult.FilesDeleted:N0} file(s) were deleted and {FormatHelpers.FormatBytes(cleanupResult.BytesFreed)} was freed.";
                CleanupReadinessScope = "Run a new preview before the next cleanup.";
            }
            else
            {
                CleanupReadinessTitle = "Cleanup failed";
                CleanupReadinessDetail = cleanupResult.Error ?? "Unknown error";
                CleanupReadinessScope = $"{CleanupFilesText} file(s) remain staged for retry.";
                ShowError("Cleanup Failed", cleanupResult.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            CleanupReadinessTitle = "Cleanup failed";
            CleanupReadinessDetail = ex.Message;
            CleanupReadinessScope = $"{CleanupFilesText} file(s) remain staged for retry.";
            ShowError("Cleanup Failed", ex.Message);
        }
        finally
        {
            IsCleanupBusy = false;
        }
    }

    private void ClearCleanupPreview()
    {
        CleanupItems.Clear();
        CleanupFilesText = "0";
        CleanupSizeText = "0 B";
        IsCleanupResultsVisible = false;
        CanExecuteCleanup = false;
        IsCleanupConfirmationVisible = false;
    }

    // ---- History ----

    public async Task LoadMaintenanceHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _adminService.GetMaintenanceHistoryAsync(limit: 10, ct);
            HistoryItems.Clear();
            if (result.Success)
            {
                foreach (var r in result.Runs)
                {
                    var completed = r.Status == "Completed";
                    var failed = r.Status == "Failed";
                    HistoryItems.Add(new MaintenanceHistoryItem
                    {
                        RunId = r.RunId,
                        TimeText = r.StartTime.ToString("g"),
                        OperationsText = $"{r.OperationsCompleted} completed, {r.OperationsFailed} failed",
                        DurationText = r.EndTime.HasValue
                            ? $"{(r.EndTime.Value - r.StartTime).TotalMinutes:F1} min"
                            : "In progress",
                        StatusIcon = completed ? "\uE73E" : (failed ? "\uEA39" : "\uE895"),
                        StatusColor = new SolidColorBrush(completed
                            ? Color.FromRgb(72, 187, 120)
                            : (failed ? Color.FromRgb(245, 101, 101) : Color.FromRgb(88, 166, 255)))
                    });
                }
            }
        }
        catch
        {
            // Leave empty
        }
    }

    // ---- InfoBar helpers ----

    public void ShowSuccess(string message) =>
        SetStatus("\uE73E", Color.FromRgb(72, 187, 120), "Success", message);

    public void ShowError(string title, string message) =>
        SetStatus("\uEA39", Color.FromRgb(245, 101, 101), title, message);

    public void ShowInfo(string title, string message) =>
        SetStatus("\uE946", Color.FromRgb(88, 166, 255), title, message);

    public void DismissStatus() => IsStatusVisible = false;

    private void SetStatus(string icon, Color color, string title, string message)
    {
        StatusIcon = icon;
        StatusColor = color;
        StatusTitle = title;
        StatusMessage = message;
        IsStatusVisible = true;
    }
}

// ---- Display item types ----

public sealed class QuickCheckDisplayItem
{
    public string Name { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string StatusIcon { get; set; } = string.Empty;
    public SolidColorBrush? StatusColor { get; set; }
}

public sealed class TierDisplayItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string FileCountText { get; set; } = string.Empty;
    public string RetentionText { get; set; } = string.Empty;
}

public sealed class RetentionPolicyDisplayItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RetentionText { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class CleanupFileDisplayItem
{
    public string Path { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class MaintenanceHistoryItem
{
    public string RunId { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public string OperationsText { get; set; } = string.Empty;
    public string DurationText { get; set; } = string.Empty;
    public string StatusIcon { get; set; } = string.Empty;
    public SolidColorBrush? StatusColor { get; set; }
}
