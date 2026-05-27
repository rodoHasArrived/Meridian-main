using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using WpfConfigService = Meridian.Wpf.Services.ConfigService;
using UiStorageOptimizationAdvisorService = Meridian.Ui.Services.StorageOptimizationAdvisorService;

namespace Meridian.Wpf.ViewModels;

public sealed class StorageOptimizationViewModel : BindableBase
{
    private const string ToneNeutral = "Neutral";
    private const string ToneInfo = "Info";
    private const string ToneSuccess = "Success";
    private const string ToneWarning = "Warning";
    private const string ToneError = "Error";

    private readonly Func<bool, CancellationToken, Task<StorageAnalytics>> _getAnalyticsAsync;
    private readonly Func<CancellationToken, Task<DriveStorageInfo?>> _getDriveInfoAsync;
    private readonly Func<CancellationToken, Task<string>> _resolveDataRootAsync;
    private readonly Func<string, StorageAnalysisOptions, CancellationToken, Task<StorageOptimizationReport>> _analyzeStorageAsync;
    private readonly Func<CancellationToken, Task<TierStatisticsApiResult?>> _getTierStatisticsAsync;

    private string _overviewStatusText = "Storage analytics pending. Open the page to scan the archive footprint.";
    private string _overviewStatusTone = ToneNeutral;
    private bool _isAnalyticsLoading;
    private string _totalSizeText = "--";
    private string _totalFilesText = "--";
    private string _dailyGrowthText = "--";
    private string _daysUntilFullText = "--";
    private string _tradeSizeText = "--";
    private string _tradeFilesText = "-- files";
    private string _depthSizeText = "--";
    private string _depthFilesText = "-- files";
    private string _historicalSizeText = "--";
    private string _historicalFilesText = "-- files";
    private string _symbolBreakdownStatusText = "Loading...";
    private string _symbolBreakdownTone = ToneNeutral;
    private string _driveNameText = "--";
    private string _freeSpaceText = "--";
    private string _usedPercentText = "--";
    private double _driveUsedPercent;
    private string _driveUsageTone = ToneInfo;
    private bool _isOptimizationActionRunning;
    private string _analysisResultText = "Run a storage analysis or fetch tier statistics to review optimization opportunities.";
    private string _analysisResultTone = ToneNeutral;

    public StorageOptimizationViewModel()
        : this(
            (forceRefresh, ct) => StorageAnalyticsService.Instance.GetAnalyticsAsync(forceRefresh, ct),
            ct => StorageAnalyticsService.Instance.GetDriveInfoAsync(ct),
            async ct =>
            {
                var config = await WpfConfigService.Instance.LoadConfigAsync();
                ct.ThrowIfCancellationRequested();
                return WpfConfigService.Instance.ResolveDataRoot(config);
            },
            (basePath, options, ct) => UiStorageOptimizationAdvisorService.Instance.AnalyzeStorageAsync(basePath, options, ct: ct),
            ct => UiStorageOptimizationAdvisorService.Instance.GetTierStatisticsAsync(ct))
    {
    }

    internal StorageOptimizationViewModel(
        Func<bool, CancellationToken, Task<StorageAnalytics>> getAnalyticsAsync,
        Func<CancellationToken, Task<DriveStorageInfo?>> getDriveInfoAsync,
        Func<CancellationToken, Task<string>> resolveDataRootAsync,
        Func<string, StorageAnalysisOptions, CancellationToken, Task<StorageOptimizationReport>> analyzeStorageAsync,
        Func<CancellationToken, Task<TierStatisticsApiResult?>> getTierStatisticsAsync)
    {
        _getAnalyticsAsync = getAnalyticsAsync ?? throw new ArgumentNullException(nameof(getAnalyticsAsync));
        _getDriveInfoAsync = getDriveInfoAsync ?? throw new ArgumentNullException(nameof(getDriveInfoAsync));
        _resolveDataRootAsync = resolveDataRootAsync ?? throw new ArgumentNullException(nameof(resolveDataRootAsync));
        _analyzeStorageAsync = analyzeStorageAsync ?? throw new ArgumentNullException(nameof(analyzeStorageAsync));
        _getTierStatisticsAsync = getTierStatisticsAsync ?? throw new ArgumentNullException(nameof(getTierStatisticsAsync));

        RefreshAnalyticsCommand = new AsyncRelayCommand(
            ct => LoadAsync(forceRefresh: true, ct),
            () => CanRefreshAnalytics);
        RunAnalysisCommand = new AsyncRelayCommand(
            RunAnalysisAsync,
            () => CanRunOptimizationActions);
        ViewTierStatisticsCommand = new AsyncRelayCommand(
            ViewTierStatisticsAsync,
            () => CanRunOptimizationActions);
    }

