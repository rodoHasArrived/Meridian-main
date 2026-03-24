using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Retention assurance page for managing retention policies, guardrails,
/// legal holds, and cleanup operations with full audit trail.
/// </summary>
public partial class RetentionAssurancePage : Page
{
    private readonly WpfServices.RetentionAssuranceService _retentionService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly ObservableCollection<LegalHoldItem> _legalHolds = new();
    private readonly ObservableCollection<AuditReportItem> _auditReports = new();
    private readonly ObservableCollection<ValidationResultItem> _validationResults = new();
    private RetentionDryRunResult? _lastDryRun;

    public RetentionAssurancePage()
    {
        InitializeComponent();

        _retentionService = WpfServices.RetentionAssuranceService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;

        LegalHoldsList.ItemsSource = _legalHolds;
        AuditReportsList.ItemsSource = _auditReports;
        ResultsList.ItemsSource = _validationResults;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LoadConfiguration();
        LoadLegalHolds();
        LoadAuditReports();
    }

    private void LoadConfiguration()
    {
        var config = _retentionService.Configuration;
        if (config.Guardrails != null)
        {
            MinTickDaysBox.Text = config.Guardrails.MinTickDataDays.ToString();
            MinBarDaysBox.Text = config.Guardrails.MinBarDataDays.ToString();
            MinQuoteDaysBox.Text = config.Guardrails.MinQuoteDataDays.ToString();
            MaxDailyDeletesBox.Text = config.Guardrails.MaxDailyDeletedFiles.ToString();
            RequireChecksumCheck.IsChecked = config.Guardrails.RequireChecksumVerification;
            RequireDryRunCheck.IsChecked = config.Guardrails.RequireDryRunPreview;
            AllowTradingHoursCheck.IsChecked = config.Guardrails.AllowDeleteDuringTradingHours;
        }
    }

    private void LoadLegalHolds()
    {
        _legalHolds.Clear();

        var holds = _retentionService.LegalHolds;
        foreach (var hold in holds.Where(h => h.IsActive))
        {
            _legalHolds.Add(new LegalHoldItem
            {
                Id = hold.Id,
                Name = hold.Name,
                Reason = hold.Reason,
                SymbolsText = string.Join(", ", hold.Symbols),
                CreatedText = FormatTimestamp(hold.CreatedAt)
            });
        }

        NoHoldsPanel.Visibility = _legalHolds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadAuditReports()
    {
        _auditReports.Clear();

        foreach (var report in _retentionService.AuditReports)
        {
            _auditReports.Add(new AuditReportItem
            {
                StatusText = report.Status.ToString(),
                StatusColor = GetStatusBrush(report.Status),
                Summary = $"{report.DeletedFiles.Count} files deleted",
                SizeText = FormatHelpers.FormatBytes(report.ActualBytesDeleted),
                TimeText = FormatTimestamp(report.ExecutedAt)
            });
        }

        NoAuditsPanel.Visibility = _auditReports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveGuardrails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = _retentionService.Configuration;
            config.Guardrails ??= new RetentionGuardrails();

            if (int.TryParse(MinTickDaysBox.Text, out var minTick)) config.Guardrails.MinTickDataDays = minTick;
            if (int.TryParse(MinBarDaysBox.Text, out var minBar)) config.Guardrails.MinBarDataDays = minBar;
            if (int.TryParse(MinQuoteDaysBox.Text, out var minQuote)) config.Guardrails.MinQuoteDataDays = minQuote;
            if (int.TryParse(MaxDailyDeletesBox.Text, out var maxDeletes)) config.Guardrails.MaxDailyDeletedFiles = maxDeletes;
            config.Guardrails.RequireChecksumVerification = RequireChecksumCheck.IsChecked == true;
            config.Guardrails.RequireDryRunPreview = RequireDryRunCheck.IsChecked == true;
            config.Guardrails.AllowDeleteDuringTradingHours = AllowTradingHoursCheck.IsChecked == true;

            await _retentionService.SaveConfigurationAsync();
            ShowStatus("Guardrails saved successfully.", false);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to save guardrails", ex);
            ShowStatus($"Failed to save guardrails: {ex.Message}", false);
        }
    }

