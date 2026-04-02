using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Advanced Analytics page. Manages quality reports, gap analysis,
/// cross-provider comparison, latency histograms, and rate-limit status.
/// All state is exposed as bindable properties or ObservableCollections for direct XAML binding.
/// </summary>
public sealed class AdvancedAnalyticsViewModel : BindableBase
{
    private readonly AdvancedAnalyticsServiceBase _analyticsService;
    private GapAnalysisResult? _lastGapAnalysis;

    // ---- Symbol / date inputs ----
    private string _gapSymbol = string.Empty;
    private string _compareSymbol = string.Empty;
    private DateTime _compareDate = DateTime.Today;

    // ---- Quality report state ----
    private string _overallScoreText = "--";
    private string _gradeText = "--";
    private Brush _gradeBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 245, 101, 101));
    private string _completenessText = "--";

    // ---- Gap analysis state ----
    private bool _isGapAnalysisBusy;
    private bool _isGapSummaryVisible;
    private string _totalGapsText = "0";
    private string _totalGapDurationText = "--";
    private string _repairableGapsText = "0";
    private bool _canRepairGaps;

    // ---- Comparison state ----
    private bool _isComparisonBusy;
    private bool _isComparisonResultsVisible;
    private string _consistencyScoreText = "--";
    private string _discrepancyCountText = "0";

    // ---- Recommendations ----
    private bool _isRecommendationsVisible;

    // ---- InfoBar ----
    private bool _isStatusVisible;
    private string _statusIcon = string.Empty;
    private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(72, 187, 120));
    private string _statusTitle = string.Empty;
    private string _statusMessage = string.Empty;

    // ---- Busy guard ----
    private bool _isRefreshBusy;

    public AdvancedAnalyticsViewModel(AdvancedAnalyticsServiceBase analyticsService)
    {
        _analyticsService = analyticsService;
    }

    // ---- Collections ----
    public ObservableCollection<string> AvailableSymbols { get; } = new();
    public ObservableCollection<SymbolQualityDisplayItem> SymbolQualityItems { get; } = new();
    public ObservableCollection<string> Recommendations { get; } = new();
    public ObservableCollection<GapDisplayItem> GapItems { get; } = new();
    public ObservableCollection<DiscrepancyDisplayItem> DiscrepancyItems { get; } = new();
    public ObservableCollection<LatencyDisplayItem> LatencyItems { get; } = new();
    public ObservableCollection<RateLimitDisplayItem> RateLimitItems { get; } = new();

    // ---- Input properties ----
    public string GapSymbol
    {
        get => _gapSymbol;
        set => SetProperty(ref _gapSymbol, value);
    }

    public string CompareSymbol
    {
        get => _compareSymbol;
        set => SetProperty(ref _compareSymbol, value);
    }

    public DateTime CompareDate
    {
        get => _compareDate;
        set => SetProperty(ref _compareDate, value);
    }

    // ---- Quality report properties ----
    public string OverallScoreText
    {
        get => _overallScoreText;
        private set => SetProperty(ref _overallScoreText, value);
    }

    public string GradeText
    {
        get => _gradeText;
        private set
        {
            if (SetProperty(ref _gradeText, value))
                GradeBadgeBackground = GetGradeBackground(value);
        }
    }

    public Brush GradeBadgeBackground
    {
        get => _gradeBadgeBackground;
        private set => SetProperty(ref _gradeBadgeBackground, value);
    }

    public string CompletenessText
    {
        get => _completenessText;
        private set => SetProperty(ref _completenessText, value);
    }

    // ---- Gap analysis properties ----
    public bool IsGapAnalysisBusy
    {
        get => _isGapAnalysisBusy;
        private set => SetProperty(ref _isGapAnalysisBusy, value);
    }

    public bool IsGapSummaryVisible
    {
        get => _isGapSummaryVisible;
        private set => SetProperty(ref _isGapSummaryVisible, value);
    }

    public string TotalGapsText
    {
        get => _totalGapsText;
        private set => SetProperty(ref _totalGapsText, value);
    }

    public string TotalGapDurationText
    {
        get => _totalGapDurationText;
        private set => SetProperty(ref _totalGapDurationText, value);
    }

    public string RepairableGapsText
    {
        get => _repairableGapsText;
        private set => SetProperty(ref _repairableGapsText, value);
    }

    public bool CanRepairGaps
    {
        get => _canRepairGaps;
        private set => SetProperty(ref _canRepairGaps, value);
    }

    // ---- Comparison properties ----
    public bool IsComparisonBusy
    {
        get => _isComparisonBusy;
        private set => SetProperty(ref _isComparisonBusy, value);
    }

    public bool IsComparisonResultsVisible
    {
        get => _isComparisonResultsVisible;
        private set => SetProperty(ref _isComparisonResultsVisible, value);
    }

    public string ConsistencyScoreText
    {
        get => _consistencyScoreText;
        private set => SetProperty(ref _consistencyScoreText, value);
    }

    public string DiscrepancyCountText
    {
        get => _discrepancyCountText;
        private set => SetProperty(ref _discrepancyCountText, value);
    }

    // ---- Recommendations visibility ----
    public bool IsRecommendationsVisible
    {
        get => _isRecommendationsVisible;
        private set => SetProperty(ref _isRecommendationsVisible, value);
    }

    // ---- Refresh busy ----
    public bool IsRefreshBusy
    {
        get => _isRefreshBusy;
        private set => SetProperty(ref _isRefreshBusy, value);
    }

    // ---- InfoBar properties ----
    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        private set => SetProperty(ref _isStatusVisible, value);
    }

    public string StatusIcon
    {
        get => _statusIcon;
        private set => SetProperty(ref _statusIcon, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // ---- Init ----

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadSymbolsAsync(ct);
        await RefreshAllAsync(ct);
    }

    // ---- Refresh ----

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        IsRefreshBusy = true;
        try
        {
            await LoadQualityReportAsync(ct);
            await LoadLatencyDataAsync(ct);
            await LoadRateLimitsAsync(ct);
        }
        finally
        {
            IsRefreshBusy = false;
        }
    }

    // ---- Symbols ----

    public async Task LoadSymbolsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _analyticsService.GetAllSymbolsAsync(ct);
            AvailableSymbols.Clear();
            AvailableSymbols.Add(string.Empty); // represents "All Symbols" entry
            if (result.Success)
            {
                foreach (var s in result.Symbols.Take(50))
                {
                    AvailableSymbols.Add(s.Symbol);
                }
            }
        }
        catch
        {
            // Keep defaults
        }
    }

    // ---- Quality report ----

    public async Task LoadQualityReportAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _analyticsService.GetQualityReportAsync(new DataQualityReportOptions
            {
                IncludeDetails = true
            }, ct);

            if (result.Success)
            {
                OverallScoreText = $"{result.OverallScore:F0}%";
                GradeText = result.Grade;

                if (result.Metrics != null)
                {
                    CompletenessText = $"{result.Metrics.CompletenessScore:F0}%";
                }

                SymbolQualityItems.Clear();
                foreach (var r in result.SymbolReports)
                {
                    SymbolQualityItems.Add(new SymbolQualityDisplayItem
                    {
                        Symbol = r.Symbol,
                        Grade = r.Grade,
                        GradeBackground = GetGradeBackground(r.Grade),
                        OverallScore = r.OverallScore,
                        CompletenessText = $"{r.CompletenessScore:F0}%",
                        IntegrityText = $"{r.IntegrityScore:F0}%",
                        IssueCount = r.Issues.Count > 0 ? r.Issues.Count.ToString() : "-"
                    });
                }

                Recommendations.Clear();
                IsRecommendationsVisible = result.Recommendations.Count > 0;
                if (IsRecommendationsVisible)
                {
                    foreach (var rec in result.Recommendations)
                    {
                        Recommendations.Add(rec);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ShowError("Failed to load quality report", ex.Message);
        }
    }

    // ---- Gap analysis ----

    public async Task AnalyzeGapsAsync(CancellationToken ct = default)
    {
        IsGapAnalysisBusy = true;
        try
        {
            var symbol = string.IsNullOrEmpty(GapSymbol) ? null : GapSymbol;

            var result = await _analyticsService.AnalyzeGapsAsync(new GapAnalysisOptions
            {
                Symbol = symbol,
                MinGapMinutes = 5
            }, ct);

            _lastGapAnalysis = result;

            if (result.Success)
            {
                IsGapSummaryVisible = true;
                TotalGapsText = result.TotalGaps.ToString();
                TotalGapDurationText = FormatDuration(result.TotalGapDuration);

                var repairableCount = result.Gaps.Count(g => g.IsRepairable);
                RepairableGapsText = repairableCount.ToString();
                CanRepairGaps = repairableCount > 0;

                GapItems.Clear();
                foreach (var g in result.Gaps)
                {
                    GapItems.Add(new GapDisplayItem
                    {
                        Symbol = g.Symbol,
                        EventType = g.EventType,
                        TimeRange = $"{g.StartTime:HH:mm} - {g.EndTime:HH:mm}",
                        DurationText = FormatDuration(g.Duration),
                        RepairableText = g.IsRepairable ? "Repairable" : "Manual",
                        RepairableBackground = new SolidColorBrush(g.IsRepairable
                            ? Color.FromArgb(40, 72, 187, 120)
                            : Color.FromArgb(40, 160, 160, 160))
                    });
                }
            }
            else
            {
                ShowError("Gap analysis failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Gap analysis failed", ex.Message);
        }
        finally
        {
            IsGapAnalysisBusy = false;
        }
    }

    public async Task RepairGapsAsync(CancellationToken ct = default)
    {
        if (_lastGapAnalysis == null) return;

        try
        {
            var result = await _analyticsService.RepairGapsAsync(new GapRepairOptions
            {
                UseAlternativeProviders = true
            }, ct);

            if (result.Success)
            {
                ShowSuccess($"Repair complete. {result.GapsRepaired} gaps repaired, {result.RecordsRecovered} records recovered.");
                await AnalyzeGapsAsync(ct);
            }
            else
            {
                ShowError("Repair failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Repair failed", ex.Message);
        }
    }

    // ---- Cross-provider comparison ----

    public async Task CompareProvidersAsync(CancellationToken ct = default)
    {
        var symbol = CompareSymbol.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
        {
            ShowError("Validation", "Please enter a symbol to compare.");
            return;
        }

        IsComparisonBusy = true;
        try
        {
            var result = await _analyticsService.CompareProvidersAsync(new CrossProviderComparisonOptions
            {
                Symbol = symbol,
                Date = DateOnly.FromDateTime(CompareDate)
            }, ct);

            if (result.Success)
            {
                IsComparisonResultsVisible = true;
                ConsistencyScoreText = $"{result.OverallConsistencyScore:F1}%";
                DiscrepancyCountText = result.Discrepancies.Count.ToString();

                DiscrepancyItems.Clear();
                foreach (var d in result.Discrepancies)
                {
                    DiscrepancyItems.Add(new DiscrepancyDisplayItem
                    {
                        TimestampText = d.Timestamp.ToString("HH:mm:ss"),
                        DiscrepancyType = d.DiscrepancyType,
                        Values = $"{d.Provider1}: {d.Value1} | {d.Provider2}: {d.Value2}",
                        DifferenceText = $"{d.Difference:F2}%"
                    });
                }
            }
            else
            {
                ShowError("Comparison failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            ShowError("Comparison failed", ex.Message);
        }
        finally
        {
            IsComparisonBusy = false;
        }
    }

    // ---- Latency ----

    public async Task LoadLatencyDataAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _analyticsService.GetLatencyHistogramAsync(ct: ct);

            LatencyItems.Clear();
            if (result.Success)
            {
                var maxP99 = result.Providers.Count > 0 ? result.Providers.Max(p => p.P99Ms) : 100;
                if (maxP99 < 1) maxP99 = 100;

                foreach (var p in result.Providers)
                {
                    LatencyItems.Add(new LatencyDisplayItem
                    {
                        Provider = p.Provider,
                        P50Text = $"{p.P50Ms:F0}ms",
                        P95Text = $"{p.P95Ms:F0}ms",
                        P99Text = $"{p.P99Ms:F0}ms",
                        LatencyPercent = (p.P50Ms / maxP99) * 100
                    });
                }
            }
            else
            {
                SetDefaultLatencyItems();
            }
        }
        catch
        {
            SetDefaultLatencyItems();
        }
    }

    private void SetDefaultLatencyItems()
    {
        LatencyItems.Clear();
        LatencyItems.Add(new LatencyDisplayItem { Provider = "Alpaca", P50Text = "--ms", P95Text = "--ms", P99Text = "--ms" });
        LatencyItems.Add(new LatencyDisplayItem { Provider = "Polygon", P50Text = "--ms", P95Text = "--ms", P99Text = "--ms" });
    }

    // ---- Rate limits ----

    public async Task LoadRateLimitsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _analyticsService.GetRateLimitStatusAsync(ct);

            RateLimitItems.Clear();
            if (result.Success)
            {
                foreach (var p in result.Providers)
                {
                    RateLimitItems.Add(new RateLimitDisplayItem
                    {
                        Provider = p.Provider,
                        UsagePercent = p.UsagePercent,
                        UsageText = $"{p.RequestsUsed}/{p.RequestsPerMinute}",
                        StatusText = p.IsThrottled ? "Throttled" : (p.UsagePercent > 80 ? "High" : "OK"),
                        UsageColor = new SolidColorBrush(p.IsThrottled
                            ? Color.FromRgb(245, 101, 101)
                            : (p.UsagePercent > 80 ? Color.FromRgb(237, 137, 54) : Color.FromRgb(72, 187, 120))),
                        StatusBackground = new SolidColorBrush(p.IsThrottled
                            ? Color.FromArgb(40, 245, 101, 101)
                            : (p.UsagePercent > 80 ? Color.FromArgb(40, 237, 137, 54) : Color.FromArgb(40, 72, 187, 120)))
                    });
                }
            }
            else
            {
                SetDefaultRateLimitItems();
            }
        }
        catch
        {
            SetDefaultRateLimitItems();
        }
    }

    private void SetDefaultRateLimitItems()
    {
        RateLimitItems.Clear();
        RateLimitItems.Add(new RateLimitDisplayItem { Provider = "Alpaca", UsageText = "--/--", StatusText = "--", UsagePercent = 0 });
        RateLimitItems.Add(new RateLimitDisplayItem { Provider = "Polygon", UsageText = "--/--", StatusText = "--", UsagePercent = 0 });
    }

    // ---- Helpers ----

    public void DismissStatus() => IsStatusVisible = false;

    public void ShowSuccess(string message) =>
        SetStatus("\uE73E", Color.FromRgb(72, 187, 120), "Success", message);

    private void ShowError(string title, string message) =>
        SetStatus("\uEA39", Color.FromRgb(245, 101, 101), title, message);

    private void SetStatus(string icon, Color color, string title, string message)
    {
        StatusIcon = icon;
        StatusBrush = new SolidColorBrush(color);
        StatusTitle = title;
        StatusMessage = message;
        IsStatusVisible = true;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{(int)duration.TotalMinutes}m";
    }

    private static SolidColorBrush GetGradeBackground(string grade) => grade switch
    {
        "A" or "A+" => new SolidColorBrush(Color.FromArgb(40, 72, 187, 120)),
        "B" or "B+" => new SolidColorBrush(Color.FromArgb(40, 88, 166, 255)),
        "C" or "C+" => new SolidColorBrush(Color.FromArgb(40, 237, 137, 54)),
        _ => new SolidColorBrush(Color.FromArgb(40, 245, 101, 101))
    };
}

