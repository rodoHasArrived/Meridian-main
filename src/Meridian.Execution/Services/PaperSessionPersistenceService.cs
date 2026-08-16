using Meridian.Execution.Logging;
using System.Collections.Concurrent;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>
/// Manages paper trading session lifecycle and persistence.
/// Tracks session metadata, portfolio snapshots, and order history
/// across session boundaries.
/// </summary>
/// <remarks>
/// When constructed with an <see cref="IPaperSessionStore"/> all session
/// metadata, fills, and order updates are written to durable storage.
/// Call <see cref="InitialiseAsync"/> on startup to reload sessions and
/// reconstruct portfolio state from the persisted fill log.
/// Without a store the service falls back to in-memory operation and
/// sessions are lost on process restart.
/// </remarks>
public sealed class PaperSessionPersistenceService : IAsyncDisposable
{
    private const int MaxRetainedSessions = 1_000;
    private const int MaxSymbolsPerSession = 128;
    private const int MaxStrategyIdLength = 128;
    private const int MaxStrategyNameLength = 256;
    private const int MaxSymbolLength = 32;

    private ConcurrentDictionary<string, PaperSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionGates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly object _initialisationSync = new();
    private readonly object _operationSync = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly IPaperSessionStore? _store;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly ILogger<PaperSessionPersistenceService> _logger;
    private Task? _initialisationTask;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private int _initialised; // 0 = not yet, 1 = successfully published
    private int _disposed;

