using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class StoragePage : Page
{
    private readonly StorageAnalyticsService _analyticsService;
    private readonly SettingsConfigurationService _settingsConfigService;

    public StoragePage()
    {
        InitializeComponent();
        _analyticsService = StorageAnalyticsService.Instance;
        _settingsConfigService = SettingsConfigurationService.Instance;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadStorageMetricsAsync();
        RefreshFileTreePreview();
    }

    private async System.Threading.Tasks.Task LoadStorageMetricsAsync()
    {
        try
        {
            var analytics = await _analyticsService.GetAnalyticsAsync();

            TotalSizeText.Text = FormatHelpers.FormatBytes(analytics.TotalSizeBytes);
            TotalFilesText.Text = analytics.TotalFileCount.ToString("N0");
            SymbolCountText.Text = analytics.SymbolBreakdown.Length.ToString("N0");

            // Tier sizes: use trade data as hot, historical as cold, remainder as warm
            HotTierSizeText.Text = FormatHelpers.FormatBytes(analytics.TradeSizeBytes);
            WarmTierSizeText.Text = FormatHelpers.FormatBytes(analytics.DepthSizeBytes);
            ColdTierSizeText.Text = FormatHelpers.FormatBytes(analytics.HistoricalSizeBytes);
        }
        catch (Exception)
        {
            // Leave placeholder "--" values in place on error
        }
    }

    private void StorageConfig_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Guard against calls during initialization
        if (FileTreePreviewText == null) return;
        RefreshFileTreePreview();
    }

    private void RefreshFileTreePreview()
    {
        var naming = GetSelectedTag(NamingConventionCombo) ?? "BySymbol";
        var compression = GetSelectedTag(CompressionCombo) ?? "gzip";
        var rootPath = DataDirectoryBox.Text?.TrimStart('.', '/') ?? "data";
        if (string.IsNullOrWhiteSpace(rootPath)) rootPath = "data";

        // Use sample symbols for preview
        var symbols = new List<string> { "SPY", "AAPL", "MSFT" };

        var preview = _settingsConfigService.GenerateStoragePreview(rootPath, naming, "daily", compression, symbols);
        FileTreePreviewText.Text = preview;

        var estimate = _settingsConfigService.EstimateDailyStorageSize(symbols.Count, trades: true, quotes: true, depth: false);
        StorageEstimateText.Text = $"Estimated daily size: ~{estimate} for {symbols.Count} symbols (trades + quotes, compressed)";
    }

    private static string? GetSelectedTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }
}
