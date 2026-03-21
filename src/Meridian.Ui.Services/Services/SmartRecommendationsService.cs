using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service that analyzes existing data and provides smart recommendations
/// for backfill operations, gap repairs, and data quality improvements.
/// </summary>
public sealed class SmartRecommendationsService
{
    private static readonly Lazy<SmartRecommendationsService> _instance = new(() => new SmartRecommendationsService());
    private readonly DataCompletenessService _completenessService;
    private readonly StorageAnalyticsService _storageService;
    private readonly ConfigService _configService;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static SmartRecommendationsService Instance => _instance.Value;

    private SmartRecommendationsService()
    {
        var tradingCalendar = new TradingCalendarService();
        var manifestService = ManifestService.Instance;
        _completenessService = new DataCompletenessService(manifestService, tradingCalendar);
        _storageService = StorageAnalyticsService.Instance;
        _configService = new ConfigService();
    }

    /// <summary>
    /// Generates smart recommendations based on current data state.
    /// </summary>
    public async Task<BackfillRecommendations> GetRecommendationsAsync(CancellationToken ct = default)
    {
        var recommendations = new BackfillRecommendations();

        try
        {
            // Load current config
            var config = await _configService.LoadConfigAsync();

            // Analyze existing data coverage
            var analytics = await _storageService.GetAnalyticsAsync();

            // Generate recommendations
            recommendations.QuickActions = await GetQuickActionsAsync(config, analytics, ct);
            recommendations.SuggestedBackfills = await GetSuggestedBackfillsAsync(config, analytics, ct);
            recommendations.DataQualityIssues = await GetDataQualityIssuesAsync(config, ct);
            recommendations.Insights = await GetInsightsAsync(config, analytics, ct);

            recommendations.GeneratedAt = DateTime.UtcNow;
            recommendations.IsStale = false;
        }
        catch (Exception ex)
        {
            recommendations.ErrorMessage = ex.Message;
        }

        return recommendations;
    }

    private async Task<List<QuickAction>> GetQuickActionsAsync(
        AppConfig? config,
        StorageAnalytics? analytics,
        CancellationToken ct)
    {
        var actions = new List<QuickAction>();

        // Check for gaps in recent data
        var gapCount = await GetRecentGapCountAsync(ct);
        if (gapCount > 0)
        {
            actions.Add(new QuickAction
            {
                Id = "fill-recent-gaps",
                Title = $"Fill {gapCount} Recent Gap{(gapCount > 1 ? "s" : "")}",
                Description = "Automatically download missing data from the past 30 days",
                Icon = "\uE90F",
                ActionType = QuickActionType.FillGaps,
                Priority = 1,
                EstimatedTime = TimeSpan.FromMinutes(gapCount * 0.5)
            });
        }

        // Suggest extending date range for subscribed symbols
        if (config?.Symbols != null && config.Symbols.Length > 0)
        {
            var shortCoverageSymbols = await GetSymbolsWithShortCoverageAsync(config.Symbols, ct);
            if (shortCoverageSymbols.Count > 0)
            {
                actions.Add(new QuickAction
                {
                    Id = "extend-coverage",
                    Title = "Extend Historical Coverage",
                    Description = $"{shortCoverageSymbols.Count} symbols have less than 1 year of data",
                    Icon = "\uE823",
                    ActionType = QuickActionType.ExtendCoverage,
                    Priority = 2,
                    AffectedSymbols = shortCoverageSymbols.ToArray(),
                    EstimatedTime = TimeSpan.FromMinutes(shortCoverageSymbols.Count * 2)
                });
            }
        }

        // Suggest backfill for new subscribed symbols
        if (config?.Symbols != null)
        {
            var symbolsWithoutData = await GetSymbolsWithoutDataAsync(config.Symbols, ct);
            if (symbolsWithoutData.Count > 0)
            {
                actions.Add(new QuickAction
                {
                    Id = "backfill-new-symbols",
                    Title = $"Backfill {symbolsWithoutData.Count} New Symbol{(symbolsWithoutData.Count > 1 ? "s" : "")}",
                    Description = "Download 1 year of historical data for newly subscribed symbols",
                    Icon = "\uE787",
                    ActionType = QuickActionType.BackfillNew,
                    Priority = 1,
                    AffectedSymbols = symbolsWithoutData.ToArray(),
                    EstimatedTime = TimeSpan.FromMinutes(symbolsWithoutData.Count * 2)
                });
            }
        }

        // Suggest updating to latest data
        var staleSymbols = await GetStaleSymbolsAsync(ct);
        if (staleSymbols.Count > 0)
        {
            actions.Add(new QuickAction
            {
                Id = "update-to-latest",
                Title = "Update to Latest Data",
                Description = $"{staleSymbols.Count} symbols haven't been updated in 7+ days",
                Icon = "\uE72C",
                ActionType = QuickActionType.UpdateLatest,
                Priority = 3,
                AffectedSymbols = staleSymbols.ToArray(),
                EstimatedTime = TimeSpan.FromMinutes(1)
            });
        }

        return actions.OrderBy(a => a.Priority).ToList();
    }

