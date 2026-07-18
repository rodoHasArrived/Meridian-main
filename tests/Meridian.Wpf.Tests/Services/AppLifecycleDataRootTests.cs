using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Lifecycle;
using Xunit;

namespace Meridian.Wpf.Tests.Services;

public sealed class AppLifecycleDataRootTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-wpf-lifecycle-data-root-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveLifecycleManifestDataRoot_UsesParentInstallManifestForDesktopPayload()
    {
        var desktopRoot = Path.Combine(_root, "desktop");
        var serviceRoot = Path.Combine(_root, "service");
        Directory.CreateDirectory(desktopRoot);
        Directory.CreateDirectory(serviceRoot);
        File.WriteAllText(
            Path.Combine(serviceRoot, "lifecycle-supervisor.json"),
            JsonSerializer.Serialize(
                new LifecycleSupervisorManifestDto { DataRoot = "operator-data" },
                LifecycleContractsJsonContext.Default.LifecycleSupervisorManifestDto));

        var resolved = App.ResolveLifecycleManifestDataRoot(desktopRoot);

        resolved.Should().Be(Path.Combine(_root, "operator-data"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
