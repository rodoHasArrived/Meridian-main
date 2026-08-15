using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

public sealed partial class FileReconciliationBreakQueueRepository
{
    public async Task VerifyAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ResetCachedState();
            try
            {
                await EnsureLoadedAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // A failed legacy migration or integrity check can populate only part of the
                // in-memory state. Never let a later readiness check reuse that partial state.
                ResetCachedState();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_items is not null)
        {
            var currentStamp = ReadSnapshotStamp();
            if (Equals(_loadedSnapshotStamp, currentStamp))
            {
                return;
            }

            ResetCachedState();
        }

        var snapshotStampAtRead = ReadSnapshotStamp();
        BreakQueueSnapshot? snapshot = null;
        var migrateLegacy = false;
        if (File.Exists(_snapshotPath))
        {
            await using var stream = File.OpenRead(_snapshotPath);
            snapshot = await JsonSerializer.DeserializeAsync<BreakQueueSnapshot>(stream, _jsonOptions, ct).ConfigureAwait(false);
            if (snapshot is null)
            {
                throw new InvalidDataException(
                    $"Reconciliation break queue snapshot '{_snapshotPath}' did not contain a readable snapshot.");
            }

            ValidateSnapshotIntegrity(snapshot);
            migrateLegacy = snapshot.SchemaVersion < CurrentSnapshotSchemaVersion;
        }
        else if (File.Exists(_auditPath))
        {
            migrateLegacy = true;
        }

        var loaded = snapshot?.Items ?? [];
        _items = loaded
            .Select(NormalizeLegacyCaseState)
            .ToDictionary(static item => item.BreakId, StringComparer.OrdinalIgnoreCase);

        _auditEvents.Clear();
        foreach (var auditEvent in snapshot?.AuditEvents ?? [])
        {
            _auditEvents.Add(migrateLegacy
                ? MigrateLegacyAuditEvent(auditEvent, _auditEvents.Count + 1L)
                : ValidateAuditEventIntegrity(auditEvent));
        }
        // The JSONL audit file is a legacy migration input only. Once a verified v2 snapshot
        // exists, replaying the untouched sidecar would reinterpret its sequence-less records as
        // current evidence and make the second restart fail. The migrated, hash-bound audit
        // collection in the snapshot is authoritative from that point forward.
        if (migrateLegacy)
        {
            await LoadLegacyAuditEventsAsync(migrateLegacy: true, ct).ConfigureAwait(false);
        }
        ValidateAuditCollectionIntegrity(_auditEvents);

        _bulkResults.Clear();
        foreach (var result in snapshot?.BulkResults ?? [])
        {
            var retainedResult = migrateLegacy ? MigrateLegacyBulkResult(result) : result;
            ValidateBulkResultIntegrity(retainedResult);
            if (!_bulkResults.TryAdd(retainedResult.BulkActionId, retainedResult))
            {
                throw new InvalidDataException(
                    $"Duplicate reconciliation bulk action id '{retainedResult.BulkActionId}' was retained in the snapshot.");
            }
        }