    private async Task<List<SuggestedBackfill>> GetSuggestedBackfillsAsync(
        AppConfig? config,
        StorageAnalytics? analytics,
        CancellationToken ct)
    {
        var suggestions = new List<SuggestedBackfill>();

        // Popular market indices suggestion
        var popularETFs = new[] { "SPY", "QQQ", "IWM", "DIA", "VTI" };
        var missingETFs = await GetMissingSymbolsAsync(popularETFs, ct);
        if (missingETFs.Count > 0)
        {
            suggestions.Add(new SuggestedBackfill
            {
                Id = "popular-etfs",
                Title = "Major Market ETFs",
                Description = "Essential market benchmarks for analysis",
                Symbols = missingETFs.ToArray(),
                RecommendedDateRange = 365 * 5, // 5 years
                Reason = "These ETFs provide important market context for your analysis",
                Category = "Market Benchmarks"
            });
        }

        // Tech sector suggestion
        var techStocks = new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA" };
        var missingTech = await GetMissingSymbolsAsync(techStocks, ct);
        if (missingTech.Count > 0)
        {
            suggestions.Add(new SuggestedBackfill
            {
                Id = "tech-stocks",
                Title = "Tech Giants",
                Description = "Major technology companies",
                Symbols = missingTech.ToArray(),
                RecommendedDateRange = 365 * 3, // 3 years
                Reason = "High-volume stocks useful for backtesting and analysis",
                Category = "Technology Sector"
            });
        }

        // Sector diversification
        var sectorETFs = new[] { "XLF", "XLK", "XLE", "XLV", "XLI", "XLU", "XLP", "XLY", "XLB", "XLRE" };
        var missingSectors = await GetMissingSymbolsAsync(sectorETFs, ct);
        if (missingSectors.Count > 0)
        {
            suggestions.Add(new SuggestedBackfill
            {
                Id = "sector-etfs",
                Title = "Sector ETFs",
                Description = "Broad sector exposure for diversified analysis",
                Symbols = missingSectors.ToArray(),
                RecommendedDateRange = 365 * 3,
                Reason = "Useful for sector rotation and relative strength analysis",
                Category = "Sector Analysis"
            });
        }

        return suggestions;
    }

