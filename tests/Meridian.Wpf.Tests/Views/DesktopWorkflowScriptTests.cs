using System.IO;

namespace Meridian.Wpf.Tests.Views;

public sealed class DesktopWorkflowScriptTests
{
    [Fact]
    public void RunDesktopWorkflowScript_ShouldConfirmShellPageBeforeCapture()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("function Get-ShellAutomationState");
        script.Should().Contain("function Wait-ForShellPage");
        script.Should().Contain("function Wait-ForStableShellPage");
        script.Should().Contain("function Send-ForwardedLaunchArgs");
        script.Should().Contain("Forwarded desktop args through single-instance pipe");
        script.Should().Contain("$startupReadiness = Wait-ForStableShellPage");
        script.Should().Contain("Requested page '$ExpectedPageTag' was not confirmed before capture.");
        script.Should().Contain("$pageReadiness = Wait-ForShellPage");
        script.Should().Contain("$stepResult.observedPageTag = $pageReadiness.State.PageTag");
    }

    [Fact]
    public void RunDesktopWorkflowScript_ShouldRestoreAndBuildWithMatchingIsolationArguments()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("$desktopRestoreArgs = @(");
        script.Should().Contain("$desktopBuildArgs = @(");
        script.Should().Contain("-AdditionalProperties @(\"Configuration=$resolvedConfiguration\")");
        script.Should().Contain("& dotnet restore $resolvedProjectPath --verbosity minimal @desktopRestoreArgs");
        script.Should().Contain("& dotnet build $resolvedProjectPath -c $resolvedConfiguration --no-restore --verbosity minimal @desktopBuildArgs");

        var restoreArgsStart = script.IndexOf("$desktopRestoreArgs = @(", StringComparison.Ordinal);
        var buildArgsStart = script.IndexOf("$desktopBuildArgs = @(", StringComparison.Ordinal);
        script.Substring(restoreArgsStart, buildArgsStart - restoreArgsStart).Should().NotContain("-TargetFramework");
        script[buildArgsStart..].Should().Contain("-TargetFramework $resolvedFramework");
    }

    [Fact]
    public void RunDesktopWorkflowScript_ShouldBringMeridianToForegroundBeforeSavingCapture()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        var captureIndex = script.IndexOf("$savedPath = Save-WindowCapture", StringComparison.Ordinal);
        var activationIndex = script.LastIndexOf("Activate-MeridianWindow | Out-Null", StringComparison.Ordinal);

        activationIndex.Should().BeGreaterThan(0);
        captureIndex.Should().BeGreaterThan(activationIndex);
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
