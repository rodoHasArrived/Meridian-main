using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;
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
    private readonly RetentionAssuranceViewModel _viewModel;
    private readonly ObservableCollection<LegalHoldItem> _legalHolds = new();
    private readonly ObservableCollection<AuditReportItem> _auditReports = new();

    public RetentionAssurancePage()
    {
        InitializeComponent();

        _retentionService = WpfServices.RetentionAssuranceService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;
        _viewModel = new RetentionAssuranceViewModel(new RetentionAssuranceClient(_retentionService));
        _viewModel.CleanupCompleted += (_, _) => LoadAuditReports();
        DataContext = _viewModel;

        LegalHoldsList.ItemsSource = _legalHolds;
        AuditReportsList.ItemsSource = _auditReports;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LoadConfiguration();
        LoadLegalHolds();
        LoadAuditReports();
    }

    private void LoadConfiguration()
    {
        _viewModel.LoadConfiguration(_retentionService.Configuration);
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

    private sealed class RetentionAssuranceClient : IRetentionAssuranceClient
    {
        private readonly WpfServices.RetentionAssuranceService _retentionService;

        public RetentionAssuranceClient(WpfServices.RetentionAssuranceService retentionService)
        {
            _retentionService = retentionService;
        }

        public RetentionConfiguration Configuration => _retentionService.Configuration;

        public Task SaveConfigurationAsync(CancellationToken ct = default) =>
            _retentionService.SaveConfigurationAsync(ct);

        public RetentionValidationResult ValidateRetentionPolicy(RetentionPolicy policy) =>
            _retentionService.ValidateRetentionPolicy(policy);

        public async Task<RetentionDryRunResult> PerformDryRunAsync(
            RetentionPolicy policy,
            CancellationToken ct = default)
        {
            var config = await WpfServices.ConfigService.Instance.LoadConfigAsync();
            var dataRoot = WpfServices.ConfigService.Instance.ResolveDataRoot(config);
            return await _retentionService.PerformDryRunAsync(policy, dataRoot, ct);
        }

        public Task<RetentionAuditReport> ExecuteRetentionCleanupAsync(
            RetentionDryRunResult dryRun,
            bool verifyChecksums,
            CancellationToken ct = default) =>
            _retentionService.ExecuteRetentionCleanupAsync(dryRun, verifyChecksums, ct);
    }
}
