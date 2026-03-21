using System.Threading;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Unit tests for CompositeSink including circuit breaker health tracking,
/// failure policies, and half-open recovery.
/// </summary>
public sealed class CompositeSinkTests
{
    private const int DefaultMaxFailures = 5;
    private static readonly TimeSpan DefaultResetTimeout = TimeSpan.FromSeconds(60);

    #region Fan-out basics

    [Fact]
    public async Task AppendAsync_FanOutToAllSinks()
    {
        var sink1 = new Mock<IStorageSink>();
        var sink2 = new Mock<IStorageSink>();
        var evt = CreateTestEvent();

        var composite = new CompositeSink(new[] { sink1.Object, sink2.Object });
        await composite.AppendAsync(evt);

        sink1.Verify(s => s.AppendAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
        sink2.Verify(s => s.AppendAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppendAsync_OneSinkFails_OtherStillReceives()
    {
        var sink1 = new Mock<IStorageSink>();
        sink1.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("Sink 1 failed"));
        var sink2 = new Mock<IStorageSink>();
        var evt = CreateTestEvent();

        var composite = new CompositeSink(new[] { sink1.Object, sink2.Object });
        await composite.AppendAsync(evt); // Should not throw

        sink2.Verify(s => s.AppendAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FlushAsync_FlushesAllSinks()
    {
        var sink1 = new Mock<IStorageSink>();
        var sink2 = new Mock<IStorageSink>();

        var composite = new CompositeSink(new[] { sink1.Object, sink2.Object });
        await composite.FlushAsync();

        sink1.Verify(s => s.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
        sink2.Verify(s => s.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FlushAsync_OneSinkFails_ThrowsAggregateException()
    {
        var sink1 = new Mock<IStorageSink>();
        sink1.Setup(s => s.FlushAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("Flush failed"));
        var sink2 = new Mock<IStorageSink>();

        var composite = new CompositeSink(new[] { sink1.Object, sink2.Object });
        await Assert.ThrowsAsync<AggregateException>(() => composite.FlushAsync());
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllSinks()
    {
        var sink1 = new Mock<IStorageSink>();
        var sink2 = new Mock<IStorageSink>();

        var composite = new CompositeSink(new[] { sink1.Object, sink2.Object });
        await composite.DisposeAsync();

        sink1.Verify(s => s.DisposeAsync(), Times.Once);
        sink2.Verify(s => s.DisposeAsync(), Times.Once);
    }

    #endregion

    #region Constructor validation

    [Fact]
    public void Constructor_EmptySinks_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CompositeSink(Enumerable.Empty<IStorageSink>()));
    }

    [Fact]
    public void Constructor_NullSinks_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeSink(null!));
    }

    [Fact]
    public void Constructor_MaxConsecutiveFailuresLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        var sink = new Mock<IStorageSink>();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CompositeSink(new[] { sink.Object }, maxConsecutiveFailures: 0));
    }

    [Fact]
    public void SinkCount_ReturnsCorrectCount()
    {
        var sink1 = new Mock<IStorageSink>();
        var sink2 = new Mock<IStorageSink>();
        var composite = new CompositeSink(new[] { sink1.Object, sink2.Object });
        Assert.Equal(2, composite.SinkCount);
    }

    #endregion

    #region Circuit breaker — tripping

    [Fact]
    public async Task CircuitBreaker_TripsAfterMaxConsecutiveFailures()
    {
        var clock = new ControllableTimeProvider();
        var failingSink = CreateFailingSink();
        var healthySink = new Mock<IStorageSink>();
        const int maxFailures = 3;

        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            maxConsecutiveFailures: maxFailures,
            timeProvider: clock);

        // Fail enough times to trip the breaker
        for (var i = 0; i < maxFailures; i++)
        {
            await composite.AppendAsync(CreateTestEvent());
        }

        // The failing sink should now be in Failed state
        var report = composite.GetSinkHealthReport();
        report[0].State.Should().Be(SinkHealthState.Failed);
        report[0].ConsecutiveFailures.Should().Be(maxFailures);
        report[0].TotalFailures.Should().Be(maxFailures);
        report[0].CircuitResetTime.Should().NotBeNull();
    }

    [Fact]
    public async Task CircuitBreaker_SkipsWritesToFailedSink()
    {
        var clock = new ControllableTimeProvider();
        var failingSink = CreateFailingSink();
        var healthySink = new Mock<IStorageSink>();
        const int maxFailures = 3;

        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            maxConsecutiveFailures: maxFailures,
            timeProvider: clock);

        // Trip the circuit breaker
        for (var i = 0; i < maxFailures; i++)
        {
            await composite.AppendAsync(CreateTestEvent());
        }

        // Reset the call count so we can measure from here
        failingSink.Invocations.Clear();

        // Next write should skip the failed sink
        await composite.AppendAsync(CreateTestEvent());

        failingSink.Verify(
            s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Failed sink should be skipped while circuit is open");
        healthySink.Verify(
            s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(maxFailures + 1));
    }

    [Fact]
    public async Task CircuitBreaker_FlushSkipsFailedSink()
    {
        var clock = new ControllableTimeProvider();
        var failingSink = CreateFailingSink();
        var healthySink = new Mock<IStorageSink>();
        const int maxFailures = 2;

        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            maxConsecutiveFailures: maxFailures,
            timeProvider: clock);

        // Trip the circuit
        for (var i = 0; i < maxFailures; i++)
        {
            await composite.AppendAsync(CreateTestEvent());
        }

        failingSink.Invocations.Clear();

        // Flush should skip the failed sink
        await composite.FlushAsync();

        failingSink.Verify(
            s => s.FlushAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "Flush should skip sinks with open circuit breaker");
        healthySink.Verify(
            s => s.FlushAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Circuit breaker — half-open recovery

    [Fact]
    public async Task CircuitBreaker_HalfOpenAfterResetTimeout_AllowsProbeWrite()
    {
        var clock = new ControllableTimeProvider();
        const int maxFailures = 2;
        var resetTimeout = TimeSpan.FromSeconds(30);

        // Sink that fails initially, then succeeds after circuit trips
        var callCount = 0;
        var sink = new Mock<IStorageSink>();
        sink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
            .Returns((MarketEvent _, CancellationToken _) =>
            {
                callCount++;
                if (callCount <= maxFailures)
                    throw new InvalidOperationException("Temporary failure");
                return ValueTask.CompletedTask;
            });

        var composite = new CompositeSink(
            new[] { sink.Object },
            maxConsecutiveFailures: maxFailures,
            circuitResetTimeout: resetTimeout,
            timeProvider: clock);

        // Trip the circuit
        for (var i = 0; i < maxFailures; i++)
        {
            await composite.AppendAsync(CreateTestEvent());
        }

        composite.GetSinkHealthReport()[0].State.Should().Be(SinkHealthState.Failed);

        // Advance time past the reset timeout
        clock.Advance(resetTimeout + TimeSpan.FromSeconds(1));

        // The sink should now be in half-open (Degraded) state
        composite.GetSinkHealthReport()[0].State.Should().Be(SinkHealthState.Degraded);

        // Probe write should succeed and close the circuit
        await composite.AppendAsync(CreateTestEvent());

        composite.GetSinkHealthReport()[0].State.Should().Be(SinkHealthState.Healthy);
        composite.GetSinkHealthReport()[0].ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpenProbeFailure_ReTripsCircuit()
    {
        var clock = new ControllableTimeProvider();
        const int maxFailures = 2;
        var resetTimeout = TimeSpan.FromSeconds(30);

        // Sink that always fails
        var failingSink = CreateFailingSink();

        var composite = new CompositeSink(
            new[] { failingSink.Object },
            maxConsecutiveFailures: maxFailures,
            circuitResetTimeout: resetTimeout,
            timeProvider: clock);

        // Trip the circuit
        for (var i = 0; i < maxFailures; i++)
        {
            await composite.AppendAsync(CreateTestEvent());
        }

        composite.FailedSinkCount.Should().Be(1);

        // Advance past reset timeout
        clock.Advance(resetTimeout + TimeSpan.FromSeconds(1));

        // Probe write will fail again
        await composite.AppendAsync(CreateTestEvent());

        // Should re-trip the circuit
        composite.GetSinkHealthReport()[0].State.Should().Be(SinkHealthState.Failed);
        composite.GetSinkHealthReport()[0].ConsecutiveFailures.Should().BeGreaterThan(maxFailures);
    }

    #endregion

    #region Health report

    [Fact]
    public void GetSinkHealthReport_AllHealthy_ReturnsCorrectStates()
    {
        var sink1 = new Mock<IStorageSink>();
        var sink2 = new Mock<IStorageSink>();

        var composite = new CompositeSink(new[] { sink1.Object, sink2.Object });
        var report = composite.GetSinkHealthReport();

        report.Should().HaveCount(2);
        report[0].State.Should().Be(SinkHealthState.Healthy);
        report[0].ConsecutiveFailures.Should().Be(0);
        report[0].TotalFailures.Should().Be(0);
        report[0].CircuitResetTime.Should().BeNull();
        report[0].SinkIndex.Should().Be(0);
        report[1].State.Should().Be(SinkHealthState.Healthy);
        report[1].SinkIndex.Should().Be(1);
    }

    [Fact]
    public async Task GetSinkHealthReport_DegradedSink_TracksConsecutiveFailures()
    {
        var clock = new ControllableTimeProvider();
        var failingSink = CreateFailingSink();
        const int maxFailures = 5;

        var composite = new CompositeSink(
            new[] { failingSink.Object },
            maxConsecutiveFailures: maxFailures,
            timeProvider: clock);

        // Fail fewer times than threshold
        await composite.AppendAsync(CreateTestEvent());
        await composite.AppendAsync(CreateTestEvent());

        var report = composite.GetSinkHealthReport();
        report[0].State.Should().Be(SinkHealthState.Degraded);
        report[0].ConsecutiveFailures.Should().Be(2);
        report[0].TotalFailures.Should().Be(2);
    }

    [Fact]
    public void GetSinkHealthReport_SinkType_ReflectsActualClassName()
    {
        var sink = new Mock<IStorageSink>();
        var composite = new CompositeSink(new[] { sink.Object });

        var report = composite.GetSinkHealthReport();

        // Moq proxies have a generated type name; the important thing is it's not null/empty
        report[0].SinkType.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Health count properties

    [Fact]
    public async Task HealthySinkCount_ReturnsCountOfHealthySinks()
    {
        var clock = new ControllableTimeProvider();
        var healthySink = new Mock<IStorageSink>();
        var failingSink = CreateFailingSink();
        const int maxFailures = 2;

        var composite = new CompositeSink(
            new[] { healthySink.Object, failingSink.Object },
            maxConsecutiveFailures: maxFailures,
            timeProvider: clock);

        composite.HealthySinkCount.Should().Be(2);

        // Trip the circuit on the failing sink
        for (var i = 0; i < maxFailures; i++)
        {
            await composite.AppendAsync(CreateTestEvent());
        }

        composite.HealthySinkCount.Should().Be(1);
        composite.FailedSinkCount.Should().Be(1);
    }

    [Fact]
    public async Task DegradedSinkCount_ReturnsCountOfDegradedSinks()
    {
        var clock = new ControllableTimeProvider();
        var failingSink = CreateFailingSink();
        const int maxFailures = 5;

        var composite = new CompositeSink(
            new[] { failingSink.Object },
            maxConsecutiveFailures: maxFailures,
            timeProvider: clock);

        // One failure puts it in Degraded state (below threshold)
        await composite.AppendAsync(CreateTestEvent());

        composite.DegradedSinkCount.Should().Be(1);
        composite.FailedSinkCount.Should().Be(0);
    }

    [Fact]
    public async Task AppendFailures_TracksAllFailures()
    {
        var clock = new ControllableTimeProvider();
        var failingSink = CreateFailingSink();

        var composite = new CompositeSink(
            new[] { failingSink.Object },
            maxConsecutiveFailures: 10,
            timeProvider: clock);

        await composite.AppendAsync(CreateTestEvent());
        await composite.AppendAsync(CreateTestEvent());
        await composite.AppendAsync(CreateTestEvent());

        composite.AppendFailures.Should().Be(3);
    }

    [Fact]
    public async Task TotalCircuitBreaks_TracksBreakEvents()
    {
        var clock = new ControllableTimeProvider();
        var failingSink = CreateFailingSink();
        const int maxFailures = 2;
        var resetTimeout = TimeSpan.FromSeconds(10);

        var composite = new CompositeSink(
            new[] { failingSink.Object },
            maxConsecutiveFailures: maxFailures,
            circuitResetTimeout: resetTimeout,
            timeProvider: clock);

        // Trip the circuit the first time
        for (var i = 0; i < maxFailures; i++)
        {
            await composite.AppendAsync(CreateTestEvent());
        }

        composite.TotalCircuitBreaks.Should().Be(1);

        // Advance past reset, then trip again
        clock.Advance(resetTimeout + TimeSpan.FromSeconds(1));
        // Half-open probe fails
        await composite.AppendAsync(CreateTestEvent());

        // The consecutive failures are now maxFailures+1, so the circuit
        // should be re-tripped. TotalCircuitBreaks may increase.
        composite.TotalCircuitBreaks.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Failure policy

    [Fact]
    public async Task FailOnAnyFailure_ThrowsImmediately()
    {
        var failingSink = CreateFailingSink();
        var healthySink = new Mock<IStorageSink>();

        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            failurePolicy: FailurePolicy.FailOnAnyFailure);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            composite.AppendAsync(CreateTestEvent()).AsTask());
    }

    [Fact]
    public async Task ContinueOnPartialFailure_DoesNotThrow()
    {
        var failingSink = CreateFailingSink();
        var healthySink = new Mock<IStorageSink>();

        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            failurePolicy: FailurePolicy.ContinueOnPartialFailure);

        // Should not throw
        await composite.AppendAsync(CreateTestEvent());
    }

    [Fact]
    public void FailurePolicy_DefaultIsContinueOnPartialFailure()
    {
        var sink = new Mock<IStorageSink>();
        var composite = new CompositeSink(new[] { sink.Object });
        composite.FailurePolicy.Should().Be(FailurePolicy.ContinueOnPartialFailure);
    }

    #endregion

    #region Recovery

    [Fact]
    public async Task CircuitBreaker_SuccessResetsConsecutiveFailures()
    {
        var clock = new ControllableTimeProvider();
        var callCount = 0;
        var sink = new Mock<IStorageSink>();
        // Fail first two calls, then succeed
        sink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
            .Returns((MarketEvent _, CancellationToken _) =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new InvalidOperationException("Transient failure");
                return ValueTask.CompletedTask;
            });

        var composite = new CompositeSink(
            new[] { sink.Object },
            maxConsecutiveFailures: DefaultMaxFailures,
            timeProvider: clock);

        // Two failures
        await composite.AppendAsync(CreateTestEvent());
        await composite.AppendAsync(CreateTestEvent());
        composite.GetSinkHealthReport()[0].ConsecutiveFailures.Should().Be(2);

        // Third call succeeds
        await composite.AppendAsync(CreateTestEvent());
        composite.GetSinkHealthReport()[0].ConsecutiveFailures.Should().Be(0);
        composite.GetSinkHealthReport()[0].State.Should().Be(SinkHealthState.Healthy);

        // TotalFailures should still reflect the 2 past failures
        composite.GetSinkHealthReport()[0].TotalFailures.Should().Be(2);
    }

    #endregion

    #region OperationCanceledException passthrough

    [Fact]
    public async Task AppendAsync_OperationCanceledException_IsNotCaughtByCircuitBreaker()
    {
        var sink = new Mock<IStorageSink>();
        sink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var composite = new CompositeSink(new[] { sink.Object });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            composite.AppendAsync(CreateTestEvent()).AsTask());

        // Should NOT count as a circuit breaker failure
        composite.AppendFailures.Should().Be(0);
    }

    #endregion

    #region Helpers

    private static Mock<IStorageSink> CreateFailingSink()
    {
        var sink = new Mock<IStorageSink>();
        sink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Persistent sink failure"));
        return sink;
    }

    private static MarketEvent CreateTestEvent()
        => MarketEvent.Heartbeat(DateTimeOffset.UtcNow);

    /// <summary>
    /// A controllable <see cref="TimeProvider"/> that allows tests to advance time
    /// deterministically, avoiding flaky timing-dependent tests.
    /// </summary>
    private sealed class ControllableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    #endregion
}
