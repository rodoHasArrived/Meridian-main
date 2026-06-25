using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage;
using Meridian.Ui.Shared.Services;
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
        var orderUpdatedAt = DateTimeOffset.UtcNow;
        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "detail-order-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            FilledQuantity = 10m,
            Status = OrderStatus.Filled,
            CreatedAt = orderUpdatedAt.AddSeconds(-1),
            LastUpdatedAt = orderUpdatedAt
        });
        await service.RecordFillAsync(summary.SessionId, new ExecutionReport
        {
            OrderId = "detail-order-1",
            ReportType = ExecutionReportType.Fill,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 10m,
            FilledQuantity = 10m,
            FillPrice = 150m,
            Timestamp = orderUpdatedAt
        });

        var detail = service.GetSession(summary.SessionId);

        detail.Should().NotBeNull();
        detail!.Summary.SessionId.Should().Be(summary.SessionId);
        detail.Symbols.Should().Equal("AAPL", "MSFT");
        detail.Portfolio.Should().NotBeNull();
        detail.OrderHistory.Should().ContainSingle();
        detail.FillCount.Should().Be(1);
        detail.LedgerEntryCount.Should().BeGreaterThan(0);
        detail.LastFillAt.Should().NotBeNull();
        detail.LastOrderUpdatedAt.Should().Be(orderUpdatedAt);
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
    public async Task RecordFillAsync_WhenLedgerSnapshotSaveFails_DoesNotThrow()
    {
        var service = Build(new ThrowingLedgerSaveStore());
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-fill-ledger-save", null, 10_000m));
        var fill = new ExecutionReport
        {
            OrderId = "fill-ledger-save",
            ReportType = ExecutionReportType.Fill,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 10m,
            FilledQuantity = 10m,
            FillPrice = 100m
        };

        Func<Task> act = () => service.RecordFillAsync(summary.SessionId, fill);

        await act.Should().NotThrowAsync();
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

    [Fact]
    public async Task SessionContinuity_CreateRestartVerifyClose_PreservesScopeAndHistoryAcrossFlow()
    {
        var store = BuildStore();
        var orderUpdatedAt = DateTimeOffset.UtcNow;

        var svc1 = Build(store);
        var created = await svc1.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-wave2-session-continuity",
            StrategyName: "Wave2 Session Continuity",
            InitialCash: 75_000m,
            Symbols: ["AAPL", "MSFT"]));
        await svc1.RecordOrderUpdateAsync(created.SessionId, new OrderState
        {
            OrderId = "wave2-order-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            FilledQuantity = 10m,
            Status = OrderStatus.Filled,
            CreatedAt = orderUpdatedAt.AddSeconds(-1),
            LastUpdatedAt = orderUpdatedAt
        });
        await svc1.RecordFillAsync(created.SessionId, BuildFill("AAPL", OrderSide.Buy, 10m, 200m));

        var svc2 = Build(store);
        await svc2.InitialiseAsync();
        var restored = svc2.GetSession(created.SessionId);

        restored.Should().NotBeNull();
        restored!.Symbols.Should().Equal("AAPL", "MSFT");
        restored.OrderHistory.Should().ContainSingle(order => order.OrderId == "wave2-order-1");
        restored.FillHistory.Should().HaveCount(1);
        restored.LedgerEntryCount.Should().BeGreaterThan(0);

        var verification = await svc2.VerifyReplayAsync(created.SessionId);
        verification.Should().NotBeNull();
        verification!.IsConsistent.Should().BeTrue();
        verification.ComparedOrderCount.Should().Be(1);
        verification.ComparedFillCount.Should().Be(1);
        verification.ComparedLedgerEntryCount.Should().BeGreaterThan(0);

        var closed = await svc2.CloseSessionAsync(created.SessionId);
        closed.Should().BeTrue();

        var svc3 = Build(store);
        await svc3.InitialiseAsync();
        svc3.GetSessions().Should().ContainSingle(session => session.SessionId == created.SessionId && !session.IsActive);
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
    public async Task TradeStationExecutionSlice_CreateUpdateCancelAndFillReconciliation_ProducesDeterministicCanonicalEvidence()
    {
        var store = BuildStore();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_tempDir, "tradestation-audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = Build(store, auditTrail);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("tradestation-order-flow", "TradeStation slice", 100_000m, ["AAPL"]));

        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "ts-order-001",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            FilledQuantity = 0m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-30),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-30)
        });

        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "ts-order-001",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            FilledQuantity = 6m,
            Status = OrderStatus.PartiallyFilled,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-20),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-20)
        });

        await service.RecordFillAsync(summary.SessionId, BuyFill("AAPL", 6m, 150m));

        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "ts-order-001",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            FilledQuantity = 6m,
            Status = OrderStatus.Cancelled,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-10)
        });

        var verification = await service.VerifyReplayAsync(summary.SessionId);

        verification.Should().NotBeNull();
        verification!.IsConsistent.Should().BeTrue();
        verification.ComparedOrderCount.Should().BeGreaterThan(0);
        verification.ComparedFillCount.Should().Be(1);
        verification.MismatchReasons.Should().BeEmpty();
        verification.ReplayPortfolio.Positions.Should().ContainSingle(p => p.Symbol == "AAPL" && p.Quantity == 6m);
    }

    [Fact]
    public async Task TradeStationExecutionSlice_DelayedOutOfOrderEvents_RemainsIdempotentAndDeterministic()
    {
        var store = BuildStore();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_tempDir, "tradestation-out-of-order-audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = Build(store, auditTrail);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("tradestation-out-of-order", null, 100_000m, ["AAPL"]));

        await service.RecordFillAsync(summary.SessionId, BuyFill("AAPL", 4m, 200m));

        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "ts-order-oo-001",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 4m,
            FilledQuantity = 4m,
            Status = OrderStatus.Filled,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-5)
        });

        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "ts-order-oo-001",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 4m,
            FilledQuantity = 0m,
            Status = OrderStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-25),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-25)
        });

        var first = await service.VerifyReplayAsync(summary.SessionId);
        var second = await service.VerifyReplayAsync(summary.SessionId);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.IsConsistent.Should().BeTrue();
        second!.IsConsistent.Should().BeTrue();
        second.ComparedFillCount.Should().Be(first.ComparedFillCount);
        second.ComparedOrderCount.Should().Be(first.ComparedOrderCount);
        second.ReplayPortfolio.Cash.Should().Be(first.ReplayPortfolio.Cash);
        second.ReplayPortfolio.Positions.Should().BeEquivalentTo(first.ReplayPortfolio.Positions);
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
    public async Task VerifyReplayAsync_WhenLatestOrderHasNoLastUpdatedAt_UsesCreatedAtForTimestamp()
    {
        var store = BuildStore();
        var service = Build(store);
        var summary = await service.CreateSessionAsync(new CreatePaperSessionDto("strat-order-ts", null, 100_000m, ["AAPL"]));

        var olderUpdatedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var newerCreatedAt = olderUpdatedAt.AddHours(2);

        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "order-older-updated",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            Status = OrderStatus.Accepted,
            CreatedAt = olderUpdatedAt.AddMinutes(-10),
            LastUpdatedAt = olderUpdatedAt
        });

        await service.RecordOrderUpdateAsync(summary.SessionId, new OrderState
        {
            OrderId = "order-newer-created",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            Status = OrderStatus.Accepted,
            CreatedAt = newerCreatedAt,
            LastUpdatedAt = null
        });

        var verification = await service.VerifyReplayAsync(summary.SessionId);

        verification.Should().NotBeNull();
        verification!.LastPersistedOrderUpdateAt.Should().Be(newerCreatedAt);
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

    [Fact]
    public async Task VerifyReplayAsync_WhenLedgerJournalContainsCorruptEntries_ReportsDegradedButKeepsSuccessfulEntries()
    {
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_tempDir, "corrupt-ledger-audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = Build(new CorruptLedgerReplayStore(), auditTrail);
        await service.InitialiseAsync();

        var verification = await service.VerifyReplayAsync("PAPER-CORRUPT-001");

        verification.Should().NotBeNull();
        verification!.ReplayPortfolio.Positions.Should().ContainSingle(position => position.Symbol == "AAPL");
        verification.CorruptLedgerEntryCount.Should().Be(1);
        verification.CorruptLedgerEntryIds.Should().Contain("22222222-2222-2222-2222-222222222222");
        verification.IsConsistent.Should().BeFalse();
        verification.MismatchReasons.Should().Contain(reason =>
            reason.Contains("skipped 1 corrupt entry", StringComparison.OrdinalIgnoreCase));

        var auditEntries = await auditTrail.GetAllAsync();
        var auditEntry = auditEntries.Single(entry => entry.AuditId == verification.VerificationAuditId);
        auditEntry.Outcome.Should().Be("AttentionRequired");
        auditEntry.Metadata!["corruptLedgerEntryCount"].Should().Be("1");
        auditEntry.Metadata["corruptLedgerEntryIds"].Should().Contain("22222222-2222-2222-2222-222222222222");
    }
}

