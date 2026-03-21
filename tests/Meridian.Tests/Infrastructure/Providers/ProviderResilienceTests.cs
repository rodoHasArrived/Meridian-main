using System.Threading;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Moq;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider resilience tests for circuit-breaker and automatic-recovery scenarios.
/// Covers CompositeSink circuit-breaker state transitions and StreamingFailoverService
/// reconnection and recovery threshold behaviour (improvement 5.3).
/// </summary>
public sealed class CompositeSinkCircuitBreakerTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static MarketEvent CreateEvent(string symbol = "SPY")
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 450m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1);
        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, seq: 1);
    }

    private static Mock<IStorageSink> CreateFailingSink(int failAfterNth = 0)
    {
        var mock = new Mock<IStorageSink>();
        var calls = 0;
        mock.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
            .Returns<MarketEvent, CancellationToken>((_, _) =>
            {
                calls++;
                if (calls > failAfterNth)
                    throw new InvalidOperationException($"Simulated sink failure on call {calls}");
                return ValueTask.CompletedTask;
            });
        return mock;
    }

    // -----------------------------------------------------------------------
    // Circuit-breaker state transitions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AppendAsync_SingleFailure_SinkRemainsHealthy()
    {
        // Arrange
        var failingSink = CreateFailingSink(failAfterNth: 0);
        var healthySink = new Mock<IStorageSink>();
        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            maxConsecutiveFailures: 3);
        var evt = CreateEvent();

        // Act – first failure should not open the circuit (threshold is 3)
        await composite.AppendAsync(evt);

        // Assert – only the failing sink is degraded; healthy sink still receives
        var report = composite.GetSinkHealthReport();
        report[0].State.Should().Be(SinkHealthState.Degraded,
            "one failure is below the open threshold of 3");
        report[1].State.Should().Be(SinkHealthState.Healthy);
        healthySink.Verify(s => s.AppendAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppendAsync_RepeatedFailures_OpensCircuit()
    {
        // Arrange – threshold of 2 consecutive failures opens the circuit
        var failingSink = new Mock<IStorageSink>();
        failingSink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("persistent failure"));
        var composite = new CompositeSink(
            new[] { failingSink.Object },
            maxConsecutiveFailures: 2);
        var evt = CreateEvent();

        // Act – exhaust the failure threshold
        await composite.AppendAsync(evt);
        await composite.AppendAsync(evt);

        // Assert – circuit is now open
        var report = composite.GetSinkHealthReport();
        report[0].State.Should().Be(SinkHealthState.Failed,
            "consecutive failures >= threshold should open the circuit");
        report[0].ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task AppendAsync_OpenCircuit_SkipsFailedSinkWithoutCallingIt()
    {
        // Arrange – open the circuit first
        var failingSink = new Mock<IStorageSink>();
        failingSink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("always fails"));
        var healthySink = new Mock<IStorageSink>();
        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            maxConsecutiveFailures: 2);

        var evt = CreateEvent();

        // Trip the circuit
        await composite.AppendAsync(evt); // failure 1
        await composite.AppendAsync(evt); // failure 2 → circuit opens

        // Reset interaction counts
        failingSink.Invocations.Clear();

        // Act – subsequent calls should skip the tripped sink
        await composite.AppendAsync(evt);

        // Assert – failed sink is never called while open
        failingSink.Verify(
            s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "open circuit should not forward events to the failed sink");

        // Healthy sink still receives the event
        healthySink.Verify(
            s => s.AppendAsync(evt, It.IsAny<CancellationToken>()),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task AppendAsync_HalfOpenProbe_SuccessClosesCircuit()
    {
        // Arrange – open the circuit, then let reset timeout expire so the sink enters half-open
        var callCount = 0;
        var failingSink = new Mock<IStorageSink>();
        failingSink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
                   .Returns<MarketEvent, CancellationToken>((_, _) =>
                   {
                       callCount++;
                       if (callCount <= 2)
                           throw new InvalidOperationException("initial failure");
                       return ValueTask.CompletedTask; // succeeds after circuit reset
                   });

        // Use a very short reset timeout so we can advance past it
        var composite = new CompositeSink(
            new[] { failingSink.Object },
            maxConsecutiveFailures: 2,
            circuitResetTimeout: TimeSpan.FromMilliseconds(1));

        var evt = CreateEvent();

        // Open the circuit
        await composite.AppendAsync(evt); // fail 1
        await composite.AppendAsync(evt); // fail 2 → open

        // Wait for the reset timeout
        await Task.Delay(20);

        // Act – this probe should succeed and close the circuit
        await composite.AppendAsync(evt);

        // Assert
        var report = composite.GetSinkHealthReport();
        report[0].State.Should().Be(SinkHealthState.Healthy,
            "successful probe after reset timeout should close the circuit");
    }

    [Fact]
    public async Task AppendAsync_FailOnAnyPolicy_ThrowsOnFirstSinkFailure()
    {
        // Arrange
        var failingSink = new Mock<IStorageSink>();
        failingSink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("failure"));
        var healthySink = new Mock<IStorageSink>();
        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            failurePolicy: FailurePolicy.FailOnAnyFailure);

        var evt = CreateEvent();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => composite.AppendAsync(evt).AsTask());
    }

    [Fact]
    public async Task FlushAsync_OpenCircuitSink_SkipsFlush()
    {
        // Arrange – trip the circuit on the first sink
        var failingSink = new Mock<IStorageSink>();
        failingSink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("always fails"));
        failingSink.Setup(s => s.FlushAsync(It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);
        var healthySink = new Mock<IStorageSink>();

        var composite = new CompositeSink(
            new[] { failingSink.Object, healthySink.Object },
            maxConsecutiveFailures: 1);

        var evt = CreateEvent();
        await composite.AppendAsync(evt); // opens circuit on failing sink

        failingSink.Invocations.Clear();

        // Act – flush should skip the open-circuit sink
        await composite.FlushAsync();

        // Assert
        failingSink.Verify(s => s.FlushAsync(It.IsAny<CancellationToken>()), Times.Never,
            "flush should skip sinks with open circuit breaker");
        healthySink.Verify(s => s.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

/// <summary>
/// Resilience tests for <see cref="StreamingFailoverService"/> covering automatic
/// failover after consecutive failures and recovery after successes (improvement 5.3).
/// </summary>
public sealed class StreamingFailoverServiceResilienceTests : IDisposable
{
    private readonly ConnectionHealthMonitor _healthMonitor;
    private readonly StreamingFailoverService _service;

    private readonly FailoverRuleConfig _rule = new(
        Id: "resilience-rule",
        PrimaryProviderId: "primary",
        BackupProviderIds: new[] { "backup" },
        FailoverThreshold: 3,
        RecoveryThreshold: 2
    );

    public StreamingFailoverServiceResilienceTests()
    {
        _healthMonitor = new ConnectionHealthMonitor();
        _service = new StreamingFailoverService(_healthMonitor);

        _service.RegisterProvider("primary");
        _service.RegisterProvider("backup");
    }

    public void Dispose()
    {
        _service.Dispose();
        _healthMonitor.Dispose();
    }

    [Fact]
    public void RecordFailure_BelowThreshold_ConsecutiveCountAccumulates()
    {
        // Arrange
        var config = new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: new[] { _rule });
        _service.Start(config);

        // Act – record two failures (threshold is 3)
        _service.RecordFailure("primary", "timeout");
        _service.RecordFailure("primary", "timeout");

        // Assert – consecutive count is tracked but threshold not reached
        var snap = _service.GetProviderHealthSnapshots().Single(s => s.ProviderId == "primary");
        snap.ConsecutiveFailures.Should().Be(2,
            "each RecordFailure should increment the consecutive failure counter");
        // Failover has not been triggered yet
        _service.GetActiveProviderId("resilience-rule").Should().Be("primary");
    }

    [Fact]
    public void RecordSuccess_AfterFailures_ResetsConsecutiveFailureCount()
    {
        // Arrange
        _service.RecordFailure("primary", "connection lost");
        _service.RecordFailure("primary", "connection lost");

        // Act – one success should reset consecutive failure counter
        _service.RecordSuccess("primary");

        // Assert
        var snap = _service.GetProviderHealthSnapshots().Single(s => s.ProviderId == "primary");
        snap.ConsecutiveFailures.Should().Be(0,
            "a success resets the consecutive failure counter");
        snap.ConsecutiveSuccesses.Should().Be(1);
    }

    [Fact]
    public void ForceFailover_ThenRecordSuccesses_RestorationTrackedViaRecoveryThreshold()
    {
        // Arrange – force failover first
        var config = new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: new[] { _rule });
        _service.Start(config);
        _service.ForceFailover("resilience-rule", "backup");

        _service.GetActiveProviderId("resilience-rule").Should().Be("backup");

        // Act – record successes on primary
        _service.RecordSuccess("primary");
        _service.RecordSuccess("primary");

        var snap = _service.GetProviderHealthSnapshots().Single(s => s.ProviderId == "primary");
        snap.ConsecutiveSuccesses.Should().Be(2,
            "consecutive successes must be tracked so recovery logic can evaluate the threshold");
    }

    [Fact]
    public void RecordFailure_InterleavedWithSuccess_ResetsConsecutiveCount()
    {
        // Arrange
        var config = new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: new[] { _rule });
        _service.Start(config);

        // Act – two failures, one success (resets count), then two more failures
        _service.RecordFailure("primary", "err");
        _service.RecordFailure("primary", "err");
        _service.RecordSuccess("primary"); // resets consecutive count
        _service.RecordFailure("primary", "err");
        _service.RecordFailure("primary", "err");

        // Assert – counter only reflects the failures SINCE the last success
        var snap = _service.GetProviderHealthSnapshots().Single(s => s.ProviderId == "primary");
        snap.ConsecutiveFailures.Should().Be(2,
            "a success between failures should reset the consecutive failure counter");
    }

    [Fact]
    public void RecordLatency_AverageCalculatedCorrectly()
    {
        // Arrange
        _service.RecordLatency("primary", 50.0);
        _service.RecordLatency("primary", 100.0);
        _service.RecordLatency("primary", 150.0);

        // Assert
        var snap = _service.GetProviderHealthSnapshots().Single(s => s.ProviderId == "primary");
        snap.AverageLatencyMs.Should().BeApproximately(100.0, 0.01,
            "average latency should be the mean of all recorded samples");
    }

    [Fact]
    public void RecordFailure_AppendedToRecentIssues()
    {
        // Arrange & Act
        _service.RecordFailure("primary", "rate-limited");
        _service.RecordFailure("backup", "timeout");

        var primarySnap = _service.GetProviderHealthSnapshots().Single(s => s.ProviderId == "primary");
        var backupSnap = _service.GetProviderHealthSnapshots().Single(s => s.ProviderId == "backup");

        // Assert
        primarySnap.RecentIssues.Should().Contain(i => i.Contains("rate-limited"),
            "failure reason should be recorded in RecentIssues");
        backupSnap.RecentIssues.Should().Contain(i => i.Contains("timeout"));
        backupSnap.ConsecutiveFailures.Should().Be(1);
        primarySnap.ConsecutiveSuccesses.Should().Be(0);
    }

    [Fact]
    public void GetProviderHealthSnapshots_AfterMixedActivity_ReflectsCorrectState()
    {
        // Arrange & Act
        _service.RecordSuccess("primary");
        _service.RecordSuccess("primary");
        _service.RecordFailure("backup", "timeout");

        var snapshots = _service.GetProviderHealthSnapshots();

        // Assert
        var primarySnap = snapshots.Single(s => s.ProviderId == "primary");
        primarySnap.ConsecutiveSuccesses.Should().Be(2);
        primarySnap.ConsecutiveFailures.Should().Be(0);

        var backupSnap = snapshots.Single(s => s.ProviderId == "backup");
        backupSnap.ConsecutiveFailures.Should().Be(1);
        backupSnap.ConsecutiveSuccesses.Should().Be(0);
    }

    [Fact]
    public void ForceFailover_MultipleProviders_TracksFailoverCount()
    {
        // Arrange
        _service.RegisterProvider("backup2");
        var ruleWithTwoBackups = new FailoverRuleConfig(
            Id: "multi-backup-rule",
            PrimaryProviderId: "primary",
            BackupProviderIds: new[] { "backup", "backup2" },
            FailoverThreshold: 3,
            RecoveryThreshold: 2);

        var config = new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: new[] { ruleWithTwoBackups });
        _service.Start(config);

        // Act
        _service.ForceFailover("multi-backup-rule", "backup");
        _service.ForceFailover("multi-backup-rule", "backup2");

        // Assert
        var snap = _service.GetRuleSnapshots().Single(s => s.RuleId == "multi-backup-rule");
        snap.FailoverCount.Should().Be(2);
        snap.CurrentActiveProviderId.Should().Be("backup2");
        snap.LastFailoverTime.Should().NotBeNull();
    }
}
