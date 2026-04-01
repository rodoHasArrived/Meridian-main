using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Admin Maintenance page. Manages schedule configuration,
/// tier usage, retention policies, cleanup preview/execution, and maintenance history.
/// Exposes ObservableCollections that the View wires to its ItemsControl elements.
/// </summary>
public sealed class AdminMaintenanceViewModel : BindableBase
{
    private readonly AdminMaintenanceServiceBase _adminService;

    // ---- Schedule config ----
    private bool _scheduleEnabled;
    private string _cronExpression = "0 2 * * *";
    private bool _runCompression = true;
    private bool _runCleanup = true;
    private bool _runIntegrityCheck = true;
    private bool _runTierMigration;
    private string _nextRunText = "Not scheduled";
    private string _lastRunText = "Never";

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
    private bool _isCleanupResultsVisible;
    private string _cleanupFilesText = "0";
    private string _cleanupSizeText = "0 B";

    // ---- InfoBar ----
    private bool _isStatusVisible;
    private string _statusIcon = string.Empty;
    private Color _statusColor = Color.FromRgb(72, 187, 120);
    private string _statusTitle = string.Empty;
    private string _statusMessage = string.Empty;

    public AdminMaintenanceViewModel(AdminMaintenanceServiceBase adminService)
    {
        _adminService = adminService;
    }

    // ---- Collections ----
    public ObservableCollection<QuickCheckDisplayItem> QuickCheckItems { get; } = new();
    public ObservableCollection<TierDisplayItem> TierItems { get; } = new();
    public ObservableCollection<RetentionPolicyDisplayItem> PolicyItems { get; } = new();
    public ObservableCollection<CleanupFileDisplayItem> CleanupItems { get; } = new();
    public ObservableCollection<MaintenanceHistoryItem> HistoryItems { get; } = new();

    // ---- Schedule config properties ----
    public bool ScheduleEnabled
    {
        get => _scheduleEnabled;
        set => SetProperty(ref _scheduleEnabled, value);
    }

    public string CronExpression
    {
        get => _cronExpression;
        set => SetProperty(ref _cronExpression, value);
    }

    public bool RunCompression
    {
        get => _runCompression;
        set => SetProperty(ref _runCompression, value);
    }

    public bool RunCleanup
    {
        get => _runCleanup;
        set => SetProperty(ref _runCleanup, value);
    }

    public bool RunIntegrityCheck
    {
        get => _runIntegrityCheck;
        set => SetProperty(ref _runIntegrityCheck, value);
    }

    public bool RunTierMigration
    {
        get => _runTierMigration;
        set => SetProperty(ref _runTierMigration, value);
    }

    public string NextRunText
    {
        get => _nextRunText;
        private set => SetProperty(ref _nextRunText, value);
    }

    public string LastRunText
    {
        get => _lastRunText;
        private set => SetProperty(ref _lastRunText, value);
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
        private set => SetProperty(ref _quickCheckIconColor, value);
    }

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
        private set => SetProperty(ref _statusColor, value);
    }

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

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadMaintenanceScheduleAsync(ct);
        await LoadTierUsageAsync(ct);
        await LoadRetentionPoliciesAsync(ct);
        await LoadMaintenanceHistoryAsync(ct);
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
        try
        {
            var config = new MaintenanceScheduleConfig
            {
                Enabled = ScheduleEnabled,
                CronExpression = CronExpression,
                RunCompression = RunCompression,
                RunCleanup = RunCleanup,
                RunIntegrityCheck = RunIntegrityCheck,
                RunTierMigration = RunTierMigration
            };

            await _adminService.UpdateMaintenanceScheduleAsync(config, ct);
        }
        catch (Exception ex)
        {
            ShowError("Failed to save schedule", ex.Message);
        }
    }

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
            }
            else
            {
                ShowError("Preview Failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Preview Failed", ex.Message);
        }
    }

    public async Task ExecuteCleanupAsync(CancellationToken ct = default)
    {
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
                IsCleanupResultsVisible = false;
            }
            else
            {
                ShowError("Cleanup Failed", cleanupResult.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Cleanup Failed", ex.Message);
        }
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
