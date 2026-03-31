using System.Windows.Media;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Desktop-facing workstation service for strategy run browsing and drill-in pages.
/// Mirrors completed backtests into the shared Phase 12 run store and exposes
/// shared read models for browser, detail, portfolio, and ledger surfaces.
/// </summary>
public sealed class StrategyRunWorkspaceService
{
    private static readonly Lazy<StrategyRunWorkspaceService> _fallbackInstance = new(() => new StrategyRunWorkspaceService());
    private static StrategyRunWorkspaceService? _instance;

    private readonly IStrategyRepository _store;
    private readonly StrategyRunReadService _readService;

    public static StrategyRunWorkspaceService Instance => _instance ?? _fallbackInstance.Value;

    public StrategyRunWorkspaceService()
        : this(new StrategyRunStore(), new PortfolioReadService(), new LedgerReadService())
    {
    }

    public StrategyRunWorkspaceService(
        IStrategyRepository store,
        PortfolioReadService portfolioReadService,
        LedgerReadService ledgerReadService)
        : this(store, new StrategyRunReadService(
            store ?? throw new ArgumentNullException(nameof(store)),
            portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService)),
            ledgerReadService ?? throw new ArgumentNullException(nameof(ledgerReadService))))
    {
    }

    public StrategyRunWorkspaceService(
        IStrategyRepository store,
        StrategyRunReadService readService)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
    }

    public static void SetInstance(StrategyRunWorkspaceService service)
    {
        _instance = service ?? throw new ArgumentNullException(nameof(service));
    }

    public string? LastRecordedRunId { get; private set; }

    public event EventHandler<StrategyRunSummary>? RunRecorded;

    public Task<IReadOnlyList<StrategyRunSummary>> GetRunsAsync(string? strategyId = null, CancellationToken ct = default) =>
        _readService.GetRunsAsync(strategyId, ct: ct);

    public Task<StrategyRunDetail?> GetRunDetailAsync(string runId, CancellationToken ct = default) =>
        _readService.GetRunDetailAsync(runId, ct);

    public async Task<PortfolioSummary?> GetPortfolioAsync(string runId, CancellationToken ct = default)
    {
        var detail = await _readService.GetRunDetailAsync(runId, ct).ConfigureAwait(false);
        return detail?.Portfolio;
    }

    public async Task<LedgerSummary?> GetLedgerAsync(string runId, CancellationToken ct = default)
    {
        var detail = await _readService.GetRunDetailAsync(runId, ct).ConfigureAwait(false);
        return detail?.Ledger;
    }

    public Task<RunCashFlowSummary?> GetCashFlowAsync(
        string runId,
        string? currency = null,
        int? bucketDays = null,
        CancellationToken ct = default)
    {
        var projectionService = new CashFlowProjectionService(_store);
        return projectionService.GetAsync(runId, asOf: null, currency, bucketDays, ct);
    }

    public async Task<StrategyRunSummary?> GetLatestRunAsync(CancellationToken ct = default)
    {
        var runs = await _readService.GetRunsAsync(null, ct: ct).ConfigureAwait(false);
        return runs.FirstOrDefault();
    }

    public async Task<TradingWorkspaceSummary> GetTradingSummaryAsync(CancellationToken ct = default)
    {
        var runs = await _readService.GetRunsAsync(null, ct: ct).ConfigureAwait(false);

        var paperRuns = runs.Where(r => r.Mode == StrategyRunMode.Paper).ToList();
        var liveRuns = runs.Where(r => r.Mode == StrategyRunMode.Live).ToList();

        var totalEquity = runs
            .Where(r => r.Mode is StrategyRunMode.Paper or StrategyRunMode.Live)
            .Sum(r => r.FinalEquity ?? 0m);

        var positions = new List<TradingActivePositionItem>();

        foreach (var run in runs.Where(r =>
            r.Mode is StrategyRunMode.Paper or StrategyRunMode.Live &&
            r.Status == StrategyRunStatus.Running))
        {
            var detail = await _readService.GetRunDetailAsync(run.RunId, ct).ConfigureAwait(false);
            if (detail?.Portfolio?.Positions is null) continue;

            foreach (var pos in detail.Portfolio.Positions)
            {
                var unrealizedBrush = pos.UnrealizedPnl >= 0 ? BrushRegistry.Success : BrushRegistry.Error;
                var realizedBrush = pos.RealizedPnl >= 0 ? BrushRegistry.Success : BrushRegistry.Error;
                var modeBrush = run.Mode == StrategyRunMode.Live ? BrushRegistry.Error : BrushRegistry.Info;
                positions.Add(new TradingActivePositionItem
                {
                    Symbol = pos.Symbol,
                    StrategyName = run.StrategyName,
                    QuantityLabel = pos.IsShort
                        ? $"Short {Math.Abs(pos.Quantity):N0}"
                        : $"Long {pos.Quantity:N0}",
                    UnrealizedPnlFormatted = $"{pos.UnrealizedPnl:+#,##0.00;-#,##0.00;0.00}",
                    UnrealizedPnlBrush = unrealizedBrush,
                    RealizedPnlFormatted = $"{pos.RealizedPnl:+#,##0.00;-#,##0.00;0.00}",
                    RealizedPnlBrush = realizedBrush,
                    ModeBadgeBackground = modeBrush,
                    ModeLabel = run.Mode == StrategyRunMode.Live ? "Live" : "Paper"
                });
            }
        }

        return new TradingWorkspaceSummary
        {
            PaperRunCount = paperRuns.Count,
            LiveRunCount = liveRuns.Count,
            TotalEquityFormatted = totalEquity == 0m ? "—" : $"{totalEquity:C0}",
            MaxDrawdownFormatted = "—",
            PositionLimitLabel = "—",
            OrderRateLabel = "—",
            ActivePositions = positions
        };
    }

    public async Task<ResearchWorkspaceSummary> GetResearchSummaryAsync(CancellationToken ct = default)
    {
        var runs = await _readService.GetRunsAsync(null, ct: ct).ConfigureAwait(false);

        var promoted = runs.Count(r =>
            r.Mode is StrategyRunMode.Paper or StrategyRunMode.Live);

        var promotionCandidates = runs
            .Where(r =>
                r.Mode == StrategyRunMode.Backtest &&
                r.Status == StrategyRunStatus.Completed &&
                r.Promotion?.RequiresReview == true)
            .Take(10)
            .ToList();

        var recentRuns = runs
            .Take(10)
            .Select(r => new ResearchRunSummaryItem
            {
                RunId = r.RunId,
                StrategyName = r.StrategyName,
                NetPnlFormatted = r.NetPnl.HasValue
                    ? $"{r.NetPnl.Value:+#,##0.00;-#,##0.00;0.00}"
                    : "—",
                NetPnlBrush = r.NetPnl is null ? BrushRegistry.MutedText
                    : r.NetPnl >= 0 ? BrushRegistry.Success
                    : BrushRegistry.Error,
                TotalReturnFormatted = r.TotalReturn.HasValue
                    ? $"{r.TotalReturn.Value:P2}"
                    : "—",
                StatusBadgeBackground = MapStatusBrush(r.Status),
                StatusLabel = r.Status.ToString()
            })
            .ToList();

        var candidateItems = promotionCandidates
            .Select(r => new ResearchPromotionCandidateItem
            {
                RunId = r.RunId,
                StrategyName = r.StrategyName,
                PromotionReason = r.Promotion?.Reason ?? "Completed backtest",
                NextModeLabel = r.Promotion?.SuggestedNextMode?.ToString() ?? "Paper"
            })
            .ToList();

        return new ResearchWorkspaceSummary
        {
            TotalRuns = runs.Count,
            PromotedCount = promoted,
            PendingReviewCount = promotionCandidates.Count,
            RecentRuns = recentRuns,
            PromotionCandidates = candidateItems
        };
    }

    private static SolidColorBrush MapStatusBrush(StrategyRunStatus status) => status switch
    {
        StrategyRunStatus.Running => BrushRegistry.Info,
        StrategyRunStatus.Completed => BrushRegistry.Success,
        StrategyRunStatus.Failed => BrushRegistry.Error,
        StrategyRunStatus.Cancelled or StrategyRunStatus.Stopped => BrushRegistry.Inactive,
        StrategyRunStatus.Paused => BrushRegistry.Warning,
        _ => BrushRegistry.MutedText
    };

    public async Task<string> RecordBacktestRunAsync(
        BacktestRequest request,
        string strategyName,
        BacktestResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);
        ArgumentNullException.ThrowIfNull(result);

        var strategyId = SlugifyStrategyName(strategyName);
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - result.ElapsedTime;

        var entry = new StrategyRunEntry(
            RunId: Guid.NewGuid().ToString("N"),
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: completedAt,
            Metrics: result,
            DatasetReference: request.DataRoot,
            FeedReference: BuildFeedReference(request),
            PortfolioId: $"{strategyId}-backtest-portfolio",
            LedgerReference: $"{strategyId}-backtest-ledger",
            AuditReference: $"audit-{strategyId}-{completedAt:yyyyMMddHHmmss}",
            Engine: "MeridianNative",
            ParameterSet: BuildParameterSet(request, result));

        await _store.RecordRunAsync(entry, ct).ConfigureAwait(false);

        LastRecordedRunId = entry.RunId;

        var summary = await _readService.GetRunDetailAsync(entry.RunId, ct).ConfigureAwait(false);
        if (summary is not null)
        {
            RunRecorded?.Invoke(this, summary.Summary);
        }

        return entry.RunId;
    }

    private static string BuildFeedReference(BacktestRequest request)
    {
        if (request.Symbols is { Count: > 0 })
        {
            return $"Local archive ({request.Symbols.Count} symbols)";
        }

        return "Local archive";
    }

    private static IReadOnlyDictionary<string, string> BuildParameterSet(BacktestRequest request, BacktestResult result)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["from"] = request.From.ToString("yyyy-MM-dd"),
            ["to"] = request.To.ToString("yyyy-MM-dd"),
            ["dataRoot"] = request.DataRoot,
            ["initialCash"] = request.InitialCash.ToString("0.##"),
            ["annualMarginRate"] = request.AnnualMarginRate.ToString("0.####"),
            ["annualShortRebateRate"] = request.AnnualShortRebateRate.ToString("0.####"),
            ["engineMode"] = request.EngineMode.ToString(),
            ["eventsProcessed"] = result.TotalEventsProcessed.ToString()
        };

        if (request.Symbols is { Count: > 0 })
        {
            parameters["symbols"] = string.Join(", ", request.Symbols);
        }
        else
        {
            parameters["symbols"] = "Universe discovery";
        }

        if (!string.IsNullOrWhiteSpace(request.StrategyAssemblyPath))
        {
            parameters["strategyAssemblyPath"] = request.StrategyAssemblyPath;
        }

        return parameters;
    }

    private static string SlugifyStrategyName(string strategyName)
    {
        var buffer = new List<char>(strategyName.Length);
        var lastWasDash = false;

        foreach (var character in strategyName.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Add(character);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash)
            {
                buffer.Add('-');
                lastWasDash = true;
            }
        }

        var slug = new string(buffer.ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "strategy" : slug;
    }
}
