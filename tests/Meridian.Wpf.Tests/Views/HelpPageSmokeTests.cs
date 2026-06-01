using System.IO;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class HelpPageSmokeTests
{
    [Fact]
    public void HelpPage_ShouldUseCanonicalWorkspaceShortcutCopy()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\HelpPage.xaml"));

        xaml.Should().Contain("Go to Strategy Workspace");
        xaml.Should().NotContain("Go to Research Workspace");
    }
}
