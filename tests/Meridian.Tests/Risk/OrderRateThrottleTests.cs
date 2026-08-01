using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Risk;

public sealed class OrderRateThrottleTests
{
    private static OrderRateThrottle CreateSut(int maxOrdersPerMinute = 10, TimeProvider? time = null) =>
        new(() => maxOrdersPerMinute, NullLogger<OrderRateThrottle>.Instance, time ?? TimeProvider.System);

    private static OrderRequest CreateOrder(string symbol = "AAPL") => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 1m,
    };

    [Fact]
    public void RuleName_ReturnsOrderRateThrottle()
    {
        CreateSut().RuleName.Should().Be("OrderRateThrottle");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new OrderRateThrottle(10, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task EvaluateAndReserveAsync_FirstOrder_IsApproved()
    {
        var result = await CreateSut(maxOrdersPerMinute: 5).EvaluateAndReserveAsync(CreateOrder());

        result.Finding.Should().BeNull();
        result.Reservation.Should().NotBeNull();
    }

    [Fact]
    public async Task EvaluateAndReserveAsync_WhenUnderLimit_ReturnsNoFinding()
    {
        var sut = CreateSut(maxOrdersPerMinute: 5);

        for (var i = 0; i < 4; i++)
        {
            await sut.EvaluateAndReserveAsync(CreateOrder());
        }

        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAndReserveAsync_WhenAtLimit_ReturnsFindingAndNoReservation()
    {
        var sut = CreateSut(maxOrdersPerMinute: 3);

        for (var i = 0; i < 3; i++)
        {
            await sut.EvaluateAndReserveAsync(CreateOrder());
        }

        var result = await sut.EvaluateAndReserveAsync(CreateOrder());

        result.Finding.Should().NotBeNull();
        result.Finding!.Code.Should().Be(OrderRateThrottle.RateExceededCode);
        result.Reservation.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAndReserveAsync_WhenAtLimit_ReportsObservedAndLimit()
    {
        var sut = CreateSut(maxOrdersPerMinute: 2);

        for (var i = 0; i < 2; i++)
        {
            await sut.EvaluateAndReserveAsync(CreateOrder());
        }

        var finding = (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding;

        // Structured values, not just a formatted sentence — the operator surface compares them.
        finding!.ObservedValue.Should().Be(2m);
        finding.LimitValue.Should().Be(2m);
        finding.Message.Should().Contain("2");
    }

    [Fact]
    public async Task EvaluateAndReserveAsync_WithZeroLimit_FirstOrderIsRejected()
    {
        var result = await CreateSut(maxOrdersPerMinute: 0).EvaluateAndReserveAsync(CreateOrder());

        result.Finding.Should().NotBeNull();
    }

    /// <summary>
    /// Evaluation must be a pure read. If it consumed capacity, an order that a later rule blocked
    /// would still count against the window and throttle subsequent orders for no reason.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_DoesNotConsumeCapacity()
    {
        var sut = CreateSut(maxOrdersPerMinute: 1);

        for (var i = 0; i < 5; i++)
        {
            (await sut.EvaluateAsync(CreateOrder())).Should().BeNull();
        }
    }

    [Fact]
    public async Task Rollback_ReturnsCapacityToTheWindow()
    {
        var sut = CreateSut(maxOrdersPerMinute: 1);

        var first = await sut.EvaluateAndReserveAsync(CreateOrder());
        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should().NotBeNull();

        first.Reservation!.Rollback();

        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should().BeNull();
    }

    [Fact]
    public async Task Commit_KeepsCapacityConsumed()
    {
        var sut = CreateSut(maxOrdersPerMinute: 1);

        (await sut.EvaluateAndReserveAsync(CreateOrder())).Reservation!.Commit();

        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should().NotBeNull();
    }

    [Fact]
    public async Task Settling_IsIdempotent()
    {
        var sut = CreateSut(maxOrdersPerMinute: 1);
        var reserved = await sut.EvaluateAndReserveAsync(CreateOrder());

        reserved.Reservation!.Commit();
        reserved.Reservation.Rollback();
        reserved.Reservation.Rollback();

        // A rollback after commit must not hand capacity back: the order was routed.
        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should().NotBeNull();
    }

    /// <summary>
    /// The race the reservation model exists to prevent. A post-decision commit callback would let
    /// concurrent callers each observe room below the cap and all consume it.
    /// </summary>
    [Fact]
    public async Task UnderConcurrentBurst_ReservationsNeverExceedLimit()
    {
        const int limit = 10;
        const int burst = 200;
        var sut = CreateSut(maxOrdersPerMinute: limit);

        var results = await Task.WhenAll(
            Enumerable.Range(0, burst).Select(_ => Task.Run(() => sut.EvaluateAndReserveAsync(CreateOrder()))));

        results.Count(r => r.Reservation is not null).Should().Be(limit,
            "a concurrent burst must not reserve more slots than the per-minute limit");
        results.Count(r => r.Finding is not null).Should().Be(burst - limit);
    }

    /// <summary>
    /// A pending reservation must keep blocking capacity even past the window length. Expiring it
    /// would free the slot while the order is still in flight, letting a second order route and
    /// leaving the first uncounted when it finally commits.
    /// </summary>
    [Fact]
    public async Task PendingReservation_DoesNotExpireWithTheWindow()
    {
        var time = new StubTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var sut = CreateSut(maxOrdersPerMinute: 1, time);

        var pending = await sut.EvaluateAndReserveAsync(CreateOrder());
        pending.Reservation.Should().NotBeNull();

        // Acknowledgement is slow: well past the one-minute window.
        time.Advance(TimeSpan.FromSeconds(120));

        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should()
            .NotBeNull("an in-flight submission still holds its slot");

        // It commits late; the slot is now stamped at routing time and holds for another minute.
        pending.Reservation!.Commit();
        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should()
            .NotBeNull("the routed order occupies the window from its acknowledgement");

        time.Advance(TimeSpan.FromSeconds(61));
        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should()
            .BeNull("the committed slot expires a minute after routing");
    }

    [Fact]
    public async Task Window_ExpiresCommittedSlotsAfterOneMinute()
    {
        var time = new StubTimeProvider(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var sut = CreateSut(maxOrdersPerMinute: 1, time);

        var first = await sut.EvaluateAndReserveAsync(CreateOrder());
        first.Finding.Should().BeNull();
        // Only committed slots age out, so the order has to route before the window can release it.
        first.Reservation!.Commit();

        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should().NotBeNull();

        time.Advance(TimeSpan.FromSeconds(61));

        (await sut.EvaluateAndReserveAsync(CreateOrder())).Finding.Should().BeNull();
    }

    private sealed class StubTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