    private async Task<List<DataQualityIssue>> GetDataQualityIssuesAsync(
        AppConfig? config,
        CancellationToken ct)
    {
        var issues = new List<DataQualityIssue>();

        try
        {
            // Check for data gaps using completeness service
            var gapCount = await GetRecentGapCountAsync(ct);
            if (gapCount > 0)
            {
                issues.Add(new DataQualityIssue
                {
                    Id = "gaps-detected",
                    Severity = gapCount > 5 ? IssueSeverity.Error : IssueSeverity.Warning,
                    Title = "Data Gaps Detected",
                    Description = $"{gapCount} trading days with missing data in the last 30 days",
                    AffectedCount = gapCount,
                    SuggestedAction = "Run gap repair to fill missing data"
                });
            }

            // Check for stale symbols
            var staleSymbols = await GetStaleSymbolsAsync(ct);
            if (staleSymbols.Count > 0)
            {
                issues.Add(new DataQualityIssue
                {
                    Id = "stale-symbols",
                    Severity = IssueSeverity.Warning,
                    Title = "Stale Data Detected",
                    Description = $"{staleSymbols.Count} symbols haven't been updated recently",
                    AffectedCount = staleSymbols.Count,
                    SuggestedAction = "Update to latest available data"
                });
            }

            // Check completeness for configured symbols
            if (config?.Symbols != null)
            {
                var symbolNames = config.Symbols
                    .Where(s => !string.IsNullOrEmpty(s.Symbol))
                    .Select(s => s.Symbol!)
                    .ToArray();

                var completeness = await _completenessService.GetCompletenessReportAsync(
                    symbolNames,
                    DateOnly.FromDateTime(DateTime.Now.AddDays(-30)),
                    DateOnly.FromDateTime(DateTime.Now),
                    ct);

                if (completeness.OverallScore < 95)
                {
                    issues.Add(new DataQualityIssue
                    {
                        Id = "low-completeness",
                        Severity = completeness.OverallScore < 80 ? IssueSeverity.Error : IssueSeverity.Warning,
                        Title = "Low Data Completeness",
                        Description = $"Overall data completeness is {completeness.OverallScore:F1}%",
                        AffectedCount = (int)(100 - completeness.OverallScore),
                        SuggestedAction = "Run backfill to improve data coverage"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRecommendations] Error getting quality issues: {ex.Message}");
        }

        return issues;
    }

    private Task<List<InsightMessage>> GetInsightsAsync(
        AppConfig? config,
        StorageAnalytics? analytics,
        CancellationToken ct)
    {
        var insights = new List<InsightMessage>();

        try
        {
            // Storage insights
            if (analytics != null)
            {
                var totalGb = analytics.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);
                var totalMb = analytics.TotalSizeBytes / (1024.0 * 1024.0);

                if (totalGb > 100)
                {
                    insights.Add(new InsightMessage
                    {
                        Type = InsightType.Warning,
                        Title = "Large Storage Usage",
                        Message = $"You have {totalGb:F1} GB of data. Consider archiving old data to cold storage."
                    });
                }
                else if (totalGb > 10)
                {
                    insights.Add(new InsightMessage
                    {
                        Type = InsightType.Info,
                        Title = "Storage Usage",
                        Message = $"You have {totalGb:F1} GB of historical data stored locally."
                    });
                }
                else if (totalMb > 100)
                {
                    insights.Add(new InsightMessage
                    {
                        Type = InsightType.Info,
                        Title = "Storage Usage",
                        Message = $"You have {totalMb:F1} MB of historical data stored locally."
                    });
                }

                // File count insight
                if (analytics.TotalFiles > 10000)
                {
                    insights.Add(new InsightMessage
                    {
                        Type = InsightType.Tip,
                        Title = "Many Data Files",
                        Message = $"You have {analytics.TotalFiles:N0} files. Consider merging small files to improve performance."
                    });
                }
            }

            // Coverage insights
            if (config?.Symbols != null && config.Symbols.Length > 0)
            {
                var symbolCount = config.Symbols.Length;
                insights.Add(new InsightMessage
                {
                    Type = InsightType.Success,
                    Title = "Symbols Configured",
                    Message = $"You have {symbolCount} symbol{(symbolCount > 1 ? "s" : "")} configured for data collection."
                });

                // Check data provider
                if (!string.IsNullOrEmpty(config.DataSource))
                {
                    insights.Add(new InsightMessage
                    {
                        Type = InsightType.Info,
                        Title = "Data Provider",
                        Message = $"Currently using {config.DataSource} as your primary data source."
                    });
                }
            }
            else
            {
                insights.Add(new InsightMessage
                {
                    Type = InsightType.Warning,
                    Title = "No Symbols Configured",
                    Message = "Add symbols to start collecting market data."
                });
            }

            // Data freshness insight
            if (analytics?.LastUpdated != null)
            {
                var hoursSinceUpdate = (DateTime.UtcNow - analytics.LastUpdated.Value).TotalHours;
                if (hoursSinceUpdate > 24)
                {
                    insights.Add(new InsightMessage
                    {
                        Type = InsightType.Warning,
                        Title = "Data Not Updated",
                        Message = $"Data hasn't been updated in {hoursSinceUpdate:F0} hours. Ensure collection is running."
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRecommendations] Error generating insights: {ex.Message}");
        }

        return Task.FromResult(insights);
    }

    // Helper methods that query actual data from services

    private async Task<int> GetRecentGapCountAsync(CancellationToken ct)
    {
        try
        {
            var config = await _configService.LoadConfigAsync();
            if (config?.Symbols == null || config.Symbols.Length == 0)
            {
                return 0;
            }

            var symbols = config.Symbols
                .Where(s => !string.IsNullOrEmpty(s.Symbol))
                .Select(s => s.Symbol!)
                .ToArray();

            if (symbols.Length == 0)
            {
                return 0;
            }

            var completeness = await _completenessService.GetCompletenessReportAsync(
                symbols,
                DateOnly.FromDateTime(DateTime.Now.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.Now),
                ct);

            return completeness.Gaps.Count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRecommendations] Error getting gap count: {ex.Message}");
            return 0;
        }
    }

    private async Task<List<string>> GetSymbolsWithShortCoverageAsync(SymbolConfig[] symbols, CancellationToken ct)
    {
        var shortCoverageSymbols = new List<string>();

        try
        {
            var symbolStrings = symbols
                .Where(s => !string.IsNullOrEmpty(s.Symbol))
                .Select(s => s.Symbol!)
                .ToArray();

            foreach (var symbol in symbolStrings.Take(20)) // Limit to avoid long queries
            {
                ct.ThrowIfCancellationRequested();

                var completeness = await _completenessService.GetSymbolCompletenessAsync(
                    symbol,
                    DateOnly.FromDateTime(DateTime.Now.AddYears(-1)),
                    DateOnly.FromDateTime(DateTime.Now),
                    ct);

                // If less than 200 trading days of data (roughly 1 year), consider it short
                if (completeness.TotalEvents < 200 * 390) // 200 days * ~390 minutes per trading day
                {
                    shortCoverageSymbols.Add(symbol);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRecommendations] Error checking coverage: {ex.Message}");
        }

        return shortCoverageSymbols;
    }

    private async Task<List<string>> GetSymbolsWithoutDataAsync(SymbolConfig[] symbols, CancellationToken ct)
    {
        var symbolsWithoutData = new List<string>();

        try
        {
            var config = await _configService.LoadConfigAsync();
            var dataRoot = config?.DataRoot ?? "data";

            foreach (var symbolConfig in symbols.Where(s => !string.IsNullOrEmpty(s.Symbol)).Take(50))
            {
                ct.ThrowIfCancellationRequested();

                var symbol = symbolConfig.Symbol!;

                // Check if any data files exist for this symbol
                var hasData = await _storageService.SymbolHasDataAsync(symbol, dataRoot);
                if (!hasData)
                {
                    symbolsWithoutData.Add(symbol);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRecommendations] Error checking symbols without data: {ex.Message}");
        }

        return symbolsWithoutData;
    }

    private async Task<List<string>> GetStaleSymbolsAsync(CancellationToken ct)
    {
        var staleSymbols = new List<string>();

        try
        {
            var config = await _configService.LoadConfigAsync();
            if (config?.Symbols == null)
            {
                return staleSymbols;
            }

            var dataRoot = config.DataRoot ?? "data";
            var staleThreshold = DateTime.UtcNow.AddDays(-7);

            foreach (var symbolConfig in config.Symbols.Where(s => !string.IsNullOrEmpty(s.Symbol)).Take(50))
            {
                ct.ThrowIfCancellationRequested();

                var symbol = symbolConfig.Symbol!;
                var lastUpdate = await _storageService.GetLastUpdateTimeAsync(symbol, dataRoot);

                if (lastUpdate.HasValue && lastUpdate.Value < staleThreshold)
                {
                    staleSymbols.Add(symbol);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRecommendations] Error checking stale symbols: {ex.Message}");
        }

        return staleSymbols;
    }

    private async Task<List<string>> GetMissingSymbolsAsync(string[] symbols, CancellationToken ct)
    {
        var missingSymbols = new List<string>();

        try
        {
            var config = await _configService.LoadConfigAsync();
            var dataRoot = config?.DataRoot ?? "data";

            foreach (var symbol in symbols)
            {
                ct.ThrowIfCancellationRequested();

                var hasData = await _storageService.SymbolHasDataAsync(symbol, dataRoot);
                if (!hasData)
                {
                    missingSymbols.Add(symbol);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRecommendations] Error checking missing symbols: {ex.Message}");
        }

        return missingSymbols;
    }
}

/// <summary>
/// Container for all backfill recommendations.
/// </summary>
public sealed class BackfillRecommendations
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public bool IsStale { get; set; }
    public string? ErrorMessage { get; set; }

    public List<QuickAction> QuickActions { get; set; } = new();
    public List<SuggestedBackfill> SuggestedBackfills { get; set; } = new();
    public List<DataQualityIssue> DataQualityIssues { get; set; } = new();
    public List<InsightMessage> Insights { get; set; } = new();
}

/// <summary>
/// A quick one-click action recommendation.
/// </summary>
public sealed class QuickAction
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE787";
    public QuickActionType ActionType { get; set; }
    public int Priority { get; set; } = 100;
    public string[]? AffectedSymbols { get; set; }
    public TimeSpan? EstimatedTime { get; set; }
}

/// <summary>
/// Types of quick actions.
/// </summary>
public enum QuickActionType : byte
{
    FillGaps,
    ExtendCoverage,
    BackfillNew,
    UpdateLatest,
    ValidateData,
    Custom
}

/// <summary>
/// A suggested backfill operation.
/// </summary>
public sealed class SuggestedBackfill
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Symbols { get; set; } = Array.Empty<string>();
    public int RecommendedDateRange { get; set; } = 365;
    public string? Reason { get; set; }
    public string? Category { get; set; }
}

/// <summary>
/// A data quality issue.
/// </summary>
public sealed class DataQualityIssue
{
    public string Id { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AffectedCount { get; set; }
    public string? SuggestedAction { get; set; }
}

/// <summary>
/// Issue severity levels.
/// </summary>
public enum IssueSeverity : byte
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// An insight message.
/// </summary>
public sealed class InsightMessage
{
    public InsightType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Insight message types.
/// </summary>
public enum InsightType : byte
{
    Info,
    Success,
    Warning,
    Tip
}
