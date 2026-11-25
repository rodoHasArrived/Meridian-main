using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;

namespace Meridian.Execution.Services;

/// <summary>A bounded delta, or one full initial/final snapshot, in the existing execution audit WAL.</summary>
public sealed record CancelAllIncidentUpdate(
    string IncidentId,
    long Sequence,
    CancelAllIncident? Snapshot = null,
    CancelAllOrderOutcome? Order = null);

public sealed partial class ExecutionAuditTrailService
{
    /// <summary>Audit delay cannot indefinitely hold an exposure-reducing sweep.</summary>
    internal async Task RecordCancelAllEvidenceAsync(ExecutionAuditEntry entry)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var token = deadline.Token;
        // Offload even a synchronously blocked filesystem call; cancellation abandons the receipt,
        // never claims the write succeeded, and cancels a waiter that has not entered the WAL.
        await Task.Run(() => RecordAsync(entry, token), token).WaitAsync(token).ConfigureAwait(false);
    }

    /// <summary>Reopens retained evidence for one account. Reading never contacts or acts on the broker.</summary>
    public async Task<CancelAllIncident?> GetCancelAllIncidentAsync(
        string incidentId, Guid fundAccountId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        if (fundAccountId == Guid.Empty)
            throw new ArgumentException("An execution account is required.", nameof(fundAccountId));
        var incidents = await ReadCancelAllIncidentsAsync(incidentId, ct).ConfigureAwait(false);
        return incidents.SingleOrDefault() is { } incident ? ProjectIncident(incident, fundAccountId) : null;
    }

    /// <summary>Reads recent account-attributed incidents from durable history, beyond audit display retention.</summary>
    public async Task<IReadOnlyList<CancelAllIncident>> GetCancelAllIncidentsAsync(
        Guid fundAccountId, int take = 50, CancellationToken ct = default)
    {
        if (fundAccountId == Guid.Empty)
            throw new ArgumentException("An execution account is required.", nameof(fundAccountId));
        var incidents = await ReadCancelAllIncidentsAsync(null, ct).ConfigureAwait(false);
        return incidents.Select(incident => ProjectIncident(incident, fundAccountId))
            .OfType<CancelAllIncident>().OrderByDescending(incident => incident.StartedAt)
            .ThenBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .Take(Math.Clamp(take, 1, 500)).ToArray();
    }

    private static CancelAllIncident? ProjectIncident(CancelAllIncident incident, Guid accountId)
    {
        var orders = incident.Orders.Where(order => order.FundAccountId == accountId).ToArray();
        if (orders.Length == 0 && incident.FundAccountId != accountId)
            return null;
        return incident with { Orders = orders, FundAccountId = accountId, IsAccountProjection = true };
    }

    private async Task<IReadOnlyList<CancelAllIncident>> ReadCancelAllIncidentsAsync(string? incidentId, CancellationToken ct)
    {
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);
        await _persistenceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _wal.FlushAsync(ct).ConfigureAwait(false);
            if (_wal.CorruptedRecordCount != 0)
                throw new InvalidDataException("Execution audit corruption prevents complete incident readback.");
            var replays = new Dictionary<string, IncidentReplay>(StringComparer.Ordinal);
            await foreach (var record in _wal.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
            {
                if (record.RecordType != AuditRecordType)
                    continue;
                var payload = record.DeserializePayload<string>();
                if (string.IsNullOrWhiteSpace(payload))
                    continue;
                // A corrupt record must not turn an incomplete incident into a complete one.
                var entry = JsonSerializer.Deserialize(payload, ExecutionJsonContext.Default.ExecutionAuditEntry)
                    ?? throw new InvalidDataException("Execution audit contains an empty record.");
                if (entry.CancelAllEvidence is not { } update || incidentId is not null && update.IncidentId != incidentId)
                    continue;
                if (!replays.TryGetValue(update.IncidentId, out var replay))
                    replays[update.IncidentId] = replay = new IncidentReplay();
                replay.Apply(update);
            }
            if (_wal.CorruptedRecordCount != 0)
                throw new InvalidDataException("Execution audit corruption prevents complete incident readback.");
            return replays.Values.Select(replay => replay.Read()).ToArray();
        }
        finally { _persistenceGate.Release(); }
    }

    private sealed class IncidentReplay
    {
        private CancelAllIncident? _snapshot;
        private readonly Dictionary<string, CancelAllOrderOutcome> _orders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string OrderId, Guid? AccountId)> _identities = new(StringComparer.Ordinal);
        private readonly Dictionary<long, string> _seen = [];
        private long _lastSequence;

        public void Apply(CancelAllIncidentUpdate update)
        {
            if (update.Sequence <= 0 || string.IsNullOrWhiteSpace(update.IncidentId))
                throw new InvalidDataException("Invalid cancel-all incident identity.");
            var content = JsonSerializer.Serialize(update, ExecutionJsonContext.Default.CancelAllIncidentUpdate);
            if (_seen.TryGetValue(update.Sequence, out var previous))
            {
                if (previous != content)
                    throw new InvalidDataException("Conflicting cancel-all incident event.");
                return;
            }
            if (update.Sequence <= _lastSequence || _snapshot?.CompletedAt is not null)
                throw new InvalidDataException("Cancel-all incident event is out of order.");
            _seen[update.Sequence] = content;
            _lastSequence = update.Sequence;
            if (update.Snapshot is { } snapshot)
            {
                if (snapshot.Version != 1 || snapshot.IncidentId != update.IncidentId || snapshot.IsAccountProjection
                    || _snapshot is { } original && (snapshot.StartedAt != original.StartedAt
                        || snapshot.GatewayId != original.GatewayId || snapshot.BrokerScopeIdentity != original.BrokerScopeIdentity
                        || snapshot.ExecutionMode != original.ExecutionMode || snapshot.FundAccountId != original.FundAccountId
                        || snapshot.TenantId != original.TenantId || snapshot.CompanyId != original.CompanyId
                        || snapshot.Actor != original.Actor || snapshot.CorrelationId != original.CorrelationId
                        || snapshot.Authority != original.Authority))
                    throw new InvalidDataException("Cancel-all incident scope changed in durable history.");
                _snapshot = snapshot;
                if (snapshot.CompletedAt is not null)
                {
                    var completedKeys = snapshot.Orders.Select(order => order.ObservationKey).ToHashSet(StringComparer.Ordinal);
                    if (_orders.Keys.Any(key => !completedKeys.Contains(key)))
                        throw new InvalidDataException("Completed cancel-all evidence dropped an observed order.");
                    _orders.Clear();
                }
                foreach (var order in snapshot.Orders)
                {
                    ValidateOrderIdentity(order);
                    if (string.IsNullOrWhiteSpace(order.ObservationKey) || !_orders.TryAdd(order.ObservationKey, order))
                        throw new InvalidDataException("Cancel-all incident has duplicate or missing order identities.");
                }
            }
            if (update.Order is { } observed)
            {
                ValidateOrderIdentity(observed);
                _orders[observed.ObservationKey] = observed;
            }
        }

        private void ValidateOrderIdentity(CancelAllOrderOutcome order)
        {
            if (string.IsNullOrWhiteSpace(order.ObservationKey) || string.IsNullOrWhiteSpace(order.OrderId))
                throw new InvalidDataException("Cancel-all incident order identity is missing.");
            if (_identities.TryGetValue(order.ObservationKey, out var previous)
                && (previous.OrderId != order.OrderId || previous.AccountId is not null && previous.AccountId != order.FundAccountId))
                throw new InvalidDataException("Cancel-all incident order ownership changed in durable history.");
            // A submission first observed before its tracked state existed may later acquire
            // verified attribution. An existing account attribution can never be lost or moved.
            _identities[order.ObservationKey] = (order.OrderId, order.FundAccountId);
        }

        public CancelAllIncident Read()
        {
            var snapshot = _snapshot ?? throw new InvalidDataException("Cancel-all incident header was not retained.");
            return snapshot with
            {
                Orders = _orders.Values.OrderBy(order => order.ObservationKey, StringComparer.Ordinal).ToArray(),
                EvidenceError = snapshot.CompletedAt is null
                    ? snapshot.EvidenceError ?? "The incident has no retained completion record; observations may be incomplete."
                    : snapshot.EvidenceError
            };
        }
    }
}
