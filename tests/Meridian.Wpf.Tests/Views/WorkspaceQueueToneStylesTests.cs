using System.IO;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceQueueToneStylesTests
{
    [Fact]
    public void ThemeSurfacesSource_ShouldDefineToneAwareWorkspaceQueueStyles()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Styles\ThemeSurfaces.xaml"));

        xaml.Should().Contain("x:Key=\"WorkspaceToneInspectorCardStyle\"");
        xaml.Should().Contain("x:Key=\"WorkspaceToneQueueCardStyle\"");
        xaml.Should().Contain("x:Key=\"WorkspaceToneBadgeStyle\"");
        xaml.Should().Contain("x:Key=\"WorkspaceToneBadgeTextStyle\"");
        xaml.Should().Contain("Binding=\"{Binding Tone}\" Value=\"Info\"");
        xaml.Should().Contain("Binding=\"{Binding Tone}\" Value=\"Success\"");
        xaml.Should().Contain("Binding=\"{Binding Tone}\" Value=\"Warning\"");
        xaml.Should().Contain("Binding=\"{Binding Tone}\" Value=\"Danger\"");
    }

    [Fact]
    public void WorkspaceShellSources_ShouldUseToneAwareQueueAndRecentCardStyles()
    {
        var dataOperationsXaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\DataOperationsWorkspaceShellPage.xaml"));
        var governanceXaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\GovernanceWorkspaceShellPage.xaml"));

        dataOperationsXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneQueueCardStyle}\"");
        dataOperationsXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneBadgeStyle}\"");
        dataOperationsXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneInspectorCardStyle}\"");
        dataOperationsXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneBadgeTextStyle}\"");

        governanceXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneQueueCardStyle}\"");
        governanceXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneBadgeStyle}\"");
        governanceXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneInspectorCardStyle}\"");
        governanceXaml.Should().Contain("Style=\"{StaticResource WorkspaceToneBadgeTextStyle}\"");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
