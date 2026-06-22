using FluentAssertions;

namespace Meridian.Tests.Wpf;

public sealed class WpfAccountingFeatureModuleTests
{
    [Fact]
    public void AccountingFeatureModule_ShouldRegisterSharedMigrationRunArtifactStore()
    {
        var source = ReadRepoFile("src", "Meridian.Wpf", "Features", "Accounting", "AccountingFeatureModule.cs");

        source.Should().Contain("IAccountingMigrationRunArtifactStore");
        source.Should().Contain("FileAccountingMigrationRunArtifactStore");
        source.Should().Contain("migration-run-artifacts.json");
        source.Should().Contain("AccountingProductionReadinessService");
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
