using System.Collections.Concurrent;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
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
    private readonly ILogger<PaperSessionPersistenceService> _logger;
    private int _initialised; // 0 = not yet, 1 = done

    public PaperSessionPersistenceService(
        ILogger<PaperSessionPersistenceService> logger,
        IPaperSessionStore? store = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store;
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
        var session = new PaperSession
        {
            SessionId = sessionId,
            StrategyId = request.StrategyId,
            StrategyName = request.StrategyName,
            InitialCash = request.InitialCash,
            CreatedAt = DateTimeOffset.UtcNow,
            Symbols = request.Symbols?.ToList() ?? [],
            Portfolio = new PaperTradingPortfolio(request.InitialCash),
        };

        _sessions[sessionId] = session;

        if (_store is not null)
        {
            await _store.SaveSessionMetadataAsync(ToPersistedRecord(session), ct).ConfigureAwait(false);
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
            var positions = session.Portfolio.Positions.Values.ToArray();
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

    /// <summary>Records an order status update for a session.</summary>
    public void RecordOrderUpdate(string sessionId, OrderState orderState)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.OrderHistory.Add(orderState);
        }

        if (_store is not null)
        {
            // Fire-and-forget — order-history persistence is best-effort.
            _ = _store.AppendOrderUpdateAsync(sessionId, orderState)
                .ContinueWith(
                    t => _logger.LogWarning(t.Exception, "Failed to persist order update for session {SessionId}", sessionId),
                    default,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
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
        if (meta is null)
            return null;

        // Replay fills through a fresh portfolio.
        var fills = await _store.LoadFillsAsync(sessionId, ct).ConfigureAwait(false);
        var portfolio = new PaperTradingPortfolio(meta.InitialCash);
        foreach (var fill in fills)
            portfolio.ApplyFill(fill);

        var positions = portfolio.Positions.Values.ToArray();
        return new ExecutionPortfolioSnapshotDto(
            Cash: portfolio.Cash,
            PortfolioValue: portfolio.PortfolioValue,
            UnrealisedPnl: portfolio.UnrealisedPnl,
            RealisedPnl: portfolio.RealisedPnl,
            Positions: positions,
            AsOf: DateTimeOffset.UtcNow);
    }

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
