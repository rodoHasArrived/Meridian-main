using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Demo;

/// <summary>
/// Guards that <c>--reset-demo</c> can only ever delete the dedicated, sentinel-marked demo root and
/// never a real data root.
/// </summary>
public sealed class DemoWorkspaceGuardTests
{
    [Fact]
    public void EnsureIsolatedDemoRoot_AcceptsDedicatedChildOfDataRoot()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(EnsureIsolatedDemoRoot_AcceptsDedicatedChildOfDataRoot));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var demoRoot = DemoWorkspaceLayout.ResolveDemoRoot(baseRoot);

        var validated = DemoWorkspaceLayout.EnsureIsolatedDemoRoot(demoRoot, baseRoot);

        Path.GetFileName(validated).Should().Be(DemoWorkspaceLayout.DemoWorkspaceFolderName);
    }

    [Fact]
    public void EnsureIsolatedDemoRoot_RejectsTheDataRootItself()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(EnsureIsolatedDemoRoot_RejectsTheDataRootItself));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");

        Action act = () => DemoWorkspaceLayout.EnsureIsolatedDemoRoot(baseRoot, baseRoot);

        act.Should().Throw<DemoWorkspaceIsolationException>();
    }

    [Fact]
    public void EnsureIsolatedDemoRoot_RejectsArbitraryNonDemoDirectory()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(EnsureIsolatedDemoRoot_RejectsArbitraryNonDemoDirectory));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var notDemo = Path.Combine(baseRoot, "ledger");

        Action act = () => DemoWorkspaceLayout.EnsureIsolatedDemoRoot(notDemo, baseRoot);

        act.Should().Throw<DemoWorkspaceIsolationException>();
    }

    [Fact]
    public async Task ResetAsync_RefusesToDeleteADemoNamedDirectoryWithoutTheSentinel()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ResetAsync_RefusesToDeleteADemoNamedDirectoryWithoutTheSentinel));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");

        // Real operator data that must survive.
        var realFile = Path.Combine(baseRoot, "ledger", "important.json");
        Directory.CreateDirectory(Path.GetDirectoryName(realFile)!);
        await File.WriteAllTextAsync(realFile, "{\"real\":true}");

        // A directory that happens to be named like the demo root but that Meridian did not seed
        // (no sentinel marker) — the guard must refuse to delete it.
        var demoRoot = DemoWorkspaceLayout.ResolveDemoRoot(baseRoot);
        Directory.CreateDirectory(demoRoot);
        await File.WriteAllTextAsync(Path.Combine(demoRoot, "user-file.txt"), "not a demo workspace");

        var seeder = new DemoWorkspaceSeeder(baseRoot);
        Func<Task> act = () => seeder.ResetAsync();

        await act.Should().ThrowAsync<DemoWorkspaceIsolationException>();
        File.Exists(realFile).Should().BeTrue();
        Directory.Exists(demoRoot).Should().BeTrue();
    }

    [Fact]
    public async Task ResetAsync_DeletesOnlyTheSeededDemoRoot()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ResetAsync_DeletesOnlyTheSeededDemoRoot));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");

        var realFile = Path.Combine(baseRoot, "ledger", "important.json");
        Directory.CreateDirectory(Path.GetDirectoryName(realFile)!);
        await File.WriteAllTextAsync(realFile, "{\"real\":true}");

        var seeder = new DemoWorkspaceSeeder(baseRoot);
        await seeder.SeedAsync();
        Directory.Exists(seeder.DemoRoot).Should().BeTrue();

        var report = await seeder.ResetAsync();

        report.Deleted.Should().BeTrue();
        Directory.Exists(seeder.DemoRoot).Should().BeFalse();
        File.Exists(realFile).Should().BeTrue();
    }

    [Fact]
    public async Task ResetAsync_OnMissingDemoRoot_IsANoOp()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ResetAsync_OnMissingDemoRoot_IsANoOp));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        var report = await seeder.ResetAsync();

        report.Deleted.Should().BeFalse();
    }
}