// ---- Display item types ----

public sealed class SymbolQualityDisplayItem
{
    public string Symbol { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public SolidColorBrush? GradeBackground { get; set; }
    public double OverallScore { get; set; }
    public string CompletenessText { get; set; } = string.Empty;
    public string IntegrityText { get; set; } = string.Empty;
    public string IssueCount { get; set; } = string.Empty;
}

public sealed class GapDisplayItem
{
    public string Symbol { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public string DurationText { get; set; } = string.Empty;
    public string RepairableText { get; set; } = string.Empty;
    public SolidColorBrush? RepairableBackground { get; set; }
}

public sealed class DiscrepancyDisplayItem
{
    public string TimestampText { get; set; } = string.Empty;
    public string DiscrepancyType { get; set; } = string.Empty;
    public string Values { get; set; } = string.Empty;
    public string DifferenceText { get; set; } = string.Empty;
}

public sealed class LatencyDisplayItem
{
    public string Provider { get; set; } = string.Empty;
    public string P50Text { get; set; } = string.Empty;
    public string P95Text { get; set; } = string.Empty;
    public string P99Text { get; set; } = string.Empty;
    public double LatencyPercent { get; set; }
}

public sealed class RateLimitDisplayItem
{
    public string Provider { get; set; } = string.Empty;
    public double UsagePercent { get; set; }
    public string UsageText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public SolidColorBrush? UsageColor { get; set; }
    public SolidColorBrush? StatusBackground { get; set; }
}
