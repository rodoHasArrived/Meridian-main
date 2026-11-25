using FluentAssertions;
using Meridian;

namespace Meridian.Tests.Demo;

/// <summary>
/// PRD-018: the demo/quickstart lane serves one verified workstation asset tree — the tracked
/// canonical bundle at <c>src/Meridian.Ui/wwwroot/workstation</c> — regardless of the directory
/// the host is launched from. These tests pin the launch-directory-independent resolution and the
/// self-explanatory remediation for a missing bundle.
/// </summary>
public sealed class DemoWorkstationAssetTreeTests
{
    [Fact]
    public void ResolveWebRoot_FromDirectoryOutsideTheRepository_StillFindsTheCanonicalBundle()
    {
        var outsideLaunchDirectory = Directory.CreateTempSubdirectory("meridian-prd018-launch-").FullName;
        try
        {
            var webRoot = WorkstationAssetTree.ResolveWebRoot(outsideLaunchDirectory);

            webRoot.Should().NotBeNull(
                "a launch directory with no wwwroot must fall back to the published output or the repository checkout");
            File.Exists(Path.Combine(webRoot!, "workstation", "index.html")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(outsideLaunchDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResolveWebRoot_FromTwoDifferentLaunchDirectories_ServesTheSameBundle()
    {
        var first = Directory.CreateTempSubdirectory("meridian-prd018-a-").FullName;
        var second = Directory.CreateTempSubdirectory("meridian-prd018-b-").FullName;
        try
        {
            var firstRoot = WorkstationAssetTree.ResolveWebRoot(first);
            var secondRoot = WorkstationAssetTree.ResolveWebRoot(second);

            firstRoot.Should().NotBeNull();
            firstRoot.Should().Be(secondRoot,
                "the served bundle must not depend on the launch directory");
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void ResolveWebRoot_PrefersABundleCarriedByTheLaunchDirectory()
    {
        // Installed layouts carry wwwroot/workstation beside the executable's working directory;
        // that local tree must win over repository discovery.
        var installRoot = Directory.CreateTempSubdirectory("meridian-prd018-install-").FullName;
        try
        {
            var workstationDir = Path.Combine(installRoot, "wwwroot", "workstation");
            Directory.CreateDirectory(workstationDir);
            File.WriteAllText(Path.Combine(workstationDir, "index.html"), "<html>installed</html>");

            var webRoot = WorkstationAssetTree.ResolveWebRoot(installRoot);

            webRoot.Should().Be(Path.GetFullPath(Path.Combine(installRoot, "wwwroot")));
        }
        finally
        {
            Directory.Delete(installRoot, recursive: true);
        }
    }

    [Fact]
    public void DescribeUnavailable_NamesTheCanonicalTreeAndTheExactRemediationCommand()
    {
        var description = WorkstationAssetTree.DescribeUnavailable();

        description.Should().Contain("src/Meridian.Ui/wwwroot/workstation");
        description.Should().Contain("npm --prefix src/Meridian.Ui/dashboard run build");
    }
}
