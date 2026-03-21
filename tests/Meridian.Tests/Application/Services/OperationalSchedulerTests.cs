using FluentAssertions;
using Meridian.Application.Scheduling;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class OperationalSchedulerTests
{
    private readonly Mock<ITradingCalendarProvider> _calendarMock;
    private readonly OperationalScheduler _scheduler;

    public OperationalSchedulerTests()
    {
        _calendarMock = new Mock<ITradingCalendarProvider>();
        _scheduler = new OperationalScheduler(_calendarMock.Object);
    }

    [Fact]
    public async Task CanExecuteAsync_HealthCheck_ShouldAlwaysBeAllowed()
    {
        // Health checks are always allowed, even during trading hours
        SetupTradingDay(isTrading: true);

        var decision = await _scheduler.CanExecuteAsync(OperationType.HealthCheck);

        decision.CanExecute.Should().BeTrue();
    }

    [Fact]
    public async Task CanExecuteAsync_Maintenance_ShouldBeDeferredDuringTradingHours()
    {
        SetupTradingDay(isTrading: true);

        var decision = await _scheduler.CanExecuteAsync(OperationType.Maintenance);

        decision.CanExecute.Should().BeFalse();
        decision.Reason.Should().Contain("market is open");
        decision.SuggestedDelay.Should().NotBeNull();
    }

    [Fact]
    public async Task CanExecuteAsync_Maintenance_ShouldBeAllowedOutsideTradingHours()
    {
        SetupTradingDay(isTrading: false);

        var decision = await _scheduler.CanExecuteAsync(OperationType.Maintenance);

        decision.CanExecute.Should().BeTrue();
    }

    [Fact]
    public async Task CanExecuteAsync_Backfill_ShouldBeAllowedOutsideTradingHours()
    {
        SetupTradingDay(isTrading: false);

        var decision = await _scheduler.CanExecuteAsync(OperationType.Backfill);

        decision.CanExecute.Should().BeTrue();
    }

    [Fact]
    public async Task CanExecuteAsync_CredentialRefresh_ShouldAlwaysBeAllowed()
    {
        SetupTradingDay(isTrading: true);

        var decision = await _scheduler.CanExecuteAsync(OperationType.CredentialRefresh);

        decision.CanExecute.Should().BeTrue();
    }

    [Fact]
    public void IsWithinTradingHours_ShouldReturnFalse_WhenNotTradingDay()
    {
        _calendarMock.Setup(c => c.IsTradingDay(It.IsAny<DateOnly>(), It.IsAny<string>()))
            .Returns(false);

        _scheduler.IsWithinTradingHours.Should().BeFalse();
    }

    [Fact]
    public void RegisterMaintenanceWindow_ShouldMakeWindowAvailable()
    {
        var window = new MaintenanceWindow(
            "test-window",
            StartTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            EndTime: DateTimeOffset.UtcNow.AddHours(1));

        _scheduler.RegisterMaintenanceWindow(window);

        var current = _scheduler.GetCurrentMaintenanceWindow();
        current.Should().NotBeNull();
        current!.Name.Should().Be("test-window");
    }

    [Fact]
    public void RemoveMaintenanceWindow_ShouldRemoveByName()
    {
        var window = new MaintenanceWindow(
            "to-remove",
            StartTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            EndTime: DateTimeOffset.UtcNow.AddHours(1));

        _scheduler.RegisterMaintenanceWindow(window);
        _scheduler.RemoveMaintenanceWindow("to-remove");

        _scheduler.GetCurrentMaintenanceWindow().Should().BeNull();
    }

    [Fact]
    public void GetNextMaintenanceWindow_ShouldReturnFutureWindow()
    {
        var futureWindow = new MaintenanceWindow(
            "future-window",
            StartTime: DateTimeOffset.UtcNow.AddHours(2),
            EndTime: DateTimeOffset.UtcNow.AddHours(4));

        _scheduler.RegisterMaintenanceWindow(futureWindow);

        var next = _scheduler.GetNextMaintenanceWindow();
        next.Should().NotBeNull();
        next!.Name.Should().Be("future-window");
    }

    [Fact]
    public async Task FindNextAvailableSlotAsync_HealthCheck_ShouldReturnImmediate()
    {
        var slot = await _scheduler.FindNextAvailableSlotAsync(
            OperationType.HealthCheck,
            TimeSpan.FromMinutes(5));

        slot.Should().NotBeNull();
        slot!.SlotType.Should().Be("immediate");
    }

    [Fact]
    public async Task FindNextAvailableSlotAsync_Maintenance_ShouldFindNonTradingSlot()
    {
        // Set up a non-trading day (weekend)
        _calendarMock.Setup(c => c.IsTradingDay(It.IsAny<DateOnly>(), It.IsAny<string>()))
            .Returns(false);

        var slot = await _scheduler.FindNextAvailableSlotAsync(
            OperationType.Maintenance,
            TimeSpan.FromHours(1));

        slot.Should().NotBeNull();
        slot!.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
    }

    [Fact]
    public void GetCurrentTradingSession_ShouldReturnNull_WhenNoSessionActive()
    {
        _calendarMock.Setup(c => c.IsTradingDay(It.IsAny<DateOnly>(), It.IsAny<string>()))
            .Returns(false);

        _scheduler.GetCurrentTradingSession().Should().BeNull();
    }

    [Fact]
    public void GetNextTradingSession_ShouldReturnNextSession()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = today.AddDays(1);

        _calendarMock.Setup(c => c.IsTradingDay(today, "US")).Returns(false);
        _calendarMock.Setup(c => c.GetNextTradingDay(today, "US")).Returns(tomorrow);
        _calendarMock.Setup(c => c.GetTradingSessions(tomorrow, "US"))
            .Returns(new List<TradingSession>
            {
                new("NYSE", "US", "Regular",
                    new DateTimeOffset(tomorrow.ToDateTime(new TimeOnly(14, 30)), TimeSpan.Zero),
                    new DateTimeOffset(tomorrow.ToDateTime(new TimeOnly(21, 0)), TimeSpan.Zero))
            });

        var next = _scheduler.GetNextTradingSession();

        next.Should().NotBeNull();
        next!.Market.Should().Be("US");
    }

    private void SetupTradingDay(bool isTrading)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _calendarMock.Setup(c => c.IsTradingDay(today, It.IsAny<string>()))
            .Returns(isTrading);

        if (isTrading)
        {
            var now = DateTimeOffset.UtcNow;
            _calendarMock.Setup(c => c.GetTradingSessions(today, It.IsAny<string>()))
                .Returns(new List<TradingSession>
                {
                    new("NYSE", "US", "Regular",
                        now.AddHours(-2),
                        now.AddHours(2))
                });
        }
        else
        {
            _calendarMock.Setup(c => c.GetTradingSessions(today, It.IsAny<string>()))
                .Returns(new List<TradingSession>());
        }
    }
}
