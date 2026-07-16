using System.Text.Json;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Execution.Events;

/// <summary>Configuration for the OMS-level last-resort accounting handoff state.</summary>
public sealed record TradeFillHandoffFailureStoreOptions(string RootDirectory, string PostingScope)
{
    public string SnapshotPath
    {
        get
        {
            var scopeKey = new TradeFillPostingStoreOptions(RootDirectory, PostingScope).ScopeStorageKey;
            return Path.Combine(RootDirectory, "handoff-failures", "scopes", scopeKey, "pending.snapshot.json");
        }
    }
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
    private const int SnapshotVersion = 1;
    private const int MaximumFailureLength = 4_096;

    private readonly TradeFillHandoffFailureStoreOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, RetainedTradeFillHandoffFailure> _pending = [];
    private readonly object _initializationSync = new();
    private Task? _initializationTask;
    private int _disposed;

    public AtomicTradeFillHandoffFailureStore(TradeFillHandoffFailureStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
            throw new ArgumentException("A durable handoff-failure root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.PostingScope))
            throw new ArgumentException("A posting scope is required.", nameof(options));
        _options = options with
        {
            RootDirectory = Path.GetFullPath(options.RootDirectory.Trim()),
            PostingScope = options.PostingScope.Trim()
        };
    }

    public string PostingScope => _options.PostingScope;

    public async Task RetainAsync(
        TradeExecutedEvent tradeEvent,
        string failure,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);
        if (tradeEvent.FillId == Guid.Empty)
            throw new ArgumentException("A durable fill id is required.", nameof(tradeEvent));
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        ThrowIfDisposed();
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
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
        ThrowIfDisposed();
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
        ThrowIfDisposed();
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        Task task;
        lock (_initializationSync)
        {
            ThrowIfDisposed();
            _initializationTask ??= InitializeCoreAsync();
            task = _initializationTask;
        }
        await task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync()
    {
        if (!File.Exists(_options.SnapshotPath))
            return;
        try
        {
            var json = await File.ReadAllTextAsync(_options.SnapshotPath, CancellationToken.None)
                .ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize(
                               json,
                               ExecutionJsonContext.Default.TradeFillHandoffFailureSnapshot)
                           ?? throw new InvalidDataException("Trade-fill handoff-failure snapshot is empty.");
            if (snapshot.Version != SnapshotVersion)
                throw new InvalidDataException($"Unsupported handoff-failure snapshot version {snapshot.Version}.");
            if (!string.Equals(snapshot.PostingScope, _options.PostingScope, StringComparison.Ordinal))
                throw new InvalidDataException("Trade-fill handoff-failure snapshot scope does not match configuration.");
            foreach (var failure in snapshot.Pending)
            {
                if (failure.TradeEvent.FillId == Guid.Empty
                    || !_pending.TryAdd(failure.TradeEvent.FillId, failure))
                {
                    throw new InvalidDataException("Trade-fill handoff-failure snapshot has invalid fill identity state.");
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Trade-fill handoff-failure snapshot is invalid.", ex);
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
                .ToArray());
        var json = JsonSerializer.Serialize(
            snapshot,
            ExecutionJsonContext.Default.TradeFillHandoffFailureSnapshot);
        await AtomicFileWriter.WriteAsync(_options.SnapshotPath, json, ct).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(AtomicTradeFillHandoffFailureStore));
    }
}

internal sealed record TradeFillHandoffFailureSnapshot(
    int Version,
    string PostingScope,
    IReadOnlyList<RetainedTradeFillHandoffFailure> Pending);
