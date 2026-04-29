using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.DataQuality;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public readonly struct TrendStatistics
{
    public string AvgText { get; init; }
    public string MinText { get; init; }
    public string MaxText { get; init; }
    public string StdDevText { get; init; }
    public string TrendText { get; init; }
    public bool HasData { get; init; }
    public bool IsTrendPositive { get; init; }
    public double ScoreChange { get; init; }
}

public sealed class DataQualityViewModel : BindableBase, IDisposable, IPageActionBarProvider
{
    private static readonly string[] s_anomalyTypeNames =
    {
        "PriceSpike", "PriceDrop", "VolumeSpike", "VolumeDrop", "SpreadWide", "StaleData",
        "RapidPriceChange", "AbnormalVolatility", "MissingData", "DuplicateData",
        "CrossedMarket", "InvalidPrice", "InvalidVolume"
    };

    private readonly IDataQualityApiClient _apiClient;
    private readonly IDataQualityPresentationService _presentationService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly DataQualityRefreshCoordinator _refreshCoordinator;
    private CancellationTokenSource? _refreshCts;
    private double _lastOverallScore = 98.5;
    /// <summary>
    /// Score from the previous snapshot; -1 means no prior reading yet.
    /// Used to detect significant drops that warrant a toast notification.
    /// </summary>
    private double _previousQualityScore = -1.0;
    private string _timeRange = "7d";
    private string _symbolFilter = string.Empty;
    private string _severityFilter = "All";
    private string _anomalyTypeFilter = "All";
    private readonly List<AlertModel> _allAlerts = new();
    private readonly List<AnomalyModel> _allAnomalies = new();

    public ObservableCollection<SymbolQualityModel> SymbolQuality { get; } = new();
    public ObservableCollection<SymbolQualityModel> FilteredSymbols { get; } = new();
    public ObservableCollection<GapModel> Gaps { get; } = new();
    public ObservableCollection<AlertModel> Alerts { get; } = new();
    public ObservableCollection<AnomalyModel> Anomalies { get; } = new();
    public ObservableCollection<DrilldownIssue> DrilldownIssues { get; } = new();
    public IReadOnlyList<DataQualityHeatmapCellPresentation> DrilldownHeatmapCells { get; private set; } = Array.Empty<DataQualityHeatmapCellPresentation>();
    public IReadOnlyList<TrendPoint> TrendPoints { get; private set; } = Array.Empty<TrendPoint>();
    public event EventHandler? TrendChartChanged;
    public event EventHandler? DrilldownChanged;

