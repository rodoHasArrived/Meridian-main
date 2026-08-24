using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk;

/// <summary>
/// The production <see cref="ICircuitBreakerTripHandler"/>: sweeps the open order book through
/// the order manager's kill-switch cancel-all when an automated critical breach trips the
/// execution circuit breaker, and records the sweep's outcome on the execution audit trail —
/// the same sweep, and the same <c>controls/CircuitBreakerCancelAll</c> evidence, the operator
/// circuit-breaker endpoint produces. Outcome is what is audited, not invocation: a broker that
/// refuses cancellations is recorded as Partial/Failed, never absorbed as success.
/// </summary>
public sealed class KillSwitchSweepTripHandler : ICircuitBreakerTripHandler
{
    // Accessor rather than a constructed dependency: the OMS depends on the risk validator for
    // pre-trade checks, and the validator holds this handler, so resolving the order manager
    // eagerly would close a DI cycle. The OMS is resolved only when a trip actually fires.
    private readonly Func<IOrderManager?> _orderManagerAccessor;
    private readonly ILogger<KillSwitchSweepTripHandler> _logger;
    private readonly ExecutionAuditTrailService? _auditTrail;

    public KillSwitchSweepTripHandler(
        Func<IOrderManager?> orderManagerAccessor,
        ILogger<KillSwitchSweepTripHandler> logger,
        ExecutionAuditTrailService? auditTrail = null)
    {
        _orderManagerAccessor = orderManagerAccessor ?? throw new ArgumentNullException(nameof(orderManagerAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditTrail = auditTrail;
    }

    /// <inheritdoc />
    public async Task OnCircuitBreakerTrippedAsync(string reason, string trippedBy, CancellationToken ct)
    {
        var orderManager = _orderManagerAccessor();
        if (orderManager is null)
        {
            // Mirrors the operator endpoint, which only sweeps when an order manager is
            // composed: no OMS means there is no tracked book to empty.
            _logger.LogWarning(
                "Circuit breaker tripped by {TrippedBy} but no order manager is composed; there is no open book to sweep",
                trippedBy);
            return;
        }

        var openCount = orderManager.GetOpenOrders().Count;
        try
        {
            // A null sweep is an order manager that established nothing about the book. Fail
            // closed on it rather than dereferencing, exactly as the operator endpoint does.
            var sweep = await orderManager.CancelAllAsync(ct).ConfigureAwait(false)
                ?? KillSwitchSweepResult.Unestablished(openCount);

            // Outcome, not invocation: the catch below fires only on a thrown exception, so a
            // broker that merely refuses a cancellation still lands in this branch and is
            // reported by what survived.
            if (sweep.RequiresOperatorAction)
            {
                _logger.LogError(
                    "Circuit breaker tripped by {TrippedBy} but the cancel-all sweep left {StillWorking} order(s) working; manual cancellation is required",
                    trippedBy,
                    sweep.StillWorking.Count);
            }
            else
            {
                _logger.LogInformation(
                    "Circuit breaker tripped by {TrippedBy}; cancel-all emptied the book of {Count} open order(s)",
                    trippedBy,
                    sweep.Requested);
            }

            if (_auditTrail is not null)
            {
                await _auditTrail.RecordAsync(
                        "controls",
                        "CircuitBreakerCancelAll",
                        sweep.Outcome.ToString(),
                        actor: trippedBy,
                        message: sweep.Describe(),
                        reason: reason,
                        ct: CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Circuit breaker tripped by {TrippedBy} but the cancel-all sweep failed; open orders may remain working",
                trippedBy);
            if (_auditTrail is not null)
            {
                await _auditTrail.RecordAsync(
                        "controls",
                        "CircuitBreakerCancelAll",
                        "Failed",
                        actor: trippedBy,
                        message: $"Kill-switch cancel-all failed with {openCount} open order(s); manual cancellation is required.",
                        reason: exception.Message,
                        ct: CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }
}
