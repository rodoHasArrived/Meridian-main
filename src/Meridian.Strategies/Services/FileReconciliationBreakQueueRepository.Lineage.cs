using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationBreakObservationRepository
{
    Task ObserveCompletedRunAsync(ReconciliationCompletedRunObservation run, CancellationToken ct = default);
}

public sealed partial class FileReconciliationBreakQueueRepository : IReconciliationBreakObservationRepository
{
    private const string ObservationReceipt = "SourceRunObserved";

    /// <summary>
    /// Atomically retains a successful, complete source comparison with its receipt. A cleared
    /// source observation never disposes of casework or removes a close blocker.
    /// </summary>
    public async Task ObserveCompletedRunAsync(ReconciliationCompletedRunObservation run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(run.Scope);
        ArgumentNullException.ThrowIfNull(run.Breaks);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.RunId);
        if (!run.Succeeded || !run.CompletePopulation)
            throw new InvalidOperationException("Only a successful complete source population can establish break lineage or clearing.");
        var scope = run.Scope;
        var parts = new[] { scope.TenantId.Trim().ToUpperInvariant(), scope.CompanyId.Trim().ToUpperInvariant(),
            scope.FundProfileId.Trim().ToUpperInvariant(), scope.FundAccountId.Trim().ToUpperInvariant(),
            scope.LedgerBookId.ToString("D"), scope.AccountingPeriodId.Trim().ToUpperInvariant(), scope.SourceSystem, scope.ExternalAccountId };
        if (parts.Any(string.IsNullOrWhiteSpace) || scope.LedgerBookId == Guid.Empty || run.ObservedAt == default)
            throw new InvalidOperationException("Source observations require exact access, accounting, source account and time scope.");
        if (run.Breaks.Any(item => string.IsNullOrWhiteSpace(item.BreakId) || string.IsNullOrWhiteSpace(item.SourceSubjectId))
            || run.Breaks.Select(item => item.BreakId).Distinct(StringComparer.Ordinal).Count() != run.Breaks.Count
            || run.Breaks.Select(item => item.SourceSubjectId).Distinct(StringComparer.Ordinal).Count() != run.Breaks.Count)
            throw new InvalidOperationException("Source comparison requires unique break and source-subject identities.");

