using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class EquityOrderSessionPolicyTests
{
    [Theory]
    [InlineData("2026-11-10T15:00:00Z", OrderSessionIntent.RegularOnly, OrderType.Limit, "passed")]
    [InlineData("2026-11-10T23:00:00Z", OrderSessionIntent.RegularOnly, OrderType.Limit, "blocked")]
    [InlineData("2026-11-27T18:00:00Z", OrderSessionIntent.RegularOnly, OrderType.Limit, "blocked")]
    [InlineData("2026-11-26T15:00:00Z", OrderSessionIntent.QueueNextSession, OrderType.Limit, "warning")]
    [InlineData("2026-11-10T12:00:00Z", OrderSessionIntent.ExtendedHours, OrderType.Limit, "passed")]
    [InlineData("2026-11-10T12:00:00Z", OrderSessionIntent.ExtendedHours, OrderType.Market, "blocked")]
    public async Task SessionIntent_RespectsCalendarHalfDaysHolidaysAndOrderEligibility(string at,
        OrderSessionIntent intent, OrderType type, string expected)
    {
        var identity = Substitute.For<ISecurityIdentityQueryService>();
        var terms = JsonSerializer.SerializeToElement(new { });
        identity.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "SPY", null, Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(Guid.NewGuid(), "Equity", SecurityStatusDto.Active, "SPY", "USD", terms, terms, [], [], 1, DateTimeOffset.MinValue, null));
        var policy = new EquityOrderSessionPolicy(identity, new Clock(DateTimeOffset.Parse(at)));
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
    public async Task UnknownIdentityCannotClaimEligibleEquitySession()
    {
        var check = await new EquityOrderSessionPolicy().EvaluateAsync(new()
        { Symbol = "UNKNOWN", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1m });
        check.Status.Should().Be("blocked");
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