    private void ValidatePolicy_Click(object sender, RoutedEventArgs e)
    {
        var policy = BuildPolicyFromUI();
        var result = _retentionService.ValidateRetentionPolicy(policy);

        _validationResults.Clear();

        foreach (var violation in result.Violations)
        {
            _validationResults.Add(new ValidationResultItem
            {
                Severity = "ERROR",
                Message = violation.Message,
                SeverityColor = (Brush)FindResource("ErrorColorBrush")
            });
        }

        foreach (var warning in result.Warnings)
        {
            _validationResults.Add(new ValidationResultItem
            {
                Severity = "WARN",
                Message = warning.Message,
                SeverityColor = (Brush)FindResource("WarningColorBrush")
            });
        }

        ResultsPanel.Visibility = Visibility.Visible;
        ResultsHeader.Text = "Validation Results";

        if (result.IsValid && _validationResults.Count == 0)
        {
            ResultsText.Text = "Policy is valid. No violations detected.";
            ResultsList.Visibility = Visibility.Collapsed;
        }
        else if (result.IsValid)
        {
            ResultsText.Text = $"Policy is valid with {_validationResults.Count} warning(s).";
            ResultsList.Visibility = Visibility.Visible;
        }
        else
        {
            ResultsText.Text = $"Policy has {result.Violations.Count} violation(s) and {result.Warnings.Count} warning(s).";
            ResultsList.Visibility = Visibility.Visible;
        }
    }

    private async void DryRun_Click(object sender, RoutedEventArgs e)
    {
        DryRunButton.IsEnabled = false;
        ShowStatus("Running dry run...", true);

        try
        {
            var policy = BuildPolicyFromUI();
            var dataRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
            _lastDryRun = await _retentionService.PerformDryRunAsync(policy, dataRoot);

            ResultsPanel.Visibility = Visibility.Visible;
            ResultsHeader.Text = "Dry Run Results";
            ResultsList.Visibility = Visibility.Collapsed;

            if (_lastDryRun.Errors.Any())
            {
                ResultsText.Text = $"Dry run completed with errors:\n{string.Join("\n", _lastDryRun.Errors)}";
            }
            else
            {
                ResultsText.Text = $"Found {_lastDryRun.FilesToDelete.Count} files to delete " +
                                   $"({FormatHelpers.FormatBytes(_lastDryRun.TotalBytesToDelete)})\n" +
                                   $"Skipped: {_lastDryRun.SkippedFiles.Count} files (legal holds)\n" +
                                   $"Symbols affected: {_lastDryRun.BySymbol.Count}";
                ExecuteCleanupButton.IsEnabled = _lastDryRun.FilesToDelete.Count > 0;
            }

            HideStatus();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Dry run failed", ex);
            ShowStatus($"Dry run failed: {ex.Message}", false);
        }
        finally
        {
            DryRunButton.IsEnabled = true;
        }
    }

