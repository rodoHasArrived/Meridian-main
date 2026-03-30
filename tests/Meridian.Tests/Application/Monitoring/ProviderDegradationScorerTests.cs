using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

namespace Meridian.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="ProviderDegradationScorer"/> composite health scoring.
/// Verifies H4: Graceful Provider Degradation Scoring.
/// </summary>
public sealed class ProviderDegradationScorerTests : IDisposable
{
    private readonly ConnectionHealthMonitor _healthMonitor;
    private readonly ProviderLatencyService _latencyService;
    private readonly ProviderDegradationScorer _scorer;

    public ProviderDegradationScorerTests()
    {
        _healthMonitor = new ConnectionHealthMonitor(new ConnectionHealthConfig
        {
            HeartbeatIntervalSeconds = 600, // Disable automatic checks in tests
            HeartbeatTimeoutSeconds = 600
        });
        _latencyService = new ProviderLatencyService();
        _scorer = new ProviderDegradationScorer(
            _healthMonitor, _latencyService,
            new ProviderDegradationConfig
            {
                EvaluationIntervalSeconds = 600 // Disable automatic evaluation in tests
            });
    }

    [Fact]
    public void GetScore_HealthyProvider_ReturnsLowScore()
    {
        // Arrange
        _healthMonitor.RegisterConnection("alpaca-1", "alpaca");
        _latencyService.RecordLatency("alpaca", 10.0);
        _latencyService.RecordLatency("alpaca", 15.0);
        _scorer.RecordSuccess("alpaca");
        _scorer.RecordSuccess("alpaca");

        // Act
        var score = _scorer.GetScore("alpaca");

        // Assert
        score.CompositeScore.Should().BeLessThan(0.3);
        score.IsDegraded.Should().BeFalse();
        score.IsConnected.Should().BeTrue();
        score.ProviderName.Should().Be("alpaca");
    }

