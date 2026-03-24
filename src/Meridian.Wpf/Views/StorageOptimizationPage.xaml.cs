using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.Services;
using Meridian.Ui.Services;
using WpfNotificationService = Meridian.Wpf.Services.NotificationService;

namespace Meridian.Wpf.Views;

public partial class StorageOptimizationPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly WpfNotificationService _notificationService;
    private readonly StorageAnalyticsService _analyticsService;
    private readonly StorageOptimizationAdvisorService _optimizationService;

    public StorageOptimizationPage(
        NavigationService navigationService,
        WpfNotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _analyticsService = StorageAnalyticsService.Instance;
        _optimizationService = StorageOptimizationAdvisorService.Instance;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadAnalyticsAsync();
        await LoadDriveInfoAsync();
    }

    private async void RefreshAnalytics_Click(object sender, RoutedEventArgs e)
    {
        await LoadAnalyticsAsync(forceRefresh: true);
        await LoadDriveInfoAsync();
    }

    private async void RunAnalysis_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AnalysisResultText.Text = "Running storage analysis...";
            AnalysisResultText.Foreground = (Brush)FindResource("ConsoleTextMutedBrush");

            var dataRoot = "data";

            var basePath = System.IO.Path.IsPathRooted(dataRoot)
                ? dataRoot
                : System.IO.Path.Combine(AppContext.BaseDirectory, dataRoot);

            var options = new StorageAnalysisOptions
            {
                CalculateHashes = false,
                FindDuplicates = true,
                AnalyzeCompression = true,
                FindSmallFiles = true,
                AnalyzeTiering = true,
                ColdTierAgeDays = 90
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var report = await _optimizationService.AnalyzeStorageAsync(basePath, options, ct: cts.Token);

            if (report.Errors.Any())
            {
                AnalysisResultText.Text = $"Analysis completed with errors:\n{string.Join("\n", report.Errors)}";
                AnalysisResultText.Foreground = (Brush)FindResource("WarningColorBrush");
                return;
            }

            AnalysisResultText.Text = report.GetSummary();
            AnalysisResultText.Foreground = (Brush)FindResource("SuccessColorBrush");
        }
        catch (Exception ex)
        {
            AnalysisResultText.Text = $"Analysis failed: {ex.Message}";
            AnalysisResultText.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

    private async void ViewTierStats_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AnalysisResultText.Text = "Fetching tier statistics...";
            AnalysisResultText.Foreground = (Brush)FindResource("ConsoleTextMutedBrush");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stats = await _optimizationService.GetTierStatisticsAsync(cts.Token);

            if (stats == null)
            {
                AnalysisResultText.Text = "Tier statistics unavailable. Backend may not be running.";
                AnalysisResultText.Foreground = (Brush)FindResource("WarningColorBrush");
                return;
            }

            var text = $"Tier Statistics (as of {stats.GeneratedAt:yyyy-MM-dd HH:mm} UTC)\n";
            text += new string('-', 50) + "\n";
            text += $"Total: {FormatHelpers.FormatBytes(stats.TotalBytes)} across {stats.TotalFiles} files\n\n";

            if (stats.Hot != null)
                text += $"Hot Tier:  {stats.Hot.FileCount} files, {FormatHelpers.FormatBytes(stats.Hot.TotalBytes)} ({stats.Hot.PercentageOfTotal:F1}%)\n";
            if (stats.Warm != null)
                text += $"Warm Tier: {stats.Warm.FileCount} files, {FormatHelpers.FormatBytes(stats.Warm.TotalBytes)} ({stats.Warm.PercentageOfTotal:F1}%)\n";
            if (stats.Cold != null)
                text += $"Cold Tier: {stats.Cold.FileCount} files, {FormatHelpers.FormatBytes(stats.Cold.TotalBytes)} ({stats.Cold.PercentageOfTotal:F1}%)\n";

            AnalysisResultText.Text = text;
            AnalysisResultText.Foreground = (Brush)FindResource("ConsoleTextPrimaryBrush");
        }
        catch (Exception ex)
        {
            AnalysisResultText.Text = $"Failed to fetch tier statistics: {ex.Message}";
            AnalysisResultText.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

    private async System.Threading.Tasks.Task LoadAnalyticsAsync(bool forceRefresh = false)
    {
        try
        {
            var analytics = await _analyticsService.GetAnalyticsAsync(forceRefresh);

            TotalSizeText.Text = FormatHelpers.FormatBytes(analytics.TotalSizeBytes);
            TotalFilesText.Text = analytics.TotalFileCount.ToString("N0");
            DailyGrowthText.Text = FormatHelpers.FormatBytes(analytics.DailyGrowthBytes) + "/day";
            DaysUntilFullText.Text = analytics.ProjectedDaysUntilFull?.ToString("N0") ?? "N/A";

            TradeSizeText.Text = FormatHelpers.FormatBytes(analytics.TradeSizeBytes);
            TradeFilesText.Text = $"{analytics.TradeFileCount} files";
            DepthSizeText.Text = FormatHelpers.FormatBytes(analytics.DepthSizeBytes);
            DepthFilesText.Text = $"{analytics.DepthFileCount} files";
            HistoricalSizeText.Text = FormatHelpers.FormatBytes(analytics.HistoricalSizeBytes);
            HistoricalFilesText.Text = $"{analytics.HistoricalFileCount} files";

            // Symbol breakdown (top 10)
            var topSymbols = analytics.SymbolBreakdown.Take(10).ToArray();
            if (topSymbols.Length > 0)
            {
                SymbolBreakdownStatus.Text = $"Showing top {topSymbols.Length} of {analytics.SymbolBreakdown.Length} symbols";
                SymbolBreakdownList.ItemsSource = topSymbols;
            }
            else
            {
                SymbolBreakdownStatus.Text = "No symbol data found.";
                SymbolBreakdownList.ItemsSource = null;
            }
        }
        catch (Exception ex)
        {
            SymbolBreakdownStatus.Text = $"Failed to load analytics: {ex.Message}";
            SymbolBreakdownStatus.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
    }

    private async System.Threading.Tasks.Task LoadDriveInfoAsync()
    {
        try
        {
            var driveInfo = await _analyticsService.GetDriveInfoAsync();
            if (driveInfo == null)
            {
                DriveNameText.Text = "Unknown";
                FreeSpaceText.Text = "Unknown";
                UsedPercentText.Text = "Unknown";
                return;
            }

            DriveNameText.Text = $"{driveInfo.DriveName} ({driveInfo.DriveType})";
            FreeSpaceText.Text = FormatHelpers.FormatBytes(driveInfo.FreeBytes);
            UsedPercentText.Text = $"{driveInfo.UsedPercent:F1}%";
            DriveUsageBar.Value = driveInfo.UsedPercent;

            if (driveInfo.UsedPercent >= 90)
                DriveUsageBar.Foreground = (Brush)FindResource("ErrorColorBrush");
            else if (driveInfo.UsedPercent >= 75)
                DriveUsageBar.Foreground = (Brush)FindResource("WarningColorBrush");
            else
                DriveUsageBar.Foreground = (Brush)FindResource("InfoColorBrush");
        }
        catch
        {
            DriveNameText.Text = "Error loading drive info";
        }
    }

}
