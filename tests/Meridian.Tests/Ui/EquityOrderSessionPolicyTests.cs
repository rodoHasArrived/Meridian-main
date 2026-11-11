using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class EquityOrderSessionPolicyTests
{
    [Theory]
    [InlineData("2026-11-10T15:00:00Z", OrderSessionIntent.RegularOnly, OrderType.Limit, "passed")]
    [InlineData("2026-11-10T23:00:00Z", OrderSessionIntent.RegularOnly, OrderType.Limit, "blocked")]
    [InlineData("2026-11-27T18:00:00Z", OrderSessionIntent.RegularOnly, OrderType.Limit, "blocked")]
    [InlineData("2026-11-10T15:00:00Z", OrderSessionIntent.QueueNextSession, OrderType.Market, "blocked")]
    [InlineData("2026-11-27T17:59:00Z", OrderSessionIntent.QueueNextSession, OrderType.Limit, "blocked")]
    [InlineData("2026-11-27T18:00:00Z", OrderSessionIntent.QueueNextSession, OrderType.Limit, "warning")]
    [InlineData("2026-11-26T15:00:00Z", OrderSessionIntent.QueueNextSession, OrderType.Limit, "warning")]
    [InlineData("2026-11-10T12:00:00Z", OrderSessionIntent.ExtendedHours, OrderType.Limit, "passed")]
    [InlineData("2026-11-10T12:00:00Z", OrderSessionIntent.ExtendedHours, OrderType.Market, "blocked")]
    public async Task SessionIntent_RespectsCalendarHalfDaysHolidaysAndOrderEligibility(string at,
        OrderSessionIntent intent, OrderType type, string expected)
    {
        var policy = new EquityOrderSessionPolicy(KnownEquity(), new Clock(DateTimeOffset.Parse(at)));
        var check = await policy.EvaluateAsync(new()
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = type,
            Quantity = 1m,
            LimitPrice = 100m,
            SessionIntent = intent,
            TimeInForce = TimeInForce.Day
        });
        check.Status.Should().Be(expected);
    }

    [Fact]
    public async Task RegularSessionOpeningDuringAdmission_RefusesQueuedDispatchAndRollsBackCapacity()
    {
        var opensAt = DateTimeOffset.Parse("2026-11-10T14:30:00Z");
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(opensAt.AddSeconds(-1), opensAt);
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("paper");
        gateway.StreamExecutionReportsAsync(Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<ExecutionReport>());
        var throttle = new OrderRateThrottle(1, NullLogger<OrderRateThrottle>.Instance);
        var risk = new CompositeRiskValidator([throttle], NullLogger<CompositeRiskValidator>.Instance);
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            riskValidator: risk, sessionPolicy: new EquityOrderSessionPolicy(KnownEquity(), clock));
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(new()
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 100m,
            SessionIntent = OrderSessionIntent.QueueNextSession,
            TimeInForce = TimeInForce.Day
        });

        result.Success.Should().BeFalse();
        result.OrderState.Status.Should().Be(OrderStatus.Rejected);
        result.ErrorMessage.Should().Contain("outside regular trading hours");
        throttle.CurrentUsage.Should().Be(0);
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LocalPaperSimulator_RefusesNextSessionQueuingInPreviewAndSubmission()
    {
        var now = DateTimeOffset.Parse("2026-11-10T14:00:00Z");
        var clock = new Clock(now);
        var sessions = new EquityOrderSessionPolicy(KnownEquity(), clock);
        var prices = Substitute.For<IOrderMarketReferenceSource>();
        prices.GetReference("SPY", OrderSide.Buy).Returns(new OrderMarketReference(100m, now, "synthetic quote"));
        var risk = new CompositeRiskValidator([], NullLogger<CompositeRiskValidator>.Instance);
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance);
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            riskValidator: risk, sessionPolicy: sessions);
        await oms.StartAsync();
        var preview = new OrderPreviewService(gateway, oms, risk, prices, NullLogger<OrderPreviewService>.Instance,
            sessions: sessions, clock: clock, coverage: sessions);
        var request = new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 100m,
            SessionIntent = OrderSessionIntent.QueueNextSession,
            TimeInForce = TimeInForce.Day
        };

        var previewResult = await preview.PreviewAsync(request);
        previewResult.CanSubmit.Should().BeFalse();
        previewResult.Checks.Should().Contain(check => check.Code == "QUEUED_SESSION_UNSUPPORTED" && check.Status == "blocked");

        var result = await oms.PlaceOrderAsync(request);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("local paper simulator");
        oms.GetOpenOrders().Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownIdentityCannotClaimEligibleEquitySession()
    {
        var check = await new EquityOrderSessionPolicy().EvaluateAsync(new()
        { Symbol = "UNKNOWN", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1m });
        check.Status.Should().Be("blocked");
    }

    private static ISecurityIdentityQueryService KnownEquity()
    {
        var identity = Substitute.For<ISecurityIdentityQueryService>();
        var terms = JsonSerializer.SerializeToElement(new { });
        identity.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "SPY", null, Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(Guid.NewGuid(), "Equity", SecurityStatusDto.Active, "SPY", "USD", terms, terms, [], [], 1, DateTimeOffset.MinValue, null));
        return identity;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
