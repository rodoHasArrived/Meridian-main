using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Shared comparison service for run compare/diff endpoint flows.
/// </summary>
public sealed class StrategyRunComparisonService
{
    private readonly StrategyRunReadService _readService;

    public StrategyRunComparisonService(StrategyRunReadService readService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
    }

    public async Task<IReadOnlyList<StrategyRunComparison>> CompareRunsAsync(
        RunComparisonRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var comparison = await _readService.CompareRunsAsync(request.RunIds, ct).ConfigureAwait(false);
        if (comparison.Count == 0)
            return comparison;

        var engineSet = comparison.Select(static row => row.Engine).Distinct().ToArray();
        var hasMixedEngines = engineSet.Length > 1;

        return comparison
            .Select(row =>
            {
                var warnings = new List<string>(row.CompatibilityWarnings ?? Array.Empty<string>());
                if (hasMixedEngines)
                {
                    warnings.Add(
                        $"Cross-engine comparison active ({string.Join(", ", engineSet)}). Validate artifact parity before promoting decisions.");
                }

                return row with
                {
                    CompatibilityWarnings = warnings
                };
            })
            .ToArray();
    }

    public async Task<StrategyRunDiff?> BuildDiffAsync(
        RunDiffRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseDetail = await _readService.GetRunDetailAsync(request.BaseRunId, ct).ConfigureAwait(false);
        var targetDetail = await _readService.GetRunDetailAsync(request.TargetRunId, ct).ConfigureAwait(false);
        if (baseDetail is null || targetDetail is null)
            return null;

        var basePositions = baseDetail.Portfolio?.Positions ?? [];
        var targetPositions = targetDetail.Portfolio?.Positions ?? [];

        var baseSymbols = new HashSet<string>(basePositions.Select(static p => p.Symbol), StringComparer.OrdinalIgnoreCase);
        var targetSymbols = new HashSet<string>(targetPositions.Select(static p => p.Symbol), StringComparer.OrdinalIgnoreCase);

        var added = targetPositions
            .Where(p => !baseSymbols.Contains(p.Symbol))
            .Select(static p => new PositionDiffEntry(p.Symbol, 0, p.Quantity, 0m, p.RealizedPnl + p.UnrealizedPnl, "Added"))
            .ToArray();

        var removed = basePositions
            .Where(p => !targetSymbols.Contains(p.Symbol))
            .Select(static p => new PositionDiffEntry(p.Symbol, p.Quantity, 0, p.RealizedPnl + p.UnrealizedPnl, 0m, "Removed"))
            .ToArray();

        var modified = basePositions
            .Where(p => targetSymbols.Contains(p.Symbol))
            .Select(basePosition => (basePosition, targetPosition: targetPositions.First(target =>
                string.Equals(target.Symbol, basePosition.Symbol, StringComparison.OrdinalIgnoreCase))))
            .Where(pair =>
                pair.basePosition.Quantity != pair.targetPosition.Quantity ||
                pair.basePosition.AverageCostBasis != pair.targetPosition.AverageCostBasis)
            .Select(static pair => new PositionDiffEntry(
                pair.basePosition.Symbol,
                pair.basePosition.Quantity,
                pair.targetPosition.Quantity,
                pair.basePosition.RealizedPnl + pair.basePosition.UnrealizedPnl,
                pair.targetPosition.RealizedPnl + pair.targetPosition.UnrealizedPnl,
                "Modified"))
            .ToArray();

        var parameterChanges = BuildParameterDiff(baseDetail.Parameters, targetDetail.Parameters);
        var metrics = new MetricsDiff(
            NetPnlDelta: (targetDetail.Summary.NetPnl ?? 0m) - (baseDetail.Summary.NetPnl ?? 0m),
            TotalReturnDelta: (targetDetail.Summary.TotalReturn ?? 0m) - (baseDetail.Summary.TotalReturn ?? 0m),
            FillCountDelta: targetDetail.Summary.FillCount - baseDetail.Summary.FillCount,
            BaseNetPnl: baseDetail.Summary.NetPnl,
            TargetNetPnl: targetDetail.Summary.NetPnl,
            BaseTotalReturn: baseDetail.Summary.TotalReturn,
            TargetTotalReturn: targetDetail.Summary.TotalReturn);

        var baseCompleteness = BuildArtifactCompleteness(baseDetail.Summary, baseDetail);
        var targetCompleteness = BuildArtifactCompleteness(targetDetail.Summary, targetDetail);
        var compatibilityWarnings = BuildDiffWarnings(baseDetail, targetDetail, baseCompleteness, targetCompleteness);

        return new StrategyRunDiff(
            BaseRunId: baseDetail.Summary.RunId,
            TargetRunId: targetDetail.Summary.RunId,
            BaseStrategyName: baseDetail.Summary.StrategyName,
            TargetStrategyName: targetDetail.Summary.StrategyName,
            AddedPositions: added,
            RemovedPositions: removed,
            ModifiedPositions: modified,
            ParameterChanges: parameterChanges,
            Metrics: metrics,
            CompatibilityWarnings: compatibilityWarnings,
            BaseArtifactCompleteness: baseCompleteness,
            TargetArtifactCompleteness: targetCompleteness);
    }

    private static IReadOnlyList<ParameterDiff> BuildParameterDiff(
        IReadOnlyDictionary<string, string> baseParams,
        IReadOnlyDictionary<string, string> targetParams)
    {
        var diffs = new List<ParameterDiff>();
        var allKeys = new HashSet<string>(baseParams.Keys.Concat(targetParams.Keys), StringComparer.Ordinal);
        foreach (var key in allKeys.Order())
        {
            baseParams.TryGetValue(key, out var baseValue);
            targetParams.TryGetValue(key, out var targetValue);
            if (!string.Equals(baseValue, targetValue, StringComparison.Ordinal))
                diffs.Add(new ParameterDiff(key, baseValue, targetValue));
        }

        return diffs;
    }

    private static StrategyRunArtifactCompleteness BuildArtifactCompleteness(StrategyRunSummary summary, StrategyRunDetail detail)
        => new(
            HasPortfolio: detail.Portfolio is not null,
            HasLedger: detail.Ledger is not null,
            HasCashFlow: detail.Execution?.FillCount > 0 || detail.Summary.FillCount > 0,
            HasFills: detail.Execution?.FillCount > 0 || detail.Summary.FillCount > 0,
            HasAuditTrail: !string.IsNullOrWhiteSpace(summary.AuditReference));

    private static IReadOnlyList<string> BuildDiffWarnings(
        StrategyRunDetail baseRun,
        StrategyRunDetail targetRun,
        StrategyRunArtifactCompleteness baseCompleteness,
        StrategyRunArtifactCompleteness targetCompleteness)
    {
        var warnings = new List<string>();
        if (baseRun.Summary.Engine != targetRun.Summary.Engine)
        {
            warnings.Add(
                $"Comparing different engines ({baseRun.Summary.Engine} vs {targetRun.Summary.Engine}); metric deltas may include model-driven variance.");
        }

        if (!baseCompleteness.HasFills || !targetCompleteness.HasFills)
            warnings.Add("Fill-level evidence is incomplete for at least one run.");

        if (!baseCompleteness.HasLedger || !targetCompleteness.HasLedger)
            warnings.Add("Ledger evidence is incomplete for at least one run.");

        return warnings;
    }
}
