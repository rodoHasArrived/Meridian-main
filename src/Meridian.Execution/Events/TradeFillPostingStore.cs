using System.Runtime.ExceptionServices;
using System.Text.Json;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Events;

/// <summary>Configuration for the durable trade-fill-to-ledger handoff.</summary>
public sealed record TradeFillPostingStoreOptions(string RootDirectory, string PostingScope)
{
    public string WalDirectory => Path.Combine(RootDirectory, "wal");
}

/// <summary>
/// One accepted fill that remains pending until its corresponding ledger journals have posted.
/// </summary>
public sealed record PendingTradeFillPosting(
    long StoreSequence,
    string PostingScope,
    TradeExecutedEvent TradeEvent,
    DateTimeOffset AcceptedAtUtc,
    int FailureCount = 0,
    string? LastFailure = null,
    DateTimeOffset? LastAttemptAtUtc = null);

/// <summary>Result of accepting a fill into the durable handoff.</summary>
public sealed record TradeFillPostingAcceptance(
    PendingTradeFillPosting? Posting,
    bool ShouldEnqueue,
    bool WasAlreadyPosted);

/// <summary>
/// Durable pending/acknowledgement boundary for fill-to-ledger processing.
/// A successful accept must survive restart; acknowledgement is allowed only after posting.
/// </summary>
public interface ITradeFillPostingStore : IAsyncDisposable
{
    string PostingScope { get; }

    Task<TradeFillPostingAcceptance> AcceptAsync(
        TradeExecutedEvent tradeEvent,
        CancellationToken ct = default);

    Task<IReadOnlyList<PendingTradeFillPosting>> LoadPendingAsync(CancellationToken ct = default);

    Task MarkPostedAsync(Guid fillId, CancellationToken ct = default);

    Task RecordFailureAsync(Guid fillId, string failure, CancellationToken ct = default);
}

/// <summary>
/// Execution-owned fill handoff backed by Meridian's write-ahead log. Pending, failure, and
/// per-fill acknowledgement records are append-only so one failed fill cannot be accidentally
/// committed by a later successful fill.
/// </summary>
public sealed class WalTradeFillPostingStore : ITradeFillPostingStore
{
    private const string PendingRecordType = "TradeFillPending";
    private const string FailureRecordType = "TradeFillFailure";
    private const string PostedRecordType = "TradeFillPosted";
    private const int MaximumFailureLength = 4_096;

    private readonly WriteAheadLog _wal;
    private readonly ILogger<WalTradeFillPostingStore> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _initializationSync = new();
    private readonly object _disposeSync = new();
    private readonly Dictionary<Guid, PendingTradeFillPosting> _pending = [];
    private readonly HashSet<Guid> _posted = [];
    private readonly Dictionary<Guid, TradeExecutedEvent> _acceptedEvents = [];
    private Task? _initializationTask;
    private Task? _disposeTask;
    private int _disposeStarted;

