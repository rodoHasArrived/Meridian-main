using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Events;

/// <summary>Configuration for the durable trade-fill-to-ledger handoff.</summary>
public sealed record TradeFillPostingStoreOptions(string RootDirectory, string PostingScope)
{
    public TradeFillPostingStoreOptions(
        string rootDirectory,
        TradeFillLedgerPostingContext postingContext)
        : this(
            rootDirectory,
            (postingContext ?? throw new ArgumentNullException(nameof(postingContext))).PostingScope)
    {
        ScopeIdentity = TradeFillPostingScopeIdentity.FromContext(postingContext);
    }

    /// <summary>
    /// Complete accounting identity for exact construction. The positional scope-only constructor
    /// remains an explicitly legacy label-only mode for existing local stores.
    /// </summary>
    public TradeFillPostingScopeIdentity ScopeIdentity { get; init; } = new(PostingScope);

    public int CompactionRecordThreshold { get; init; } = 128;

    public long MaxWalFileSizeBytes { get; init; } = 512 * 1024;

    public int MaxRememberedPostedFillIds { get; init; } = 4_096;

    /// <summary>
    /// Stable partition for one exact posting scope. A book/period rollover therefore opens a new
    /// WAL rather than attempting to interpret a prior period's records under a new scope.
    /// </summary>
    public string ScopeStorageKey => BuildScopeStorageKey(PostingScope);

    public string ScopeDirectory => Path.Combine(RootDirectory, "scopes", ScopeStorageKey);

    public string WalDirectory => Path.Combine(ScopeDirectory, "wal");

    public string SnapshotPath => Path.Combine(ScopeDirectory, "pending.snapshot.json");

    internal Func<string, string, CancellationToken, Task<WalRecord>>? WalAppendOverride { get; init; }

    internal Func<string, CancellationToken, Task>? SnapshotWriteOverride { get; init; }

    internal TradeFillPostingStoreOptions Validate()
    {
        if (string.IsNullOrWhiteSpace(RootDirectory))
            throw new ArgumentException("A durable trade-fill store root is required.", nameof(RootDirectory));
        if (string.IsNullOrWhiteSpace(PostingScope))
            throw new ArgumentException("A ledger posting scope is required.", nameof(PostingScope));
        if (CompactionRecordThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(CompactionRecordThreshold));
        if (MaxWalFileSizeBytes < 1_024)
            throw new ArgumentOutOfRangeException(nameof(MaxWalFileSizeBytes));
        if (MaxRememberedPostedFillIds <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRememberedPostedFillIds));

        var scopeIdentity = (ScopeIdentity
                             ?? throw new ArgumentException("A trade-fill posting scope identity is required.", nameof(ScopeIdentity)))
            .Validate();
        var postingScope = PostingScope.Trim();
        if (!string.Equals(scopeIdentity.PostingScope, postingScope, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The trade-fill posting scope identity label must match the configured posting scope.",
                nameof(ScopeIdentity));
        }

        return this with
        {
            RootDirectory = Path.GetFullPath(RootDirectory.Trim()),
            PostingScope = postingScope,
            ScopeIdentity = scopeIdentity
        };
    }

    private static string BuildScopeStorageKey(string postingScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postingScope);
        var normalized = postingScope.Trim();
        var readable = new string(normalized
            .Select(static character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '_')
            .Take(48)
            .ToArray());
        if (string.IsNullOrWhiteSpace(readable))
            readable = "scope";
        // Deliberately NOT routed through Sha256Digest (which lowercases): the truncated hash is
        // part of a persisted scope identity, so changing its casing would detach existing
        // retained state from its scope (#2691).
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return $"{readable}-{hash}";
    }
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

    /// <summary>
    /// Complete accounting identity for exact stores. Existing implementations remain explicitly
    /// label-only until they opt into the aggregate, period, book, and policy fields.
    /// </summary>
    TradeFillPostingScopeIdentity ScopeIdentity => new(PostingScope);

    Task<TradeFillPostingAcceptance> AcceptAsync(
        TradeExecutedEvent tradeEvent,
        CancellationToken ct = default);

    Task<IReadOnlyList<PendingTradeFillPosting>> LoadPendingAsync(CancellationToken ct = default);

    Task MarkPostedAsync(Guid fillId, CancellationToken ct = default);

    Task RecordFailureAsync(Guid fillId, string failure, CancellationToken ct = default);
}

