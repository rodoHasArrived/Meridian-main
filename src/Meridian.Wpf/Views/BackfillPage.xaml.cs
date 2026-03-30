using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Historical data backfill page with provider selection, date ranges, and scheduling.
/// Wired to real BackfillApiService for live execution and progress tracking.
/// Supports job resumability via BackfillCheckpointService.
/// Timer management and service orchestration are delegated to BackfillViewModel.
/// </summary>
public partial class BackfillPage : Page
{
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.WorkspaceService _workspaceService;
    private readonly WpfServices.ConfigService _configService;
    private readonly BackfillViewModel _viewModel;

    public BackfillPage(
        WpfServices.NotificationService notificationService,
        WpfServices.WorkspaceService workspaceService,
        WpfServices.ConfigService configService,
        BackfillViewModel viewModel)
    {
        InitializeComponent();

        _notificationService = notificationService;
        _workspaceService = workspaceService;
        _configService = configService;
        _viewModel = viewModel;

        // Bind lists to ViewModel collections so UI updates automatically
        SymbolProgressList.ItemsSource = _viewModel.SymbolProgress;
        ScheduledJobsList.ItemsSource = _viewModel.ScheduledJobs;
        ResumableJobsList.ItemsSource = _viewModel.ResumableJobs;
        GapAnalysisList.ItemsSource = _viewModel.GapItems;

        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Set default dates first; RestorePageFilterState will override with saved values
        ToDatePicker.SelectedDate = DateTime.Today;
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);

        RestorePageFilterState();
        UpdateProviderPrioritySummary();
        UpdateGranularityHint();

