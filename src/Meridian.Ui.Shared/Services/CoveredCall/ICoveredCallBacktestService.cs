using Meridian.Ui.Shared.Contracts;

namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Orchestrates covered-call backtests for the browser workstation:
/// starts runs asynchronously, reports progress, persists completed runs
/// through <c>IStrategyRepository</c>, and projects results into UI DTOs.
/// </summary>
public interface ICoveredCallBacktestService
{
    /// <summary>
    /// Legacy unscoped entry point retained for source compatibility. Unscoped execution is no
    /// longer supported because it cannot establish tenant and company ownership.
    /// </summary>
    [Obsolete("Use StartAsync(request, scope, ct); an authenticated tenant/company scope is required.")]
    ValueTask<CoveredCallRunHandle> StartAsync(
        CoveredCallBacktestRequest request,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    /// <summary>Starts a new backtest run. Returns immediately with the assigned <c>RunId</c>.</summary>
    ValueTask<CoveredCallRunHandle> StartAsync(
        CoveredCallBacktestRequest request,
        CoveredCallRunScope scope,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    /// <summary>Legacy unscoped status lookup retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use GetStatusAsync(runId, scope, ct); an authenticated tenant/company scope is required.")]
    ValueTask<CoveredCallRunStatusDto?> GetStatusAsync(
        string runId,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    /// <summary>Returns the current status of the run, or <c>null</c> if unknown.</summary>
    ValueTask<CoveredCallRunStatusDto?> GetStatusAsync(
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    /// <summary>Legacy unscoped result lookup retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use GetResultAsync(runId, scope, ct); an authenticated tenant/company scope is required.")]
    ValueTask<CoveredCallRunResult?> GetResultAsync(
        string runId,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    /// <summary>Returns the completed result, or <c>null</c> if the run is not yet finished or unknown.</summary>
    ValueTask<CoveredCallRunResult?> GetResultAsync(
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    /// <summary>Legacy unscoped run listing retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use ListRunsAsync(scope, limit, ct); an authenticated tenant/company scope is required.")]
    ValueTask<IReadOnlyList<CoveredCallRunSummary>> ListRunsAsync(
        int limit = 50,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    /// <summary>Lists prior covered-call runs (most recent first).</summary>
    ValueTask<IReadOnlyList<CoveredCallRunSummary>> ListRunsAsync(
        CoveredCallRunScope scope,
        int limit = 50,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    /// <summary>Legacy unscoped cancellation retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use CancelAsync(runId, scope, ct); an authenticated tenant/company scope is required.")]
    ValueTask CancelAsync(
        string runId,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    /// <summary>Cancels a queued or running backtest. Idempotent.</summary>
    ValueTask CancelAsync(
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    /// <summary>Returns the chain snapshot that would be evaluated for <c>scanDate</c> — for the configure-form preview.</summary>
    ValueTask<CoveredCallChainPreview> PreviewChainAsync(CoveredCallChainPreviewRequest request, CancellationToken ct = default);

    private static NotSupportedException UnscopedAccessNotSupported() =>
        new("Covered-call run access requires an authenticated tenant and company scope. " +
            "Migrate to the overload that accepts CoveredCallRunScope.");

    private static NotSupportedException ScopedImplementationRequired() =>
        new("This covered-call service implementation must implement the tenant/company-scoped contract.");
}
