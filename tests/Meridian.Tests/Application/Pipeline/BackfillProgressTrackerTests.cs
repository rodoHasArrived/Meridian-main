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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();
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
        using var tracker = new BackfillProgressTracker();

        // Act & Assert
        tracker.MarkFailed("UNKNOWN", "some error"); // should not throw
        var snapshot = tracker.GetSnapshot();
        snapshot.TotalSymbols.Should().Be(0);
    }

    [Fact]
    public void GetSnapshot_IncludesTimestamp()
    {
        // Arrange
        using var tracker = new BackfillProgressTracker();
        var before = DateTimeOffset.UtcNow;

        // Act
        var snapshot = tracker.GetSnapshot();

        // Assert
        snapshot.Timestamp.Should().BeOnOrAfter(before);
        snapshot.Timestamp.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Publish_RecordsProviderRangeAndAttemptInSnapshot()
    {
        using var tracker = new BackfillProgressTracker();
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 3);
        tracker.RegisterSymbol("SPY", from, to);

        tracker.Publish(new ProviderBackfillProgress(
            "SPY",
            "polygon",
            BarsDownloaded: 0,
            TotalSymbols: 1,
            CurrentSymbolIndex: 1,
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            CurrentStatus: "trying",
            RangeStart: from,
            RangeEnd: to,
            ProviderAttempt: 2,
            RetryRound: 1,
            Operation: "bars",
            ObservedAt: DateTimeOffset.UtcNow));

        var snapshot = tracker.GetSnapshot();
        var symbol = snapshot.Symbols["SPY"];
        symbol.CurrentProvider.Should().Be("polygon");
        symbol.CurrentStatus.Should().Be("trying");
        symbol.ProviderAttempt.Should().Be(2);
        symbol.RetryRound.Should().Be(1);
        symbol.Operation.Should().Be("bars");
        snapshot.RecentProviderAttempts.Should().ContainSingle();
    }

    [Fact]
    public void Publish_WithDownloadedBars_DoesNotTreatBarsAsCompletedCalendarDays()
    {
        using var tracker = new BackfillProgressTracker();
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 3);
        tracker.RegisterSymbol("SPY", from, to);

        tracker.Publish(new ProviderBackfillProgress(
            "SPY",
            "polygon",
            BarsDownloaded: 250,
            TotalSymbols: 1,
            CurrentSymbolIndex: 1,
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            CurrentStatus: "provider-succeeded",
            RangeStart: from,
            RangeEnd: to,
            ProviderAttempt: 1,
            RetryRound: 0,
            Operation: "bars",
            ObservedAt: DateTimeOffset.UtcNow));

        var snapshot = tracker.GetSnapshot();
        snapshot.Symbols["SPY"].CompletedDays.Should().Be(0);
        snapshot.Symbols["SPY"].PercentComplete.Should().Be(0);
        snapshot.OverallPercentComplete.Should().Be(0);
        snapshot.CompletedSymbols.Should().Be(0);
        snapshot.RecentProviderAttempts.Should().ContainSingle()
            .Which.BarsDownloaded.Should().Be(250);
    }

    [Fact]
    public async Task Publish_WhenSubscriberIsSlow_DropsOldestWithoutBlockingProducer()
    {
        using var tracker = new BackfillProgressTracker(notificationCapacity: 1, historyCapacity: 4);
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseHandler = new ManualResetEventSlim();
        tracker.ProgressPublished += _ =>
        {
            handlerEntered.TrySetResult();
            releaseHandler.Wait(TimeSpan.FromSeconds(10));
        };

        tracker.Publish(CreateProgress(1));
        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var started = DateTimeOffset.UtcNow;
        for (var attempt = 2; attempt <= 50; attempt++)
            tracker.Publish(CreateProgress(attempt));
        var elapsed = DateTimeOffset.UtcNow - started;

        releaseHandler.Set();
        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(250));
        tracker.GetSnapshot().DroppedProviderNotifications.Should().BeGreaterThan(0);

        static ProviderBackfillProgress CreateProgress(int attempt) => new(
            "AAPL",
            "stooq",
            BarsDownloaded: 0,
            TotalSymbols: 1,
            CurrentSymbolIndex: 1,
            StartedAt: DateTimeOffset.UtcNow,
            CurrentStatus: "trying",
            RangeStart: new DateOnly(2026, 7, 1),
            RangeEnd: new DateOnly(2026, 7, 2),
            ProviderAttempt: attempt,
            RetryRound: 1,
            Operation: "bars",
            ObservedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public void MarkSkipped_ReportsTerminalCompletedStateWithoutFailure()
    {
        using var tracker = new BackfillProgressTracker();
        tracker.RegisterSymbol("META", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2));

        tracker.MarkSkipped("META");

        var symbol = tracker.GetSnapshot().Symbols["META"];
        symbol.IsSkipped.Should().BeTrue();
        symbol.IsCompleted.Should().BeTrue();
        symbol.IsFailed.Should().BeFalse();
        symbol.PercentComplete.Should().Be(100);
    }
}
