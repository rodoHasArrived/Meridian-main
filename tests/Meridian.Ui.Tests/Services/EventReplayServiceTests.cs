using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="EventReplayService"/> — replay state management,
/// event args, data models, and state machine behavior.
/// </summary>
public sealed class EventReplayServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = EventReplayService.Instance;
        var b = EventReplayService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── ReplayOptions Defaults ───────────────────────────────────────

    [Fact]
    public void ReplayOptions_ShouldHaveDefaults()
    {
        var options = new ReplayOptions();
        options.FilePath.Should().BeNull();
        options.Symbol.Should().BeNull();
        options.FromDate.Should().BeNull();
        options.ToDate.Should().BeNull();
        options.EventTypes.Should().BeNull();
        options.SpeedMultiplier.Should().Be(1.0);
        options.PublishToEventBus.Should().BeFalse();
        options.PreserveTiming.Should().BeTrue();
    }

    [Fact]
    public void ReplayOptions_ShouldAcceptCustomValues()
    {
        var options = new ReplayOptions
        {
            FilePath = "/data/test.jsonl",
            Symbol = "SPY",
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            EventTypes = new[] { "Trade", "BboQuote" },
            SpeedMultiplier = 5.0,
            PublishToEventBus = true,
            PreserveTiming = false
        };

        options.FilePath.Should().Be("/data/test.jsonl");
        options.Symbol.Should().Be("SPY");
        options.SpeedMultiplier.Should().Be(5.0);
        options.EventTypes.Should().HaveCount(2);
    }

    // ── ReplayState Enum ─────────────────────────────────────────────

    [Theory]
    [InlineData(ReplayState.Unknown)]
    [InlineData(ReplayState.Initializing)]
    [InlineData(ReplayState.Playing)]
    [InlineData(ReplayState.Paused)]
    [InlineData(ReplayState.Stopped)]
    [InlineData(ReplayState.Completed)]
    [InlineData(ReplayState.Error)]
    public void ReplayState_AllValues_ShouldBeDefined(ReplayState state)
    {
        Enum.IsDefined(typeof(ReplayState), state).Should().BeTrue();
    }

    [Fact]
    public void ReplayState_ShouldHaveSevenValues()
    {
        Enum.GetValues<ReplayState>().Should().HaveCount(7);
    }

    // ── ReplayStartResult ────────────────────────────────────────────

    [Fact]
    public void ReplayStartResult_ShouldHaveDefaults()
    {
        var result = new ReplayStartResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.SessionId.Should().BeNull();
        result.TotalEvents.Should().Be(0);
        result.EstimatedDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ReplayStartResult_ShouldAcceptCustomValues()
    {
        var result = new ReplayStartResult
        {
            Success = true,
            SessionId = "session-123",
            TotalEvents = 50000,
            EstimatedDuration = TimeSpan.FromMinutes(5)
        };

        result.Success.Should().BeTrue();
        result.SessionId.Should().Be("session-123");
        result.TotalEvents.Should().Be(50000);
        result.EstimatedDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    // ── ReplayFilesResult ────────────────────────────────────────────

    [Fact]
    public void ReplayFilesResult_ShouldHaveDefaults()
    {
        var result = new ReplayFilesResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Files.Should().NotBeNull().And.BeEmpty();
    }

    // ── ReplayFileInfo ───────────────────────────────────────────────

    [Fact]
    public void ReplayFileInfo_ShouldHaveDefaults()
    {
        var info = new ReplayFileInfo();
        info.Path.Should().BeEmpty();
        info.FileName.Should().BeEmpty();
        info.Symbol.Should().BeEmpty();
        info.EventType.Should().BeEmpty();
        info.FileSizeBytes.Should().Be(0);
        info.EventCount.Should().Be(0);
        info.IsCompressed.Should().BeFalse();
    }

    [Fact]
    public void ReplayFileInfo_ShouldAcceptCustomValues()
    {
        var info = new ReplayFileInfo
        {
            Path = "/data/live/alpaca/2024-01-01/SPY_trades.jsonl.gz",
            FileName = "SPY_trades.jsonl.gz",
            Symbol = "SPY",
            EventType = "Trade",
            Date = new DateOnly(2024, 1, 1),
            FileSizeBytes = 1024 * 1024,
            EventCount = 50000,
            IsCompressed = true
        };

        info.Symbol.Should().Be("SPY");
        info.EventCount.Should().Be(50000);
        info.IsCompressed.Should().BeTrue();
    }

    // ── ReplayStatus ─────────────────────────────────────────────────

    [Fact]
    public void ReplayStatus_ShouldHaveDefaults()
    {
        var status = new ReplayStatus();
        status.State.Should().Be(ReplayState.Unknown);
        status.SessionId.Should().BeNull();
        status.EventsReplayed.Should().Be(0);
        status.TotalEvents.Should().Be(0);
        status.ProgressPercent.Should().Be(0);
        status.SpeedMultiplier.Should().Be(0);
        status.CurrentEventTime.Should().BeNull();
    }

    // ── EventPreviewResult ───────────────────────────────────────────

    [Fact]
    public void EventPreviewResult_ShouldHaveDefaults()
    {
        var result = new EventPreviewResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Events.Should().NotBeNull().And.BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    // ── ReplayEvent ──────────────────────────────────────────────────

    [Fact]
    public void ReplayEvent_ShouldHaveDefaults()
    {
        var evt = new ReplayEvent();
        evt.EventType.Should().BeEmpty();
        evt.Symbol.Should().BeEmpty();
        evt.Data.Should().BeNull();
    }

    // ── ReplayFileStats ──────────────────────────────────────────────

    [Fact]
    public void ReplayFileStats_ShouldHaveDefaults()
    {
        var stats = new ReplayFileStats();
        stats.EventCount.Should().Be(0);
        stats.Duration.Should().Be(TimeSpan.Zero);
        stats.EventTypeCounts.Should().BeNull();
    }

    // ── Event Args Models ────────────────────────────────────────────

    [Fact]
    public void ReplayStateChangedEventArgs_ShouldHaveDefaults()
    {
        var args = new ReplayStateChangedEventArgs();
        args.SessionId.Should().BeEmpty();
        args.State.Should().Be(ReplayState.Unknown);
    }

    [Fact]
    public void ReplayEventArgs_ShouldHaveDefaults()
    {
        var args = new ReplayEventArgs();
        args.Event.Should().BeNull();
    }

    [Fact]
    public void ReplayProgressEventArgs_ShouldHaveDefaults()
    {
        var args = new ReplayProgressEventArgs();
        args.SessionId.Should().BeEmpty();
        args.EventsReplayed.Should().Be(0);
        args.TotalEvents.Should().Be(0);
        args.ProgressPercent.Should().Be(0);
        args.Elapsed.Should().Be(TimeSpan.Zero);
        args.Remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ReplayProgressEventArgs_ShouldAcceptCustomValues()
    {
        var args = new ReplayProgressEventArgs
        {
            SessionId = "session-abc",
            EventsReplayed = 25000,
            TotalEvents = 50000,
            ProgressPercent = 50.0,
            Elapsed = TimeSpan.FromMinutes(2),
            Remaining = TimeSpan.FromMinutes(2)
        };

        args.ProgressPercent.Should().Be(50.0);
        args.EventsReplayed.Should().Be(25000);
    }

    // ── API Response Models ──────────────────────────────────────────

    [Fact]
    public void ReplayFilesResponse_ShouldHaveDefaults()
    {
        var response = new ReplayFilesResponse();
        response.Files.Should().BeNull();
    }

    [Fact]
    public void ReplayStartResponse_ShouldHaveDefaults()
    {
        var response = new ReplayStartResponse();
        response.SessionId.Should().BeNull();
        response.TotalEvents.Should().Be(0);
        response.EstimatedDurationSeconds.Should().Be(0);
    }
}
