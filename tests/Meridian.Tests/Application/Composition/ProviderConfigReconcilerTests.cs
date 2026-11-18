using FluentAssertions;
using Meridian.Application.Composition.Startup;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure.Adapters.Failover;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// The provider reconciler decides, per saved setting, whether a running collector applied it,
/// reconfigured a provider, ignored it, or needs a restart, and says so exactly.
/// </summary>
public sealed class ProviderConfigReconcilerTests : IDisposable
{
    private readonly ConnectionHealthMonitor _healthMonitor = new();
    private readonly StreamingFailoverService _failover;
    private readonly IDisposable _handler;
    private readonly ProviderConfigReconciler _reconciler;
    private readonly AppConfig _startup;

    private static readonly FailoverRuleConfig Rule = new("equities", "alpaca", ["polygon", "ib"], FailoverThreshold: 3);

    public ProviderConfigReconcilerTests()
    {
        _failover = new StreamingFailoverService(_healthMonitor);
        _handler = _failover.RegisterTransitionHandler(Rule.Id, static transition => transition.TryComplete());
        foreach (var id in new[] { "alpaca", "polygon", "ib" })
            _failover.RegisterProvider(id);

        _startup = new AppConfig(DataSources: new DataSourcesConfig(
            Sources:
            [
                new DataSourceConfig("alpaca", "Alpaca", DataSourceKind.Alpaca),
                new DataSourceConfig("polygon", "Polygon", DataSourceKind.Polygon),
                new DataSourceConfig("ib", "IB", DataSourceKind.IB),
                new DataSourceConfig("tiingo", "Tiingo", DataSourceKind.Alpaca, Enabled: false)
            ],
            HealthCheckIntervalSeconds: 60,
            FailoverRules: [Rule]));
        _failover.Start(_startup.DataSources!);
        _reconciler = new ProviderConfigReconciler(_failover, ["alpaca", "polygon", "ib"], _startup, Log.Logger,
            handOffTimeout: TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _reconciler.Dispose();
        _handler.Dispose();
        _failover.Dispose();
        _healthMonitor.Dispose();
    }

    private AppConfig With(Func<DataSourcesConfig, DataSourcesConfig> edit)
        => _startup with { DataSources = edit(_startup.DataSources!) };

    private static DataSourcesConfig SetSource(DataSourcesConfig config, int index, Func<DataSourceConfig, DataSourceConfig> edit)
    {
        var sources = config.Sources!.ToArray();
        sources[index] = edit(sources[index]);
        return config with { Sources = sources };
    }

    private async Task<IReadOnlyList<ConfigurationApplyEntry>> SaveAsync(AppConfig next)
    {
        var paths = ConfigurationChangeTracker.ChangedPaths(_startup, next);
        return await _reconciler.ApplyAsync(_startup, next, paths, CancellationToken.None);
    }

    [Fact]
    public async Task ThresholdEdit_IsAppliedLive()
    {
        var entries = await SaveAsync(With(ds => ds with { FailoverRules = [Rule with { FailoverThreshold = 1 }] }));

        entries.Should().ContainSingle(entry => string.Equals(entry.Path,
                "DataSources.FailoverRules.0.FailoverThreshold", StringComparison.OrdinalIgnoreCase))
            .Which.Kind.Should().Be(ConfigurationApplyKind.AppliedLive);
        _failover.GetRuleSnapshots().Single().FailoverThreshold.Should().Be(1);
    }

    [Fact]
    public async Task CredentialEdit_IsDeferredToRestart()
    {
        var next = With(ds => SetSource(ds, 1, s => s with { Polygon = new PolygonOptions(ApiKey: "rotated") }));

        var entries = await SaveAsync(next);

        entries.Should().NotBeEmpty();
        entries.Should().OnlyContain(entry => entry.Kind == ConfigurationApplyKind.Deferred);
        entries.Should().OnlyContain(entry => !entry.Detail!.Contains("rotated"), "details never echo values");
    }

    [Fact]
    public async Task PriorityEdit_IsReportedAsNoRuntimeEffect()
    {
        var entries = await SaveAsync(With(ds => SetSource(ds, 1, s => s with { Priority = 1 })));

        entries.Should().ContainSingle().Which.Kind.Should().Be(ConfigurationApplyKind.NoRuntimeEffect);
    }

    [Fact]
    public async Task DisablingActiveProvider_MovesStreamingToNextEnabledBackup()
    {
        var entries = await SaveAsync(With(ds => SetSource(ds, 0, s => s with { Enabled = false })));

        var entry = entries.Should().ContainSingle().Subject;
        entry.Kind.Should().Be(ConfigurationApplyKind.ProviderReconfigured);
        entry.Detail.Should().Contain("polygon");
        _failover.GetActiveProviderId(Rule.Id).Should().Be("polygon");
        _failover.IsProviderEnabled("alpaca").Should().BeFalse();
    }

    [Fact]
    public async Task EnablingSourceWithoutLiveClient_IsDeferred()
    {
        var entries = await SaveAsync(With(ds => SetSource(ds, 3, s => s with { Enabled = true })));

        entries.Should().ContainSingle().Which.Kind.Should().Be(ConfigurationApplyKind.Deferred);
    }

    [Fact]
    public async Task RuleReferencingUnknownSource_IsDeferredAndNothingApplied()
    {
        var entries = await SaveAsync(With(ds => ds with
        {
            FailoverRules = [Rule with { FailoverThreshold = 1, BackupProviderIds = ["polygon", "nasdaq"] }]
        }));

        entries.Should().OnlyContain(entry => entry.Kind == ConfigurationApplyKind.Deferred);
        _failover.GetRuleSnapshots().Single().FailoverThreshold.Should().Be(3);
    }

    [Fact]
    public async Task SameChangeFromSaveAndFileWatcher_IsAppliedOnce()
    {
        var next = With(ds => ds with { FailoverRules = [Rule with { FailoverThreshold = 2 }] });
        await SaveAsync(next);

        var fromWatcher = await _reconciler.ApplyAsync(next, CancellationToken.None);
        fromWatcher.Should().BeEmpty();

        var resaved = await SaveAsync(next);
        resaved.Should().ContainSingle().Which.Detail.Should().Be("Already applied to the running host.");
    }

    [Fact]
    public async Task ApplyAsync_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _reconciler.ApplyAsync(With(ds => ds with { HealthCheckIntervalSeconds = 5 }), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _failover.EvaluationIntervalSeconds.Should().Be(60);
    }
}
