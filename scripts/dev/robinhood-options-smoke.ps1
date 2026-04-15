#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$ProjectPath = "src/Meridian.Wpf/Meridian.Wpf.csproj",
    [string]$Configuration = "Release",
    [string]$Framework = "net9.0-windows10.0.19041.0",
    [string]$ExecutablePath = "",
    [string]$OutputDirectory = "artifacts/desktop-workflows/robinhood-options-smoke",
    [string]$SeedWorkspacePath = "scripts/dev/fixtures/robinhood-options-smoke.seed.json",
    [switch]$SkipBuild,
    [switch]$KeepAppOpen,
    [bool]$FixtureMode = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))

function Resolve-RepoPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $RepoRoot
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = (Get-Command -Name "pwsh" -ErrorAction SilentlyContinue)?.Source
    if ([string]::IsNullOrWhiteSpace($pwsh)) {
        throw "This smoke harness requires PowerShell 7 or newer (`pwsh`)."
    }

    $argList = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $PSCommandPath,
        "-ProjectPath", $ProjectPath,
        "-Configuration", $Configuration,
        "-Framework", $Framework,
        "-ExecutablePath", $ExecutablePath,
        "-OutputDirectory", $OutputDirectory,
        "-SeedWorkspacePath", $SeedWorkspacePath,
        "-FixtureMode", $FixtureMode.ToString()
    )

    if ($SkipBuild.IsPresent) {
        $argList += "-SkipBuild"
    }

    if ($KeepAppOpen.IsPresent) {
        $argList += "-KeepAppOpen"
    }

    & $pwsh @argList
    exit $LASTEXITCODE
}

if (-not ($IsWindows -or $env:OS -eq "Windows_NT")) {
    throw "Desktop smoke automation requires Windows."
}

Set-Location $RepoRoot

$ResolvedProjectPath = Resolve-RepoPath $ProjectPath
$ResolvedOutputDirectory = Resolve-RepoPath $OutputDirectory
$ResolvedSeedWorkspacePath = Resolve-RepoPath $SeedWorkspacePath
$ResolvedExecutablePath = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $projectDirectory = Split-Path -Parent $ResolvedProjectPath
    [System.IO.Path]::GetFullPath((Join-Path $projectDirectory "bin/$Configuration/$Framework/Meridian.Desktop.exe"))
}
else {
    Resolve-RepoPath $ExecutablePath
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class MeridianSmokeNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
}
"@

Add-Type -ReferencedAssemblies @([System.Text.Json.JsonSerializer].Assembly.Location) @"
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
#nullable enable

public static class MeridianSmokeJson
{
    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public static string SeedWorkspaceJson(
        string baseJson,
        string workspaceId,
        string pageTag,
        string pageTitle,
        string fundProfileId,
        string operatingContextKey,
        string savedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(baseJson))
        {
            throw new InvalidOperationException("Base workspace JSON was empty.");
        }

        JsonObject root = JsonNode.Parse(baseJson) as JsonObject
            ?? throw new InvalidOperationException("Workspace JSON root must be an object.");

        JsonObject session = BuildSession(root["lastSession"], workspaceId, pageTag, pageTitle, savedAtUtc);

        root["activeWorkspaceId"] = workspaceId;
        root["lastSession"] = session.DeepClone();
        root["lastSelectedFundProfileId"] = operatingContextKey;

        JsonObject sessionsByFundProfileId = EnsureObject(root, "sessionsByFundProfileId");
        sessionsByFundProfileId[operatingContextKey] = session.DeepClone();
        sessionsByFundProfileId[fundProfileId] = session.DeepClone();