        _bulkResultIdsByIdempotencyKey.Clear();
        foreach (var pair in snapshot?.BulkResultIdsByIdempotencyKey ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) ||
                !_bulkResults.ContainsKey(pair.Value))
            {
                throw new InvalidDataException(
                    "A retained reconciliation bulk idempotency binding is empty or references a missing bulk result.");
            }
            _bulkResultIdsByIdempotencyKey[pair.Key] = pair.Value;
        }

        _bulkReceipts.Clear();
        if (migrateLegacy)
        {
            foreach (var result in _bulkResults.Values)
            {
                var receipt = new BulkCaseworkReceipt(
                    result.BulkActionId,
                    result.BulkActionId,
                    result.IdempotencyKey,
                    result.InputHashSha256,
                    result,
                    LegacyUnverified: true);
                ValidateBulkReceiptIntegrity(receipt);
                _bulkReceipts[result.BulkActionId] = receipt;
            }
        }
        else
        {
            foreach (var receipt in snapshot?.BulkReceipts ?? [])
            {
                ValidateBulkReceiptIntegrity(receipt);
                if (!_bulkReceipts.TryAdd(receipt.BulkActionId, receipt))
                {
                    throw new InvalidDataException(
                        $"Duplicate reconciliation bulk receipt '{receipt.BulkActionId}' was retained in the snapshot.");
                }
            }
        }

        _commandReceipts.Clear();
        foreach (var receipt in snapshot?.CommandReceipts ?? [])
        {
            var retainedReceipt = migrateLegacy ? MigrateLegacyCommandReceipt(receipt) : receipt;
            ValidateCommandReceiptIntegrity(retainedReceipt);
            if (!_commandReceipts.TryAdd(retainedReceipt.CommandId, retainedReceipt))
            {
                throw new InvalidDataException(
                    $"Duplicate reconciliation casework receipt '{retainedReceipt.CommandId}' was retained in the snapshot.");
            }
        }

        _closeScopeLocks.Clear();
        foreach (var closeScopeLock in snapshot?.CloseScopeLocks ?? [])
        {
            var retainedCloseScopeLock =
                snapshot is { SchemaVersion: < 6 } && closeScopeLock.Generation <= 0
                    ? closeScopeLock with { Generation = 1 }
                    : closeScopeLock;
            ValidateCloseScopeLock(retainedCloseScopeLock);
            if (!_closeScopeLocks.TryAdd(retainedCloseScopeLock.ScopeKey, retainedCloseScopeLock))
            {
                throw new InvalidDataException(
                    $"Duplicate reconciliation close-scope lock '{retainedCloseScopeLock.ScopeKey}' was retained in the snapshot.");
            }
        }

        if (snapshot is { SchemaVersion: >= CurrentSnapshotSchemaVersion })
        {
            if (_bulkReceipts.Count != _bulkResults.Count ||
                _bulkResultIdsByIdempotencyKey.Count != _bulkResults.Count ||
                _bulkResults.Keys.Any(actionId => !_bulkReceipts.ContainsKey(actionId)) ||
                _commandReceipts.Values.Any(static receipt => receipt.Outcome is null))
            {
                throw new InvalidDataException(
                    "Reconciliation break queue receipts are incomplete for a verified snapshot.");
            }
        }

        if (migrateLegacy)
        {
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var snapshotStampAfterRead = ReadSnapshotStamp();
            if (!Equals(snapshotStampAtRead, snapshotStampAfterRead))
            {
                ResetCachedState();
                await EnsureLoadedAsync(ct).ConfigureAwait(false);
                return;
            }
            _loadedSnapshotStamp = snapshotStampAfterRead;
        }
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new BreakQueueSnapshot(
            _items!.Values.OrderByDescending(static item => item.LastUpdatedAt).ToArray(),
            _auditEvents.OrderBy(static item => item.Sequence).ThenBy(static item => item.OccurredAt).ToArray(),
            _bulkResults.Values.OrderBy(static item => item.BulkActionId, StringComparer.Ordinal).ToArray(),
            new Dictionary<string, string>(_bulkResultIdsByIdempotencyKey, StringComparer.OrdinalIgnoreCase),
            _commandReceipts.Values.OrderBy(static item => item.CommandId, StringComparer.Ordinal).ToArray(),
            _bulkReceipts.Values.OrderBy(static item => item.BulkActionId, StringComparer.Ordinal).ToArray(),
            _closeScopeLocks.Values.OrderBy(static item => item.ScopeKey, StringComparer.Ordinal).ToArray())
        {
            SchemaVersion = CurrentSnapshotSchemaVersion
        };
        var contentHash = ComputeSnapshotContentHash(snapshot);
        snapshot = snapshot with { ContentHashSha256 = contentHash };
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await _stateWriter(_snapshotPath, json, ct).ConfigureAwait(false);
        _loadedSnapshotStamp = ReadSnapshotStamp();
    }

    private void ResetCachedState()
    {
        _items = null;
        _loadedSnapshotStamp = null;
        _auditEvents.Clear();
        _bulkResults.Clear();
        _bulkResultIdsByIdempotencyKey.Clear();
        _bulkReceipts.Clear();
        _commandReceipts.Clear();
        _closeScopeLocks.Clear();
    }

    private SnapshotStamp? ReadSnapshotStamp()
    {
        if (!File.Exists(_snapshotPath))
        {
            return null;
        }

        var info = new FileInfo(_snapshotPath);
        return new SnapshotStamp(info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private async Task<FileStream> AcquireMutationLeaseAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _mutationLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }

    private RepositoryState CaptureState()
        => new(
            new Dictionary<string, ReconciliationBreakQueueItem>(_items!, StringComparer.OrdinalIgnoreCase),
            _auditEvents.ToList(),
            new Dictionary<string, ReconciliationBulkCaseworkResult>(_bulkResults, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(_bulkResultIdsByIdempotencyKey, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, BulkCaseworkReceipt>(_bulkReceipts, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CaseworkCommandReceipt>(_commandReceipts, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CloseScopeLockRecord>(_closeScopeLocks, StringComparer.Ordinal),
            _loadedSnapshotStamp);

    private void RestoreState(RepositoryState state)
    {
        _items = state.Items;
        _loadedSnapshotStamp = state.LoadedSnapshotStamp;
        _auditEvents.Clear();
        _auditEvents.AddRange(state.AuditEvents);
        RestoreDictionary(_bulkResults, state.BulkResults);
        RestoreDictionary(_bulkResultIdsByIdempotencyKey, state.BulkResultIdsByIdempotencyKey);
        RestoreDictionary(_bulkReceipts, state.BulkReceipts);
        RestoreDictionary(_commandReceipts, state.CommandReceipts);
        RestoreDictionary(_closeScopeLocks, state.CloseScopeLocks);
    }

    private static void RestoreDictionary<TKey, TValue>(
        IDictionary<TKey, TValue> target,
        IReadOnlyDictionary<TKey, TValue> source)
        where TKey : notnull
    {
        target.Clear();
        foreach (var pair in source)
        {
            target.Add(pair.Key, pair.Value);
        }
    }

    private Task AppendAuditAsync(ReconciliationBreakQueueAuditEvent auditEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var scoped = BindAuditScope(auditEvent);
        var sequenced = scoped with
        {
            Sequence = scoped.Sequence > 0 ? scoped.Sequence : NextAuditSequence(),
            BeforePayloadHash = scoped.BeforePayloadHash ?? HashPayload(scoped.BeforePayload),
            AfterPayloadHash = scoped.AfterPayloadHash ?? HashPayload(scoped.AfterPayload)
        };
        _auditEvents.Add(sequenced);
        return Task.CompletedTask;
    }

    private ReconciliationBreakQueueAuditEvent BindAuditScope(
        ReconciliationBreakQueueAuditEvent auditEvent)
    {
        var hasTenant = !string.IsNullOrWhiteSpace(auditEvent.TenantId);
        var hasCompany = !string.IsNullOrWhiteSpace(auditEvent.CompanyId);
        if (hasTenant != hasCompany)
        {
            throw new InvalidOperationException(
                $"Reconciliation audit event '{auditEvent.EventId}' must retain both tenant and company scope.");
        }

        if (hasTenant)
        {
            return auditEvent with
            {
                TenantId = auditEvent.TenantId!.Trim(),
                CompanyId = auditEvent.CompanyId!.Trim()
            };
        }

        var item = _items?.GetValueOrDefault(auditEvent.BreakId)
                   ?? TryReadQueueItem(auditEvent.AfterPayload)
                   ?? TryReadQueueItem(auditEvent.BeforePayload);
        if (item is null
            || string.IsNullOrWhiteSpace(item.TenantId)
            || string.IsNullOrWhiteSpace(item.CompanyId))
        {
            return auditEvent;
        }

        return auditEvent with
        {
            TenantId = item.TenantId.Trim(),
            CompanyId = item.CompanyId.Trim()
        };
    }

    private ReconciliationBreakQueueItem? TryReadQueueItem(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(payload, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task LoadLegacyAuditEventsAsync(bool migrateLegacy, CancellationToken ct)
    {
        if (!File.Exists(_auditPath))
        {
            return;
        }

        var retainedEvents = new Dictionary<string, ReconciliationBreakQueueAuditEvent>(StringComparer.OrdinalIgnoreCase);
        foreach (var retained in _auditEvents)
        {
            if (!retainedEvents.TryAdd(retained.EventId, retained))
            {
                throw new InvalidDataException(
                    $"Duplicate reconciliation audit event id '{retained.EventId}' was retained in the snapshot.");
            }
        }
        await using var stream = File.OpenRead(_auditPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var auditEvent = JsonSerializer.Deserialize<ReconciliationBreakQueueAuditEvent>(line, _jsonOptions)
                    ?? throw new InvalidDataException(
                        $"Reconciliation break queue audit evidence is null at '{_auditPath}'.");
                if (retainedEvents.TryGetValue(auditEvent.EventId, out var existing))
                {
                    var validatedDuplicate = migrateLegacy
                        ? MigrateLegacyAuditEvent(auditEvent, existing.Sequence)
                        : ValidateAuditEventIntegrity(auditEvent);
                    if (!existing.Equals(validatedDuplicate))
                    {
                        throw new InvalidDataException(
                            $"Reconciliation audit event id '{auditEvent.EventId}' was reused with different evidence.");
                    }
                    continue;
                }

                var validated = migrateLegacy
                    ? MigrateLegacyAuditEvent(auditEvent, _auditEvents.Count + 1L)
                    : ValidateAuditEventIntegrity(auditEvent);
                retainedEvents.Add(validated.EventId, validated);
                _auditEvents.Add(validated);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Reconciliation break queue audit evidence is malformed at '{_auditPath}'. Repair or quarantine is required before casework can continue.",
                    ex);
            }
        }
    }

    private static ReconciliationBreakQueueAuditEvent ValidateAuditEventIntegrity(
        ReconciliationBreakQueueAuditEvent auditEvent)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.EventId) ||
            string.IsNullOrWhiteSpace(auditEvent.BreakId) ||
            string.IsNullOrWhiteSpace(auditEvent.EventType) ||
            auditEvent.OccurredAt == default ||
            auditEvent.SchemaVersion != 1 ||
            auditEvent.Sequence <= 0)
        {
            throw new InvalidDataException(
                "Reconciliation break queue audit evidence has an invalid event identity, timestamp, schema version, or sequence.");
        }

        if (string.IsNullOrWhiteSpace(auditEvent.TenantId) != string.IsNullOrWhiteSpace(auditEvent.CompanyId))
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{auditEvent.EventId}' must retain both tenant and company scope or remain legacy-unscoped.");
        }

        ValidateJsonPayload(auditEvent.EventId, "before", auditEvent.BeforePayload);
        ValidateJsonPayload(auditEvent.EventId, "after", auditEvent.AfterPayload);
        var expectedBeforeHash = HashPayload(auditEvent.BeforePayload);
        var expectedAfterHash = HashPayload(auditEvent.AfterPayload);
        if (!string.Equals(auditEvent.BeforePayloadHash, expectedBeforeHash, StringComparison.Ordinal) ||
            !string.Equals(auditEvent.AfterPayloadHash, expectedAfterHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Reconciliation break queue audit event '{auditEvent.EventId}' failed payload hash verification.");
        }

        ValidateAuditPayloadIdentity(auditEvent);

        return auditEvent;
    }

    private static ReconciliationBreakQueueAuditEvent MigrateLegacyAuditEvent(
        ReconciliationBreakQueueAuditEvent auditEvent,
        long expectedSequence)
    {
        if (auditEvent.Sequence != 0 && auditEvent.Sequence != expectedSequence)
        {
            throw new InvalidDataException(
                $"Legacy reconciliation audit event '{auditEvent.EventId}' has unexpected sequence {auditEvent.Sequence}; expected 0 or {expectedSequence}.");
        }

        var expectedBeforeHash = HashPayload(auditEvent.BeforePayload);
        var expectedAfterHash = HashPayload(auditEvent.AfterPayload);
        if ((auditEvent.BeforePayloadHash is not null &&
             !string.Equals(auditEvent.BeforePayloadHash, expectedBeforeHash, StringComparison.Ordinal)) ||
            (auditEvent.AfterPayloadHash is not null &&
             !string.Equals(auditEvent.AfterPayloadHash, expectedAfterHash, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Legacy reconciliation audit event '{auditEvent.EventId}' failed payload hash verification.");
        }

        return ValidateAuditEventIntegrity(auditEvent with
        {
            SchemaVersion = 1,
            Sequence = expectedSequence,
            BeforePayloadHash = expectedBeforeHash,
            AfterPayloadHash = expectedAfterHash
        });
    }

    private static void ValidateJsonPayload(string eventId, string label, string? payload)
    {
        if (payload is null)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new JsonException("The payload root is null.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{eventId}' contains malformed {label} payload evidence.",
                ex);
        }
    }

    private static void ValidateAuditPayloadIdentity(ReconciliationBreakQueueAuditEvent auditEvent)
    {
        if (auditEvent.EventType.StartsWith("CaseworkReplay", StringComparison.Ordinal))
        {
            ValidateCaseworkReplayPayload(auditEvent);
            return;
        }
        if (auditEvent.EventType.StartsWith("BulkActionReplay", StringComparison.Ordinal))
        {
            ValidateBulkReplayPayload(auditEvent);
            return;
        }
        if (auditEvent.EventType is "CreateReplayAccepted" or "CreateConflict")
        {
            ValidateCasePayloadIdentity(auditEvent, auditEvent.BeforePayload, isBefore: true, allowDifferentBreakId: false, validateState: false);
            ValidateCasePayloadIdentity(auditEvent, auditEvent.AfterPayload, isBefore: false, allowDifferentBreakId: false, validateState: false);
            return;
        }

        ValidateCasePayloadIdentity(
            auditEvent,
            auditEvent.BeforePayload,
            isBefore: true,
            allowDifferentBreakId: string.Equals(auditEvent.EventType, "BreakIdMigrated", StringComparison.Ordinal));
        ValidateCasePayloadIdentity(auditEvent, auditEvent.AfterPayload, isBefore: false, allowDifferentBreakId: false);
    }

    private static void ValidateCaseworkReplayPayload(ReconciliationBreakQueueAuditEvent auditEvent)
    {
        try
        {
            var command = JsonSerializer.Deserialize<ReconciliationCaseworkCommand>(
                auditEvent.BeforePayload ?? string.Empty,
                AuditValidationJsonOptions);
            if (command is null ||
                !string.Equals(command.BreakId, auditEvent.BreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(command.CommandId, auditEvent.CommandId, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException("Replay command identity does not match its audit event.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{auditEvent.EventId}' contains invalid casework replay evidence.",
                ex);
        }
    }

    private static void ValidateBulkReplayPayload(ReconciliationBreakQueueAuditEvent auditEvent)
    {
        try
        {
            var request = JsonSerializer.Deserialize<ReconciliationBulkCaseworkRequest>(
                auditEvent.BeforePayload ?? string.Empty,
                AuditValidationJsonOptions);
            if (request is null ||
                !string.Equals(request.CommandId, auditEvent.BreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.CommandId, auditEvent.CommandId, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException("Replay request identity does not match its audit event.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{auditEvent.EventId}' contains invalid bulk replay evidence.",
                ex);
        }
    }

    private static void ValidateCasePayloadIdentity(
        ReconciliationBreakQueueAuditEvent auditEvent,
        string? payload,
        bool isBefore,
        bool allowDifferentBreakId,
        bool validateState = true)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("breakId", out _))
        {
            return;
        }

        ReconciliationBreakQueueItem item;
        try
        {
            item = JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(payload, AuditValidationJsonOptions)
                ?? throw new JsonException("The case payload is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{auditEvent.EventId}' contains an invalid case payload.",
                ex);
        }

        if (!allowDifferentBreakId &&
            !string.Equals(item.BreakId, auditEvent.BreakId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{auditEvent.EventId}' payload break identity does not match the event.");
        }

        if (!validateState)
        {
            return;
        }

        var expectedStatus = isBefore ? auditEvent.PreviousStatus : auditEvent.NewStatus;
        var expectedLifecycle = isBefore ? auditEvent.PreviousLifecycleState : auditEvent.NewLifecycleState;
        if (expectedStatus.HasValue && item.Status != expectedStatus.Value)
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{auditEvent.EventId}' payload status does not match the event.");
        }
        if (expectedLifecycle.HasValue && item.LifecycleState != expectedLifecycle.Value)
        {
            throw new InvalidDataException(
                $"Reconciliation audit event '{auditEvent.EventId}' payload lifecycle state does not match the event.");
        }
    }

    private static void ValidateAuditCollectionIntegrity(IReadOnlyList<ReconciliationBreakQueueAuditEvent> auditEvents)
    {
        var eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expectedSequence = 1;
        foreach (var auditEvent in auditEvents)
        {
            if (!eventIds.Add(auditEvent.EventId))
            {
                throw new InvalidDataException(
                    $"Duplicate reconciliation audit event id '{auditEvent.EventId}' was retained.");
            }
            if (auditEvent.Sequence != expectedSequence)
            {
                throw new InvalidDataException(
                    $"Reconciliation audit sequence is discontinuous at event '{auditEvent.EventId}': expected {expectedSequence}, found {auditEvent.Sequence}.");
            }
            expectedSequence++;
        }
    }

    private void ValidateSnapshotIntegrity(BreakQueueSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is < 1 or > CurrentSnapshotSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported reconciliation break queue snapshot schema version '{snapshot.SchemaVersion}'.");
        }

        if (snapshot.SchemaVersion < CurrentSnapshotSchemaVersion)
        {
            return;
        }

        var expectedHash = ComputeSnapshotContentHash(snapshot);
        var legacyTypedSerializationHash = HashPayload(JsonSerializer.Serialize(
            snapshot with { ContentHashSha256 = null },
            _jsonOptions));
        if (string.IsNullOrWhiteSpace(snapshot.ContentHashSha256) ||
            (!string.Equals(snapshot.ContentHashSha256, expectedHash, StringComparison.Ordinal) &&
             !string.Equals(snapshot.ContentHashSha256, legacyTypedSerializationHash, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Reconciliation break queue snapshot failed content hash verification.");
        }
    }

    private string ComputeSnapshotContentHash(BreakQueueSnapshot snapshot)
    {
        var node = JsonSerializer.SerializeToNode(
            snapshot with { ContentHashSha256 = null },
            _jsonOptions)?.AsObject()
            ?? throw new InvalidDataException(
                "Reconciliation break queue snapshot could not be canonicalized for content hash verification.");
        node["contentHashSha256"] = null;
        return HashPayload(node.ToJsonString())!;
    }

    private static void ValidateBulkResultIntegrity(ReconciliationBulkCaseworkResult result)
    {
        if (string.IsNullOrWhiteSpace(result.BulkActionId) ||
            string.IsNullOrWhiteSpace(result.IdempotencyKey) ||
            !Sha256Digest.IsWellFormed(result.InputHashSha256) ||
            result.RequestedCount < 0 ||
            result.SucceededCount < 0 ||
            result.FailedCount < 0 ||
            result.Results is null ||
            result.Results.Count != result.RequestedCount ||
            result.SucceededCount + result.FailedCount != result.RequestedCount)
        {
            throw new InvalidDataException(
                $"Reconciliation bulk result '{result.BulkActionId}' has invalid identity, counts, or input hash.");
        }

        var uniqueBreakIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var caseResult in result.Results)
        {
            if (string.IsNullOrWhiteSpace(caseResult.BreakId) || !uniqueBreakIds.Add(caseResult.BreakId))
            {
                throw new InvalidDataException(
                    $"Reconciliation bulk result '{result.BulkActionId}' has an empty or duplicate case result identity.");
            }
            if (caseResult.Succeeded && (!caseResult.WouldSucceed || caseResult.Item is null || caseResult.Error is not null))
            {
                throw new InvalidDataException(
                    $"Reconciliation bulk result '{result.BulkActionId}' contains a contradictory successful case result.");
            }
        }

        var computedSuccessCount = result.DryRun
            ? result.Results.Count(static item => item.WouldSucceed)
            : result.Results.Count(static item => item.Succeeded);
        if (computedSuccessCount != result.SucceededCount ||
            result.RequestedCount - computedSuccessCount != result.FailedCount)
        {
            throw new InvalidDataException(
                $"Reconciliation bulk result '{result.BulkActionId}' retained counts do not match its case results.");
        }

        try
        {
            VerifiedOperationOutcomeValidator.ValidateAndThrow(result.Outcome);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(
                $"Reconciliation bulk result '{result.BulkActionId}' contains an invalid verified outcome.",
                ex);
        }
        if (!string.Equals(result.InputHashSha256, result.Outcome.InputHashSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Reconciliation bulk result '{result.BulkActionId}' outcome is not bound to its request input hash.");
        }
    }

    private ReconciliationBulkCaseworkResult MigrateLegacyBulkResult(ReconciliationBulkCaseworkResult result)
    {
        var legacyPayload = JsonSerializer.Serialize(new
        {
            result.BulkActionId,
            result.IdempotencyKey,
            result.DryRun,
            result.RequestedCount,
            result.SucceededCount,
            result.FailedCount,
            result.Results
        }, _jsonOptions);
        var legacyHash = HashPayload($"meridian.reconciliation-legacy-bulk-result.v1\n{legacyPayload}")!;
        var outcome = CreateLegacyUnverifiedOutcome(
            $"reconciliation-bulk:{result.BulkActionId}",
            "reconciliation.casework.bulk-legacy-unverified",
            result.BulkActionId,
            legacyHash,
            "Legacy bulk result did not retain the complete canonical request input and cannot satisfy verified idempotent replay.");
        return result with
        {
            InputHashSha256 = legacyHash,
            Outcome = outcome
        };
    }

    private static CaseworkCommandReceipt MigrateLegacyCommandReceipt(CaseworkCommandReceipt receipt)
    {
        var hash = Sha256Digest.IsWellFormed(receipt.InputHashSha256)
            ? receipt.InputHashSha256
            : HashPayload($"meridian.reconciliation-legacy-command-receipt.v1\n{receipt.CommandId}|{receipt.BreakId}|{receipt.Action}|{receipt.Result.Version}")!;
        return receipt with
        {
            InputHashSha256 = hash,
            Outcome = CreateLegacyUnverifiedOutcome(
                $"reconciliation-casework:{receipt.CommandId}",
                "reconciliation.casework.legacy-unverified",
                receipt.BreakId,
                hash,
                "Legacy casework receipt did not retain a verified canonical input binding and cannot satisfy replay."),
            LegacyUnverified = true
        };
    }

    private static VerifiedOperationOutcome CreateLegacyUnverifiedOutcome(
        string operationId,
        string operationKind,
        string correlationId,
        string evidenceHash,
        string message)
    {
        var now = DateTimeOffset.UtcNow;
        const string evidenceId = "legacy-unverified-receipt";
        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: operationId,
            OperationKind: operationKind,
            State: OperationTerminalState.Blocked,
            StartedAtUtc: now,
            CompletedAtUtc: now,
            AttemptNumber: 1,
            CorrelationId: correlationId,
            InputHashSha256: evidenceHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "canonical-input-bound",
                    "The retained receipt is bound to the complete canonical operation input.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [evidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    evidenceId,
                    "legacy-result",
                    "Hash of the retained legacy result; this is not a canonical request-input hash.",
                    Uri: $"urn:sha256:{evidenceHash}",
                    ContentHashSha256: evidenceHash,
                    CapturedAtUtc: now)
            ],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    "legacy-unverified",
                    message,
                    OperationIssueSeverity.Error,
                    EvidenceId: evidenceId)
                {
                    IsBlocking = true
                }
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "submit-new-command",
                    "Submit a new command",
                    "Review the legacy result, then submit the desired operation with a new command id and idempotency key so a verified receipt can be retained.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidenceId]
                }
            ]));
    }

    private void ValidateBulkReceiptIntegrity(BulkCaseworkReceipt receipt)
    {
        if (receipt.AccessScope is not null &&
            receipt.Result.Results.Any(result => result.Item is not null && !receipt.AccessScope.Owns(result.Item)))
        {
            throw new InvalidDataException(
                $"Reconciliation bulk receipt '{receipt.BulkActionId}' contains a result outside its retained tenant and company scope.");
        }

        if (string.IsNullOrWhiteSpace(receipt.BulkActionId) ||
            string.IsNullOrWhiteSpace(receipt.CommandId) ||
            string.IsNullOrWhiteSpace(receipt.IdempotencyKey) ||
            !Sha256Digest.IsWellFormed(receipt.InputHashSha256) ||
            !string.Equals(receipt.BulkActionId, receipt.CommandId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.Result.BulkActionId, receipt.BulkActionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.Result.IdempotencyKey, receipt.IdempotencyKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.Result.InputHashSha256, receipt.InputHashSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Reconciliation bulk receipt '{receipt.BulkActionId}' has an invalid identity or input binding.");
        }

        if (!_bulkResults.TryGetValue(receipt.BulkActionId, out var result) ||
            !string.Equals(JsonSerializer.Serialize(result, _jsonOptions), JsonSerializer.Serialize(receipt.Result, _jsonOptions), StringComparison.Ordinal) ||
            !_bulkResultIdsByIdempotencyKey.TryGetValue(receipt.IdempotencyKey, out var boundActionId) ||
            !string.Equals(boundActionId, receipt.BulkActionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Reconciliation bulk receipt '{receipt.BulkActionId}' does not match its retained result and idempotency binding.");
        }
        if (receipt.LegacyUnverified != receipt.Result.Outcome.Issues.Any(static issue =>
                string.Equals(issue.Code, "legacy-unverified", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Reconciliation bulk receipt '{receipt.BulkActionId}' has inconsistent legacy-verification metadata.");
        }
    }

    private void ValidateCommandReceiptIntegrity(CaseworkCommandReceipt receipt)
    {
        if (receipt.AccessScope is not null && !receipt.AccessScope.Owns(receipt.Result))
        {
            throw new InvalidDataException(
                $"Reconciliation casework receipt '{receipt.CommandId}' contains a result outside its retained tenant and company scope.");
        }

        if (string.IsNullOrWhiteSpace(receipt.CommandId) ||
            string.IsNullOrWhiteSpace(receipt.BreakId) ||
            !Sha256Digest.IsWellFormed(receipt.InputHashSha256) ||
            !string.Equals(receipt.Result.BreakId, receipt.BreakId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Reconciliation casework receipt '{receipt.CommandId}' has an invalid identity or input binding.");
        }

        if (!receipt.LegacyUnverified && receipt.Outcome is null)
        {
            throw new InvalidDataException(
                $"Reconciliation casework receipt '{receipt.CommandId}' is missing its verified terminal outcome.");
        }

        if (receipt.Outcome is not null)
        {
            try
            {
                VerifiedOperationOutcomeValidator.ValidateAndThrow(receipt.Outcome);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(
                    $"Reconciliation casework receipt '{receipt.CommandId}' contains an invalid verified outcome.",
                    ex);
            }
            if (!string.Equals(receipt.Outcome.InputHashSha256, receipt.InputHashSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Reconciliation casework receipt '{receipt.CommandId}' outcome is not bound to its command input hash.");
            }
            if (!receipt.LegacyUnverified &&
                !string.Equals(
                    receipt.Outcome.OperationId,
                    $"reconciliation-casework:{receipt.CommandId}",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Reconciliation casework receipt '{receipt.CommandId}' outcome is not bound to its command id.");
            }
            if (receipt.LegacyUnverified != receipt.Outcome.Issues.Any(static issue =>
                    string.Equals(issue.Code, "legacy-unverified", StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Reconciliation casework receipt '{receipt.CommandId}' has inconsistent legacy-verification metadata.");
            }

            if (!receipt.LegacyUnverified && receipt.Outcome.State == OperationTerminalState.Succeeded)
            {
                const string auditEvidencePrefix = "audit:";
                var auditEvidence = receipt.Outcome.Evidence
                    .Where(static evidence =>
                        string.Equals(evidence.Kind, "reconciliation-audit-event", StringComparison.Ordinal))
                    .ToArray();
                if (auditEvidence.Length != 1 ||
                    !auditEvidence[0].EvidenceId.StartsWith(auditEvidencePrefix, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Reconciliation casework receipt '{receipt.CommandId}' does not identify exactly one retained audit event.");
                }

                var auditEventId = auditEvidence[0].EvidenceId[auditEvidencePrefix.Length..];
                var auditEvent = _auditEvents.SingleOrDefault(entry =>
                    string.Equals(entry.EventId, auditEventId, StringComparison.OrdinalIgnoreCase));
                var expectedAuditHash = auditEvent?.AfterPayloadHash ?? auditEvent?.BeforePayloadHash;
                if (auditEvent is null ||
                    !string.Equals(auditEvent.BreakId, receipt.BreakId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(auditEvent.CommandId, receipt.CommandId, StringComparison.Ordinal) ||
                    !string.Equals(
                        auditEvidence[0].ContentHashSha256,
                        expectedAuditHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Reconciliation casework receipt '{receipt.CommandId}' is not bound to its retained audit evidence.");
                }
            }
        }
    }

}