/// <summary>
/// Execution-owned fill handoff backed by a scope-partitioned WAL and an atomically replaced
/// pending-state snapshot. The snapshot is written before acceptance is reported, so a transient
/// WAL append outage still leaves a fail-closed retained handoff that can replay after restart.
/// Periodic WAL commit/truncation bounds completed history while pending fills remain lossless.
/// </summary>
public sealed class WalTradeFillPostingStore : ITradeFillPostingStore
{
    private const int LegacySnapshotVersion = 1;
    private const int SnapshotVersion = 2;
    private const string PendingRecordType = "TradeFillPending";
    private const string FailureRecordType = "TradeFillFailure";
    private const string PostedRecordType = "TradeFillPosted";
    private const int MaximumFailureLength = 4_096;

    private readonly TradeFillPostingStoreOptions _options;
    private readonly WriteAheadLog _wal;
    private readonly ILogger<WalTradeFillPostingStore> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _initializationSync = new();
    private readonly object _disposeSync = new();
    private readonly Dictionary<Guid, PendingTradeFillPosting> _pending = [];
    private readonly HashSet<Guid> _walAccepted = [];
    private readonly HashSet<Guid> _posted = [];
    private readonly Queue<Guid> _postedOrder = [];
    private Task? _initializationTask;
    private Task? _disposeTask;
    private long _lastAppliedWalSequence;
    private long _snapshotAppliedThroughWalSequence;
    private long _nextStoreSequence;
    private int _recordsSinceCompaction;
    private int _disposeStarted;

    public WalTradeFillPostingStore(
        TradeFillPostingStoreOptions options,
        ILogger<WalTradeFillPostingStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Validate();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        PostingScope = _options.PostingScope;
        ScopeIdentity = _options.ScopeIdentity;
        _wal = new WriteAheadLog(
            _options.WalDirectory,
            new WalOptions
            {
                SyncMode = WalSyncMode.EveryWrite,
                ArchiveAfterTruncate = false,
                MaxWalFileAge = TimeSpan.FromMinutes(15),
                MaxWalFileSizeBytes = _options.MaxWalFileSizeBytes,
                CorruptionMode = WalCorruptionMode.Halt
            });
    }

    public string PostingScope { get; }

    public TradeFillPostingScopeIdentity ScopeIdentity { get; }

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
            if (_posted.Contains(tradeEvent.FillId))
                return new TradeFillPostingAcceptance(null, ShouldEnqueue: false, WasAlreadyPosted: true);
            if (_pending.TryGetValue(tradeEvent.FillId, out var retained))
            {
                EnsureSameEconomics(retained.TradeEvent, tradeEvent);
                return new TradeFillPostingAcceptance(retained, ShouldEnqueue: false, WasAlreadyPosted: false);
            }

            ct.ThrowIfCancellationRequested();
            var acceptedAtUtc = DateTimeOffset.UtcNow;
            var posting = new PendingTradeFillPosting(
                ++_nextStoreSequence,
                PostingScope,
                tradeEvent,
                acceptedAtUtc);