    public IAsyncRelayCommand RefreshAnalyticsCommand { get; }

    public IAsyncRelayCommand RunAnalysisCommand { get; }

    public IAsyncRelayCommand ViewTierStatisticsCommand { get; }

    public ObservableCollection<StorageOptimizationSymbolRow> SymbolBreakdownRows { get; } = new();

    public string OverviewStatusText
    {
        get => _overviewStatusText;
        private set => SetProperty(ref _overviewStatusText, value);
    }

    public string OverviewStatusTone
    {
        get => _overviewStatusTone;
        private set => SetProperty(ref _overviewStatusTone, value);
    }

    public bool IsAnalyticsLoading
    {
        get => _isAnalyticsLoading;
        private set
        {
            if (SetProperty(ref _isAnalyticsLoading, value))
            {
                OnPropertyChanged(nameof(CanRefreshAnalytics));
                RefreshAnalyticsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanRefreshAnalytics => !IsAnalyticsLoading;

    public string TotalSizeText
    {
        get => _totalSizeText;
        private set => SetProperty(ref _totalSizeText, value);
    }

    public string TotalFilesText
    {
        get => _totalFilesText;
        private set => SetProperty(ref _totalFilesText, value);
    }

    public string DailyGrowthText
    {
        get => _dailyGrowthText;
        private set => SetProperty(ref _dailyGrowthText, value);
    }

    public string DaysUntilFullText
    {
        get => _daysUntilFullText;
        private set => SetProperty(ref _daysUntilFullText, value);
    }

    public string TradeSizeText
    {
        get => _tradeSizeText;
        private set => SetProperty(ref _tradeSizeText, value);
    }

    public string TradeFilesText
    {
        get => _tradeFilesText;
        private set => SetProperty(ref _tradeFilesText, value);
    }

    public string DepthSizeText
    {
        get => _depthSizeText;
        private set => SetProperty(ref _depthSizeText, value);
    }

    public string DepthFilesText
    {
        get => _depthFilesText;
        private set => SetProperty(ref _depthFilesText, value);
    }

    public string HistoricalSizeText
    {
        get => _historicalSizeText;
        private set => SetProperty(ref _historicalSizeText, value);
    }

    public string HistoricalFilesText
    {
        get => _historicalFilesText;
        private set => SetProperty(ref _historicalFilesText, value);
    }

    public string SymbolBreakdownStatusText
    {
        get => _symbolBreakdownStatusText;
        private set => SetProperty(ref _symbolBreakdownStatusText, value);
    }

    public string SymbolBreakdownTone
    {
        get => _symbolBreakdownTone;
        private set => SetProperty(ref _symbolBreakdownTone, value);
    }

    public bool HasSymbolBreakdown => SymbolBreakdownRows.Count > 0;

    public bool IsSymbolBreakdownEmpty => !HasSymbolBreakdown && !IsAnalyticsLoading;

    public string DriveNameText
    {
        get => _driveNameText;
        private set => SetProperty(ref _driveNameText, value);
    }

    public string FreeSpaceText
    {
        get => _freeSpaceText;
        private set => SetProperty(ref _freeSpaceText, value);
    }

    public string UsedPercentText
    {
        get => _usedPercentText;
        private set => SetProperty(ref _usedPercentText, value);
    }

    public double DriveUsedPercent
    {
        get => _driveUsedPercent;
        private set => SetProperty(ref _driveUsedPercent, value);
    }

    public string DriveUsageTone
    {
        get => _driveUsageTone;
        private set => SetProperty(ref _driveUsageTone, value);
    }

    public bool IsOptimizationActionRunning
    {
        get => _isOptimizationActionRunning;
        private set
        {
            if (SetProperty(ref _isOptimizationActionRunning, value))
            {
                OnPropertyChanged(nameof(CanRunOptimizationActions));
                RunAnalysisCommand.NotifyCanExecuteChanged();
                ViewTierStatisticsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanRunOptimizationActions => !IsOptimizationActionRunning;

    public string AnalysisResultText
    {
        get => _analysisResultText;
        private set
        {
            if (SetProperty(ref _analysisResultText, value))
            {
                OnPropertyChanged(nameof(HasAnalysisResult));
            }
        }
    }

    public bool HasAnalysisResult => !string.IsNullOrWhiteSpace(AnalysisResultText);

    public string AnalysisResultTone
    {
        get => _analysisResultTone;
        private set => SetProperty(ref _analysisResultTone, value);
    }

    public Task LoadAsync(CancellationToken ct = default)
        => LoadAsync(forceRefresh: false, ct);

    internal async Task LoadAsync(bool forceRefresh, CancellationToken ct = default)
    {
        IsAnalyticsLoading = true;
        OverviewStatusText = forceRefresh
            ? "Refreshing storage analytics..."
            : "Loading storage analytics...";
        OverviewStatusTone = ToneNeutral;

        try
        {
            var analytics = await _getAnalyticsAsync(forceRefresh, ct);
            ct.ThrowIfCancellationRequested();
            ApplyAnalytics(analytics);
            OverviewStatusText = BuildOverviewStatusText(analytics);
            OverviewStatusTone = ToneSuccess;
        }
        catch (OperationCanceledException)
        {
            OverviewStatusText = "Storage analytics load cancelled.";
            OverviewStatusTone = ToneWarning;
        }
        catch (Exception ex)
        {
            SymbolBreakdownRows.Clear();
            SymbolBreakdownStatusText = $"Failed to load analytics: {ex.Message}";
            SymbolBreakdownTone = ToneError;
            OverviewStatusText = "Storage analytics failed. Verify DataRoot access and refresh.";
            OverviewStatusTone = ToneError;
            RaiseSymbolBreakdownStateChanged();
        }

        try
        {
            var driveInfo = await _getDriveInfoAsync(ct);
            ct.ThrowIfCancellationRequested();
            ApplyDriveInfo(driveInfo);
        }
        catch (OperationCanceledException)
        {
            DriveNameText = "Drive scan cancelled";
            FreeSpaceText = "--";
            UsedPercentText = "--";
            DriveUsedPercent = 0;
            DriveUsageTone = ToneWarning;
        }
        catch (Exception)
        {
            ApplyDriveInfo(null, "Error loading drive info");
        }
        finally
        {
            IsAnalyticsLoading = false;
            RaiseSymbolBreakdownStateChanged();
        }
    }

    private async Task RunAnalysisAsync(CancellationToken ct)
    {
        IsOptimizationActionRunning = true;
        AnalysisResultText = "Running storage analysis...";
        AnalysisResultTone = ToneNeutral;

        try
        {
            var basePath = await _resolveDataRootAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));

            var report = await _analyzeStorageAsync(basePath, CreateDefaultAnalysisOptions(), timeout.Token);

            if (report.Errors.Count > 0)
            {
                AnalysisResultText = $"Analysis completed with errors:{Environment.NewLine}{string.Join(Environment.NewLine, report.Errors)}";
                AnalysisResultTone = ToneWarning;
                return;
            }

            AnalysisResultText = report.GetSummary();
            AnalysisResultTone = ToneSuccess;
        }
        catch (OperationCanceledException)
        {
            AnalysisResultText = "Analysis cancelled before completion.";
            AnalysisResultTone = ToneWarning;
        }
        catch (Exception ex)
        {
            AnalysisResultText = $"Analysis failed: {ex.Message}";
            AnalysisResultTone = ToneError;
        }
        finally
        {
            IsOptimizationActionRunning = false;
        }
    }

    private async Task ViewTierStatisticsAsync(CancellationToken ct)
    {
        IsOptimizationActionRunning = true;
        AnalysisResultText = "Fetching tier statistics...";
        AnalysisResultTone = ToneNeutral;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            var stats = await _getTierStatisticsAsync(timeout.Token);
            if (stats is null)
            {
                AnalysisResultText = "Tier statistics unavailable. Backend may not be running.";
                AnalysisResultTone = ToneWarning;
                return;
            }

            AnalysisResultText = FormatTierStatistics(stats);
            AnalysisResultTone = ToneInfo;
        }
        catch (OperationCanceledException)
        {
            AnalysisResultText = "Tier statistics request cancelled.";
            AnalysisResultTone = ToneWarning;
        }
        catch (Exception ex)
        {
            AnalysisResultText = $"Failed to fetch tier statistics: {ex.Message}";
            AnalysisResultTone = ToneError;
        }
        finally
        {
            IsOptimizationActionRunning = false;
        }
    }

    internal static StorageAnalysisOptions CreateDefaultAnalysisOptions()
        => new()
        {
            CalculateHashes = false,
            FindDuplicates = true,
            AnalyzeCompression = true,
            FindSmallFiles = true,
            AnalyzeTiering = true,
            ColdTierAgeDays = 90
        };

    internal static string FormatTierStatistics(TierStatisticsApiResult stats)
    {
        var text = new StringBuilder();
        text.AppendLine($"Tier Statistics (as of {stats.GeneratedAt:yyyy-MM-dd HH:mm} UTC)");
        text.AppendLine(new string('-', 50));
        text.AppendLine($"Total: {FormatHelpers.FormatBytes(stats.TotalBytes)} across {stats.TotalFiles} files");
        text.AppendLine();
        AppendTier(text, "Hot Tier: ", stats.Hot);
        AppendTier(text, "Warm Tier:", stats.Warm);
        AppendTier(text, "Cold Tier:", stats.Cold);
        return text.ToString().TrimEnd();
    }

    internal static string BuildOverviewStatusText(StorageAnalytics analytics)
    {
        if (analytics.TotalFileCount <= 0)
        {
            return "Analytics loaded: no archive files found under the configured DataRoot.";
        }

        var symbolCount = analytics.SymbolBreakdown.Length;
        var symbolLabel = symbolCount == 1 ? "symbol" : "symbols";
        return $"Analytics loaded: {analytics.TotalFileCount:N0} files across {symbolCount:N0} {symbolLabel}.";
    }

    private void ApplyAnalytics(StorageAnalytics analytics)
    {
        TotalSizeText = FormatHelpers.FormatBytes(analytics.TotalSizeBytes);
        TotalFilesText = analytics.TotalFileCount.ToString("N0");
        DailyGrowthText = $"{FormatHelpers.FormatBytes(analytics.DailyGrowthBytes)}/day";
        DaysUntilFullText = analytics.ProjectedDaysUntilFull?.ToString("N0") ?? "N/A";

        TradeSizeText = FormatHelpers.FormatBytes(analytics.TradeSizeBytes);
        TradeFilesText = $"{analytics.TradeFileCount:N0} files";
        DepthSizeText = FormatHelpers.FormatBytes(analytics.DepthSizeBytes);
        DepthFilesText = $"{analytics.DepthFileCount:N0} files";
        HistoricalSizeText = FormatHelpers.FormatBytes(analytics.HistoricalSizeBytes);
        HistoricalFilesText = $"{analytics.HistoricalFileCount:N0} files";

        SymbolBreakdownRows.Clear();
        foreach (var row in analytics.SymbolBreakdown
                     .OrderByDescending(symbol => symbol.SizeBytes)
                     .Take(10)
                     .Select(StorageOptimizationSymbolRow.FromAnalytics))
        {
            SymbolBreakdownRows.Add(row);
        }

        if (SymbolBreakdownRows.Count > 0)
        {
            SymbolBreakdownStatusText = $"Showing top {SymbolBreakdownRows.Count:N0} of {analytics.SymbolBreakdown.Length:N0} symbols";
            SymbolBreakdownTone = ToneInfo;
        }
        else
        {
            SymbolBreakdownStatusText = "No symbol data found.";
            SymbolBreakdownTone = ToneWarning;
        }

        RaiseSymbolBreakdownStateChanged();
    }

    private void ApplyDriveInfo(DriveStorageInfo? driveInfo, string unavailableText = "Unknown")
    {
        if (driveInfo is null)
        {
            DriveNameText = unavailableText;
            FreeSpaceText = "Unknown";
            UsedPercentText = "Unknown";
            DriveUsedPercent = 0;
            DriveUsageTone = ToneWarning;
            return;
        }

        DriveNameText = $"{driveInfo.DriveName} ({driveInfo.DriveType})";
        FreeSpaceText = FormatHelpers.FormatBytes(driveInfo.FreeBytes);
        UsedPercentText = $"{driveInfo.UsedPercent:F1}%";
        DriveUsedPercent = driveInfo.UsedPercent;
        DriveUsageTone = driveInfo.UsedPercent switch
        {
            >= 90 => ToneError,
            >= 75 => ToneWarning,
            _ => ToneInfo
        };
    }

    private void RaiseSymbolBreakdownStateChanged()
    {
        OnPropertyChanged(nameof(HasSymbolBreakdown));
        OnPropertyChanged(nameof(IsSymbolBreakdownEmpty));
    }

    private static void AppendTier(StringBuilder text, string label, TierStats? tier)
    {
        if (tier is null)
        {
            return;
        }

        text.AppendLine($"{label} {tier.FileCount} files, {FormatHelpers.FormatBytes(tier.TotalBytes)} ({tier.PercentageOfTotal:F1}%)");
    }
}

public sealed record StorageOptimizationSymbolRow(
    string Symbol,
    string SizeText,
    string FileCountText,
    double PercentOfTotal)
{
    public static StorageOptimizationSymbolRow FromAnalytics(SymbolAnalyticsInfo info)
        => new(
            info.Symbol,
            FormatHelpers.FormatBytes(info.SizeBytes),
            $"{info.FileCount:N0} files",
            info.PercentOfTotal);
}