    [Fact]
    public void GetScore_DisconnectedProvider_ReturnsHighConnectionScore()
    {
        // Arrange
        _healthMonitor.RegisterConnection("polygon-1", "polygon");
        _healthMonitor.MarkDisconnected("polygon-1", "Network error");

        // Act
        var score = _scorer.GetScore("polygon");

        // Assert
        score.ConnectionScore.Should().Be(1.0);
        score.CompositeScore.Should().BeGreaterThan(0.3);
        score.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void GetScore_HighLatencyProvider_ReturnsHighLatencyScore()
    {
        // Arrange
        _healthMonitor.RegisterConnection("slow-provider", "slow-provider");
        // Record many high-latency samples to push P95 above threshold
        for (int i = 0; i < 100; i++)
        {
            _latencyService.RecordLatency("slow-provider", 1500.0);
        }

        // Act
        var score = _scorer.GetScore("slow-provider");

        // Assert
        score.LatencyScore.Should().BeGreaterThan(0.5);
        score.P95LatencyMs.Should().BeGreaterThan(200);
    }

    [Fact]
    public void GetScore_HighErrorRate_ReturnsHighErrorScore()
    {
        // Arrange
        _healthMonitor.RegisterConnection("error-provider", "error-provider");
        // Record 80% error rate
        for (int i = 0; i < 80; i++)
        {
            _scorer.RecordError("error-provider", "timeout");
        }
        for (int i = 0; i < 20; i++)
        {
            _scorer.RecordSuccess("error-provider");
        }

        // Act
        var score = _scorer.GetScore("error-provider");

        // Assert
        score.ErrorRateScore.Should().BeGreaterThan(0.5);
        score.ErrorRate.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void GetScore_UnknownProvider_ReturnsZeroScore()
    {
        // Act
        var score = _scorer.GetScore("nonexistent-provider");

        // Assert
        score.CompositeScore.Should().Be(0.0);
        score.IsDegraded.Should().BeFalse();
        score.ProviderName.Should().Be("nonexistent-provider");
    }

    [Fact]
    public void GetAllScores_ReturnsAllKnownProviders()
    {
        // Arrange
        _healthMonitor.RegisterConnection("provider-a", "provider-a");
        _healthMonitor.RegisterConnection("provider-b", "provider-b");
        _scorer.RecordSuccess("provider-c"); // Provider without connection but with error tracking

        // Act
        var scores = _scorer.GetAllScores();

        // Assert
        scores.Should().HaveCountGreaterThanOrEqualTo(3);
        scores.Select(s => s.ProviderName).Should().Contain("provider-a");
        scores.Select(s => s.ProviderName).Should().Contain("provider-b");
        scores.Select(s => s.ProviderName).Should().Contain("provider-c");
    }

    [Fact]
    public void GetProvidersByHealth_ReturnsOrderedList()
    {
        // Arrange
        _healthMonitor.RegisterConnection("healthy", "healthy");
        _healthMonitor.RegisterConnection("degraded", "degraded");
        _healthMonitor.MarkDisconnected("degraded", "test");

        _latencyService.RecordLatency("healthy", 10.0);

        // Act
        var ranked = _scorer.GetProvidersByHealth();

        // Assert
        ranked.Should().HaveCountGreaterThanOrEqualTo(2);
        // Healthy provider should come first (lower score)
        var healthyIndex = ranked.ToList().IndexOf("healthy");
        var degradedIndex = ranked.ToList().IndexOf("degraded");
        healthyIndex.Should().BeLessThan(degradedIndex);
    }

    [Fact]
    public void IsDegraded_HealthyProvider_ReturnsFalse()
    {
        // Arrange
        _healthMonitor.RegisterConnection("stable", "stable");
        _latencyService.RecordLatency("stable", 10.0);

        // Act
        var isDegraded = _scorer.IsDegraded("stable");

        // Assert
        isDegraded.Should().BeFalse();
    }

    [Fact]
    public void IsDegraded_DisconnectedProvider_ReturnsTrue()
    {
        // Arrange
        _healthMonitor.RegisterConnection("down", "down");
        _healthMonitor.MarkDisconnected("down", "error");

        // Record many errors to push composite above threshold
        for (int i = 0; i < 100; i++)
        {
            _scorer.RecordError("down", "connection_failed");
        }

        // Act
        var isDegraded = _scorer.IsDegraded("down");

        // Assert
        isDegraded.Should().BeTrue();
    }

    [Fact]
    public void RecordError_NullProvider_DoesNotThrow()
    {
        // Act
        var act = () => _scorer.RecordError(null!, "error");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordError_EmptyProvider_DoesNotThrow()
    {
        // Act
        var act = () => _scorer.RecordError("", "error");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordSuccess_NullProvider_DoesNotThrow()
    {
        // Act
        var act = () => _scorer.RecordSuccess(null!);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void OnProviderDegraded_EventIsExposed()
    {
        // Arrange
        bool eventFired = false;
        _scorer.OnProviderDegraded += _ => eventFired = true;

        // Assert - Event delegate was registered without errors
        eventFired.Should().BeFalse();
    }

    [Fact]
    public void EvaluateNow_DegradedProvider_FiresOnProviderDegradedEvent()
    {
        // Arrange
        _healthMonitor.RegisterConnection("alpha-conn", "recovering-provider");
        _healthMonitor.MarkDisconnected("alpha-conn", "simulated outage");

        for (var i = 0; i < 100; i++)
            _scorer.RecordError("recovering-provider", "connection_failed");

        ProviderDegradedEvent? degradedEvent = null;
        _scorer.OnProviderDegraded += e => degradedEvent = e;

        // Act
        _scorer.EvaluateNow();

        // Assert
        _scorer.IsDegraded("recovering-provider").Should().BeTrue();
        degradedEvent.Should().NotBeNull();
        degradedEvent!.Value.ProviderName.Should().Be("recovering-provider");
        degradedEvent.Value.CompositeScore.Should().BeGreaterThanOrEqualTo(_scorer.GetScore("recovering-provider").CompositeScore - 0.01);
    }

    [Fact]
    public void EvaluateNow_ProviderRecovery_FiresOnProviderRecoveredEvent()
    {
        // Arrange – first, make the provider degrade
        _healthMonitor.RegisterConnection("recovery-conn", "recovering-provider");
        _healthMonitor.MarkDisconnected("recovery-conn", "simulated outage");

        for (var i = 0; i < 100; i++)
            _scorer.RecordError("recovering-provider", "connection_failed");

        _scorer.EvaluateNow(); // mark as previously degraded

        // Now simulate recovery: reconnect and clear errors
        _healthMonitor.MarkConnected("recovery-conn");

        // Replace error history with successes to drop score below threshold
        for (var i = 0; i < 300; i++)
            _scorer.RecordSuccess("recovering-provider");

        ProviderRecoveredEvent? recoveredEvent = null;
        _scorer.OnProviderRecovered += e => recoveredEvent = e;

        // Act
        _scorer.EvaluateNow();

        // Assert
        _scorer.IsDegraded("recovering-provider").Should().BeFalse();
        recoveredEvent.Should().NotBeNull("recovery event should fire when provider transitions from degraded to healthy");
        recoveredEvent!.Value.ProviderName.Should().Be("recovering-provider");
    }

    [Fact]
    public void EvaluateNow_AlwaysHealthy_DoesNotFireRecoveredEvent()
    {
        // Arrange – a healthy provider that was never degraded
        _healthMonitor.RegisterConnection("stable-conn", "stable-provider");
        _latencyService.RecordLatency("stable-provider", 10.0);
        _scorer.RecordSuccess("stable-provider");

        bool recoveredFired = false;
        _scorer.OnProviderRecovered += _ => recoveredFired = true;

        // Act – two evaluations; no degradation ever happened
        _scorer.EvaluateNow();
        _scorer.EvaluateNow();

        // Assert
        recoveredFired.Should().BeFalse("recovered event must not fire when provider was never degraded");
    }

    [Fact]
    public void Config_DefaultValues_AreReasonable()
    {
        // Act
        var config = ProviderDegradationConfig.Default;

        // Assert
        config.EvaluationIntervalSeconds.Should().BeGreaterThan(0);
        config.DegradationThreshold.Should().BeInRange(0.0, 1.0);
        config.LatencyThresholdMs.Should().BeGreaterThan(0);
        config.LatencyMaxMs.Should().BeGreaterThan(config.LatencyThresholdMs);
        config.ErrorRateThreshold.Should().BeInRange(0.0, 1.0);
        config.ErrorWindowSeconds.Should().BeGreaterThan(0);
        config.MaxReconnectsPerHour.Should().BeGreaterThan(0);

        // Weights should sum to approximately 1.0
        var totalWeight = config.ConnectionWeight + config.LatencyWeight +
                         config.ErrorRateWeight + config.ReconnectWeight;
        totalWeight.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void CompositeScore_IsClamped_Between0And1()
    {
        // Arrange - Create worst-case scenario
        _healthMonitor.RegisterConnection("worst", "worst");
        _healthMonitor.MarkDisconnected("worst", "total failure");

        for (int i = 0; i < 200; i++)
        {
            _latencyService.RecordLatency("worst", 10000.0);
            _scorer.RecordError("worst", "critical");
        }

        // Act
        var score = _scorer.GetScore("worst");

        // Assert
        score.CompositeScore.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void Constructor_NullHealthMonitor_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ProviderDegradationScorer(null!, _latencyService);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("healthMonitor");
    }

    [Fact]
    public void Constructor_NullLatencyService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ProviderDegradationScorer(_healthMonitor, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("latencyService");
    }

    public void Dispose()
    {
        _scorer.Dispose();
        _healthMonitor.Dispose();
        _latencyService.Dispose();
    }
}
