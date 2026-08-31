using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.ViewModels;

internal interface IStorageFolderPicker
{
    string? SelectFolder(string currentPath);
}

internal sealed class StorageFolderPicker : IStorageFolderPicker
{
    public string? SelectFolder(string currentPath)
    {
        var selectedPath = string.IsNullOrWhiteSpace(currentPath)
            ? Environment.CurrentDirectory
            : currentPath;

        if (!System.IO.Path.IsPathRooted(selectedPath))
        {
            selectedPath = System.IO.Path.GetFullPath(selectedPath);
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select storage data directory",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = selectedPath
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }
}

/// <summary>
/// ViewModel for the Storage configuration and analytics page.
/// Owns async metric loading and the preview-generation logic so that the
/// code-behind is thinned to constructor DI and lifecycle wiring only.
/// </summary>
public sealed class StorageViewModel : BindableBase
{
    private readonly SettingsConfigurationService _settingsConfigService;
    private readonly IStorageFolderPicker _folderPicker;
    private readonly Func<bool, CancellationToken, Task<StorageAnalytics>> _getAnalyticsAsync;

    // ── Tier size properties ──────────────────────────────────────────────────────────
    private string _totalSizeText = "--";
    public string TotalSizeText { get => _totalSizeText; private set => SetProperty(ref _totalSizeText, value); }

    private string _totalFilesText = "--";
    public string TotalFilesText { get => _totalFilesText; private set => SetProperty(ref _totalFilesText, value); }

    private string _symbolCountText = "--";
    public string SymbolCountText { get => _symbolCountText; private set => SetProperty(ref _symbolCountText, value); }

