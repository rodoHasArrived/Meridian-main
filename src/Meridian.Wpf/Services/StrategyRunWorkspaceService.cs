using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;

namespace Meridian.Wpf.Services;

/// <summary>
/// Desktop-facing workstation service for strategy run browsing and drill-in pages.
/// Mirrors completed backtests into the shared Phase 12 run store and exposes
/// shared read models for browser, detail, portfolio, and ledger surfaces.
/// </summary>
public sealed class StrategyRunWorkspaceService
{
    private static readonly Lazy<StrategyRunWorkspaceService> _instance = new(() => new StrategyRunWorkspaceService());

    private readonly StrategyRunStore _store = new();
    private readonly PortfolioReadService _portfolioReadService = new();
    private readonly LedgerReadService _ledgerReadService = new();
    private readonly StrategyRunReadService _readService;

    public static StrategyRunWorkspaceService Instance => _instance.Value;

    private StrategyRunWorkspaceService()
    {
        _readService = new StrategyRunReadService(_store, _portfolioReadService, _ledgerReadService);
    }

    public string? LastRecordedRunId { get; private set; }

    public event EventHandler<StrategyRunSummary>? RunRecorded;

    public Task<IReadOnlyList<StrategyRunSummary>> GetRunsAsync(string? strategyId = null, CancellationToken ct = default) =>
        _readService.GetRunsAsync(strategyId, ct);

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

    public async Task<StrategyRunSummary?> GetLatestRunAsync(CancellationToken ct = default)
    {
        var runs = await _readService.GetRunsAsync(null, ct).ConfigureAwait(false);
        return runs.FirstOrDefault();
    }

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
