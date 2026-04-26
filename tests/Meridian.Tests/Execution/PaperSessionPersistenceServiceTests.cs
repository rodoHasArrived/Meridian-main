using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed class PaperSessionPersistenceServiceTests
{
    private static PaperSessionPersistenceService Build() =>
        new(NullLogger<PaperSessionPersistenceService>.Instance);

    private static PaperSessionPersistenceService Build(IPaperSessionStore store) =>
        new(NullLogger<PaperSessionPersistenceService>.Instance, store);

    // ---- CreateSessionAsync ----

    [Fact]
    public async Task CreateSessionAsync_ReturnsNewSession_WithMatchingStrategyId()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-1", "My Strategy", 50_000m);

        var summary = await service.CreateSessionAsync(dto);

        summary.StrategyId.Should().Be("strat-1");
        summary.StrategyName.Should().Be("My Strategy");
        summary.InitialCash.Should().Be(50_000m);
        summary.IsActive.Should().BeTrue();
        summary.ClosedAt.Should().BeNull();
        summary.SessionId.Should().StartWith("PAPER-");
    }

    [Fact]
    public async Task CreateSessionAsync_TwoCalls_ProduceDistinctSessionIds()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-1", null, 100_000m);

        var s1 = await service.CreateSessionAsync(dto);
        var s2 = await service.CreateSessionAsync(dto);

        s1.SessionId.Should().NotBe(s2.SessionId);
    }

    [Fact]
    public async Task CreateSessionAsync_WithNullSymbols_CreatesSessionWithEmptySymbolList()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-sym", null, 100_000m, Symbols: null);

        var summary = await service.CreateSessionAsync(dto);

        summary.Should().NotBeNull();
        summary.SessionId.Should().StartWith("PAPER-");
    }

    // ---- GetSessions ----

    [Fact]
    public async Task GetSessions_AfterCreation_ContainsNewSession()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-2", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);

        var sessions = service.GetSessions();

        sessions.Should().ContainSingle(s => s.SessionId == summary.SessionId);
    }

    [Fact]
    public void GetSessions_WhenEmpty_ReturnsEmptyList()
    {
        var service = Build();

        var sessions = service.GetSessions();

        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessions_MultipleSessions_ReturnsAllOrderedByCreationTimeDescending()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-order", null, 100_000m);
        var s1 = await service.CreateSessionAsync(dto);
        await Task.Delay(5); // ensure distinct creation timestamps
        var s2 = await service.CreateSessionAsync(dto);

        var sessions = service.GetSessions();

        sessions.Should().HaveCount(2);
        // Most-recently created comes first
        sessions[0].SessionId.Should().Be(s2.SessionId);
        sessions[1].SessionId.Should().Be(s1.SessionId);
    }

    // ---- GetSession ----

    [Fact]
    public async Task GetSession_WhenSessionExists_ReturnsDetail()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-3", "Detail Test", 100_000m, ["AAPL", "MSFT"]);
        var summary = await service.CreateSessionAsync(dto);

        var detail = service.GetSession(summary.SessionId);

        detail.Should().NotBeNull();
        detail!.Summary.SessionId.Should().Be(summary.SessionId);
        detail.Symbols.Should().Equal("AAPL", "MSFT");
        detail.Portfolio.Should().NotBeNull();
        detail.OrderHistory.Should().BeEmpty();
    }

    [Fact]
    public void GetSession_WhenSessionNotFound_ReturnsNull()
    {
        var service = Build();

        var detail = service.GetSession("nonexistent-session");

        detail.Should().BeNull();
    }

    // ---- CloseSessionAsync ----

    [Fact]
    public async Task CloseSessionAsync_WhenSessionExists_ReturnsTrue()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-4", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);

        var closed = await service.CloseSessionAsync(summary.SessionId);

        closed.Should().BeTrue();
    }

    [Fact]
    public async Task CloseSessionAsync_WhenSessionExists_MarksInactive()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-5", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);

        await service.CloseSessionAsync(summary.SessionId);

        var sessions = service.GetSessions();
        sessions.Should().ContainSingle(s => s.SessionId == summary.SessionId && !s.IsActive);
    }

    [Fact]
    public async Task CloseSessionAsync_WhenSessionExists_SetsClosedAt()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-5b", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);
        var before = DateTimeOffset.UtcNow;

        await service.CloseSessionAsync(summary.SessionId);

        var sessions = service.GetSessions();
        var closed = sessions.Single(s => s.SessionId == summary.SessionId);
        closed.ClosedAt.Should().NotBeNull();
        closed.ClosedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task CloseSessionAsync_WhenSessionNotFound_ReturnsFalse()
    {
        var service = Build();

        var closed = await service.CloseSessionAsync("does-not-exist");

        closed.Should().BeFalse();
    }

    // ---- GetActivePortfolio ----

    [Fact]
    public async Task GetActivePortfolio_AfterCreation_ReturnsPortfolio()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-6", null, 75_000m);
        var summary = await service.CreateSessionAsync(dto);

        var portfolio = service.GetActivePortfolio(summary.SessionId);

        portfolio.Should().NotBeNull();
        portfolio!.Cash.Should().Be(75_000m);
    }

    [Fact]
    public async Task GetActivePortfolio_AfterClose_ReturnsNull()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-7", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);
        await service.CloseSessionAsync(summary.SessionId);

        var portfolio = service.GetActivePortfolio(summary.SessionId);

        portfolio.Should().BeNull();
    }

    [Fact]
    public void GetActivePortfolio_WhenSessionNotFound_ReturnsNull()
    {
        var service = Build();

        var portfolio = service.GetActivePortfolio("unknown-session");

        portfolio.Should().BeNull();
    }

    // ---- RecordOrderUpdateAsync ----

    [Fact]
    public async Task RecordOrderUpdateAsync_WhenSessionActive_AppendsToOrderHistory()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-8", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);
        var orderState = new OrderState
        {
            OrderId = "order-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 100m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await service.RecordOrderUpdateAsync(summary.SessionId, orderState);

        var detail = service.GetSession(summary.SessionId);
        detail!.OrderHistory.Should().ContainSingle(o => o.OrderId == "order-1");
    }

    [Fact]
    public async Task RecordOrderUpdateAsync_MultipleUpdates_AppendsAllInOrder()
    {
        var service = Build();
        var dto = new CreatePaperSessionDto("strat-9", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);
        var order1 = new OrderState
        {
            OrderId = "order-A",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 100m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var order2 = new OrderState
        {
            OrderId = "order-B",
            Symbol = "MSFT",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 50m,
            LimitPrice = 350m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await service.RecordOrderUpdateAsync(summary.SessionId, order1);
        await service.RecordOrderUpdateAsync(summary.SessionId, order2);

        var detail = service.GetSession(summary.SessionId);
        detail!.OrderHistory.Should().HaveCount(2);
        detail.OrderHistory[0].OrderId.Should().Be("order-A");
        detail.OrderHistory[1].OrderId.Should().Be("order-B");
    }

    [Fact]
    public async Task RecordOrderUpdateAsync_WhenSessionNotFound_DoesNotThrow()
    {
        var service = Build();
        var orderState = new OrderState
        {
            OrderId = "order-x",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 100m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Should silently ignore unknown session IDs
        Func<Task> action = () => service.RecordOrderUpdateAsync("nonexistent-session", orderState);

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordOrderUpdateAsync_WhenStoreFails_PropagatesException()
    {
        var service = Build(new ThrowingOrderUpdateStore(new IOException("disk full")));
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-order-fail", null, 10_000m));
        var orderState = new OrderState
        {
            OrderId = "order-fail",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Func<Task> act = () => service.RecordOrderUpdateAsync(summary.SessionId, orderState);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task RecordOrderUpdateAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var service = Build(new ThrowingOrderUpdateStore());
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-order-cancel", null, 10_000m));
        var orderState = new OrderState
        {
            OrderId = "order-cancel",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => service.RecordOrderUpdateAsync(summary.SessionId, orderState, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecordFillAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var service = Build(new ThrowingOrderUpdateStore());
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-fill-cancel", null, 10_000m));
        var fill = new ExecutionReport
        {
            OrderId = "fill-cancel",
            ReportType = ExecutionReportType.Fill,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 10m,
            FilledQuantity = 10m,
            FillPrice = 100m
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => service.RecordFillAsync(summary.SessionId, fill, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// ---------------------------------------------------------------------------
// File-backed durable persistence tests
// ---------------------------------------------------------------------------

/// <summary>
/// Tests for <see cref="PaperSessionPersistenceService"/> backed by
/// <see cref="JsonlFilePaperSessionStore"/>.  Uses a temp directory that is
/// deleted after each test class run.
/// </summary>
public sealed class PaperSessionDurablePersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public PaperSessionDurablePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "meridian_paper_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    private JsonlFilePaperSessionStore BuildStore() =>
        new(_tempDir, NullLogger<JsonlFilePaperSessionStore>.Instance);

    private static PaperSessionPersistenceService Build(IPaperSessionStore store) =>
        new(NullLogger<PaperSessionPersistenceService>.Instance, store);

    private static ExecutionReport BuildFill(string symbol, OrderSide side, decimal qty, decimal price) =>
        new()
        {
            OrderId = Guid.NewGuid().ToString("N"),
            ReportType = ExecutionReportType.Fill,
            Symbol = symbol,
            Side = side,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = qty,
            FilledQuantity = qty,
            FillPrice = price
        };

    // ---- CreateSession persists to disk ----

    [Fact]
    public async Task CreateSessionAsync_WithStore_WritesSessionJsonToDisk()
    {
        var store = BuildStore();
        var service = Build(store);
        var dto = new CreatePaperSessionDto("strat-1", "Durable Test", 10_000m);

        var summary = await service.CreateSessionAsync(dto);

        var sessionDir = Path.Combine(_tempDir, summary.SessionId);
        File.Exists(Path.Combine(sessionDir, "session.json")).Should().BeTrue();
    }

    // ---- CloseSession updates disk ----

    [Fact]
    public async Task CloseSessionAsync_WithStore_UpdatesSessionJsonOnDisk()
    {
        var store = BuildStore();
        var service = Build(store);
        var dto = new CreatePaperSessionDto("strat-2", null, 10_000m);
        var summary = await service.CreateSessionAsync(dto);

        await service.CloseSessionAsync(summary.SessionId);

        var records = await store.LoadAllSessionsAsync();
        var record = records.Single(r => r.SessionId == summary.SessionId);
        record.IsActive.Should().BeFalse();
        record.ClosedAt.Should().NotBeNull();
    }

    // ---- RecordFillAsync persists fills to JSONL ----

    [Fact]
    public async Task RecordFillAsync_WithStore_AppendsFillToJsonlFile()
    {
        var store = BuildStore();
        var service = Build(store);
        var dto = new CreatePaperSessionDto("strat-3", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);
        var fill = BuildFill("AAPL", OrderSide.Buy, qty: 10m, price: 200m);

        await service.RecordFillAsync(summary.SessionId, fill);

        var fills = await store.LoadFillsAsync(summary.SessionId);
        fills.Should().ContainSingle(f => f.Symbol == "AAPL" && f.FillPrice == 200m);
    }

    [Fact]
    public async Task RecordFillAsync_MultipleFills_AllPersistedInOrder()
    {
        var store = BuildStore();
        var service = Build(store);
        var dto = new CreatePaperSessionDto("strat-4", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);

        await service.RecordFillAsync(summary.SessionId, BuildFill("AAPL", OrderSide.Buy, 10m, 200m));
        await service.RecordFillAsync(summary.SessionId, BuildFill("MSFT", OrderSide.Buy, 5m, 400m));

        var fills = await store.LoadFillsAsync(summary.SessionId);
        fills.Should().HaveCount(2);
        fills[0].Symbol.Should().Be("AAPL");
        fills[1].Symbol.Should().Be("MSFT");
    }

    // ---- InitialiseAsync reloads sessions after restart ----

    [Fact]
    public async Task InitialiseAsync_AfterRestart_ReloadsAllSessions()
    {
        var store = BuildStore();

        // First "process": create sessions and record fills.
        var svc1 = Build(store);
        var dto = new CreatePaperSessionDto("strat-reload", null, 50_000m);
        var summary = await svc1.CreateSessionAsync(dto);
        await svc1.RecordFillAsync(summary.SessionId, BuildFill("AAPL", OrderSide.Buy, 10m, 150m));

        // Second "process": create a fresh service with the same store.
        var svc2 = Build(store);
        await svc2.InitialiseAsync();

        var sessions = svc2.GetSessions();
        sessions.Should().ContainSingle(s => s.SessionId == summary.SessionId);
    }

    [Fact]
    public async Task InitialiseAsync_AfterRestart_ReconstructsPortfolioFromFills()
    {
        var store = BuildStore();
        const decimal InitialCash = 100_000m;
        const decimal FillPrice = 200m;
        const decimal FillQty = 10m;

        // First "process": create session and record a buy fill.
        var svc1 = Build(store);
        var summary = await svc1.CreateSessionAsync(
            new CreatePaperSessionDto("strat-pf", null, InitialCash));
        await svc1.RecordFillAsync(summary.SessionId,
            BuildFill("AAPL", OrderSide.Buy, FillQty, FillPrice));

        // Second "process": fresh service, initialise from store.
        var svc2 = Build(store);
        await svc2.InitialiseAsync();

        var detail = svc2.GetSession(summary.SessionId);
        detail.Should().NotBeNull();
        detail!.Portfolio.Should().NotBeNull();
        // Cash should be reduced by the buy cost.
        detail.Portfolio!.Cash.Should().Be(InitialCash - FillQty * FillPrice);
    }

    [Fact]
    public async Task InitialiseAsync_CalledTwice_OnlyLoadsOnce()
    {
        var store = BuildStore();
        var svc1 = Build(store);
        await svc1.CreateSessionAsync(new CreatePaperSessionDto("strat-once", null, 10_000m));

        var svc2 = Build(store);
        await svc2.InitialiseAsync();
        await svc2.InitialiseAsync(); // Second call is a no-op.

        svc2.GetSessions().Should().HaveCount(1);
    }

    [Fact]
    public async Task InitialiseAsync_RestoresClosedSessions_WithIsActiveFalse()
    {
        var store = BuildStore();
        var svc1 = Build(store);
        var summary = await svc1.CreateSessionAsync(new CreatePaperSessionDto("strat-closed", null, 10_000m));
        await svc1.CloseSessionAsync(summary.SessionId);

        var svc2 = Build(store);
        await svc2.InitialiseAsync();

        var sessions = svc2.GetSessions();
        sessions.Should().ContainSingle(s => s.SessionId == summary.SessionId && !s.IsActive);
    }

    [Fact]
    public async Task InitialiseAsync_RestoresOrderHistory()
    {
        var store = BuildStore();
        var svc1 = Build(store);
        var summary = await svc1.CreateSessionAsync(new CreatePaperSessionDto("strat-orders", null, 10_000m));
        var order = new OrderState
        {
            OrderId = "O-1",
            Symbol = "TSLA",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await svc1.RecordOrderUpdateAsync(summary.SessionId, order);

        var svc2 = Build(store);
        await svc2.InitialiseAsync();

        var detail = svc2.GetSession(summary.SessionId);
        detail!.OrderHistory.Should().ContainSingle(o => o.OrderId == "O-1");
    }
}

// ---------------------------------------------------------------------------
// ReplaySessionAsync tests
// ---------------------------------------------------------------------------

public sealed class PaperSessionReplayTests : IDisposable
{
    private readonly string _tempDir;

    public PaperSessionReplayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "meridian_replay_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    private JsonlFilePaperSessionStore BuildStore() =>
        new(_tempDir, NullLogger<JsonlFilePaperSessionStore>.Instance);

    private static PaperSessionPersistenceService Build(
        IPaperSessionStore? store = null,
        ExecutionAuditTrailService? auditTrail = null) =>
        new(NullLogger<PaperSessionPersistenceService>.Instance, store, auditTrail);

    private static ExecutionReport BuyFill(string symbol, decimal qty, decimal price) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Buy,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = qty,
        FilledQuantity = qty,
        FillPrice = price
    };

    private static ExecutionReport SellFill(string symbol, decimal qty, decimal price) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Sell,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = qty,
        FilledQuantity = qty,
        FillPrice = price
    };

    // ---- In-memory fallback ----

    [Fact]
    public async Task ReplaySessionAsync_NoStore_ReturnsSameAsGetSession()
    {
        var service = Build(store: null);
        var dto = new CreatePaperSessionDto("strat-A", null, 100_000m);
        var summary = await service.CreateSessionAsync(dto);
        await service.RecordFillAsync(summary.SessionId, BuyFill("AAPL", 10m, 200m));

        var replay = await service.ReplaySessionAsync(summary.SessionId);
        var detail = service.GetSession(summary.SessionId);

        replay.Should().NotBeNull();
        replay!.Cash.Should().Be(detail!.Portfolio!.Cash);
    }

    [Fact]
    public async Task ReplaySessionAsync_NoStore_UnknownSession_ReturnsNull()
    {
        var service = Build(store: null);

        var result = await service.ReplaySessionAsync("unknown-session");

        result.Should().BeNull();
    }

    // ---- File-backed replay ----

    [Fact]
    public async Task ReplaySessionAsync_WithStore_ReconstructsCashFromBuyFill()
    {
        var store = BuildStore();
        var service = Build(store);
        const decimal InitialCash = 100_000m;
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-B", null, InitialCash));

        await service.RecordFillAsync(summary.SessionId, BuyFill("AAPL", 10m, 200m));

        var replay = await service.ReplaySessionAsync(summary.SessionId);

        replay.Should().NotBeNull();
        replay!.Cash.Should().Be(InitialCash - 10m * 200m);
    }

    [Fact]
    public async Task ReplaySessionAsync_WithStore_ReflectsOpenPosition()
    {
        var store = BuildStore();
        var service = Build(store);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-C", null, 100_000m));

        await service.RecordFillAsync(summary.SessionId, BuyFill("TSLA", 5m, 300m));

        var replay = await service.ReplaySessionAsync(summary.SessionId);

        replay!.Positions.Should().Contain(p => p.Symbol == "TSLA");
    }

    [Fact]
    public async Task ReplaySessionAsync_WithStore_BuyThenSell_ReflectsRoundTripPnl()
    {
        var store = BuildStore();
        var service = Build(store);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-D", null, 100_000m));

        await service.RecordFillAsync(summary.SessionId, BuyFill("AAPL", 10m, 150m));
        await service.RecordFillAsync(summary.SessionId, SellFill("AAPL", 10m, 200m));

        var replay = await service.ReplaySessionAsync(summary.SessionId);

        // Cash = 100_000 − 1_500 + 2_000 = 100_500
        replay!.Cash.Should().Be(100_500m);
        replay.RealisedPnl.Should().Be(500m); // (200 − 150) × 10
    }

    [Fact]
    public async Task ReplaySessionAsync_WithStore_UnknownSession_ReturnsNull()
    {
        var store = BuildStore();
        var service = Build(store);

        var result = await service.ReplaySessionAsync("does-not-exist");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReplaySessionAsync_MatchesLivePortfolio()
    {
        var store = BuildStore();
        var service = Build(store);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-E", null, 100_000m));

        await service.RecordFillAsync(summary.SessionId, BuyFill("AAPL", 20m, 180m));
        await service.RecordFillAsync(summary.SessionId, BuyFill("MSFT", 15m, 300m));

        // Live state.
        var liveDetail = service.GetSession(summary.SessionId);

        // Replay.
        var replay = await service.ReplaySessionAsync(summary.SessionId);

        replay!.Cash.Should().Be(liveDetail!.Portfolio!.Cash);
        replay.RealisedPnl.Should().Be(liveDetail.Portfolio.RealisedPnl);
    }

    [Fact]
    public async Task VerifyReplayAsync_WithStore_ReturnsConsistentVerification()
    {
        var store = BuildStore();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_tempDir, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = Build(store, auditTrail);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-F", "Replay Verify", 100_000m, ["AAPL"]));

        await service.RecordFillAsync(summary.SessionId, BuyFill("AAPL", 12m, 150m));
        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "verify-order-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 12m,
            FilledQuantity = 12m,
            Status = OrderStatus.Filled,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        });

        var verification = await service.VerifyReplayAsync(summary.SessionId);

        verification.Should().NotBeNull();
        verification!.Summary.SessionId.Should().Be(summary.SessionId);
        verification.Symbols.Should().Equal("AAPL");
        verification.ReplaySource.Should().Be("DurableFillLog");
        verification.IsConsistent.Should().BeTrue();
        verification.MismatchReasons.Should().BeEmpty();
        verification.CurrentPortfolio.Should().NotBeNull();
        verification.ReplayPortfolio.Cash.Should().Be(100_000m - (12m * 150m));
        verification.ComparedFillCount.Should().Be(1);
        verification.ComparedOrderCount.Should().Be(1);
        verification.ComparedLedgerEntryCount.Should().BeGreaterThan(0);
        verification.LastPersistedFillAt.Should().NotBeNull();
        verification.LastPersistedOrderUpdateAt.Should().NotBeNull();
        verification.VerificationAuditId.Should().NotBeNullOrWhiteSpace();

        var auditEntries = await auditTrail.GetAllAsync();
        auditEntries.Should().Contain(entry =>
            entry.AuditId == verification.VerificationAuditId &&
            entry.Action == "VerifyReplay");
        var auditEntry = auditEntries.Single(entry => entry.AuditId == verification.VerificationAuditId);
        auditEntry.Metadata.Should().NotBeNull();
        auditEntry.Metadata!["isConsistent"].Should().Be(bool.TrueString);
        auditEntry.Metadata["currentLedgerEntryCount"].Should().NotBe("0");
        auditEntry.Metadata["currentLedgerLineCount"].Should().NotBe("0");
        auditEntry.Metadata["persistedLedgerLineCount"].Should().NotBe("0");
        auditEntry.Metadata["lastPersistedFillAt"].Should().NotBeNullOrWhiteSpace();
        auditEntry.Metadata["lastPersistedOrderUpdateAt"].Should().NotBeNullOrWhiteSpace();
        auditEntry.Metadata["primaryMismatchReason"].Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyReplayAsync_WhenPersistedOrderHistoryDiffers_ReturnsMismatchWithCounts()
    {
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_tempDir, "mismatch-audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = Build(new ReplayMismatchStore(), auditTrail);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-mismatch", null, 100_000m, ["AAPL"]));

        var verification = await service.VerifyReplayAsync(summary.SessionId);

        verification.Should().NotBeNull();
        verification!.IsConsistent.Should().BeFalse();
        verification.ComparedOrderCount.Should().Be(1);
        verification.LastPersistedOrderUpdateAt.Should().NotBeNull();
        verification.MismatchReasons.Should().Contain(reason =>
            reason.Contains("Persisted order history count", StringComparison.OrdinalIgnoreCase));

        var auditEntries = await auditTrail.GetAllAsync();
        var auditEntry = auditEntries.Single(entry => entry.AuditId == verification.VerificationAuditId);
        auditEntry.Outcome.Should().Be("AttentionRequired");
        auditEntry.Message.Should().Contain("Persisted order history count");
        auditEntry.Metadata.Should().NotBeNull();
        auditEntry.Metadata!["isConsistent"].Should().Be(bool.FalseString);
        auditEntry.Metadata["primaryMismatchReason"].Should().Contain("Persisted order history count");
    }

    [Fact]
    public async Task VerifyReplayAsync_WhenPersistedLedgerJournalIsMissing_ReturnsMismatchWithLedgerReason()
    {
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_tempDir, "ledger-mismatch-audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = Build(new MissingLedgerJournalStore(), auditTrail);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-ledger-mismatch", null, 100_000m, ["AAPL"]));

        var verification = await service.VerifyReplayAsync(summary.SessionId);

        verification.Should().NotBeNull();
        verification!.IsConsistent.Should().BeFalse();
        verification.ComparedLedgerEntryCount.Should().Be(0);
        verification.MismatchReasons.Should().Contain(reason =>
            reason.Contains("Persisted ledger journal count differs", StringComparison.OrdinalIgnoreCase));

        var auditEntries = await auditTrail.GetAllAsync();
        var auditEntry = auditEntries.Single(entry => entry.AuditId == verification.VerificationAuditId);
        auditEntry.Outcome.Should().Be("AttentionRequired");
        auditEntry.Metadata.Should().NotBeNull();
        auditEntry.Metadata!["currentLedgerEntryCount"].Should().NotBe("0");
        auditEntry.Metadata["persistedLedgerLineCount"].Should().Be("0");
        auditEntry.Metadata["primaryMismatchReason"].Should().Contain("Persisted ledger journal count");
    }

    [Fact]
    public async Task VerifyReplayAsync_UnknownSession_ReturnsNull()
    {
        var service = Build(BuildStore());

        var verification = await service.VerifyReplayAsync("missing-session");

        verification.Should().BeNull();
    }
}

internal sealed class ThrowingOrderUpdateStore : IPaperSessionStore
{
    private readonly Exception? _appendException;

    public ThrowingOrderUpdateStore(Exception? appendException = null)
    {
        _appendException = appendException;
    }

    public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_appendException is not null)
        {
            return Task.FromException(_appendException);
        }

        return Task.CompletedTask;
    }

    public Task SaveLedgerJournalAsync(
        string sessionId,
        IReadOnlyList<PersistedJournalEntryDto> entries,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([]);

    public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExecutionReport>>([]);

    public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrderState>>([]);

    public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
        string sessionId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
}

internal sealed class ReplayMismatchStore : IPaperSessionStore
{
    public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SaveLedgerJournalAsync(
        string sessionId,
        IReadOnlyList<PersistedJournalEntryDto> entries,
        CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([]);

    public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExecutionReport>>([]);

    public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrderState>>([
            new OrderState
            {
                OrderId = "persisted-order-1",
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = OrderType.Market,
                Quantity = 5m,
                Status = OrderStatus.Accepted,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-4)
            }
        ]);

    public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
        string sessionId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
}

internal sealed class MissingLedgerJournalStore : IPaperSessionStore
{
    public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SaveLedgerJournalAsync(
        string sessionId,
        IReadOnlyList<PersistedJournalEntryDto> entries,
        CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([]);

    public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExecutionReport>>([]);

    public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrderState>>([]);

    public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
        string sessionId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
}
