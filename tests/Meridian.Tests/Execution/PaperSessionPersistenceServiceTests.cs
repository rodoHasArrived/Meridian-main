using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed class PaperSessionPersistenceServiceTests
{
    private static PaperSessionPersistenceService Build() =>
        new(NullLogger<PaperSessionPersistenceService>.Instance);

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
        var dto = new CreatePaperSessionDto("strat-3", "Detail Test", 100_000m);
        var summary = await service.CreateSessionAsync(dto);

        var detail = service.GetSession(summary.SessionId);

        detail.Should().NotBeNull();
        detail!.Summary.SessionId.Should().Be(summary.SessionId);
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

    // ---- RecordOrderUpdate ----

    [Fact]
    public async Task RecordOrderUpdate_WhenSessionActive_AppendsToOrderHistory()
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

        service.RecordOrderUpdate(summary.SessionId, orderState);

        var detail = service.GetSession(summary.SessionId);
        detail!.OrderHistory.Should().ContainSingle(o => o.OrderId == "order-1");
    }

    [Fact]
    public async Task RecordOrderUpdate_MultipleUpdates_AppendsAllInOrder()
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

        service.RecordOrderUpdate(summary.SessionId, order1);
        service.RecordOrderUpdate(summary.SessionId, order2);

        var detail = service.GetSession(summary.SessionId);
        detail!.OrderHistory.Should().HaveCount(2);
        detail.OrderHistory[0].OrderId.Should().Be("order-A");
        detail.OrderHistory[1].OrderId.Should().Be("order-B");
    }

    [Fact]
    public void RecordOrderUpdate_WhenSessionNotFound_DoesNotThrow()
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
        var action = () => service.RecordOrderUpdate("nonexistent-session", orderState);

        action.Should().NotThrow();
    }
}