    public PaperSessionPersistenceService(
        ILogger<PaperSessionPersistenceService> logger,
        IPaperSessionStore? store = null,
        ExecutionAuditTrailService? auditTrail = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store;
        _auditTrail = auditTrail;
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Loads all sessions from the durable store and reconstructs portfolio state by replaying
    /// the persisted fill log. Concurrent callers share one attempt. A failed or cancelled attempt
    /// publishes no partial state and the next call starts a fresh attempt.
    /// </summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _initialised) != 0)
                return;

            Task initialisationTask;
            lock (_initialisationSync)
            {
                if (_initialised != 0)
                    return;

                if (_initialisationTask is null
                    || _initialisationTask.IsFaulted
                    || _initialisationTask.IsCanceled)
                {
                    _initialisationTask = InitialiseCoreAsync(ct);
                }

                initialisationTask = _initialisationTask;
            }

            try
            {
                await initialisationTask.WaitAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested && !_lifetimeCts.IsCancellationRequested)
            {
                // The shared attempt was cancelled by an EARLIER caller's token, not ours and
                // not disposal. The IsCanceled check above races the task's transition — a
                // cancelled-but-still-completing attempt passes it and gets joined — so the
                // cancellation must also be absorbed here: clear the doomed attempt (if still
                // current) and retry with our own token (#2676).
                lock (_initialisationSync)
                {
                    if (ReferenceEquals(_initialisationTask, initialisationTask))
                        _initialisationTask = null;
                }
            }
        }
    }

    private async Task InitialiseCoreAsync(CancellationToken callerToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken,
            _lifetimeCts.Token);
        var ct = linkedCts.Token;

        if (_store is null)
        {
            Volatile.Write(ref _initialised, 1);
            return;
        }

        var records = await _store.LoadAllSessionsAsync(ct).ConfigureAwait(false);
        if (records.Count > MaxRetainedSessions)
        {
            throw new InvalidDataException(
                $"Paper-session store contains {records.Count} sessions; limit is {MaxRetainedSessions}.");
        }

        var candidate = new ConcurrentDictionary<string, PaperSession>(StringComparer.Ordinal);
        var unapplied = new List<(string SessionId, PaperSessionFillRecord Record)>();
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            ValidatePersistedRecord(record);
            if (candidate.ContainsKey(record.SessionId))
            {
                throw new InvalidDataException(
                    $"Paper-session store returned duplicate metadata for '{record.SessionId}'.");
            }

            var fillRecords = await _store.LoadFillRecordsAsync(record.SessionId, ct).ConfigureAwait(false);
            var replayLedger = new Meridian.Ledger.Ledger();
            var portfolio = new PaperTradingPortfolio(record.InitialCash, replayLedger);
            var appliedFillHashes = new Dictionary<Guid, string>();
            var fillHistory = new List<ExecutionReport>(fillRecords.Count);
            var hasUnappliedFill = false;
            foreach (var fillRecord in fillRecords)
            {
                fillRecord.Validate();
                if (!appliedFillHashes.TryAdd(fillRecord.FillId, fillRecord.CanonicalHash))
                {
                    throw new InvalidDataException(
                        $"Paper session '{record.SessionId}' contains duplicate FillId '{fillRecord.FillId:D}'.");
                }

                var fillSnapshot = CloneExecutionReport(fillRecord.Fill);
                portfolio.ApplyFill(fillSnapshot);
                fillHistory.Add(fillSnapshot);
                if (!fillRecord.IsApplied)
                {
                    hasUnappliedFill = true;
                    unapplied.Add((record.SessionId, fillRecord));
                }
            }

            var ledgerEntries = await _store.LoadLedgerJournalAsync(record.SessionId, ct).ConfigureAwait(false);
            var reconstruction = ReconstructLedger(ledgerEntries);
            var orders = await _store.LoadOrderHistoryAsync(record.SessionId, ct).ConfigureAwait(false);
            var session = new PaperSession
            {
                SessionId = record.SessionId,
                StrategyId = record.StrategyId,
                StrategyName = record.StrategyName,
                InitialCash = record.InitialCash,
                CreatedAt = record.CreatedAt,
                ClosedAt = record.ClosedAt,
                IsActive = record.IsActive,
                Symbols = record.Symbols.ToList(),
                Portfolio = portfolio,
                ReconstructedLedger = record.IsActive || hasUnappliedFill ? null : reconstruction.Ledger,
                Reconstruction = reconstruction,
                MatchingModelVersion = record.MatchingModelVersion,
                CostModelVersion = record.CostModelVersion,
            };
            foreach (var appliedFill in appliedFillHashes)
                session.AppliedFillHashes.Add(appliedFill.Key, appliedFill.Value);
            session.FillHistory.AddRange(fillHistory);
            session.OrderHistory.AddRange(orders.Select(CloneOrderState));
            if (!candidate.TryAdd(record.SessionId, session))
            {
                throw new InvalidDataException(
                    $"Paper-session store returned duplicate metadata for '{record.SessionId}'.");
            }
        }

        // Every fallible load/validation step completed off-side. Refresh the ledger projection
        // for sessions whose fill claim survived without an apply acknowledgement, then ack. If
        // either step fails, no candidate is published and a later attempt safely retries recovery.
        foreach (var sessionId in unapplied.Select(static item => item.SessionId).Distinct(StringComparer.Ordinal))
        {
            var session = candidate[sessionId];
            var ledger = session.Portfolio?.Ledger;
            if (ledger is not null && ledger.JournalEntryCount > 0)
            {
                await _store.SaveLedgerJournalAsync(
                    sessionId,
                    SerializeLedgerJournal(ledger, sessionId),
                    ct).ConfigureAwait(false);
            }
        }

        foreach (var pending in unapplied)
        {
            await _store.MarkFillAppliedAsync(
                pending.SessionId,
                pending.Record.FillId,
                pending.Record.CanonicalHash,
                ct).ConfigureAwait(false);
        }

        foreach (var sessionId in candidate.Keys)
            _sessionGates.TryAdd(sessionId, new SemaphoreSlim(1, 1));

        Volatile.Write(ref _sessions, candidate);
        Volatile.Write(ref _initialised, 1);
        _logger.LogInformation(
            "Initialised paper session store: loaded {Count} session(s), recovered {RecoveredFillCount} unapplied fill(s)",
            records.Count,
            unapplied.Count);
    }

    // ------------------------------------------------------------------
    // CRUD
    // ------------------------------------------------------------------

    /// <summary>Creates a new paper trading session and returns its summary.</summary>
    public async Task<PaperSessionSummaryDto> CreateSessionAsync(CreatePaperSessionDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        ValidateCreateRequest(request);
        await _catalogGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessions = CurrentSessions;
            TrimClosedSessionsIfNeeded(sessions);
            if (sessions.Count >= MaxRetainedSessions)
            {
                throw new InvalidOperationException($"Paper session limit reached ({MaxRetainedSessions}). Close existing sessions and retry.");
            }

            var sessionId = $"PAPER-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}";
            var ledger = new Meridian.Ledger.Ledger();
            var session = new PaperSession
            {
                SessionId = sessionId,
                StrategyId = request.StrategyId,
                StrategyName = request.StrategyName,
                InitialCash = request.InitialCash,
                CreatedAt = DateTimeOffset.UtcNow,
                Symbols = request.Symbols?.ToList() ?? [],
                Portfolio = new PaperTradingPortfolio(request.InitialCash, ledger),
                // New sessions record the matching and cost policies in effect so promotion
                // evidence can cite exactly which paper execution model produced them.
                MatchingModelVersion = PaperMatching.PaperOrderMatchingPolicy.MatchingModelVersion,
                CostModelVersion = PaperMatching.PaperTradingCostModel.CostModelVersion,
            };

            if (_store is not null)
            {
                await PersistSessionLedgerAsync(session, ct).ConfigureAwait(false);
                // Metadata is the final durable create candidate. It must succeed before the
                // session becomes observable; no fallible persistence follows this write.
                await _store.SaveSessionMetadataAsync(ToPersistedRecord(session), ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            if (!sessions.TryAdd(sessionId, session))
                throw new InvalidOperationException($"Paper session '{sessionId}' already exists.");
            _sessionGates.TryAdd(sessionId, new SemaphoreSlim(1, 1));

            _logger.LogInformation(
                "Created paper session {SessionId} for strategy {StrategyId} with {InitialCash:C} initial capital",
                sessionId, LogSanitizer.Sanitize(request.StrategyId), request.InitialCash);

            return ToSummarySnapshot(session);
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    private void ValidateCreateRequest(CreatePaperSessionDto request)
    {
        if (request.InitialCash < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.InitialCash,
                "InitialCash must be non-negative.");
        }

        if (string.IsNullOrWhiteSpace(request.StrategyId))
        {
            throw new ArgumentException("StrategyId is required.", nameof(request));
        }

        if (request.StrategyId.Length > MaxStrategyIdLength)
        {
            throw new ArgumentException($"StrategyId exceeds the maximum length of {MaxStrategyIdLength} characters.", nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(request.StrategyName) &&
            request.StrategyName.Length > MaxStrategyNameLength)
        {
            throw new ArgumentException($"StrategyName exceeds the maximum length of {MaxStrategyNameLength} characters.", nameof(request));
        }

        if (request.Symbols is { Count: > MaxSymbolsPerSession })
        {
            throw new ArgumentException($"A paper session can include at most {MaxSymbolsPerSession} symbols.", nameof(request));
        }

        if (request.Symbols is not null)
        {
            for (var i = 0; i < request.Symbols.Count; i++)
            {
                var symbol = request.Symbols[i];
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    throw new ArgumentException("Symbols cannot contain empty entries.", nameof(request));
                }

                if (symbol.Length > MaxSymbolLength)
                {
                    throw new ArgumentException($"Symbol '{symbol}' exceeds the maximum length of {MaxSymbolLength} characters.", nameof(request));
                }
            }
        }
    }

    private void TrimClosedSessionsIfNeeded(ConcurrentDictionary<string, PaperSession> sessions)
    {
        if (sessions.Count < MaxRetainedSessions)
        {
            return;
        }

        var sessionsToTrim = sessions.Values
            .Select(CreateRetentionSnapshot)
            .Where(static session => !session.IsActive)
            .OrderBy(static session => session.ClosedAt ?? session.CreatedAt)
            .Take(Math.Max(1, sessions.Count - MaxRetainedSessions + 1))
            .Select(static session => session.SessionId)
            .ToArray();

        foreach (var sessionId in sessionsToTrim)
        {
            sessions.TryRemove(sessionId, out _);
            _sessionGates.TryRemove(sessionId, out _);
        }
    }

    /// <summary>Closes a paper trading session and snapshots its final state.</summary>
    public async Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        var sessions = CurrentSessions;
        if (!sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!sessions.TryGetValue(sessionId, out session))
                return false;

            DateTimeOffset closedAt;
            PersistedSessionRecord closeCandidate;
            lock (session.SyncRoot)
            {
                if (!session.IsActive)
                    return true;

                closedAt = DateTimeOffset.UtcNow;
                closeCandidate = ToPersistedRecord(session, isActive: false, closedAt);
            }

            if (_store is not null)
            {
                var ledger = session.Portfolio?.Ledger;
                if (ledger is not null && ledger.JournalEntryCount > 0)
                {
                    // A closed session prefers its persisted journal after restart. Publishing
                    // closed metadata without the final journal would therefore make a stale
                    // snapshot authoritative over the complete fill log. The final snapshot is a
                    // close precondition, even though ordinary post-fill snapshots remain best effort.
                    await PersistSessionLedgerAsync(
                        session,
                        ct,
                        requireDurableSuccess: true).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Persisted {Count} ledger entries for paper session {SessionId}",
                        ledger.JournalEntryCount, sessionId);
                }

                // The close record is the final fallible persistence step while the session gate
                // excludes fills. Only after it succeeds does closed state become visible.
                await _store.SaveSessionMetadataAsync(closeCandidate, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            decimal finalEquity;
            lock (session.SyncRoot)
            {
                session.ClosedAt = closedAt;
                session.IsActive = false;
                finalEquity = session.Portfolio?.PortfolioValue ?? 0m;
            }

            _logger.LogInformation(
                "Closed paper session {SessionId} — final equity: {Equity:C}",
                sessionId, finalEquity);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Returns summaries of all tracked sessions.</summary>
    public IReadOnlyList<PaperSessionSummaryDto> GetSessions()
    {
        ThrowIfDisposed();
        return CurrentSessions.Values
            .Select(ToSummarySnapshot)
            .OrderByDescending(static s => s.CreatedAt)
            .ToArray();
    }

    /// <summary>Returns detailed session state or <c>null</c> if not found.</summary>
    public PaperSessionDetailDto? GetSession(string sessionId)
    {
        ThrowIfDisposed();
        if (!CurrentSessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }
        return BuildSessionDetailDto(session);
    }

    /// <summary>Returns a defensive portfolio snapshot for an active session, or null.</summary>
    public PaperTradingPortfolio? GetActivePortfolio(string sessionId)
    {
        ThrowIfDisposed();
        if (!CurrentSessions.TryGetValue(sessionId, out var session))
            return null;

        lock (session.SyncRoot)
        {
            if (!session.IsActive || session.Portfolio is null)
                return null;
        }

        return BuildPortfolioProjection(session);
    }

    /// <summary>
    /// Returns a detached snapshot of the double-entry ledger for a session — either the live
    /// ledger (active sessions) or the ledger reconstructed from the persisted journal (closed
    /// sessions). The snapshot can be queried or even downcast and mutated without changing the
    /// authoritative session ledger.
    /// Returns <see langword="null"/> when no ledger data is available for the session.
    /// </summary>
    public IReadOnlyLedger? GetLedger(string sessionId)
    {
        ThrowIfDisposed();
        if (!CurrentSessions.TryGetValue(sessionId, out var session))
            return null;

        JournalEntry[]? journalSnapshot;
        lock (session.SyncRoot)
        {
            // For active sessions return the replay-built live ledger. Closed sessions prefer the
            // exact persisted journal but fall back to the fill-rebuilt ledger after crash recovery.
            var ledger = session.IsActive
                ? session.Portfolio?.Ledger ?? session.ReconstructedLedger
                : session.ReconstructedLedger ?? session.Portfolio?.Ledger;
            journalSnapshot = ledger?.Journal.ToArray();
        }

        // Copying the authoritative journal is the only work performed under the session lock.
        // Rebuilding detached ledger indexes may be comparatively expensive for long sessions and
        // must not block an arriving fill from entering the durable session gate.
        return journalSnapshot is null ? null : CreateLedgerSnapshot(journalSnapshot);
    }

    /// <summary>
    /// Compatibility alias for <see cref="GetActivePortfolio"/>. The returned portfolio is a
    /// defensive projection; callers cannot mutate authoritative state outside the session's
    /// durable fill gate. Returns <see langword="null"/> when the session is absent or closed.
    /// </summary>
    [Obsolete("Use GetActivePortfolio; mutable session portfolios are no longer exposed.")]
    public PaperTradingPortfolio? GetLivePortfolio(string sessionId)
        => GetActivePortfolio(sessionId);

    /// <summary>
    /// Converts a lightweight <see cref="ExecutionFill"/> into an <see cref="ExecutionReport"/>
    /// and records it against the session via <see cref="RecordFillAsync"/>.
    /// </summary>
    public async Task RecordPaperFillAsync(
        string sessionId,
        ExecutionFill fill,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fill);

        var report = new ExecutionReport
        {
            OrderId = "paper-fill",
            ReportType = ExecutionReportType.Fill,
            Symbol = fill.Symbol,
            Side = fill.Quantity >= 0m ? OrderSide.Buy : OrderSide.Sell,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
            OrderQuantity = Math.Abs(fill.Quantity),
            FilledQuantity = Math.Abs(fill.Quantity),
            FillPrice = fill.FillPrice,
            Timestamp = fill.FilledAt
        };

        // ExecutionFill carries no venue or client execution id. Derive a stable synthetic order
        // id from the exact report content it does carry so retrying this convenience path cannot
        // mint a second paper-session FillId.
        report = report with
        {
            OrderId = $"paper-{PaperSessionFillRecord.ComputeCanonicalFillId(report):N}"
        };

        await RecordFillAsync(sessionId, report, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that the current session portfolio matches the replayed fill-log state,
    /// optionally asserting that at least <paramref name="expectedFillCount"/> fills and
    /// <paramref name="expectedOrderCount"/> orders were persisted.
    /// When the counts do not match a mismatch reason is added to the result.
    /// </summary>
    public async Task<PaperSessionReplayVerificationDto?> VerifyReplayAsync(
        string sessionId,
        int? expectedFillCount,
        int? expectedOrderCount,
        CancellationToken ct = default)
    {
        var result = await VerifyReplayInternalAsync(sessionId, ct).ConfigureAwait(false);
        if (result is null)
            return null;

        // If the caller specified expected counts we do an additional assertion pass.
        if (expectedFillCount.HasValue && result.ComparedFillCount < expectedFillCount.Value)
        {
            var reasons = result.MismatchReasons is IList<string> mutable
                ? mutable
                : result.MismatchReasons.ToList();

            reasons.Add(
                $"fill-count-mismatch: expected>={expectedFillCount.Value}, actual={result.ComparedFillCount}");

            result = result with
            {
                IsConsistent = false,
                MismatchReasons = (IReadOnlyList<string>)reasons
            };
        }

        if (expectedOrderCount.HasValue && result.ComparedOrderCount < expectedOrderCount.Value)
        {
            var reasons = result.MismatchReasons is IList<string> mutable2
                ? mutable2
                : result.MismatchReasons.ToList();

            reasons.Add(
                $"order-count-mismatch: expected>={expectedOrderCount.Value}, actual={result.ComparedOrderCount}");

            result = result with
            {
                IsConsistent = false,
                MismatchReasons = (IReadOnlyList<string>)reasons
            };
        }

        return result;
    }

    /// <summary>
    /// Records an order status update for a session and does not complete until
    /// the durable order-history append finishes.
    /// </summary>
    public async Task RecordOrderUpdateAsync(
        string sessionId,
        OrderState orderState,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderState);
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        var sessions = CurrentSessions;
        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var orderSnapshot = CloneOrderState(orderState);
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                // Compatibility: the OMS can begin writing history before a separately composed
                // session catalog is present. Retain that durable evidence, but publish no
                // synthetic in-memory session.
                if (_store is not null)
                    await _store.AppendOrderUpdateAsync(sessionId, orderSnapshot, ct).ConfigureAwait(false);
                return;
            }

            lock (session.SyncRoot)
            {
                if (!session.IsActive)
                    return;
            }

            if (_store is not null)
            {
                // The durable history entry is the order-update candidate. A failed append must
                // remain invisible to readers so callers can retry it safely.
                await _store.AppendOrderUpdateAsync(sessionId, orderSnapshot, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            lock (session.SyncRoot)
                session.OrderHistory.Add(orderSnapshot);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Applies a fill execution report to the session portfolio and persists
    /// the fill event to the durable store for future replay.
    /// No-op for non-fill reports (accepted, cancelled, rejected).
    /// </summary>
    public Task RecordFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fill);
        if (fill.ReportType is not (ExecutionReportType.Fill or ExecutionReportType.PartialFill))
            return Task.CompletedTask;

        var fillId = PaperSessionFillRecord.ComputeCanonicalFillId(fill);
        return RecordFillAsync(sessionId, fillId, fill, ct);
    }

    /// <summary>
    /// Durably claims and applies a fill using the caller-provided canonical OMS FillId.
    /// Reusing the FillId with identical content is idempotent; reusing it with different
    /// content fails closed.
    /// </summary>
    public async Task RecordFillAsync(
        string sessionId,
        Guid fillId,
        ExecutionReport fill,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fill);
        if (fillId == Guid.Empty)
            throw new ArgumentException("A paper-session fill requires a non-empty FillId.", nameof(fillId));
        if (fill.ReportType is not (ExecutionReportType.Fill or ExecutionReportType.PartialFill))
            return;

        var expectedFillId = PaperSessionFillRecord.ComputeCanonicalFillId(fill);
        if (fillId != expectedFillId)
        {
            throw new InvalidDataException(
                $"Paper-session FillId '{fillId:D}' does not match the account-independent "
                + $"canonical FillId '{expectedFillId:D}' for this fill.");
        }

        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        var sessions = CurrentSessions;
        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fillSnapshot = CloneExecutionReport(fill);
            var record = PaperSessionFillRecord.Create(fillId, fillSnapshot, DateTimeOffset.UtcNow);
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                throw new InvalidOperationException(
                    $"Cannot record paper-session fill '{fillId:D}' because session '{sessionId}' is not loaded.");
            }

            lock (session.SyncRoot)
            {
                // The session gate defines close-vs-fill ordering. A fill that claims the gate
                // before close is committed first; a fill that arrives after close is ignored.
                if (!session.IsActive)
                    return;
            }

            var appendResult = _store is null
                ? new PaperSessionFillAppendResult(PaperSessionFillAppendStatus.Added)
                : await _store.TryAppendFillAsync(sessionId, record, ct).ConfigureAwait(false);

            if (appendResult.Status == PaperSessionFillAppendStatus.Conflict)
            {
                throw new InvalidDataException(
                    $"Paper-session FillId '{fillId:D}' is already claimed by different content "
                    + $"(existing hash {appendResult.ExistingCanonicalHash ?? "unknown"}, candidate {record.CanonicalHash}).");
            }

            bool alreadyApplied;
            lock (session.SyncRoot)
            {
                alreadyApplied = session.AppliedFillHashes.TryGetValue(fillId, out var appliedHash);
                if (alreadyApplied
                    && !string.Equals(appliedHash, record.CanonicalHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Paper-session FillId '{fillId:D}' is already applied with different content.");
                }
            }

            if (!alreadyApplied)
            {
                // Build the projection off-side. If applying the candidate throws, its durable
                // claim remains unacknowledged and no partial portfolio/history state is visible.
                var replacementPortfolio = BuildPortfolioProjection(session, fillSnapshot);
                ct.ThrowIfCancellationRequested();
                lock (session.SyncRoot)
                {
                    session.Portfolio = replacementPortfolio;
                    session.FillHistory.Add(fillSnapshot);
                    session.AppliedFillHashes.Add(fillId, record.CanonicalHash);
                }
            }

            if (_store is not null)
            {
                // Ack after projection publication. If the ack fails, a retry observes the applied
                // FillId, skips projection, and retries only this idempotent acknowledgement.
                await _store.MarkFillAppliedAsync(
                    sessionId,
                    fillId,
                    record.CanonicalHash,
                    ct).ConfigureAwait(false);

                await PersistSessionLedgerAsync(session, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Replays the persisted fill log for <paramref name="sessionId"/> through a
    /// fresh <see cref="PaperTradingPortfolio"/> and returns the reconstructed
    /// portfolio snapshot.
    /// </summary>
    /// <remarks>
    /// When no durable store is configured the current in-memory portfolio state
    /// is returned instead.
    /// Returns <see langword="null"/> when the session does not exist.
    /// </remarks>
    public async Task<ExecutionPortfolioSnapshotDto?> ReplaySessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_store is null)
        {
            // Fall back to current in-memory state.
            return GetSession(sessionId)?.Portfolio;
        }

        var sessions = CurrentSessions;
        if (!sessions.TryGetValue(sessionId, out var session))
            return null;

        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!sessions.TryGetValue(sessionId, out session))
                return null;

            var fills = await _store.LoadFillRecordsAsync(sessionId, ct).ConfigureAwait(false);
            var portfolio = new PaperTradingPortfolio(session.InitialCash);
            foreach (var fill in fills)
                portfolio.ApplyFill(CloneExecutionReport(fill.Fill));

            return CreatePortfolioSnapshot(portfolio);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Verifies that the current session portfolio matches the replayed fill-log state.
    /// This gives operators an explicit continuity check for paper sessions that are
    /// expected to survive restarts and replay cleanly from durable fills.
    /// </summary>
    public async Task<PaperSessionReplayVerificationDto?> VerifyReplayAsync(
        string sessionId,
        CancellationToken ct = default)
        => await VerifyReplayInternalAsync(sessionId, ct).ConfigureAwait(false);

    private async Task<PaperSessionReplayVerificationDto?> VerifyReplayInternalAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        if (!CurrentSessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }
        var detail = BuildSessionDetailDto(session);

        var replayPortfolio = await ReplaySessionAsync(sessionId, ct).ConfigureAwait(false);
        if (replayPortfolio is null)
        {
            return null;
        }

        var persistedFills = _store is null
            ? []
            : await _store.LoadFillsAsync(sessionId, ct).ConfigureAwait(false);
        var persistedOrders = _store is null
            ? []
            : await _store.LoadOrderHistoryAsync(sessionId, ct).ConfigureAwait(false);
        var persistedLedgerEntries = _store is null
            ? []
            : await _store.LoadLedgerJournalAsync(sessionId, ct).ConfigureAwait(false);

        var mismatchReasons = ComparePortfolios(detail.Portfolio, replayPortfolio);
        var currentLedger = GetLedger(sessionId);
        var currentLedgerEntryCount = currentLedger?.JournalEntryCount ?? 0;
        var currentLedgerLineCount = currentLedger?.TotalLedgerEntryCount ?? 0;
        var comparedFillCount = _store is null
            ? detail.FillCount
            : persistedFills.Count;
        var comparedOrderCount = _store is null
            ? detail.OrderHistory?.Count ?? 0
            : persistedOrders.Count;
        var comparedLedgerEntryCount = _store is null
            ? currentLedgerEntryCount
            : persistedLedgerEntries.Count;
        var persistedLedgerLineCount = _store is null
            ? currentLedgerLineCount
            : persistedLedgerEntries.Sum(static entry => entry.Lines.Count);
        var reconstruction = session.Reconstruction;
        var lastPersistedFillAt = persistedFills.Count > 0
            ? persistedFills.Max(fill => fill.Timestamp)
            : (DateTimeOffset?)null;
        var lastPersistedOrderUpdateAt = persistedOrders.Count > 0
            ? persistedOrders.Max(order => order.LastUpdatedAt ?? order.CreatedAt)
            : (DateTimeOffset?)null;
        if (_store is not null)
        {
            CompareOrderHistory(detail.OrderHistory, persistedOrders, mismatchReasons);
            CompareLedgerJournal(currentLedger, persistedLedgerEntries, mismatchReasons);
        }

        if (reconstruction.CorruptEntryCount > 0)
        {
            mismatchReasons.Add(
                $"Persisted ledger reconstruction skipped {reconstruction.CorruptEntryCount} corrupt entr{(reconstruction.CorruptEntryCount == 1 ? "y" : "ies")} (IDs: {string.Join(", ", reconstruction.CorruptEntryIds)}).");
        }

        var verificationAudit = await RecordVerificationAuditAsync(
            detail,
            mismatchReasons,
            comparedFillCount,
            comparedOrderCount,
            comparedLedgerEntryCount,
            currentLedgerEntryCount,
            currentLedgerLineCount,
            persistedLedgerLineCount,
            lastPersistedFillAt,
            lastPersistedOrderUpdateAt,
            reconstruction,
            replayPortfolio,
            ct).ConfigureAwait(false);

        return new PaperSessionReplayVerificationDto(
            Summary: detail.Summary,
            Symbols: detail.Symbols,
            ReplaySource: _store is null ? "InMemoryFallback" : "DurableFillLog",
            IsConsistent: mismatchReasons.Count == 0,
            MismatchReasons: mismatchReasons,
            CurrentPortfolio: detail.Portfolio,
            ReplayPortfolio: replayPortfolio,
            VerifiedAt: DateTimeOffset.UtcNow,
            ComparedFillCount: comparedFillCount,
            ComparedOrderCount: comparedOrderCount,
            ComparedLedgerEntryCount: comparedLedgerEntryCount,
            CorruptLedgerEntryCount: reconstruction.CorruptEntryCount,
            CorruptLedgerEntryIds: reconstruction.CorruptEntryIds,
            LastPersistedFillAt: lastPersistedFillAt,
            LastPersistedOrderUpdateAt: lastPersistedOrderUpdateAt,
            VerificationAuditId: verificationAudit?.AuditId);
    }

    private async Task<ExecutionAuditEntry?> RecordVerificationAuditAsync(
        PaperSessionDetailDto detail,
        IReadOnlyList<string> mismatchReasons,
        int comparedFillCount,
        int comparedOrderCount,
        int comparedLedgerEntryCount,
        int currentLedgerEntryCount,
        int currentLedgerLineCount,
        int persistedLedgerLineCount,
        DateTimeOffset? lastPersistedFillAt,
        DateTimeOffset? lastPersistedOrderUpdateAt,
        LedgerReconstructionResult reconstruction,
        ExecutionPortfolioSnapshotDto replayPortfolio,
        CancellationToken ct)
    {
        if (_auditTrail is null)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sessionId"] = detail.Summary.SessionId,
            ["strategyId"] = detail.Summary.StrategyId,
            ["replaySource"] = _store is null ? "InMemoryFallback" : "DurableFillLog",
            ["isConsistent"] = (mismatchReasons.Count == 0).ToString(),
            ["comparedFillCount"] = comparedFillCount.ToString(),
            ["comparedOrderCount"] = comparedOrderCount.ToString(),
            ["comparedLedgerEntryCount"] = comparedLedgerEntryCount.ToString(),
            ["currentLedgerEntryCount"] = currentLedgerEntryCount.ToString(),
            ["currentLedgerLineCount"] = currentLedgerLineCount.ToString(),
            ["persistedLedgerLineCount"] = persistedLedgerLineCount.ToString(),
            ["lastPersistedFillAt"] = lastPersistedFillAt?.ToString("O") ?? string.Empty,
            ["lastPersistedOrderUpdateAt"] = lastPersistedOrderUpdateAt?.ToString("O") ?? string.Empty,
            ["mismatchCount"] = mismatchReasons.Count.ToString(),
            ["corruptLedgerEntryCount"] = reconstruction.CorruptEntryCount.ToString(),
            ["corruptLedgerEntryIds"] = string.Join(",", reconstruction.CorruptEntryIds),
            ["primaryMismatchReason"] = mismatchReasons.FirstOrDefault() ?? string.Empty
        };

        return await _auditTrail.RecordAsync(
            category: "PaperSession",
            action: "VerifyReplay",
            outcome: mismatchReasons.Count == 0 ? "Completed" : "AttentionRequired",
            actor: "PaperSessionPersistenceService",
            correlationId: detail.Summary.SessionId,
            message: mismatchReasons.Count == 0
                ? $"Replay verification completed for {detail.Summary.SessionId} (cash {replayPortfolio.Cash})."
                : $"Replay verification mismatch for {detail.Summary.SessionId}: {mismatchReasons[0]}",
            metadata: metadata,
            ct: ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Ledger serialisation helpers
    // ------------------------------------------------------------------

    private static IReadOnlyList<PersistedJournalEntryDto> SerializeLedgerJournal(
        IReadOnlyLedger ledger,
        string strategyId)
    {
        var dtos = new List<PersistedJournalEntryDto>(ledger.JournalEntryCount);
        foreach (var entry in ledger.Journal)
        {
            var lines = entry.Lines.Select(line => new PersistedLedgerLineDto(
                EntryId: line.EntryId,
                JournalEntryId: line.JournalEntryId,
                Timestamp: line.Timestamp,
                Account: new PersistedLedgerAccountDto(
                    Name: line.Account.Name,
                    AccountType: line.Account.AccountType.ToString(),
                    Symbol: line.Account.Symbol,
                    FinancialAccountId: line.Account.FinancialAccountId),
                Debit: line.Debit,
                Credit: line.Credit,
                Description: line.Description)).ToArray();

            dtos.Add(new PersistedJournalEntryDto(
                JournalEntryId: entry.JournalEntryId,
                Timestamp: entry.Timestamp,
                Description: entry.Description,
                Lines: lines,
                ActivityType: entry.Metadata.ActivityType?.ToString(),
                Symbol: entry.Metadata.Symbol,
                SecurityId: entry.Metadata.SecurityId,
                OrderId: entry.Metadata.OrderId,
                LedgerView: entry.Metadata.LedgerView?.ToString(),
                StrategyId: strategyId));
        }

        return dtos;
    }

    private async Task PersistSessionLedgerAsync(
        PaperSession session,
        CancellationToken ct,
        bool requireDurableSuccess = false)
    {
        if (_store is null)
        {
            return;
        }

        IReadOnlyLedger? ledger;
        lock (session.SyncRoot)
            ledger = session.Portfolio?.Ledger;
        if (ledger is null || ledger.JournalEntryCount == 0)
        {
            return;
        }

        var dtos = SerializeLedgerJournal(ledger, session.SessionId);
        try
        {
            await _store.SaveLedgerJournalAsync(session.SessionId, dtos, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (requireDurableSuccess)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist final ledger journal for paper session {SessionId}; session close was not published",
                    session.SessionId);
                throw;
            }

            _logger.LogWarning(
                ex,
                "Failed to persist ledger journal snapshot for paper session {SessionId}; continuing with in-memory ledger state",
                session.SessionId);
        }
    }

    private LedgerReconstructionResult ReconstructLedger(IReadOnlyList<PersistedJournalEntryDto> dtos)
    {
        if (dtos.Count == 0)
            return LedgerReconstructionResult.Empty;

        var ledger = new Meridian.Ledger.Ledger();
        var corruptEntryIds = new List<string>();
        foreach (var dto in dtos)
        {
            try
            {
                var lines = dto.Lines.Select(line =>
                {
                    var accountType = Enum.TryParse<LedgerAccountType>(line.Account.AccountType, out var at)
                        ? at : LedgerAccountType.Asset;
                    var account = new LedgerAccount(
                        line.Account.Name, accountType,
                        line.Account.Symbol, line.Account.FinancialAccountId);
                    return new LedgerEntry(
                        line.EntryId, line.JournalEntryId, line.Timestamp,
                        account, line.Debit, line.Credit, line.Description);
                }).ToArray();

                var entry = new JournalEntry(
                    dto.JournalEntryId,
                    dto.Timestamp,
                    dto.Description,
                    lines);

                ledger.Post(entry);
            }
            catch (Exception ex)
            {
                var accountHint = dto.Lines?.FirstOrDefault()?.Account?.Name ?? "unknown";
                corruptEntryIds.Add(dto.JournalEntryId.ToString("D"));
                _logger.LogWarning(
                    ex,
                    "Skipping corrupt persisted ledger journal entry {JournalEntryId} (symbol={Symbol}, accountHint={AccountHint}) during paper session reconstruction.",
                    dto.JournalEntryId,
                    dto.Symbol ?? "unknown",
                    accountHint);
            }
        }

        return new LedgerReconstructionResult(ledger, corruptEntryIds);
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private ConcurrentDictionary<string, PaperSession> CurrentSessions =>
        Volatile.Read(ref _sessions);

    private SemaphoreSlim GetSessionGate(string sessionId) =>
        _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));

    private static PaperSessionSummaryDto CreateRetentionSnapshot(PaperSession session) =>
        ToSummarySnapshot(session);

    private static PaperSessionSummaryDto ToSummarySnapshot(PaperSession session)
    {
        lock (session.SyncRoot)
        {
            return new PaperSessionSummaryDto(
                SessionId: session.SessionId,
                StrategyId: session.StrategyId,
                StrategyName: session.StrategyName,
                InitialCash: session.InitialCash,
                CreatedAt: session.CreatedAt,
                ClosedAt: session.ClosedAt,
                IsActive: session.IsActive,
                MatchingModelVersion: session.MatchingModelVersion,
                CostModelVersion: session.CostModelVersion);
        }
    }

    private static PersistedSessionRecord ToPersistedRecord(
        PaperSession session,
        bool? isActive = null,
        DateTimeOffset? closedAt = null)
    {
        lock (session.SyncRoot)
        {
            return new PersistedSessionRecord(
                SessionId: session.SessionId,
                StrategyId: session.StrategyId,
                StrategyName: session.StrategyName,
                InitialCash: session.InitialCash,
                CreatedAt: session.CreatedAt,
                ClosedAt: closedAt ?? session.ClosedAt,
                IsActive: isActive ?? session.IsActive,
                Symbols: session.Symbols.ToList(),
                MatchingModelVersion: session.MatchingModelVersion,
                CostModelVersion: session.CostModelVersion);
        }
    }

    private static PaperTradingPortfolio BuildPortfolioProjection(
        PaperSession session,
        ExecutionReport? candidate = null)
    {
        ExecutionReport[] fills;
        lock (session.SyncRoot)
        {
            var snapshots = session.FillHistory.Select(CloneExecutionReport);
            if (candidate is not null)
                snapshots = snapshots.Append(CloneExecutionReport(candidate));
            fills = snapshots.ToArray();
        }

        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(session.InitialCash, ledger);
        foreach (var fill in fills)
            portfolio.ApplyFill(fill);
        return portfolio;
    }

    private static IReadOnlyLedger CreateLedgerSnapshot(IReadOnlyList<JournalEntry> journal)
    {
        var snapshot = new Meridian.Ledger.Ledger();
        foreach (var entry in journal)
            snapshot.Post(entry);
        return snapshot;
    }

    private static ExecutionPortfolioSnapshotDto CreatePortfolioSnapshot(PaperTradingPortfolio portfolio)
    {
        var positions = portfolio.Positions.Values.Cast<ExecutionPosition>().ToArray();
        return new ExecutionPortfolioSnapshotDto(
            Cash: portfolio.Cash,
            PortfolioValue: portfolio.PortfolioValue,
            UnrealisedPnl: portfolio.UnrealisedPnl,
            RealisedPnl: portfolio.RealisedPnl,
            Positions: positions,
            AsOf: DateTimeOffset.UtcNow);
    }

    private static ExecutionReport CloneExecutionReport(ExecutionReport fill) => fill with
    {
        OptionContract = fill.OptionContract is null ? null : fill.OptionContract with { },
        Legs = fill.Legs?.Select(static leg => leg with
        {
            OptionContract = leg.OptionContract is null ? null : leg.OptionContract with { }
        }).ToArray(),
        Diagnostics = fill.Diagnostics is null ? null : fill.Diagnostics with { }
    };

    private static OrderState CloneOrderState(OrderState order) => order with
    {
        OptionContract = order.OptionContract is null ? null : order.OptionContract with { },
        Legs = order.Legs?.Select(static leg => leg with
        {
            OptionContract = leg.OptionContract is null ? null : leg.OptionContract with { }
        }).ToArray()
    };

    private void ValidatePersistedRecord(PersistedSessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.SessionId))
            throw new InvalidDataException("Paper-session metadata has no SessionId.");
        if (record.Symbols is null)
            throw new InvalidDataException($"Paper-session metadata for '{record.SessionId}' has no symbol list.");

        try
        {
            ValidateCreateRequest(new CreatePaperSessionDto(
                record.StrategyId,
                record.StrategyName,
                record.InitialCash,
                record.Symbols));
            _ = new PaperTradingPortfolio(record.InitialCash);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(
                $"Paper-session metadata for '{record.SessionId}' is invalid.",
                ex);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private OperationLease EnterOperation(CancellationToken callerToken)
    {
        lock (_operationSync)
        {
            ThrowIfDisposed();
            _activeOperations++;
            try
            {
                return new OperationLease(
                    this,
                    CancellationTokenSource.CreateLinkedTokenSource(callerToken, _lifetimeCts.Token));
            }
            catch
            {
                _activeOperations--;
                throw;
            }
        }
    }

    private void ExitOperation()
    {
        TaskCompletionSource? drained = null;
        lock (_operationSync)
        {
            _activeOperations--;
            if (_activeOperations == 0 && _operationsDrained is not null)
            {
                drained = _operationsDrained;
                _operationsDrained = null;
            }
        }

        drained?.TrySetResult();
    }

    /// <summary>Cancels an in-flight initialisation attempt and closes this service to new work.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Task operationsDrained;
        lock (_operationSync)
        {
            operationsDrained = _activeOperations == 0
                ? Task.CompletedTask
                : (_operationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        Task? initialisationTask;
        lock (_initialisationSync)
            initialisationTask = _initialisationTask;

        if (initialisationTask is not null)
        {
            try
            {
                await initialisationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
                // Expected when disposal interrupts store hydration.
            }
            catch
            {
                // Initialisation failures were already observed by their caller; disposal only
                // guarantees the attempt is no longer running.
            }
        }

        await operationsDrained.ConfigureAwait(false);
        _lifetimeCts.Dispose();
    }

    private sealed class OperationLease(
        PaperSessionPersistenceService owner,
        CancellationTokenSource cancellation) : IDisposable
    {
        private PaperSessionPersistenceService? _owner = owner;

        public CancellationToken Token => cancellation.Token;

        public void Dispose()
        {
            cancellation.Dispose();
            Interlocked.Exchange(ref _owner, null)?.ExitOperation();
        }
    }

    private static List<string> ComparePortfolios(
        ExecutionPortfolioSnapshotDto? current,
        ExecutionPortfolioSnapshotDto replay)
    {
        var mismatchReasons = new List<string>();
        if (current is null)
        {
            mismatchReasons.Add("Current session portfolio is unavailable for comparison.");
            return mismatchReasons;
        }

        CompareDecimal("cash", current.Cash, replay.Cash, mismatchReasons);
        CompareDecimal("portfolio value", current.PortfolioValue, replay.PortfolioValue, mismatchReasons);
        CompareDecimal("unrealised PnL", current.UnrealisedPnl, replay.UnrealisedPnl, mismatchReasons);
        CompareDecimal("realised PnL", current.RealisedPnl, replay.RealisedPnl, mismatchReasons);

        var currentPositions = current.Positions.ToDictionary(
            static position => position.Symbol,
            StringComparer.OrdinalIgnoreCase);
        var replayPositions = replay.Positions.ToDictionary(
            static position => position.Symbol,
            StringComparer.OrdinalIgnoreCase);

        foreach (var currentSymbol in currentPositions.Keys.Except(replayPositions.Keys, StringComparer.OrdinalIgnoreCase))
        {
            mismatchReasons.Add($"Replay is missing position {currentSymbol}.");
        }

        foreach (var replaySymbol in replayPositions.Keys.Except(currentPositions.Keys, StringComparer.OrdinalIgnoreCase))
        {
            mismatchReasons.Add($"Replay produced unexpected position {replaySymbol}.");
        }

        foreach (var symbol in currentPositions.Keys.Intersect(replayPositions.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var currentPosition = currentPositions[symbol];
            var replayPosition = replayPositions[symbol];

            if (currentPosition.Quantity != replayPosition.Quantity)
            {
                mismatchReasons.Add(
                    $"Position {symbol} quantity differs: current={currentPosition.Quantity:G29}, replay={replayPosition.Quantity:G29}.");
            }

            CompareDecimal(
                $"{symbol} average cost basis",
                currentPosition.AverageCostBasis,
                replayPosition.AverageCostBasis,
                mismatchReasons);
            CompareDecimal(
                $"{symbol} unrealised PnL",
                currentPosition.UnrealisedPnl,
                replayPosition.UnrealisedPnl,
                mismatchReasons);
            CompareDecimal(
                $"{symbol} realised PnL",
                currentPosition.RealisedPnl,
                replayPosition.RealisedPnl,
                mismatchReasons);
        }

        return mismatchReasons;
    }

    private static void CompareDecimal(
        string label,
        decimal current,
        decimal replay,
        List<string> mismatchReasons)
    {
        if (current != replay)
        {
            mismatchReasons.Add($"{label} differs: current={current:G29}, replay={replay:G29}.");
        }
    }

    private static void CompareOrderHistory(
        IReadOnlyList<OrderState>? currentOrders,
        IReadOnlyList<OrderState> persistedOrders,
        List<string> mismatchReasons)
    {
        var currentCount = currentOrders?.Count ?? 0;
        if (currentCount != persistedOrders.Count)
        {
            mismatchReasons.Add(
                $"Persisted order history count differs: current={currentCount}, persisted={persistedOrders.Count}.");
        }

        if (currentOrders is null || currentOrders.Count == 0 || persistedOrders.Count == 0)
        {
            return;
        }

        var currentById = currentOrders
            .GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
        var persistedById = persistedOrders
            .GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var orderId in currentById.Keys.Except(persistedById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            mismatchReasons.Add($"Persisted order history is missing order {orderId}.");
        }

        foreach (var orderId in persistedById.Keys.Except(currentById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            mismatchReasons.Add($"Persisted order history contains unexpected order {orderId}.");
        }

        foreach (var orderId in currentById.Keys.Intersect(persistedById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var current = currentById[orderId];
            var persisted = persistedById[orderId];
            if (current.Status != persisted.Status)
            {
                mismatchReasons.Add(
                    $"Order {orderId} status differs: current={current.Status}, persisted={persisted.Status}.");
            }

            if (current.FilledQuantity != persisted.FilledQuantity)
            {
                mismatchReasons.Add(
                    $"Order {orderId} filled quantity differs: current={current.FilledQuantity:G29}, persisted={persisted.FilledQuantity:G29}.");
            }
        }
    }

    private static void CompareLedgerJournal(
        IReadOnlyLedger? currentLedger,
        IReadOnlyList<PersistedJournalEntryDto> persistedLedgerEntries,
        List<string> mismatchReasons)
    {
        if (currentLedger is null)
        {
            if (persistedLedgerEntries.Count > 0)
            {
                mismatchReasons.Add(
                    $"Current session ledger is unavailable while {persistedLedgerEntries.Count} persisted journal entr{(persistedLedgerEntries.Count == 1 ? "y exists" : "ies exist")}.");
            }

            return;
        }

        if (currentLedger.JournalEntryCount != persistedLedgerEntries.Count)
        {
            mismatchReasons.Add(
                $"Persisted ledger journal count differs: current={currentLedger.JournalEntryCount}, persisted={persistedLedgerEntries.Count}.");
        }

        var persistedLineCount = persistedLedgerEntries.Sum(static entry => entry.Lines.Count);
        if (currentLedger.TotalLedgerEntryCount != persistedLineCount)
        {
            mismatchReasons.Add(
                $"Persisted ledger line count differs: current={currentLedger.TotalLedgerEntryCount}, persisted={persistedLineCount}.");
        }

        CompareTrialBalance(currentLedger.TrialBalance(), BuildPersistedTrialBalance(persistedLedgerEntries), mismatchReasons);
    }

    private static IReadOnlyDictionary<LedgerAccount, decimal> BuildPersistedTrialBalance(
        IReadOnlyList<PersistedJournalEntryDto> persistedLedgerEntries)
    {
        var balances = new Dictionary<LedgerAccount, decimal>();
        foreach (var line in persistedLedgerEntries.SelectMany(static entry => entry.Lines))
        {
            var accountType = Enum.TryParse<LedgerAccountType>(line.Account.AccountType, out var parsedAccountType)
                ? parsedAccountType
                : LedgerAccountType.Asset;
            var account = new LedgerAccount(
                line.Account.Name,
                accountType,
                line.Account.Symbol,
                line.Account.FinancialAccountId);
            balances.TryGetValue(account, out var balance);
            balances[account] = balance + Meridian.Ledger.Ledger.CalculateNetBalance(accountType, line.Debit, line.Credit);
        }

        return balances;
    }

    private static void CompareTrialBalance(
        IReadOnlyDictionary<LedgerAccount, decimal> current,
        IReadOnlyDictionary<LedgerAccount, decimal> persisted,
        List<string> mismatchReasons)
    {
        foreach (var account in current.Keys.Except(persisted.Keys))
        {
            mismatchReasons.Add($"Persisted ledger trial balance is missing account {account}.");
        }

        foreach (var account in persisted.Keys.Except(current.Keys))
        {
            mismatchReasons.Add($"Persisted ledger trial balance contains unexpected account {account}.");
        }

        foreach (var account in current.Keys.Intersect(persisted.Keys))
        {
            if (current[account] != persisted[account])
            {
                mismatchReasons.Add(
                    $"Ledger balance for {account} differs: current={current[account]:G29}, persisted={persisted[account]:G29}.");
            }
        }
    }

    private static DateTimeOffset? ResolveLastOrderUpdatedAt(IReadOnlyList<OrderState> orderHistory)
    {
        if (orderHistory.Count == 0)
        {
            return null;
        }

        return orderHistory
            .Select(static order => order.LastUpdatedAt ?? order.CreatedAt)
            .Max();
    }

    private static PaperSessionDetailDto BuildSessionDetailDto(PaperSession session)
    {
        lock (session.SyncRoot)
        {
            var portfolioSnapshot = session.Portfolio is null
                ? null
                : CreatePortfolioSnapshot(session.Portfolio);
            var orderHistory = session.OrderHistory.Select(CloneOrderState).ToArray();
            var fillHistory = session.FillHistory.Select(CloneExecutionReport).ToArray();
            var ledger = session.IsActive
                ? session.Portfolio?.Ledger ?? session.ReconstructedLedger
                : session.ReconstructedLedger ?? session.Portfolio?.Ledger;

            return new PaperSessionDetailDto(
                Summary: new PaperSessionSummaryDto(
                    session.SessionId,
                    session.StrategyId,
                    session.StrategyName,
                    session.InitialCash,
                    session.CreatedAt,
                    session.ClosedAt,
                    session.IsActive,
                    session.MatchingModelVersion,
                    session.CostModelVersion),
                Symbols: session.Symbols.ToArray(),
                Portfolio: portfolioSnapshot,
                OrderHistory: orderHistory,
                FillCount: fillHistory.Length,
                LedgerEntryCount: ledger?.JournalEntryCount ?? 0,
                LastFillAt: fillHistory.Length > 0
                    ? fillHistory.Max(static fill => fill.Timestamp)
                    : null,
                LastOrderUpdatedAt: ResolveLastOrderUpdatedAt(orderHistory),
                FillHistory: fillHistory,
                TradingCosts: fillHistory.Sum(static fill =>
                    (fill.Commission ?? 0m) + (fill.Fees ?? 0m) + (fill.SlippageCost ?? 0m)));
        }
    }

    private sealed class PaperSession
    {
        public object SyncRoot { get; } = new();
        public required string SessionId { get; init; }
        public required string StrategyId { get; init; }
        public string? StrategyName { get; init; }
        public decimal InitialCash { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ClosedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public List<string> Symbols { get; init; } = [];
        public string? MatchingModelVersion { get; init; }
        public string? CostModelVersion { get; init; }
        public PaperTradingPortfolio? Portfolio { get; set; }
        public List<OrderState> OrderHistory { get; } = [];
        public List<ExecutionReport> FillHistory { get; } = [];
        public Dictionary<Guid, string> AppliedFillHashes { get; } = [];

        /// <summary>
        /// Ledger reconstructed from persisted JSONL entries on load (closed sessions only).
        /// For active sessions use <c>Portfolio.Ledger</c> instead.
        /// </summary>
        public IReadOnlyLedger? ReconstructedLedger { get; init; }
        public LedgerReconstructionResult Reconstruction { get; init; } = LedgerReconstructionResult.Empty;
    }
}


// --- DTOs used by the service (decoupled from endpoint DTOs) ---

/// <summary>Request to create a new paper session.</summary>
public sealed record CreatePaperSessionDto(
    string StrategyId,
    string? StrategyName,
    decimal InitialCash = 100_000m,
    IReadOnlyList<string>? Symbols = null);

/// <summary>Session summary DTO.</summary>
public sealed record PaperSessionSummaryDto(
    string SessionId,
    string StrategyId,
    string? StrategyName,
    decimal InitialCash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    bool IsActive,
    string? MatchingModelVersion = null,
    string? CostModelVersion = null);

/// <summary>
/// Detailed session DTO. <see cref="TradingCosts"/> is the session's total explicit
/// transaction cost (commission + fees + modeled slippage) across all fills.
/// </summary>
public sealed record PaperSessionDetailDto(
    PaperSessionSummaryDto Summary,
    IReadOnlyList<string> Symbols,
    ExecutionPortfolioSnapshotDto? Portfolio,
    IReadOnlyList<OrderState>? OrderHistory,
    int FillCount,
    int LedgerEntryCount,
    DateTimeOffset? LastFillAt,
    DateTimeOffset? LastOrderUpdatedAt,
    IReadOnlyList<ExecutionReport>? FillHistory = null,
    decimal TradingCosts = 0m);

/// <summary>Portfolio snapshot DTO for session detail.</summary>
public sealed record ExecutionPortfolioSnapshotDto(
    decimal Cash,
    decimal PortfolioValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    IReadOnlyList<ExecutionPosition> Positions,
    DateTimeOffset AsOf)
{
    /// <summary>Alias for <see cref="Cash"/> using the naming used in operator-facing surfaces.</summary>
    public decimal CashBalance => Cash;
}

internal sealed record LedgerReconstructionResult(
    Meridian.Ledger.Ledger? Ledger,
    IReadOnlyList<string> CorruptEntryIds)
{
    public static LedgerReconstructionResult Empty { get; } = new(null, []);
    public int CorruptEntryCount => CorruptEntryIds.Count;
}

/// <summary>
/// Result of replaying a paper session and comparing the replayed state to the
/// currently tracked portfolio snapshot.
/// </summary>
public sealed record PaperSessionReplayVerificationDto(
    PaperSessionSummaryDto Summary,
    IReadOnlyList<string> Symbols,
    string ReplaySource,
    bool IsConsistent,
    IReadOnlyList<string> MismatchReasons,
    ExecutionPortfolioSnapshotDto? CurrentPortfolio,
    ExecutionPortfolioSnapshotDto ReplayPortfolio,
    DateTimeOffset VerifiedAt,
    int ComparedFillCount,
    int ComparedOrderCount,
    int ComparedLedgerEntryCount,
    int CorruptLedgerEntryCount,
    IReadOnlyList<string> CorruptLedgerEntryIds,
    DateTimeOffset? LastPersistedFillAt,
    DateTimeOffset? LastPersistedOrderUpdateAt,
    string? VerificationAuditId)
{
    /// <summary>Alias for <see cref="ComparedFillCount"/> using the paper-cockpit acceptance naming.</summary>
    public int VerifiedFilledCount => ComparedFillCount;

    /// <summary>Alias for <see cref="ComparedOrderCount"/> using the paper-cockpit acceptance naming.</summary>
    public int VerifiedOrderCount => ComparedOrderCount;

    /// <summary>Alias for <see cref="ComparedLedgerEntryCount"/> using the paper-cockpit acceptance naming.</summary>
    public int VerifiedLedgerEntriesCount => ComparedLedgerEntryCount;

    /// <summary>Alias for <see cref="VerifiedAt"/> using the paper-cockpit acceptance naming.</summary>
    public DateTimeOffset LastVerifiedAt => VerifiedAt;
}
