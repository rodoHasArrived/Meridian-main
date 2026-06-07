using Meridian.Core.Config;
using Meridian.Application.UI;
using Meridian.Wpf.Features;
using Microsoft.Extensions.Options;

namespace Meridian.Wpf.Tests.Features;

public sealed class FeatureCapabilityGateTests
{
    [Fact]
    public void IsEnabled_ShouldUseDefaultsAndOverrides()
    {
        using var scope = CreateGate(new FeatureCapabilityOptions(new Dictionary<string, bool>
        {
            ["desktop.test.optional"] = false
        }));

        scope.Gate.IsEnabled("desktop.test.required").Should().BeTrue();
        scope.Gate.IsEnabled("desktop.test.optional").Should().BeFalse();
        scope.Gate.IsEnabled("desktop.test.unknown").Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_ShouldPersistOverrideRoundTrip()
    {
        using var scope = CreateGate();

        scope.Gate.SetEnabled("desktop.test.optional", false);

        var persisted = ConfigStore.LoadConfig(scope.ConfigPath);
        persisted.FeatureCapabilities.Should().NotBeNull();
        persisted.FeatureCapabilities!.Overrides.Should()
            .ContainKey("desktop.test.optional")
            .WhoseValue.Should().BeFalse();

        using var reloaded = CreateGate(
            persisted.FeatureCapabilities,
            scope.ConfigPath);

        reloaded.Gate.IsEnabled("desktop.test.optional").Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_ShouldDeliverChangeNotifications()
    {
        using var scope = CreateGate();
        var observer = new RecordingObserver();
        using var subscription = scope.Gate.Changes.Subscribe(observer);

        scope.Gate.SetEnabled("desktop.test.optional", false);

        observer.Events.Should().ContainSingle().Which.Should().Be(
            new CapabilityChangedEvent("desktop.test.optional", false));
    }

    private static GateScope CreateGate(
        FeatureCapabilityOptions? options = null,
        string? configPath = null)
    {
        configPath ??= Path.Combine(Path.GetTempPath(), "meridian-capabilities-" + Guid.NewGuid().ToString("N"), "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, "{}");
        }

        var monitor = new TestOptionsMonitor(options ?? new FeatureCapabilityOptions());
        var store = new ConfigStore(configPath);
        var gate = new FeatureCapabilityGateService(monitor, store, TestCapabilities());
        return new GateScope(configPath, gate);
    }

    private static IReadOnlyList<FeatureCapabilityDescriptor> TestCapabilities() =>
    [
        new("desktop.test.required", "Required capability", "Always-on test capability.", true, true),
        new("desktop.test.optional", "Optional capability", "Toggleable test capability.", true, false)
    ];

    private sealed record GateScope(string ConfigPath, FeatureCapabilityGateService Gate) : IDisposable
    {
        public void Dispose()
        {
            Gate.Dispose();
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class RecordingObserver : IObserver<CapabilityChangedEvent>
    {
        public List<CapabilityChangedEvent> Events { get; } = [];

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(CapabilityChangedEvent value)
        {
            Events.Add(value);
        }
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<FeatureCapabilityOptions>
    {
        public TestOptionsMonitor(FeatureCapabilityOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public FeatureCapabilityOptions CurrentValue { get; private set; }

        public FeatureCapabilityOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<FeatureCapabilityOptions, string?> listener)
            => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
