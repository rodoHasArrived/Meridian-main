using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Symbol storage page showing per-symbol storage analytics, data type breakdown,
/// file counts, and growth projections.
/// </summary>
public partial class SymbolStoragePage : Page
{
    private readonly StorageAnalyticsService _analyticsService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly ObservableCollection<SymbolStorageItem> _symbols = new();

    public SymbolStoragePage()
    {
        InitializeComponent();

        _analyticsService = StorageAnalyticsService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;

        SymbolList.ItemsSource = _symbols;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadAnalyticsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        await LoadAnalyticsAsync(forceRefresh: true);
        RefreshButton.IsEnabled = true;
    }

    private async System.Threading.Tasks.Task LoadAnalyticsAsync(bool forceRefresh = false)
    {
        LoadingPanel.Visibility = Visibility.Visible;

        try
        {
            var analytics = await _analyticsService.GetAnalyticsAsync(forceRefresh);

            // Summary stats
            TotalSizeText.Text = FormatHelpers.FormatBytes(analytics.TotalSizeBytes);
            TotalFilesText.Text = analytics.TotalFileCount.ToString("N0");
            SymbolCountText.Text = analytics.SymbolBreakdown.Length.ToString("N0");
            DailyGrowthText.Text = analytics.DailyGrowthBytes > 0
                ? FormatHelpers.FormatBytes(analytics.DailyGrowthBytes) + "/d"
                : "--";

            // Data type breakdown
            TradeSizeText.Text = FormatHelpers.FormatBytes(analytics.TradeSizeBytes);
            TradeFilesText.Text = $"{analytics.TradeFileCount:N0} files";
            DepthSizeText.Text = FormatHelpers.FormatBytes(analytics.DepthSizeBytes);
            DepthFilesText.Text = $"{analytics.DepthFileCount:N0} files";
            HistoricalSizeText.Text = FormatHelpers.FormatBytes(analytics.HistoricalSizeBytes);
            HistoricalFilesText.Text = $"{analytics.HistoricalFileCount:N0} files";

            // Per-symbol breakdown
            _symbols.Clear();
            foreach (var symbolInfo in analytics.SymbolBreakdown)
            {
                _symbols.Add(new SymbolStorageItem
                {
                    Symbol = symbolInfo.Symbol,
                    SizeText = FormatHelpers.FormatBytes(symbolInfo.SizeBytes),
                    FileCountText = $"{symbolInfo.FileCount} files",
                    PercentOfTotal = symbolInfo.PercentOfTotal,
                    DateRangeText = $"{symbolInfo.OldestData:MMM dd} - {symbolInfo.NewestData:MMM dd}"
                });
            }

            NoSymbolsPanel.Visibility = _symbols.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SymbolList.Visibility = _symbols.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Projection
            if (analytics.ProjectedDaysUntilFull.HasValue && analytics.DailyGrowthBytes > 0)
            {
                ProjectionPanel.Visibility = Visibility.Visible;
                var days = analytics.ProjectedDaysUntilFull.Value;
                ProjectionText.Text = days > 365
                    ? $"At current growth rate ({FormatHelpers.FormatBytes(analytics.DailyGrowthBytes)}/day), disk capacity is projected to last more than a year."
                    : $"At current growth rate ({FormatHelpers.FormatBytes(analytics.DailyGrowthBytes)}/day), disk capacity is projected to be reached in approximately {days} days.";
            }
            else
            {
                ProjectionPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load storage analytics", ex);
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }


    public sealed class SymbolStorageItem
    {
        public string Symbol { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public string FileCountText { get; set; } = string.Empty;
        public double PercentOfTotal { get; set; }
        public string DateRangeText { get; set; } = string.Empty;
    }
}