    public WalTradeFillPostingStore(
        TradeFillPostingStoreOptions options,
        ILogger<WalTradeFillPostingStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
            throw new ArgumentException("A durable trade-fill store root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.PostingScope))
            throw new ArgumentException("A ledger posting scope is required.", nameof(options));

        PostingScope = options.PostingScope.Trim();
        _wal = new WriteAheadLog(
            options.WalDirectory,
            new WalOptions
            {
                SyncMode = WalSyncMode.EveryWrite,
                ArchiveAfterTruncate = false,
                MaxWalFileAge = TimeSpan.FromDays(1),
                MaxWalFileSizeBytes = 5 * 1024 * 1024,
                CorruptionMode = WalCorruptionMode.Halt
            });
    }

    public string PostingScope { get; }

    public async Task<TradeFillPostingAcceptance> AcceptAsync(
        TradeExecutedEvent tradeEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);
        if (tradeEvent.FillId == Guid.Empty)
            throw new ArgumentException("A durable fill id is required.", nameof(tradeEvent));

        ThrowIfDisposing();
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            if (_acceptedEvents.TryGetValue(tradeEvent.FillId, out var acceptedEvent)
                && acceptedEvent != tradeEvent)
            {
                throw new InvalidOperationException(
                    $"Fill '{tradeEvent.FillId:D}' was replayed with different economic content.");
            }
            if (_posted.Contains(tradeEvent.FillId))
            {
                return new TradeFillPostingAcceptance(null, ShouldEnqueue: false, WasAlreadyPosted: true);
            }

            if (_pending.TryGetValue(tradeEvent.FillId, out var retained))
            {
                return new TradeFillPostingAcceptance(retained, ShouldEnqueue: false, WasAlreadyPosted: false);
            }

            var acceptedAtUtc = DateTimeOffset.UtcNow;
            var payload = new TradeFillPendingWalPayload(PostingScope, tradeEvent, acceptedAtUtc);
            var json = JsonSerializer.Serialize(
                payload,
                ExecutionJsonContext.Default.TradeFillPendingWalPayload);
            var walRecord = await _wal.AppendAsync(json, PendingRecordType, ct).ConfigureAwait(false);
            var posting = new PendingTradeFillPosting(
                walRecord.Sequence,
                PostingScope,
                tradeEvent,
                acceptedAtUtc);
            _pending.Add(tradeEvent.FillId, posting);
            _acceptedEvents.Add(tradeEvent.FillId, tradeEvent);
            return new TradeFillPostingAcceptance(posting, ShouldEnqueue: true, WasAlreadyPosted: false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<PendingTradeFillPosting>> LoadPendingAsync(CancellationToken ct = default)
    {
        ThrowIfDisposing();
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            return _pending.Values
                .OrderBy(static posting => posting.StoreSequence)
                .ToArray();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task MarkPostedAsync(Guid fillId, CancellationToken ct = default)
    {
        if (fillId == Guid.Empty)
            throw new ArgumentException("A durable fill id is required.", nameof(fillId));

        ThrowIfDisposing();
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            if (_posted.Contains(fillId))
                return;
            if (!_pending.ContainsKey(fillId))
                throw new InvalidOperationException($"Fill '{fillId:D}' is not pending and cannot be acknowledged.");

            var payload = new TradeFillStatusWalPayload(PostingScope, fillId, DateTimeOffset.UtcNow, null);
            var json = JsonSerializer.Serialize(
                payload,
                ExecutionJsonContext.Default.TradeFillStatusWalPayload);
            await _wal.AppendAsync(json, PostedRecordType, ct).ConfigureAwait(false);
            _pending.Remove(fillId);
            _posted.Add(fillId);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RecordFailureAsync(Guid fillId, string failure, CancellationToken ct = default)
    {
        if (fillId == Guid.Empty)
            throw new ArgumentException("A durable fill id is required.", nameof(fillId));
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);

        ThrowIfDisposing();
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposing();
            if (_posted.Contains(fillId))
                return;
            if (!_pending.TryGetValue(fillId, out var posting))
                throw new InvalidOperationException($"Fill '{fillId:D}' is not pending and cannot record a failure.");

            var occurredAtUtc = DateTimeOffset.UtcNow;
            var normalizedFailure = failure.Trim();
            if (normalizedFailure.Length > MaximumFailureLength)
                normalizedFailure = normalizedFailure[..MaximumFailureLength];
            var payload = new TradeFillStatusWalPayload(
                PostingScope,
                fillId,
                occurredAtUtc,
                normalizedFailure);
            var json = JsonSerializer.Serialize(
                payload,
                ExecutionJsonContext.Default.TradeFillStatusWalPayload);
            await _wal.AppendAsync(json, FailureRecordType, ct).ConfigureAwait(false);
            _pending[fillId] = posting with
            {
                FailureCount = posting.FailureCount + 1,
                LastFailure = normalizedFailure,
                LastAttemptAtUtc = occurredAtUtc
            };
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            Interlocked.Exchange(ref _disposeStarted, 1);
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        Task initializationTask;
        lock (_initializationSync)
        {
            ThrowIfDisposing();
            _initializationTask ??= InitializeCoreAsync();
            initializationTask = _initializationTask;
        }

        await initializationTask.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync()
    {
        await _wal.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        await foreach (var record in _wal.GetUncommittedRecordsAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (record.RecordType == PendingRecordType)
            {
                var payload = DeserializePending(record);
                EnsureScope(payload.PostingScope);
                if (payload.TradeEvent.FillId == Guid.Empty)
                    throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} has an empty fill id.");
                if (_acceptedEvents.TryGetValue(payload.TradeEvent.FillId, out var acceptedEvent)
                    && acceptedEvent != payload.TradeEvent)
                {
                    throw new InvalidDataException(
                        $"Trade-fill WAL contains conflicting economics for fill '{payload.TradeEvent.FillId:D}'.");
                }
                _acceptedEvents.TryAdd(payload.TradeEvent.FillId, payload.TradeEvent);
                if (_posted.Contains(payload.TradeEvent.FillId))
                    continue;
                if (_pending.ContainsKey(payload.TradeEvent.FillId))
                    continue;

                _pending.Add(payload.TradeEvent.FillId, new PendingTradeFillPosting(
                    record.Sequence,
                    PostingScope,
                    payload.TradeEvent,
                    payload.AcceptedAtUtc));
                continue;
            }

            if (record.RecordType is FailureRecordType or PostedRecordType)
            {
                var payload = DeserializeStatus(record);
                EnsureScope(payload.PostingScope);
                if (payload.FillId == Guid.Empty || !_acceptedEvents.ContainsKey(payload.FillId))
                {
                    throw new InvalidDataException(
                        $"Trade-fill WAL status record {record.Sequence} does not reference an accepted fill.");
                }
                if (record.RecordType == PostedRecordType)
                {
                    if (payload.Failure is not null)
                        throw new InvalidDataException($"Trade-fill acknowledgement record {record.Sequence} contains failure data.");
                    _pending.Remove(payload.FillId);
                    _posted.Add(payload.FillId);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(payload.Failure))
                    throw new InvalidDataException($"Trade-fill failure record {record.Sequence} has no failure detail.");

                if (_pending.TryGetValue(payload.FillId, out var posting))
                {
                    _pending[payload.FillId] = posting with
                    {
                        FailureCount = posting.FailureCount + 1,
                        LastFailure = payload.Failure,
                        LastAttemptAtUtc = payload.OccurredAtUtc
                    };
                }
            }
        }

        _logger.LogInformation(
            "Recovered {PendingCount} pending trade fill(s) for ledger posting scope {PostingScope}",
            _pending.Count,
            PostingScope);
    }

    private async Task DisposeCoreAsync()
    {
        Task? initializationTask;
        lock (_initializationSync)
        {
            initializationTask = _initializationTask;
        }

        Exception? initializationFailure = null;
        if (initializationTask is not null)
        {
            try
            {
                await initializationTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                initializationFailure = ex;
            }
        }

        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _wal.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }

        if (initializationFailure is not null)
            ExceptionDispatchInfo.Capture(initializationFailure).Throw();
    }

    private TradeFillPendingWalPayload DeserializePending(WalRecord record)
    {
        var json = record.DeserializePayload<string>()
            ?? throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} has no payload.");
        try
        {
            return JsonSerializer.Deserialize(
                       json,
                       ExecutionJsonContext.Default.TradeFillPendingWalPayload)
                   ?? throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} is invalid.", ex);
        }
    }

    private TradeFillStatusWalPayload DeserializeStatus(WalRecord record)
    {
        var json = record.DeserializePayload<string>()
            ?? throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} has no payload.");
        try
        {
            return JsonSerializer.Deserialize(
                       json,
                       ExecutionJsonContext.Default.TradeFillStatusWalPayload)
                   ?? throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} is invalid.", ex);
        }
    }

    private void EnsureScope(string retainedScope)
    {
        if (!string.Equals(retainedScope, PostingScope, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Trade-fill WAL scope '{retainedScope}' does not match configured posting scope '{PostingScope}'.");
        }
    }

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(WalTradeFillPostingStore));
    }
}

internal sealed record TradeFillPendingWalPayload(
    string PostingScope,
    TradeExecutedEvent TradeEvent,
    DateTimeOffset AcceptedAtUtc);

internal sealed record TradeFillStatusWalPayload(
    string PostingScope,
    Guid FillId,
    DateTimeOffset OccurredAtUtc,
    string? Failure);
