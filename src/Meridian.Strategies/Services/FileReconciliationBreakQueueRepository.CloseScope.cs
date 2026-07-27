using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            if (_closeScopeLocks.TryGetValue(scopeKey, out var retained))
            {
                if (retained.State == CloseScopeLockState.HardClosed)
                {
                    throw new InvalidOperationException(
                        $"Reconciliation close scope '{scopeKey}' is already sealed as hard-closed.");
                }

                // Holding the repository mutation file exclusively proves that the process which
                // retained this Closing token no longer owns the lease. Rotate the token to fence
                // any stale owner, but retain the exact hash-verified checkpoint. The caller must
                // re-read the authoritative ledger state before either resuming or abandoning it.
                checkpointItems = CloneCloseScopeCheckpointItems(retained.CheckpointItems!);
                checkpointHash = retained.CheckpointHashSha256;
                _closeScopeLocks[scopeKey] = retained with
                {
                    Token = token,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
            }
            else
            {
                checkpointItems = FreezeCloseScopeCheckpointItems(scope, _items!.Values);
                checkpointHash = ComputeCloseScopeCheckpointHash(scope, checkpointItems);
                _closeScopeLocks[scopeKey] = new CloseScopeLockRecord(
                    scopeKey,
                    scope,
                    CloseScopeLockState.Closing,
                    token,
                    checkpointHash,
                    DateTimeOffset.UtcNow,
                    checkpointItems);
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

            return CreateCloseScopeCheckpoint(retained);
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
        if (proposedScopeLock is not null)
        {
            return Invalid(
                item,
                $"Reconciliation case {item.BreakId} cannot bind its handoff to accounting period '{proposedScopeLock.Scope.AccountingPeriodId:D}' because that scope is {DescribeState(proposedScopeLock.State)}.",
                ReconciliationBreakQueueTransitionErrorCode.AccountingPeriodHardClosed,
                ["closeScope"],
                RequestedLifecycle(command));
        }

        var retained = FindCloseScopeLock(item);
        if (retained is null)
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
        if (retained is null)
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
        _closeScopeLocks.Remove(scopeKey);
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
            || !IsSha256(retained.CheckpointHashSha256)
            || retained.UpdatedAtUtc == default
            || retained.CheckpointItems is null
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

        if (retained.CheckpointItems.Any(static item => string.IsNullOrWhiteSpace(item.BreakId))
            || retained.CheckpointItems
                .GroupBy(static item => item.BreakId, StringComparer.OrdinalIgnoreCase)
                .Any(static group => group.Count() > 1)
            || retained.CheckpointItems.Any(item =>
                !IsExactCloseScope(item, retained.Scope)
                && !IsUnscopedStatementCloseBlocker(item)))
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{retained.ScopeKey}' contains invalid, duplicate, or out-of-scope checkpoint items.");
        }

        var expectedHash = ComputeCloseScopeCheckpointHash(
            retained.Scope,
            retained.CheckpointItems);
        if (!string.Equals(
                retained.CheckpointHashSha256,
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Reconciliation close-scope lock '{retained.ScopeKey}' failed checkpoint hash verification.");
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
        => state == CloseScopeLockState.HardClosed ? "hard-closed" : "being hard-closed";

    private string ComputeCloseScopeCheckpointHash(
        ReconciliationCloseScope scope,
        IReadOnlyList<ReconciliationBreakQueueItem> items)
    {
        var checkpoint = new
        {
            scope = BuildCloseScopeKey(scope),
            items = items
                .OrderBy(static item => item.BreakId, StringComparer.Ordinal)
                .ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(checkpoint, _jsonOptions))))
            .ToLowerInvariant();
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
            retained.CheckpointHashSha256);

    private enum CloseScopeLockState : byte
    {
        Closing = 0,
        HardClosed = 1
    }

    private sealed record CloseScopeLockRecord(
        string ScopeKey,
        ReconciliationCloseScope Scope,
        CloseScopeLockState State,
        string Token,
        string CheckpointHashSha256,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<ReconciliationBreakQueueItem>? CheckpointItems = null);

    private sealed class CloseScopeLease(
        FileReconciliationBreakQueueRepository owner,
        string scopeKey,
        string token,
        ReconciliationCloseScope scope,
        IReadOnlyList<ReconciliationBreakQueueItem> items,
        string checkpointHashSha256,
        FileStream mutationLease) : IReconciliationCloseScopeLease
    {
        private bool _hardCloseCommitAttempted;
        private bool _checkpointSealed;
        private bool _checkpointAbandoned;
        private bool _disposed;

        public ReconciliationCloseScope Scope { get; } = scope;

        public IReadOnlyList<ReconciliationBreakQueueItem> Items { get; } = items;

        public string CheckpointHashSha256 { get; } = checkpointHashSha256;

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
