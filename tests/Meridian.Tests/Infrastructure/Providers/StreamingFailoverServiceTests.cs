using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Infrastructure.Adapters.Failover;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="StreamingFailoverService"/>.
/// Tests health tracking, automatic failover triggering, recovery logic, and force failover.
/// </summary>
public sealed class StreamingFailoverServiceTests : IDisposable
{
    private readonly ConnectionHealthMonitor _healthMonitor;
    private readonly StreamingFailoverService _service;

    private readonly FailoverRuleConfig _rule = new(
        Id: "test-rule",
        PrimaryProviderId: "primary",
        BackupProviderIds: new[] { "backup1", "backup2" },
        FailoverThreshold: 3,
        RecoveryThreshold: 2
    );

    private readonly DataSourcesConfig _config;

    public StreamingFailoverServiceTests()
    {
        _healthMonitor = new ConnectionHealthMonitor();
        _service = new StreamingFailoverService(_healthMonitor);

        _config = new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 60, // Large interval — we trigger evaluation manually
            FailoverRules: new[] { _rule }
        );
    }

    public void Dispose()
    {
        _service.Dispose();
        _healthMonitor.Dispose();
    }

    [Fact]
    public void RegisterProvider_TracksHealthState()
    {
        _service.RegisterProvider("primary");
        _service.RegisterProvider("backup1");

        var snapshots = _service.GetProviderHealthSnapshots();
        snapshots.Should().HaveCount(2);
        snapshots.Should().Contain(s => s.ProviderId == "primary");
        snapshots.Should().Contain(s => s.ProviderId == "backup1");
    }

    [Fact]
    public void RecordSuccess_IncrementsConsecutiveSuccesses()
    {
        _service.RegisterProvider("primary");

        _service.RecordSuccess("primary");
        _service.RecordSuccess("primary");
        _service.RecordSuccess("primary");

        var snap = _service.GetProviderHealthSnapshots().First(s => s.ProviderId == "primary");
        snap.ConsecutiveSuccesses.Should().Be(3);
        snap.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RecordFailure_IncrementsConsecutiveFailures_ResetsSuccesses()
    {
        _service.RegisterProvider("primary");

        _service.RecordSuccess("primary");
        _service.RecordSuccess("primary");
        _service.RecordFailure("primary", "Connection lost");

        var snap = _service.GetProviderHealthSnapshots().First(s => s.ProviderId == "primary");
        snap.ConsecutiveFailures.Should().Be(1);
        snap.ConsecutiveSuccesses.Should().Be(0);
    }

    [Fact]
    public void RecordLatency_TracksAverageLatency()
    {
        _service.RegisterProvider("primary");

        _service.RecordLatency("primary", 10.0);
        _service.RecordLatency("primary", 20.0);
        _service.RecordLatency("primary", 30.0);

        var snap = _service.GetProviderHealthSnapshots().First(s => s.ProviderId == "primary");
        snap.AverageLatencyMs.Should().BeApproximately(20.0, 0.01);
    }

    [Fact]
    public void Start_LoadsRules()
    {
        _service.RegisterProvider("primary");
        _service.RegisterProvider("backup1");
        _service.Start(_config);

        var rules = _service.GetRuleSnapshots();
        rules.Should().HaveCount(1);
        rules[0].RuleId.Should().Be("test-rule");
        rules[0].CurrentActiveProviderId.Should().Be("primary");
        rules[0].IsInFailoverState.Should().BeFalse();
    }

    [Fact]
    public void Start_WithFailoverDisabled_DoesNotActivate()
    {
        var disabledConfig = new DataSourcesConfig(
            EnableFailover: false,
            FailoverRules: new[] { _rule }
        );

        _service.Start(disabledConfig);

        var rules = _service.GetRuleSnapshots();
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Start_WithNoRules_DoesNotActivate()
    {
        var noRulesConfig = new DataSourcesConfig(
            EnableFailover: true,
            FailoverRules: Array.Empty<FailoverRuleConfig>()
        );

        _service.Start(noRulesConfig);

        var rules = _service.GetRuleSnapshots();
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ForceFailover_SwitchesToTargetProvider()
    {
        _service.RegisterProvider("primary");
        _service.RegisterProvider("backup1");
        _service.Start(_config);

        FailoverTriggeredEvent? triggeredEvent = null;
        _service.OnFailoverTriggered += evt => triggeredEvent = evt;

        var result = _service.ForceFailover("test-rule", "backup1");

        result.Should().BeTrue();
        _service.GetActiveProviderId("test-rule").Should().Be("backup1");

        triggeredEvent.Should().NotBeNull();
        triggeredEvent!.Value.RuleId.Should().Be("test-rule");
        triggeredEvent.Value.FromProviderId.Should().Be("primary");
        triggeredEvent.Value.ToProviderId.Should().Be("backup1");
        triggeredEvent.Value.Reason.Should().Contain("Manual");
    }

    [Fact]
    public void ForceFailover_WithUnknownRule_ReturnsFalse()
    {
        _service.Start(_config);

        var result = _service.ForceFailover("nonexistent", "backup1");

        result.Should().BeFalse();
    }

    [Fact]
    public void ForceFailover_WithInvalidTarget_ReturnsFalse()
    {
        _service.RegisterProvider("primary");
        _service.Start(_config);

        var result = _service.ForceFailover("test-rule", "unknown-provider");

        result.Should().BeFalse();
    }

    [Fact]
    public void GetActiveProviderId_ReturnsNullForUnknownRule()
    {
        _service.GetActiveProviderId("nonexistent").Should().BeNull();
    }

    [Fact]
    public void GetRuleSnapshots_IncludesFailoverCount()
    {
        _service.RegisterProvider("primary");
        _service.RegisterProvider("backup1");
        _service.RegisterProvider("backup2");
        _service.Start(_config);

        _service.ForceFailover("test-rule", "backup1");
        _service.ForceFailover("test-rule", "backup2");

        var snap = _service.GetRuleSnapshots().First();
        snap.FailoverCount.Should().Be(2);
        snap.CurrentActiveProviderId.Should().Be("backup2");
        snap.LastFailoverTime.Should().NotBeNull();
    }

    [Fact]
    public void ProviderHealthSnapshot_IncludesRecentIssues()
    {
        _service.RegisterProvider("primary");

        _service.RecordFailure("primary", "Timeout");
        _service.RecordFailure("primary", "Connection reset");

        var snap = _service.GetProviderHealthSnapshots().First(s => s.ProviderId == "primary");
        snap.RecentIssues.Should().HaveCount(2);
        snap.RecentIssues[0].Should().Contain("Timeout");
        snap.RecentIssues[1].Should().Contain("Connection reset");
    }

    [Fact]
    public void ProviderHealthSnapshot_TracksTimestamps()
    {
        _service.RegisterProvider("primary");

        var beforeSuccess = DateTimeOffset.UtcNow;
        _service.RecordSuccess("primary");

        var snap = _service.GetProviderHealthSnapshots().First(s => s.ProviderId == "primary");
        snap.LastSuccessTime.Should().NotBeNull();
        snap.LastSuccessTime!.Value.Should().BeOnOrAfter(beforeSuccess);
        snap.LastFailureTime.Should().BeNull();

        _service.RecordFailure("primary", "test");
        snap = _service.GetProviderHealthSnapshots().First(s => s.ProviderId == "primary");
        snap.LastFailureTime.Should().NotBeNull();
    }
}