    private bool _isMetricsLoading;
    public bool IsMetricsLoading
    {
        get => _isMetricsLoading;
        private set
        {
            if (SetProperty(ref _isMetricsLoading, value))
            {
                OnPropertyChanged(nameof(CanRefreshMetrics));
                RefreshMetricsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanRefreshMetrics => !IsMetricsLoading;

    private string _metricsStatusText = "Metrics pending. Open Storage to scan archive metrics.";
    public string MetricsStatusText { get => _metricsStatusText; private set => SetProperty(ref _metricsStatusText, value); }

    private string _hotTierSizeText = "--";
    public string HotTierSizeText { get => _hotTierSizeText; private set => SetProperty(ref _hotTierSizeText, value); }

    private string _warmTierSizeText = "--";
    public string WarmTierSizeText { get => _warmTierSizeText; private set => SetProperty(ref _warmTierSizeText, value); }

    private string _coldTierSizeText = "--";
    public string ColdTierSizeText { get => _coldTierSizeText; private set => SetProperty(ref _coldTierSizeText, value); }

    // -- Archive posture properties ---------------------------------------------------
    private string _storagePostureTitle = "Archive posture pending";
    public string StoragePostureTitle { get => _storagePostureTitle; private set => SetProperty(ref _storagePostureTitle, value); }

    private string _storagePostureDetail = "Open Storage to scan archive metrics and estimate capacity posture.";
    public string StoragePostureDetail { get => _storagePostureDetail; private set => SetProperty(ref _storagePostureDetail, value); }

    private string _storageGrowthText = "Daily growth: --";
    public string StorageGrowthText { get => _storageGrowthText; private set => SetProperty(ref _storageGrowthText, value); }

    private string _storageCapacityHorizonText = "Capacity horizon: --";
    public string StorageCapacityHorizonText { get => _storageCapacityHorizonText; private set => SetProperty(ref _storageCapacityHorizonText, value); }

    private string _storageLastScanText = "Last scan: --";
    public string StorageLastScanText { get => _storageLastScanText; private set => SetProperty(ref _storageLastScanText, value); }

    // ── Preview properties ────────────────────────────────────────────────────────────
    private string _dataDirectoryText = "./data";
    public string DataDirectoryText
    {
        get => _dataDirectoryText;
        set
        {
            var nextValue = string.IsNullOrWhiteSpace(value) ? "./data" : value;
            if (SetProperty(ref _dataDirectoryText, nextValue))
            {
                RefreshPreview();
            }
        }
    }

    private string _namingConvention = "BySymbol";
    public string NamingConvention
    {
        get => _namingConvention;
        set
        {
            var nextValue = string.IsNullOrWhiteSpace(value) ? "BySymbol" : value;
            if (SetProperty(ref _namingConvention, nextValue))
            {
                RefreshPreview();
            }
        }
    }

    private string _compressionFormat = "gzip";
    public string CompressionFormat
    {
        get => _compressionFormat;
        set
        {
            var nextValue = string.IsNullOrWhiteSpace(value) ? "gzip" : value;
            if (SetProperty(ref _compressionFormat, nextValue))
            {
                RefreshPreview();
            }
        }
    }

    private string _fileTreePreviewText = string.Empty;
    public string FileTreePreviewText { get => _fileTreePreviewText; private set => SetProperty(ref _fileTreePreviewText, value); }

    private string _storageEstimateText = string.Empty;
    public string StorageEstimateText { get => _storageEstimateText; private set => SetProperty(ref _storageEstimateText, value); }

    private string _previewScopeText = "Preview pending";
    public string PreviewScopeText { get => _previewScopeText; private set => SetProperty(ref _previewScopeText, value); }

    private string _previewActionText = "Open the page to generate a sample SPY, AAPL, and MSFT layout.";
    public string PreviewActionText { get => _previewActionText; private set => SetProperty(ref _previewActionText, value); }

    public IRelayCommand BrowseStorageRootCommand { get; }
    public IAsyncRelayCommand RefreshMetricsCommand { get; }

    public StorageViewModel(
        StorageAnalyticsService analyticsService,
        SettingsConfigurationService settingsConfigService)
        : this(analyticsService, settingsConfigService, new StorageFolderPicker())
    {
    }

    internal StorageViewModel(
        StorageAnalyticsService analyticsService,
        SettingsConfigurationService settingsConfigService,
        IStorageFolderPicker folderPicker)
        : this(
            (forceRefresh, ct) => analyticsService.GetAnalyticsAsync(forceRefresh, ct),
            settingsConfigService,
            folderPicker)
    {
    }

    internal StorageViewModel(
        Func<bool, CancellationToken, Task<StorageAnalytics>> getAnalyticsAsync,
        SettingsConfigurationService settingsConfigService,
        IStorageFolderPicker folderPicker)
    {
        _settingsConfigService = settingsConfigService;
        _folderPicker = folderPicker;
        _getAnalyticsAsync = getAnalyticsAsync;

        BrowseStorageRootCommand = new RelayCommand(BrowseStorageRoot);
        RefreshMetricsCommand = new AsyncRelayCommand(
            ct => LoadStorageMetricsAsync(forceRefresh: true, ct),
            () => CanRefreshMetrics);
        RefreshPreview();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        await LoadStorageMetricsAsync();
    }

    // ── Data loading ──────────────────────────────────────────────────────────────────

    private async Task LoadStorageMetricsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        IsMetricsLoading = true;
        MetricsStatusText = forceRefresh
            ? "Refreshing storage metrics..."
            : "Scanning storage metrics...";

        try
        {
            var analytics = await _getAnalyticsAsync(forceRefresh, ct);

            TotalSizeText = FormatHelpers.FormatBytes(analytics.TotalSizeBytes);
            TotalFilesText = analytics.TotalFileCount.ToString("N0");
            SymbolCountText = analytics.SymbolBreakdown.Length.ToString("N0");

            HotTierSizeText = FormatHelpers.FormatBytes(analytics.TradeSizeBytes);
            WarmTierSizeText = FormatHelpers.FormatBytes(analytics.DepthSizeBytes);
            ColdTierSizeText = FormatHelpers.FormatBytes(analytics.HistoricalSizeBytes);
            ApplyStoragePosture(BuildStoragePosture(analytics));
            MetricsStatusText = BuildMetricsStatusText(analytics);
        }
        catch (OperationCanceledException)
        {
            MetricsStatusText = "Storage metrics scan cancelled.";
        }
        catch (Exception ex)
        {
            // Leave metric placeholders in place while making the posture failure explicit.
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "Storage metrics scan failed; showing unavailable posture.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
            ApplyStoragePosture(BuildStoragePostureUnavailable());
            MetricsStatusText = "Storage metrics scan failed. Verify the configured DataRoot and permissions, then refresh.";
        }
        finally
        {
            IsMetricsLoading = false;
        }
    }

    /// <summary>
    /// Regenerates the file-tree preview and storage estimate based on the current
    /// configuration selections.
    /// </summary>
    public void RefreshPreview(string dataDirectory, string naming, string compression)
    {
        _dataDirectoryText = string.IsNullOrWhiteSpace(dataDirectory) ? "./data" : dataDirectory;
        _namingConvention = string.IsNullOrWhiteSpace(naming) ? "BySymbol" : naming;
        _compressionFormat = string.IsNullOrWhiteSpace(compression) ? "gzip" : compression;
        OnPropertyChanged(nameof(DataDirectoryText));
        OnPropertyChanged(nameof(NamingConvention));
        OnPropertyChanged(nameof(CompressionFormat));
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var rootPath = NormalizePreviewRoot(DataDirectoryText);

        var symbols = new List<string> { "SPY", "AAPL", "MSFT" };

        FileTreePreviewText = _settingsConfigService.GenerateStoragePreview(
            rootPath, NamingConvention, "daily", CompressionFormat, symbols);

        var estimate = _settingsConfigService.EstimateDailyStorageSize(
            symbols.Count, trades: true, quotes: true, depth: false);
        StorageEstimateText =
            $"Estimated daily size: ~{estimate} for {symbols.Count} symbols (trades + quotes sample)";
        PreviewScopeText = $"{FormatNamingConvention(NamingConvention)} layout - {FormatCompression(CompressionFormat)} - {rootPath}/";
        PreviewActionText = "Use this preview to verify the archive path before running backfill, export, or packaging jobs.";
    }

    private void BrowseStorageRoot()
    {
        var selectedPath = _folderPicker.SelectFolder(DataDirectoryText);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DataDirectoryText = selectedPath;
        }
    }

    public static string NormalizePreviewRoot(string? dataDirectory)
    {
        var rootPath = dataDirectory?.Trim().TrimStart('.', '/', '\\').Replace('\\', '/') ?? string.Empty;
        return string.IsNullOrWhiteSpace(rootPath) ? "data" : rootPath;
    }

    private static string FormatNamingConvention(string? naming) =>
        naming?.ToLowerInvariant() switch
        {
            "bydate" => "By date",
            "bytype" => "By type",
            "flat" => "Flat",
            _ => "By symbol",
        };

    private static string FormatCompression(string? compression) =>
        compression?.ToLowerInvariant() switch
        {
            "none" => "No compression",
            "lz4" => "LZ4",
            "zstd" => "ZSTD",
            _ => "Gzip",
        };

    public static StoragePosture BuildStoragePosture(StorageAnalytics analytics)
    {
        var growthText = analytics.DailyGrowthBytes > 0
            ? $"Daily growth: {FormatHelpers.FormatBytes(analytics.DailyGrowthBytes)}/day"
            : "Daily growth: no recent files";

        var capacityText = analytics.ProjectedDaysUntilFull is int daysUntilFull
            ? $"Capacity horizon: {FormatDaysUntilFull(daysUntilFull)}"
            : "Capacity horizon: not enough growth history";

        var scanText = analytics.LastUpdated is DateTime lastUpdated
            ? $"Last scan: {lastUpdated.ToLocalTime():MMM d, h:mm tt}"
            : "Last scan: not available";

        if (analytics.TotalFileCount <= 0)
        {
            return new StoragePosture(
                "Archive waiting for data",
                "Run backfill or collection to start retaining market data under the configured DataRoot.",
                growthText,
                capacityText,
                scanText);
        }

        if (analytics.ProjectedDaysUntilFull is <= 14)
        {
            return new StoragePosture(
                "Storage capacity needs attention",
                "Current growth could exhaust usable disk space soon; review retention, tiering, or packaging before the next large backfill.",
                growthText,
                capacityText,
                scanText);
        }

        if (analytics.DailyGrowthBytes <= 0)
        {
            return new StoragePosture(
                "Archive is stable",
                $"No recent growth was detected across {analytics.TotalFileCount:N0} retained files; verify scheduled backfills if this archive should update daily.",
                growthText,
                capacityText,
                scanText);
        }

        return new StoragePosture(
            "Storage growth is being tracked",
            $"Archive contains {analytics.TotalFileCount:N0} files across {analytics.SymbolBreakdown.Length:N0} symbols; keep retention and package jobs aligned with growth.",
            growthText,
            capacityText,
            scanText);
    }

    public static StoragePosture BuildStoragePostureUnavailable() =>
        new(
            "Storage metrics unavailable",
            "Storage analytics could not scan the configured DataRoot; verify the path and permissions before running archive jobs.",
            "Daily growth: --",
            "Capacity horizon: --",
            "Last scan: failed");

    public static string BuildMetricsStatusText(StorageAnalytics analytics)
    {
        if (analytics.TotalFileCount <= 0)
        {
            return "Metrics loaded: no retained archive files found under the configured DataRoot.";
        }

        var fileLabel = analytics.TotalFileCount == 1 ? "file" : "files";
        var symbolCount = analytics.SymbolBreakdown.Length;
        var symbolLabel = symbolCount == 1 ? "symbol" : "symbols";

        return $"Metrics loaded: {analytics.TotalFileCount:N0} {fileLabel} across {symbolCount:N0} {symbolLabel}.";
    }

    private void ApplyStoragePosture(StoragePosture posture)
    {
        StoragePostureTitle = posture.Title;
        StoragePostureDetail = posture.Detail;
        StorageGrowthText = posture.GrowthText;
        StorageCapacityHorizonText = posture.CapacityHorizonText;
        StorageLastScanText = posture.LastScanText;
    }

    private static string FormatDaysUntilFull(int daysUntilFull) =>
        daysUntilFull <= 0
            ? "less than 1 day"
            : daysUntilFull == 1
                ? "1 day"
                : $"{daysUntilFull:N0} days";
}

public sealed record StoragePosture(
    string Title,
    string Detail,
    string GrowthText,
    string CapacityHorizonText,
    string LastScanText);
