using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Storage configuration and analytics page.
/// Owns async metric loading and the preview-generation logic so that the
/// code-behind is thinned to constructor DI and lifecycle wiring only.
/// </summary>
public sealed class StorageViewModel : BindableBase
{
    private readonly StorageAnalyticsService _analyticsService;
    private readonly SettingsConfigurationService _settingsConfigService;

    // ── Tier size properties ──────────────────────────────────────────────────────────
    private string _totalSizeText = "--";
    public string TotalSizeText { get => _totalSizeText; private set => SetProperty(ref _totalSizeText, value); }

    private string _totalFilesText = "--";
    public string TotalFilesText { get => _totalFilesText; private set => SetProperty(ref _totalFilesText, value); }

    private string _symbolCountText = "--";
    public string SymbolCountText { get => _symbolCountText; private set => SetProperty(ref _symbolCountText, value); }

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
    private string _fileTreePreviewText = string.Empty;
    public string FileTreePreviewText { get => _fileTreePreviewText; private set => SetProperty(ref _fileTreePreviewText, value); }

    private string _storageEstimateText = string.Empty;
    public string StorageEstimateText { get => _storageEstimateText; private set => SetProperty(ref _storageEstimateText, value); }

    private string _previewScopeText = "Preview pending";
    public string PreviewScopeText { get => _previewScopeText; private set => SetProperty(ref _previewScopeText, value); }

    private string _previewActionText = "Open the page to generate a sample SPY, AAPL, and MSFT layout.";
    public string PreviewActionText { get => _previewActionText; private set => SetProperty(ref _previewActionText, value); }

    public StorageViewModel(
        StorageAnalyticsService analyticsService,
        SettingsConfigurationService settingsConfigService)
    {
        _analyticsService = analyticsService;
        _settingsConfigService = settingsConfigService;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        await LoadStorageMetricsAsync();
    }

    // ── Data loading ──────────────────────────────────────────────────────────────────

    private async Task LoadStorageMetricsAsync()
    {
        try
        {
            var analytics = await _analyticsService.GetAnalyticsAsync();

            TotalSizeText = FormatHelpers.FormatBytes(analytics.TotalSizeBytes);
            TotalFilesText = analytics.TotalFileCount.ToString("N0");
            SymbolCountText = analytics.SymbolBreakdown.Length.ToString("N0");

            HotTierSizeText = FormatHelpers.FormatBytes(analytics.TradeSizeBytes);
            WarmTierSizeText = FormatHelpers.FormatBytes(analytics.DepthSizeBytes);
            ColdTierSizeText = FormatHelpers.FormatBytes(analytics.HistoricalSizeBytes);
            ApplyStoragePosture(BuildStoragePosture(analytics));
        }
        catch (Exception)
        {
            // Leave metric placeholders in place while making the posture failure explicit.
            ApplyStoragePosture(BuildStoragePostureUnavailable());
        }
    }

    /// <summary>
    /// Regenerates the file-tree preview and storage estimate based on the current
    /// configuration selections.  Called from the code-behind whenever a ComboBox or
    /// TextBox selection changes (WPF does not support direct Command binding on
    /// SelectionChanged for non-ItemsControl sources).
    /// </summary>
    public void RefreshPreview(string dataDirectory, string naming, string compression)
    {
        var rootPath = NormalizePreviewRoot(dataDirectory);

        var symbols = new List<string> { "SPY", "AAPL", "MSFT" };

        FileTreePreviewText = _settingsConfigService.GenerateStoragePreview(
            rootPath, naming, "daily", compression, symbols);

        var estimate = _settingsConfigService.EstimateDailyStorageSize(
            symbols.Count, trades: true, quotes: true, depth: false);
        StorageEstimateText =
            $"Estimated daily size: ~{estimate} for {symbols.Count} symbols (trades + quotes sample)";
        PreviewScopeText = $"{FormatNamingConvention(naming)} layout - {FormatCompression(compression)} - {rootPath}/";
        PreviewActionText = "Use this preview to verify the archive path before running backfill, export, or packaging jobs.";
    }

    public static string NormalizePreviewRoot(string? dataDirectory)
    {
        var rootPath = dataDirectory?.Trim().TrimStart('.', '/', '\\') ?? string.Empty;
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
