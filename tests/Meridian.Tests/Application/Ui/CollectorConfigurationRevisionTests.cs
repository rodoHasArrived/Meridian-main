using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Integrity;
using Meridian.Core.Config;
using Meridian.ProcessTestHelper;
using Serilog;

namespace Meridian.Tests.Application.UI;

public sealed class CollectorConfigurationRevisionTests : IDisposable
{
    private const string ThresholdPath = "dataSources.failoverRules.0.failoverThreshold";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-config-revisions-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SeparateCollectorAcknowledgesExactSave_ExitAndRestartCannotReuseEvidence()
    {
        var store = await CreateStoreAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ready = Path.Combine(_root, "collector-ready");
        using var collector = StartCollector(store, ready);
        var errors = collector.StandardError.ReadToEndAsync();
        Guid firstCollector;
        ConfigurationRevision? firstRevision;
        try
        {
            await WaitForAsync(() => File.Exists(ready), collector, errors, deadline.Token);

            var saved = await SaveThresholdAsync(store, 2, deadline.Token);
            saved.RuntimeState.Should().Be(ConfigurationRuntimeState.Saved);
            saved.CollectorAcknowledgement.Should().BeNull();
            await WaitForAsync(() => store.GetChangeStatus().RuntimeState == ConfigurationRuntimeState.AppliedLive,
                collector, errors, deadline.Token);

            var status = new ConfigStore(store.ConfigPath).GetChangeStatus();
            var evidence = status.CollectorAcknowledgement!;
            evidence.Revision.Should().Be(saved.Revision);
            evidence.Revision.ContentHash.Should().Be(Sha256Digest.Compute(await File.ReadAllBytesAsync(store.ConfigPath, deadline.Token)));
            evidence.AppliedAtUtc.Should().BeOnOrAfter(saved.SavedAtUtc!.Value);
            evidence.Entries.Should().ContainSingle().Which.Should().Be(new ConfigurationApplyEntry(ThresholdPath, ConfigurationApplyKind.AppliedLive));
            status.RequiresRestart.Should().BeFalse();
            firstCollector = evidence.CollectorInstanceId;
            firstRevision = evidence.Revision;
        }
        finally
        {
            await StopCollectorAsync(collector);
        }

        var disconnected = store.GetChangeStatus();
        disconnected.Revision.Should().Be(firstRevision);
        disconnected.RuntimeState.Should().Be(ConfigurationRuntimeState.Unknown);
        disconnected.CollectorAcknowledgement.Should().BeNull();

        File.Delete(ready);
        var savedWhileDisconnected = await SaveThresholdAsync(store, 1, deadline.Token);
        savedWhileDisconnected.Revision.Should().NotBe(firstRevision);
        savedWhileDisconnected.LastApply.Should().BeNull();

        using var restarted = StartCollector(store, ready);
        var restartErrors = restarted.StandardError.ReadToEndAsync();
        try
        {
            await WaitForAsync(() => File.Exists(ready), restarted, restartErrors, deadline.Token);
            store.GetChangeStatus().CollectorAcknowledgement.Should().BeNull();

            var savedAfterRestart = await SaveThresholdAsync(store, 2, deadline.Token);
            var revision = savedAfterRestart.Revision!;
            revision.ContentHash.Should().Be(firstRevision!.ContentHash, "the same bytes can be saved again");
            revision.Id.Should().NotBe(firstRevision.Id);
            await WaitForAsync(() => store.GetChangeStatus().CollectorAcknowledgement?.Revision == savedAfterRestart.Revision,
                restarted, restartErrors, deadline.Token);
            store.GetChangeStatus().CollectorAcknowledgement!.CollectorInstanceId.Should().NotBe(firstCollector);
        }
        finally
        {
            await StopCollectorAsync(restarted);
        }
    }

    [Fact]
    public async Task ConcurrentSaveResponsesKeepTheirOwnRevisionAndOutcome()
    {
        var store = await CreateStoreAsync();
        var secondStore = new ConfigStore(store.ConfigPath);
        await using var runtime = new CollectorConfigurationRuntime(
            store, new ThresholdApplier(), store.LoadRequired(), Log.Logger, CancellationToken.None);

        var responses = await Task.WhenAll(SaveThresholdAsync(store, 4), SaveThresholdAsync(secondStore, 5));

        responses.Select(status => status.Revision!.Id).Should().OnlyHaveUniqueItems();
        responses.Select(status => status.Revision!.ContentHash).Should().OnlyHaveUniqueItems();
        responses.Should().OnlyContain(status => status.CollectorAcknowledgement != null
            && status.CollectorAcknowledgement.Revision == status.Revision
            && status.RuntimeState == ConfigurationRuntimeState.AppliedLive);
        var current = store.GetChangeStatus();
        responses.Should().ContainSingle(status => status.Revision == current.Revision);
        current.Revision!.ContentHash.Should().Be(Sha256Digest.Compute(await File.ReadAllBytesAsync(store.ConfigPath)));
    }

    [Theory]
    [InlineData(ConfigurationApplyKind.Deferred, ConfigurationRuntimeState.Deferred)]
    [InlineData(ConfigurationApplyKind.Failed, ConfigurationRuntimeState.Failed)]
    public async Task CollectorReportsDeferredAndFailedSeparately(ConfigurationApplyKind kind, ConfigurationRuntimeState expected)
    {
        var store = await CreateStoreAsync();
        await using var runtime = new CollectorConfigurationRuntime(
            store, new ThresholdApplier(kind), store.LoadRequired(), Log.Logger, CancellationToken.None);

        var status = await SaveThresholdAsync(store, 2);

        status.RuntimeState.Should().Be(expected);
        var acknowledgement = status.CollectorAcknowledgement!;
        status.Revision.Should().Be(acknowledgement.Revision);
        acknowledgement.Entries.Should().ContainSingle().Which.Kind.Should().Be(kind);
        status.PendingChanges.Should().Contain(change => change.Path == ThresholdPath);
        store.LoadRequired().DataSources!.FailoverRules![0].FailoverThreshold.Should().Be(2);
    }