        // Length-safe serialization prevents delimiter collisions. Canonical case is used only for
        // accounting/access identities; source account and source subject identifiers remain exact.
        var scopeId = "comparison:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(parts));
        var canonicalRun = run with { Breaks = run.Breaks.OrderBy(item => item.BreakId, StringComparer.Ordinal).ToArray() };
        var inputHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(canonicalRun, _jsonOptions));
        var receiptId = "source-run:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { scopeId, run.RunId }));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var lease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var receipt = _auditEvents.FirstOrDefault(item => item.EventId == receiptId);
            if (receipt is not null)
            {
                if (!string.Equals(receipt.Reason, inputHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("A completed source run cannot be rebound to different observations.");
                return;
            }
            var latest = _auditEvents.Where(item => item.EventType == ObservationReceipt && item.Source == scopeId)
                .OrderByDescending(item => item.Sequence).FirstOrDefault();
            if (latest is not null && latest.OccurredAt >= run.ObservedAt)
            {
                // Publication may finish out of order. Retain a non-comparable receipt without
                // rolling back the newer comparison head or failing already-retained intake.
                var priorState = CaptureState();
                try
                {
                    await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                        receiptId, scopeId, "SourceRunNotCompared", null, ReconciliationBreakQueueStatus.Open,
                        null, ReconciliationCaseLifecycleState.Open, run.ObservedAt, null, null, null,
                        "An older or equally dated run was retained without changing the newer comparison head.",
                        AfterPayload: JsonSerializer.Serialize(canonicalRun, _jsonOptions),
                        CorrelationId: run.RunId, Source: scopeId, Reason: inputHash)
                    { TenantId = scope.TenantId, CompanyId = scope.CompanyId }, ct).ConfigureAwait(false);
                    await PersistSnapshotAsync(ct).ConfigureAwait(false);
                }
                catch { RestoreState(priorState); throw; }
                return;
            }

            var access = new ReconciliationBreakQueueScope(scope.TenantId, scope.CompanyId);
            var incoming = run.Breaks.Select(observation =>
            {
                var item = _items!.GetValueOrDefault(observation.BreakId);
                if (item is null || !access.Owns(item)
                    || !Same(item.FundProfileId, scope.FundProfileId)
                    || !Same(item.FundAccountId, scope.FundAccountId)
                    || item.LedgerBookId != scope.LedgerBookId
                    || !Same(item.AccountingPeriodId, scope.AccountingPeriodId)
                    || !Same(item.SourceImportId, run.RunId))
                    throw new InvalidOperationException("Source observation does not own the exact retained case scope and run.");
                EnsureCloseScopeMutationAllowed(item, "retain source comparison");
                var lineageId = "lineage:" + Sha256Digest.ComputeUtf8(
                    JsonSerializer.Serialize(new[] { scopeId, observation.SourceSubjectId }));
                if (item.Lineage is not null && (item.Lineage.ComparisonScopeId != scopeId || item.Lineage.LineageId != lineageId))
                    throw new InvalidOperationException("A retained source observation cannot be rebound to a different population or identity.");
                return (Item: item, LineageId: lineageId);
            }).ToArray();
            var existing = _items!.Values.Where(item => item.Lineage?.ComparisonScopeId == scopeId).ToArray();
            foreach (var item in existing)
                EnsureCloseScopeMutationAllowed(item, "retain source comparison");
            var state = CaptureState();
            try
            {
                var observedIds = incoming.Select(item => item.LineageId).ToHashSet(StringComparer.Ordinal);
                var historyByLineage = existing.GroupBy(item => item.Lineage!.LineageId)
                    .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Lineage!.LastObservedAt).ToArray());
                foreach (var (item, lineageId) in incoming)
                {
                    var history = historyByLineage.GetValueOrDefault(lineageId) ?? [];
                    var previous = history.FirstOrDefault()?.Lineage;
                    var recurrence = previous?.ClearedAt is not null;
                    var lineage = new ReconciliationBreakLineageDto(
                        lineageId, scopeId,
                        previous is null || recurrence ? item.BreakId : previous.OccurrenceId,
                        previous is null ? 1 : previous.OccurrenceNumber + (recurrence ? 1 : 0),
                        previous is null ? "New" : recurrence ? "Recurring" : "Aging",
                        previous?.FirstObservedAt ?? run.ObservedAt,
                        previous is null || recurrence ? run.ObservedAt : previous.OccurrenceFirstObservedAt,
                        run.ObservedAt, run.RunId);
                    await RetainObservationAsync(item, item with { Lineage = lineage, Version = item.Version + 1 }, run, ct).ConfigureAwait(false);
                    foreach (var prior in history.Where(prior => prior.BreakId != item.BreakId && prior.Lineage!.ClearedAt is null))
                    {
                        var refreshed = prior with
                        {
                            Lineage = prior.Lineage! with { ObservationState = "Aging", LastObservedAt = run.ObservedAt, LastObservedRunId = run.RunId },
                            Version = prior.Version + 1
                        };
                        await RetainObservationAsync(prior, refreshed, run, ct).ConfigureAwait(false);
                    }
                }
                foreach (var item in existing.Where(item => item.Lineage!.ClearedAt is null && !observedIds.Contains(item.Lineage.LineageId)))
                {
                    var next = item with
                    {
                        Lineage = item.Lineage! with { ObservationState = "Cleared", ClearedAt = run.ObservedAt, ClearedByRunId = run.RunId },
                        Version = item.Version + 1
                    };
                    await RetainObservationAsync(item, next, run, ct).ConfigureAwait(false);
                }
                await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                    receiptId, scopeId, ObservationReceipt, null, ReconciliationBreakQueueStatus.Open,
                    null, ReconciliationCaseLifecycleState.Open, run.ObservedAt, null, null, null,
                    "Successful complete source comparison; casework status and approval are unchanged.",
                    AfterPayload: JsonSerializer.Serialize(canonicalRun, _jsonOptions),
                    CorrelationId: run.RunId, Source: scopeId, Reason: inputHash)
                { TenantId = scope.TenantId, CompanyId = scope.CompanyId }, ct).ConfigureAwait(false);
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                RestoreState(state);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task RetainObservationAsync(ReconciliationBreakQueueItem before, ReconciliationBreakQueueItem after,
        ReconciliationCompletedRunObservation run, CancellationToken ct)
    {
        _items![after.BreakId] = after;
        await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
            Guid.NewGuid().ToString("N"), after.BreakId, "SourceBreakObserved", before.Status, after.Status,
            before.LifecycleState, after.LifecycleState, run.ObservedAt, after.AssignedTo, after.ReviewedBy, after.ResolvedBy,
            after.Lineage!.ObservationState, BeforePayload: JsonSerializer.Serialize(before, _jsonOptions),
            AfterPayload: JsonSerializer.Serialize(after, _jsonOptions), CorrelationId: run.RunId,
            Source: after.SourceType), ct).ConfigureAwait(false);
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
