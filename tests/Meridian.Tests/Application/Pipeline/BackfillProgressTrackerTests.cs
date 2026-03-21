using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class BackfillProgressTrackerTests
{
    [Fact]
    public void RegisterSymbol_SetsUpTracking()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 10);

        // Act
        tracker.RegisterSymbol("SPY", from, to);
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.TotalSymbols.Should().Be(1);
        snapshot.Symbols.Should().ContainKey("SPY");
        snapshot.Symbols["SPY"].TotalDays.Should().Be(10);
        snapshot.Symbols["SPY"].CompletedDays.Should().Be(0);
        snapshot.Symbols["SPY"].PercentComplete.Should().Be(0.0);
        snapshot.Symbols["SPY"].IsCompleted.Should().BeFalse();
        snapshot.Symbols["SPY"].IsFailed.Should().BeFalse();
    }

    [Fact]
    public void RecordProgress_UpdatesCompletedDays()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("AAPL", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));

        // Act
        tracker.RecordProgress("AAPL", 3);
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Symbols["AAPL"].CompletedDays.Should().Be(3);
        snapshot.Symbols["AAPL"].PercentComplete.Should().Be(30.0);
    }

    [Fact]
    public void RecordProgress_AccumulatesMultipleCalls()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("SPY", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20));

        // Act
        tracker.RecordProgress("SPY", 5);
        tracker.RecordProgress("SPY", 3);
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Symbols["SPY"].CompletedDays.Should().Be(8);
        snapshot.Symbols["SPY"].PercentComplete.Should().Be(40.0);
    }

    [Fact]
    public void MarkCompleted_SetsIsCompletedAndFullProgress()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("TSLA", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

        // Act
        tracker.MarkCompleted("TSLA");
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Symbols["TSLA"].IsCompleted.Should().BeTrue();
        snapshot.Symbols["TSLA"].CompletedDays.Should().Be(5);
        snapshot.Symbols["TSLA"].PercentComplete.Should().Be(100.0);
        snapshot.CompletedSymbols.Should().Be(1);
    }

    [Fact]
    public void MarkFailed_SetsIsFailedAndError()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("GOOG", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

        // Act
        tracker.MarkFailed("GOOG", "Rate limit exceeded");
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Symbols["GOOG"].IsFailed.Should().BeTrue();
        snapshot.Symbols["GOOG"].Error.Should().Be("Rate limit exceeded");
        snapshot.FailedSymbols.Should().Be(1);
    }

    [Fact]
    public void GetSnapshot_CalculatesOverallProgress()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("SPY", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));
        tracker.RegisterSymbol("AAPL", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));

        // Act - SPY 50%, AAPL 100%
        tracker.RecordProgress("SPY", 5);
        tracker.MarkCompleted("AAPL");
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.TotalSymbols.Should().Be(2);
        snapshot.CompletedSymbols.Should().Be(1);
        snapshot.OverallPercentComplete.Should().Be(75.0); // (5 + 10) / 20 * 100
    }

    [Fact]
    public void GetSnapshot_CapsPercentAt100()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("SPY", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

        // Act - report more bars than total days
        tracker.RecordProgress("SPY", 100);
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Symbols["SPY"].PercentComplete.Should().Be(100.0);
    }

    [Fact]
    public void Clear_RemovesAllTracking()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("SPY", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));
        tracker.RegisterSymbol("AAPL", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));

        // Act
        tracker.Clear();
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.TotalSymbols.Should().Be(0);
        snapshot.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void GetSnapshot_IsCaseInsensitive()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("spy", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));

        // Act
        tracker.RecordProgress("SPY", 5);
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Symbols.Should().ContainKey("SPY");
        snapshot.Symbols["SPY"].CompletedDays.Should().Be(5);
    }

    [Fact]
    public void MarkFailed_ForUnregisteredSymbol_DoesNotThrow()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();

        // Act & Assert
        tracker.MarkFailed("UNKNOWN", "some error"); // should not throw
        var snapshot = tracker.GetSnapshot();
        snapshot.TotalSymbols.Should().Be(0);
    }

    [Fact]
    public void GetSnapshot_IncludesTimestamp()
    {
        // Arrange
        var tracker = new BackfillProgressTracker();
        var before = DateTimeOffset.UtcNow;

        // Act
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Timestamp.Should().BeOnOrAfter(before);
        snapshot.Timestamp.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }
}