        JsonArray? workspaces = root["workspaces"] as JsonArray;
        if (workspaces is not null)
        {
            foreach (JsonNode? workspaceNode in workspaces)
            {
                JsonObject? workspace = workspaceNode as JsonObject;
                if (workspace is null)
                {
                    continue;
                }

                if (!string.Equals((string?)workspace["id"], workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                workspace["lastActivePageTag"] = pageTag;
                workspace["recentPageTags"] = new JsonArray(pageTag);
                workspace["sessionSnapshot"] = session.DeepClone();
                break;
            }
        }

        return root.ToJsonString(WriteOptions);
    }

    private static JsonObject BuildSession(
        JsonNode? lastSessionNode,
        string workspaceId,
        string pageTag,
        string pageTitle,
        string savedAtUtc)
    {
        JsonObject session = lastSessionNode is JsonObject existingSession
            ? (JsonObject)existingSession.DeepClone()
            : new JsonObject();

        session["activePageTag"] = pageTag;
        session["activeWorkspaceId"] = workspaceId;
        session["openPages"] = new JsonArray(CreatePage(pageTag, pageTitle));
        session["recentPages"] = new JsonArray(pageTag);
        session["widgetLayout"] = EnsureObject(session, "widgetLayout");
        session["activeFilters"] = EnsureObject(session, "activeFilters");
        session["workspaceContext"] = EnsureObject(session, "workspaceContext");
        session["savedLayoutPresets"] = EnsureArray(session, "savedLayoutPresets");
        session["savedAt"] = savedAtUtc;

        return session;
    }

    private static JsonObject CreatePage(string pageTag, string pageTitle)
    {
        return new JsonObject
        {
            ["pageTag"] = pageTag,
            ["title"] = pageTitle,
            ["isDefault"] = true,
            ["scrollPosition"] = 0,
            ["pageState"] = new JsonObject()
        };
    }

    private static JsonObject EnsureObject(JsonObject owner, string propertyName)
    {
        if (owner[propertyName] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = new JsonObject();
        owner[propertyName] = created;
        return created;
    }

    private static JsonArray EnsureArray(JsonObject owner, string propertyName)
    {
        if (owner[propertyName] is JsonArray existing)
        {
            return existing;
        }

        JsonArray created = new JsonArray();
        owner[propertyName] = created;
        return created;
    }
}
"@

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[smoke $timestamp] $Message"
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}
function Save-JsonHashtable {
    param(
        [string]$Path,
        [object]$Value
    )

    $json = $Value | ConvertTo-Json -Depth 100
    Write-Utf8File -Path $Path -Content $json
}

function Stop-MeridianProcesses {
    $existing = Get-Process -Name "Meridian.Desktop" -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        return
    }

    foreach ($process in @($existing)) {
        try {
            if ($process.MainWindowHandle -ne 0) {
                $null = $process.CloseMainWindow()
                Start-Sleep -Milliseconds 700
            }
        }
        catch {
        }

        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }

    Start-Sleep -Milliseconds 500
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [int]$TimeoutSeconds = 20,
        [int]$IntervalMilliseconds = 250,
        [string]$FailureMessage = "Timed out waiting for condition."
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $result = & $Condition
        if ($result) {
            return $result
        }

        Start-Sleep -Milliseconds $IntervalMilliseconds
    } while ((Get-Date) -lt $deadline)

    throw $FailureMessage
}

function Invoke-DesktopBuild {
    param(
        [string]$ProjectPath,
        [string]$Configuration,
        [string]$Framework
    )

    Write-Log "Building desktop project $ProjectPath ($Configuration, $Framework)."
    & dotnet build $ProjectPath -c $Configuration -p:TargetFramework=$Framework /p:EnableFullWpfBuild=true -nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $ProjectPath"
    }
}

function Get-WindowAutomationRoot {
    param([System.Diagnostics.Process]$Process)

    return Wait-Until -TimeoutSeconds 30 -FailureMessage "Timed out waiting for Meridian main window." -Condition {
        try {
            $Process.Refresh()
            if ($Process.HasExited) {
                throw "Meridian.Desktop exited before exposing a main window."
            }

            if ($Process.MainWindowHandle -eq 0) {
                return $null
            }

            [MeridianSmokeNative]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
            return [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
        }
        catch {
            return $null
        }
    }
}

function Find-ElementByExactName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ElementByAutomationId {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)

    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-FirstElementByNames {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $element = Find-ElementByExactName -Root $Root -Name $name
        if ($null -ne $element) {
            return $element
        }
    }

    return $null
}

