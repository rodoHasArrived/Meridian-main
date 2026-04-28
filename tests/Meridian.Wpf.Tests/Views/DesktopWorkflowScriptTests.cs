using System.IO;

namespace Meridian.Wpf.Tests.Views;

public sealed class DesktopWorkflowScriptTests
{
    [Fact]
    public void RunDesktopWorkflowScript_ShouldConfirmShellPageBeforeCapture()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("function Get-MeridianWindowFromProcess");
        script.Should().Contain("return [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)");
        script.Should().Contain("Find-MeridianWindow -Process $Process");
        script.Should().NotContain("$root.FindAll(");
        script.Should().Contain("function Find-DescendantByAutomationId");
        script.Should().Contain("Find-DescendantByAutomationId -Window $Window -AutomationId 'ShellAutomationState'");
        script.Should().Contain("Find-DescendantByAutomationId -Window $Window -AutomationId 'PageTitleText'");
        script.Should().Contain("Transient UI Automation timeouts are expected while WPF pages load");
        script.Should().Contain("function Get-ShellAutomationState");
        script.Should().Contain("function Resolve-WorkflowPageTag");
        script.Should().Contain("'ResearchShell' { return 'StrategyShell' }");
        script.Should().Contain("'DataOperationsShell' { return 'DataShell' }");
        script.Should().Contain("'GovernanceShell' { return 'AccountingShell' }");
        script.Should().Contain("$expectedCanonicalPageTag = Resolve-WorkflowPageTag -PageTag $ExpectedPageTag");
        script.Should().Contain("function Wait-ForShellPage");
        script.Should().Contain("function Wait-ForStableShellPage");
        script.Should().Contain("function Send-ForwardedLaunchArgs");
        script.Should().Contain("Forwarded desktop args through single-instance pipe");
        script.Should().Contain("$startupReadiness = Wait-ForStableShellPage");
        script.Should().Contain("Requested page '$ExpectedPageTag' (canonical '$expectedCanonicalPageTag') was not confirmed before capture.");
        script.Should().Contain("$expectedPageTag = Resolve-WorkflowPageTag -PageTag $pageTag");
        script.Should().Contain("expectedPageTag = $expectedPageTag");
        script.Should().Contain("$pageReadiness = Wait-ForShellPage");
        script.Should().Contain("$stepResult.observedPageTag = $pageReadiness.State.PageTag");
    }

    [Fact]
    public void DesktopWorkflowCatalog_ShouldUseCanonicalWorkspacePageTags()
    {
        var workflowCatalog = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\desktop-workflows.json"));

        workflowCatalog.Should().Contain("\"pageTag\": \"StrategyShell\"");
        workflowCatalog.Should().Contain("\"pageTag\": \"DataShell\"");
        workflowCatalog.Should().Contain("\"pageTag\": \"AccountingShell\"");

        workflowCatalog.Should().NotContain("\"pageTag\": \"ResearchShell\"");
        workflowCatalog.Should().NotContain("\"pageTag\": \"DataOperationsShell\"");
        workflowCatalog.Should().NotContain("\"pageTag\": \"GovernanceShell\"");
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

    [Fact]
    public void RunDesktopWorkflowScript_ShouldEnterOperatingContextBeforeWaitingForShellReadiness()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("function Ensure-EnteredOperatingContext");
        script.Should().Contain("EnterWorkstationButton");
        script.Should().Contain("Seed Sample Contexts");
        script.Should().Contain("$manifest.run.operatingContextConfirmed = $operatingContextConfirmed");
        script.Should().Contain("Operating context was not confirmed; screenshot workflow cannot continue before shell readiness.");
        script.Should().Contain("Operating context confirmed.");

        var contextIndex = script.IndexOf("Ensure-EnteredOperatingContext -Process $ownedProcess", StringComparison.Ordinal);
        var startupIndex = script.IndexOf("$startupReadiness = Wait-ForStableShellPage", StringComparison.Ordinal);

        contextIndex.Should().BeGreaterThan(0);
        startupIndex.Should().BeGreaterThan(contextIndex);
    }

    [Fact]
    public void RunDesktopWorkflowScript_ShouldPruneWorkflowArtifactsBeforeCreatingRunDirectory()
    {
        var sharedBuildScript = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\SharedBuild.ps1"));
        var workflowScript = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        sharedBuildScript.Should().Contain("function Invoke-MeridianWorkflowArtifactRetention");
        sharedBuildScript.Should().Contain("[int]$MaxAgeDays = 14");
        sharedBuildScript.Should().Contain("[int]$RetainLatest = 10");

        workflowScript.Should().Contain("Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot");

        var retentionIndex = workflowScript.IndexOf("Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot", StringComparison.Ordinal);
        var runDirectoryIndex = workflowScript.IndexOf("$runDirectory = Join-Path $resolvedOutputRoot", StringComparison.Ordinal);

        retentionIndex.Should().BeGreaterThan(0);
        runDirectoryIndex.Should().BeGreaterThan(retentionIndex);
    }

    [Fact]
    public void FocusedValidationScripts_ShouldPruneWorkflowArtifactsBeforeCreatingSummaryDirectory()
    {
        foreach (var relativePath in new[]
                 {
                     @"scripts\dev\validate-position-blotter-route.ps1",
                     @"scripts\dev\validate-operator-inbox-route.ps1"
                 })
        {
            var script = File.ReadAllText(GetRepositoryFilePath(relativePath));

            script.Should().Contain("$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot");
            script.Should().Contain("Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot");
            script.Should().Contain("$summaryDir = Join-Path $resolvedOutputRoot $runStamp");

            var retentionIndex = script.IndexOf("Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot", StringComparison.Ordinal);
            var summaryIndex = script.IndexOf("$summaryDir = Join-Path $resolvedOutputRoot $runStamp", StringComparison.Ordinal);

            retentionIndex.Should().BeGreaterThan(0);
            summaryIndex.Should().BeGreaterThan(retentionIndex);
        }
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
