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
        var parts = ScopeParts(scope, scope.SourceSystem);
        if (parts.Any(string.IsNullOrWhiteSpace) || scope.LedgerBookId == Guid.Empty || run.ObservedAt == default
            || scope.LineageSourceIdentity is not null && string.IsNullOrWhiteSpace(scope.LineageSourceIdentity))
            throw new InvalidOperationException("Source observations require exact access, accounting, source account and time scope.");
        if (run.Breaks.Any(item => string.IsNullOrWhiteSpace(item.BreakId) || string.IsNullOrWhiteSpace(item.SourceSubjectId))
            || run.Breaks.Select(item => item.BreakId).Distinct(StringComparer.Ordinal).Count() != run.Breaks.Count
            || run.Breaks.Select(item => item.SourceSubjectId).Distinct(StringComparer.Ordinal).Count() != run.Breaks.Count)
            throw new InvalidOperationException("Source comparison requires unique break and source-subject identities.");

        // Length-safe serialization prevents delimiter collisions. Canonical case is used only for
        // accounting/access identities; source account and source subject identifiers remain exact.
        var scopeId = "comparison:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(parts));
        var identityScopeId = scope.LineageSourceIdentity is null ? scopeId
            : "identity:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(ScopeParts(scope, scope.LineageSourceIdentity)));
        var canonicalRun = run with { Breaks = run.Breaks.OrderBy(item => item.BreakId, StringComparer.Ordinal).ToArray() };
        var inputHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(canonicalRun, _jsonOptions));
        var receiptId = "source-run:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { identityScopeId, run.RunId }));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var lease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var legacyScopes = FindLegacyIdentityScopes(scope);
            var legacyReceiptIds = legacyScopes.Select(key => "source-run:" + Sha256Digest.ComputeUtf8(
                JsonSerializer.Serialize(new[] { key, run.RunId }))).ToHashSet(StringComparer.Ordinal);
            var receipts = _auditEvents.Where(item => item.EventId == receiptId || legacyReceiptIds.Contains(item.EventId)).ToArray();
            if (receipts.Length > 0)
            {
                var legacyHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(
                    canonicalRun with { Scope = scope with { LineageSourceIdentity = null } }, _jsonOptions));
                foreach (var receipt in receipts)
                    if (!string.Equals(receipt.Reason, receipt.EventId == receiptId ? inputHash : legacyHash, StringComparison.Ordinal)
                        && !SubjectUpgradeReplayMatches(receipt, canonicalRun, receipt.EventId != receiptId))
                        throw new InvalidOperationException("A completed source run cannot be rebound to different observations.");
                return;
            }
            var latest = _auditEvents.Where(item => item.EventType == ObservationReceipt
                    && (item.Source == identityScopeId || item.Source is not null && legacyScopes.Contains(item.Source)))
                .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Sequence).FirstOrDefault();
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
                        CorrelationId: run.RunId, Source: identityScopeId, Reason: inputHash)
                    { TenantId = scope.TenantId, CompanyId = scope.CompanyId }, ct).ConfigureAwait(false);
                    await PersistSnapshotAsync(ct).ConfigureAwait(false);
                }
                catch { RestoreState(priorState); throw; }
                return;
            }

            var access = new ReconciliationBreakQueueScope(scope.TenantId, scope.CompanyId);
            var existing = _items!.Values.Where(item => item.Lineage is { } lineage
                && ((lineage.IdentityScopeId ?? lineage.ComparisonScopeId) == identityScopeId
                    || lineage.IdentityScopeId is null && legacyScopes.Contains(lineage.ComparisonScopeId))).ToArray();
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
                var lineageId = ResolveLineageId(identityScopeId, legacyScopes, observation.SourceSubjectId, existing);
                if (item.Lineage is not null && (item.Lineage.ComparisonScopeId != scopeId || item.Lineage.LineageId != lineageId))
                    throw new InvalidOperationException("A retained source observation cannot be rebound to a different population or identity.");
                return (Item: item, LineageId: lineageId);
            }).ToArray();
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
                        run.ObservedAt, run.RunId)
                    { IdentityScopeId = scope.LineageSourceIdentity is null ? null : identityScopeId };
                    await RetainObservationAsync(item, StampComputedFields(item with { Lineage = lineage }, run.ObservedAt), run, ct).ConfigureAwait(false);
                    foreach (var prior in history.Where(prior => prior.BreakId != item.BreakId && prior.Lineage!.ClearedAt is null))
                    {
                        var refreshed = prior with
                        {
                            Lineage = prior.Lineage! with
                            {
                                IdentityScopeId = lineage.IdentityScopeId,
                                ComparisonScopeId = scopeId,
                                ObservationState = "Aging",
                                LastObservedAt = run.ObservedAt,
                                LastObservedRunId = run.RunId
                            },
                            Version = prior.Version + 1
                        };
                        await RetainObservationAsync(prior, StampComputedFields(refreshed with { Version = prior.Version }, run.ObservedAt), run, ct).ConfigureAwait(false);
                    }
                }
                foreach (var item in existing.Where(item => item.Lineage!.ClearedAt is null && item.Lineage.ComparisonScopeId == scopeId && !observedIds.Contains(item.Lineage.LineageId)))
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
                    CorrelationId: run.RunId, Source: identityScopeId, Reason: inputHash)
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

    private static string[] ScopeParts(ReconciliationRunObservationScope scope, string source)
        => [scope.TenantId?.Trim().ToUpperInvariant() ?? "", scope.CompanyId?.Trim().ToUpperInvariant() ?? "",
            scope.FundProfileId?.Trim().ToUpperInvariant() ?? "", scope.FundAccountId?.Trim().ToUpperInvariant() ?? "",
            scope.LedgerBookId.ToString("D"), scope.AccountingPeriodId?.Trim().ToUpperInvariant() ?? "",
            source, scope.ExternalAccountId];

    private HashSet<string> FindLegacyIdentityScopes(ReconciliationRunObservationScope scope)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (scope.LineageSourceIdentity is null)
            return result;
        var expected = ScopeParts(scope, scope.LineageSourceIdentity);
        foreach (var receipt in _auditEvents.Where(item => item.EventType is ObservationReceipt or "SourceRunNotCompared"))
        {
            if (receipt.AfterPayload is null || receipt.Source is null)
                continue;
            var retained = JsonSerializer.Deserialize<ReconciliationCompletedRunObservation>(receipt.AfterPayload, _jsonOptions);
            if (retained?.Scope is not { LineageSourceIdentity: null } previous)
                continue;
            // Only the production adapter's retained shape supplies an upgrade identity. Never
            // derive it from a current profile catalog or guess it from an arbitrary source label.
            try
            {
                using var document = JsonDocument.Parse(previous.SourceSystem);
                var source = document.RootElement;
                if (source.ValueKind != JsonValueKind.Object
                    || !source.TryGetProperty("institution", out var institution) || institution.ValueKind != JsonValueKind.String
                    || !source.TryGetProperty("SourceComparisonPolicyFingerprint", out var policy) || policy.ValueKind != JsonValueKind.String
                    || !Sha256Digest.IsWellFormed(policy.GetString())
                    || !source.TryGetProperty("SourceComparisonPopulationKinds", out var populations) || populations.ValueKind != JsonValueKind.Array
                    || !ScopeParts(previous, institution.GetString()!).SequenceEqual(expected, StringComparer.Ordinal))
                    continue;
                var key = "comparison:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(ScopeParts(previous, previous.SourceSystem)));
                if (receipt.Source == key)
                    result.Add(key);
            }
            catch (JsonException) { /* Non-production source identities cannot establish legacy continuity. */ }
        }
        return result;
    }

    private string ResolveLineageId(string identityScopeId, HashSet<string> legacyScopes,
        string subject, ReconciliationBreakQueueItem[] existing)
    {
        static string Key(string scope, string subjectId) => "lineage:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { scope, subjectId }));
        var canonical = Key(identityScopeId, subject);
        var candidates = legacyScopes.Select(scope => Key(scope, subject)).Append(canonical).ToHashSet(StringComparer.Ordinal);
        var businessSubject = StatementBusinessSubject(subject);
        if (businessSubject is not null)
        {
            // Older production receipts wrapped the source subject in its break classification;
            // transaction subjects also included mutable mapped fields. Adopt only identities
            // proven by retained, complete observations under this exact business scope.
            foreach (var receipt in _auditEvents.Where(item => item.EventType == ObservationReceipt
                         && item.Source is not null && (item.Source == identityScopeId || legacyScopes.Contains(item.Source))))
            {
                if (receipt.AfterPayload is null)
                    continue;
                var historicalRun = JsonSerializer.Deserialize<ReconciliationCompletedRunObservation>(receipt.AfterPayload, _jsonOptions);
                if (historicalRun is not { Succeeded: true, CompletePopulation: true }
                    || historicalRun.Breaks is null
                    || !HasVerifiedSourceReceipt(receipt, historicalRun))
                    continue;
                foreach (var observation in historicalRun.Breaks.Where(item => StatementBusinessSubject(item.SourceSubjectId) == businessSubject))
                {
                    candidates.Add(Key(identityScopeId, observation.SourceSubjectId));
                    foreach (var scope in legacyScopes)
                        candidates.Add(Key(scope, observation.SourceSubjectId));
                }
            }
        }
        var retained = existing.Select(item => item.Lineage!.LineageId).Where(candidates.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (retained.Length > 1)
            throw new InvalidOperationException("Historical policy forks require independent reconciliation review before lineage can be unified.");
        return retained.SingleOrDefault() ?? canonical;
    }

    private bool SubjectUpgradeReplayMatches(ReconciliationBreakQueueAuditEvent receipt,
        ReconciliationCompletedRunObservation requested, bool legacyScope)
    {
        if (receipt.AfterPayload is null)
            return false;
        var original = JsonSerializer.Deserialize<ReconciliationCompletedRunObservation>(receipt.AfterPayload, _jsonOptions);
        if (original?.Breaks is null
            || !HasVerifiedSourceReceipt(receipt, original))
            return false;
        static ReconciliationCompletedRunObservation Normalize(ReconciliationCompletedRunObservation run)
            => run with
            {
                Breaks = run.Breaks.Select(item => item with
                { SourceSubjectId = StatementBusinessSubject(item.SourceSubjectId) ?? item.SourceSubjectId })
                    .OrderBy(item => item.BreakId, StringComparer.Ordinal).ToArray()
            };
        var compatibleRequest = legacyScope
            ? requested with { Scope = requested.Scope with { LineageSourceIdentity = null } }
            : requested;
        // Preserve the original receipt. Only a subject representation change is compatible;
        // run, time, policy, access/accounting scope and break membership still compare exactly.
        return JsonSerializer.Serialize(Normalize(original), _jsonOptions)
            == JsonSerializer.Serialize(Normalize(compatibleRequest), _jsonOptions);
    }

    private bool HasVerifiedSourceReceipt(ReconciliationBreakQueueAuditEvent receipt, ReconciliationCompletedRunObservation run)
    {
        if (run.Scope is null)
            return false;
        var source = run.Scope.LineageSourceIdentity ?? run.Scope.SourceSystem;
        var scopeId = (run.Scope.LineageSourceIdentity is null ? "comparison:" : "identity:")
            + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(ScopeParts(run.Scope, source)));
        var receiptId = "source-run:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { scopeId, run.RunId }));
        return receipt.Source == scopeId && receipt.EventId == receiptId && receipt.CorrelationId == run.RunId
            && receipt.Reason == Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(run, _jsonOptions));
    }

    private static string? StatementBusinessSubject(string subject)
    {
        try
        {
            var parts = JsonSerializer.Deserialize<string[]>(subject);
            if (parts is { Length: 2 } && parts[0]?.StartsWith("[", StringComparison.Ordinal) == true
                && !string.IsNullOrWhiteSpace(parts[1]))
                parts = JsonSerializer.Deserialize<string[]>(parts[0]);
            if (parts is null || parts.Length == 0)
                return null;
            return parts[0] switch
            {
                "transaction" when (parts.Length is 2 or 5) && !string.IsNullOrWhiteSpace(parts[1])
                    => JsonSerializer.Serialize(new[] { "transaction", parts[1] }),
                "position" when parts.Length == 3 && !parts.Any(string.IsNullOrWhiteSpace) => JsonSerializer.Serialize(parts),
                "cash" when parts.Length == 2 && !parts.Any(string.IsNullOrWhiteSpace) => JsonSerializer.Serialize(parts),
                _ => null
            };
        }
        catch (JsonException) { return null; }
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
