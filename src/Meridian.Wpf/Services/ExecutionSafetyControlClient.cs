using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Wpf.Services;

/// <summary>What a desk safety command actually did.</summary>
/// <param name="Succeeded">Whether the shared execution service carried the command out.</param>
/// <param name="Message">
/// Operator-facing outcome. On failure this states that nothing happened, because the failure
/// these controls exist to prevent is an operator believing a safety action took effect.
/// </param>
public readonly record struct SafetyCommandOutcome(bool Succeeded, string Message)
{
    public static SafetyCommandOutcome Failed(string commandLabel) =>
        new(false, $"{commandLabel} did NOT take effect: the workstation API did not confirm it. Verify the book at the broker.");
}

/// <summary>
/// The desk safety commands the WPF Trading command bar issues, routed to the same shared
/// execution service the browser workstation calls.
/// </summary>
/// <remarks>
/// This client exists because the WPF lane had no execution-control access at all. Its Pause,
/// Stop, Flatten, Cancel All, and Acknowledge Risk commands each resolved to a pane-layout change
/// and a confirmation message, so an operator could press Cancel All during an incident, read
/// "Cancel-all review opened", and believe the book was being cancelled while nothing had been
/// asked of any broker.
/// </remarks>
public interface IExecutionSafetyControlClient
{
    /// <summary>Cancels the open book through the shared kill-switch sweep.</summary>
    Task<SafetyCommandOutcome> CancelAllOrdersAsync(CancellationToken ct = default);

    /// <summary>
    /// Opens the execution circuit breaker, which halts new submissions and issues the
    /// cancel-all sweep server-side.
    /// </summary>
    Task<SafetyCommandOutcome> HaltTradingAsync(string reason, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ExecutionSafetyControlClient : IExecutionSafetyControlClient
{
    private readonly ApiClientService _apiClient;

    public ExecutionSafetyControlClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    /// <inheritdoc />
    public async Task<SafetyCommandOutcome> CancelAllOrdersAsync(CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<TradingActionResult>(UiApiRoutes.ExecutionOrdersCancelAll, body: null, ct)
            .ConfigureAwait(false);

        var result = response.DataOrLoggedNull("Cancel all orders");
        if (result is null)
        {
            return SafetyCommandOutcome.Failed("Cancel all");
        }

        // The sweep's own verdict, verbatim. It reports Partial or Failed when orders survived and
        // names them, and passing that through unchanged is the whole point: a WPF surface that
        // rewrote it as "cancel-all issued" would reintroduce the claim the sweep exists to refuse.
        return new SafetyCommandOutcome(
            string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase),
            result.Message);
    }

    /// <inheritdoc />
    public async Task<SafetyCommandOutcome> HaltTradingAsync(string reason, CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<ExecutionControlSnapshot>(
                UiApiRoutes.ExecutionControlsCircuitBreaker,
                new UpdateExecutionCircuitBreakerRequest(IsOpen: true, Reason: reason),
                ct)
            .ConfigureAwait(false);

        var snapshot = response.DataOrLoggedNull("Halt trading");
        if (snapshot is null)
        {
            return SafetyCommandOutcome.Failed("Stop");
        }

        // Confirmed against the returned state rather than against the call succeeding: a 200 that
        // did not open the breaker is a halt that did not happen.
        return snapshot.CircuitBreaker?.IsOpen == true
            ? new SafetyCommandOutcome(true, "Trading halted. The circuit breaker is open and the cancel-all sweep has run; check its outcome in the audit trail.")
            : SafetyCommandOutcome.Failed("Stop");
    }
}
