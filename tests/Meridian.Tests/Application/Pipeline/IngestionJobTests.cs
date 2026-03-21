using FluentAssertions;
using Meridian.Contracts.Pipeline;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

/// <summary>
/// Tests for the unified ingestion job state machine contract.
/// </summary>
public sealed class IngestionJobTests
{
    [Fact]
    public void NewJob_HasDraftState()
    {
        var job = new IngestionJob();

        job.State.Should().Be(IngestionJobState.Draft);
        job.IsTerminal.Should().BeFalse();
        job.IsResumable.Should().BeFalse();
        job.JobId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryTransition_Draft_To_Queued_Succeeds()
    {
        var job = new IngestionJob();

        var result = job.TryTransition(IngestionJobState.Queued);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Queued);
    }

    [Fact]
    public void TryTransition_Queued_To_Running_SetsStartedAt()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);

        var result = job.TryTransition(IngestionJobState.Running);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Running);
        job.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void TryTransition_Running_To_Completed_SetsCompletedAt()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);

        var result = job.TryTransition(IngestionJobState.Completed);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Completed);
        job.CompletedAt.Should().NotBeNull();
        job.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void TryTransition_Running_To_Failed_SetsCompletedAt()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);

        var result = job.TryTransition(IngestionJobState.Failed);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Failed);
        job.CompletedAt.Should().NotBeNull();
        job.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void TryTransition_Running_To_Paused_Succeeds()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);

        var result = job.TryTransition(IngestionJobState.Paused);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Paused);
        job.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void TryTransition_Paused_To_Running_Succeeds()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);
        job.TryTransition(IngestionJobState.Paused);

        var result = job.TryTransition(IngestionJobState.Running);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Running);
    }

    [Fact]
    public void TryTransition_Failed_To_Queued_AllowsRetry()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);
        job.TryTransition(IngestionJobState.Failed);

        var result = job.TryTransition(IngestionJobState.Queued);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Queued);
        job.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void TryTransition_Invalid_Draft_To_Running_Fails()
    {
        var job = new IngestionJob();

        var result = job.TryTransition(IngestionJobState.Running);

        result.Should().BeFalse();
        job.State.Should().Be(IngestionJobState.Draft);
    }

    [Fact]
    public void TryTransition_Invalid_Completed_To_Running_Fails()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);
        job.TryTransition(IngestionJobState.Completed);

        var result = job.TryTransition(IngestionJobState.Running);

        result.Should().BeFalse();
        job.State.Should().Be(IngestionJobState.Completed);
    }

    [Fact]
    public void TryTransition_Queued_To_Cancelled_Succeeds()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);

        var result = job.TryTransition(IngestionJobState.Cancelled);

        result.Should().BeTrue();
        job.State.Should().Be(IngestionJobState.Cancelled);
        job.IsTerminal.Should().BeTrue();
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void IsResumable_WhenFailedWithCheckpoint_ReturnsTrue()
    {
        var job = new IngestionJob
        {
            CheckpointToken = new IngestionCheckpointToken
            {
                LastSymbol = "SPY",
                LastDate = DateTime.UtcNow
            }
        };
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);
        job.TryTransition(IngestionJobState.Failed);

        job.IsResumable.Should().BeTrue();
    }

    [Fact]
    public void IsResumable_WhenFailedWithoutCheckpoint_ReturnsFalse()
    {
        var job = new IngestionJob();
        job.TryTransition(IngestionJobState.Queued);
        job.TryTransition(IngestionJobState.Running);
        job.TryTransition(IngestionJobState.Failed);

        job.IsResumable.Should().BeFalse();
    }

    [Fact]
    public void RetryEnvelope_ExponentialBackoff_CalculatesCorrectly()
    {
        var envelope = new RetryEnvelope
        {
            BaseDelaySeconds = 30,
            MaxRetries = 3,
            AttemptCount = 0
        };

        envelope.IsExhausted.Should().BeFalse();
        envelope.NextDelay.Should().Be(TimeSpan.FromSeconds(30));

        envelope.AttemptCount = 1;
        envelope.NextDelay.Should().Be(TimeSpan.FromSeconds(60));

        envelope.AttemptCount = 2;
        envelope.NextDelay.Should().Be(TimeSpan.FromSeconds(120));

        envelope.AttemptCount = 3;
        envelope.IsExhausted.Should().BeTrue();
    }

    [Fact]
    public void SymbolProgress_TracksPercentage()
    {
        var progress = new IngestionSymbolProgress
        {
            Symbol = "AAPL",
            DataPointsProcessed = 250,
            ExpectedDataPoints = 1000
        };

        progress.ProgressPercent.Should().Be(25.0);
    }

    [Fact]
    public void SymbolProgress_ZeroExpected_ReturnsZeroPercent()
    {
        var progress = new IngestionSymbolProgress
        {
            Symbol = "AAPL",
            DataPointsProcessed = 100,
            ExpectedDataPoints = 0
        };

        progress.ProgressPercent.Should().Be(0);
    }

    [Theory]
    [InlineData(IngestionJobState.Draft, IngestionJobState.Queued, true)]
    [InlineData(IngestionJobState.Queued, IngestionJobState.Running, true)]
    [InlineData(IngestionJobState.Running, IngestionJobState.Paused, true)]
    [InlineData(IngestionJobState.Running, IngestionJobState.Completed, true)]
    [InlineData(IngestionJobState.Running, IngestionJobState.Failed, true)]
    [InlineData(IngestionJobState.Running, IngestionJobState.Cancelled, true)]
    [InlineData(IngestionJobState.Paused, IngestionJobState.Running, true)]
    [InlineData(IngestionJobState.Paused, IngestionJobState.Cancelled, true)]
    [InlineData(IngestionJobState.Failed, IngestionJobState.Queued, true)]
    [InlineData(IngestionJobState.Draft, IngestionJobState.Running, false)]
    [InlineData(IngestionJobState.Draft, IngestionJobState.Completed, false)]
    [InlineData(IngestionJobState.Completed, IngestionJobState.Running, false)]
    [InlineData(IngestionJobState.Cancelled, IngestionJobState.Running, false)]
    public void IsValidTransition_ReturnsExpected(IngestionJobState from, IngestionJobState to, bool expected)
    {
        IngestionJob.IsValidTransition(from, to).Should().Be(expected);
    }

    [Fact]
    public void FullLifecycle_HistoricalBackfill()
    {
        var job = new IngestionJob
        {
            WorkloadType = IngestionWorkloadType.Historical,
            Symbols = new[] { "SPY", "AAPL", "MSFT" },
            Provider = "alpaca",
            FromDate = new DateTime(2024, 1, 1),
            ToDate = new DateTime(2024, 12, 31),
            Sla = new IngestionSla
            {
                MinimumCompleteness = 0.95f,
                CompletionDeadline = DateTime.UtcNow.AddHours(2)
            }
        };

        // Draft → Queued
        job.TryTransition(IngestionJobState.Queued).Should().BeTrue();

        // Queued → Running
        job.TryTransition(IngestionJobState.Running).Should().BeTrue();
        job.StartedAt.Should().NotBeNull();

        // Update checkpoint
        job.CheckpointToken = new IngestionCheckpointToken
        {
            LastSymbol = "SPY",
            LastDate = new DateTime(2024, 6, 15),
            LastOffset = 12345
        };

        // Running → Paused (user pause)
        job.TryTransition(IngestionJobState.Paused).Should().BeTrue();

        // Paused → Running (resume)
        job.TryTransition(IngestionJobState.Running).Should().BeTrue();

        // Running → Completed
        job.TryTransition(IngestionJobState.Completed).Should().BeTrue();
        job.IsTerminal.Should().BeTrue();
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void FullLifecycle_RealtimeWithFailureAndRetry()
    {
        var job = new IngestionJob
        {
            WorkloadType = IngestionWorkloadType.Realtime,
            Symbols = new[] { "SPY" },
            Provider = "alpaca",
            RetryEnvelope = new RetryEnvelope { MaxRetries = 3 }
        };

        // Draft → Queued → Running
        job.TryTransition(IngestionJobState.Queued).Should().BeTrue();
        job.TryTransition(IngestionJobState.Running).Should().BeTrue();

        // Running → Failed (provider disconnection)
        job.TryTransition(IngestionJobState.Failed).Should().BeTrue();
        job.ErrorMessage = "Provider disconnected";
        job.RetryEnvelope.AttemptCount = 1;

        // Set checkpoint for gap-fill on resume
        job.CheckpointToken = new IngestionCheckpointToken
        {
            GapFillWindowStart = DateTime.UtcNow.AddMinutes(-5),
            LastOffset = 99999
        };

        // Failed → Queued (retry)
        job.TryTransition(IngestionJobState.Queued).Should().BeTrue();
        job.IsResumable.Should().BeFalse(); // Not resumable while queued

        // Queued → Running (retry execution)
        job.TryTransition(IngestionJobState.Running).Should().BeTrue();

        // Running → Completed (success on retry)
        job.TryTransition(IngestionJobState.Completed).Should().BeTrue();
        job.IsTerminal.Should().BeTrue();
    }
}