    private string _lastUpdateText = "Last updated: --";
    public string LastUpdateText { get => _lastUpdateText; private set => SetProperty(ref _lastUpdateText, value); }
    private string _overallScoreText = "--";
    public string OverallScoreText { get => _overallScoreText; private set => SetProperty(ref _overallScoreText, value); }
    private string _overallGradeText = "--";
    public string OverallGradeText { get => _overallGradeText; private set => SetProperty(ref _overallGradeText, value); }
    private string _statusText = "--";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    private Brush _scoreBrush = SuccessBrush();
    public Brush ScoreBrush { get => _scoreBrush; private set => SetProperty(ref _scoreBrush, value); }
    private DoubleCollection _scoreSegments = new() { 100, 100 };
    public DoubleCollection ScoreSegments { get => _scoreSegments; private set => SetProperty(ref _scoreSegments, value); }
    private string _latencyText = "--";
    public string LatencyText { get => _latencyText; private set => SetProperty(ref _latencyText, value); }
    private string _completenessText = "--";
    public string CompletenessText { get => _completenessText; private set => SetProperty(ref _completenessText, value); }
    private string _healthyFilesText = "--";
    public string HealthyFilesText { get => _healthyFilesText; private set => SetProperty(ref _healthyFilesText, value); }
    private string _warningFilesText = "--";
    public string WarningFilesText { get => _warningFilesText; private set => SetProperty(ref _warningFilesText, value); }
    private string _criticalFilesText = "--";
    public string CriticalFilesText { get => _criticalFilesText; private set => SetProperty(ref _criticalFilesText, value); }
    private string _gapsCountText = "--";
    public string GapsCountText { get => _gapsCountText; private set => SetProperty(ref _gapsCountText, value); }
    private Brush _gapsCountBrush = SuccessBrush();
    public Brush GapsCountBrush { get => _gapsCountBrush; private set => SetProperty(ref _gapsCountBrush, value); }
    private string _errorsCountText = "--";
    public string ErrorsCountText { get => _errorsCountText; private set => SetProperty(ref _errorsCountText, value); }
    private Brush _errorsCountBrush = SuccessBrush();
    public Brush ErrorsCountBrush { get => _errorsCountBrush; private set => SetProperty(ref _errorsCountBrush, value); }
    private string _unacknowledgedText = "--";
    public string UnacknowledgedText { get => _unacknowledgedText; private set => SetProperty(ref _unacknowledgedText, value); }
    private string _totalActiveAlertsText = "--";
    public string TotalActiveAlertsText { get => _totalActiveAlertsText; private set => SetProperty(ref _totalActiveAlertsText, value); }
    private string _alertCountBadgeText = "0";
    public string AlertCountBadgeText { get => _alertCountBadgeText; private set => SetProperty(ref _alertCountBadgeText, value); }
    private bool _isAlertCountBadgeVisible;
    public bool IsAlertCountBadgeVisible { get => _isAlertCountBadgeVisible; private set => SetProperty(ref _isAlertCountBadgeVisible, value); }
    private string _crossedMarketCount = "--";
    public string CrossedMarketCount { get => _crossedMarketCount; private set => SetProperty(ref _crossedMarketCount, value); }
    private string _staleDataCount = "--";
    public string StaleDataCount { get => _staleDataCount; private set => SetProperty(ref _staleDataCount, value); }
    private string _invalidPriceCount = "--";
    public string InvalidPriceCount { get => _invalidPriceCount; private set => SetProperty(ref _invalidPriceCount, value); }
    private string _invalidVolumeCount = "--";
    public string InvalidVolumeCount { get => _invalidVolumeCount; private set => SetProperty(ref _invalidVolumeCount, value); }
    private string _missingDataCount = "--";
    public string MissingDataCount { get => _missingDataCount; private set => SetProperty(ref _missingDataCount, value); }
    private string _lastCheckTimeText = "--";
    public string LastCheckTimeText { get => _lastCheckTimeText; private set => SetProperty(ref _lastCheckTimeText, value); }
    private string _nextCheckText = "--";
    public string NextCheckText { get => _nextCheckText; private set => SetProperty(ref _nextCheckText, value); }
    private double _checkProgressValue;
    public double CheckProgressValue { get => _checkProgressValue; private set => SetProperty(ref _checkProgressValue, value); }
    private string _p50Text = "--";
    public string P50Text { get => _p50Text; private set => SetProperty(ref _p50Text, value); }
    private string _p75Text = "--";
    public string P75Text { get => _p75Text; private set => SetProperty(ref _p75Text, value); }
    private string _p90Text = "--";
    public string P90Text { get => _p90Text; private set => SetProperty(ref _p90Text, value); }
    private string _p95Text = "--";
    public string P95Text { get => _p95Text; private set => SetProperty(ref _p95Text, value); }
    private string _p99Text = "--";
    public string P99Text { get => _p99Text; private set => SetProperty(ref _p99Text, value); }
    private bool _hasNoGaps = true;
    public bool HasNoGaps { get => _hasNoGaps; private set => SetProperty(ref _hasNoGaps, value); }
    private bool _hasNoAlerts = true;
    public bool HasNoAlerts { get => _hasNoAlerts; private set => SetProperty(ref _hasNoAlerts, value); }
    private bool _hasNoAnomalies = true;
    public bool HasNoAnomalies { get => _hasNoAnomalies; private set => SetProperty(ref _hasNoAnomalies, value); }
    private bool _hasNoSymbols = true;
    public bool HasNoSymbols { get => _hasNoSymbols; private set => SetProperty(ref _hasNoSymbols, value); }
    private bool _hasActiveSymbolFilter;
    public bool HasActiveSymbolFilter { get => _hasActiveSymbolFilter; private set => SetProperty(ref _hasActiveSymbolFilter, value); }
    private string _symbolFilterScopeText = "Showing all monitored symbols.";
    public string SymbolFilterScopeText { get => _symbolFilterScopeText; private set => SetProperty(ref _symbolFilterScopeText, value); }
    private string _symbolEmptyStateTitle = "No symbols are currently being monitored.";
    public string SymbolEmptyStateTitle { get => _symbolEmptyStateTitle; private set => SetProperty(ref _symbolEmptyStateTitle, value); }
    private string _symbolEmptyStateDetail = "Add symbols from the workspace before running quality checks.";
    public string SymbolEmptyStateDetail { get => _symbolEmptyStateDetail; private set => SetProperty(ref _symbolEmptyStateDetail, value); }
    private bool _isAnomalyCountBadgeVisible;
    public bool IsAnomalyCountBadgeVisible { get => _isAnomalyCountBadgeVisible; private set => SetProperty(ref _isAnomalyCountBadgeVisible, value); }
    private string _anomalyCountText = "0";
    public string AnomalyCountText { get => _anomalyCountText; private set => SetProperty(ref _anomalyCountText, value); }
    private string _trendText = "--";
    public string TrendText { get => _trendText; private set => SetProperty(ref _trendText, value); }
    private string _avgScoreText = "--";
    public string AvgScoreText { get => _avgScoreText; private set => SetProperty(ref _avgScoreText, value); }
    private string _minScoreText = "--";
    public string MinScoreText { get => _minScoreText; private set => SetProperty(ref _minScoreText, value); }
    private string _maxScoreText = "--";
    public string MaxScoreText { get => _maxScoreText; private set => SetProperty(ref _maxScoreText, value); }
    private string _stdDevText = "--";
    public string StdDevText { get => _stdDevText; private set => SetProperty(ref _stdDevText, value); }
    private string _trendIconGlyph = "\uE70E";
    public string TrendIconGlyph { get => _trendIconGlyph; private set => SetProperty(ref _trendIconGlyph, value); }
    private Brush _trendBrush = SuccessBrush();
    public Brush TrendBrush { get => _trendBrush; private set => SetProperty(ref _trendBrush, value); }
    private bool _isDrilldownVisible;
    public bool IsDrilldownVisible { get => _isDrilldownVisible; private set => SetProperty(ref _isDrilldownVisible, value); }
    private string _drilldownSymbolHeader = string.Empty;
    public string DrilldownSymbolHeader { get => _drilldownSymbolHeader; private set => SetProperty(ref _drilldownSymbolHeader, value); }
    private string _drilldownScoreText = "--";
    public string DrilldownScoreText { get => _drilldownScoreText; private set => SetProperty(ref _drilldownScoreText, value); }
    private Brush _drilldownScoreBrush = SuccessBrush();
    public Brush DrilldownScoreBrush { get => _drilldownScoreBrush; private set => SetProperty(ref _drilldownScoreBrush, value); }
    private string _drilldownCompletenessText = "--";
    public string DrilldownCompletenessText { get => _drilldownCompletenessText; private set => SetProperty(ref _drilldownCompletenessText, value); }
    private string _drilldownGapsText = "--";
    public string DrilldownGapsText { get => _drilldownGapsText; private set => SetProperty(ref _drilldownGapsText, value); }
    private string _drilldownErrorsText = "--";
    public string DrilldownErrorsText { get => _drilldownErrorsText; private set => SetProperty(ref _drilldownErrorsText, value); }
    private string _drilldownLatencyText = "--";
    public string DrilldownLatencyText { get => _drilldownLatencyText; private set => SetProperty(ref _drilldownLatencyText, value); }
    private bool _hasNoDrilldownIssues = true;
    public bool HasNoDrilldownIssues { get => _hasNoDrilldownIssues; private set => SetProperty(ref _hasNoDrilldownIssues, value); }

