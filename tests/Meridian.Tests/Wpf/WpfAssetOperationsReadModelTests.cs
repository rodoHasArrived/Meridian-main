using FluentAssertions;

namespace Meridian.Tests.Wpf;

public sealed class WpfAssetOperationsReadModelTests
{
    [Fact]
    public void PortfolioCockpit_ShouldAdvertiseSharedAssetOperationsDetailRoute()
    {
        var source = ReadRepoFile("src", "Meridian.Wpf", "ViewModels", "WorkspaceShellViewModelBase.cs");

        source.Should().Contain("AssetOperationsRoute = UiApiRoutes.WorkstationAssetOperations");
        source.Should().Contain("AssetOperationsDetailDto.Subject");
        source.Should().Contain("AssetOperationsDetailDto.ProjectedCashFlows");
        source.Should().Contain("AssetOperationsDetailDto.ReconciliationResults");
        source.Should().Contain("AssetOperationsDetailDto.LedgerProjections");
        source.Should().Contain("Portfolio asset operations detail decision");
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(pathParts.Prepend(root).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Wpf")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
