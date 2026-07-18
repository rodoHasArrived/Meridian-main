using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Execution.Events;

/// <summary>
/// Durable identity for one accounting handoff scope. Legacy label-only identities remain valid
/// for existing local stores; production composition should use <see cref="FromContext"/> so a
/// reused label cannot cross an aggregate, period, or ledger-book boundary.
/// </summary>
public sealed record TradeFillPostingScopeIdentity(
    string PostingScope,
    Guid? AggregateId = null,
    Guid? PeriodId = null,
    Guid? LedgerBookId = null)
{
    /// <summary>Accounting policy identifier included in an exact posting identity.</summary>
    public string? AccountingPolicyId { get; init; }

    /// <summary>Accounting policy version included in an exact posting identity.</summary>
    public string? AccountingPolicyVersion { get; init; }

    [JsonIgnore]
    public bool IsExact => AggregateId.HasValue
                           && PeriodId.HasValue
                           && LedgerBookId.HasValue
                           && !string.IsNullOrWhiteSpace(AccountingPolicyId)
                           && !string.IsNullOrWhiteSpace(AccountingPolicyVersion);

    public static TradeFillPostingScopeIdentity FromContext(TradeFillLedgerPostingContext postingContext)
    {
        ArgumentNullException.ThrowIfNull(postingContext);
        var validated = postingContext.Validate();
        return new TradeFillPostingScopeIdentity(
            validated.PostingScope,
            validated.AggregateId,
            validated.PeriodId,
            validated.LedgerBookId)
        {
            AccountingPolicyId = validated.AccountingPolicyId,
            AccountingPolicyVersion = validated.AccountingPolicyVersion
        };
    }

    public TradeFillPostingScopeIdentity Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PostingScope);
        var hasAnyIdentifier = AggregateId.HasValue
                               || PeriodId.HasValue
                               || LedgerBookId.HasValue
                               || AccountingPolicyId is not null
                               || AccountingPolicyVersion is not null;
        if (hasAnyIdentifier && !IsExact)
        {
            throw new ArgumentException(
                "A trade-fill scope identity must provide aggregate, period, ledger-book, accounting-policy id, and accounting-policy version together.");
        }
        if (IsExact
            && (AggregateId == Guid.Empty || PeriodId == Guid.Empty || LedgerBookId == Guid.Empty))
        {
            throw new ArgumentException("Trade-fill scope identifiers must be non-empty.");
        }

        return this with
        {
            PostingScope = PostingScope.Trim(),
            AccountingPolicyId = AccountingPolicyId?.Trim(),
            AccountingPolicyVersion = AccountingPolicyVersion?.Trim()
        };
    }
}

/// <summary>Configuration for the OMS-level last-resort accounting handoff state.</summary>
public sealed record TradeFillHandoffFailureStoreOptions(string RootDirectory, string PostingScope)
{
    public TradeFillHandoffFailureStoreOptions(
        string rootDirectory,
        TradeFillLedgerPostingContext postingContext)
        : this(
            rootDirectory,
            (postingContext ?? throw new ArgumentNullException(nameof(postingContext))).PostingScope)
    {
        ScopeIdentity = TradeFillPostingScopeIdentity.FromContext(postingContext);
    }

    public TradeFillPostingScopeIdentity ScopeIdentity { get; init; } = new(PostingScope);

    public string ScopeDirectory
    {
        get
        {
            var scopeKey = new TradeFillPostingStoreOptions(RootDirectory, PostingScope).ScopeStorageKey;
            return Path.Combine(RootDirectory, "handoff-failures", "scopes", scopeKey);
        }
    }

    public string SnapshotPath => Path.Combine(ScopeDirectory, "pending.snapshot.json");

    /// <summary>
    /// Persistent lock-file path used to coordinate independent store instances and processes.
    /// The file remains on disk; ownership is the lifetime of an exclusive file handle.
    /// </summary>
    public string LockPath => Path.Combine(ScopeDirectory, "pending.snapshot.lock");
}

/// <summary>Fill that the primary accounting publisher could not durably accept.</summary>
public sealed record RetainedTradeFillHandoffFailure(
    TradeExecutedEvent TradeEvent,
    DateTimeOffset RetainedAtUtc,
    int FailureCount,
    string LastFailure,
    DateTimeOffset LastAttemptAtUtc);

/// <summary>
/// Independent last-resort durable state for publisher acceptance failures. It is intentionally
/// separate from the primary posting WAL/snapshot so an outage in that path can be surfaced and
/// replayed rather than hidden in OMS process memory.
/// </summary>
public interface ITradeFillHandoffFailureStore : IAsyncDisposable
{
    /// <summary>The exact ledger posting scope whose rejected fills are retained.</summary>
    string PostingScope { get; }