    [Fact]
    public async Task PreparedOrExternallyReplacedConfigurationDoesNotInheritOldEvidence()
    {
        var store = await CreateStoreAsync();
        await using var runtime = new CollectorConfigurationRuntime(
            store, new ThresholdApplier(), store.LoadRequired(), Log.Logger, CancellationToken.None);
        var original = await SaveThresholdAsync(store, 2);
        var changed = WithThreshold(store.LoadRequired(), 1);
        var json = JsonSerializer.Serialize(changed, AppConfigJsonOptions.Write);

        var journal = new ConfigurationRevisionJournal(store.ConfigPath);
        using (await store.AcquireWriteLeaseAsync())
        {
            // Interruption after the config commit and before the matching receipt commit.
            await journal.PrepareAsync(json, [new(ThresholdPath, ConfigurationChangeImpact.RestartHost)], CancellationToken.None);
            await File.WriteAllTextAsync(store.ConfigPath, json);
        }

        store.GetChangeStatus().Revision.Should().BeNull();
        store.GetChangeStatus().CollectorAcknowledgement.Should().BeNull();
        store.GetChangeStatus().RuntimeState.Should().Be(ConfigurationRuntimeState.Unknown);

        var restored = await SaveThresholdAsync(store, 2);
        restored.Revision!.Id.Should().NotBe(original.Revision!.Id);
        // An out-of-band write has no authorization to reuse the original record.
        await File.AppendAllTextAsync(store.ConfigPath, Environment.NewLine);
        store.GetChangeStatus().CollectorAcknowledgement.Should().BeNull();
        store.GetChangeStatus().RuntimeState.Should().Be(ConfigurationRuntimeState.Unknown);
    }

    [Fact]
    public async Task DurableStatusContainsTypedPathsWithoutOperatorControlledMapKeys()
    {
        var store = await CreateStoreAsync();
        const string operatorValue = "synthetic-private-provider-alias";
        await store.UpdateAsync(current => current with
        {
            DataSources = current.DataSources! with
            {
                SymbolMappings = new SymbolMappingsConfig(ProviderResolutionModes: new Dictionary<string, SymbolResolutionMode>
                {
                    [operatorValue] = SymbolResolutionMode.Compare
                })
            }
        });

        var serialized = JsonSerializer.Serialize(store.GetChangeStatus());
        serialized.Should().NotContain(operatorValue);
        (await File.ReadAllTextAsync(store.ConfigPath + ".runtime-revision.json")).Should().NotContain(operatorValue);
    }

    private async Task<ConfigStore> CreateStoreAsync()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        var baseline = new AppConfig(DataSources: new DataSourcesConfig(
            Sources:
            [
                new DataSourceConfig("feed-a", "Feed A", DataSourceKind.Synthetic),
                new DataSourceConfig("feed-b", "Feed B", DataSourceKind.Synthetic)
            ],
            FailoverRules: [new FailoverRuleConfig("equities", "feed-a", ["feed-b"], FailoverThreshold: 3)]));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(baseline, AppConfigJsonOptions.Write));
        return new ConfigStore(path);
    }

    private static async Task<ConfigurationChangeStatus> SaveThresholdAsync(
        ConfigStore store, int threshold, CancellationToken ct = default)
        => (await store.UpdateWithChangeStatusAsync(config => (WithThreshold(config, threshold), threshold), ct)).Status;

    private static AppConfig WithThreshold(AppConfig config, int threshold) => config with
    {
        DataSources = config.DataSources! with
        {
            FailoverRules = [config.DataSources!.FailoverRules![0] with { FailoverThreshold = threshold }]
        }
    };

    private static Process StartCollector(ConfigStore store, string ready)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        var name = typeof(CollectorConfigurationRevisionTests).Assembly.GetName().Name;
        foreach (var argument in new[]
        {
            "exec", "--depsfile", Path.Combine(AppContext.BaseDirectory, name + ".deps.json"),
            "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, name + ".runtimeconfig.json"),
            typeof(ProcessTestHelperMarker).Assembly.Location, "collector-config-watch", store.ConfigPath, ready
        })
            start.ArgumentList.Add(argument);

        return Process.Start(start) ?? throw new InvalidOperationException("Synthetic collector process did not start.");
    }

    private static async Task WaitForAsync(
        Func<bool> predicate, Process collector, Task<string> errors, CancellationToken ct)
    {
        while (!predicate())
        {
            Assert.False(collector.HasExited, collector.HasExited ? await errors : "");
            await Task.Delay(25, ct);
        }
    }

    private static async Task StopCollectorAsync(Process collector)
    {
        if (!collector.HasExited)
            collector.Kill(entireProcessTree: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await collector.WaitForExitAsync(deadline.Token);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class ThresholdApplier(ConfigurationApplyKind kind = ConfigurationApplyKind.AppliedLive) : IConfigurationRuntimeApplier
    {
        public string Name => "test collector";
        public bool Handles(string path) => path == ThresholdPath;

        public Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
            AppConfig previous, AppConfig next, IReadOnlyList<string> changedPaths, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ConfigurationApplyEntry>>(
                changedPaths.Select(path => new ConfigurationApplyEntry(path, kind)).ToArray());
    }
}
