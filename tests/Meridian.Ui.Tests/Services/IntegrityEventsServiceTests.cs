using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="IntegrityEventsService"/> — integrity event tracking,
/// filtering, summary aggregation, and event notification.
/// </summary>
public sealed class IntegrityEventsServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ReturnsNonNullSingleton()
    {
        // Act
        var instance = IntegrityEventsService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = IntegrityEventsService.Instance;
        var instance2 = IntegrityEventsService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void Instance_ThreadSafety_MultipleThreadsGetSameInstance()
    {
        // Arrange
        IntegrityEventsService? instance1 = null;
        IntegrityEventsService? instance2 = null;
        var task1 = Task.Run(() => instance1 = IntegrityEventsService.Instance);
        var task2 = Task.Run(() => instance2 = IntegrityEventsService.Instance);

        // Act
        Task.WaitAll(task1, task2);

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2);
    }

    // ── IntegrityEvent model ────────────────────────────────────────

    [Fact]
    public void IntegrityEvent_DefaultValues_ShouldBeValid()
    {
        // Act
        var evt = new IntegrityEvent();

        // Assert
        evt.Id.Should().NotBeNull();
        evt.Symbol.Should().NotBeNull();
        evt.Description.Should().NotBeNull();
    }

    [Fact]
    public void IntegrityEvent_CanStoreAllProperties()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var evt = new IntegrityEvent
        {
            Id = "evt-001",
            Timestamp = timestamp,
            Symbol = "SPY",
            EventType = IntegrityEventType.SequenceGap,
            Severity = IntegritySeverity.Warning,
            Description = "Sequence gap detected",
            ExpectedSequence = 100,
            ActualSequence = 105,
            GapSize = 5
        };

        // Assert
        evt.Id.Should().Be("evt-001");
        evt.Timestamp.Should().Be(timestamp);
        evt.Symbol.Should().Be("SPY");
        evt.EventType.Should().Be(IntegrityEventType.SequenceGap);
        evt.Severity.Should().Be(IntegritySeverity.Warning);
        evt.Description.Should().Be("Sequence gap detected");
        evt.ExpectedSequence.Should().Be(100);
        evt.ActualSequence.Should().Be(105);
        evt.GapSize.Should().Be(5);
    }

    [Theory]
    [InlineData("SPY")]
    [InlineData("AAPL")]
    [InlineData("MSFT")]
    [InlineData("TSLA")]
    public void IntegrityEvent_AcceptsDifferentSymbols(string symbol)
    {
        // Act
        var evt = new IntegrityEvent { Symbol = symbol };

        // Assert
        evt.Symbol.Should().Be(symbol);
    }

    // ── IntegrityEventType enum ─────────────────────────────────────

    [Theory]
    [InlineData(IntegrityEventType.SequenceGap)]
    [InlineData(IntegrityEventType.OutOfOrder)]
    [InlineData(IntegrityEventType.Duplicate)]
    [InlineData(IntegrityEventType.ValidationFailure)]
    [InlineData(IntegrityEventType.StaleData)]
    [InlineData(IntegrityEventType.ProviderSwitch)]
    [InlineData(IntegrityEventType.Other)]
    public void IntegrityEventType_AllValues_ShouldBeDefined(IntegrityEventType eventType)
    {
        // Assert
        Enum.IsDefined(typeof(IntegrityEventType), eventType).Should().BeTrue();
    }

    [Fact]
    public void IntegrityEventType_ShouldHaveSevenValues()
    {
        // Act
        var values = Enum.GetValues<IntegrityEventType>();

        // Assert
        values.Should().HaveCount(7);
        values.Should().Contain(IntegrityEventType.SequenceGap);
        values.Should().Contain(IntegrityEventType.OutOfOrder);
        values.Should().Contain(IntegrityEventType.Duplicate);
        values.Should().Contain(IntegrityEventType.ValidationFailure);
        values.Should().Contain(IntegrityEventType.StaleData);
        values.Should().Contain(IntegrityEventType.ProviderSwitch);
        values.Should().Contain(IntegrityEventType.Other);
    }

    // ── IntegritySeverity enum ────────────────────────────────────

    [Theory]
    [InlineData(IntegritySeverity.Info)]
    [InlineData(IntegritySeverity.Warning)]
    [InlineData(IntegritySeverity.Critical)]
    public void IntegritySeverity_AllValues_ShouldBeDefined(IntegritySeverity severity)
    {
        // Assert
        Enum.IsDefined(typeof(IntegritySeverity), severity).Should().BeTrue();
    }

    [Fact]
    public void IntegritySeverity_ShouldHaveThreeValues()
    {
        // Act
        var values = Enum.GetValues<IntegritySeverity>();

        // Assert
        values.Should().HaveCount(3);
        values.Should().Contain(IntegritySeverity.Info);
        values.Should().Contain(IntegritySeverity.Warning);
        values.Should().Contain(IntegritySeverity.Critical);
    }

    // ── IntegritySummary model ────────────────────────────────────

    [Fact]
    public void IntegritySummary_DefaultValues_ShouldBeZero()
    {
        // Act
        var summary = new IntegritySummary();

        // Assert
        summary.TotalEvents.Should().Be(0);
        summary.CriticalCount.Should().Be(0);
        summary.WarningCount.Should().Be(0);
        summary.InfoCount.Should().Be(0);
        summary.EventsLast24Hours.Should().Be(0);
        summary.EventsLastHour.Should().Be(0);
        summary.UnacknowledgedCount.Should().Be(0);
    }

    [Fact]
    public void IntegritySummary_CanSetAllProperties()
    {
        // Arrange
        var lastEvent = DateTime.UtcNow;

        // Act
        var summary = new IntegritySummary
        {
            TotalEvents = 42,
            CriticalCount = 2,
            WarningCount = 15,
            InfoCount = 25,
            EventsLast24Hours = 20,
            EventsLastHour = 5,
            UnacknowledgedCount = 10,
            MostAffectedSymbol = "SPY",
            LastEventTime = lastEvent
        };

        // Assert
        summary.TotalEvents.Should().Be(42);
        summary.CriticalCount.Should().Be(2);
        summary.WarningCount.Should().Be(15);
        summary.InfoCount.Should().Be(25);
        summary.EventsLast24Hours.Should().Be(20);
        summary.EventsLastHour.Should().Be(5);
        summary.UnacknowledgedCount.Should().Be(10);
        summary.MostAffectedSymbol.Should().Be("SPY");
        summary.LastEventTime.Should().Be(lastEvent);
    }

    [Fact]
    public void IntegritySummary_LastEventTime_CanBeNull()
    {
        // Act
        var summary = new IntegritySummary();

        // Assert
        summary.LastEventTime.Should().BeNull();
    }

    // ── GetAllEvents ─────────────────────────────────────────────

    [Fact]
    public void GetAllEvents_ReturnsNonNullCollection()
    {
        // Arrange
        var service = IntegrityEventsService.Instance;

        // Act
        var events = service.GetAllEvents();

        // Assert
        events.Should().NotBeNull();
    }

    // ── GetEventsBySymbol ────────────────────────────────────────

    [Fact]
    public void GetEventsBySymbol_ReturnsNonNullCollection()
    {
        // Arrange
        var service = IntegrityEventsService.Instance;

        // Act
        var events = service.GetEventsBySymbol("SPY");

        // Assert
        events.Should().NotBeNull();
    }

    // ── GetEventsBySeverity ──────────────────────────────────────

    [Fact]
    public void GetEventsBySeverity_ReturnsNonNullCollection()
    {
        // Arrange
        var service = IntegrityEventsService.Instance;

        // Act
        var events = service.GetEventsBySeverity(IntegritySeverity.Warning);

        // Assert
        events.Should().NotBeNull();
    }

    // ── GetSummary ───────────────────────────────────────────────

    [Fact]
    public void GetSummary_ReturnsNonNullSummary()
    {
        // Arrange
        var service = IntegrityEventsService.Instance;

        // Act
        var summary = service.GetSummary();

        // Assert
        summary.Should().NotBeNull();
        summary.TotalEvents.Should().BeGreaterThanOrEqualTo(0);
        summary.CriticalCount.Should().BeGreaterThanOrEqualTo(0);
        summary.WarningCount.Should().BeGreaterThanOrEqualTo(0);
        summary.InfoCount.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── GetRecentEvents ──────────────────────────────────────────

    [Fact]
    public void GetRecentEvents_ReturnsNonNullCollection()
    {
        // Arrange
        var service = IntegrityEventsService.Instance;

        // Act
        var events = service.GetRecentEvents();

        // Assert
        events.Should().NotBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void GetRecentEvents_WithCount_ReturnsNonNullCollection(int count)
    {
        // Arrange
        var service = IntegrityEventsService.Instance;

        // Act
        var events = service.GetRecentEvents(count);

        // Assert
        events.Should().NotBeNull();
    }
}
