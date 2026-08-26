namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Identity and provenance for a research-originated backtest, supplied by whichever surface
/// produced it.
/// </summary>
/// <param name="StrategyId">Stable identity of the strategy under test.</param>
/// <param name="StrategyName">Display name for the run listing.</param>
/// <param name="CorrelationId">
/// Pointer back to the artifact that produced the run — for a notebook, the notebook and cell.
/// This is what makes a recorded run traceable to the work that created it.
/// </param>
/// <param name="DatasetReference">Data the run replayed, for reproducibility.</param>
/// <param name="ParameterSet">Parameters the run was configured with.</param>
/// <param name="ExecutionRealism">
/// The execution-realism configuration the run used. Supplying it lets run identity distinguish
/// two runs that differ only in fill timing or cost model; omitting it records the run as having
/// unknown realism rather than silently claiming the defaults.
/// </param>
public sealed record ResearchRunDescriptor(
    string StrategyId,
    string StrategyName,
    string? CorrelationId = null,
    string? DatasetReference = null,
    IReadOnlyDictionary<string, string>? ParameterSet = null,
    ExecutionRealismDescriptor? ExecutionRealism = null);

/// <summary>
/// Records research-originated backtests into the shared strategy-run store so that work done in a
/// scripting surface carries the same lineage as a run launched from the Studio.
/// </summary>
/// <remarks>
/// <para>
/// This contract lives in the SDK so that scripting and research surfaces can hand off a completed
/// run without taking a dependency on the strategy-run storage layer.
/// </para>
/// <para>
/// Implementations must be <b>fail-open for research</b>: a store outage should not destroy a
/// researcher's in-flight work, so recording failures are reported, not thrown. They must remain
/// <b>fail-closed for promotion</b>: a run that was not recorded has no lineage, and nothing may
/// treat it as promotion-eligible on the strength of having executed.
/// </para>
/// </remarks>
public interface IResearchRunRecorder
{
    /// <summary>
    /// Records a completed backtest and returns the assigned run id, or <see langword="null"/> when
    /// the run could not be recorded.
    /// </summary>
    Task<string?> RecordAsync(
        ResearchRunDescriptor descriptor,
        BacktestResult result,
        CancellationToken ct = default);
}
