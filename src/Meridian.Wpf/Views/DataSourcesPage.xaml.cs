using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Configuration;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for managing multiple data source configurations.
/// </summary>
public partial class DataSourcesPage : Page
{
    private readonly WpfServices.ConfigService _configService;
    private readonly DataSourcesViewModel _viewModel;
    private string? _editingSourceId;

    private ObservableCollection<DataSourceConfigDto> DataSources => _viewModel.DataSources;

    public DataSourcesPage(WpfServices.ConfigService configService)
    {
        InitializeComponent();
        _configService = configService;
        _viewModel = new DataSourcesViewModel();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadDataSourcesAsync();
    }

    private async Task LoadDataSourcesAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await _configService.GetDataSourcesConfigAsync();

            EnableFailoverToggle.IsChecked = config.EnableFailover;
            FailoverTimeoutBox.Text = config.FailoverTimeoutSeconds.ToString();

            DataSources.Clear();
            var sources = config.Sources ?? Array.Empty<DataSourceConfigDto>();
            foreach (var source in sources)
            {
                DataSources.Add(source);
            }

            UpdateSourceCountText();
            UpdateDefaultSourceCombos(config);
            NoSourcesText.Visibility = DataSources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to load data sources: {ex.Message}", isError: true);
        }
    }

    private void UpdateSourceCountText()
    {
        SourceCountText.Text = $"({DataSources.Count})";
    }

    private void UpdateDefaultSourceCombos(DataSourcesConfigDto config)
    {
        var realTimeSources = DataSources.Where(s => s.Type is "RealTime" or "Both").ToList();
        var historicalSources = DataSources.Where(s => s.Type is "Historical" or "Both").ToList();

        DefaultRealTimeCombo.ItemsSource = realTimeSources;
        DefaultHistoricalCombo.ItemsSource = historicalSources;

        DefaultRealTimeCombo.SelectedItem = realTimeSources.FirstOrDefault(s => s.Id == config.DefaultRealTimeSourceId);
        DefaultHistoricalCombo.SelectedItem = historicalSources.FirstOrDefault(s => s.Id == config.DefaultHistoricalSourceId);
    }

    private void AddDataSource_Click(object sender, RoutedEventArgs e)
    {
        _editingSourceId = null;
        EditPanelTitle.Text = "Add Data Source";
        ClearEditForm();
        EditPanel.Visibility = Visibility.Visible;
    }

    private void EditDataSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sourceId)
        {
            var source = DataSources.FirstOrDefault(s => s.Id == sourceId);
            if (source != null)
            {
                _editingSourceId = sourceId;
                EditPanelTitle.Text = "Edit Data Source";
                PopulateEditForm(source);
                EditPanel.Visibility = Visibility.Visible;
            }
        }
    }

    private async void DeleteDataSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string sourceId)
        {
            var source = DataSources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
            {
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{source.Name}'?",
                "Delete Data Source",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _configService.DeleteDataSourceAsync(sourceId);
                    await LoadDataSourcesAsync();
                    ShowStatus("Data source deleted successfully.");
                }
                catch (Exception ex)
                {
                    ShowStatus($"Failed to delete data source: {ex.Message}", isError: true);
                }
            }
        }
    }

    private async void SaveDataSource_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateEditForm())
        {
            return;
        }

        SaveProgress.Visibility = Visibility.Visible;
        SaveSourceButton.IsEnabled = false;

        try
        {
            var source = BuildDataSourceFromForm();
            await _configService.AddOrUpdateDataSourceAsync(source);

            EditPanel.Visibility = Visibility.Collapsed;
            await LoadDataSourcesAsync();

            ShowStatus(_editingSourceId == null
                ? "Data source added successfully."
                : "Data source updated successfully.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to save data source: {ex.Message}", isError: true);
        }
        finally
        {
            SaveProgress.Visibility = Visibility.Collapsed;
            SaveSourceButton.IsEnabled = true;
        }
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        EditPanel.Visibility = Visibility.Collapsed;
        _editingSourceId = null;
    }

    private DataSourceConfigDto BuildDataSourceFromForm()
    {
        var source = new DataSourceConfigDto
        {
            Id = _editingSourceId ?? Guid.NewGuid().ToString("N"),
            Name = SourceNameBox.Text.Trim(),
            Provider = GetComboSelectedTag(ProviderCombo) ?? "IB",
            Type = GetComboSelectedTag(TypeCombo) ?? "RealTime",
            Priority = ParseIntOrDefault(PriorityBox.Text, 100),
            Description = DescriptionBox.Text.Trim(),
            Enabled = true
        };

        if (!string.IsNullOrWhiteSpace(SymbolsBox.Text))
        {
            source.Symbols = SymbolsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        switch (source.Provider)
        {
            case "IB":
                source.IB = new IBOptionsDto
                {
                    Host = IBHostBox.Text.Trim(),
                    Port = ParseIntOrDefault(IBPortBox.Text, 7496),
                    ClientId = ParseIntOrDefault(IBClientIdBox.Text, 0),
                    UsePaperTrading = IBPaperTradingCheck.IsChecked ?? false,
                    SubscribeDepth = IBSubscribeDepthCheck.IsChecked ?? true,
                    TickByTick = IBTickByTickCheck.IsChecked ?? true
                };
                break;
            case "Alpaca":
                source.Alpaca = new AlpacaOptionsDto
                {
                    Feed = GetComboSelectedTag(AlpacaFeedCombo) ?? "iex",
                    UseSandbox = GetComboSelectedTag(AlpacaEnvironmentCombo) == "true",
                    SubscribeQuotes = AlpacaSubscribeQuotesCheck.IsChecked ?? false
                };
                break;
            case "Polygon":
                source.Polygon = new PolygonOptionsDto
                {
                    ApiKey = PolygonApiKeyBox.Password,
                    Feed = GetComboSelectedTag(PolygonFeedCombo) ?? "stocks",
                    UseDelayed = PolygonDelayedCheck.IsChecked ?? false,
                    SubscribeTrades = PolygonTradesCheck.IsChecked ?? true,
                    SubscribeQuotes = PolygonQuotesCheck.IsChecked ?? false,
                    SubscribeAggregates = PolygonAggregatesCheck.IsChecked ?? false
                };
                break;
        }

        return source;
    }

    private void PopulateEditForm(DataSourceConfigDto source)
    {
        SourceNameBox.Text = source.Name;
        SelectComboItemByTag(ProviderCombo, source.Provider);
        SelectComboItemByTag(TypeCombo, source.Type);
        PriorityBox.Text = source.Priority.ToString();
        DescriptionBox.Text = source.Description ?? string.Empty;
        SymbolsBox.Text = source.Symbols != null ? string.Join(", ", source.Symbols) : string.Empty;

        UpdateProviderSettingsPanels(source.Provider);

        if (source.IB != null)
        {
            IBHostBox.Text = source.IB.Host;
            IBPortBox.Text = source.IB.Port.ToString();
            IBClientIdBox.Text = source.IB.ClientId.ToString();
            IBPaperTradingCheck.IsChecked = source.IB.UsePaperTrading;
            IBSubscribeDepthCheck.IsChecked = source.IB.SubscribeDepth;
            IBTickByTickCheck.IsChecked = source.IB.TickByTick;
        }

        if (source.Alpaca != null)
        {
            SelectComboItemByTag(AlpacaFeedCombo, source.Alpaca.Feed ?? "iex");
            SelectComboItemByTag(AlpacaEnvironmentCombo, source.Alpaca.UseSandbox ? "true" : "false");
            AlpacaSubscribeQuotesCheck.IsChecked = source.Alpaca.SubscribeQuotes;
        }

        if (source.Polygon != null)
        {
            PolygonApiKeyBox.Password = source.Polygon.ApiKey ?? string.Empty;
            SelectComboItemByTag(PolygonFeedCombo, source.Polygon.Feed);
            PolygonDelayedCheck.IsChecked = source.Polygon.UseDelayed;
            PolygonTradesCheck.IsChecked = source.Polygon.SubscribeTrades;
            PolygonQuotesCheck.IsChecked = source.Polygon.SubscribeQuotes;
            PolygonAggregatesCheck.IsChecked = source.Polygon.SubscribeAggregates;
        }
    }

    private void ClearEditForm()
    {
        SourceNameBox.Text = string.Empty;
        ProviderCombo.SelectedIndex = 0;
        TypeCombo.SelectedIndex = 0;
        PriorityBox.Text = "100";
        DescriptionBox.Text = string.Empty;
        SymbolsBox.Text = string.Empty;

        IBHostBox.Text = "127.0.0.1";
        IBPortBox.Text = "7496";
        IBClientIdBox.Text = "0";
        IBPaperTradingCheck.IsChecked = false;
        IBSubscribeDepthCheck.IsChecked = true;
        IBTickByTickCheck.IsChecked = true;

        AlpacaFeedCombo.SelectedIndex = 0;
        AlpacaEnvironmentCombo.SelectedIndex = 0;
        AlpacaSubscribeQuotesCheck.IsChecked = false;

        PolygonApiKeyBox.Password = string.Empty;
        PolygonFeedCombo.SelectedIndex = 0;
        PolygonDelayedCheck.IsChecked = false;
        PolygonTradesCheck.IsChecked = true;
        PolygonQuotesCheck.IsChecked = false;
        PolygonAggregatesCheck.IsChecked = false;

        ClearValidationErrors();
        UpdateProviderSettingsPanels("IB");
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var provider = GetComboSelectedTag(ProviderCombo) ?? "IB";
        UpdateProviderSettingsPanels(provider);
    }

    private void UpdateProviderSettingsPanels(string provider)
    {
        IBSettingsPanel.Visibility = provider == "IB" ? Visibility.Visible : Visibility.Collapsed;
        AlpacaSettingsPanel.Visibility = provider == "Alpaca" ? Visibility.Visible : Visibility.Collapsed;
        PolygonSettingsPanel.Visibility = provider == "Polygon" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void EnableFailoverToggle_Changed(object sender, RoutedEventArgs e)
    {
        await UpdateFailoverSettingsAsync();
    }

    private async void FailoverTimeoutBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        await UpdateFailoverSettingsAsync();
    }

    private async Task UpdateFailoverSettingsAsync(CancellationToken ct = default)
    {
        if (!TryParseTimeout(out var timeoutSeconds))
        {
            return;
        }

        try
        {
            await _configService.UpdateFailoverSettingsAsync(
                EnableFailoverToggle.IsChecked ?? false,
                timeoutSeconds);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to update failover settings: {ex.Message}", isError: true);
        }
    }

    private async void DefaultRealTimeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DefaultRealTimeCombo.SelectedItem is DataSourceConfigDto source)
        {
            try
            {
                await _configService.SetDefaultDataSourceAsync(source.Id, isHistorical: false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to set default real-time source: {ex.Message}", isError: true);
            }
        }
    }

    private async void DefaultHistoricalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DefaultHistoricalCombo.SelectedItem is DataSourceConfigDto source)
        {
            try
            {
                await _configService.SetDefaultDataSourceAsync(source.Id, isHistorical: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to set default historical source: {ex.Message}", isError: true);
            }
        }
    }

    private async void SourceEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is DataSourceConfigDto source)
        {
            try
            {
                source.Enabled = checkBox.IsChecked ?? false;
                await _configService.AddOrUpdateDataSourceAsync(source);
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to update data source: {ex.Message}", isError: true);
            }
        }
    }

    private bool ValidateEditForm()
    {
        ClearValidationErrors();
        var hasError = false;

        if (string.IsNullOrWhiteSpace(SourceNameBox.Text))
        {
            SourceNameError.Text = "Name is required.";
            SourceNameError.Visibility = Visibility.Visible;
            hasError = true;
        }

        if (!int.TryParse(PriorityBox.Text, out var priority) || priority is < 1 or > 1000)
        {
            PriorityError.Text = "Priority must be between 1 and 1000.";
            PriorityError.Visibility = Visibility.Visible;
            hasError = true;
        }

        if (!TryParseTimeout(out _))
        {
            hasError = true;
        }

        return !hasError;
    }

    private bool TryParseTimeout(out int timeoutSeconds)
    {
        FailoverTimeoutError.Visibility = Visibility.Collapsed;

        if (!int.TryParse(FailoverTimeoutBox.Text, out timeoutSeconds) || timeoutSeconds is < 5 or > 300)
        {
            FailoverTimeoutError.Text = "Timeout must be between 5 and 300 seconds.";
            FailoverTimeoutError.Visibility = Visibility.Visible;
            return false;
        }

        return true;
    }

    private void ClearValidationErrors()
    {
        SourceNameError.Visibility = Visibility.Collapsed;
        PriorityError.Visibility = Visibility.Collapsed;
        FailoverTimeoutError.Visibility = Visibility.Collapsed;
    }

    private void ShowStatus(string message, bool isError = false)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusPanel.Background = isError
            ? (System.Windows.Media.Brush)FindResource("ConsoleAccentRedAlpha10Brush")
            : (System.Windows.Media.Brush)FindResource("ConsoleAccentGreenAlpha10Brush");
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("ErrorColorBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
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

    private static string? GetComboSelectedTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }

    private static int ParseIntOrDefault(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