public sealed class PaperTradingAccountingScenarioTests : IDisposable
{
    private const decimal InitialCash = 100_000m;
    private const string TenantId = "tenant-alpha";
    private const string CompanyId = "company-alpha";
    private const string FundProfileId = "fund-alpha";
    private const string EntityId = "entity-master";
    private const string OperatorUserName = "ops.accountant@meridian.local";
    private const string OperatorRoleProfileName = "paper-trade-accounting-operator";
    private const string OperatorRoleDisplayName = "Paper Trade Accounting Operator";
    private const string SetupCorrelationId = "paper-accounting-real-user-setup";

    private readonly string _tempDir;

    public PaperTradingAccountingScenarioTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "meridian_paper_accounting_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp artifacts.
        }
    }

    [Fact]
    public async Task Scenario_PaperTradeOrderLifecycle_ProducesBalancedLedgerAndGovernedAccountingCandidate()
    {
        var ct = CancellationToken.None;
        var operatorProfile = await SetupOperatorProfileAsync(ct);
        var store = new JsonlFilePaperSessionStore(
            Path.Combine(_tempDir, "paper-session"),
            NullLogger<JsonlFilePaperSessionStore>.Instance);
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(_tempDir, "execution-audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            store,
            auditTrail);

        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto(
                StrategyId: "paper-accounting-scenario",
                StrategyName: $"{OperatorRoleDisplayName} accounting review",
                InitialCash: InitialCash,
                Symbols: ["AAPL"]),
            ct);
        session.StrategyName.Should().Contain(OperatorRoleDisplayName);

        var now = DateTimeOffset.Parse("2026-05-31T15:30:00Z");
        await service.RecordOrderUpdateAsync(
            session.SessionId,
            BuildOrder("paper-buy-1", OrderSide.Buy, 10m, 0m, OrderStatus.Accepted, now.AddMinutes(-30)),
            ct);
        await service.RecordOrderUpdateAsync(
            session.SessionId,
            BuildOrder("paper-buy-1", OrderSide.Buy, 10m, 10m, OrderStatus.Filled, now.AddMinutes(-25)),
            ct);
        await service.RecordFillAsync(
            session.SessionId,
            BuildFill("paper-buy-1", OrderSide.Buy, 10m, 200m, commission: 2.50m, now.AddMinutes(-25)),
            ct);

        await service.RecordOrderUpdateAsync(
            session.SessionId,
            BuildOrder("paper-sell-1", OrderSide.Sell, 4m, 4m, OrderStatus.Filled, now.AddMinutes(-10)),
            ct);
        await service.RecordFillAsync(
            session.SessionId,
            BuildFill("paper-sell-1", OrderSide.Sell, 4m, 230m, commission: 1.25m, now.AddMinutes(-10)),
            ct);

        var restoredService = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            store,
            auditTrail);
        await restoredService.InitialiseAsync(ct);
        var detail = restoredService.GetSession(session.SessionId);

        detail.Should().NotBeNull();
        detail!.OrderHistory.Should().HaveCount(3);
        detail.FillHistory.Should().HaveCount(2);
        detail.Portfolio.Should().NotBeNull();
        detail.Portfolio!.Cash.Should().Be(98_916.25m);
        detail.Portfolio.RealisedPnl.Should().Be(120m);
        detail.Portfolio.Positions.Should().ContainSingle(position =>
            position.Symbol == "AAPL" &&
            position.Quantity == 6m &&
            position.AverageCostBasis == 200m);

        var ledger = restoredService.GetLedger(session.SessionId);
        ledger.Should().NotBeNull();
        var liveLedger = ledger!;
        liveLedger.GetBalance(LedgerAccounts.Cash).Should().Be(98_916.25m);
        liveLedger.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(1_200m);
        liveLedger.GetBalance(LedgerAccounts.RealizedGain).Should().Be(120m);
        liveLedger.GetBalance(LedgerAccounts.CommissionExpense).Should().Be(3.75m);
        liveLedger.GetBalance(LedgerAccounts.CapitalAccount)
            .Should()
            .Be(InitialCash, "opening capital should be retained as equity for the paper account");

        var endingAssets = liveLedger.GetBalance(LedgerAccounts.Cash) + liveLedger.GetBalance(LedgerAccounts.Securities("AAPL"));
        var endingEquity =
            liveLedger.GetBalance(LedgerAccounts.CapitalAccount) +
            liveLedger.GetBalance(LedgerAccounts.RealizedGain) -
            liveLedger.GetBalance(LedgerAccounts.CommissionExpense);
        endingAssets.Should().Be(endingEquity);

        var trialBalance = liveLedger.TrialBalance();
        trialBalance[LedgerAccounts.CapitalAccount].Should().Be(InitialCash);
        var debitNormalTotal = trialBalance
            .Where(static item => item.Key.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense)
            .Sum(static item => item.Value);
        var creditNormalTotal = trialBalance
            .Where(static item => item.Key.AccountType is LedgerAccountType.Liability or LedgerAccountType.Equity or LedgerAccountType.Revenue)
            .Sum(static item => item.Value);
        debitNormalTotal.Should().Be(creditNormalTotal);

        var replay = await restoredService.VerifyReplayAsync(session.SessionId, ct);
        replay.Should().NotBeNull();
        replay!.IsConsistent.Should().BeTrue();
        replay.MismatchReasons.Should().BeEmpty();
        replay.ComparedOrderCount.Should().Be(3);
        replay.ComparedFillCount.Should().Be(2);
        replay.ComparedLedgerEntryCount.Should().BeGreaterThan(0);

        var evidenceLinks = new[]
        {
            $"paper-session://{session.SessionId}/orders/paper-buy-1/fills",
            $"paper-session://{session.SessionId}/orders/paper-sell-1/fills",
            $"paper-session://{session.SessionId}/replay/{replay.VerificationAuditId}",
            $"identity://{CompanyId}/users/{operatorProfile.Username}/role-profile/{operatorProfile.RoleProfileName}",
            operatorProfile.TenantAdministrationEvidenceLink
        };
        var labPreview = new InvestmentAccountingTransactionLabService().Preview(
            new InvestmentAccountingTransactionLabRequestDto(
                InvestmentAccountingTransactionKindDto.Trade,
                FundAccountId: FundProfileId,
                Symbol: "AAPL",
                EventDate: new DateOnly(2026, 5, 31),
                Currency: "USD",
                Amount: 920m,
                Quantity: 4m,
                Price: 230m,
                FeeAmount: 1.25m,
                Side: InvestmentAccountingTradeSideDto.Sell,
                SourceSessionId: session.SessionId,
                EvidenceIds: evidenceLinks,
                PreviewMode: InvestmentAccountingPreviewModeDto.BooksBeforeBroker));

        labPreview.JournalPreview.IsBalanced.Should().BeTrue();
        labPreview.LedgerImpact.HasValidationWarnings.Should().BeFalse();
        labPreview.TrialBalanceImpact.Should().Contain(line =>
            line.AccountName == "Cash" &&
            line.AccountType == "Asset" &&
            line.BalanceDelta == 918.75m);
        labPreview.TrialBalanceImpact.Should().Contain(line =>
            line.AccountName == "Investment Fees" &&
            line.AccountType == "Expense" &&
            line.BalanceDelta == 1.25m);
        labPreview.BooksBeforeBroker.Should().NotBeNull();
        labPreview.BooksBeforeBroker!.CanStageBrokerAction.Should().BeTrue();
        labPreview.BooksBeforeBroker.RequiredApprovals.Should().Contain("operator-accounting-approval");

        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreatePaperTradeCandidateServiceAsync(ledgerBookId, operatorProfile, ct);
        var candidateRequest = BuildCandidateRequest(
            session.SessionId,
            ledgerBookId,
            periodId,
            sourceEventId,
            operatorProfile,
            evidenceLinks);
        candidateRequest.Actor.Should().Be(operatorProfile.Username);
        candidateRequest.TenantId.Should().Be(operatorProfile.TenantId);
        candidateRequest.CompanyId.Should().Be(operatorProfile.CompanyId);

        var candidate = await candidateService.BuildCandidateAsync(candidateRequest, ct);

        candidate.HasBlockingIssues.Should().BeFalse(string.Join("; ", candidate.Issues.Select(issue => issue.Message)));
        candidate.SelectedRuleId.Should().Be("posting.paper-trade-sale");
        candidate.IsBalanced.Should().BeTrue();
        candidate.TotalDebits.Should().Be(920m);
        candidate.TotalCredits.Should().Be(920m);
        candidate.CanSubmitForApproval.Should().BeTrue();
        candidate.CanPostWithoutAdditionalApproval.Should().BeFalse();
        candidate.PostingCommand.Should().NotBeNull();
        candidate.PostingCommand!.LedgerBookId.Should().Be(ledgerBookId);
        candidate.PostingCommand.SourceEventType.Should().Be("PaperTradeFill");
        candidate.PostingCommand.TreasuryContext.Should().NotBeNull();
        candidate.PostingCommand.TreasuryContext!.IdempotencyKey.Should().Be($"paper-trade:{session.SessionId}:paper-sell-1");
        candidate.GeneratedPostingLines.Should().Contain(line =>
            line.AccountPath == "assets/cash" &&
            line.Side == AccountingTemplateLineSideDto.Debit &&
            line.Amount == 918.75m);
        candidate.GeneratedPostingLines.Should().Contain(line =>
            line.AccountPath == "assets/securities/aapl" &&
            line.Side == AccountingTemplateLineSideDto.Credit &&
            line.Amount == 800m);
        candidate.GeneratedPostingLines.Should().Contain(line =>
            line.AccountPath == "income/realized-gains" &&
            line.Side == AccountingTemplateLineSideDto.Credit &&
            line.Amount == 120m);
        candidate.GeneratedPostingLines.Should().Contain(line =>
            line.AccountPath == "expenses/commissions" &&
            line.Side == AccountingTemplateLineSideDto.Debit &&
            line.Amount == 1.25m);
        candidate.GeneratedPostingLines.Should().AllSatisfy(line =>
        {
            line.Dimensions.Should().NotBeNull();
            line.Dimensions!.FundId.Should().Be(FundProfileId);
            line.Dimensions.EntityId.Should().Be(EntityId);
            line.Dimensions.OrganizationId.Should().Be(TenantId);
            line.Dimensions.BookId.Should().Be(ledgerBookId.ToString("D"));
        });
        candidate.EvidenceLinks.Should().Contain(operatorProfile.TenantAdministrationEvidenceLink);
        candidate.PostingCommand.Evidence.Should().Contain(evidence =>
            evidence.Uri == operatorProfile.TenantAdministrationEvidenceLink &&
            evidence.RetainedBy == "financial-operations");

        var postService = new AccountingPostingCandidatePostService(candidateService);
        var automatedPost = async () => await postService.PostCandidateAsync(
            new PostPostingRuleJournalCandidateRequestDto(
                candidateRequest,
                Actor: operatorProfile.Username,
                ApprovalId: "approval-paper-trade-sell-202605",
                ApprovalNotes: "Operator reviewed paper trade replay and accounting evidence.",
                EvidenceLinks: evidenceLinks,
                CorrelationId: SetupCorrelationId,
                ActionOrigin: OperationsActionOriginDto.HumanOperator,
                TenantId: operatorProfile.TenantId,
                CompanyId: operatorProfile.CompanyId),
            ct);

        await automatedPost.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Postgres-backed ledger journal store*");
    }

    private async Task<OperatorProfile> SetupOperatorProfileAsync(CancellationToken ct)
    {
        const UserPermission permissions =
            UserPermission.ViewTrades |
            UserPermission.ExecuteTrades |
            UserPermission.ManageOrders |
            UserPermission.ViewAnalytics |
            UserPermission.ExportData |
            UserPermission.ViewConfig |
            UserPermission.ManageFundStructure;

        var identityRoot = Path.Combine(_tempDir, "identity");
        var storageOptions = new StorageOptions { RootPath = identityRoot };
        var roleStore = new FileRolePermissionProfileStore(storageOptions);
        var roleResult = await roleStore.UpsertAsync(
            new RolePermissionProfileUpsertRequestDto(
                ProfileName: OperatorRoleProfileName,
                DisplayName: OperatorRoleDisplayName,
                Description: "Can run paper trade accounting reviews and prepare governed posting candidates.",
                BaseRole: nameof(UserRole.TradeDesk),
                PermissionNames: RolePermissions.GetPermissionNames(permissions),
                RequestedBy: OperatorUserName,
                Rationale: "Realistic paper-trade operator profile for accounting scenario coverage.",
                CorrelationId: SetupCorrelationId),
            actor: OperatorUserName,
            ct);

        roleResult.Profile.Role.Should().Be(OperatorRoleProfileName);
        roleResult.Profile.DisplayName.Should().Be(OperatorRoleDisplayName);
        roleResult.Profile.PermissionMask.Should().Be((long)permissions);
        roleResult.Profile.Permissions.Should().Contain(nameof(UserPermission.ManageFundStructure));
        roleResult.AuditEvent.Actor.Should().Be(OperatorUserName);
        roleStore.TryGetProfile(OperatorRoleDisplayName, out var retainedRole).Should().BeTrue();
        retainedRole.PermissionMask.Should().Be((long)permissions);

        var userStore = new FileUserAccountStore(storageOptions);
        var userResult = await userStore.UpsertAsync(
            new UserAccountUpsertRequestDto(
                Username: OperatorUserName,
                Role: nameof(UserRole.TradeDesk),
                RoleProfileName: OperatorRoleProfileName,
                PermissionNames: RolePermissions.GetPermissionNames(permissions),
                NewPassword: "PaperAccounting!2026",
                PasswordHash: null,
                IsDisabled: false,
                PasswordResetRequired: false,
                RequestedBy: OperatorUserName,
                Rationale: "Provision paper-trade accounting operator for scenario testing.",
                CorrelationId: SetupCorrelationId,
                CompanyId: CompanyId),
            actor: OperatorUserName,
            ct);

        userResult.Account.Username.Should().Be(OperatorUserName);
        userResult.Account.Role.Should().Be(nameof(UserRole.TradeDesk));
        userResult.Account.RoleProfileName.Should().Be(OperatorRoleProfileName);
        userResult.Account.CompanyId.Should().Be(CompanyId);
        userResult.Account.IsDisabled.Should().BeFalse();
        userResult.Account.PermissionMask.Should().Be((long)permissions);
        userResult.AuditEvent.Actor.Should().Be(OperatorUserName);
        userStore.HasAccounts.Should().BeTrue();
        var loadedAccount = userStore.LoadAccounts().Single(account =>
            account.Username == OperatorUserName &&
            account.RoleProfileName == OperatorRoleProfileName &&
            account.CompanyId == CompanyId);
        loadedAccount.Permissions.Should().BeEquivalentTo(RolePermissions.GetPermissionNames(permissions));

        var tenantAdministrationEvidence =
            $"evidence://tenant-admin/full/{TenantId}/{CompanyId}/paper-trade-accounting-setup";
        var tenantProfileStore = new InMemoryAccountingTenantAdministrationProfileStore();
        var tenantProfile = new AccountingTenantAdministrationProfileDto(
            TenantId: TenantId,
            CompanyId: CompanyId,
            TenantScopeConfigured: true,
            AdminRoleProfileConfigured: true,
            ScopedAccessPoliciesConfigured: true,
            ReportingGroupsConfigured: true,
            AccountingAdminSurfaceConfigured: true,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-05-31T14:00:00Z"),
            UpdatedBy: OperatorUserName,
            EvidenceReferences: [tenantAdministrationEvidence],
            CorrelationId: SetupCorrelationId,
            BrowserAccountingAdminSurfaceConfigured: true,
            WpfAccountingAdminSurfaceConfigured: true,
            ChartAdministrationStudioConfigured: true,
            RuleTestPromotionStudioConfigured: true,
            CloseSetupStudioConfigured: true,
            ProviderMappingStudioConfigured: true,
            TenantCompanyReportGroupSetupStudioConfigured: true,
            AuditReviewToolingConfigured: true,
            BulkImportExportSafeguardsConfigured: true,
            PerformanceValidationConfigured: true,
            DisasterRecoveryRunbookConfigured: true,
            LedgerBookAdministrationStudioConfigured: true,
            PostingRuleAuthoringStudioConfigured: true,
            ApprovalQueueStudioConfigured: true,
            DimensionMappingStudioConfigured: true,
            ImplementationSandboxConfigured: true,
            ApprovalQueueConfigurations:
            [
                new AccountingApprovalQueueConfigurationDto(
                    "paper-trade-journal-approval",
                    "Paper trade journal approvals",
                    "journal-entry",
                    "AccountingController",
                    RequiredApprovalCount: 1,
                    "submitter-cannot-approve",
                    "Retain paper-trade journal approval evidence before posting.")
            ],
            DimensionMappingConfigurations:
            [
                new AccountingDimensionMappingConfigurationDto(
                    "paper-trade-dimensions",
                    "Paper trade dimension mapping",
                    "paper-session",
                    new LedgerDimensionSetDto(FundId: "paper-fund", EntityId: CompanyId, BookId: "paper-book"),
                    new LedgerDimensionSetDto(PortfolioId: "paper-session", BookId: "paper-book"),
                    "Retain dimension mapping evidence for paper-trade accounting.")
            ]);
        var retainedTenantProfile = await tenantProfileStore.UpsertAsync(
            new AccountingTenantAdministrationProfileUpsertRequestDto(
                tenantProfile,
                Actor: OperatorUserName,
                CorrelationId: SetupCorrelationId,
                EvidenceLinks: [tenantAdministrationEvidence],
                ActionOrigin: OperationsActionOriginDto.HumanOperator),
            ct);

        retainedTenantProfile.TenantId.Should().Be(TenantId);
        retainedTenantProfile.CompanyId.Should().Be(CompanyId);
        retainedTenantProfile.UpdatedBy.Should().Be(OperatorUserName);
        retainedTenantProfile.AdminRoleProfileConfigured.Should().BeTrue();
        retainedTenantProfile.LedgerBookAdministrationStudioConfigured.Should().BeTrue();
        retainedTenantProfile.PostingRuleAuthoringStudioConfigured.Should().BeTrue();
        retainedTenantProfile.EvidenceReferences.Should().Contain(tenantAdministrationEvidence);
        var loadedTenantProfile = await tenantProfileStore.GetAsync(TenantId, CompanyId, ct);
        loadedTenantProfile.Should().BeEquivalentTo(retainedTenantProfile);

        return new OperatorProfile(
            OperatorUserName,
            OperatorRoleProfileName,
            permissions,
            TenantId,
            CompanyId,
            tenantAdministrationEvidence);
    }

    private static async Task<AccountingPostingCandidateService> CreatePaperTradeCandidateServiceAsync(
        Guid ledgerBookId,
        OperatorProfile operatorProfile,
        CancellationToken ct)
    {
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        await SeedPaperTradeConfigurationAsync(configurationService, ledgerBookId, operatorProfile, ct);

        var policyService = new AccountingPolicyService();
        await policyService.CreatePolicyAsync(
            new CreateAccountingPolicyRequest(
                AccountingBasisKindDto.Gaap,
                PolicyId: "gaap-paper-trade-v1",
                Version: "v1",
                DisplayName: "GAAP paper trade sale treatment",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                RulePack: new AccountingPolicyRulePackDto(
                    "gaap-paper-trade-rules",
                    "v1",
                    [
                        new AccountingPolicyRuleDto(
                            "paper-trade.fill.gaap",
                            AccountingTreatmentKindDto.TaxLotRelief,
                            RuleVersion: "v1",
                            SourceEventType: "PaperTradeFill",
                            RequiresEvidence: true,
                            RequiresApproval: true,
                            AllowsAutoPosting: false,
                            Description: "Post paper trade sale fills from retained session replay evidence.")
                    ])),
            ct);

        return new AccountingPostingCandidateService(
            configurationService,
            new AccountingJournalDraftService(
                policyService,
                new AccountingBasisProjectionService(policyService)));
    }

    private static async Task SeedPaperTradeConfigurationAsync(
        AccountingConfigurationService configurationService,
        Guid ledgerBookId,
        OperatorProfile operatorProfile,
        CancellationToken ct)
    {
        await UpsertChartNodeAsync(configurationService, "cash", "assets/cash", "Cash", "Asset", ledgerBookId, operatorProfile, ct);
        await UpsertChartNodeAsync(configurationService, "aapl-investment", "assets/securities/aapl", "AAPL Investment", "Asset", ledgerBookId, operatorProfile, ct);
        await UpsertChartNodeAsync(configurationService, "realized-gains", "income/realized-gains", "Realized Gains", "Revenue", ledgerBookId, operatorProfile, ct);
        await UpsertChartNodeAsync(configurationService, "commissions", "expenses/commissions", "Trading Commissions", "Expense", ledgerBookId, operatorProfile, ct);

        await configurationService.UpsertPostingRuleAsync(
            new UpsertPostingRuleRequest(
                FundProfileId,
                new PostingRuleDto(
                    "posting.paper-trade-sale",
                    "Paper trade sale fill",
                    "PaperTradeFill",
                    TemplateId: "generated",
                    RuleVersion: "v1",
                    EffectiveFrom: new DateOnly(2026, 1, 1),
                    Priority: 100,
                    Scope: new LedgerDimensionSetDto(
                        FundId: FundProfileId,
                        EntityId: EntityId,
                        OrganizationId: operatorProfile.TenantId,
                        BookId: ledgerBookId.ToString("D")),
                    Conditions:
                    [
                        new AccountingRuleConditionDto(
                            "minimum-sale-amount",
                            "eventAmount",
                            AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                            "100")
                    ],
                    Formulas:
                    [
                        new AccountingRuleFormulaDto("cash-net-proceeds", AccountingRuleFormulaKindDto.FixedAmount, 918.75m),
                        new AccountingRuleFormulaDto("cost-relief", AccountingRuleFormulaKindDto.FixedAmount, 800m),
                        new AccountingRuleFormulaDto("realized-gain", AccountingRuleFormulaKindDto.FixedAmount, 120m),
                        new AccountingRuleFormulaDto("commission-expense", AccountingRuleFormulaKindDto.FixedAmount, 1.25m)
                    ],
                    GeneratedPostings:
                    [
                        new GeneratedPostingLineDto(
                            "cash-net-proceeds",
                            "assets/cash",
                            AccountingTemplateLineSideDto.Debit,
                            "cash-net-proceeds",
                            918.75m,
                            "USD",
                            Description: "Debit cash for net sale proceeds"),
                        new GeneratedPostingLineDto(
                            "cost-relief",
                            "assets/securities/aapl",
                            AccountingTemplateLineSideDto.Credit,
                            "cost-relief",
                            800m,
                            "USD",
                            Description: "Credit investment cost relieved from AAPL lots"),
                        new GeneratedPostingLineDto(
                            "realized-gain",
                            "income/realized-gains",
                            AccountingTemplateLineSideDto.Credit,
                            "realized-gain",
                            120m,
                            "USD",
                            Description: "Credit realized gain from sale"),
                        new GeneratedPostingLineDto(
                            "commission-expense",
                            "expenses/commissions",
                            AccountingTemplateLineSideDto.Debit,
                            "commission-expense",
                            1.25m,
                            "USD",
                            Description: "Debit commission expense")
                    ]),
                operatorProfile.Username,
                CorrelationId: SetupCorrelationId,
                EvidenceLinks: [operatorProfile.TenantAdministrationEvidenceLink],
                CompanyId: operatorProfile.CompanyId,
                LedgerBookId: ledgerBookId,
                TenantId: operatorProfile.TenantId),
            ct);
    }

    private static Task UpsertChartNodeAsync(
        AccountingConfigurationService configurationService,
        string nodeId,
        string path,
        string accountName,
        string accountType,
        Guid ledgerBookId,
        OperatorProfile operatorProfile,
        CancellationToken ct)
        => configurationService.UpsertChartNodeAsync(
            new UpsertChartOfAccountsNodeRequest(
                FundProfileId,
                new ChartOfAccountsNodeDto(nodeId, path, accountName, accountType),
                operatorProfile.Username,
                CorrelationId: SetupCorrelationId,
                EvidenceLinks: [operatorProfile.TenantAdministrationEvidenceLink],
                CompanyId: operatorProfile.CompanyId,
                LedgerBookId: ledgerBookId,
                TenantId: operatorProfile.TenantId),
            ct);

    private static PostingRuleJournalCandidateRequestDto BuildCandidateRequest(
        string sessionId,
        Guid ledgerBookId,
        Guid periodId,
        Guid sourceEventId,
        OperatorProfile operatorProfile,
        IReadOnlyList<string> evidenceLinks)
        => new(
            FundProfileId,
            "PaperTradeFill",
            920m,
            "USD",
            new DateOnly(2026, 5, 31),
            operatorProfile.Username,
            ledgerBookId,
            periodId,
            DateTimeOffset.Parse("2026-05-31T15:30:00Z"),
            "Post paper AAPL sale fill after replay verification",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(
                FundId: FundProfileId,
                EntityId: EntityId,
                OrganizationId: operatorProfile.TenantId,
                BookId: ledgerBookId.ToString("D")),
            CounterpartyId: "broker-paper",
            InstrumentSymbol: "AAPL",
            CorrelationId: Guid.Parse("11111111-1111-4111-8111-111111111111"),
            SourceEventId: sourceEventId,
            PolicyId: "gaap-paper-trade-v1",
            TreatmentKind: AccountingTreatmentKindDto.TaxLotRelief,
            TreasuryContext: new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 5, 31),
                IdempotencyKey: $"paper-trade:{sessionId}:paper-sell-1",
                FundEventId: $"fund-event:{FundProfileId}:paper-trade-sale:{sourceEventId:N}",
                FundEventType: "PaperTradeFill",
                CapitalAccountId: "capital-account:fund-alpha:master",
                InvestorId: "investor:paper-strategy",
                PaymentIntentId: $"payment:{FundProfileId}:paper-trade-sale:{sourceEventId:N}",
                SettlementReference: $"settlement:{FundProfileId}:paper-trade-sale:{sourceEventId:N}"),
            EvidenceLinks: evidenceLinks,
            TenantId: operatorProfile.TenantId,
            CompanyId: operatorProfile.CompanyId);

    private static OrderState BuildOrder(
        string orderId,
        OrderSide side,
        decimal quantity,
        decimal filledQuantity,
        OrderStatus status,
        DateTimeOffset timestamp) =>
        new()
        {
            OrderId = orderId,
            Symbol = "AAPL",
            Side = side,
            Type = OrderType.Market,
            Quantity = quantity,
            FilledQuantity = filledQuantity,
            Status = status,
            CreatedAt = timestamp.AddMinutes(-1),
            LastUpdatedAt = timestamp
        };

    private static ExecutionReport BuildFill(
        string orderId,
        OrderSide side,
        decimal quantity,
        decimal price,
        decimal commission,
        DateTimeOffset timestamp) =>
        new()
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Fill,
            Symbol = "AAPL",
            Side = side,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = quantity,
            FilledQuantity = quantity,
            FillPrice = price,
            Commission = commission,
            Timestamp = timestamp
        };

    private sealed record OperatorProfile(
        string Username,
        string RoleProfileName,
        UserPermission Permissions,
        string TenantId,
        string CompanyId,
        string TenantAdministrationEvidenceLink);
}

