using System.Collections.Concurrent;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>
/// Manages paper trading session lifecycle and persistence.
/// Tracks session metadata, portfolio snapshots, and order history
/// across session boundaries. Sessions are persisted in-memory with
/// support for future durable storage backends.
/// </summary>
public sealed class PaperSessionPersistenceService
{
    private readonly ConcurrentDictionary<string, PaperSession> _sessions = new(StringComparer.Ordinal);
    private readonly ILogger<PaperSessionPersistenceService> _logger;

    public PaperSessionPersistenceService(ILogger<PaperSessionPersistenceService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Creates a new paper trading session and returns its summary.</summary>
    public Task<PaperSessionSummaryDto> CreateSessionAsync(CreatePaperSessionDto request, CancellationToken ct = default)
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

        _logger.LogInformation(
            "Created paper session {SessionId} for strategy {StrategyId} with {InitialCash:C} initial capital",
            sessionId, request.StrategyId, request.InitialCash);

        return Task.FromResult(ToSummary(session));
    }

    /// <summary>Closes a paper trading session and snapshots its final state.</summary>
    public Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(false);
        }

        session.ClosedAt = DateTimeOffset.UtcNow;
        session.IsActive = false;

        _logger.LogInformation(
            "Closed paper session {SessionId} — final equity: {Equity:C}",
            sessionId, session.Portfolio?.PortfolioValue ?? 0m);

        return Task.FromResult(true);
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
    }

    private static PaperSessionSummaryDto ToSummary(PaperSession session) => new(
        SessionId: session.SessionId,
        StrategyId: session.StrategyId,
        StrategyName: session.StrategyName,
        InitialCash: session.InitialCash,
        CreatedAt: session.CreatedAt,
        ClosedAt: session.ClosedAt,
        IsActive: session.IsActive);

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