    /// <summary>
    /// Exact accounting identity when the store was constructed from a posting context. Existing
    /// custom implementations remain label-only until they opt into the identifier fields.
    /// </summary>
    TradeFillPostingScopeIdentity ScopeIdentity => new(PostingScope);

    Task RetainAsync(
        TradeExecutedEvent tradeEvent,
        string failure,
        CancellationToken ct = default);

    Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> LoadPendingAsync(
        CancellationToken ct = default);

    Task MarkReplayedAsync(Guid fillId, CancellationToken ct = default);
}

/// <summary>Atomic snapshot implementation of the independent OMS handoff-failure state.</summary>
public sealed class AtomicTradeFillHandoffFailureStore : ITradeFillHandoffFailureStore
{
    private const int LegacySnapshotVersion = 1;
    private const int ScopeWithoutPolicySnapshotVersion = 2;
    private const int SnapshotVersion = 3;
    private const int MaximumFailureLength = 4_096;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(20);

    private readonly TradeFillHandoffFailureStoreOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, RetainedTradeFillHandoffFailure> _pending = [];
    private readonly object _lifecycleSync = new();
    private Task? _disposeTask;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private bool _disposeStarted;

    public AtomicTradeFillHandoffFailureStore(TradeFillHandoffFailureStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
            throw new ArgumentException("A durable handoff-failure root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.PostingScope))
            throw new ArgumentException("A posting scope is required.", nameof(options));
        var scopeIdentity = (options.ScopeIdentity
                             ?? throw new ArgumentException("A handoff-failure scope identity is required.", nameof(options)))
            .Validate();
        var postingScope = options.PostingScope.Trim();
        if (!string.Equals(scopeIdentity.PostingScope, postingScope, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The handoff-failure scope identity label must match the configured posting scope.",
                nameof(options));
        }
        _options = options with
        {
            RootDirectory = Path.GetFullPath(options.RootDirectory.Trim()),
            PostingScope = postingScope,
            ScopeIdentity = scopeIdentity
        };
    }

    public string PostingScope => _options.PostingScope;

    public TradeFillPostingScopeIdentity ScopeIdentity => _options.ScopeIdentity;

    public async Task RetainAsync(
        TradeExecutedEvent tradeEvent,
        string failure,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);
        if (tradeEvent.FillId == Guid.Empty)
            throw new ArgumentException("A durable fill id is required.", nameof(tradeEvent));
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        using var operation = EnterOperation();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scopeLock = await AcquireScopeLockAsync(ct).ConfigureAwait(false);
            await ReloadPendingUnderLockAsync(ct).ConfigureAwait(false);

            var normalizedFailure = failure.Trim();
            if (normalizedFailure.Length > MaximumFailureLength)
                normalizedFailure = normalizedFailure[..MaximumFailureLength];
            var now = DateTimeOffset.UtcNow;
            var hadPrevious = _pending.TryGetValue(tradeEvent.FillId, out var previous);
            if (hadPrevious && previous!.TradeEvent != tradeEvent)
            {
                throw new InvalidOperationException(
                    $"Fill '{tradeEvent.FillId:D}' cannot retain different accounting economics.");
            }

            _pending[tradeEvent.FillId] = previous is null
                ? new RetainedTradeFillHandoffFailure(tradeEvent, now, 1, normalizedFailure, now)
                : previous with
                {
                    FailureCount = previous.FailureCount + 1,
                    LastFailure = normalizedFailure,
                    LastAttemptAtUtc = now
                };
            try
            {
                await PersistAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                if (hadPrevious)
                    _pending[tradeEvent.FillId] = previous!;
                else
                    _pending.Remove(tradeEvent.FillId);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> LoadPendingAsync(
        CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scopeLock = await AcquireScopeLockAsync(ct).ConfigureAwait(false);
            await ReloadPendingUnderLockAsync(ct).ConfigureAwait(false);
            return _pending.Values
                .OrderBy(static failure => failure.RetainedAtUtc)
                .ThenBy(static failure => failure.TradeEvent.FillId)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkReplayedAsync(Guid fillId, CancellationToken ct = default)
    {
        if (fillId == Guid.Empty)
            throw new ArgumentException("A durable fill id is required.", nameof(fillId));
        using var operation = EnterOperation();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scopeLock = await AcquireScopeLockAsync(ct).ConfigureAwait(false);
            await ReloadPendingUnderLockAsync(ct).ConfigureAwait(false);
            if (!_pending.Remove(fillId, out var retained))
                return;
            try
            {
                await PersistAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                _pending.Add(fillId, retained);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleSync)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            _disposeStarted = true;
            var operationsDrained = _activeOperations == 0
                ? Task.CompletedTask
                : (_operationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            _disposeTask = DisposeCoreAsync(operationsDrained);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task operationsDrained)
    {
        await Task.Yield();
        await operationsDrained.ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task<FileStream> AcquireScopeLockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_options.ScopeDirectory);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _options.LockPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.OpenOrCreate,
                        Access = FileAccess.ReadWrite,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous,
                        BufferSize = 1
                    });
            }
            catch (IOException)
            {
                // FileShare.None is enforced across processes. A persistent lock file therefore
                // needs no stale-lock cleanup: process termination releases the owning handle.
                await Task.Delay(LockRetryDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ReloadPendingUnderLockAsync(CancellationToken ct)
    {
        _pending.Clear();
        if (!File.Exists(_options.SnapshotPath))
            return;

        TradeFillHandoffFailureSnapshot snapshot;
        try
        {
            var json = await File.ReadAllTextAsync(_options.SnapshotPath, ct)
                .ConfigureAwait(false);
            snapshot = JsonSerializer.Deserialize(
                           json,
                           ExecutionJsonContext.Default.TradeFillHandoffFailureSnapshot)
                       ?? throw new InvalidDataException("Trade-fill handoff-failure snapshot is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Trade-fill handoff-failure snapshot is invalid.", ex);
        }

        if (snapshot.Version is not LegacySnapshotVersion
            and not ScopeWithoutPolicySnapshotVersion
            and not SnapshotVersion)
            throw new InvalidDataException($"Unsupported handoff-failure snapshot version {snapshot.Version}.");
        if ((snapshot.Version is ScopeWithoutPolicySnapshotVersion or SnapshotVersion)
            && snapshot.ScopeIdentity is null)
        {
            throw new InvalidDataException("Trade-fill handoff-failure snapshot has no durable scope identity state.");
        }

        TradeFillPostingScopeIdentity persistedScopeIdentity;
        try
        {
            persistedScopeIdentity = (snapshot.ScopeIdentity
                                      ?? new TradeFillPostingScopeIdentity(snapshot.PostingScope))
                .Validate();
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Trade-fill handoff-failure snapshot scope identity is invalid.", ex);
        }

        if (!string.Equals(snapshot.PostingScope, persistedScopeIdentity.PostingScope, StringComparison.Ordinal)
            || persistedScopeIdentity != ScopeIdentity)
        {
            throw new InvalidDataException(
                $"Trade-fill handoff-failure snapshot scope '{persistedScopeIdentity}' does not match configured scope '{ScopeIdentity}'.");
        }
        if (snapshot.Pending is null)
            throw new InvalidDataException("Trade-fill handoff-failure snapshot has no pending state.");

        foreach (var failure in snapshot.Pending)
        {
            if (failure is null
                || failure.TradeEvent is null
                || failure.TradeEvent.FillId == Guid.Empty
                || !_pending.TryAdd(failure.TradeEvent.FillId, failure))
            {
                throw new InvalidDataException("Trade-fill handoff-failure snapshot has invalid fill identity state.");
            }
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var snapshot = new TradeFillHandoffFailureSnapshot(
            SnapshotVersion,
            _options.PostingScope,
            _pending.Values
                .OrderBy(static failure => failure.RetainedAtUtc)
                .ThenBy(static failure => failure.TradeEvent.FillId)
                .ToArray(),
            ScopeIdentity);
        var json = JsonSerializer.Serialize(
            snapshot,
            ExecutionJsonContext.Default.TradeFillHandoffFailureSnapshot);
        await AtomicFileWriter.WriteAsync(_options.SnapshotPath, json, ct).ConfigureAwait(false);
    }

    private OperationLease EnterOperation()
    {
        lock (_lifecycleSync)
        {
            if (_disposeStarted)
                throw new ObjectDisposedException(nameof(AtomicTradeFillHandoffFailureStore));
            _activeOperations++;
            return new OperationLease(this);
        }
    }

    private void ExitOperation()
    {
        TaskCompletionSource? drained = null;
        lock (_lifecycleSync)
        {
            _activeOperations--;
            if (_disposeStarted && _activeOperations == 0)
                drained = _operationsDrained;
        }
        drained?.TrySetResult();
    }

    private sealed class OperationLease(AtomicTradeFillHandoffFailureStore owner) : IDisposable
    {
        private AtomicTradeFillHandoffFailureStore? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.ExitOperation();
    }
}

internal sealed record TradeFillHandoffFailureSnapshot(
    int Version,
    string PostingScope,
    IReadOnlyList<RetainedTradeFillHandoffFailure> Pending,
    TradeFillPostingScopeIdentity? ScopeIdentity = null);
