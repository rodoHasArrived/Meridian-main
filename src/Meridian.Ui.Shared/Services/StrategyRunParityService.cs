using System.Globalization;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;

namespace Meridian.Ui.Shared.Services;

public sealed record StrategyRunParityReport(string RunId, string? BaselineRunId, string Status,
    string InputMode, decimal? BaselineNetPnl, decimal? ObservedNetPnl, int? BaselineFills, int? ObservedFills,
    IReadOnlyList<OrderCheck> Checks, string JournalRoute);

/// <summary>Compares retained operands/results; agreement never grants promotion or live authority.</summary>
public sealed class StrategyRunParityService(IStrategyRepository repository)
{
    public async Task<StrategyRunParityReport?> GetAsync(Guid account, string runId, CancellationToken ct = default)
    {
        var run = await repository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        if (run?.FundAccountId != account)
            return null;
        var baseline = run.ParentRunId is { } parent ? await repository.GetRunByIdAsync(parent, ct).ConfigureAwait(false) : null;
        if (baseline?.FundAccountId != account)
            baseline = null;
        var checks = new List<OrderCheck>();
        var comparable = baseline is not null;
        if (baseline is null)
            checks.Add(new("BASELINE_UNAVAILABLE", "unassessed", "No authorized account-bound parent run is retained for comparison."));
        else
        {
            comparable &= baseline.StrategyId == run.StrategyId && Parameters(baseline) == Parameters(run)
                && !string.IsNullOrWhiteSpace(run.ParameterSet?.GetValueOrDefault("strategyVersion"));
            checks.Add(new("STRATEGY_INPUTS", comparable ? "passed" : "unassessed", "Strategy identity and retained parameter sets must match exactly."));
            var sharedData = !string.IsNullOrWhiteSpace(run.DataProvenanceToken) && run.DataProvenanceToken == baseline.DataProvenanceToken;
            var realism = run.ExecutionRealism is not null && baseline.ExecutionRealism is not null
                && run.ExecutionRealism.ToCanonicalString() == baseline.ExecutionRealism.ToCanonicalString();
            comparable &= sharedData && realism && run.EndedAt.HasValue && baseline.EndedAt.HasValue
                && HasFullEvidence(run.Metrics) && HasFullEvidence(baseline.Metrics)
                && run.Metrics!.Metrics.InitialCapital == baseline.Metrics!.Metrics.InitialCapital
                && run.Metrics.Universe.SetEquals(baseline.Metrics.Universe)
                && run.ParameterSet?.GetValueOrDefault("inputMode") == "bars";
            checks.Add(new("DATA_AND_EXECUTION", sharedData && realism ? "passed" : "unassessed",
                "Controlled parity requires the same retained data provenance and execution assumptions; real broker fills can differ."));
        }
        var matched = comparable && baseline!.Metrics!.Fills.Select(FillKey).SequenceEqual(run.Metrics!.Fills.Select(FillKey))
            && Math.Abs(baseline.Metrics.Metrics.NetPnl - run.Metrics.Metrics.NetPnl) <= 0.01m;
        checks.Add(new("EXECUTION_EVIDENCE", !comparable ? "unassessed" : matched ? "passed" : "warning",
            !comparable ? "Evidence is incomplete or represents different inputs; no parity claim is available."
                : matched ? "Retained normalized fills and net P&L agree within one cent."
                : "Observed fills or net P&L differ. Inspect the run assumptions and journal evidence."));
        return new(runId, baseline?.RunId, !comparable ? "not-comparable" : matched ? "controlled-evidence-agrees" : "observed-differences",
            run.ParameterSet?.GetValueOrDefault("inputMode") ?? "legacy/unspecified",
            baseline?.Metrics?.Metrics.NetPnl, run.Metrics?.Metrics.NetPnl, baseline?.Metrics?.Fills.Count, run.Metrics?.Fills.Count,
            checks, $"/trading/orders?fundAccountId={account:D}&runId={Uri.EscapeDataString(runId)}");
    }
    private static bool HasFullEvidence(BacktestResult? result) => result is not null
        && result.EngineMetadata?.ResultKind != BacktestResultKinds.SummaryOnly
        && !(result.EngineMetadata?.CoverageWarnings?.Count > 0)
        && result.TotalEventsProcessed > 0 && result.Snapshots.Count > 0
        && (result.Metrics.TotalTrades == 0 || result.Fills.Count > 0);
    private static string Parameters(StrategyRunEntry run) => string.Join("\n", (run.ParameterSet ?? new Dictionary<string, string>())
        .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}"));
    private static string FillKey(FillEvent fill) => string.Create(CultureInfo.InvariantCulture,
                $"{fill.Symbol}:{fill.FilledQuantity}:{fill.FillPrice:G29}:{fill.Commission:G29}:{fill.FilledAt:O}");
}
