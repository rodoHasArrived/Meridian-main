using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    private readonly WpfServices.WorkspaceService _workspaceService;
    private readonly BackfillViewModel _viewModel;

    public BackfillPage(
        WpfServices.WorkspaceService workspaceService,
        BackfillViewModel viewModel)
    {
        InitializeComponent();

        _workspaceService = workspaceService;
        _viewModel = viewModel;

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
        // M1: delegate text update to the ViewModel instead of writing to a named element directly.
        _viewModel.UpdateSymbolCount(symbols.Length);
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

        // M1: delegate hint text update to the ViewModel instead of writing to a named element.
        var lookbackDays = (int)(toDate - fromDate).TotalDays;
        _viewModel.UpdateDateRangeHint(lookbackDays, symbolCount, BackfillViewModel.GetGranularityDisplay(granularity));
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
        SymbolsBox.Text = await _viewModel.GetSubscribedSymbolsTextAsync();
    }

    private void AddMajorETFs_Click(object sender, RoutedEventArgs e)
    {
        SymbolsBox.Text = _viewModel.AppendSymbols(SymbolsBox.Text, "SPY", "QQQ", "IWM");
    }

    private async void UpdateLatest_Click(object sender, RoutedEventArgs e)
    {
        var subscribedSymbols = await _viewModel.GetSubscribedSymbolsTextAsync();
        var preset = _viewModel.BuildLatestUpdatePreset(subscribedSymbols);
        FromDatePicker.SelectedDate = preset.From;
        ToDatePicker.SelectedDate = preset.To;
        SymbolsBox.Text = preset.SymbolsText;

        // M3: notification belongs in ViewModel.
        _viewModel.HandleUpdateToLatest();
    }

    // ── Backfill control ─────────────────────────────────────────────────────

    private async void StartBackfill_Click(object sender, RoutedEventArgs e)
    {
        var provider = (ProviderCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var granularity = (GranularityCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        if (!_viewModel.TryCreateStartRequest(
                SymbolsBox.Text,
                FromDatePicker.SelectedDate,
                ToDatePicker.SelectedDate,
                provider,
                granularity,
                out var request,
                out var symbolValidationError,
                out var fromDateValidationError,
                out var toDateValidationError))
        {
            SymbolsValidationError.Text = symbolValidationError ?? string.Empty;
            SymbolsValidationError.Visibility = string.IsNullOrWhiteSpace(symbolValidationError)
                ? Visibility.Collapsed
                : Visibility.Visible;

            FromDateValidationError.Text = fromDateValidationError ?? string.Empty;
            FromDateValidationError.Visibility = string.IsNullOrWhiteSpace(fromDateValidationError)
                ? Visibility.Collapsed
                : Visibility.Visible;

            ToDateValidationError.Text = toDateValidationError ?? string.Empty;
            ToDateValidationError.Visibility = string.IsNullOrWhiteSpace(toDateValidationError)
                ? Visibility.Collapsed
                : Visibility.Visible;
            return;
        }

        SymbolsValidationError.Visibility = Visibility.Collapsed;
        FromDateValidationError.Visibility = Visibility.Collapsed;
        ToDateValidationError.Visibility = Visibility.Collapsed;

        await _viewModel.StartBackfillAsync(
            request.Symbols,
            request.Provider,
            request.FromDate,
            request.ToDate,
            request.Granularity);
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

        // M3: notification belongs in ViewModel.
        _viewModel.HandleRefreshStatusNotification();
    }

    // ── Validation / data ops ────────────────────────────────────────────────

    private async void ValidateData_Click(object sender, RoutedEventArgs e)
    {
        var symbols = SymbolsBox.Text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-30);
        var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;
        await _viewModel.ValidateDataAsync(symbols, fromDate, toDate);
    }

    private async void RepairGaps_Click(object sender, RoutedEventArgs e)
    {
        var symbols = SymbolsBox.Text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-30);
        var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;
        await _viewModel.RepairGapsAsync(symbols, fromDate, toDate);
    }

    private void OpenWizard_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NavigateToWizard();
    }

    private async void FillAllGaps_Click(object sender, RoutedEventArgs e)
    {
        var symbols = SymbolsBox.Text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-30);
        var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;
        await _viewModel.FillAllGapsAsync(symbols, fromDate, toDate);
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

    private async void AutoFillGaps_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.AutoFillGapsAsync();
    }

    // ── Schedule management ──────────────────────────────────────────────────

    private void ScheduledBackfill_Toggled(object sender, RoutedEventArgs e)
    {
        // M1: delegate opacity to the ViewModel; bind ScheduleSettingsPanel.Opacity in XAML.
        _viewModel.SetScheduleEnabled(ScheduledBackfillToggle.IsChecked.GetValueOrDefault());
    }

    private async void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        var frequency = (ScheduleFrequencyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";
        var timeText = ScheduleTimeBox.Text?.Trim() ?? "06:00";
        await _viewModel.SaveScheduleAsync(
            ScheduledBackfillToggle.IsChecked.GetValueOrDefault(),
            frequency,
            timeText,
            ScheduleAllSymbolsCheck.IsChecked.GetValueOrDefault(),
            ScheduleSymbolsBox.Text ?? string.Empty);
    }

    private async void RunScheduledJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ScheduledJobInfo job)
        {
            await _viewModel.RunScheduledJobAsync(job);
        }
    }

    private async void EditScheduledJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ScheduledJobInfo job)
        {
            var dialog = new EditScheduledJobDialog(job);
            if (dialog.ShowDialog() == true)
            {
                if (dialog.ShouldDelete)
                {
                    await _viewModel.DeleteScheduledJobAsync(job);
                }
                else
                {
                    await _viewModel.UpdateScheduledJobAsync(
                        job,
                        dialog.JobName,
                        dialog.FrequencyTag,
                        dialog.RunTimeText);
                }
            }
        }
    }
}
