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

        script.Should().Contain("$buildIsolationKey = if ($SkipBuild) { '' } else { New-MeridianBuildIsolationKey");
        script.Should().Contain("$desktopRestoreArgs = @(");
        script.Should().Contain("$desktopBuildArgs = @(");
        script.Should().Contain("-AdditionalProperties @(\"Configuration=$resolvedConfiguration\")");
        script.Should().Contain("& dotnet restore $resolvedProjectPath --verbosity minimal @desktopRestoreArgs");
        script.Should().Contain("& dotnet build $resolvedProjectPath -c $resolvedConfiguration --no-restore --verbosity minimal @desktopBuildArgs");

        var isolationIndex = script.IndexOf("$buildIsolationKey = if ($SkipBuild)", StringComparison.Ordinal);
        var exePathIndex = script.IndexOf("$exePath = Get-MeridianProjectBinaryPath", StringComparison.Ordinal);
        var restoreArgsStart = script.IndexOf("$desktopRestoreArgs = @(", StringComparison.Ordinal);
        var buildArgsStart = script.IndexOf("$desktopBuildArgs = @(", StringComparison.Ordinal);
        isolationIndex.Should().BeGreaterThan(0);
        exePathIndex.Should().BeGreaterThan(isolationIndex);
        script.Substring(restoreArgsStart, buildArgsStart - restoreArgsStart).Should().NotContain("-TargetFramework");
        script[buildArgsStart..].Should().Contain("-TargetFramework $resolvedFramework");
    }

    [Fact]
    public void SharedBuildScript_ShouldResolveAbsoluteProjectBinaryPathWithoutRepoPrefix()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\SharedBuild.ps1"));

        script.Should().Contain("function Get-MeridianProjectBinaryPath");
        script.Should().Contain("$projectDirectory = Split-Path -Parent $ProjectPath");
        script.Should().Contain("[System.IO.Path]::IsPathRooted($projectDirectory)");
        script.Should().Contain("Join-Path $projectDirectory \"bin/$Configuration/$Framework\"");
        script.Should().Contain("Join-Path $RepoRoot (Join-Path $projectDirectory \"bin/$Configuration/$Framework\")");
        script.Should().Contain("return [System.IO.Path]::GetFullPath((Join-Path $projectOutputDirectory $BinaryName))");
        script.Should().NotContain("Join-Path $RepoRoot (Join-Path $projectDirectory \"bin/$Configuration/$Framework/$BinaryName\")");
    }

    [Fact]
    public void RunDesktopWorkflowScript_ShouldImportCheckpointHelpersBeforeWorkflowExecution()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain(". (Join-Path $PSScriptRoot 'SharedCheckpoint.ps1')");
        script.Should().Contain("$checkpoint = Initialize-MeridianCheckpoint");
        script.Should().Contain("Test-MeridianCheckpointStepShouldRun -Context $checkpoint");
        script.Should().Contain("Start-MeridianCheckpointStep -Context $checkpoint");
        script.Should().Contain("Complete-MeridianCheckpointStep -Context $checkpoint");
        script.Should().Contain("Fail-MeridianCheckpointStep -Context $checkpoint");
        script.Should().Contain(". (Join-Path $PSScriptRoot 'shared/retry.ps1')");
        script.Should().Contain("Invoke-MeridianRetry");
        script.Should().Contain("Test-MeridianDictionaryContainsKey -Dictionary $checkpoint.Data.metadata -Key 'runDirectory'");
        script.Should().Contain("Test-MeridianDictionaryContainsKey -Dictionary $StageData -Key 'outputs'");
        script.Should().Contain("Test-MeridianDictionaryContainsKey -Dictionary $outputs -Key 'requiredFiles'");
        script.Should().NotContain("$checkpoint.Data.metadata.ContainsKey('runDirectory')");
        script.Should().NotContain("$StageData.ContainsKey('outputs')");
        script.Should().NotContain("$outputs.ContainsKey('requiredFiles')");

        var importIndex = script.IndexOf(". (Join-Path $PSScriptRoot 'SharedCheckpoint.ps1')", StringComparison.Ordinal);
        var initializeIndex = script.IndexOf("$checkpoint = Initialize-MeridianCheckpoint", StringComparison.Ordinal);

        importIndex.Should().BeGreaterThan(0);
        initializeIndex.Should().BeGreaterThan(importIndex);
    }

    [Fact]
    public void CaptureDesktopScreenshotsScript_ShouldImportRetryHelperBeforeCapture()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\capture-desktop-screenshots.ps1"));

        script.Should().Contain(". (Join-Path $PSScriptRoot 'shared/retry.ps1')");
        script.Should().Contain("Invoke-MeridianRetry");

        var importIndex = script.IndexOf(". (Join-Path $PSScriptRoot 'shared/retry.ps1')", StringComparison.Ordinal);
        var retryIndex = script.IndexOf("Invoke-MeridianRetry", StringComparison.Ordinal);

        importIndex.Should().BeGreaterThan(0);
        retryIndex.Should().BeGreaterThan(importIndex);
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
    public void SharedWorkflowProfilesScript_ShouldNotAssignPowerShellHostVariable()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\SharedWorkflowProfiles.ps1"));

        script.Should().Contain("$hostProfile = Get-MeridianWorkflowProfileValue -Table $ProfileData -Key 'host'");
        script.Should().NotContain("$host =");
    }

    [Fact]
    public void WindowsDesktopBuildWorkflow_ShouldEnableFullWpfBuildTestAndPublish()
    {
        var workflow = File.ReadAllText(GetRepositoryFilePath(@".github\workflows\windows-desktop-build.yml"));

        workflow.Should().Contain("dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj");
        workflow.Should().Contain("dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj");
        workflow.Should().Contain("dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj");
        workflow.Should().Contain("dotnet restore src/Meridian.Wpf/Meridian.Wpf.csproj");
        workflow.Should().Contain("dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj");
        workflow.Should().Contain("/p:EnableWindowsTargeting=true");
        workflow.Should().Contain("/p:EnableFullWpfBuild=true");
        workflow.Should().Contain("/p:WindowsPackageType=None");
        workflow.Should().Contain("/p:PublishReadyToRun=false");
        workflow.Should().Contain("artifacts/publish/desktop-smoke/Meridian.Desktop.exe");
    }

    [Fact]
    public void WorkflowReadme_ShouldDocumentWindowsDesktopBuildWorkflow()
    {
        var readme = File.ReadAllText(GetRepositoryFilePath(@".github\workflows\README.md"));

        readme.Should().Contain("| Windows Desktop Build | `windows-desktop-build.yml` |");
        readme.Should().Contain("dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true");
        readme.Should().Contain("dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None");
        readme.Should().Contain("dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter \"Category!=Integration&FullyQualifiedName!~Integration\" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true");
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

    [Fact]
    public void WpfDevelopmentTestScript_ShouldUseSerializedBuildAndFocusedTestDefaults()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\validate-wpf-dev.ps1"));
        var sharedBuildScript = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\SharedBuild.ps1"));
        var makefile = File.ReadAllText(GetRepositoryFilePath(@"make\desktop.mk"));
        var guide = File.ReadAllText(GetRepositoryFilePath(@"docs\development\desktop-testing-guide.md"));

        script.Should().Contain("[string]$Filter = \"FullyQualifiedName~DesktopWorkflowScriptTests\"");
        script.Should().Contain("[switch]$BuildOnly");
        script.Should().Contain("[switch]$Restore");
        script.Should().Contain("[switch]$AllowConcurrentDotnet");
        script.Should().Contain("New-MeridianBuildIsolationKey -Prefix \"wpf-dev-test\"");
        script.Should().Contain("Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot");
        script.Should().Contain("Get-ActiveRepoDotnetProcess");
        script.Should().Contain("active-dotnet-processes.log");
        script.Should().Contain("/m:1");
        script.Should().Contain("/nr:false");
        script.Should().Contain("/p:BuildInParallel=false");
        script.Should().Contain("/p:UseSharedCompilation=false");
        script.Should().Contain("/p:EnableWindowsTargeting=true");
        script.Should().Contain("/p:EnableFullWpfBuild=true");
        script.Should().Contain("/p:WindowsPackageType=None");
        script.Should().Contain("dotnet\",");
        script.Should().Contain("\"build\"");
        script.Should().Contain("$wpfProject");
        script.Should().Contain("$wpfTestsProject");
        script.Should().Contain("\"test\"");
        script.Should().Contain("--no-build");
        script.Should().Contain("wpf-dev-test-validation.json");

        sharedBuildScript.Should().Contain("function Invoke-MeridianLoggedStep");
        sharedBuildScript.Should().Contain("[System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($LogPath))");
        sharedBuildScript.Should().Contain("Stop-MeridianRepoOwnedTestHostProcesses");
        sharedBuildScript.Should().Contain("function Invoke-MeridianStepWithTestHostRetry");

        makefile.Should().Contain("desktop-test-dev:");
        makefile.Should().Contain("scripts/dev/validate-wpf-dev.ps1");

        guide.Should().Contain("pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1");
        guide.Should().Contain("make desktop-test-dev");
        guide.Should().Contain("-AllowConcurrentDotnet");
        guide.Should().Contain("dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None -v:minimal");
    }

    [Fact]
    public void DesktopDevBootstrap_ShouldUseSharedDesktopToolingAndCurrentCommands()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\desktop-dev.ps1"));
        var launcher = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop.ps1"));

        script.Should().Contain("[string]$Profile = 'debug-startup'");
        script.Should().Contain("[switch]$SkipLaunchSmoke");
        script.Should().Contain(". (Join-Path $PSScriptRoot 'SharedBuild.ps1')");
        script.Should().Contain(". (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')");
        script.Should().Contain("$buildIsolationKey = if ($NoIsolation) { '' } else { New-MeridianBuildIsolationKey -Prefix 'desktop-dev' }");
        script.Should().Contain("Get-MeridianWorkflowProfile -RepoRoot $repoRoot -ProfileName $Profile -ProfileRoot $ProfileRoot");
        script.Should().Contain("Test-MeridianWorkflowProfile -ProfileData $profileEnvelope.data");
        script.Should().Contain("Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -EnableFullWpfBuild");
        script.Should().Contain("dotnet', 'build', $wpfProject, '-c', $Configuration, '--no-restore'");
        script.Should().Contain("dotnet', 'build', $wpfTestsProject, '-c', $Configuration, '--no-restore'");
        script.Should().Contain("Launch fixture desktop startup smoke");
        script.Should().Contain("run-desktop.ps1");
        script.Should().Contain("'-StartupSmoke'");
        script.Should().Contain("make desktop-test-position-blotter-route");
        script.Should().Contain("pwsh ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup");
        script.Should().Contain("python ./scripts/dev/desktop_screen_blueprint_checklist.py --summary");
        script.Should().Contain("Launch full fixture desktop: pwsh ./scripts/dev/run-desktop.ps1 -Fixture");

        launcher.Should().Contain("[switch]$StartupSmoke");
        launcher.Should().Contain("[int]$StartupSmokeTimeoutSec = 45");
        launcher.Should().Contain("function Wait-ForDesktopWindow");
        launcher.Should().Contain("function Stop-DesktopProcessAfterSmoke");
        launcher.Should().Contain("Write-Step 'Startup smoke'");
        launcher.Should().Contain("Wait-ForDesktopWindow -Process $desktopProcess -TimeoutSec $StartupSmokeTimeoutSec");
        launcher.Should().Contain("Stop-DesktopProcessAfterSmoke -Process $desktopProcess");
        launcher.Should().Contain("Meridian desktop startup smoke completed successfully");

        script.Should().NotContain("src/Meridian.Uwp/Meridian.Uwp.csproj");
        script.Should().NotContain("make build-wpf");
        script.Should().NotContain("make test-desktop-services");
        script.Should().NotContain("make uwp-xaml-diagnose");
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
