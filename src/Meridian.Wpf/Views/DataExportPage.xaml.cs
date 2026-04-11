using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for exporting collected market data and configuring integrations.
/// Code-behind is limited to:
///  – constructor DI and DataContext wiring
///  – ComboBox-tag helpers (static XAML items with Tag attributes)
///  – PasswordBox read (WPF security restriction prevents binding Password)
/// All business logic lives in <see cref="DataExportViewModel"/>.
/// </summary>
public partial class DataExportPage : Page
{
    private readonly DataExportViewModel _viewModel;

    public DataExportPage()
    {
        InitializeComponent();
        _viewModel = new DataExportViewModel();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Initialise static ComboBoxes that use Tag-based items
        ExportFormatCombo.SelectedIndex = 0;
        CompressionCombo.SelectedIndex = 1;
        DatabaseTypeCombo.SelectedIndex = 0;
        ScheduleFrequencyCombo.SelectedIndex = 1;
        WebhookFormatCombo.SelectedIndex = 0;
        WebhookBatchCombo.SelectedIndex = 1;
        LeanResolutionCombo.SelectedIndex = 2;
    }

    // ── ComboBox tag helpers ─────────────────────────────────────────────

    private void ExportFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedExportFormat = GetComboTag(ExportFormatCombo) ?? "CSV";
    }

    private void CompressionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedCompression = GetComboTag(CompressionCombo) ?? "gzip";
    }

    private void DatabaseType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var type = GetComboTag(DatabaseTypeCombo) ?? "postgresql";
        _viewModel.SelectedDatabaseType = type;
        _viewModel.DatabasePort = type switch
        {
            "postgresql" or "timescaledb" => "5432",
            "clickhouse" => "8123",
            "questdb" => "8812",
            "influxdb" => "8086",
            "sqlite" => "0",
            _ => "5432"
        };
    }

    private void ScheduleFrequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedScheduleFrequency = GetComboTag(ScheduleFrequencyCombo) ?? "daily";
    }

    private void WebhookFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedWebhookFormat = GetComboTag(WebhookFormatCombo) ?? "json";
    }

    private void WebhookBatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedWebhookBatch = GetComboTag(WebhookBatchCombo) ?? "trade";
    }

    private void LeanResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedLeanResolution = GetComboTag(LeanResolutionCombo) ?? "minute";
    }

    // ── Scheduled exports toggle ──────────────────────────────────────────

    private void EnableScheduledExportsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
            _viewModel.IsScheduleEnabled = cb.IsChecked == true;
    }

    // ── Database PasswordBox (non-bindable) ───────────────────────────────

    private void SetDatabaseCredentials_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordBox.Password;
        _viewModel.SetDatabaseCredentialsCommand.Execute(null);
    }

    private void TestDatabaseConnection_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordBox.Password;
        _viewModel.TestDatabaseConnectionCommand.Execute(null);
    }

    private void ConfigureDatabaseSync_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DatabasePassword = DatabasePasswordBox.Password;
        _viewModel.ConfigureDatabaseSyncCommand.Execute(null);
    }

    // ── Utility ──────────────────────────────────────────────────────────

    private static string? GetComboTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
}