        await _viewModel.StartAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Stop();
        SavePageFilterState();
    }

    // ── ViewModel property sync ──────────────────────────────────────────────

    // ── Data loading helpers ─────────────────────────────────────────────────

    private async Task LoadScheduledJobsAsync(CancellationToken ct = default)
    {
        await _viewModel.LoadScheduledJobsAsync();
    }

    private async Task LoadResumableJobsAsync(CancellationToken ct = default)
    {
        await _viewModel.LoadResumableJobsAsync();
    }

    private async Task RefreshStatusFromApiAsync(CancellationToken ct = default)
    {
        await _viewModel.RefreshStatusFromApiAsync();
    }

    // ── Resume / dismiss ─────────────────────────────────────────────────────

    private async void ResumeJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ResumableJobInfo job)
            return;

        await _viewModel.ResumeJobAsync(job);
    }

    private void DismissJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ResumableJobInfo job)
            return;

        _viewModel.DismissJob(job);
    }

    // ── Symbol helpers ───────────────────────────────────────────────────────

    private void SymbolsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var symbols = SymbolsBox.Text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        SymbolCountText.Text = $"{symbols.Length} symbols";
    }

    private void ProviderPriority_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateProviderPrioritySummary();
    }

    private void GranularityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateGranularityHint();
    }

    private void ApplySmartRange_Click(object sender, RoutedEventArgs e)
    {
        var granularity = (GranularityCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";
        var symbolCount = SymbolsBox.Text?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length ?? 0;

        var (fromDate, toDate) = BackfillViewModel.ComputeSmartRange(granularity, symbolCount);
        ToDatePicker.SelectedDate = toDate;
        FromDatePicker.SelectedDate = fromDate;

        var lookbackDays = (int)(toDate - fromDate).TotalDays;
        DateRangeHintText.Text = $"Smart range applied: last {lookbackDays} days for {Math.Max(symbolCount, 1)} symbol(s) at {BackfillViewModel.GetGranularityDisplay(granularity)} granularity.";
    }

    private void UpdateProviderPrioritySummary()
    {
        var primary = GetProviderName(PrimaryProviderCombo);
        var secondary = GetProviderName(SecondaryProviderCombo);
        var tertiary = GetProviderName(TertiaryProviderCombo);
        _viewModel.UpdateProviderPrioritySummary(primary, secondary, tertiary);
    }

    private void UpdateGranularityHint()
    {
        var granularity = (GranularityCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";
        _viewModel.UpdateGranularityHint(granularity);
    }

    private static string GetProviderName(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    }

    private static string? GetComboSelectedTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }

    private static void SelectComboItemByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    // ── Filter state persistence ─────────────────────────────────────────────

    private const string PageTag = "Backfill";

    private void SavePageFilterState()
    {
        _workspaceService.UpdatePageFilterState(PageTag, "Symbols", SymbolsBox.Text);
        _workspaceService.UpdatePageFilterState(PageTag, "ProviderCombo", GetComboSelectedTag(ProviderCombo) ?? "composite");
        _workspaceService.UpdatePageFilterState(PageTag, "PrimaryProvider", GetComboSelectedTag(PrimaryProviderCombo) ?? "yahoo");
        _workspaceService.UpdatePageFilterState(PageTag, "SecondaryProvider", GetComboSelectedTag(SecondaryProviderCombo) ?? "stooq");
        _workspaceService.UpdatePageFilterState(PageTag, "TertiaryProvider", GetComboSelectedTag(TertiaryProviderCombo) ?? "nasdaq");
        _workspaceService.UpdatePageFilterState(PageTag, "Granularity", GetComboSelectedTag(GranularityCombo) ?? "Daily");
        _workspaceService.UpdatePageFilterState(PageTag, "FromDate", FromDatePicker.SelectedDate?.ToString("yyyy-MM-dd"));
        _workspaceService.UpdatePageFilterState(PageTag, "ToDate", ToDatePicker.SelectedDate?.ToString("yyyy-MM-dd"));
    }

    private void RestorePageFilterState()
    {
        var symbols = _workspaceService.GetPageFilterState(PageTag, "Symbols");
        if (symbols is not null)
            SymbolsBox.Text = symbols;

        var provider = _workspaceService.GetPageFilterState(PageTag, "ProviderCombo");
        if (provider is not null)
            SelectComboItemByTag(ProviderCombo, provider);

        var primary = _workspaceService.GetPageFilterState(PageTag, "PrimaryProvider");
        if (primary is not null)
            SelectComboItemByTag(PrimaryProviderCombo, primary);

        var secondary = _workspaceService.GetPageFilterState(PageTag, "SecondaryProvider");
        if (secondary is not null)
            SelectComboItemByTag(SecondaryProviderCombo, secondary);

        var tertiary = _workspaceService.GetPageFilterState(PageTag, "TertiaryProvider");
        if (tertiary is not null)
            SelectComboItemByTag(TertiaryProviderCombo, tertiary);

        var granularity = _workspaceService.GetPageFilterState(PageTag, "Granularity");
        if (granularity is not null)
            SelectComboItemByTag(GranularityCombo, granularity);

        if (DateTime.TryParse(_workspaceService.GetPageFilterState(PageTag, "FromDate"), out var fromDate))
            FromDatePicker.SelectedDate = fromDate;

        if (DateTime.TryParse(_workspaceService.GetPageFilterState(PageTag, "ToDate"), out var toDate))
            ToDatePicker.SelectedDate = toDate;
    }

    // ── Date helpers ─────────────────────────────────────────────────────────

    private void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        FromDateValidationError.Visibility = Visibility.Collapsed;
        ToDateValidationError.Visibility = Visibility.Collapsed;
    }

    private void Last30Days_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private void Last90Days_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-90);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private void YearToDate_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private void LastYear_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.SelectedDate = DateTime.Today.AddYears(-1);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private void Last5Years_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.SelectedDate = DateTime.Today.AddYears(-5);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    // ── Symbol quick-add ─────────────────────────────────────────────────────

    private async void AddAllSubscribed_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configSymbols = await _configService.GetConfiguredSymbolsAsync();
            if (configSymbols.Length > 0)
            {
                SymbolsBox.Text = string.Join(", ", configSymbols.Select(s => s.Symbol));
            }
            else
            {
                SymbolsBox.Text = "SPY, QQQ, AAPL, MSFT, GOOGL, AMZN, NVDA, META, TSLA";
            }
        }
        catch
        {
            SymbolsBox.Text = "SPY, QQQ, AAPL, MSFT, GOOGL, AMZN, NVDA, META, TSLA";
        }
    }

    private void AddMajorETFs_Click(object sender, RoutedEventArgs e)
    {
        var current = SymbolsBox.Text?.Trim() ?? "";
        var etfs = "SPY, QQQ, IWM";
        SymbolsBox.Text = string.IsNullOrEmpty(current) ? etfs : $"{current}, {etfs}";
    }

    private void UpdateLatest_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-5);
        ToDatePicker.SelectedDate = DateTime.Today;
        AddAllSubscribed_Click(sender, e);

        _notificationService.ShowNotification(
            "Update to Latest",
            "Configured to update all subscribed symbols to latest data.",
            NotificationType.Info);
    }

    // ── Backfill control ─────────────────────────────────────────────────────

    private async void StartBackfill_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SymbolsBox.Text))
        {
            SymbolsValidationError.Text = "Please enter at least one symbol";
            SymbolsValidationError.Visibility = Visibility.Visible;
            return;
        }

        SymbolsValidationError.Visibility = Visibility.Collapsed;

        var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-30);
        var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;

        if (fromDate > toDate)
        {
            FromDateValidationError.Text = "From date must be earlier than To date";
            FromDateValidationError.Visibility = Visibility.Visible;
            ToDateValidationError.Text = "To date must be on or after From date";
            ToDateValidationError.Visibility = Visibility.Visible;
            return;
        }

        FromDateValidationError.Visibility = Visibility.Collapsed;
        ToDateValidationError.Visibility = Visibility.Collapsed;

        var symbols = SymbolsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var provider = (ProviderCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "composite";
        var granularity = (GranularityCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";

        await _viewModel.StartBackfillAsync(symbols, provider, fromDate, toDate, granularity);
    }

    private void PauseBackfill_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PauseOrResumeBackfill();
    }

    private void CancelBackfill_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to cancel the backfill operation?",
            "Cancel Backfill",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.CancelBackfill();
        }
    }

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        await RefreshStatusFromApiAsync();

        _notificationService.ShowNotification(
            "Status Refreshed",
            "Backfill status has been refreshed.",
            NotificationType.Info);
    }

    // ── Validation / data ops ────────────────────────────────────────────────

    private void ValidateData_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowNotification(
            "Data Validation",
            "Starting data validation...",
            NotificationType.Info);
    }

    private void RepairGaps_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowNotification(
            "Gap Repair",
            "Checking for data gaps...",
            NotificationType.Info);
    }

    private void OpenWizard_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NavigateToWizard();
    }

    private void FillAllGaps_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowNotification(
            "Fill Gaps",
            "Analyzing all symbols for gaps...",
            NotificationType.Info);
    }

    private void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NavigateToBrowser();
    }

    // ── API key management ───────────────────────────────────────────────────

    private void SetNasdaqApiKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ApiKeyDialog("Nasdaq Data Link", "NASDAQDATALINK__APIKEY");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ApiKey))
        {
            _viewModel.SetNasdaqApiKey(dialog.ApiKey);
        }
    }

    private void ClearNasdaqApiKey_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearNasdaqApiKey();
    }

    private void SetOpenFigiApiKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ApiKeyDialog("OpenFIGI", "OPENFIGI__APIKEY", isOptional: true);
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ApiKey))
        {
            _viewModel.SetOpenFigiApiKey(dialog.ApiKey);
        }
    }

    private void ClearOpenFigiApiKey_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearOpenFigiApiKey();
    }

    // ── Gap analysis ─────────────────────────────────────────────────────────

    private async void ScanGaps_Click(object sender, RoutedEventArgs e)
    {
        var symbolsText = SymbolsBox.Text?.Trim() ?? "";
        var symbols = symbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-30);
        var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;

        await _viewModel.ScanGapsAsync(symbols, fromDate, toDate);
    }

    private void AutoFillGaps_Click(object sender, RoutedEventArgs e)
    {
        var symbolsWithGaps = _viewModel.GetSymbolsWithGaps();
        if (symbolsWithGaps.Length == 0)
        {
            _notificationService.ShowNotification("No Gaps", "No gaps detected to fill.", NotificationType.Info);
            return;
        }

        SymbolsBox.Text = string.Join(", ", symbolsWithGaps);
        _notificationService.ShowNotification(
            "Gap Fill",
            $"Configured to fill gaps for {symbolsWithGaps.Length} symbols. Press Start Backfill to begin.",
            NotificationType.Info);
    }

    // ── Schedule management ──────────────────────────────────────────────────

    private void ScheduledBackfill_Toggled(object sender, RoutedEventArgs e)
    {
        if (ScheduleSettingsPanel != null)
        {
            ScheduleSettingsPanel.Opacity = ScheduledBackfillToggle.IsChecked.GetValueOrDefault() ? 1.0 : 0.5;
        }
    }

    private void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowNotification(
            "Schedule Saved",
            "Backfill schedule has been saved.",
            NotificationType.Success);
    }

    private void RunScheduledJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ScheduledJobInfo job)
        {
            _notificationService.ShowNotification(
                "Running Job",
                $"Starting scheduled job: {job.Name}",
                NotificationType.Info);
        }
    }

    private void EditScheduledJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ScheduledJobInfo job)
        {
            var dialog = new EditScheduledJobDialog(job);
            if (dialog.ShowDialog() == true)
            {
                if (dialog.ShouldDelete)
                {
                    _viewModel.DeleteScheduledJob(job);

                    _notificationService.ShowNotification(
                        "Job Deleted",
                        $"Scheduled job '{job.Name}' has been deleted.",
                        NotificationType.Success);
                }
                else
                {
                    var index = _viewModel.ScheduledJobs.IndexOf(job);
                    if (index >= 0)
                    {
                        _viewModel.ScheduledJobs[index] = new ScheduledJobInfo
                        {
                            Name = dialog.JobName,
                            NextRun = dialog.NextRunText
                        };
                    }

                    _notificationService.ShowNotification(
                        "Job Updated",
                        $"Scheduled job '{dialog.JobName}' has been updated.",
                        NotificationType.Success);
                }
            }
        }
    }
}
