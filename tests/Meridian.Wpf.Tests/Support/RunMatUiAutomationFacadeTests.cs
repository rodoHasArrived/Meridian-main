using System.IO;
using FluentAssertions;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Support;

public sealed class RunMatUiAutomationFacadeTests
{
    [Fact]
    public void ResolveRepoRoot_ShouldHonorExplicitOverride()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "meridian-runmat-root-override");

        var resolvedRoot = RunMatUiAutomationFacade.ResolveRepoRoot(
            explicitRepoRoot: tempRoot,
            baseDirectory: Path.Combine(tempRoot, "ignored"));

        resolvedRoot.Should().Be(Path.GetFullPath(tempRoot));
    }

    [Fact]
    public void ResolveRepoRoot_ShouldFindRepositoryRootFromBaseDirectory()
    {
        var resolvedRoot = RunMatUiAutomationFacade.ResolveRepoRoot(baseDirectory: AppContext.BaseDirectory);

        File.Exists(Path.Combine(resolvedRoot, "Meridian.sln")).Should().BeTrue();
    }
}
