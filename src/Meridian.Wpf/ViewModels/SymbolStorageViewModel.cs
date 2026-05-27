using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Symbol Storage page. Owns analytics loading, metric formatting,
/// empty/error state, and refresh command state so the page remains declarative.
/// </summary>
public sealed class SymbolStorageViewModel : BindableBase
{
    private readonly Func<bool, CancellationToken, Task<StorageAnalytics>> _getAnalyticsAsync;
    private string _totalSizeText = "--";
    private string _totalFilesText = "--";
    private string _symbolCountText = "--";
    private string _dailyGrowthText = "--";
    private string _tradeSizeText = "--";
    private string _tradeFilesText = "-- files";
    private string _depthSizeText = "--";
    private string _depthFilesText = "-- files";
    private string _historicalSizeText = "--";
    private string _historicalFilesText = "-- files";
    private string _statusText = "Storage analytics pending. Open Symbol Storage to scan retained market data.";
    private string _errorText = string.Empty;
    private string _projectionText = string.Empty;
    private bool _isLoading;
    private bool _hasProjection;

    public SymbolStorageViewModel()
        : this((forceRefresh, ct) => StorageAnalyticsService.Instance.GetAnalyticsAsync(forceRefresh, ct))
    {
    }

    internal SymbolStorageViewModel(Func<bool, CancellationToken, Task<StorageAnalytics>> getAnalyticsAsync)
    {
        _getAnalyticsAsync = getAnalyticsAsync ?? throw new ArgumentNullException(nameof(getAnalyticsAsync));
        RefreshCommand = new AsyncRelayCommand(
            ct => LoadAnalyticsAsync(forceRefresh: true, ct),
            () => CanRefresh);
    }

    public ObservableCollection<SymbolStorageItem> Symbols { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }

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

    public string SymbolCountText
    {
        get => _symbolCountText;
        private set => SetProperty(ref _symbolCountText, value);
    }

    public string DailyGrowthText
    {
        get => _dailyGrowthText;
        private set => SetProperty(ref _dailyGrowthText, value);
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

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                RaisePropertyChanged(nameof(HasError));
                RaisePropertyChanged(nameof(IsSymbolListEmpty));
            }
        }
    }

    public string ProjectionText
    {
        get => _projectionText;
        private set => SetProperty(ref _projectionText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaisePropertyChanged(nameof(CanRefresh));
                RaisePropertyChanged(nameof(IsSymbolListEmpty));
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSymbols => Symbols.Count > 0;

    public bool IsSymbolListEmpty => !IsLoading && !HasError && !HasSymbols;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasProjection
    {
        get => _hasProjection;
        private set => SetProperty(ref _hasProjection, value);
    }

    public bool CanRefresh => !IsLoading;

    public async Task LoadAsync(CancellationToken ct = default)
        => await LoadAnalyticsAsync(forceRefresh: false, ct);

    private async Task LoadAnalyticsAsync(bool forceRefresh, CancellationToken ct)
    {
        IsLoading = true;
        ErrorText = string.Empty;
        StatusText = forceRefresh ? "Refreshing symbol storage analytics..." : "Scanning symbol storage analytics...";

        try
        {
            var analytics = await _getAnalyticsAsync(forceRefresh, ct).ConfigureAwait(true);
            ApplyAnalytics(analytics);
            StatusText = BuildStatusText(analytics);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Symbol storage analytics scan cancelled.";
        }
        catch (Exception ex)
        {
            ErrorText = $"Failed to load symbol storage analytics: {ex.Message}";
            StatusText = "Symbol storage analytics unavailable. Verify the configured DataRoot and permissions, then refresh.";
            HasProjection = false;
            ProjectionText = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyAnalytics(StorageAnalytics analytics)
    {
        TotalSizeText = FormatHelpers.FormatBytes(analytics.TotalSizeBytes);
        TotalFilesText = analytics.TotalFileCount.ToString("N0");
        SymbolCountText = analytics.SymbolBreakdown.Length.ToString("N0");
        DailyGrowthText = analytics.DailyGrowthBytes > 0
            ? $"{FormatHelpers.FormatBytes(analytics.DailyGrowthBytes)}/d"
            : "--";

        TradeSizeText = FormatHelpers.FormatBytes(analytics.TradeSizeBytes);
        TradeFilesText = FormatFileCount(analytics.TradeFileCount);
        DepthSizeText = FormatHelpers.FormatBytes(analytics.DepthSizeBytes);
        DepthFilesText = FormatFileCount(analytics.DepthFileCount);
        HistoricalSizeText = FormatHelpers.FormatBytes(analytics.HistoricalSizeBytes);
        HistoricalFilesText = FormatFileCount(analytics.HistoricalFileCount);

        Symbols.Clear();
        foreach (var symbolInfo in analytics.SymbolBreakdown)
        {
            Symbols.Add(new SymbolStorageItem(
                symbolInfo.Symbol,
                FormatHelpers.FormatBytes(symbolInfo.SizeBytes),
                FormatFileCount(symbolInfo.FileCount),
                symbolInfo.PercentOfTotal,
                $"{symbolInfo.OldestData:MMM dd} - {symbolInfo.NewestData:MMM dd}"));
        }

        ProjectionText = BuildProjectionText(analytics);
        HasProjection = !string.IsNullOrWhiteSpace(ProjectionText);
        RaiseSymbolStateChanged();
    }

    internal static string BuildStatusText(StorageAnalytics analytics)
    {
        if (analytics.TotalFileCount <= 0)
        {
            return "Analytics loaded: no retained symbol files were found under the configured DataRoot.";
        }

        var fileLabel = analytics.TotalFileCount == 1 ? "file" : "files";
        var symbolCount = analytics.SymbolBreakdown.Length;
        var symbolLabel = symbolCount == 1 ? "symbol" : "symbols";
        return $"Analytics loaded: {analytics.TotalFileCount:N0} {fileLabel} across {symbolCount:N0} {symbolLabel}.";
    }

    internal static string BuildProjectionText(StorageAnalytics analytics)
    {
        if (analytics.ProjectedDaysUntilFull is not int days || analytics.DailyGrowthBytes <= 0)
        {
            return string.Empty;
        }

        var growthText = FormatHelpers.FormatBytes(analytics.DailyGrowthBytes);
        return days > 365
            ? $"At current growth rate ({growthText}/day), disk capacity is projected to last more than a year."
            : $"At current growth rate ({growthText}/day), disk capacity is projected to be reached in approximately {days:N0} days.";
    }

    private void RaiseSymbolStateChanged()
    {
        RaisePropertyChanged(nameof(HasSymbols));
        RaisePropertyChanged(nameof(IsSymbolListEmpty));
    }

    private static string FormatFileCount(int fileCount)
        => fileCount == 1 ? "1 file" : $"{fileCount:N0} files";
}

public sealed record SymbolStorageItem(
    string Symbol,
    string SizeText,
    string FileCountText,
    double PercentOfTotal,
    string DateRangeText);
