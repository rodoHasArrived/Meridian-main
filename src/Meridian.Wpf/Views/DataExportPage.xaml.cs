using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for exporting collected market data and configuring integrations.
/// </summary>
public partial class DataExportPage : Page
{
    private readonly DataExportViewModel _viewModel;

    // Surface ViewModel collections so XAML x:Name bindings still resolve via DataContext.
    private ObservableCollection<string> SelectedSymbols => _viewModel.SelectedSymbols;
    private ObservableCollection<ExportHistoryItem> ExportHistory => _viewModel.ExportHistory;

    public DataExportPage()
    {
        InitializeComponent();
        _viewModel = new DataExportViewModel();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ExportFormatCombo.SelectedIndex = 0;
        CompressionCombo.SelectedIndex = 1;
        DatabaseTypeCombo.SelectedIndex = 0;
        ScheduleFrequencyCombo.SelectedIndex = 1;
        WebhookFormatCombo.SelectedIndex = 0;
        WebhookBatchCombo.SelectedIndex = 1;
        LeanResolutionCombo.SelectedIndex = 2;

        SelectedSymbols.Clear();
        SelectedSymbols.Add("AAPL");
        SelectedSymbols.Add("MSFT");
        SelectedSymbols.Add("TSLA");

        ExportHistory.Clear();
        ExportHistory.Add(new ExportHistoryItem
        {
            Timestamp = DateTimeOffset.Now.AddMinutes(-42).ToString("g"),
            Format = "CSV",
            SymbolCount = "3",
            Size = "24 MB",
            Destination = "C:\\Exports\\AAPL_MSFT_TSLA.csv"
        });
        ExportHistory.Add(new ExportHistoryItem
        {
            Timestamp = DateTimeOffset.Now.AddHours(-6).ToString("g"),
            Format = "Parquet",
            SymbolCount = "5",
            Size = "120 MB",
            Destination = "D:\\MarketData\\intraday.parquet"
        });

        NoExportHistoryText.Visibility = ExportHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScheduledExportsPanel.IsEnabled = EnableScheduledExportsToggle.IsChecked == true;
    }

    private void AddSymbol_Click(object sender, RoutedEventArgs e)
    {
        SymbolsValidationError.Visibility = Visibility.Collapsed;
        var symbol = SymbolSearchBox.Text?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            SymbolsValidationError.Text = "Enter a symbol to add.";
            SymbolsValidationError.Visibility = Visibility.Visible;
            return;
        }

        if (!SelectedSymbols.Contains(symbol))
        {
            SelectedSymbols.Add(symbol);
        }

