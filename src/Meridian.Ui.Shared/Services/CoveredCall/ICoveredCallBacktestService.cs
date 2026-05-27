using Meridian.Ui.Shared.Contracts;

namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Orchestrates covered-call backtests for the browser workstation:
/// starts runs asynchronously, reports progress, persists completed runs
/// through <c>IStrategyRepository</c>, and projects results into UI DTOs.
/// </summary>
public interface ICoveredCallBacktestService
{
    /// <summary>Starts a new backtest run. Returns immediately with the assigned <c>RunId</c>.</summary>
    ValueTask<CoveredCallRunHandle> StartAsync(CoveredCallBacktestRequest request, CancellationToken ct = default);

    /// <summary>Returns the current status of the run, or <c>null</c> if unknown.</summary>
    ValueTask<CoveredCallRunStatusDto?> GetStatusAsync(string runId, CancellationToken ct = default);

    /// <summary>Returns the completed result, or <c>null</c> if the run is not yet finished or unknown.</summary>
    ValueTask<CoveredCallRunResult?> GetResultAsync(string runId, CancellationToken ct = default);

    /// <summary>Lists prior covered-call runs (most recent first).</summary>
    ValueTask<IReadOnlyList<CoveredCallRunSummary>> ListRunsAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>Cancels a queued or running backtest. Idempotent.</summary>
    ValueTask CancelAsync(string runId, CancellationToken ct = default);

    /// <summary>Returns the chain snapshot that would be evaluated for <c>scanDate</c> — for the configure-form preview.</summary>
    ValueTask<CoveredCallChainPreview> PreviewChainAsync(CoveredCallChainPreviewRequest request, CancellationToken ct = default);
}
