using System.Text.Json;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// PRD-002 concurrent configuration writes, two-process workstation: a <see cref="ConfigServiceBase"/>
/// mutation that returns the host's write lease must wait for a concurrent
/// <see cref="ConfigStore.UpdateAsync{T}"/> instead of persisting a snapshot loaded before the lock.
/// </summary>
[Collection("Sequential")]
public sealed class ConfigServiceBaseLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "config-service-lock", Guid.NewGuid().ToString("N"));

    public ConfigServiceBaseLockTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ShellMutation_WaitsWhileTheHostHoldsTheConfigurationLock()
    {
        var path = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        using var hostEntered = new ManualResetEventSlim();
        using var hostRelease = new ManualResetEventSlim();

        // The host holds the lock inside UpdateAsync; returning the same reference writes nothing, so
        // the two clients' serializers never have to read each other's document here.
        var host = Task.Run(() => new ConfigStore(path).UpdateAsync(cfg =>
        {
            hostEntered.Set();
            hostRelease.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
            return cfg;
        }));
        hostEntered.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

        var shell = new LockedFileConfigService(path);
        var shellEdit = Task.Run(() => shell.AddSymbolAsync(new SymbolConfigDto { Symbol = "SHELL", SubscribeTrades = true }));
        await Task.Delay(200);
        shellEdit.IsCompleted.Should().BeFalse("the shell must wait for the host's lock rather than write a stale snapshot");

        hostRelease.Set();
        await Task.WhenAll(host, shellEdit);

        (await shell.GetConfiguredSymbolsAsync()).Select(symbol => symbol.Symbol).Should().BeEquivalentTo(["SHELL"]);
    }

    [Fact]
    public async Task HostUpdate_WaitsWhileTheShellHoldsTheConfigurationLock()
    {
        var path = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        using var shellEntered = new ManualResetEventSlim();
        using var shellRelease = new ManualResetEventSlim();
        using var hostEntered = new ManualResetEventSlim();
        var shell = new LockedFileConfigService(path);

        var shellEdit = Task.Run(() => shell.BlockingMutateAsync(shellEntered, shellRelease));
        shellEntered.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

        var host = Task.Run(() => new ConfigStore(path).UpdateAsync(cfg =>
        {
            hostEntered.Set();
            return cfg;
        }));
        hostEntered.Wait(TimeSpan.FromMilliseconds(300)).Should().BeFalse("the host must not read the document while the shell's edit is in flight");

        shellRelease.Set();
        await Task.WhenAll(shellEdit, host);
        hostEntered.IsSet.Should().BeTrue();
    }

    [Fact]
    public async Task Mutation_WithoutALease_StillLoadsInsideTheCycle()
    {
        var path = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(path, "{}");
        var service = new LockedFileConfigService(path, useLease: false);

        await service.AddSymbolAsync(new SymbolConfigDto { Symbol = "ONE", SubscribeTrades = true });
        await service.AddSymbolAsync(new SymbolConfigDto { Symbol = "ONE", SubscribeTrades = true });
        await service.RemoveSymbolAsync("ONE");

        (await service.GetConfiguredSymbolsAsync()).Should().BeEmpty();
        service.Saves.Should().Be(2, "an unchanged add is not persisted");
    }

    /// <summary>A file-backed <see cref="ConfigServiceBase"/> mirroring the WPF shell's lease override.</summary>
    private sealed class LockedFileConfigService(string path, bool useLease = true) : ConfigServiceBase
    {
        public int Saves { get; private set; }

        public override string ConfigPath => path;

        /// <summary>Holds the lease inside a mutation until released, so a test can observe contention.</summary>
        public Task BlockingMutateAsync(ManualResetEventSlim entered, ManualResetEventSlim release)
            => MutateConfigAsync(_ =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
            });

        protected override async Task<IDisposable?> AcquireConfigurationWriteLeaseAsync(CancellationToken ct)
            => useLease ? await new ConfigStore(path).AcquireWriteLeaseAsync(ct) : null;

        protected override async Task<AppConfigDto?> LoadConfigCoreAsync(CancellationToken ct = default)
        {
            // The host serializes nullable booleans (e.g. compress: null) that the DTO models as bool;
            // drop null members so this fixture can read a host-written document, as the real
            // shell reader tolerates them through its own validation.
            var json = await File.ReadAllTextAsync(path, ct);
            if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject root)
                return new AppConfigDto();
            foreach (var nullMember in root.Where(static pair => pair.Value is null).Select(static pair => pair.Key).ToList())
                root.Remove(nullMember);
            return root.Deserialize<AppConfigDto>(SharedJsonOptions);
        }

        protected override async Task SaveConfigCoreAsync(AppConfigDto config, CancellationToken ct = default)
        {
            Saves++;
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(config, SharedJsonOptions), ct);
        }

        protected override void LogError(string message, Exception? exception = null)
        {
        }
    }
}
