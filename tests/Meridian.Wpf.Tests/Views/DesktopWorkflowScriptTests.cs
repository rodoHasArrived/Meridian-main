using System.IO;
using System.Text.Json;
using Meridian.Wpf.Models;

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
    public void DesktopScreenshotCatalog_ShouldIncludeEveryRegisteredShellPage()
    {
        var workflowCatalog = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\desktop-workflows.json"));
        using var document = JsonDocument.Parse(workflowCatalog);

        var screenshotWorkflow = document.RootElement
            .GetProperty("workflows")
            .EnumerateArray()
            .Single(workflow =>
                string.Equals(
                    workflow.GetProperty("name").GetString(),
                    "screenshot-catalog",
                    StringComparison.Ordinal));

        var screenshotSteps = screenshotWorkflow
            .GetProperty("steps")
            .EnumerateArray()
            .Select(step => new
            {
                PageTag = step.GetProperty("pageTag").GetString() ?? string.Empty,
                CaptureName = step.GetProperty("captureName").GetString() ?? string.Empty
            })
            .ToArray();

        var registeredPageTags = ShellNavigationCatalog.Pages
            .Select(page => page.PageTag)
            .OrderBy(pageTag => pageTag, StringComparer.Ordinal)
            .ToArray();
        var catalogPageTags = screenshotSteps
            .Select(step => step.PageTag)
            .OrderBy(pageTag => pageTag, StringComparer.Ordinal)
            .ToArray();

        catalogPageTags.Should().Equal(registeredPageTags);

        screenshotSteps
            .GroupBy(step => step.CaptureName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Should()
            .BeEmpty("each registered WPF screen needs a distinct committed screenshot file");
    }

    [Fact]
    public void RunDesktopWorkflowScript_ShouldRestoreAndBuildWithMatchingIsolationArguments()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("$buildIsolationKey = if ($SkipBuild) { '' } else { New-MeridianBuildIsolationKey");
        script.Should().Contain("$desktopRestoreArgs = @(");
        script.Should().Contain("$desktopBuildArgs = @(");
        script.Should().Contain("-AdditionalProperties @(\"Configuration=$resolvedConfiguration\", 'UseSharedCompilation=false')");
        script.Should().Contain("-MaxCpuCount 1");
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
    public void CaptureDesktopScreenshotsScript_ShouldRouteThroughSharedWorkflowRunner()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\capture-desktop-screenshots.ps1"));

        script.Should().Contain("$workflowRunner = Join-Path $PSScriptRoot 'run-desktop-workflow.ps1'");
        script.Should().Contain("$workflowName = if ([string]::IsNullOrWhiteSpace($Profile)) { 'screenshot-catalog' } else { $Profile }");
        script.Should().Contain("'-Workflow', $workflowName");
        script.Should().Contain("'-Profile', $workflowName");
        script.Should().Contain("capture-{0}-{1}");
        script.Should().Contain("'-OutputRoot', $workflowArtifactRoot");
        script.Should().NotContain("'-OutputRoot', 'artifacts/desktop-workflows'");
        script.Should().Contain("'-ScreenshotDirectory', $screenshotDirectory");
        script.Should().Contain("if ($PSBoundParameters.ContainsKey('ProjectPath'))");
        script.Should().Contain("if ($PSBoundParameters.ContainsKey('Configuration'))");
        script.Should().Contain("if ($PSBoundParameters.ContainsKey('Framework'))");
        script.Should().Contain("if ($PSBoundParameters.ContainsKey('ExeName'))");
        script.Should().Contain("if ($SkipBuild)");
        script.Should().Contain("if ($KeepAppOpen)");
        script.Should().Contain("& pwsh -NoProfile @runnerArgs");
        script.Should().Contain("exit $LASTEXITCODE");

        script.Should().NotContain("CommandPaletteInput");
        script.Should().NotContain("function New-ScreenNavigationRegistry");
        script.Should().NotContain("function Invoke-NavigateWithKeyboard");
        script.Should().NotContain("function Invoke-NavigateWithMouse");
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
    public void RunDesktopWorkflowScript_ShouldCaptureWindowUsingPrintWindow()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("function Save-InProcessWindowCapture");
        script.Should().Contain("--screenshot=$resolvedPath");
        script.Should().Contain("function Test-ImageFileHasVisualContent");
        script.Should().Contain("[System.Drawing.Bitmap]::new($stream)");
        script.Should().Contain("In-process desktop screenshot capture failed; falling back to native window capture");
        script.Should().Contain("Desktop screenshot capture remained blank after PrintWindow and screen fallback.");
        script.Should().Contain("MeridianDesktopCaptureNative");
        script.Should().Contain("[MeridianDesktopCaptureNative]::PrintWindow");
        script.Should().NotContain("CopyFromScreen(");
    }

    [Fact]
    public void RunDesktopWorkflowScript_ShouldEnterOperatingContextBeforeWaitingForShellReadiness()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("function Ensure-EnteredOperatingContext");
        script.Should().Contain("EnterWorkstationButton");
        script.Should().Contain("Seed Sample Contexts");
        script.Should().Contain("StartupContinueWithoutCredentialsButton");
        script.Should().Contain("continue without credentials");
        script.Should().Contain("$manifest.run.operatingContextConfirmed = $operatingContextConfirmed");
        script.Should().Contain("Operating context was not confirmed; screenshot workflow cannot continue before shell readiness.");
        script.Should().Contain("Operating context confirmed.");

        var contextIndex = script.IndexOf("Ensure-EnteredOperatingContext -Process $ownedProcess", StringComparison.Ordinal);
        var startupIndex = script.IndexOf("$startupReadiness = Wait-ForStableShellPage", StringComparison.Ordinal);

        contextIndex.Should().BeGreaterThan(0);
        startupIndex.Should().BeGreaterThan(contextIndex);
    }

    [Fact]
    public void RunDesktopWorkflowScript_ShouldUseDevelopmentFixtureEnvironmentForScreenshotCapture()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop-workflow.ps1"));

        script.Should().Contain("DOTNET_ENVIRONMENT', 'Development'");
        script.Should().Contain("ASPNETCORE_ENVIRONMENT', 'Development'");
        script.Should().Contain("MERIDIAN_USE_INMEMORY_GOVERNANCE', 'true'");
        script.Should().Contain("MDC_WPF_SOFTWARE_RENDERING', '1'");
        script.Should().Contain("$originalWorkflowEnv");
    }

    [Fact]
    public void WpfApp_ShouldExposeSoftwareRenderingAutomationOverride()
    {
        var app = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\App.xaml.cs"));

        app.Should().Contain("MDC_WPF_SOFTWARE_RENDERING");
        app.Should().Contain("RenderOptions.ProcessRenderMode");
        app.Should().Contain("RenderMode.SoftwareOnly");
    }

    [Fact]
    public void MainWindow_ShouldExposeInProcessScreenshotCaptureForAutomation()
    {
        var mainWindow = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\MainWindow.xaml.cs"));

        mainWindow.Should().Contain("CaptureMainWindowScreenshotAsync");
        mainWindow.Should().Contain("SaveMainWindowScreenshot");
        mainWindow.Should().Contain("RenderTargetBitmap");
        mainWindow.Should().Contain("PngBitmapEncoder");
        mainWindow.Should().Contain("request.HasScreenshotRequest");
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
        var workflow = File.ReadAllText(GetRepositoryFilePath(@".github\workflows\wpf-dev-validation.yml"));
        var makefile = File.ReadAllText(GetRepositoryFilePath(@"make\desktop.mk"));
        var guide = File.ReadAllText(GetRepositoryFilePath(@"docs\engineering\README.md"));

        script.Should().Contain("[string]$Filter = \"FullyQualifiedName~DesktopWorkflowScriptTests\"");
        script.Should().Contain("[switch]$BuildOnly");
        script.Should().Contain("[switch]$Restore");
        script.Should().Contain("[switch]$AllowConcurrentDotnet");
        script.Should().Contain("New-MeridianBuildIsolationKey -Prefix \"wpf-dev-test\"");
        script.Should().Contain("Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot");
        script.Should().Contain("Get-ActiveRepoDotnetProcess");
        script.Should().Contain("Name = 'dotnet.exe' OR Name = 'MSBuild.exe' OR Name = 'testhost.exe' OR Name = 'csc.exe' OR Name = 'VBCSCompiler.exe'");
        script.Should().Contain("(build|test|restore|msbuild)");
        script.Should().Contain("active-dotnet-processes.log");
        script.Should().Contain("Invoke-MeridianWpfTempProjectCleanup -RepoRoot $repoRoot -WpfProjectPath $wpfProject");
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
        script.Should().Contain("\"--no-dependencies\"");
        script.Should().Contain("\"test\"");
        script.Should().Contain("--no-build");
        script.Should().Contain("wpf-dev-test-validation.json");

        sharedBuildScript.Should().Contain("function Invoke-MeridianLoggedStep");
        sharedBuildScript.Should().Contain("[System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($LogPath))");
        sharedBuildScript.Should().Contain("Stop-MeridianRepoOwnedTestHostProcesses");
        sharedBuildScript.Should().Contain("function Invoke-MeridianStepWithTestHostRetry");
        sharedBuildScript.Should().Contain("function Get-MeridianRepoOwnedBuildProcesses");
        sharedBuildScript.Should().Contain("function Invoke-MeridianWpfTempProjectCleanup");
        sharedBuildScript.Should().Contain("-Filter '*_wpftmp.csproj'");

        workflow.Should().Contain("$devArgs = @{");
        workflow.Should().Contain("Restore = $true");

        makefile.Should().Contain("desktop-test-dev:");
        makefile.Should().Contain("scripts/dev/validate-wpf-dev.ps1");

        guide.Should().Contain("pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1");
        guide.Should().Contain("make desktop-test-dev");
        guide.Should().Contain("-AllowConcurrentDotnet");
        guide.Should().Contain("dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore --no-dependencies /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None -v:minimal");
    }

    [Fact]
    public void DesktopDevBootstrap_ShouldUseSharedDesktopToolingAndCurrentCommands()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\desktop-dev.ps1"));
        var launcher = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\run-desktop.ps1"));

        script.Should().Contain("[string]$Profile = 'desktop-development'");
        script.Should().Contain("[switch]$SkipLaunchSmoke");
        script.Should().Contain(". (Join-Path $PSScriptRoot 'SharedBuild.ps1')");
        script.Should().Contain(". (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')");
        script.Should().Contain("$buildIsolationKey = if ($NoIsolation) { '' } else { New-MeridianBuildIsolationKey -Prefix 'desktop-dev' }");
        script.Should().Contain("Get-MeridianWorkflowProfile -RepoRoot $repoRoot -ProfileName $Profile -ProfileRoot $ProfileRoot");
        script.Should().Contain("Test-MeridianWorkflowProfile -ProfileData $profileEnvelope.data");
        script.Should().Contain("Get-MeridianRepoOwnedBuildProcesses -RepoRoot $repoRoot");
        script.Should().Contain("Invoke-MeridianWpfTempProjectCleanup -RepoRoot $repoRoot -WpfProjectPath $wpfProject");
        script.Should().Contain("Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -EnableFullWpfBuild");
        script.Should().Contain("dotnet', 'build', $wpfProject, '-c', $Configuration, '--no-restore'");
        script.Should().Contain("dotnet', 'build', $wpfTestsProject, '-c', $Configuration, '--no-restore'");
        script.Should().Contain("Launch fixture desktop startup smoke");
        script.Should().Contain("run-desktop.ps1");
        script.Should().Contain("'-LaunchMode', 'Development'");
        script.Should().Contain("'-StartupSmoke'");
        script.Should().Contain("make desktop-test-position-blotter-route");
        script.Should().Contain("pwsh ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup");
        script.Should().Contain("python ./scripts/dev/desktop_screen_blueprint_checklist.py --summary");
        script.Should().Contain("Launch development desktop: pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development");
        script.Should().Contain("Launch fixture desktop:     pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture");
        script.Should().Contain("Build production desktop:   pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly");

        launcher.Should().Contain("[ValidateSet('Development', 'Production')]");
        launcher.Should().Contain("[string]$LaunchMode = 'Development'");
        launcher.Should().Contain("[switch]$BuildOnly");
        launcher.Should().Contain("if ($LaunchMode -eq 'Production') { 'desktop-production' } else { 'desktop-development' }");
        launcher.Should().Contain("$hostConfiguration = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'configuration' -Fallback $desktopConfiguration)");
        launcher.Should().Contain("[switch]$StartupSmoke");
        launcher.Should().Contain("[int]$StartupSmokeTimeoutSec = 45");
        launcher.Should().Contain("$buildIsolationKey = if ($NoBuild) { '' } else { New-MeridianBuildIsolationKey -Prefix 'desktop-run' }");
        launcher.Should().Contain("Get-MeridianRepoOwnedBuildProcesses -RepoRoot $repoRoot");
        launcher.Should().Contain("Invoke-MeridianWpfTempProjectCleanup -RepoRoot $repoRoot -WpfProjectPath $desktopProject");
        launcher.Should().Contain("function Apply-DesktopLaunchMode");
        launcher.Should().Contain("MERIDIAN_USE_INMEMORY_GOVERNANCE' -Value 'true'");
        launcher.Should().Contain("function Assert-ProductionGovernanceConfiguration");
        launcher.Should().Contain("'MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING'");
        launcher.Should().Contain("'MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING'");
        launcher.Should().Contain("Production launch mode cannot run with -Fixture");
        launcher.Should().Contain("Assert-ProductionGovernanceConfiguration");
        launcher.Should().Contain("function Wait-ForDesktopWindow");
        launcher.Should().Contain("function Stop-DesktopProcessAfterSmoke");
        launcher.Should().Contain("function Stop-OwnedDesktopProcessSafely");
        launcher.Should().Contain("$hostShutdownToken = [System.Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))");
        launcher.Should().Contain("MDC_SHUTDOWN_TOKEN = $env:MDC_SHUTDOWN_TOKEN");
        launcher.Should().Contain("DOTNET_ENVIRONMENT = $env:DOTNET_ENVIRONMENT");
        launcher.Should().Contain("ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT");
        launcher.Should().Contain("MERIDIAN_USE_INMEMORY_GOVERNANCE = $env:MERIDIAN_USE_INMEMORY_GOVERNANCE");
        launcher.Should().Contain("$env:MDC_SHUTDOWN_TOKEN = $hostShutdownToken");
        launcher.Should().Contain("http://localhost:$hostPort/api/system/shutdown");
        launcher.Should().Contain("\"X-Meridian-Shutdown-Token\" = $hostShutdownToken");
        launcher.Should().Contain("Local Meridian host stopped gracefully");
        launcher.Should().Contain("-WindowStyle Hidden");
        launcher.Should().Contain("Desktop build completed; skipping host and shell launch because -BuildOnly was supplied.");
        launcher.Should().Contain("Stop-OwnedDesktopProcessSafely");
        launcher.Should().Contain("Write-Step 'Startup smoke'");
        launcher.Should().Contain("Wait-ForDesktopWindow -Process $desktopProcess -TimeoutSec $StartupSmokeTimeoutSec");
        launcher.Should().Contain("Stop-DesktopProcessAfterSmoke -Process $desktopProcess");
        launcher.Should().Contain("Meridian desktop startup smoke completed successfully");

        var developmentProfile = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\workflow-profiles\desktop-development.json"));
        var productionProfile = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\workflow-profiles\desktop-production.json"));
        developmentProfile.Should().Contain("\"configuration\": \"Debug\"");
        developmentProfile.Should().Contain("\"required\": false");
        productionProfile.Should().Contain("\"configuration\": \"Release\"");
        productionProfile.Should().Contain("\"required\": false");

        script.Should().NotContain("src/Meridian.Uwp/Meridian.Uwp.csproj");
        script.Should().NotContain("make build-wpf");
        script.Should().NotContain("make test-desktop-services");
        script.Should().NotContain("make uwp-xaml-diagnose");
    }

    [Fact]
    public void CleanupGeneratedScript_ShouldIncludeStaleWpfTempProjects()
    {
        var script = File.ReadAllText(GetRepositoryFilePath(@"scripts\dev\cleanup-generated.ps1"));

        script.Should().Contain("function Add-WpfTempProjectFiles");
        script.Should().Contain("-Filter '*_wpftmp.csproj'");
        script.Should().Contain("Stale WPF temporary project file");
        script.Should().Contain("Generated files:");
        script.Should().Contain("Deleting generated artifacts...");
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