    private async void ExecuteCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDryRun == null || _lastDryRun.FilesToDelete.Count == 0)
        {
            MessageBox.Show("Please run a dry run first.", "No Dry Run", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"This will permanently delete {_lastDryRun.FilesToDelete.Count} files ({FormatHelpers.FormatBytes(_lastDryRun.TotalBytesToDelete)}).\n\nContinue?",
            "Confirm Cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        ExecuteCleanupButton.IsEnabled = false;
        ShowStatus("Executing cleanup...", true);

        try
        {
            var verifyChecksums = RequireChecksumCheck.IsChecked == true;
            var report = await _retentionService.ExecuteRetentionCleanupAsync(_lastDryRun, verifyChecksums);

            ResultsPanel.Visibility = Visibility.Visible;
            ResultsHeader.Text = "Cleanup Results";
            ResultsList.Visibility = Visibility.Collapsed;
            ResultsText.Text = $"Status: {report.Status}\n" +
                               $"Files deleted: {report.DeletedFiles.Count}\n" +
                               $"Bytes freed: {FormatHelpers.FormatBytes(report.ActualBytesDeleted)}\n" +
                               (report.Errors.Any() ? $"Errors: {string.Join(", ", report.Errors)}\n" : "") +
                               (report.Notes.Any() ? $"Notes: {string.Join(", ", report.Notes)}" : "");

            _lastDryRun = null;
            LoadAuditReports();
            HideStatus();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Cleanup failed", ex);
            ShowStatus($"Cleanup failed: {ex.Message}", false);
        }
        finally
        {
            ExecuteCleanupButton.IsEnabled = false;
        }
    }

    private async void CreateHold_Click(object sender, RoutedEventArgs e)
    {
        var name = HoldNameBox.Text?.Trim();
        var reason = HoldReasonBox.Text?.Trim();
        var symbolsText = HoldSymbolsBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(symbolsText))
        {
            MessageBox.Show("Please enter a hold name and at least one symbol.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var symbols = symbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s));

        try
        {
            await _retentionService.CreateLegalHoldAsync(name, reason ?? "", symbols);
            HoldNameBox.Text = "";
            HoldReasonBox.Text = "";
            HoldSymbolsBox.Text = "";
            LoadLegalHolds();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to create legal hold", ex);
            MessageBox.Show($"Failed to create legal hold: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ReleaseHold_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string holdId)
        {
            var confirm = MessageBox.Show(
                "Release this legal hold? Protected symbols will become eligible for cleanup.",
                "Confirm Release",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    await _retentionService.ReleaseLegalHoldAsync(holdId);
                    LoadLegalHolds();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Failed to release legal hold", ex);
                }
            }
        }
    }

    private RetentionPolicy BuildPolicyFromUI()
    {
        return new RetentionPolicy
        {
            TickDataDays = int.TryParse(TickDataDaysBox.Text, out var tick) ? tick : 30,
            BarDataDays = int.TryParse(BarDataDaysBox.Text, out var bar) ? bar : 365,
            QuoteDataDays = int.TryParse(QuoteDataDaysBox.Text, out var quote) ? quote : 30,
            DeletedFilesPerRun = int.TryParse(FilesPerRunBox.Text, out var files) ? files : 100,
            CompressBeforeDelete = CompressBeforeDeleteCheck.IsChecked == true
        };
    }

    private Brush GetStatusBrush(CleanupStatus status)
    {
        return status switch
        {
            CleanupStatus.Success => (Brush)FindResource("SuccessColorBrush"),
            CleanupStatus.PartialSuccess => (Brush)FindResource("WarningColorBrush"),
            CleanupStatus.Failed or CleanupStatus.FailedVerification => (Brush)FindResource("ErrorColorBrush"),
            CleanupStatus.Cancelled => (Brush)FindResource("ConsoleTextMutedBrush"),
            _ => (Brush)FindResource("InfoColorBrush")
        };
    }

    private void ShowStatus(string message, bool showProgress)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = message;
        StatusProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideStatus()
    {
        StatusPanel.Visibility = Visibility.Collapsed;
    }


    private static string FormatTimestamp(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;
        return elapsed.TotalSeconds switch
        {
            < 60 => "Just now",
            < 3600 => $"{(int)elapsed.TotalMinutes}m ago",
            < 86400 => $"{(int)elapsed.TotalHours}h ago",
            _ => timestamp.ToString("MMM dd, HH:mm")
        };
    }

    public sealed class LegalHoldItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string SymbolsText { get; set; } = string.Empty;
        public string CreatedText { get; set; } = string.Empty;
    }

    public sealed class AuditReportItem
    {
        public string StatusText { get; set; } = string.Empty;
        public Brush StatusColor { get; set; } = Brushes.Gray;
        public string Summary { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public string TimeText { get; set; } = string.Empty;
    }

    public sealed class ValidationResultItem
    {
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Brush SeverityColor { get; set; } = Brushes.Gray;
    }
}