            // Try both independent durability paths. Acceptance is reported when either the atomic
            // snapshot or WAL commits; if both fail the OMS receives a fail-closed publisher error.
            _pending.Add(tradeEvent.FillId, posting);
            Exception? snapshotFailure = null;
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                snapshotFailure = ex;
            }

            try
            {
                var payload = new TradeFillPendingWalPayload(
                    PostingScope,
                    tradeEvent,
                    acceptedAtUtc,
                    posting.StoreSequence,
                    ScopeIdentity);
                var record = await AppendWalAsync(
                        JsonSerializer.Serialize(payload, ExecutionJsonContext.Default.TradeFillPendingWalPayload),
                        PendingRecordType,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                _walAccepted.Add(tradeEvent.FillId);
                _lastAppliedWalSequence = Math.Max(_lastAppliedWalSequence, record.Sequence);
                _recordsSinceCompaction++;
                await PersistSnapshotAfterCommitSafelyAsync().ConfigureAwait(false);
                await CompactSafelyAsync().ConfigureAwait(false);
            }
            catch (Exception walFailure)
            {
                if (snapshotFailure is not null)
                {
                    _pending.Remove(tradeEvent.FillId);
                    _nextStoreSequence--;
                    throw new AggregateException(
                        $"Neither durable trade-fill acceptance path retained fill '{tradeEvent.FillId:D}'.",
                        snapshotFailure,
                        walFailure);
                }

                _logger.LogError(
                    walFailure,
                    "Trade-fill WAL append failed for {FillId} in scope {PostingScope}; the atomic pending snapshot retained the accounting handoff for restart replay",
                    tradeEvent.FillId,
                    PostingScope);
            }

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
            return _pending.Values.OrderBy(static posting => posting.StoreSequence).ToArray();
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
            if (!_pending.TryGetValue(fillId, out var posting))
                throw new InvalidOperationException($"Fill '{fillId:D}' is not pending and cannot be acknowledged.");

            var wasWalAccepted = _walAccepted.Contains(fillId);
            var walAcknowledgementPersisted = false;
            if (wasWalAccepted)
            {
                try
                {
                    var payload = new TradeFillStatusWalPayload(
                        PostingScope,
                        fillId,
                        DateTimeOffset.UtcNow,
                        null,
                        ScopeIdentity);
                    var record = await AppendWalAsync(
                            JsonSerializer.Serialize(payload, ExecutionJsonContext.Default.TradeFillStatusWalPayload),
                            PostedRecordType,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    _lastAppliedWalSequence = Math.Max(_lastAppliedWalSequence, record.Sequence);
                    _recordsSinceCompaction++;
                    walAcknowledgementPersisted = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Trade-fill acknowledgement WAL append failed for {FillId}; committing the acknowledgement through the atomic snapshot",
                        fillId);
                }
            }

            _pending.Remove(fillId);
            _walAccepted.Remove(fillId);
            try
            {
                // This is the durable acknowledgement. It is reached only after the consumer's
                // authoritative posting target has completed and read the journal back.
                await PersistSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception snapshotFailure)
            {
                if (!walAcknowledgementPersisted)
                {
                    _pending.Add(fillId, posting);
                    if (wasWalAccepted)
                        _walAccepted.Add(fillId);
                    throw;
                }

                _logger.LogError(
                    snapshotFailure,
                    "Trade-fill acknowledgement snapshot failed for {FillId}; the posted WAL record remains authoritative and replayable",
                    fillId);
            }

            RememberPosted(fillId);
            await CompactSafelyAsync().ConfigureAwait(false);
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

            var walFailurePersisted = false;
            if (_walAccepted.Contains(fillId))
            {
                try
                {
                    var payload = new TradeFillStatusWalPayload(
                        PostingScope,
                        fillId,
                        occurredAtUtc,
                        normalizedFailure,
                        ScopeIdentity);
                    var record = await AppendWalAsync(
                            JsonSerializer.Serialize(payload, ExecutionJsonContext.Default.TradeFillStatusWalPayload),
                            FailureRecordType,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    _lastAppliedWalSequence = Math.Max(_lastAppliedWalSequence, record.Sequence);
                    _recordsSinceCompaction++;
                    walFailurePersisted = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Trade-fill failure WAL append failed for {FillId}; retaining the reconciliation state in the atomic snapshot",
                        fillId);
                }
            }

            var updated = posting with
            {
                FailureCount = posting.FailureCount + 1,
                LastFailure = normalizedFailure,
                LastAttemptAtUtc = occurredAtUtc
            };
            _pending[fillId] = updated;
            try
            {
                await PersistSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception snapshotFailure)
            {
                if (!walFailurePersisted)
                {
                    _pending[fillId] = posting;
                    throw;
                }

                _logger.LogError(
                    snapshotFailure,
                    "Trade-fill failure snapshot failed for {FillId}; the WAL failure record remains authoritative and replayable",
                    fillId);
            }

            await CompactSafelyAsync().ConfigureAwait(false);
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
        await LoadSnapshotAsync().ConfigureAwait(false);
        await _wal.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        var replayed = 0;
        await foreach (var record in _wal.GetUncommittedRecordsAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (record.Sequence <= _lastAppliedWalSequence)
                continue;

            ApplyWalRecord(record);
            _lastAppliedWalSequence = record.Sequence;
            replayed++;
        }

        if (replayed > 0)
        {
            _recordsSinceCompaction += replayed;
            await PersistSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            await CompactSafelyAsync(force: true).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Recovered {PendingCount} pending trade fill(s) for ledger posting scope {PostingScope}",
            _pending.Count,
            PostingScope);
    }

    private async Task LoadSnapshotAsync()
    {
        if (!File.Exists(_options.SnapshotPath))
            return;

        TradeFillPostingSnapshot snapshot;
        try
        {
            var json = await File.ReadAllTextAsync(_options.SnapshotPath, CancellationToken.None)
                .ConfigureAwait(false);
            snapshot = JsonSerializer.Deserialize(
                           json,
                           ExecutionJsonContext.Default.TradeFillPostingSnapshot)
                       ?? throw new InvalidDataException("Trade-fill posting snapshot is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Trade-fill posting snapshot is invalid.", ex);
        }

        if (snapshot.Version is not LegacySnapshotVersion and not SnapshotVersion)
            throw new InvalidDataException($"Unsupported trade-fill posting snapshot version {snapshot.Version}.");
        if (snapshot.Version == SnapshotVersion && snapshot.ScopeIdentity is null)
            throw new InvalidDataException("Trade-fill posting snapshot has no durable scope identity state.");

        EnsureScopeIdentity(
            snapshot.PostingScope,
            snapshot.Version == LegacySnapshotVersion ? null : snapshot.ScopeIdentity,
            "snapshot");
        if (snapshot.AppliedThroughWalSequence < 0 || snapshot.NextStoreSequence < 0)
            throw new InvalidDataException("Trade-fill posting snapshot has invalid sequence state.");
        if (snapshot.Pending is null)
            throw new InvalidDataException("Trade-fill posting snapshot has no pending state.");

        _lastAppliedWalSequence = snapshot.AppliedThroughWalSequence;
        _snapshotAppliedThroughWalSequence = snapshot.AppliedThroughWalSequence;
        _nextStoreSequence = snapshot.NextStoreSequence;
        foreach (var item in snapshot.Pending)
        {
            if (item is null || item.Posting is null || item.Posting.TradeEvent is null)
                throw new InvalidDataException("Trade-fill posting snapshot contains invalid pending state.");
            EnsureScope(item.Posting.PostingScope);
            var normalizedPosting = item.Posting;
            if (normalizedPosting.TradeEvent.FillId == Guid.Empty)
                throw new InvalidDataException("Trade-fill posting snapshot contains an empty fill id.");
            if (!_pending.TryAdd(normalizedPosting.TradeEvent.FillId, normalizedPosting))
                throw new InvalidDataException($"Trade-fill posting snapshot repeats fill '{normalizedPosting.TradeEvent.FillId:D}'.");
            if (item.WalAccepted)
                _walAccepted.Add(normalizedPosting.TradeEvent.FillId);
            _nextStoreSequence = Math.Max(_nextStoreSequence, normalizedPosting.StoreSequence);
        }
    }

    private void ApplyWalRecord(WalRecord record)
    {
        if (record.RecordType == PendingRecordType)
        {
            var payload = DeserializePending(record);
            EnsureScopeIdentity(
                payload.PostingScope,
                payload.ScopeIdentity,
                $"WAL record {record.Sequence}");
            if (payload.TradeEvent.FillId == Guid.Empty)
                throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} has an empty fill id.");
            if (_posted.Contains(payload.TradeEvent.FillId))
                return;

            if (_pending.TryGetValue(payload.TradeEvent.FillId, out var existing))
            {
                EnsureSameEconomics(existing.TradeEvent, payload.TradeEvent);
            }
            else
            {
                var storeSequence = payload.StoreSequence > 0
                    ? payload.StoreSequence
                    : ++_nextStoreSequence;
                _pending.Add(payload.TradeEvent.FillId, new PendingTradeFillPosting(
                    storeSequence,
                    PostingScope,
                    payload.TradeEvent,
                    payload.AcceptedAtUtc));
                _nextStoreSequence = Math.Max(_nextStoreSequence, storeSequence);
            }

            _walAccepted.Add(payload.TradeEvent.FillId);
            return;
        }

        if (record.RecordType is not (FailureRecordType or PostedRecordType))
            throw new InvalidDataException($"Trade-fill WAL record {record.Sequence} has unknown type '{record.RecordType}'.");

        var status = DeserializeStatus(record);
        EnsureScopeIdentity(
            status.PostingScope,
            status.ScopeIdentity,
            $"WAL status record {record.Sequence}");
        if (status.FillId == Guid.Empty)
            throw new InvalidDataException($"Trade-fill WAL status record {record.Sequence} has an empty fill id.");
        if (record.RecordType == PostedRecordType)
        {
            if (status.Failure is not null)
                throw new InvalidDataException($"Trade-fill acknowledgement record {record.Sequence} contains failure data.");
            if (_posted.Contains(status.FillId))
                return;
            if (!_pending.Remove(status.FillId))
            {
                throw new InvalidDataException(
                    $"Trade-fill WAL acknowledgement record {record.Sequence} does not reference a pending fill.");
            }
            _walAccepted.Remove(status.FillId);
            RememberPosted(status.FillId);
            return;
        }

        if (string.IsNullOrWhiteSpace(status.Failure))
            throw new InvalidDataException($"Trade-fill failure record {record.Sequence} has no failure detail.");
        if (!_pending.TryGetValue(status.FillId, out var posting))
        {
            throw new InvalidDataException(
                $"Trade-fill WAL failure record {record.Sequence} does not reference a pending fill.");
        }
        _pending[status.FillId] = posting with
        {
            FailureCount = posting.FailureCount + 1,
            LastFailure = status.Failure,
            LastAttemptAtUtc = status.OccurredAtUtc
        };
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        var snapshot = new TradeFillPostingSnapshot(
            SnapshotVersion,
            PostingScope,
            _lastAppliedWalSequence,
            _nextStoreSequence,
            _pending.Values
                .OrderBy(static posting => posting.StoreSequence)
                .Select(posting => new TradeFillPostingSnapshotItem(
                    posting,
                    _walAccepted.Contains(posting.TradeEvent.FillId)))
                .ToArray(),
            ScopeIdentity);
        var json = JsonSerializer.Serialize(snapshot, ExecutionJsonContext.Default.TradeFillPostingSnapshot);
        if (_options.SnapshotWriteOverride is { } snapshotWriteOverride)
            await snapshotWriteOverride(json, ct).ConfigureAwait(false);
        else
            await AtomicFileWriter.WriteAsync(_options.SnapshotPath, json, ct).ConfigureAwait(false);
        _snapshotAppliedThroughWalSequence = _lastAppliedWalSequence;
    }

    private async Task PersistSnapshotAfterCommitSafelyAsync()
    {
        try
        {
            await PersistSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not advance the trade-fill snapshot WAL watermark; the earlier snapshot and WAL record remain replayable");
        }
    }

    private async Task CompactSafelyAsync(bool force = false)
    {
        if (_snapshotAppliedThroughWalSequence <= 0
            || (!force && _recordsSinceCompaction < _options.CompactionRecordThreshold))
        {
            return;
        }

        try
        {
            await _wal.CommitAsync(_snapshotAppliedThroughWalSequence, CancellationToken.None).ConfigureAwait(false);
            await _wal.TruncateAsync(_snapshotAppliedThroughWalSequence, CancellationToken.None).ConfigureAwait(false);
            _recordsSinceCompaction = 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Trade-fill WAL compaction failed at sequence {Sequence}; retained records remain replayable",
                _snapshotAppliedThroughWalSequence);
        }
    }

    private Task<WalRecord> AppendWalAsync(string json, string recordType, CancellationToken ct)
        => _options.WalAppendOverride is { } appendOverride
            ? appendOverride(json, recordType, ct)
            : _wal.AppendAsync(json, recordType, ct);

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
            if (initializationFailure is null)
                await CompactSafelyAsync(force: true).ConfigureAwait(false);
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

    private void RememberPosted(Guid fillId)
    {
        if (!_posted.Add(fillId))
            return;
        _postedOrder.Enqueue(fillId);
        while (_postedOrder.Count > _options.MaxRememberedPostedFillIds
            && _postedOrder.TryDequeue(out var forgotten))
        {
            _posted.Remove(forgotten);
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

    private TradeFillPostingScopeIdentity EnsureScopeIdentity(
        string retainedScope,
        TradeFillPostingScopeIdentity? retainedScopeIdentity,
        string source)
    {
        EnsureScope(retainedScope);

        TradeFillPostingScopeIdentity normalizedIdentity;
        try
        {
            normalizedIdentity = (retainedScopeIdentity
                                  ?? new TradeFillPostingScopeIdentity(retainedScope))
                .Validate();
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Trade-fill {source} scope identity is invalid.", ex);
        }

        if (!string.Equals(retainedScope, normalizedIdentity.PostingScope, StringComparison.Ordinal)
            || normalizedIdentity != ScopeIdentity)
        {
            throw new InvalidDataException(
                $"Trade-fill {source} scope identity '{normalizedIdentity}' does not match configured scope identity '{ScopeIdentity}'.");
        }

        return normalizedIdentity;
    }

    private static void EnsureSameEconomics(TradeExecutedEvent retained, TradeExecutedEvent candidate)
    {
        if (retained != candidate)
        {
            throw new InvalidOperationException(
                $"Fill '{candidate.FillId:D}' was replayed with different economic content.");
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
    DateTimeOffset AcceptedAtUtc,
    long StoreSequence = 0,
    TradeFillPostingScopeIdentity? ScopeIdentity = null);

internal sealed record TradeFillStatusWalPayload(
    string PostingScope,
    Guid FillId,
    DateTimeOffset OccurredAtUtc,
    string? Failure,
    TradeFillPostingScopeIdentity? ScopeIdentity = null);

internal sealed record TradeFillPostingSnapshot(
    int Version,
    string PostingScope,
    long AppliedThroughWalSequence,
    long NextStoreSequence,
    IReadOnlyList<TradeFillPostingSnapshotItem> Pending,
    TradeFillPostingScopeIdentity? ScopeIdentity = null);

internal sealed record TradeFillPostingSnapshotItem(
    PendingTradeFillPosting Posting,
    bool WalAccepted);
