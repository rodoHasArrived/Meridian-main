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
public sealed class PaperSessionPersistenceService
{
    private readonly ConcurrentDictionary<string, PaperSession> _sessions = new(StringComparer.Ordinal);
    private readonly IPaperSessionStore? _store;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly ILogger<PaperSessionPersistenceService> _logger;
    private int _initialised; // 0 = not yet, 1 = done

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
    /// Loads all sessions from the durable store and reconstructs portfolio
    /// state by replaying the persisted fill log.  Safe to call multiple times;
    /// only executes once.  No-op when no <see cref="IPaperSessionStore"/> was
    /// provided.
    /// </summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        if (_store is null || Interlocked.Exchange(ref _initialised, 1) != 0)
            return;

        var records = await _store.LoadAllSessionsAsync(ct).ConfigureAwait(false);
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();

            // Reconstruct portfolio by replaying the persisted fill log.
            var fills = await _store.LoadFillsAsync(record.SessionId, ct).ConfigureAwait(false);
            var portfolio = new PaperTradingPortfolio(record.InitialCash);
            foreach (var fill in fills)
                portfolio.ApplyFill(fill);

            // Reconstruct the ledger from its persisted journal entries so past runs
            // remain queryable by LedgerReadService without a live portfolio.
            var ledgerEntries = await _store.LoadLedgerJournalAsync(record.SessionId, ct).ConfigureAwait(false);
            var reconstructedLedger = ReconstructLedger(ledgerEntries);

