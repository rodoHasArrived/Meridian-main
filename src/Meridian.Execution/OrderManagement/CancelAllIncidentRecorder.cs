using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Evidence writer for an existing OMS sweep. It cannot dispatch, cancel, release risk, or change
/// order state. Failed retention never stops the sweep; the caller receives an explicit receipt.
/// </summary>
internal sealed class CancelAllIncidentRecorder(
    CancelAllIncident header, ExecutionAuditTrailService? audit, ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CancelAllOrderOutcome> _orders = new(StringComparer.Ordinal);
    private long _sequence;
    private string? _retentionError;
    private bool _checkpointWritesDisabled;

    internal string IncidentId => header.IncidentId;

    internal Task StartAsync() => WriteAsync(new(IncidentId, 0, Snapshot: header));

    internal async Task<CancelAllOrderOutcome?> GetObservationAsync(string key)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return _orders.GetValueOrDefault(key); }
        finally { _gate.Release(); }
    }

    internal async Task ObserveAsync(CancelAllOrderOutcome order)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_orders.TryGetValue(order.ObservationKey, out var previous))
                order = order with
                {
                    InitialStatus = previous.InitialStatus,
                    InitialQuantity = previous.InitialQuantity,
                    InitialFilledQuantity = previous.InitialFilledQuantity,
                    CancellationAttempted = order.CancellationAttempted || previous.CancellationAttempted
                };
            _orders[order.ObservationKey] = order;
            // Every delta repeats only the constant-size header. It can be reopened even if the
            // first append failed, without writing a growing order list once per cancellation.
            await WriteLockedAsync(new(IncidentId, 0, Snapshot: header, Order: order)).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal async Task<KillSwitchSweepResult> CompleteAsync(KillSwitchSweepResult result)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = header with
            {
                CompletedAt = DateTimeOffset.UtcNow,
                EvidenceStatus = "Complete",
                EvidenceError = _retentionError,
                BrokerViewUnavailable = result.BrokerViewUnavailable,
                BrokerViewError = result.BrokerViewError,
                Orders = _orders.Values.OrderBy(order => order.ObservationKey, StringComparer.Ordinal).ToArray()
            };
            var retained = await WriteLockedAsync(new(IncidentId, 0, Snapshot: snapshot), finalAttempt: true).ConfigureAwait(false);
            return result with { IncidentId = IncidentId, EvidenceRetained = retained, EvidenceError = _retentionError };
        }
        finally { _gate.Release(); }
    }

    internal Task InterruptAsync(Exception exception) => WriteAsync(new(IncidentId, 0, Snapshot: header with
    {
        EvidenceStatus = "Interrupted",
        EvidenceError = $"The sweep was interrupted ({exception.GetType().Name}); reconcile retained observations."
    }), finalAttempt: true);

    private async Task WriteAsync(CancelAllIncidentUpdate update, bool finalAttempt = false)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await WriteLockedAsync(update, finalAttempt).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<bool> WriteLockedAsync(CancelAllIncidentUpdate update, bool finalAttempt = false)
    {
        if (_checkpointWritesDisabled && !finalAttempt)
            return false;
        if (audit is null)
        {
            RecordUnavailableStorage("Execution audit storage is unavailable; the incident has not been retained.");
            return false;
        }
        if (!audit.SupportsDurableIncidentEvidence)
        {
            RecordUnavailableStorage("Cancel-all incident evidence requires EveryWrite execution audit durability; the incident has not been retained.");
            return false;
        }
        try
        {
            update = update with { Sequence = ++_sequence };
            var entry = new ExecutionAuditEntry(
                AuditId: $"{IncidentId}:{update.Sequence}", Category: "Order",
                Action: update.Order is not null ? "CancelAllIncidentOrder"
                    : update.Snapshot?.CompletedAt is not null ? "CancelAllIncidentCompleted"
                    : update.Snapshot?.EvidenceStatus == "Interrupted" ? "CancelAllIncidentInterrupted" : "CancelAllIncidentStarted",
                Outcome: update.Order?.Disposition.ToString() ?? update.Snapshot!.EvidenceStatus,
                OccurredAt: update.Order?.ObservedAt ?? update.Snapshot?.CompletedAt ?? header.StartedAt,
                Actor: header.Actor, BrokerName: header.GatewayId, CorrelationId: header.CorrelationId,
                Message: "Cancel-all incident evidence; read the account-scoped incident for order details.",
                CancelAllEvidence: update);
            // A slow audit gate, initialization or filesystem must not hold up an emergency halt.
            // Once a checkpoint fails, keep observations in memory and make only one final retry.
            await audit.RecordCancelAllEvidenceAsync(entry).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _checkpointWritesDisabled = true;
            _retentionError = $"Cancel-all incident evidence could not be retained ({exception.GetType().Name}).";
            try
            {
                logger.LogError(exception, "Cancel-all incident {IncidentId} evidence retention failed; cancellation continues", IncidentId);
            }
            catch (Exception loggingFailure) when (loggingFailure is not OutOfMemoryException)
            {
                // An unavailable logger cannot veto the exposure-reducing operation either.
            }
            return false;
        }
    }

    private void RecordUnavailableStorage(string error)
    {
        _checkpointWritesDisabled = true;
        if (_retentionError == error)
            return;
        _retentionError = error;
        try { logger.LogError("Cancel-all incident {IncidentId}: {EvidenceError}", IncidentId, error); }
        catch (Exception loggingFailure) when (loggingFailure is not OutOfMemoryException) { }
    }

    public void Dispose() => _gate.Dispose();
}