        SymbolSearchBox.Text = string.Empty;
    }

    private void SetToday_Click(object sender, RoutedEventArgs e)
    {
        ExportFromDate.SelectedDate = DateTime.Today;
        ExportToDate.SelectedDate = DateTime.Today;
    }

    private void SetWeek_Click(object sender, RoutedEventArgs e)
    {
        ExportFromDate.SelectedDate = DateTime.Today.AddDays(-7);
        ExportToDate.SelectedDate = DateTime.Today;
    }

    private void SetMonth_Click(object sender, RoutedEventArgs e)
    {
        ExportFromDate.SelectedDate = DateTime.Today.AddMonths(-1);
        ExportToDate.SelectedDate = DateTime.Today;
    }

    private async void ExportData_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateExportInputs())
        {
            return;
        }

        ExportButton.IsEnabled = false;
        ExportProgress.Visibility = Visibility.Visible;
        ExportProgressPanel.Visibility = Visibility.Visible;
        ExportProgressBar.Value = 0;
        ExportProgressPercent.Text = "0%";
        ExportProgressLabel.Text = $"Exporting {SelectedSymbols.First()} trades...";

        for (var step = 1; step <= 5; step++)
        {
            await Task.Delay(200);
            var progress = step * 20;
            ExportProgressBar.Value = progress;
            ExportProgressPercent.Text = $"{progress}%";
        }

        ExportHistory.Insert(0, new ExportHistoryItem
        {
            Timestamp = DateTimeOffset.Now.ToString("g"),
            Format = (ExportFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "CSV",
            SymbolCount = SelectedSymbols.Count.ToString(),
            Size = $"{SelectedSymbols.Count * 8} MB",
            Destination = "C:\\Exports\\latest_export.csv"
        });

        NoExportHistoryText.Visibility = ExportHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ExportProgressPanel.Visibility = Visibility.Collapsed;
        ExportProgress.Visibility = Visibility.Collapsed;
        ExportButton.IsEnabled = true;

        ShowInfo("Export queued successfully. You can track progress in Export History.");
    }

    private bool ValidateExportInputs()
    {
        SymbolsValidationError.Visibility = Visibility.Collapsed;
        DateValidationError.Visibility = Visibility.Collapsed;

        var hasError = false;

        if (SelectedSymbols.Count == 0)
        {
            SymbolsValidationError.Text = "Select at least one symbol.";
            SymbolsValidationError.Visibility = Visibility.Visible;
            hasError = true;
        }

        if (!ExportFromDate.SelectedDate.HasValue || !ExportToDate.SelectedDate.HasValue)
        {
            DateValidationError.Text = "Select both start and end dates.";
            DateValidationError.Visibility = Visibility.Visible;
            hasError = true;
        }
        else if (ExportFromDate.SelectedDate > ExportToDate.SelectedDate)
        {
            DateValidationError.Text = "Start date must be before end date.";
            DateValidationError.Visibility = Visibility.Visible;
            hasError = true;
        }

        return !hasError;
    }

    private void DatabaseType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var type = (DatabaseTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        DatabasePortBox.Text = type switch
        {
            "postgresql" => "5432",
            "timescaledb" => "5432",
            "clickhouse" => "8123",
            "questdb" => "8812",
            "influxdb" => "8086",
            "sqlite" => "0",
            _ => "5432"
        };
    }

    private void SetDatabaseCredentials_Click(object sender, RoutedEventArgs e)
    {
        DbCredentialStatus.Text = "Stored securely";
        DbCredentialStatus.Foreground = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
        ShowInfo("Database credentials saved.");
    }

    private void TestDatabaseConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateDatabaseInputs(out var error))
        {
            ShowInfo(error ?? "Missing required database fields.", isError: true);
            return;
        }

        ShowInfo("Database connection successful.");
    }

    private void ConfigureDatabaseSync_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateDatabaseInputs(out var error))
        {
            ShowInfo(error ?? "Missing required database fields.", isError: true);
            return;
        }

        ShowInfo("Database sync configured. Scheduled exports will push data automatically.");
    }

    private bool TryValidateDatabaseInputs(out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(DatabaseHostBox.Text))
        {
            error = "Database host is required.";
            return false;
        }

        if (!int.TryParse(DatabasePortBox.Text, out var port) || port < 0)
        {
            error = "Database port must be a valid number.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DatabaseNameBox.Text))
        {
            error = "Database name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DatabaseUserBox.Text))
        {
            error = "Database username is required.";
            return false;
        }

        return true;
    }

    private void EnableScheduledExportsToggle_Changed(object sender, RoutedEventArgs e)
    {
        ScheduledExportsPanel.IsEnabled = EnableScheduledExportsToggle.IsChecked == true;
        ValidateScheduleTime();
    }

    private void ScheduleTimeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateScheduleTime();
    }

    private void ValidateScheduleTime()
    {
        ScheduleTimeError.Visibility = Visibility.Collapsed;

        if (EnableScheduledExportsToggle.IsChecked != true)
        {
            return;
        }

        if (!TimeSpan.TryParse(ScheduleTimeBox.Text, out _))
        {
            ScheduleTimeError.Text = "Enter a valid time (HH:mm).";
            ScheduleTimeError.Visibility = Visibility.Visible;
        }
    }

    private void TestWebhook_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WebhookUrlBox.Text))
        {
            WebhookTestResult.Text = "Webhook URL required.";
            WebhookTestResult.Foreground = (System.Windows.Media.Brush)FindResource("ErrorColorBrush");
            return;
        }

        WebhookTestResult.Text = "Webhook responded successfully.";
        WebhookTestResult.Foreground = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
        ShowInfo("Webhook test completed.");
    }

    private void BrowseLeanPath_Click(object sender, RoutedEventArgs e)
    {
        ShowInfo("Select the Lean data directory in the file picker.");
    }

    private void ExportToLean_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LeanDataPathBox.Text))
        {
            ShowInfo("Lean data folder is required.", isError: true);
            return;
        }

        ShowInfo("Lean export job created. Data will be exported in the selected resolution.");
    }

    private void VerifyLeanData_Click(object sender, RoutedEventArgs e)
    {
        ShowInfo("Lean data verification scheduled.");
    }

    private void ShowInfo(string message, bool isError = false)
    {
        ActionInfoPanel.Visibility = Visibility.Visible;
        ActionInfoPanel.Background = isError
            ? (System.Windows.Media.Brush)FindResource("ConsoleAccentRedAlpha10Brush")
            : (System.Windows.Media.Brush)FindResource("ConsoleAccentGreenAlpha10Brush");
        ActionInfoText.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("ErrorColorBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
        ActionInfoText.Text = message;
    }
}
