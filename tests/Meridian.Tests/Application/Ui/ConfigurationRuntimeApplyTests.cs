using System.Text.Json;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Tests.Application.UI;

/// <summary>
/// A save reports what the running host actually did: applied paths stop being pending, deferred
/// paths stay pending as restart-required, and without an attached runtime nothing changes.
/// </summary>
public sealed class ConfigurationRuntimeApplyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-runtime-apply-" + Guid.NewGuid().ToString("N"));

    private static readonly AppConfig Baseline = new(DataSources: new DataSourcesConfig(
        Sources: [new DataSourceConfig("alpaca", "Alpaca", DataSourceKind.Alpaca)],
        FailoverRules: [new FailoverRuleConfig("equities", "alpaca", ["alpaca-backup"])]));

    private async Task<ConfigStore> CreateStore()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Baseline, AppConfigJsonOptions.Write));
        return new ConfigStore(path);
    }

    private static AppConfig TightenThreshold(AppConfig config) => config with
    {
        DataSources = config.DataSources! with
        {
            FailoverRules = [config.DataSources!.FailoverRules![0] with { FailoverThreshold = 1 }]
        }
    };

    [Fact]
    public async Task WithoutAttachedRuntime_DataSourcesEditStillRequiresRestart()
    {
        var store = await CreateStore();

        await store.UpdateAsync(TightenThreshold);

        var status = store.GetChangeStatus();
        status.RequiresRestart.Should().BeTrue();
        status.LastApply.Should().BeNull();
    }

    [Fact]
    public async Task AppliedLivePaths_AreNotPending()
    {
        var store = await CreateStore();
        var applier = new RecordingApplier(ConfigurationApplyKind.AppliedLive);
        using var _ = ConfigurationRuntimeAppliers.Attach(store.ConfigPath, applier);

        await store.UpdateAsync(TightenThreshold);

        var status = store.GetChangeStatus();
        status.RequiresRestart.Should().BeFalse();
        status.PendingChanges.Should().BeEmpty();
        status.Impact.Should().Be(ConfigurationChangeImpact.Live);
        status.LastApply!.Entries.Should().ContainSingle()
            .Which.Kind.Should().Be(ConfigurationApplyKind.AppliedLive);
        applier.Calls.Should().Be(1);
    }

    [Fact]
    public async Task DeferredPaths_StayPendingAsRestart()
    {
        var store = await CreateStore();
        using var _ = ConfigurationRuntimeAppliers.Attach(store.ConfigPath, new RecordingApplier(ConfigurationApplyKind.Deferred));

        await store.UpdateAsync(TightenThreshold);

        var status = store.GetChangeStatus();
        status.RequiresRestart.Should().BeTrue();
        status.LastApply!.AnyDeferred.Should().BeTrue();
    }

    [Fact]
    public async Task ApplierFailure_DoesNotFailTheSaveAndIsReportedAsFailed()
    {
        var store = await CreateStore();
        using var _ = ConfigurationRuntimeAppliers.Attach(store.ConfigPath, new ThrowingApplier());

        await store.UpdateAsync(TightenThreshold);

        store.Load().DataSources!.FailoverRules![0].FailoverThreshold.Should().Be(1, "the save is durable regardless");
        var status = store.GetChangeStatus();
        status.RequiresRestart.Should().BeTrue();
        status.LastApply!.Entries.Should().OnlyContain(entry => entry.Kind == ConfigurationApplyKind.Failed);
    }

    [Fact]
    public async Task DetachedApplier_IsNoLongerCalled()
    {
        var store = await CreateStore();
        var applier = new RecordingApplier(ConfigurationApplyKind.AppliedLive);
        ConfigurationRuntimeAppliers.Attach(store.ConfigPath, applier).Dispose();

        await store.UpdateAsync(TightenThreshold);

        applier.Calls.Should().Be(0);
        store.GetChangeStatus().RequiresRestart.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private sealed class RecordingApplier(ConfigurationApplyKind kind) : IConfigurationRuntimeApplier
    {
        public int Calls { get; private set; }

        public string Name => "recording";

        public bool Handles(string path) => path.StartsWith("dataSources.", StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
            AppConfig previous, AppConfig next, IReadOnlyList<string> changedPaths, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ConfigurationApplyEntry>>(
                changedPaths.Select(path => new ConfigurationApplyEntry(path, kind, "test")).ToArray());
        }
    }

    private sealed class ThrowingApplier : IConfigurationRuntimeApplier
    {
        public string Name => "throwing";

        public bool Handles(string path) => true;

        public Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
            AppConfig previous, AppConfig next, IReadOnlyList<string> changedPaths, CancellationToken ct)
            => throw new InvalidOperationException("runtime unavailable");
    }
}
