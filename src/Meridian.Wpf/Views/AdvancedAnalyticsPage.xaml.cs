using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for advanced analytics including gap analysis, cross-provider comparison,
/// latency histograms, anomaly detection, and detailed quality reports.
/// </summary>
public partial class AdvancedAnalyticsPage : Page
{
    private readonly AdvancedAnalyticsServiceBase _analyticsService;
    private GapAnalysisResult? _lastGapAnalysis;

    public AdvancedAnalyticsPage(AdvancedAnalyticsServiceBase analyticsService)
    {
        InitializeComponent();
        _analyticsService = analyticsService;

        CompareDatePicker.SelectedDate = DateTime.Today;
        Loaded += AdvancedAnalyticsPage_Loaded;
    }

    private async void AdvancedAnalyticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSymbolsAsync();
        await RefreshAllAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async System.Threading.Tasks.Task RefreshAllAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            await LoadQualityReportAsync();
            await LoadLatencyDataAsync();
            await LoadRateLimitsAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task LoadSymbolsAsync()
    {
        try
        {
            var result = await _analyticsService.GetAllSymbolsAsync();
            if (result.Success)
            {
                GapSymbolCombo.Items.Clear();
                GapSymbolCombo.Items.Add(new ComboBoxItem { Content = "All Symbols", Tag = "" });

                foreach (var symbol in result.Symbols.Take(50))
                {
                    GapSymbolCombo.Items.Add(new ComboBoxItem { Content = symbol.Symbol, Tag = symbol.Symbol });
                }

                GapSymbolCombo.SelectedIndex = 0;
            }
        }
        catch
        {
            // Keep default items
        }
    }


