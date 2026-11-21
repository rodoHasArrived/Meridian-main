using FluentAssertions;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure.Adapters.Failover;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Live (no-restart) configuration of <see cref="StreamingFailoverService"/>: a quant tightening a
/// failover threshold or disabling a vendor mid-session without interrupting streaming.
/// </summary>
public sealed class StreamingFailoverLiveConfigurationTests : IDisposable
{
    private readonly ConnectionHealthMonitor _healthMonitor = new();
    private readonly StreamingFailoverService _service;
    private readonly IDisposable _handler;

    private static readonly FailoverRuleConfig Rule = new(
        Id: "equities",
        PrimaryProviderId: "alpaca",
        BackupProviderIds: ["polygon", "ibkr"],
        FailoverThreshold: 3,
        RecoveryThreshold: 2);

    public StreamingFailoverLiveConfigurationTests()
    {
        _service = new StreamingFailoverService(_healthMonitor);
        _handler = _service.RegisterTransitionHandler(Rule.Id, static transition => transition.TryComplete());
        foreach (var id in new[] { "alpaca", "polygon", "ibkr" })
            _service.RegisterProvider(id);
        _service.Start(Config(Rule));
    }

    public void Dispose()
    {
        _handler.Dispose();
        _service.Dispose();
        _healthMonitor.Dispose();
    }

    private static DataSourcesConfig Config(params FailoverRuleConfig[] rules)
        => new(EnableFailover: true, HealthCheckIntervalSeconds: 60, FailoverRules: rules);

    [Fact]
    public void ApplyConfiguration_TightenedThreshold_TakesEffectOnNextFailureWithoutRestart()
    {
        _service.RecordFailure("alpaca", "timeout");
        _service.GetActiveProviderId(Rule.Id).Should().Be("alpaca", "one failure is below the original threshold of 3");

        var outcomes = _service.ApplyConfiguration(Config(Rule with { FailoverThreshold = 1 }));

        outcomes.Should().ContainSingle().Which.Should().Be(new FailoverRuleApplyOutcome(Rule.Id, true, null));
        _service.GetActiveProviderId(Rule.Id).Should().Be("polygon",
            "the tightened threshold is evaluated immediately against the recorded failure");
        _service.GetRuleSnapshots().Single().FailoverThreshold.Should().Be(1);
    }

    [Fact]
    public void ApplyConfiguration_KeepsActiveProviderAndFailoverState()
    {
        _service.ForceFailover(Rule.Id, "ibkr").Should().BeTrue();

        _service.ApplyConfiguration(Config(Rule with { RecoveryThreshold = 10, MaxLatencyMs = 250 }));

        var snapshot = _service.GetRuleSnapshots().Single();
        snapshot.CurrentActiveProviderId.Should().Be("ibkr");
        snapshot.IsInFailoverState.Should().BeTrue();
        snapshot.RecoveryThreshold.Should().Be(10);
        snapshot.FailoverCount.Should().Be(1);
    }

    [Fact]
    public void ApplyConfiguration_UnchangedRule_ReportsNothing()
    {
        var copy = Rule with { BackupProviderIds = ["polygon", "ibkr"] };

        _service.ApplyConfiguration(Config(copy)).Should().BeEmpty();
    }

    [Fact]
    public void ApplyConfiguration_BackupWithoutLiveClient_IsDeferredAndRuleUnchanged()
    {
        var outcomes = _service.ApplyConfiguration(Config(Rule with { BackupProviderIds = ["polygon", "tiingo"] }));

        var outcome = outcomes.Should().ContainSingle().Subject;
        outcome.Applied.Should().BeFalse();
        outcome.Reason.Should().Contain("tiingo");
        _service.GetRuleSnapshots().Single().BackupProviderIds.Should().Equal("polygon", "ibkr");
    }

    [Fact]
    public void ApplyConfiguration_NewRuleAndRemovingDrivingRule_AreDeferred()
    {
        var added = new FailoverRuleConfig("futures", "ibkr", ["polygon"]);

        var outcomes = _service.ApplyConfiguration(Config(added));

        outcomes.Should().HaveCount(2);
        outcomes.Should().OnlyContain(outcome => !outcome.Applied);
        _service.GetRuleSnapshots().Select(rule => rule.RuleId).Should().Equal(Rule.Id);
    }

    [Fact]
    public void ApplyConfiguration_HealthCheckInterval_ChangesLive()
    {
        _service.EvaluationIntervalSeconds.Should().Be(60);

        _service.ApplyConfiguration(Config(Rule) with { HealthCheckIntervalSeconds = 5 });

        _service.EvaluationIntervalSeconds.Should().Be(5);
    }

    [Fact]
    public void SetProviderEnabled_DisabledBackupIsSkippedOnFailover()
    {
        _service.SetProviderEnabled("polygon", enabled: false).Should().BeTrue();
        _service.SetProviderEnabled("polygon", enabled: false).Should().BeFalse("the state did not change");

        _service.ApplyConfiguration(Config(Rule with { FailoverThreshold = 1 }));
        _service.RecordFailure("alpaca", "disconnect");

        _service.GetActiveProviderId(Rule.Id).Should().Be("ibkr");
    }

    [Fact]
    public async Task MoveOffDisabledProviderAsync_HandsOffToNextEnabledProvider()
    {
        (await _service.MoveOffDisabledProviderAsync(Rule.Id)).Should().BeNull("alpaca is still enabled");

        _service.SetProviderEnabled("alpaca", enabled: false);
        _service.SetProviderEnabled("polygon", enabled: false);

        (await _service.MoveOffDisabledProviderAsync(Rule.Id)).Should().BeTrue();
        _service.GetActiveProviderId(Rule.Id).Should().Be("ibkr");
    }

    [Fact]
    public async Task MoveOffDisabledProviderAsync_NoEnabledBackup_ReturnsFalseAndKeepsStreaming()
    {
        foreach (var id in new[] { "alpaca", "polygon", "ibkr" })
            _service.SetProviderEnabled(id, enabled: false);

        (await _service.MoveOffDisabledProviderAsync(Rule.Id)).Should().BeFalse();
        _service.GetActiveProviderId(Rule.Id).Should().Be("alpaca");
    }

    [Fact]
    public void DisabledPrimary_IsNotAutoRecovered()
    {
        _service.ForceFailover(Rule.Id, "polygon").Should().BeTrue();
        _service.SetProviderEnabled("alpaca", enabled: false);

        for (var i = 0; i < 5; i++)
            _service.RecordSuccess("alpaca");

        _service.GetActiveProviderId(Rule.Id).Should().Be("polygon");
    }
}
