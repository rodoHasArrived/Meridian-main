using Meridian.Contracts.Backtesting;

namespace Meridian.Contracts.Services;

/// <summary>
/// Runs backtest-scoped trust-gate checks before replay begins.
/// </summary>
public interface IBacktestPreflightService
{
    /// <summary>
    /// Executes backtest preflight checks and returns a structured report.
    /// </summary>
    Task<BacktestPreflightReportV2Dto> RunAsync(BacktestPreflightRequestDto request, CancellationToken ct = default);
}
