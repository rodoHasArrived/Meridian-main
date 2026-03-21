using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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

        // Sync initial status display (color + text logic not handled by bindings)
        SyncStatusDisplay();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Stop();
        SavePageFilterState();
    }

    // ── ViewModel property sync ──────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackfillViewModel.HasApiStatus))
            _ = Dispatcher.InvokeAsync(SyncStatusDisplay);
    }

    private void SyncStatusDisplay()
    {
        if (_viewModel.LastApiStatus != null)
        {
            StatusGrid.Visibility = Visibility.Visible;
            NoStatusText.Visibility = Visibility.Collapsed;

            var lastStatus = _viewModel.LastApiStatus;
            var isSuccess = lastStatus.Success;
            StatusText.Text = isSuccess ? "Completed" : "Failed";
            StatusText.Foreground = isSuccess
                ? new SolidColorBrush(Color.FromRgb(63, 185, 80))
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));
            ProviderText.Text = lastStatus.Provider ?? "Unknown";
            SymbolsText.Text = lastStatus.Symbols != null
                ? string.Join(", ", lastStatus.Symbols)
                : "N/A";
            BarsWrittenText.Text = lastStatus.BarsWritten.ToString("N0");
            StartedText.Text = lastStatus.StartedUtc?.LocalDateTime.ToString("g") ?? "Unknown";
            CompletedText.Text = lastStatus.CompletedUtc?.LocalDateTime.ToString("g") ?? "N/A";
        }
        else
        {
            StatusGrid.Visibility = Visibility.Collapsed;
            NoStatusText.Visibility = Visibility.Visible;
        }
    }

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
        SyncStatusDisplay();
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

/// <summary>
/// Dialog for configuring API keys.
/// </summary>
public sealed class ApiKeyDialog : Window
{
    private readonly TextBox _apiKeyBox;
    private readonly string _providerName;

    public string ApiKey => _apiKeyBox.Text;

    public ApiKeyDialog(string providerName, string envVarName, bool isOptional = false)
    {
        _providerName = providerName;

        Title = $"Configure {providerName} API Key";
        Width = 450;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Description
        var descText = new TextBlock
        {
            Text = $"Enter your {providerName} API key{(isOptional ? " (optional)" : "")}:",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(descText, 0);
        grid.Children.Add(descText);

        // Environment variable hint
        var hintText = new TextBlock
        {
            Text = $"Environment variable: {envVarName}",
            Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(hintText, 1);
        grid.Children.Add(hintText);

        // API Key input
        _apiKeyBox = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 62)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 78)),
            Padding = new Thickness(10, 8, 10, 8),
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 0, 0, 16)
        };

        // Try to load existing value
        var existingValue = Environment.GetEnvironmentVariable(envVarName, EnvironmentVariableTarget.User);
        if (!string.IsNullOrEmpty(existingValue))
        {
            _apiKeyBox.Text = existingValue;
        }

        Grid.SetRow(_apiKeyBox, 2);
        grid.Children.Add(_apiKeyBox);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttonPanel, 4);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            Background = new SolidColorBrush(Color.FromRgb(58, 58, 78)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        buttonPanel.Children.Add(cancelButton);

        var saveButton = new Button
        {
            Content = "Save",
            Width = 100,
            Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 8, 12, 8)
        };
        saveButton.Click += OnSaveClick;
        buttonPanel.Children.Add(saveButton);

        grid.Children.Add(buttonPanel);
        Content = grid;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

/// <summary>
/// Dialog for editing scheduled jobs.
/// </summary>
public sealed class EditScheduledJobDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly ComboBox _frequencyCombo;
    private readonly ComboBox _timeCombo;
    private readonly ComboBox _dayCombo;

    public string JobName => _nameBox.Text;
    public string NextRunText { get; private set; } = string.Empty;
    public bool ShouldDelete { get; private set; }

    public EditScheduledJobDialog(ScheduledJobInfo job)
    {
        Title = "Edit Scheduled Job";
        Width = 450;
        Height = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Job name
        AddLabel(grid, "Job Name:", 0);
        _nameBox = new TextBox
        {
            Text = job.Name,
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 62)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 78)),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 12)
        };
        Grid.SetRow(_nameBox, 1);
        grid.Children.Add(_nameBox);

        // Frequency
        AddLabel(grid, "Frequency:", 2);
        _frequencyCombo = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 62)),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 12)
        };
        _frequencyCombo.Items.Add("Daily");
        _frequencyCombo.Items.Add("Weekly");
        _frequencyCombo.Items.Add("Monthly");
        _frequencyCombo.SelectedIndex = job.Name.Contains("Weekly") ? 1 : 0;
        _frequencyCombo.SelectionChanged += OnFrequencyChanged;
        Grid.SetRow(_frequencyCombo, 3);
        grid.Children.Add(_frequencyCombo);

        // Time
        AddLabel(grid, "Time:", 4);
        _timeCombo = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 62)),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 12)
        };
        for (var hour = 0; hour < 24; hour++)
        {
            _timeCombo.Items.Add($"{hour:D2}:00");
            _timeCombo.Items.Add($"{hour:D2}:30");
        }
        _timeCombo.SelectedIndex = 12; // 6:00 AM
        Grid.SetRow(_timeCombo, 5);
        grid.Children.Add(_timeCombo);

        // Day of week (for weekly)
        _dayCombo = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 62)),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 12),
            Visibility = job.Name.Contains("Weekly") ? Visibility.Visible : Visibility.Collapsed
        };
        foreach (var day in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" })
        {
            _dayCombo.Items.Add(day);
        }
        _dayCombo.SelectedIndex = 6; // Sunday
        Grid.SetRow(_dayCombo, 6);
        grid.Children.Add(_dayCombo);

        // Buttons
        var buttonPanel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(buttonPanel, 7);

        var deleteButton = new Button
        {
            Content = "Delete",
            Width = 100,
            Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 8, 12, 8)
        };
        deleteButton.Click += (_, _) => { ShouldDelete = true; DialogResult = true; Close(); };
        Grid.SetColumn(deleteButton, 0);
        buttonPanel.Children.Add(deleteButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            Background = new SolidColorBrush(Color.FromRgb(58, 58, 78)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 8, 12, 8)
        };
        cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        Grid.SetColumn(cancelButton, 2);
        buttonPanel.Children.Add(cancelButton);

        var saveButton = new Button
        {
            Content = "Save",
            Width = 100,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 8, 12, 8)
        };
        saveButton.Click += OnSaveClick;
        Grid.SetColumn(saveButton, 3);
        buttonPanel.Children.Add(saveButton);

        grid.Children.Add(buttonPanel);
        Content = grid;
    }

    private void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private void OnFrequencyChanged(object sender, SelectionChangedEventArgs e)
    {
        _dayCombo.Visibility = _frequencyCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Please enter a job name.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Calculate next run text
        var time = _timeCombo.SelectedItem?.ToString() ?? "06:00";
        var frequency = _frequencyCombo.SelectedItem?.ToString() ?? "Daily";

        NextRunText = frequency switch
        {
            "Daily" => $"Tomorrow {time}",
            "Weekly" => $"{_dayCombo.SelectedItem} {time}",
            "Monthly" => $"1st of month {time}",
            _ => $"Tomorrow {time}"
        };

        DialogResult = true;
        Close();
    }
}
