using FluentAssertions;
using Meridian.Platform.Monitoring;
using Xunit;

namespace Meridian.Tests.Platform.Monitoring;

public sealed class CircuitBreakerStatusServiceTests
{
    [Fact]
    public void Register_AddsClosedBreakerToDashboard()
    {
        var service = new CircuitBreakerStatusService();

        service.Register("quotes-http");

        var dashboard = service.GetDashboard();
        dashboard.OverallHealth.Should().Be("Green");
        dashboard.TotalBreakers.Should().Be(1);
        dashboard.ClosedCount.Should().Be(1);
        dashboard.Breakers.Single().State.Should().Be(nameof(CircuitBreakerState.Closed));
    }

    [Fact]
    public void RecordStateTransition_ToOpen_TracksTripAndRaisesEvent()
    {
        var service = new CircuitBreakerStatusService();
        CircuitBreakerStateChange? observed = null;
        service.OnStateChanged += change => observed = change;

        service.RecordStateTransition(
            "orders-http",
            CircuitBreakerState.Open,
            lastError: "timeout",
            failureCount: 3,
            breakDuration: TimeSpan.FromSeconds(30));

        var breaker = service.GetDashboard().Breakers.Single();
        breaker.Name.Should().Be("orders-http");
        breaker.State.Should().Be(nameof(CircuitBreakerState.Open));
        breaker.TripCount.Should().Be(1);
        breaker.LastError.Should().Be("timeout");
        breaker.CooldownRemaining.Should().NotBeNull();
        observed.Should().NotBeNull();
        observed!.Name.Should().Be("orders-http");
        observed.NewState.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public void GetDashboard_WithHalfOpenBreaker_ReturnsYellowHealth()
    {
        var service = new CircuitBreakerStatusService();

        service.RecordStateTransition("market-data-http", CircuitBreakerState.HalfOpen);

        var dashboard = service.GetDashboard();
        dashboard.OverallHealth.Should().Be("Yellow");
        dashboard.HalfOpenCount.Should().Be(1);
    }

    [Fact]
    public void GetDashboard_WithOpenBreaker_ReturnsRedHealth()
    {
        var service = new CircuitBreakerStatusService();

        service.RecordStateTransition("market-data-http", CircuitBreakerState.Open);

        var dashboard = service.GetDashboard();
        dashboard.OverallHealth.Should().Be("Red");
        dashboard.OpenCount.Should().Be(1);
    }
}