function Find-FirstElementByAutomationIds {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string[]]$AutomationIds
    )

    foreach ($automationId in $AutomationIds) {
        $element = Find-ElementByAutomationId -Root $Root -AutomationId $automationId
        if ($null -ne $element) {
            return $element
        }
    }

    return $null
}

function Find-FirstElementByPartialNames {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string[]]$Patterns
    )

    $allElements = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)

    foreach ($element in $allElements) {
        try {
            $name = $element.Current.Name
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            foreach ($pattern in $Patterns) {
                if ($name.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    return $element
                }
            }
        }
        catch {
        }
    }

    return $null
}

function Get-ElementNameSnapshot {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [int]$MaxCount = 80
    )

    $names = New-Object System.Collections.Generic.List[string]
    $allElements = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)

    foreach ($element in $allElements) {
        try {
            $name = $element.Current.Name
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            $trimmed = $name.Trim()
            if (-not $names.Contains($trimmed)) {
                $names.Add($trimmed)
                if ($names.Count -ge $MaxCount) {
                    break
                }
            }
        }
        catch {
        }
    }

    return @($names)
}

function Write-UiSnapshot {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Path
    )

    $names = Get-ElementNameSnapshot -Root $Root
    Write-Utf8File -Path $Path -Content ($names -join [Environment]::NewLine)
}

function Wait-ForElementByNames {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string[]]$Names,
        [int]$TimeoutSeconds = 20,
        [string]$FailureMessage = "Timed out waiting for a target UI element."
    )

    return Wait-Until -TimeoutSeconds $TimeoutSeconds -FailureMessage $FailureMessage -Condition {
        return Find-FirstElementByNames -Root $Root -Names $Names
    }
}
function Get-ParentElement {
    param([System.Windows.Automation.AutomationElement]$Element)
    return [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($Element)
}

function Invoke-OrClickElement {
    param([System.Windows.Automation.AutomationElement]$Element)

    $current = $Element
    while ($null -ne $current) {
        $invokePattern = $null
        if ($current.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokePattern)) {
            $invokePattern.Invoke()
            return
        }

        $selectionItemPattern = $null
        if ($current.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selectionItemPattern)) {
            $selectionItemPattern.Select()
            return
        }

        [System.Windows.Point]$clickPoint = New-Object System.Windows.Point -ArgumentList 0, 0
        if ($current.TryGetClickablePoint([ref]$clickPoint)) {
            [MeridianSmokeNative]::SetCursorPos([int]$clickPoint.X, [int]$clickPoint.Y) | Out-Null
            Start-Sleep -Milliseconds 120
            [MeridianSmokeNative]::mouse_event([MeridianSmokeNative]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 60
            [MeridianSmokeNative]::mouse_event([MeridianSmokeNative]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
            return
        }

        $bounding = $current.Current.BoundingRectangle
        if ($bounding.Width -gt 1 -and $bounding.Height -gt 1) {
            $centerX = [int]($bounding.Left + ($bounding.Width / 2))
            $centerY = [int]($bounding.Top + ($bounding.Height / 2))
            [MeridianSmokeNative]::SetCursorPos($centerX, $centerY) | Out-Null
            Start-Sleep -Milliseconds 120
            [MeridianSmokeNative]::mouse_event([MeridianSmokeNative]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 60
            [MeridianSmokeNative]::mouse_event([MeridianSmokeNative]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
            return
        }

        $current = Get-ParentElement -Element $current
    }

    throw "Unable to click or invoke the requested UI element."
}

function Send-WindowKeys {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Keys
    )

    if ($Process.MainWindowHandle -eq 0) {
        throw "Process does not have a main window handle."
    }

    [MeridianSmokeNative]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds 150
}

function Set-ValuePatternText {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Text
    )

    $valuePattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
        $valuePattern.SetValue($Text)
        return
    }

    throw "Element does not support ValuePattern."
}

