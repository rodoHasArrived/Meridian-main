using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed partial class FileReconciliationBreakQueueRepository
{
    public async Task<IReconciliationCloseScopeLease> AcquireCloseScopeLeaseAsync(
        ReconciliationCloseScope scope,
        CancellationToken ct = default)
    {
        ValidateCloseScope(scope);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        FileStream? mutationLease = null;
        try
        {
            mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);

            var scopeKey = BuildCloseScopeKey(scope);
            var repositoryState = CaptureState();
            var token = Guid.NewGuid().ToString("N");
            IReadOnlyList<ReconciliationBreakQueueItem> checkpointItems;
            string checkpointHash;
            long checkpointGeneration;
            if (_closeScopeLocks.TryGetValue(scopeKey, out var retained))
            {
                if (retained.State == CloseScopeLockState.HardClosed)
                {
                    throw new InvalidOperationException(
                        $"Reconciliation close scope '{scopeKey}' is already sealed as hard-closed.");
                }

                if (retained.State == CloseScopeLockState.Reopened)
                {
                    checkpointGeneration = checked(retained.Generation + 1);
                    checkpointItems = FreezeCloseScopeCheckpointItems(scope, _items!.Values);
                    checkpointHash = ComputeCloseScopeCheckpointHash(
                        scope,
                        checkpointItems,
                        checkpointGeneration);
                    _closeScopeLocks[scopeKey] = retained with
                    {
                        State = CloseScopeLockState.Closing,
                        Token = token,
                        CheckpointHashSha256 = checkpointHash,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        CheckpointItems = checkpointItems,
                        Generation = checkpointGeneration
                    };
                }
                else
                {
                    // Holding the repository mutation file exclusively proves that the process which
                    // retained this Closing token no longer owns the lease. Rotate the token to fence
                    // any stale owner, but retain the exact hash-verified checkpoint. The caller must
                    // re-read the authoritative ledger state before either resuming or abandoning it.
                    checkpointGeneration = retained.Generation;
                    checkpointItems = CloneCloseScopeCheckpointItems(retained.CheckpointItems!);
                    checkpointHash = retained.CheckpointHashSha256;
                    _closeScopeLocks[scopeKey] = retained with
                    {
                        Token = token,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    };
                }
            }
            else
            {
                checkpointGeneration = 1;
                checkpointItems = FreezeCloseScopeCheckpointItems(scope, _items!.Values);
                checkpointHash = ComputeCloseScopeCheckpointHash(
                    scope,
                    checkpointItems,
                    checkpointGeneration);
                _closeScopeLocks[scopeKey] = new CloseScopeLockRecord(
                    scopeKey,
                    scope,
                    CloseScopeLockState.Closing,
                    token,
                    checkpointHash,
                    DateTimeOffset.UtcNow,
                    checkpointItems,
                    checkpointGeneration);
            }

            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                RestoreState(repositoryState);
                throw;
            }

            var lease = new CloseScopeLease(
                this,
                scopeKey,
                token,
                scope,
                CloneCloseScopeCheckpointItems(checkpointItems),
                checkpointHash,
                checkpointGeneration,
                mutationLease);
            mutationLease = null;
            return lease;
        }
        catch
        {
            if (mutationLease is not null)
            {
                await mutationLease.DisposeAsync().ConfigureAwait(false);
            }
            _gate.Release();
            throw;
        }
    }

    public async Task<ReconciliationCloseScopeCheckpoint> RecoverHardClosedScopeCheckpointAsync(
        ReconciliationCloseScope scope,
        CancellationToken ct = default)
    {
        ValidateCloseScope(scope);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);

            var scopeKey = BuildCloseScopeKey(scope);
            if (!_closeScopeLocks.TryGetValue(scopeKey, out var retained))
            {
                throw new InvalidOperationException(
                    $"Reconciliation close scope '{scopeKey}' has no retained point-in-time checkpoint. Hard-close evidence cannot be reconstructed from the mutable queue.");
            }

            ValidateCloseScopeLock(retained);
            if (retained.State == CloseScopeLockState.Closing)
            {
                var sealedCheckpoint = retained with
                {
                    State = CloseScopeLockState.HardClosed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                _closeScopeLocks[scopeKey] = sealedCheckpoint;
                try
                {
                    await PersistSnapshotAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    _closeScopeLocks[scopeKey] = retained;
                    throw;
                }

                retained = sealedCheckpoint;
            }
            else if (retained.State == CloseScopeLockState.Reopened)
            {
                throw new InvalidOperationException(
                    $"Reconciliation close scope '{scopeKey}' was governed-reopened and has no active hard-close checkpoint.");
            }

            return CreateCloseScopeCheckpoint(retained);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationCloseScopeReopenReceipt> ReopenCloseScopeAsync(
        ReconciliationCloseScope scope,
        ReconciliationCloseScopeReopenCommand command,
        CancellationToken ct = default)
    {
        ValidateCloseScope(scope);
        ValidateCloseScopeReopenCommand(command);
        var evidence = NormalizeCloseScopeReopenEvidence(command.EvidenceLinks);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);

            var scopeKey = BuildCloseScopeKey(scope);
            if (!_closeScopeLocks.TryGetValue(scopeKey, out var retained))
            {
                throw new InvalidOperationException(
                    $"Reconciliation close scope '{scopeKey}' has no sealed checkpoint to governed-reopen.");
            }

            ValidateCloseScopeLock(retained);
            if (retained.State == CloseScopeLockState.Closing)
            {
                throw new InvalidOperationException(
                    $"Reconciliation close scope '{scopeKey}' is still being hard-closed and cannot enter governed reopen.");
            }

            if (retained.State == CloseScopeLockState.Reopened)
            {
                var prior = retained.History?.LastOrDefault()?.ReopenReceipt
                    ?? throw new InvalidDataException(
                        $"Reconciliation close scope '{scopeKey}' is reopened without retained reopen evidence.");
                if (!SameReopenCommand(prior, command, evidence))
                {
                    throw new InvalidOperationException(
                        $"Reconciliation close scope '{scopeKey}' was already reopened by a different governed command.");
                }

                return prior with { WasAlreadyReopened = true };
            }

            var previousReopen = retained.History?.LastOrDefault()?.ReopenReceipt;
            if (previousReopen is not null
                && command.ReopenedLedgerPeriodVersion <= previousReopen.ReopenedLedgerPeriodVersion)
            {
                throw new InvalidOperationException(
                    $"Reconciliation close scope '{scopeKey}' cannot reopen at ledger version {command.ReopenedLedgerPeriodVersion}; the prior governed reopen retained version {previousReopen.ReopenedLedgerPeriodVersion}.");
            }

            var reopenedAtUtc = DateTimeOffset.UtcNow;
            var receipt = new ReconciliationCloseScopeReopenReceipt(
                retained.Scope,
                retained.Generation,
                retained.CheckpointHashSha256,
                command.ReopenedLedgerPeriodVersion,
                command.Actor.Trim(),
                command.Role.Trim(),
                command.Reason.Trim(),
                command.ApprovalReference.Trim(),
                command.CorrelationId.Trim(),
                evidence,
                command.CommandHashSha256.Trim().ToLowerInvariant(),
                reopenedAtUtc);
            var history = (retained.History ?? [])
                .Append(new ReconciliationCloseScopeHistoryEntry(
                    retained.Scope,
                    retained.Generation,
                    retained.CheckpointHashSha256,
                    CloneCloseScopeCheckpointItems(retained.CheckpointItems!),
                    retained.UpdatedAtUtc,
                    receipt))
                .ToArray();
            var reopened = retained with
            {
                State = CloseScopeLockState.Reopened,
                UpdatedAtUtc = reopenedAtUtc,
                History = history
            };
            ValidateCloseScopeLock(reopened);
            _closeScopeLocks[scopeKey] = reopened;
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                _closeScopeLocks[scopeKey] = retained;
                throw;
            }

            return receipt;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReconciliationCloseScopeHistoryEntry>> ListCloseScopeHistoryAsync(
        ReconciliationCloseScope scope,
        CancellationToken ct = default)
    {
        ValidateCloseScope(scope);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);

            var scopeKey = BuildCloseScopeKey(scope);
            if (!_closeScopeLocks.TryGetValue(scopeKey, out var retained))
            {
                return [];
            }

            ValidateCloseScopeLock(retained);
            return (retained.History ?? [])
                .Select(CloneCloseScopeHistoryEntry)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private ReconciliationBreakQueueTransitionResult? ValidateCloseScopeMutation(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkCommand command)
    {
        var proposedScopeLock = command.CloseScope is null
            ? null
            : _closeScopeLocks.GetValueOrDefault(BuildCloseScopeKey(
                new ReconciliationCloseScope(
                    command.CloseScope.FundProfileId,
                    command.CloseScope.LedgerBookId,
                    command.CloseScope.AccountingPeriodId,
                    command.CloseScope.AsOfDate)));
        if (proposedScopeLock is { State: not CloseScopeLockState.Reopened })
        {
            return Invalid(
                item,
                $"Reconciliation case {item.BreakId} cannot bind its handoff to accounting period '{proposedScopeLock.Scope.AccountingPeriodId:D}' because that scope is {DescribeState(proposedScopeLock.State)}.",
                ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed,
                ["closeScope"],
                RequestedLifecycle(command));
        }

        var retained = FindCloseScopeLock(item);
        if (retained is null || retained.State == CloseScopeLockState.Reopened)
        {
            return null;
        }

        var state = DescribeState(retained.State);
        return Invalid(
            item,
            $"Reconciliation case {item.BreakId} cannot apply {command.Action} because accounting period '{retained.Scope.AccountingPeriodId:D}' is {state}. A committed ledger is never reopened through reconciliation casework.",
            ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed,
            ["accountingPeriodId"],
            RequestedLifecycle(command));
    }

    private void EnsureCloseScopeMutationAllowed(
        ReconciliationBreakQueueItem item,
        string operation)
    {
        var retained = FindCloseScopeLock(item);
        if (retained is null || retained.State == CloseScopeLockState.Reopened)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Reconciliation case '{item.BreakId}' cannot {operation} because accounting period '{retained.Scope.AccountingPeriodId:D}' is {DescribeState(retained.State)}.");
    }

    private CloseScopeLockRecord? FindCloseScopeLock(ReconciliationBreakQueueItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FundProfileId)
            || !item.LedgerBookId.HasValue
            || item.LedgerBookId.Value == Guid.Empty
            || !Guid.TryParse(item.AccountingPeriodId, out var accountingPeriodId)
            || accountingPeriodId == Guid.Empty
            || !item.AsOfDate.HasValue
            || item.AsOfDate.Value == default)
        {
            return null;
        }

        var scope = new ReconciliationCloseScope(
            item.FundProfileId,
            item.LedgerBookId.Value,
            accountingPeriodId,
            item.AsOfDate.Value);
        return _closeScopeLocks.GetValueOrDefault(BuildCloseScopeKey(scope));
    }

    private async Task CommitCloseScopeAsync(
        string scopeKey,
        string token,
        CancellationToken ct)
    {
        if (!_closeScopeLocks.TryGetValue(scopeKey, out var retained)
            || retained.State != CloseScopeLockState.Closing
            || !string.Equals(retained.Token, token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Reconciliation close-scope lease '{scopeKey}' is no longer current.");
        }

        ValidateCloseScopeLock(retained);
        _closeScopeLocks[scopeKey] = retained with
        {
            State = CloseScopeLockState.HardClosed,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        try
        {
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // The durable Closing state remains the recovery posture if sealing fails after the
            // ledger committed. The lease records that fact before this method is invoked.
            _closeScopeLocks[scopeKey] = retained;
            throw;
        }
    }

    private async Task AbandonCloseScopeAsync(
        string scopeKey,
        string token,
        CancellationToken ct)
    {
        if (!_closeScopeLocks.TryGetValue(scopeKey, out var retained)
            || retained.State != CloseScopeLockState.Closing
            || !string.Equals(retained.Token, token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Reconciliation close-scope lease '{scopeKey}' is no longer current.");
        }

        ValidateCloseScopeLock(retained);
        var priorGeneration = retained.History?.LastOrDefault();
        if (priorGeneration is null)
        {
            _closeScopeLocks.Remove(scopeKey);
        }
        else
        {
            _closeScopeLocks[scopeKey] = new CloseScopeLockRecord(
                scopeKey,
                priorGeneration.Scope,
                CloseScopeLockState.Reopened,
                Guid.NewGuid().ToString("N"),
                priorGeneration.CheckpointHashSha256,
                priorGeneration.ReopenReceipt.ReopenedAtUtc,
                CloneCloseScopeCheckpointItems(priorGeneration.Items),
                priorGeneration.CheckpointGeneration,
                retained.History);
        }
        try
        {
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _closeScopeLocks[scopeKey] = retained;
            throw;
        }
    }

    private void ReleaseCloseScopeOwnership() => _gate.Release();

    private static void ValidateCloseScope(ReconciliationCloseScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.FundProfileId);
        if (scope.LedgerBookId == Guid.Empty
            || scope.AccountingPeriodId == Guid.Empty
            || scope.AsOfDate == default)
        {
            throw new ArgumentException(
                "Fund, ledger-book, accounting-period, and as-of scope are required for a reconciliation close checkpoint.",
                nameof(scope));
        }
    }

    private void ValidateCloseScopeLock(CloseScopeLockRecord retained)
    {
        if (string.IsNullOrWhiteSpace(retained.ScopeKey)
            || string.IsNullOrWhiteSpace(retained.Token)
            || !Guid.TryParseExact(retained.Token, "N", out _)
            || !Sha256Digest.IsWellFormed(retained.CheckpointHashSha256)
            || retained.UpdatedAtUtc == default
            || retained.CheckpointItems is null
            || retained.Generation <= 0
            || !Enum.IsDefined(retained.State))
        {
            throw new InvalidDataException(
                "A retained reconciliation close-scope lock has invalid identity, state, or checkpoint evidence.");
        }

        ValidateCloseScope(retained.Scope);
        if (!string.Equals(retained.ScopeKey, BuildCloseScopeKey(retained.Scope), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{retained.ScopeKey}' does not match its retained scope.");
        }

        ValidateCloseScopeCheckpointItems(
            retained.ScopeKey,
            retained.Scope,
            retained.CheckpointItems);
        var expectedHash = ComputeCloseScopeCheckpointHash(
            retained.Scope,
            retained.CheckpointItems,
            retained.Generation);
        if (!string.Equals(
                retained.CheckpointHashSha256,
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{retained.ScopeKey}' failed checkpoint hash verification.");
        }

        var history = retained.History ?? [];
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            ValidateCloseScopeHistoryEntry(retained.ScopeKey, retained.Scope, entry);
            var expectedGeneration = index + 1L;
            if (entry.CheckpointGeneration != expectedGeneration)
            {
                throw new InvalidDataException(
                    $"Reconciliation close-scope lock '{retained.ScopeKey}' contains non-contiguous or out-of-order checkpoint history.");
            }

            if (index > 0
                && entry.ReopenReceipt.ReopenedLedgerPeriodVersion
                    <= history[index - 1].ReopenReceipt.ReopenedLedgerPeriodVersion)
            {
                throw new InvalidDataException(
                    $"Reconciliation close-scope lock '{retained.ScopeKey}' contains non-monotonic reopened ledger versions.");
            }
        }

        var latestHistory = history.LastOrDefault();
        if (retained.State == CloseScopeLockState.Reopened)
        {
            if (latestHistory is null
                || latestHistory.CheckpointGeneration != retained.Generation
                || history.Count != retained.Generation
                || !string.Equals(
                    latestHistory.CheckpointHashSha256,
                    retained.CheckpointHashSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Reconciliation close-scope lock '{retained.ScopeKey}' is reopened without matching immutable checkpoint history.");
            }
        }
        else if (history.Count != retained.Generation - 1
                 || latestHistory is not null && latestHistory.CheckpointGeneration >= retained.Generation)
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{retained.ScopeKey}' has history that is not older than its active generation.");
        }
    }

    private static string BuildCloseScopeKey(ReconciliationCloseScope scope)
        => string.Join(
            "|",
            scope.FundProfileId.Trim().ToLowerInvariant(),
            scope.LedgerBookId.ToString("D"),
            scope.AccountingPeriodId.ToString("D"),
            scope.AsOfDate.ToString("yyyy-MM-dd"));

    private static string DescribeState(CloseScopeLockState state)
        => state switch
        {
            CloseScopeLockState.HardClosed => "hard-closed",
            CloseScopeLockState.Reopened => "governed-reopened",
            _ => "being hard-closed"
        };

    private string ComputeCloseScopeCheckpointHash(
        ReconciliationCloseScope scope,
        IReadOnlyList<ReconciliationBreakQueueItem> items,
        long generation)
    {
        var ordered = items
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .ToArray();
        object checkpoint = generation == 1
            ? new
            {
                scope = BuildCloseScopeKey(scope),
                items = ordered
            }
            : new
            {
                scope = BuildCloseScopeKey(scope),
                generation,
                items = ordered
            };
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(checkpoint, _jsonOptions))))
            .ToLowerInvariant();
    }

    private static void ValidateCloseScopeReopenCommand(
        ReconciliationCloseScopeReopenCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Role);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ApprovalReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (!string.Equals(command.Role, "Controller", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command.Role, "Fund Controller", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Governed reconciliation checkpoint reopen requires Controller or Fund Controller authority.");
        }
        var evidence = NormalizeCloseScopeReopenEvidence(command.EvidenceLinks);
        if (command.ReopenedLedgerPeriodVersion <= 0
            || !Sha256Digest.IsWellFormed(command.CommandHashSha256)
            || evidence.Count == 0
            || !evidence.Any(link =>
                link.Contains(command.ApprovalReference.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Governed reconciliation checkpoint reopen requires a positive ledger version, exact command hash, and retained evidence linked to the approval reference.",
                nameof(command));
        }
    }

    private static IReadOnlyList<string> NormalizeCloseScopeReopenEvidence(
        IReadOnlyList<string> evidenceLinks)
        => (evidenceLinks ?? [])
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool SameReopenCommand(
        ReconciliationCloseScopeReopenReceipt retained,
        ReconciliationCloseScopeReopenCommand command,
        IReadOnlyList<string> evidence)
        => retained.ReopenedLedgerPeriodVersion == command.ReopenedLedgerPeriodVersion
            && string.Equals(retained.Actor, command.Actor.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(retained.Role, command.Role.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(retained.Reason, command.Reason.Trim(), StringComparison.Ordinal)
            && string.Equals(
                retained.ApprovalReference,
                command.ApprovalReference.Trim(),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                retained.CorrelationId,
                command.CorrelationId.Trim(),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                retained.CommandHashSha256,
                command.CommandHashSha256.Trim(),
                StringComparison.OrdinalIgnoreCase)
            && retained.EvidenceLinks.SequenceEqual(evidence, StringComparer.OrdinalIgnoreCase);

    private void ValidateCloseScopeHistoryEntry(
        string scopeKey,
        ReconciliationCloseScope scope,
        ReconciliationCloseScopeHistoryEntry entry)
    {
        if (entry is null
            || entry.Scope is null
            || entry.Items is null
            || entry.ReopenReceipt is null
            || entry.ReopenReceipt.Scope is null
            || entry.ReopenReceipt.EvidenceLinks is null
            || entry.CheckpointGeneration <= 0
            || entry.SealedAtUtc == default
            || !string.Equals(BuildCloseScopeKey(entry.Scope), scopeKey, StringComparison.Ordinal)
            || !string.Equals(BuildCloseScopeKey(entry.ReopenReceipt.Scope), scopeKey, StringComparison.Ordinal)
            || entry.ReopenReceipt.CheckpointGeneration != entry.CheckpointGeneration
            || !string.Equals(
                entry.ReopenReceipt.CheckpointHashSha256,
                entry.CheckpointHashSha256,
                StringComparison.OrdinalIgnoreCase)
            || entry.ReopenReceipt.WasAlreadyReopened
            || entry.ReopenReceipt.ReopenedAtUtc < entry.SealedAtUtc)
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{scopeKey}' contains invalid checkpoint history.");
        }

        ValidateCloseScopeCheckpointItems(scopeKey, scope, entry.Items);
        var expectedHash = ComputeCloseScopeCheckpointHash(
            scope,
            entry.Items,
            entry.CheckpointGeneration);
        if (!string.Equals(entry.CheckpointHashSha256, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{scopeKey}' contains history that failed checkpoint hash verification.");
        }

        var receipt = entry.ReopenReceipt;
        ValidateCloseScopeReopenCommand(new ReconciliationCloseScopeReopenCommand(
            receipt.Actor,
            receipt.Role,
            receipt.Reason,
            receipt.ApprovalReference,
            receipt.CorrelationId,
            receipt.EvidenceLinks,
            receipt.ReopenedLedgerPeriodVersion,
            receipt.CommandHashSha256));
        if (!receipt.EvidenceLinks.SequenceEqual(
                NormalizeCloseScopeReopenEvidence(receipt.EvidenceLinks),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{scopeKey}' contains non-canonical reopen evidence.");
        }
    }

    private static void ValidateCloseScopeCheckpointItems(
        string scopeKey,
        ReconciliationCloseScope scope,
        IReadOnlyList<ReconciliationBreakQueueItem> items)
    {
        if (items.Any(static item => string.IsNullOrWhiteSpace(item.BreakId))
            || items
                .GroupBy(static item => item.BreakId, StringComparer.OrdinalIgnoreCase)
                .Any(static group => group.Count() > 1)
            || items.Any(item =>
                !IsExactCloseScope(item, scope)
                && !IsUnscopedStatementCloseBlocker(item)))
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{scopeKey}' contains invalid, duplicate, or out-of-scope checkpoint items.");
        }
    }

    private ReconciliationBreakQueueItem[] FreezeCloseScopeCheckpointItems(
        ReconciliationCloseScope scope,
        IEnumerable<ReconciliationBreakQueueItem> items)
    {
        var ordered = items
            .Where(item =>
                IsExactCloseScope(item, scope)
                || IsUnscopedStatementCloseBlocker(item))
            .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
            .ToArray();
        var payload = JsonSerializer.Serialize(ordered, _jsonOptions);
        return JsonSerializer.Deserialize<ReconciliationBreakQueueItem[]>(payload, _jsonOptions)
            ?? throw new InvalidDataException(
                "The reconciliation close-scope checkpoint could not be frozen.");
    }

    private ReconciliationBreakQueueItem[] CloneCloseScopeCheckpointItems(
        IReadOnlyList<ReconciliationBreakQueueItem> items)
    {
        var payload = JsonSerializer.Serialize(items, _jsonOptions);
        return JsonSerializer.Deserialize<ReconciliationBreakQueueItem[]>(payload, _jsonOptions)
            ?? throw new InvalidDataException(
                "The retained reconciliation close-scope checkpoint could not be cloned.");
    }

    private ReconciliationCloseScopeHistoryEntry CloneCloseScopeHistoryEntry(
        ReconciliationCloseScopeHistoryEntry entry)
    {
        var payload = JsonSerializer.Serialize(entry, _jsonOptions);
        return JsonSerializer.Deserialize<ReconciliationCloseScopeHistoryEntry>(payload, _jsonOptions)
            ?? throw new InvalidDataException(
                "The retained reconciliation close-scope history could not be cloned.");
    }

    private static bool IsExactCloseScope(
        ReconciliationBreakQueueItem item,
        ReconciliationCloseScope scope)
        => item.LedgerBookId == scope.LedgerBookId
            && string.Equals(
                item.FundProfileId,
                scope.FundProfileId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                item.AccountingPeriodId,
                scope.AccountingPeriodId.ToString("D"),
                StringComparison.OrdinalIgnoreCase)
            && item.AsOfDate == scope.AsOfDate;

    private static bool IsUnscopedStatementCloseBlocker(ReconciliationBreakQueueItem item)
        => string.Equals(item.SourceType, "statement", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(item.FundProfileId)
                || !item.LedgerBookId.HasValue
                || item.LedgerBookId.Value == Guid.Empty
                || !Guid.TryParse(item.AccountingPeriodId, out var accountingPeriodId)
                || accountingPeriodId == Guid.Empty
                || !item.AsOfDate.HasValue
                || item.AsOfDate.Value == default);

    private ReconciliationCloseScopeCheckpoint CreateCloseScopeCheckpoint(
        CloseScopeLockRecord retained)
        => new(
            retained.Scope,
            CloneCloseScopeCheckpointItems(retained.CheckpointItems!),
            retained.CheckpointHashSha256,
            retained.Generation);

    private enum CloseScopeLockState : byte
    {
        Closing = 0,
        HardClosed = 1,
        Reopened = 2
    }

    private sealed record CloseScopeLockRecord(
        string ScopeKey,
        ReconciliationCloseScope Scope,
        CloseScopeLockState State,
        string Token,
        string CheckpointHashSha256,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<ReconciliationBreakQueueItem>? CheckpointItems = null,
        long Generation = 1,
        IReadOnlyList<ReconciliationCloseScopeHistoryEntry>? History = null);

    private sealed class CloseScopeLease(
        FileReconciliationBreakQueueRepository owner,
        string scopeKey,
        string token,
        ReconciliationCloseScope scope,
        IReadOnlyList<ReconciliationBreakQueueItem> items,
        string checkpointHashSha256,
        long generation,
        FileStream mutationLease) : IReconciliationCloseScopeLease
    {
        private bool _hardCloseCommitAttempted;
        private bool _checkpointSealed;
        private bool _checkpointAbandoned;
        private bool _disposed;

        public ReconciliationCloseScope Scope { get; } = scope;

        public IReadOnlyList<ReconciliationBreakQueueItem> Items { get; } = items;

        public string CheckpointHashSha256 { get; } = checkpointHashSha256;

        public long Generation { get; } = generation;

        public async Task CommitHardCloseAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_checkpointSealed)
            {
                return;
            }

            if (_checkpointAbandoned)
            {
                throw new InvalidOperationException(
                    "An abandoned reconciliation close-scope checkpoint cannot be sealed.");
            }

            _hardCloseCommitAttempted = true;
            await owner.CommitCloseScopeAsync(scopeKey, token, ct).ConfigureAwait(false);
            _checkpointSealed = true;
        }

        public async Task AbandonBeforeLedgerCommitAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_checkpointAbandoned)
            {
                return;
            }

            if (_hardCloseCommitAttempted || _checkpointSealed)
            {
                throw new InvalidOperationException(
                    "A reconciliation close-scope checkpoint cannot be abandoned after hard-close sealing was attempted.");
            }

            await owner.AbandonCloseScopeAsync(scopeKey, token, ct).ConfigureAwait(false);
            _checkpointAbandoned = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                await mutationLease.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                owner.ReleaseCloseScopeOwnership();
            }
        }
    }
}