    // ── IPageActionBarProvider implementation ──────────────────────────────────────
    public string PageTitle => "Data Quality";
    public ObservableCollection<ActionEntry> Actions { get; } = new();

    public DataQualityViewModel(WpfServices.StatusService statusService, WpfServices.LoggingService loggingService, WpfServices.NotificationService notificationService, IRefreshScheduler? refreshScheduler = null)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;
        ApiClientService.Instance.Configure(statusService.BaseUrl);
        _apiClient = new DataQualityApiClient(ApiClientService.Instance);
        _presentationService = new DataQualityPresentationService(_apiClient);
        _refreshCoordinator = new DataQualityRefreshCoordinator(refreshScheduler ?? new PeriodicRefreshScheduler(), RefreshDataAsync, ex => _loggingService.LogError("Failed to refresh data quality", ex));
        UpdateTrendState();
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        // Populate action bar.
        Actions.Clear();
        Actions.Add(new ActionEntry("Refresh", new RelayCommand(() => _ = RefreshAsync()), "\uE72C", "Refresh data quality metrics", IsPrimary: true));
        Actions.Add(new ActionEntry("Export Report", new RelayCommand(() => _notificationService.NotifyInfo("Export", "Report export started")), "\uE8A5", "Export quality report"));

        return _refreshCoordinator.StartAsync(TimeSpan.FromSeconds(30), ct);
    }

    public void Stop()
    {
        _refreshCoordinator.Stop();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refreshCoordinator.RefreshAsync(ct).ConfigureAwait(false);
        _notificationService.ShowNotification("Refreshed", "Data quality metrics have been refreshed.", NotificationType.Info);
    }

    public void SetTimeRange(string timeRange)
    {
        _timeRange = string.IsNullOrWhiteSpace(timeRange) ? "7d" : timeRange;
        UpdateTrendState();
        _ = RefreshDataAsync();
    }

    public void ApplySymbolFilter(string query)
    {
        _symbolFilter = (query ?? string.Empty).Trim();
        ReplaceCollection(FilteredSymbols, SymbolQuality.Where(s => string.IsNullOrWhiteSpace(_symbolFilter) || s.Symbol.Contains(_symbolFilter, StringComparison.OrdinalIgnoreCase)));
        HasNoSymbols = FilteredSymbols.Count == 0;
        UpdateSymbolFilterState();
    }

    public void ClearSymbolFilter() => ApplySymbolFilter(string.Empty);

    public void ApplyAlertFilter(string severity)
    {
        _severityFilter = string.IsNullOrWhiteSpace(severity) ? "All" : severity;
        ReplaceCollection(Alerts, _allAlerts.Where(a => _severityFilter == "All" || a.Severity.Equals(_severityFilter, StringComparison.OrdinalIgnoreCase)));
        HasNoAlerts = Alerts.Count == 0;
    }

    public void ApplyAnomalyFilter(string type)
    {
        _anomalyTypeFilter = string.IsNullOrWhiteSpace(type) ? "All" : type;
        ReplaceCollection(Anomalies, _allAnomalies.Where(a => _anomalyTypeFilter == "All" || a.Type.Equals(_anomalyTypeFilter, StringComparison.OrdinalIgnoreCase)));
        HasNoAnomalies = Anomalies.Count == 0;
        IsAnomalyCountBadgeVisible = Anomalies.Count > 0;
        AnomalyCountText = Anomalies.Count.ToString();
    }

    public void ShowSymbolDrilldown(SymbolQualityModel model)
    {
        var presentation = _presentationService.BuildSymbolDrilldown(new DataQualitySymbolPresentation
        {
            Symbol = model.Symbol,
            Score = model.Score,
            ScoreFormatted = model.ScoreFormatted,
            Grade = model.Grade,
            Status = model.Status,
            Issues = model.Issues,
            LastUpdate = model.LastUpdate,
            LastUpdateFormatted = model.LastUpdateFormatted
        });

        DrilldownSymbolHeader = presentation.HeaderText;
        DrilldownScoreText = presentation.ScoreText;
        DrilldownScoreBrush = ToneToBrush(presentation.ScoreTone);
        DrilldownCompletenessText = presentation.CompletenessText;
        DrilldownGapsText = presentation.GapsText;
        DrilldownErrorsText = presentation.ErrorsText;
        DrilldownLatencyText = presentation.LatencyText;
        DrilldownHeatmapCells = presentation.HeatmapCells;

        ReplaceCollection(DrilldownIssues, presentation.Issues.Select(issue => new DrilldownIssue
        {
            Description = issue.Description,
            Timestamp = issue.Timestamp,
            SeverityBrush = CloneBrush(ToneToBrush(issue.Tone))
        }));

        HasNoDrilldownIssues = DrilldownIssues.Count == 0;
        IsDrilldownVisible = true;
        DrilldownChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HideSymbolDrilldown()
    {
        IsDrilldownVisible = false;
        DrilldownChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AcknowledgeAlertAsync(string alertId, CancellationToken ct = default)
    {
        try
        {
            if (await _apiClient.AcknowledgeAnomalyAsync(alertId, ct).ConfigureAwait(false))
            {
                _allAlerts.RemoveAll(a => a.Id == alertId);
                ApplyAlertFilter(_severityFilter);
                return;
            }

            _notificationService.ShowNotification("Acknowledge Failed", "Failed to acknowledge alert.", NotificationType.Warning);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to acknowledge alert", ex);
            _notificationService.ShowNotification("Acknowledge Failed", "An error occurred while acknowledging the alert.", NotificationType.Error);
        }
    }

    public async Task AcknowledgeAllAlertsAsync(CancellationToken ct = default)
    {
        foreach (var alert in _allAlerts.ToList())
        {
            try
            { await _apiClient.AcknowledgeAnomalyAsync(alert.Id, ct).ConfigureAwait(false); }
            catch (Exception ex) { _loggingService.LogError("Failed to acknowledge alert", ex); }
        }

        _allAlerts.Clear();
        ApplyAlertFilter(_severityFilter);
        _notificationService.ShowNotification("All Alerts Acknowledged", "All alerts have been acknowledged.", NotificationType.Success);
    }

    public async Task<bool> RepairGapAsync(string gapId, CancellationToken ct = default)
    {
        try
        {
            if (await _apiClient.RepairGapAsync(gapId, ct).ConfigureAwait(false))
            {
                var gap = Gaps.FirstOrDefault(g => g.GapId == gapId);
                if (gap is not null)
                {
                    Gaps.Remove(gap);
                }

                HasNoGaps = Gaps.Count == 0;
                _notificationService.ShowNotification("Gap Repair Started", "Repair has been initiated.", NotificationType.Success);
                return true;
            }

            _notificationService.ShowNotification("Repair Failed", "Failed to initiate gap repair.", NotificationType.Warning);
            return false;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to repair gap", ex);
            _notificationService.ShowNotification("Repair Failed", "An error occurred while initiating gap repair.", NotificationType.Error);
            return false;
        }
    }

    public async Task<bool> RepairAllGapsAsync(CancellationToken ct = default)
    {
        try
        {
            if (await _apiClient.RepairAllGapsAsync(ct).ConfigureAwait(false))
            {
                Gaps.Clear();
                HasNoGaps = true;
                _notificationService.ShowNotification("Repair Started", "Initiated repair for all gaps.", NotificationType.Success);
                return true;
            }

            _notificationService.ShowNotification("Repair Failed", "Failed to initiate gap repairs.", NotificationType.Warning);
            return false;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to repair all gaps", ex);
            _notificationService.ShowNotification("Repair Failed", "An error occurred while initiating gap repairs.", NotificationType.Error);
            return false;
        }
    }

    public async Task RunQualityCheckAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var result = await _apiClient.RunQualityCheckAsync(path, ct).ConfigureAwait(false);
            if (result?.Success == true)
            {
                _notificationService.ShowNotification("Quality Check Complete", "Quality check completed successfully.", NotificationType.Success);
                await RefreshDataAsync(ct).ConfigureAwait(false);
            }
            else
            {
                _notificationService.ShowNotification("Quality Check Failed", "Failed to run quality check.", NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to run quality check", ex);
            _notificationService.ShowNotification("Quality Check Failed", "An error occurred while running the quality check.", NotificationType.Error);
        }
    }

    public Task<DataQualityProviderComparisonPresentation> GetProviderComparisonAsync(string symbol, CancellationToken ct = default)
        => _presentationService.GetProviderComparisonAsync(symbol, ct);

    public TrendStatistics ComputeTrendStatistics()
    {
        if (TrendPoints.Count == 0)
        {
            return new TrendStatistics { HasData = false, AvgText = "--", MinText = "--", MaxText = "--", StdDevText = "--", TrendText = "--" };
        }

        var scores = TrendPoints.Select(point => point.Score).ToList();
        var average = scores.Average();
        var minimum = scores.Min();
        var maximum = scores.Max();
        var stdDev = Math.Sqrt(scores.Sum(score => Math.Pow(score - average, 2)) / scores.Count);
        var change = scores.Last() - scores.First();
        var positive = change >= 0;

        return new TrendStatistics
        {
            HasData = true,
            AvgText = $"{average:F1}%",
            MinText = $"{minimum:F1}%",
            MaxText = $"{maximum:F1}%",
            StdDevText = $"{stdDev:F1}%",
            TrendText = $"{(positive ? "+" : string.Empty)}{change:F1}% this {GetTimeWindowLabel(_timeRange)}",
            IsTrendPositive = positive,
            ScoreChange = change
        };
    }

    public static List<TrendPoint> BuildTrendPoints(double baseScore, string window)
    {
        var count = window switch { "1d" => 6, "7d" => 7, "30d" => 10, "90d" => 12, _ => 7 };
        var points = new List<TrendPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var factor = count == 1 ? 0 : i / (double)(count - 1);
            var delta = Math.Sin(factor * Math.PI) * 2.0 - 1.0;
            var score = Math.Clamp(baseScore + delta, 80, 100);
            var label = window switch
            {
                "1d" => DateTime.UtcNow.AddHours(-(count - 1 - i) * 4).ToString("HH:mm"),
                "7d" => DateTime.UtcNow.AddDays(-(count - 1 - i)).ToString("ddd"),
                "30d" => DateTime.UtcNow.AddDays(-(count - 1 - i) * 3).ToString("MMM d"),
                "90d" => DateTime.UtcNow.AddDays(-(count - 1 - i) * 7).ToString("MMM d"),
                _ => DateTime.UtcNow.AddDays(-(count - 1 - i)).ToString("MMM d")
            };
            points.Add(new TrendPoint(score, label));
        }

        return points;
    }

    public static string GetTimeWindowLabel(string window) => window switch
    {
        "1d" => "day",
        "7d" => "week",
        "30d" => "month",
        "90d" => "quarter",
        _ => "period"
    };

    public static string GetGrade(double score) => score switch
    {
        >= 95 => "A+",
        >= 90 => "A",
        >= 85 => "A-",
        >= 80 => "B+",
        >= 75 => "B",
        >= 70 => "B-",
        >= 65 => "C+",
        >= 60 => "C",
        >= 55 => "C-",
        >= 50 => "D",
        _ => "F"
    };

    public static string GetStatus(double score) => score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Healthy",
        >= 50 => "Warning",
        _ => "Critical"
    };

    public void Dispose()
    {
        Stop();
        _refreshCoordinator.Dispose();
    }

    private async Task RefreshDataAsync(CancellationToken ct = default)
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var snapshot = await _presentationService.GetSnapshotAsync(_timeRange, _refreshCts.Token).ConfigureAwait(false);
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (_refreshCts.IsCancellationRequested || ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh data quality", ex);
        }
    }

    private void ApplySnapshot(DataQualityPresentationSnapshot snapshot)
    {
        // Track the previous score before overwriting it so we can detect drops.
        var prevScore = _previousQualityScore < 0 ? snapshot.OverallScore : _lastOverallScore;

        _lastOverallScore = snapshot.OverallScore;
        LastUpdateText = snapshot.LastUpdateText;
        OverallScoreText = snapshot.OverallScoreText;
        OverallGradeText = snapshot.OverallGradeText;
        StatusText = snapshot.StatusText;
        ScoreBrush = ToneToBrush(snapshot.ScoreTone);
        ScoreSegments = new DoubleCollection { snapshot.OverallScore, Math.Max(0, 100 - snapshot.OverallScore) };
        LatencyText = snapshot.LatencyText;
        CompletenessText = snapshot.CompletenessText;
        HealthyFilesText = snapshot.HealthyFilesText;
        WarningFilesText = snapshot.WarningFilesText;
        CriticalFilesText = snapshot.CriticalFilesText;
        GapsCountText = snapshot.GapsCountText;
        GapsCountBrush = ToneToBrush(snapshot.GapsTone);
        ErrorsCountText = snapshot.ErrorsCountText;
        ErrorsCountBrush = ToneToBrush(snapshot.ErrorsTone);
        UnacknowledgedText = snapshot.UnacknowledgedText;
        TotalActiveAlertsText = snapshot.TotalActiveAlertsText;
        AlertCountBadgeText = snapshot.AlertCountBadgeText;
        IsAlertCountBadgeVisible = snapshot.IsAlertCountBadgeVisible;
        CrossedMarketCount = snapshot.CrossedMarketCount;
        StaleDataCount = snapshot.StaleDataCount;
        InvalidPriceCount = snapshot.InvalidPriceCount;
        InvalidVolumeCount = snapshot.InvalidVolumeCount;
        MissingDataCount = snapshot.MissingDataCount;
        LastCheckTimeText = snapshot.LastCheckTimeText;
        NextCheckText = snapshot.NextCheckText;
        CheckProgressValue = snapshot.CheckProgressValue;
        P50Text = snapshot.P50Text;
        P75Text = snapshot.P75Text;
        P90Text = snapshot.P90Text;
        P95Text = snapshot.P95Text;
        P99Text = snapshot.P99Text;

        ReplaceCollection(SymbolQuality, snapshot.Symbols.Select(symbol => new SymbolQualityModel
        {
            Symbol = symbol.Symbol,
            Score = symbol.Score,
            ScoreFormatted = symbol.ScoreFormatted,
            Grade = symbol.Grade,
            Status = symbol.Status,
            Issues = symbol.Issues,
            LastUpdate = symbol.LastUpdate,
            LastUpdateFormatted = symbol.LastUpdateFormatted
        }));

        ReplaceCollection(Gaps, snapshot.Gaps.Select(gap => new GapModel
        {
            GapId = gap.GapId,
            Symbol = gap.Symbol,
            Description = gap.Description,
            Duration = gap.Duration
        }));
        HasNoGaps = Gaps.Count == 0;

        _allAlerts.Clear();
        _allAlerts.AddRange(snapshot.Alerts.Select(alert => new AlertModel
        {
            Id = alert.Id,
            Symbol = alert.Symbol,
            AlertType = alert.AlertType,
            Message = alert.Message,
            Severity = alert.Severity,
            SeverityBrush = ToneToBrush(alert.SeverityTone)
        }));

        _allAnomalies.Clear();
        _allAnomalies.AddRange(snapshot.Anomalies.Select(anomaly => new AnomalyModel
        {
            Symbol = anomaly.Symbol,
            Description = anomaly.Description,
            Timestamp = anomaly.Timestamp,
            Type = anomaly.Type,
            SeverityColor = CloneBrush(ToneToBrush(anomaly.SeverityTone))
        }));

        ApplySymbolFilter(_symbolFilter);
        ApplyAlertFilter(_severityFilter);
        ApplyAnomalyFilter(_anomalyTypeFilter);
        UpdateTrendState();

        // Fire a Windows toast when the overall quality score drops below 90 %
        // by 2 or more points relative to the previous reading.
        const double WarningThreshold = 90.0;
        const double MinDropToAlert = 2.0;
        if (_previousQualityScore >= 0
            && snapshot.OverallScore < WarningThreshold
            && (prevScore - snapshot.OverallScore) >= MinDropToAlert)
        {
            WpfServices.ToastNotificationService.Instance.ShowDataQualityAlert("Overall", snapshot.OverallScore);
        }

        _previousQualityScore = snapshot.OverallScore;
    }

    private void UpdateTrendState()
    {
        TrendPoints = BuildTrendPoints(_lastOverallScore, _timeRange);
        var trend = ComputeTrendStatistics();
        AvgScoreText = trend.AvgText;
        MinScoreText = trend.MinText;
        MaxScoreText = trend.MaxText;
        StdDevText = trend.StdDevText;
        TrendText = trend.TrendText;
        TrendIconGlyph = trend.IsTrendPositive ? "\uE70E" : "\uE70D";
        TrendBrush = trend.ScoreChange switch
        {
            > 0.5 => ToneToBrush(DataQualityVisualTones.Success),
            < -0.5 => ToneToBrush(DataQualityVisualTones.Error),
            _ => ToneToBrush(DataQualityVisualTones.Warning)
        };
        TrendChartChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSymbolFilterState()
    {
        HasActiveSymbolFilter = !string.IsNullOrWhiteSpace(_symbolFilter);
        SymbolFilterScopeText = HasActiveSymbolFilter
            ? $"Filtering monitored symbols by \"{_symbolFilter}\"."
            : "Showing all monitored symbols.";

        if (SymbolQuality.Count == 0)
        {
            SymbolEmptyStateTitle = "No symbols are currently being monitored.";
            SymbolEmptyStateDetail = HasActiveSymbolFilter
                ? "Clear the filter or add symbols from the workspace before running quality checks."
                : "Add symbols from the workspace before running quality checks.";
            return;
        }

        if (HasActiveSymbolFilter)
        {
            SymbolEmptyStateTitle = $"No monitored symbols match \"{_symbolFilter}\".";
            SymbolEmptyStateDetail = "Clear the filter to return to the monitored-symbol quality list.";
            return;
        }

        SymbolEmptyStateTitle = "No symbols are currently being monitored.";
        SymbolEmptyStateDetail = "Add symbols from the workspace before running quality checks.";
    }

    private AlertModel? BuildAlertModel(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        var severityIndex = element.TryGetProperty("severity", out var severityNode) && severityNode.TryGetInt32(out var parsedSeverity) ? parsedSeverity : 0;

        return new AlertModel
        {
            Id = element.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Symbol = element.TryGetProperty("symbol", out var symbol) ? symbol.GetString() ?? string.Empty : string.Empty,
            AlertType = element.TryGetProperty("type", out var type) ? type.GetString() ?? "Unknown" : "Unknown",
            Message = element.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
            Severity = MapSeverity(severityIndex),
            SeverityBrush = ToneToBrush(MapSeverityTone(severityIndex))
        };
    }

    private AnomalyModel? BuildAnomalyModel(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        var severityIndex = element.TryGetProperty("severity", out var severityNode) && severityNode.TryGetInt32(out var parsedSeverity) ? parsedSeverity : 0;
        var typeIndex = element.TryGetProperty("type", out var typeNode) && typeNode.TryGetInt32(out var parsedType) ? parsedType : -1;
        var timestamp = string.Empty;
        if (element.TryGetProperty("detectedAt", out var detectedAtNode) && DateTimeOffset.TryParse(detectedAtNode.GetString(), out var detectedAt))
        {
            timestamp = detectedAt.ToString("MMM dd HH:mm");
        }

        return new AnomalyModel
        {
            Symbol = element.TryGetProperty("symbol", out var symbol) ? symbol.GetString() ?? string.Empty : string.Empty,
            Description = element.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
            Timestamp = timestamp,
            Type = typeIndex >= 0 && typeIndex < s_anomalyTypeNames.Length ? s_anomalyTypeNames[typeIndex] : "Unknown",
            SeverityColor = CloneBrush(ToneToBrush(MapSeverityTone(severityIndex)))
        };
    }

    private static string MapSeverity(int severity) => severity switch
    {
        >= 3 => "Critical",
        2 => "Error",
        1 => "Warning",
        _ => "Info"
    };

    private static string MapSeverityTone(int severity) => severity switch
    {
        >= 2 => DataQualityVisualTones.Error,
        1 => DataQualityVisualTones.Warning,
        _ => DataQualityVisualTones.Info
    };

    private static Brush ToneToBrush(string tone) => tone switch
    {
        DataQualityVisualTones.Success => SuccessBrush(),
        DataQualityVisualTones.Info => new SolidColorBrush(Color.FromRgb(33, 150, 243)),
        DataQualityVisualTones.Warning => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
        DataQualityVisualTones.Error => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
        _ => new SolidColorBrush(Color.FromRgb(139, 148, 158))
    };

    private static SolidColorBrush CloneBrush(Brush brush)
    {
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.Gray;
        return new SolidColorBrush(color);
    }

    private static SolidColorBrush SuccessBrush() => new(Color.FromRgb(63, 185, 80));

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