internal sealed class ThrowingLedgerSaveStore : IPaperSessionStore
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
        => Task.FromException(new IOException("ledger snapshot failed"));

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

internal sealed class CorruptLedgerReplayStore : IPaperSessionStore
{
    public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default) => Task.CompletedTask;
    public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default) => Task.CompletedTask;
    public Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveLedgerJournalAsync(string sessionId, IReadOnlyList<PersistedJournalEntryDto> entries, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveSessionClosedAtAsync(string sessionId, DateTimeOffset? closedAtUtc, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PersistedSessionRecord>>(
            [new PersistedSessionRecord("PAPER-CORRUPT-001", "strat-corrupt", "Corrupt", 100_000m, DateTimeOffset.UtcNow.AddMinutes(-30), null, true, ["AAPL"])]);

    public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExecutionReport>>(
            [
                new()
                {
                    OrderId = "fill-1",
                    ReportType = ExecutionReportType.Fill,
                    Symbol = "AAPL",
                    Side = OrderSide.Buy,
                    OrderStatus = OrderStatus.Filled,
                    OrderQuantity = 10m,
                    FilledQuantity = 10m,
                    FillPrice = 100m,
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-20)
                }
            ]);

    public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrderState>>([]);

    public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(string sessionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var journalOkId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var corruptJournalId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        PersistedJournalEntryDto BuildEntry(
            Guid journalId,
            string description,
            DateTimeOffset timestamp,
            IReadOnlyList<PersistedLedgerLineDto> lines)
            => new(
                JournalEntryId: journalId,
                Timestamp: timestamp,
                Description: description,
                Lines: lines,
                ActivityType: "Trade",
                Symbol: "AAPL",
                SecurityId: null,
                OrderId: null,
                LedgerView: "Trading",
                StrategyId: "strat-corrupt");

        PersistedLedgerLineDto BuildLine(
            Guid entryId,
            Guid journalId,
            DateTimeOffset timestamp,
            string accountName,
            decimal debit,
            decimal credit,
            string description,
            string? accountSymbol = "AAPL")
            => new(
                EntryId: entryId,
                JournalEntryId: journalId,
                Timestamp: timestamp,
                Account: new PersistedLedgerAccountDto(accountName, "Asset", accountSymbol, null),
                Debit: debit,
                Credit: credit,
                Description: description);

        var validEntry = BuildEntry(
            journalOkId,
            "buy entry",
            now.AddMinutes(-19),
            [
                BuildLine(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), journalOkId, now.AddMinutes(-19), "Cash", 0m, 1000m, "buy entry", accountSymbol: null),
                BuildLine(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), journalOkId, now.AddMinutes(-19), "Position", 1000m, 0m, "buy entry")
            ]);

        // Intentionally corrupt: line references a different JournalEntryId than parent.
        var corruptEntry = BuildEntry(
            corruptJournalId,
            "corrupt entry",
            now.AddMinutes(-18),
            [
                BuildLine(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"),
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    now.AddMinutes(-18),
                    "Cash",
                    100m,
                    0m,
                    "corrupt entry",
                    accountSymbol: null)
            ]);

        return Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([validEntry, corruptEntry]);
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