function Get-FirstEditElement {
    param([System.Windows.Automation.AutomationElement]$Root)

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)

    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Save-WindowScreenshot {
    param(
        [System.IntPtr]$WindowHandle,
        [string]$Path
    )

    [MeridianSmokeNative]::SetForegroundWindow($WindowHandle) | Out-Null
    Start-Sleep -Milliseconds 200

    $rect = New-Object MeridianSmokeNative+RECT
    if (-not [MeridianSmokeNative]::GetWindowRect($WindowHandle, [ref]$rect)) {
        throw "Unable to capture window bounds."
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "Window bounds were empty."
    }

    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-SeededWorkspaceJson {
    param(
        [string]$BaseWorkspaceJson,
        [string]$WorkspaceId,
        [string]$PageTag,
        [string]$PageTitle,
        [string]$FundProfileId,
        [string]$OperatingContextKey
    )

    return [MeridianSmokeJson]::SeedWorkspaceJson(
        $BaseWorkspaceJson,
        $WorkspaceId,
        $PageTag,
        $PageTitle,
        $FundProfileId,
        $OperatingContextKey,
        (Get-Date).ToUniversalTime().ToString("o"))
}

function Save-OperatingContextState {
    param(
        [string]$Path,
        [string]$OperatingContextKey
    )

    $state = [ordered]@{
        LastSelectedOperatingContextKey = $OperatingContextKey
        WindowMode                      = 0
        CurrentLayoutPresetId           = $null
    }

    Save-JsonHashtable -Path $Path -Value $state
}

function Invoke-ForwardedLaunch {
    param(
        [string]$ExecutablePath,
        [string[]]$Arguments
    )

    $process = Start-Process `
        -FilePath $ExecutablePath `
        -ArgumentList $Arguments `
        -WorkingDirectory (Split-Path -Parent $ExecutablePath) `
        -PassThru `
        -WindowStyle Hidden

    $null = $process.WaitForExit(10000)
    if (-not $process.HasExited) {
        Write-Log "Secondary launcher stayed open longer than expected for args: $($Arguments -join ' ')"
        return
    }

    if ($process.ExitCode -ne 0) {
        Write-Log "Secondary launcher returned exit code $($process.ExitCode) for args: $($Arguments -join ' ')"
    }
}

function Invoke-EnterWorkstation {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [System.Diagnostics.Process]$Process
    )

    $enterButton = $null
    try {
        $enterButton = Wait-Until -TimeoutSeconds 8 -FailureMessage "Enter workstation control did not become available." -Condition {
            $byAutomationId = Find-FirstElementByAutomationIds -Root $Root -AutomationIds @("EnterWorkstationButton")
            if ($null -ne $byAutomationId) {
                return $byAutomationId
            }

            return Find-FirstElementByNames -Root $Root -Names @("Enter Workstation", "Enter Fund")
        }
    }
    catch {
        $enterButton = $null
    }

    if ($null -ne $enterButton) {
        Invoke-OrClickElement -Element $enterButton
        return
    }

    Send-WindowKeys -Process $Process -Keys "{ENTER}"
}

function Get-WorkspaceShellMarker {
    param([string]$WorkspaceId)

    switch ($WorkspaceId) {
        "trading" { return "Trading Workspace" }
        "data-operations" { return "Data Operations Workspace" }
        "governance" { return "Governance Workspace" }
        default { return "Research Workspace" }
    }
}

function Get-WorkspaceTileNames {
    param([string]$WorkspaceId)

    switch ($WorkspaceId) {
        "trading" { return @("Trading") }
        "data-operations" { return @("Data Ops", "Data Operations") }
        "governance" { return @("Governance") }
        default { return @("Research") }
    }
}
function Wait-ForShellReady {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [System.Diagnostics.Process]$Process,
        [string[]]$PageMarkers,
        [int]$TimeoutSeconds = 45
    )

    $selectionMarkers = @("Operating Context Selection", "Fund Profile Selection", "Choose Fund Profile")
    $shellMarkers = @("Research Workspace", "Trading Workspace", "Data Operations Workspace", "Governance Workspace")
    $initialMarkers = $selectionMarkers + $shellMarkers + $PageMarkers

    $state = Wait-Until -TimeoutSeconds $TimeoutSeconds -FailureMessage "Timed out waiting for Meridian startup." -Condition {
        $failure = Find-FirstElementByPartialNames -Root $Root -Patterns @("Unable to open", "Object reference not set to an instance")
        if ($null -ne $failure) {
            throw "Desktop surfaced a page-load error during startup: $($failure.Current.Name)"
        }

        $match = Find-FirstElementByNames -Root $Root -Names $initialMarkers
        if ($null -ne $match) {
            return $match
        }

        return $null
    }

    if ($state.Current.Name -in $selectionMarkers) {
        Write-Log "Operating context selection detected. Entering the preselected workstation context."
        Invoke-EnterWorkstation -Root $Root -Process $Process

        Wait-Until -TimeoutSeconds $TimeoutSeconds -FailureMessage "Timed out waiting for the workstation shell after context selection." -Condition {
            $failure = Find-FirstElementByPartialNames -Root $Root -Patterns @("Unable to open", "Object reference not set to an instance")
            if ($null -ne $failure) {
                throw "Desktop surfaced a page-load error after context selection: $($failure.Current.Name)"
            }

            return Find-FirstElementByNames -Root $Root -Names ($shellMarkers + $PageMarkers)
        } | Out-Null
    }
}

function Wait-ForCasePage {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [hashtable]$Case,
        [int]$TimeoutSeconds
    )

    return Wait-Until -TimeoutSeconds $TimeoutSeconds -FailureMessage "Timed out waiting for the $($Case.Name) page to load." -Condition {
        $failure = Find-FirstElementByPartialNames -Root $Root -Patterns @("Unable to open", "Object reference not set to an instance")
        if ($null -ne $failure) {
            throw "Desktop surfaced a page-load error while loading $($Case.PageTag): $($failure.Current.Name)"
        }

        return Find-FirstElementByNames -Root $Root -Names $Case.ReadyMarkers
    }
}

function Try-ActivateWorkspaceShell {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$WorkspaceId
    )

    $tile = Find-FirstElementByNames -Root $Root -Names (Get-WorkspaceTileNames -WorkspaceId $WorkspaceId)
    if ($null -eq $tile) {
        return $false
    }

    Invoke-OrClickElement -Element $tile

    $shellMarker = Get-WorkspaceShellMarker -WorkspaceId $WorkspaceId
    Wait-ForElementByNames -Root $Root -Names @($shellMarker) -TimeoutSeconds 10 -FailureMessage "Timed out waiting for the $WorkspaceId shell after selecting the workspace tile." | Out-Null
    return $true
}

function Invoke-SmokeCase {
    param(
        [string]$BaseWorkspaceJson,
        [string]$WorkspaceDataPath,
        [string]$OperatingContextPath,
        [string]$FundProfileId,
        [string]$OperatingContextKey,
        [hashtable]$Case,
        [string]$ExecutablePath,
        [string]$OutputDirectory,
        [bool]$FixtureMode
    )

    Write-Log "Preparing session for $($Case.Name) ($($Case.PageTag))."
    $seededJson = Get-SeededWorkspaceJson `
        -BaseWorkspaceJson $BaseWorkspaceJson `
        -WorkspaceId $Case.WorkspaceId `
        -PageTag $Case.PageTag `
        -PageTitle $Case.PageTitle `
        -FundProfileId $FundProfileId `
        -OperatingContextKey $OperatingContextKey

    $seedPath = Join-Path $OutputDirectory ("seeded-" + $Case.Name + ".json")
    Write-Utf8File -Path $seedPath -Content $seededJson
    Write-Utf8File -Path $WorkspaceDataPath -Content $seededJson
    Save-OperatingContextState -Path $OperatingContextPath -OperatingContextKey $OperatingContextKey

    Write-Log "Launching desktop app for $($Case.Name)."
    $startProcessArgs = @{
        FilePath         = $ExecutablePath
        PassThru         = $true
        WorkingDirectory = (Split-Path -Parent $ExecutablePath)
    }

    if ($FixtureMode) {
        $startProcessArgs["Environment"] = @{ MDC_FIXTURE_MODE = "1" }
    }

    $process = Start-Process @startProcessArgs
    $root = $null

    try {
        $root = Get-WindowAutomationRoot -Process $process
        $startupTimeoutSeconds = if ($Case.ContainsKey("StartupTimeoutSeconds")) { [int]$Case.StartupTimeoutSeconds } else { 45 }
        Wait-ForShellReady -Root $root -Process $process -PageMarkers $Case.ReadyMarkers -TimeoutSeconds $startupTimeoutSeconds

        $pageReady = $null
        try {
            $pageReady = Wait-ForCasePage -Root $root -Case $Case -TimeoutSeconds 6
        }
        catch {
            $pageReady = $null
        }

        if ($null -eq $pageReady) {
            Write-Log "$($Case.Name) was not visible immediately after startup. Activating workspace shell and forwarding page navigation."
            $null = Try-ActivateWorkspaceShell -Root $root -WorkspaceId $Case.WorkspaceId
            Invoke-ForwardedLaunch -ExecutablePath $ExecutablePath -Arguments @("--page=$($Case.PageTag)")
            Start-Sleep -Milliseconds 1200
            $pageReady = Wait-ForCasePage -Root $root -Case $Case -TimeoutSeconds 25
        }

        Write-Log "$($Case.Name) page is visible via '$($pageReady.Current.Name)'."

        if ($Case.ContainsKey("Action") -and $Case["Action"] -is [scriptblock]) {
            & $Case["Action"] $root $process
        }

        $screenshotPath = Join-Path $OutputDirectory $Case.ScreenshotName
        Save-WindowScreenshot -WindowHandle $process.MainWindowHandle -Path $screenshotPath
        Write-Log "Saved screenshot to $screenshotPath"

        $markersFound = @()
        foreach ($marker in $Case.ExpectMarkers) {
            $found = Find-ElementByExactName -Root $root -Name $marker
            if ($null -ne $found) {
                $markersFound += $marker
            }
        }

        return [ordered]@{
            name         = $Case.Name
            pageTag      = $Case.PageTag
            screenshot   = $screenshotPath
            seededState  = $seedPath
            readyMarker  = $pageReady.Current.Name
            markersFound = $markersFound
        }
    }
    catch {
        $debugScreenshotPath = Join-Path $OutputDirectory ("debug-" + $Case.ScreenshotName)
        $debugUiPath = Join-Path $OutputDirectory ("debug-" + $Case.Name + "-uia.txt")

        try {
            if ($process.MainWindowHandle -ne 0) {
                Save-WindowScreenshot -WindowHandle $process.MainWindowHandle -Path $debugScreenshotPath
                Write-Log "Saved failure screenshot to $debugScreenshotPath"
            }
        }
        catch {
        }

        try {
            if ($null -ne $root) {
                Write-UiSnapshot -Root $root -Path $debugUiPath
                Write-Log "Saved UI automation snapshot to $debugUiPath"
            }
        }
        catch {
        }

        throw
    }
    finally {
        if (-not $KeepAppOpen.IsPresent) {
            try {
                if (-not $process.HasExited) {
                    $null = $process.CloseMainWindow()
                    Start-Sleep -Seconds 1
                }
            }
            catch {
            }

            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
            }

            Start-Sleep -Milliseconds 500
        }

        if (Test-Path -LiteralPath $WorkspaceDataPath) {
            $postRunWorkspacePath = Join-Path $OutputDirectory ("workspace-after-" + $Case.Name + ".json")
            Write-Utf8File -Path $postRunWorkspacePath -Content (Get-Content -Path $WorkspaceDataPath -Raw)
        }
    }
}
if (-not (Test-Path -LiteralPath $ResolvedProjectPath)) {
    throw "Desktop project was not found at $ResolvedProjectPath"
}

if (-not (Test-Path -LiteralPath $ResolvedSeedWorkspacePath)) {
    throw "Seed workspace file was not found at $ResolvedSeedWorkspacePath"
}

if (-not $SkipBuild.IsPresent) {
    Invoke-DesktopBuild -ProjectPath $ResolvedProjectPath -Configuration $Configuration -Framework $Framework
}

if (-not (Test-Path -LiteralPath $ResolvedExecutablePath)) {
    throw "Desktop executable was not found at $ResolvedExecutablePath"
}

New-Item -ItemType Directory -Force -Path $ResolvedOutputDirectory | Out-Null

$workspaceDataPath = Join-Path $env:LOCALAPPDATA "Meridian\workspace-data.json"
$workspaceDataBackupPath = Join-Path $ResolvedOutputDirectory "workspace-data.pre-robinhood-options-smoke-live.json"
$workspaceDataOriginal = if (Test-Path -LiteralPath $workspaceDataPath) {
    Get-Content -Path $workspaceDataPath -Raw
}
else {
    $null
}

$operatingContextPath = Join-Path $env:LOCALAPPDATA "Meridian\workstation-operating-context.json"
$operatingContextBackupPath = Join-Path $ResolvedOutputDirectory "workstation-operating-context.pre-robinhood-options-smoke-live.json"
$operatingContextOriginal = if (Test-Path -LiteralPath $operatingContextPath) {
    Get-Content -Path $operatingContextPath -Raw
}
else {
    $null
}

if ($null -ne $workspaceDataOriginal) {
    Write-Utf8File -Path $workspaceDataBackupPath -Content $workspaceDataOriginal
    Write-Log "Backed up the current session file to $workspaceDataBackupPath"
}

if ($null -ne $operatingContextOriginal) {
    Write-Utf8File -Path $operatingContextBackupPath -Content $operatingContextOriginal
    Write-Log "Backed up the current operating context file to $operatingContextBackupPath"
}

$baseWorkspaceJson = Get-Content -Path $ResolvedSeedWorkspacePath -Raw
$baseWorkspaceData = $baseWorkspaceJson | ConvertFrom-Json -AsHashtable -Depth 100

$rawSelectionValue = if ($baseWorkspaceData.ContainsKey("lastSelectedFundProfileId") -and -not [string]::IsNullOrWhiteSpace([string]$baseWorkspaceData["lastSelectedFundProfileId"])) {
    [string]$baseWorkspaceData["lastSelectedFundProfileId"]
}
else {
    "Fund:alpha-credit"
}

if ($rawSelectionValue.StartsWith("Fund:", [System.StringComparison]::OrdinalIgnoreCase)) {
    $fundProfileId = $rawSelectionValue.Substring(5)
    $operatingContextKey = $rawSelectionValue
}
else {
    $fundProfileId = $rawSelectionValue
    $operatingContextKey = "Fund:$fundProfileId"
}

$cases = @(
    @{
        Name = "AddProviderWizard"
        WorkspaceId = "data-operations"
        PageTag = "AddProviderWizard"
        PageTitle = "Add Provider Wizard"
        ReadyMarkers = @("Add Provider Wizard", "Add Provider Relationship")
        ExpectMarkers = @("Add Provider Relationship", "Robinhood", "Capabilities: Streaming, Symbol Search, Options, Brokerage", "Robinhood Access Token")
        ScreenshotName = "meridian-robinhood-provider-smoke.png"
        Action = {
            param($root, $process)
            $robinhoodCard = Wait-Until -TimeoutSeconds 10 -FailureMessage "Robinhood provider card was not found." -Condition {
                $byAutomationId = Find-FirstElementByAutomationIds -Root $root -AutomationIds @("ProviderCard_robinhood")
                if ($null -ne $byAutomationId) {
                    return $byAutomationId
                }

                return Find-FirstElementByNames -Root $root -Names @("Provider Robinhood", "Robinhood")
            }

            Invoke-OrClickElement -Element $robinhoodCard
            Wait-ForElementByNames -Root $root -Names @("Robinhood Access Token") -TimeoutSeconds 15 -FailureMessage "Robinhood credential prompt did not appear after selecting the provider card." | Out-Null
        }
    },
    @{
        Name = "Options"
        WorkspaceId = "data-operations"
        PageTag = "Options"
        PageTitle = "Options / Derivatives"
        StartupTimeoutSeconds = 45
        ReadyMarkers = @("Options", "Options Chain")
        ExpectMarkers = @("Options Chain", "Options Summary", "Option Chain Lookup", "Tracked Underlyings", "Load Expirations", "Refresh")
        ScreenshotName = "meridian-options-smoke.png"
        Action = {
            param($root, $process)
            $symbolEdit = Wait-Until -TimeoutSeconds 8 -FailureMessage "The options lookup textbox did not appear." -Condition {
                Get-FirstEditElement -Root $root
            }
            Set-ValuePatternText -Element $symbolEdit -Text "AAPL"
            $loadButton = Wait-ForElementByNames -Root $root -Names @("Load Expirations") -TimeoutSeconds 8 -FailureMessage "Load Expirations button was not found."
            Invoke-OrClickElement -Element $loadButton
            Start-Sleep -Seconds 4
        }
    },
    @{
        Name = "PositionBlotter"
        WorkspaceId = "trading"
        PageTag = "PositionBlotter"
        PageTitle = "Position Blotter"
        StartupTimeoutSeconds = 60
        ReadyMarkers = @("Position Blotter")
        ExpectMarkers = @("Position Blotter", "Upsize", "Terminate", "Refresh")
        ScreenshotName = "meridian-position-blotter-smoke.png"
    }
)

$results = @()

try {
    Stop-MeridianProcesses

    foreach ($case in $cases) {
        $results += Invoke-SmokeCase `
            -BaseWorkspaceJson $baseWorkspaceJson `
            -WorkspaceDataPath $workspaceDataPath `
            -OperatingContextPath $operatingContextPath `
            -FundProfileId $fundProfileId `
            -OperatingContextKey $operatingContextKey `
            -Case $case `
            -ExecutablePath $ResolvedExecutablePath `
            -OutputDirectory $ResolvedOutputDirectory `
            -FixtureMode $FixtureMode
    }
}
finally {
    if (-not $KeepAppOpen.IsPresent) {
        Stop-MeridianProcesses
    }

    if ($null -ne $workspaceDataOriginal) {
        Write-Utf8File -Path $workspaceDataPath -Content $workspaceDataOriginal
        Write-Log "Restored the user's original workspace-data.json."
    }
    elseif (Test-Path -LiteralPath $workspaceDataPath) {
        Remove-Item -LiteralPath $workspaceDataPath -Force
        Write-Log "Removed the temporary workspace-data.json created for smoke testing."
    }

    if ($null -ne $operatingContextOriginal) {
        Write-Utf8File -Path $operatingContextPath -Content $operatingContextOriginal
        Write-Log "Restored the user's original workstation-operating-context.json."
    }
    elseif (Test-Path -LiteralPath $operatingContextPath) {
        Remove-Item -LiteralPath $operatingContextPath -Force
        Write-Log "Removed the temporary workstation-operating-context.json created for smoke testing."
    }
}

$resultsPath = Join-Path $ResolvedOutputDirectory "robinhood-options-smoke-results.json"
Save-JsonHashtable -Path $resultsPath -Value $results
Write-Log "Wrote smoke results to $resultsPath"