            // Load persisted order history.
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
                ReconstructedLedger = reconstructedLedger,
            };
            foreach (var order in orders)
                session.OrderHistory.Add(order);

            _sessions[record.SessionId] = session;
        }

        _logger.LogInformation(
            "Initialised paper session store: loaded {Count} session(s)", records.Count);
    }

    // ------------------------------------------------------------------
    // CRUD
    // ------------------------------------------------------------------

    /// <summary>Creates a new paper trading session and returns its summary.</summary>
    public async Task<PaperSessionSummaryDto> CreateSessionAsync(CreatePaperSessionDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = $"PAPER-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}";

        // Create a ledger for double-entry accounting of fills and commissions.
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
        };

        _sessions[sessionId] = session;

        if (_store is not null)
        {
            await _store.SaveSessionMetadataAsync(ToPersistedRecord(session), ct).ConfigureAwait(false);
            await PersistSessionLedgerAsync(session, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Created paper session {SessionId} for strategy {StrategyId} with {InitialCash:C} initial capital",
            sessionId, request.StrategyId, request.InitialCash);

        return ToSummary(session);
    }

    /// <summary>Closes a paper trading session and snapshots its final state.</summary>
    public async Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        session.ClosedAt = DateTimeOffset.UtcNow;
        session.IsActive = false;

        if (_store is not null)
        {
            await _store.SaveSessionMetadataAsync(ToPersistedRecord(session), ct).ConfigureAwait(false);

            // Persist the double-entry ledger journal so it can be queried from the workstation
            // UI's LedgerReadService even after the process restarts.
            var ledger = session.Portfolio?.Ledger;
            if (ledger is not null && ledger.JournalEntryCount > 0)
            {
                try
                {
                    await PersistSessionLedgerAsync(session, ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Persisted {Count} ledger entries for paper session {SessionId}",
                        ledger.JournalEntryCount, sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist ledger journal for session {SessionId}", sessionId);
                }
            }
        }

        _logger.LogInformation(
            "Closed paper session {SessionId} — final equity: {Equity:C}",
            sessionId, session.Portfolio?.PortfolioValue ?? 0m);

        return true;
    }

    /// <summary>Returns summaries of all tracked sessions.</summary>
    public IReadOnlyList<PaperSessionSummaryDto> GetSessions()
    {
        return _sessions.Values
            .Select(ToSummary)
            .OrderByDescending(static s => s.CreatedAt)
            .ToArray();
    }

    /// <summary>Returns detailed session state or <c>null</c> if not found.</summary>
    public PaperSessionDetailDto? GetSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        ExecutionPortfolioSnapshotDto? portfolioSnapshot = null;
        if (session.Portfolio is not null)
        {
            var positions = session.Portfolio.Positions.Values.Cast<ExecutionPosition>().ToArray();
            portfolioSnapshot = new ExecutionPortfolioSnapshotDto(
                Cash: session.Portfolio.Cash,
                PortfolioValue: session.Portfolio.PortfolioValue,
                UnrealisedPnl: session.Portfolio.UnrealisedPnl,
                RealisedPnl: session.Portfolio.RealisedPnl,
                Positions: positions,
                AsOf: DateTimeOffset.UtcNow);
        }

        return new PaperSessionDetailDto(
            Summary: ToSummary(session),
            Symbols: session.Symbols.ToArray(),
            Portfolio: portfolioSnapshot,
            OrderHistory: session.OrderHistory.ToArray());
    }

    /// <summary>Returns the portfolio state for a live session, or null.</summary>
    public PaperTradingPortfolio? GetActivePortfolio(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) && session.IsActive
            ? session.Portfolio
            : null;
    }

    /// <summary>
    /// Returns the double-entry ledger for a session — either the live ledger (active sessions)
    /// or the ledger reconstructed from the persisted journal (closed sessions).
    /// Returns <see langword="null"/> when no ledger data is available for the session.
    /// </summary>
    public IReadOnlyLedger? GetLedger(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        // For active sessions return the live ledger; for closed sessions the reconstructed one.
        return session.Portfolio?.Ledger ?? session.ReconstructedLedger;
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
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.OrderHistory.Add(orderState);
        }

        if (_store is not null)
        {
            await _store.AppendOrderUpdateAsync(sessionId, orderState, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies a fill execution report to the session portfolio and persists
    /// the fill event to the durable store for future replay.
    /// No-op for non-fill reports (accepted, cancelled, rejected).
    /// </summary>
    public async Task RecordFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fill);

        if (_sessions.TryGetValue(sessionId, out var session) && session.IsActive)
        {
            session.Portfolio?.ApplyFill(fill);
        }

        if (_store is not null)
        {
            await _store.AppendFillAsync(sessionId, fill, ct).ConfigureAwait(false);

            if (_sessions.TryGetValue(sessionId, out var persistedSession))
            {
                await PersistSessionLedgerAsync(persistedSession, ct).ConfigureAwait(false);
            }
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
        if (_store is null)
        {
            // Fall back to current in-memory state.
            return GetSession(sessionId)?.Portfolio;
        }

        // Load session metadata to get initial cash.
        var allRecords = await _store.LoadAllSessionsAsync(ct).ConfigureAwait(false);
        var meta = allRecords.FirstOrDefault(r => r.SessionId == sessionId);
        var initialCash = meta?.InitialCash;
        if (!initialCash.HasValue)
        {
            if (!_sessions.TryGetValue(sessionId, out var activeSession))
                return null;

            initialCash = activeSession.InitialCash;
        }

        // Replay fills through a fresh portfolio.
        var fills = await _store.LoadFillsAsync(sessionId, ct).ConfigureAwait(false);
        var portfolio = new PaperTradingPortfolio(initialCash.Value);
        foreach (var fill in fills)
            portfolio.ApplyFill(fill);

        var positions = portfolio.Positions.Values.Cast<ExecutionPosition>().ToArray();
        return new ExecutionPortfolioSnapshotDto(
            Cash: portfolio.Cash,
            PortfolioValue: portfolio.PortfolioValue,
            UnrealisedPnl: portfolio.UnrealisedPnl,
            RealisedPnl: portfolio.RealisedPnl,
            Positions: positions,
            AsOf: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Verifies that the current session portfolio matches the replayed fill-log state.
    /// This gives operators an explicit continuity check for paper sessions that are
    /// expected to survive restarts and replay cleanly from durable fills.
    /// </summary>
    public async Task<PaperSessionReplayVerificationDto?> VerifyReplayAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var detail = GetSession(sessionId);
        if (detail is null)
        {
            return null;
        }

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
        var comparedFillCount = persistedFills.Count;
        var comparedOrderCount = _store is null
            ? detail.OrderHistory?.Count ?? 0
            : persistedOrders.Count;
        var comparedLedgerEntryCount = _store is null
            ? 0
            : persistedLedgerEntries.Count;
        var persistedLedgerLineCount = persistedLedgerEntries.Sum(static entry => entry.Lines.Count);
        var currentLedger = GetLedger(sessionId);
        var currentLedgerEntryCount = currentLedger?.JournalEntryCount ?? 0;
        var currentLedgerLineCount = currentLedger?.TotalLedgerEntryCount ?? 0;
        var lastPersistedFillAt = persistedFills.Count > 0
            ? persistedFills.Max(fill => fill.Timestamp)
            : (DateTimeOffset?)null;
        var lastPersistedOrderUpdateAt = persistedOrders.Count > 0
            ? persistedOrders
                .Where(order => order.LastUpdatedAt.HasValue)
                .Select(order => order.LastUpdatedAt!.Value)
                .DefaultIfEmpty(persistedOrders.Max(order => order.CreatedAt))
                .Max()
            : (DateTimeOffset?)null;
        if (_store is not null)
        {
            CompareOrderHistory(detail.OrderHistory, persistedOrders, mismatchReasons);
            CompareLedgerJournal(currentLedger, persistedLedgerEntries, mismatchReasons);
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

    private async Task PersistSessionLedgerAsync(PaperSession session, CancellationToken ct)
    {
        if (_store is null)
        {
            return;
        }

        var ledger = session.Portfolio?.Ledger;
        if (ledger is null || ledger.JournalEntryCount == 0)
        {
            return;
        }

        var dtos = SerializeLedgerJournal(ledger, session.SessionId);
        await _store.SaveLedgerJournalAsync(session.SessionId, dtos, ct).ConfigureAwait(false);
    }

    private static Meridian.Ledger.Ledger? ReconstructLedger(IReadOnlyList<PersistedJournalEntryDto> dtos)
    {
        if (dtos.Count == 0)
            return null;

        var ledger = new Meridian.Ledger.Ledger();
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
            catch (Exception)
            {
                // Skip corrupt entries — best-effort reconstruction.
            }
        }

        return ledger;
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static PaperSessionSummaryDto ToSummary(PaperSession session) => new(
        SessionId: session.SessionId,
        StrategyId: session.StrategyId,
        StrategyName: session.StrategyName,
        InitialCash: session.InitialCash,
        CreatedAt: session.CreatedAt,
        ClosedAt: session.ClosedAt,
        IsActive: session.IsActive);

    private static PersistedSessionRecord ToPersistedRecord(PaperSession session) => new(
        SessionId: session.SessionId,
        StrategyId: session.StrategyId,
        StrategyName: session.StrategyName,
        InitialCash: session.InitialCash,
        CreatedAt: session.CreatedAt,
        ClosedAt: session.ClosedAt,
        IsActive: session.IsActive,
        Symbols: session.Symbols);

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
            balances[account] = balance + CalculateNormalBalanceDelta(accountType, line.Debit, line.Credit);
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

    private static decimal CalculateNormalBalanceDelta(
        LedgerAccountType accountType,
        decimal debit,
        decimal credit)
        => accountType is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? debit - credit
            : credit - debit;

    private sealed class PaperSession
    {
        public required string SessionId { get; init; }
        public required string StrategyId { get; init; }
        public string? StrategyName { get; init; }
        public decimal InitialCash { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ClosedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public List<string> Symbols { get; init; } = [];
        public PaperTradingPortfolio? Portfolio { get; init; }
        public List<OrderState> OrderHistory { get; } = [];

        /// <summary>
        /// Ledger reconstructed from persisted JSONL entries on load (closed sessions only).
        /// For active sessions use <c>Portfolio.Ledger</c> instead.
        /// </summary>
        public IReadOnlyLedger? ReconstructedLedger { get; init; }
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
    bool IsActive);

/// <summary>Detailed session DTO.</summary>
public sealed record PaperSessionDetailDto(
    PaperSessionSummaryDto Summary,
    IReadOnlyList<string> Symbols,
    ExecutionPortfolioSnapshotDto? Portfolio,
    IReadOnlyList<OrderState>? OrderHistory);

/// <summary>Portfolio snapshot DTO for session detail.</summary>
public sealed record ExecutionPortfolioSnapshotDto(
    decimal Cash,
    decimal PortfolioValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    IReadOnlyList<ExecutionPosition> Positions,
    DateTimeOffset AsOf);

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
    DateTimeOffset? LastPersistedFillAt,
    DateTimeOffset? LastPersistedOrderUpdateAt,
    string? VerificationAuditId);