    private async System.Threading.Tasks.Task LoadQualityReportAsync()
    {
        try
        {
            var result = await _analyticsService.GetQualityReportAsync(new DataQualityReportOptions
            {
                IncludeDetails = true
            });

            if (result.Success)
            {
                OverallScoreText.Text = $"{result.OverallScore:F0}%";
                GradeText.Text = result.Grade;
                UpdateGradeBadge(result.Grade);

                if (result.Metrics != null)
                {
                    CompletenessText.Text = $"{result.Metrics.CompletenessScore:F0}%";
                }

                var items = result.SymbolReports.Select(r => new SymbolQualityDisplayItem
                {
                    Symbol = r.Symbol,
                    Grade = r.Grade,
                    GradeBackground = GetGradeBackground(r.Grade),
                    OverallScore = r.OverallScore,
                    CompletenessText = $"{r.CompletenessScore:F0}%",
                    IntegrityText = $"{r.IntegrityScore:F0}%",
                    IssueCount = r.Issues.Count > 0 ? r.Issues.Count.ToString() : "-"
                }).ToList();

                SymbolQualityList.ItemsSource = items;

                if (result.Recommendations.Count > 0)
                {
                    RecommendationsCard.Visibility = Visibility.Visible;
                    RecommendationsList.ItemsSource = result.Recommendations;
                }
                else
                {
                    RecommendationsCard.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            ShowError("Failed to load quality report", ex.Message);
        }
    }

    private void UpdateGradeBadge(string grade)
    {
        var background = grade switch
        {
            "A" or "A+" => new SolidColorBrush(Color.FromArgb(40, 72, 187, 120)),
            "B" or "B+" => new SolidColorBrush(Color.FromArgb(40, 88, 166, 255)),
            "C" or "C+" => new SolidColorBrush(Color.FromArgb(40, 237, 137, 54)),
            _ => new SolidColorBrush(Color.FromArgb(40, 245, 101, 101))
        };
        GradeBadge.Background = background;
    }

    private static SolidColorBrush GetGradeBackground(string grade)
    {
        return grade switch
        {
            "A" or "A+" => new SolidColorBrush(Color.FromArgb(40, 72, 187, 120)),
            "B" or "B+" => new SolidColorBrush(Color.FromArgb(40, 88, 166, 255)),
            "C" or "C+" => new SolidColorBrush(Color.FromArgb(40, 237, 137, 54)),
            _ => new SolidColorBrush(Color.FromArgb(40, 245, 101, 101))
        };
    }

    private async void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        await LoadQualityReportAsync();
        ShowSuccess("Quality report generated.");
    }



    private async void AnalyzeGaps_Click(object sender, RoutedEventArgs e)
    {
        GapAnalysisProgress.Visibility = Visibility.Visible;
        try
        {
            var symbol = (GapSymbolCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            var result = await _analyticsService.AnalyzeGapsAsync(new GapAnalysisOptions
            {
                Symbol = string.IsNullOrEmpty(symbol) ? null : symbol,
                MinGapMinutes = 5
            });

            _lastGapAnalysis = result;

            if (result.Success)
            {
                GapSummaryCard.Visibility = Visibility.Visible;
                TotalGapsText.Text = result.TotalGaps.ToString();
                TotalGapDurationText.Text = FormatDuration(result.TotalGapDuration);
                GapCountText.Text = result.TotalGaps.ToString();

                var repairableCount = result.Gaps.Count(g => g.IsRepairable);
                RepairableGapsText.Text = repairableCount.ToString();
                RepairGapsButton.IsEnabled = repairableCount > 0;

                var items = result.Gaps.Select(g => new GapDisplayItem
                {
                    Symbol = g.Symbol,
                    EventType = g.EventType,
                    TimeRange = $"{g.StartTime:HH:mm} - {g.EndTime:HH:mm}",
                    DurationText = FormatDuration(g.Duration),
                    RepairableText = g.IsRepairable ? "Repairable" : "Manual",
                    RepairableBackground = new SolidColorBrush(
                        g.IsRepairable
                            ? Color.FromArgb(40, 72, 187, 120)
                            : Color.FromArgb(40, 160, 160, 160))
                }).ToList();

                GapsList.ItemsSource = items;
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
            GapAnalysisProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void RepairGaps_Click(object sender, RoutedEventArgs e)
    {
        if (_lastGapAnalysis == null) return;

        var dialogResult = MessageBox.Show(
            "This will attempt to fetch missing data from alternative providers. Continue?",
            "Repair Data Gaps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (dialogResult != MessageBoxResult.Yes) return;

        try
        {
            var result = await _analyticsService.RepairGapsAsync(new GapRepairOptions
            {
                UseAlternativeProviders = true
            });

            if (result.Success)
            {
                ShowSuccess($"Repair complete. {result.GapsRepaired} gaps repaired, {result.RecordsRecovered} records recovered.");
                AnalyzeGaps_Click(sender, e);
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



    private async void CompareProviders_Click(object sender, RoutedEventArgs e)
    {
        var symbol = CompareSymbolBox.Text?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
        {
            ShowError("Validation", "Please enter a symbol to compare.");
            return;
        }

        ComparisonProgress.Visibility = Visibility.Visible;
        try
        {
            var date = CompareDatePicker.SelectedDate ?? DateTime.Today;

            var result = await _analyticsService.CompareProvidersAsync(new CrossProviderComparisonOptions
            {
                Symbol = symbol,
                Date = DateOnly.FromDateTime(date)
            });

            if (result.Success)
            {
                ComparisonResultsCard.Visibility = Visibility.Visible;
                ConsistencyScoreText.Text = $"{result.OverallConsistencyScore:F1}%";
                DiscrepancyCountText.Text = result.Discrepancies.Count.ToString();

                var items = result.Discrepancies.Select(d => new DiscrepancyDisplayItem
                {
                    TimestampText = d.Timestamp.ToString("HH:mm:ss"),
                    DiscrepancyType = d.DiscrepancyType,
                    Values = $"{d.Provider1}: {d.Value1} | {d.Provider2}: {d.Value2}",
                    DifferenceText = $"{d.Difference:F2}%"
                }).ToList();

                DiscrepanciesList.ItemsSource = items;
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
            ComparisonProgress.Visibility = Visibility.Collapsed;
        }
    }



    private async System.Threading.Tasks.Task LoadLatencyDataAsync()
    {
        try
        {
            var result = await _analyticsService.GetLatencyHistogramAsync();

            if (result.Success)
            {
                var maxP99 = result.Providers.Count > 0 ? result.Providers.Max(p => p.P99Ms) : 100;
                if (maxP99 < 1) maxP99 = 100;

                var items = result.Providers.Select(p => new LatencyDisplayItem
                {
                    Provider = p.Provider,
                    P50Text = $"{p.P50Ms:F0}ms",
                    P95Text = $"{p.P95Ms:F0}ms",
                    P99Text = $"{p.P99Ms:F0}ms",
                    LatencyPercent = (p.P50Ms / maxP99) * 100
                }).ToList();

                LatencyList.ItemsSource = items;
            }
        }
        catch
        {
            LatencyList.ItemsSource = new List<LatencyDisplayItem>
            {
                new() { Provider = "Alpaca", P50Text = "--ms", P95Text = "--ms", P99Text = "--ms" },
                new() { Provider = "Polygon", P50Text = "--ms", P95Text = "--ms", P99Text = "--ms" }
            };
        }
    }



    private async System.Threading.Tasks.Task LoadRateLimitsAsync()
    {
        try
        {
            var result = await _analyticsService.GetRateLimitStatusAsync();

            if (result.Success)
            {
                var items = result.Providers.Select(p => new RateLimitDisplayItem
                {
                    Provider = p.Provider,
                    UsagePercent = p.UsagePercent,
                    UsageText = $"{p.RequestsUsed}/{p.RequestsPerMinute}",
                    StatusText = p.IsThrottled ? "Throttled" : (p.UsagePercent > 80 ? "High" : "OK"),
                    UsageColor = new SolidColorBrush(
                        p.IsThrottled
                            ? Color.FromRgb(245, 101, 101)
                            : (p.UsagePercent > 80
                                ? Color.FromRgb(237, 137, 54)
                                : Color.FromRgb(72, 187, 120))),
                    StatusBackground = new SolidColorBrush(
                        p.IsThrottled
                            ? Color.FromArgb(40, 245, 101, 101)
                            : (p.UsagePercent > 80
                                ? Color.FromArgb(40, 237, 137, 54)
                                : Color.FromArgb(40, 72, 187, 120)))
                }).ToList();

                RateLimitsList.ItemsSource = items;
            }
        }
        catch
        {
            RateLimitsList.ItemsSource = new List<RateLimitDisplayItem>
            {
                new() { Provider = "Alpaca", UsageText = "--/--", StatusText = "--", UsagePercent = 0 },
                new() { Provider = "Polygon", UsageText = "--/--", StatusText = "--", UsagePercent = 0 }
            };
        }
    }



    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{(int)duration.TotalMinutes}m";
    }

    private void ShowSuccess(string message)
    {
        StatusInfoBar.Visibility = Visibility.Visible;
        StatusInfoIcon.Text = "\uE73E";
        StatusInfoIcon.Foreground = new SolidColorBrush(Color.FromRgb(72, 187, 120));
        StatusInfoTitle.Text = "Success";
        StatusInfoMessage.Text = message;
    }

    private void ShowError(string title, string message)
    {
        StatusInfoBar.Visibility = Visibility.Visible;
        StatusInfoIcon.Text = "\uEA39";
        StatusInfoIcon.Foreground = new SolidColorBrush(Color.FromRgb(245, 101, 101));
        StatusInfoTitle.Text = title;
        StatusInfoMessage.Text = message;
    }

    private void CloseInfoBar_Click(object sender, RoutedEventArgs e)
    {
        StatusInfoBar.Visibility = Visibility.Collapsed;
    }

}


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

