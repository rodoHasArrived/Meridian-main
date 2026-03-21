using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfServices = Meridian.Wpf.Services;

using Meridian.Ui.Services;
namespace Meridian.Wpf.Views;

/// <summary>
/// Page for administrative and maintenance operations including
/// archive scheduling, tier migration, retention policies, and cleanup.
/// </summary>
public partial class AdminMaintenancePage : Page
{
    private readonly AdminMaintenanceServiceBase _adminService;
    private bool _isLoaded;

    public AdminMaintenancePage(AdminMaintenanceServiceBase adminService)
    {
        InitializeComponent();
        _adminService = adminService;

        Loaded += AdminMaintenancePage_Loaded;
    }

    private async void AdminMaintenancePage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        await LoadMaintenanceScheduleAsync();
        await LoadTierUsageAsync();
        await LoadRetentionPoliciesAsync();
        await LoadMaintenanceHistoryAsync();
    }

    #region Quick Check

    private async void QuickCheck_Click(object sender, RoutedEventArgs e)
    {
        QuickCheckButton.IsEnabled = false;
        try
        {
            var result = await _adminService.RunQuickCheckAsync();
            if (result.Success)
            {
                ShowQuickCheckResults(result);
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
            QuickCheckButton.IsEnabled = true;
        }
    }

    private void ShowQuickCheckResults(QuickCheckResult result)
    {
        QuickCheckResultsCard.Visibility = Visibility.Visible;

        var isOk = result.Overall == "OK" || result.Overall == "Healthy";
        QuickCheckIcon.Text = isOk ? "\uE73E" : "\uE7BA";
        QuickCheckIcon.Foreground = new SolidColorBrush(
            isOk ? Color.FromRgb(72, 187, 120) : Color.FromRgb(237, 137, 54));
        QuickCheckStatusText.Text = isOk ? "System Healthy" : "Issues Detected";
        QuickCheckOverallText.Text = result.Overall;

        var items = result.Checks.Select(c => new QuickCheckDisplayItem
        {
            Name = c.Name,
            Details = c.Details ?? "",
            StatusIcon = c.Status == "OK" || c.Status == "Pass" ? "\uE73E" : "\uE7BA",
            StatusColor = new SolidColorBrush(
                c.Status == "OK" || c.Status == "Pass"
                    ? Color.FromRgb(72, 187, 120)
                    : Color.FromRgb(237, 137, 54))
        }).ToList();

        QuickCheckList.ItemsSource = items;
    }

    #endregion

    #region Maintenance Schedule

    private async System.Threading.Tasks.Task LoadMaintenanceScheduleAsync()
    {
        try
        {
            var result = await _adminService.GetMaintenanceScheduleAsync();
            if (result.Success && result.Schedule != null)
            {
                EnableScheduleToggle.IsChecked = result.Schedule.Enabled;

                // Select matching schedule
                foreach (ComboBoxItem item in ScheduleFrequencyCombo.Items)
                {
                    if (item.Tag?.ToString() == result.Schedule.CronExpression)
                    {
                        ScheduleFrequencyCombo.SelectedItem = item;
                        break;
                    }
                }

                NextRunText.Text = result.Schedule.NextRunTime?.ToString("g") ?? "Not scheduled";
                LastRunText.Text = result.Schedule.LastRunTime?.ToString("g") ?? "Never";

                // Set operation checkboxes
                var ops = result.Schedule.EnabledOperations;
                RunCompressionCheck.IsChecked = ops.Contains("compression");
                RunCleanupCheck.IsChecked = ops.Contains("cleanup");
                RunIntegrityCheck.IsChecked = ops.Contains("integrity");
                RunTierMigrationCheck.IsChecked = ops.Contains("tiermigration");
            }
        }
        catch
        {
            // Use defaults
        }
    }

    private async void EnableSchedule_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            await SaveScheduleAsync();
        }
    }

    private async void ScheduleFrequency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded)
        {
            await SaveScheduleAsync();
        }
    }

    private async void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        await SaveScheduleAsync();
        ShowSuccess("Schedule saved successfully.");
    }

    private async System.Threading.Tasks.Task SaveScheduleAsync()
    {
        try
        {
            var cronExpression = (ScheduleFrequencyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0 2 * * *";

            var config = new MaintenanceScheduleConfig
            {
                Enabled = EnableScheduleToggle.IsChecked == true,
                CronExpression = cronExpression,
                RunCompression = RunCompressionCheck.IsChecked == true,
                RunCleanup = RunCleanupCheck.IsChecked == true,
                RunIntegrityCheck = RunIntegrityCheck.IsChecked == true,
                RunTierMigration = RunTierMigrationCheck.IsChecked == true
            };

            await _adminService.UpdateMaintenanceScheduleAsync(config);
        }
        catch (Exception ex)
        {
            ShowError("Failed to save schedule", ex.Message);
        }
    }

    private async void RunMaintenance_Click(object sender, RoutedEventArgs e)
    {
        RunMaintenanceButton.IsEnabled = false;
        try
        {
            var options = new MaintenanceRunOptions
            {
                RunCompression = RunCompressionCheck.IsChecked == true,
                RunCleanup = RunCleanupCheck.IsChecked == true,
                RunIntegrityCheck = RunIntegrityCheck.IsChecked == true,
                RunTierMigration = RunTierMigrationCheck.IsChecked == true
            };

            var result = await _adminService.RunMaintenanceNowAsync(options);

            if (result.Success)
            {
                ShowSuccess($"Maintenance started. Run ID: {result.RunId}");
                await LoadMaintenanceHistoryAsync();
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
            RunMaintenanceButton.IsEnabled = true;
        }
    }

    #endregion

    #region Storage Tiers

    private async System.Threading.Tasks.Task LoadTierUsageAsync()
    {
        try
        {
            var result = await _adminService.GetTierUsageAsync();
            if (result.Success)
            {
                var items = result.TierUsage.Select(t => new TierDisplayItem
                {
                    Name = t.TierName,
                    Path = "",
                    SizeText = FormatHelpers.FormatBytes(t.SizeBytes),
                    FileCountText = $"{t.FileCount:N0} files",
                    RetentionText = $"{t.PercentOfTotal:F1}% of total"
                }).ToList();

                TiersList.ItemsSource = items;
            }
        }
        catch
        {
            // Show default tiers
            TiersList.ItemsSource = new List<TierDisplayItem>
            {
                new() { Name = "Hot (Live)", SizeText = "-- GB", FileCountText = "-- files", RetentionText = "Real-time data" },
                new() { Name = "Warm (Historical)", SizeText = "-- GB", FileCountText = "-- files", RetentionText = "Recent data" },
                new() { Name = "Cold (Archive)", SizeText = "-- GB", FileCountText = "-- files", RetentionText = "Compressed archive" }
            };
        }
    }

    private async void MigrateNow_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will compress and migrate older data to the archive tier. Continue?",
            "Migrate to Archive",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var migrationResult = await _adminService.MigrateToTierAsync("archive", new TierMigrationOptions
            {
                OlderThan = DateOnly.FromDateTime(DateTime.Today.AddDays(-30))
            });

            if (migrationResult.Success)
            {
                ShowSuccess($"Migration complete. {migrationResult.FilesProcessed} files migrated, {FormatHelpers.FormatBytes(migrationResult.SpaceSavedBytes)} saved.");
                await LoadTierUsageAsync();
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

    #endregion

    #region Retention Policies

    private async System.Threading.Tasks.Task LoadRetentionPoliciesAsync()
    {
        try
        {
            var result = await _adminService.GetRetentionPoliciesAsync();
            if (result.Success)
            {
                var items = result.Policies.Select(p => new RetentionPolicyDisplayItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.SymbolPattern ?? "All symbols",
                    RetentionText = $"{p.RetentionDays} days",
                    Enabled = p.Enabled
                }).ToList();

                PoliciesList.ItemsSource = items;
            }
        }
        catch
        {
            // Show default policies
            PoliciesList.ItemsSource = new List<RetentionPolicyDisplayItem>
            {
                new() { Name = "Default Policy", Description = "All symbols", RetentionText = "90 days", Enabled = true },
                new() { Name = "Archive Policy", Description = "Archived data", RetentionText = "365 days", Enabled = true }
            };
        }
    }

    private async void AddPolicy_Click(object sender, RoutedEventArgs e)
    {
        ShowInfo("Add Policy", "Policy editor dialog will be implemented here.");
        await LoadRetentionPoliciesAsync();
    }

    private void EditPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string policyId)
        {
            ShowInfo("Edit Policy", $"Editing policy: {policyId}");
        }
    }

    private async void DeletePolicy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string policyId)
        {
            var result = MessageBox.Show(
                "Are you sure you want to delete this policy?",
                "Delete Policy",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var deleteResult = await _adminService.DeleteRetentionPolicyAsync(policyId);
                if (deleteResult.Success)
                {
                    await LoadRetentionPoliciesAsync();
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
    }

    private async void ApplyRetention_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will delete data older than the retention period. This action cannot be undone. Continue?",
            "Apply Retention Policies",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var applyResult = await _adminService.ApplyRetentionPoliciesAsync(dryRun: false);
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

    private async void PreviewCleanup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _adminService.PreviewCleanupAsync(new CleanupOptions
            {
                DeleteEmptyDirectories = true,
                DeleteTempFiles = true
            });

            if (result.Success)
            {
                CleanupResultsCard.Visibility = Visibility.Visible;
                CleanupFilesText.Text = result.TotalFiles.ToString();
                CleanupSizeText.Text = FormatHelpers.FormatBytes(result.TotalBytes);

                var items = result.FilesToDelete.Select(f => new CleanupFileDisplayItem
                {
                    Path = f.Path,
                    SizeText = FormatHelpers.FormatBytes(f.SizeBytes),
                    Reason = f.Reason
                }).ToList();

                CleanupFilesList.ItemsSource = items;
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

    private async void ExecuteCleanup_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will permanently delete the listed files. Continue?",
            "Execute Cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var cleanupResult = await _adminService.ExecuteCleanupAsync(new CleanupOptions
            {
                DeleteEmptyDirectories = true,
                DeleteTempFiles = true
            });

            if (cleanupResult.Success)
            {
                ShowSuccess($"Cleanup complete. {cleanupResult.FilesDeleted} files deleted, {FormatHelpers.FormatBytes(cleanupResult.BytesFreed)} freed.");
                CleanupResultsCard.Visibility = Visibility.Collapsed;
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

    #endregion

    #region Maintenance History

    private async System.Threading.Tasks.Task LoadMaintenanceHistoryAsync()
    {
        try
        {
            var result = await _adminService.GetMaintenanceHistoryAsync(limit: 10);
            if (result.Success)
            {
                var items = result.Runs.Select(r => new MaintenanceHistoryItem
                {
                    RunId = r.RunId,
                    TimeText = r.StartTime.ToString("g"),
                    OperationsText = $"{r.OperationsCompleted} completed, {r.OperationsFailed} failed",
                    DurationText = r.EndTime.HasValue
                        ? $"{(r.EndTime.Value - r.StartTime).TotalMinutes:F1} min"
                        : "In progress",
                    StatusIcon = r.Status == "Completed" ? "\uE73E" : (r.Status == "Failed" ? "\uEA39" : "\uE895"),
                    StatusColor = new SolidColorBrush(
                        r.Status == "Completed"
                            ? Color.FromRgb(72, 187, 120)
                            : (r.Status == "Failed"
                                ? Color.FromRgb(245, 101, 101)
                                : Color.FromRgb(88, 166, 255)))
                }).ToList();

                HistoryList.ItemsSource = items;
            }
        }
        catch
        {
            HistoryList.ItemsSource = null;
        }
    }

    #endregion

    #region Helpers

    private void ShowSuccess(string message)
    {
        StatusInfoBar.Visibility = Visibility.Visible;
        StatusInfoIcon.Text = "\uE73E";
        StatusInfoIcon.Foreground = new SolidColorBrush(Color.FromRgb(72, 187, 120));
        StatusInfoTitle.Text = "Success";
        StatusInfoMessage.Text = message;
    }

    private void ShowError(string title, string message)
    {
        StatusInfoBar.Visibility = Visibility.Visible;
        StatusInfoIcon.Text = "\uEA39";
        StatusInfoIcon.Foreground = new SolidColorBrush(Color.FromRgb(245, 101, 101));
        StatusInfoTitle.Text = title;
        StatusInfoMessage.Text = message;
    }

    private void ShowInfo(string title, string message)
    {
        StatusInfoBar.Visibility = Visibility.Visible;
        StatusInfoIcon.Text = "\uE946";
        StatusInfoIcon.Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255));
        StatusInfoTitle.Text = title;
        StatusInfoMessage.Text = message;
    }

    private void CloseInfoBar_Click(object sender, RoutedEventArgs e)
    {
        StatusInfoBar.Visibility = Visibility.Collapsed;
    }


    #endregion
}

#region Display Items

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

#endregion
