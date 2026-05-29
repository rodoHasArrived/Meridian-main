using System.Text.Json;
using Meridian.Ledger;

namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Normalizes native and external engine outputs into Meridian's canonical <see cref="BacktestResult"/> shape.
/// </summary>
public static class CanonicalBacktestResultNormalizer
{
    private static readonly IReadOnlyList<string> LeanSummaryOnlyWarnings =
    [
        "Lean result import contains summary metrics only; fill, cash-flow, attribution, and ledger comparisons are limited."
    ];

    public static BacktestResult FromNative(BacktestResult result, string engineId = "MeridianNative")
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);

        return result with
        {
            EngineMetadata = new BacktestEngineMetadata(
                EngineId: engineId,
                ExternalRunId: result.EngineMetadata?.ExternalRunId,
                ResultKind: BacktestResultKinds.Full,
                CoverageWarnings: result.EngineMetadata?.CoverageWarnings)
        };
    }

    public static BacktestResult FromLeanResult(
        JsonElement root,
        string backtestId,
        string algorithmName,
        DateTimeOffset? ingestedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backtestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmName);

        var fallbackTime = ingestedAt ?? DateTimeOffset.UtcNow;
        var startedAt = fallbackTime;
        var endedAt = fallbackTime;
        if (root.TryGetProperty("Period", out var period))
        {
            if (period.TryGetProperty("Start", out var startElement) &&
                DateTimeOffset.TryParse(startElement.GetString(), out var parsedStart))
            {
                startedAt = parsedStart;
            }

            if (period.TryGetProperty("End", out var endElement) &&
                DateTimeOffset.TryParse(endElement.GetString(), out var parsedEnd))
            {
                endedAt = parsedEnd;
            }
        }

        var totalReturn = 0m;
        var sharpeRatio = 0d;
        var totalTrades = 0;
        if (root.TryGetProperty("Statistics", out var stats))
        {
            totalReturn = ParsePercentageStatistic(stats, "Total Return");
            sharpeRatio = ParseDoubleStatistic(stats, "Sharpe Ratio");
            totalTrades = ParseIntStatistic(stats, "Total Trades");
        }

        var initialCapital = ParseDecimalPath(root, "Portfolio", "StartingCapital") ?? 100000m;
        var finalEquity = initialCapital * (1m + totalReturn);
        var netPnl = finalEquity - initialCapital;

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(endedAt.UtcDateTime),
            Symbols: null,
            InitialCash: initialCapital);

        var metrics = new BacktestMetrics(
            InitialCapital: initialCapital,
            FinalEquity: finalEquity,
            GrossPnl: netPnl,
            NetPnl: netPnl,
            TotalReturn: totalReturn,
            AnnualizedReturn: totalReturn,
            SharpeRatio: sharpeRatio,
            SortinoRatio: 0d,
            CalmarRatio: 0d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 0d,
            WinRate: 0d,
            TotalTrades: totalTrades,
            WinningTrades: 0,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>(StringComparer.OrdinalIgnoreCase));

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Snapshots: Array.Empty<PortfolioSnapshot>(),
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills: Array.Empty<FillEvent>(),
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: endedAt >= startedAt ? endedAt - startedAt : TimeSpan.Zero,
            TotalEventsProcessed: totalTrades,
            EngineMetadata: new BacktestEngineMetadata(
                EngineId: "Lean",
                ExternalRunId: backtestId,
                ResultKind: BacktestResultKinds.SummaryOnly,
                CoverageWarnings: LeanSummaryOnlyWarnings));
    }

    private static decimal ParsePercentageStatistic(JsonElement stats, string key)
    {
        if (!stats.TryGetProperty(key, out var element))
            return 0m;

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;

        var trimmed = raw.Trim().TrimEnd('%');
        return decimal.TryParse(trimmed, out var value)
            ? value / 100m
            : 0m;
    }

    private static double ParseDoubleStatistic(JsonElement stats, string key)
    {
        if (!stats.TryGetProperty(key, out var element))
            return 0d;

        var raw = element.GetString();
        return double.TryParse(raw, out var value) ? value : 0d;
    }

    private static int ParseIntStatistic(JsonElement stats, string key)
    {
        if (!stats.TryGetProperty(key, out var element))
            return 0;

        var raw = element.GetString();
        return int.TryParse(raw, out var value) ? value : 0;
    }

    private static decimal? ParseDecimalPath(JsonElement root, string parent, string field)
    {
        if (!root.TryGetProperty(parent, out var parentElement))
            return null;

        if (!parentElement.TryGetProperty(field, out var fieldElement))
            return null;

        if (fieldElement.ValueKind == JsonValueKind.Number && fieldElement.TryGetDecimal(out var direct))
            return direct;

        return decimal.TryParse(fieldElement.GetString(), out var parsed)
            ? parsed
            : null;
    }
}
