param(
    [string]$ExecutablePath = "C:\Users\Andrew James Rowden\OneDrive\Documents\OneDrive\Documents\Desktop\Meridian-main\publish\sessionfix\win-x64\desktop\Meridian.Desktop.exe",
    [string]$OutputDirectory = "C:\Users\Andrew James Rowden\OneDrive\Documents\OneDrive\Documents\Desktop\Meridian-main\output\manual-captures",
    [string]$SeedWorkspacePath = "C:\Users\Andrew James Rowden\OneDrive\Documents\OneDrive\Documents\Desktop\Meridian-main\output\manual-captures\workspace-data.after-fix-smoke.json",
    [bool]$FixtureMode = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
        "-ExecutablePath", $ExecutablePath,
        "-OutputDirectory", $OutputDirectory,
        "-SeedWorkspacePath", $SeedWorkspacePath,
        "-FixtureMode", $FixtureMode.ToString()
    )

    & $pwsh @argList
    exit $LASTEXITCODE
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

function Read-JsonHashtable {
    param([string]$Path)
    return (Get-Content -Path $Path -Raw | ConvertFrom-Json -AsHashtable -Depth 100)
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
        } catch {
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
        } catch {
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

        [System.Windows.Point]$clickPoint = [System.Windows.Point]::new()
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
    } finally {
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
        WindowMode                    = 0
        CurrentLayoutPresetId         = $null
    }

    Save-JsonHashtable -Path $Path -Value $state
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
    } catch {
        $enterButton = $null
    }

    if ($null -ne $enterButton) {
        Invoke-OrClickElement -Element $enterButton
        return
    }

    Send-WindowKeys -Process $Process -Keys "{ENTER}"
}

function Invoke-SmokeCase {
    param(
        [string]$BaseWorkspaceJson,
        [string]$WorkspaceDataPath,
        [string]$OperatingContextPath,
        [string]$FundProfileId,
        [string]$OperatingContextKey,
        [hashtable]$Case
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

    Write-Log "Launching published desktop executable for $($Case.Name)."
    $startProcessArgs = @{
        FilePath         = $ExecutablePath
        ArgumentList     = @("--page=$($Case.PageTag)")
        PassThru         = $true
        WorkingDirectory = (Split-Path -Parent $ExecutablePath)
    }

    if ($FixtureMode) {
        $startProcessArgs["Environment"] = @{ MDC_FIXTURE_MODE = "1" }
    }

    $process = Start-Process @startProcessArgs

    try {
        $root = Get-WindowAutomationRoot -Process $process

        $selectionMarkers = @("Operating Context Selection", "Fund Profile Selection", "Choose Fund Profile")
        $initialMarkers = $selectionMarkers + @($Case.ReadyMarkers)
        $initialElement = Wait-ForElementByNames -Root $root -TimeoutSeconds 18 -Names $initialMarkers -FailureMessage "Timed out waiting for operating context selection or target page."

        if ($initialElement.Current.Name -in $selectionMarkers) {
            Write-Log "Operating context selection detected. Entering the preselected workstation context."
            Invoke-EnterWorkstation -Root $root -Process $process
        }

        $pageReady = Wait-ForElementByNames -Root $root -Names $Case.ReadyMarkers -TimeoutSeconds 25 -FailureMessage "Timed out waiting for the $($Case.Name) page to load."
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
    } catch {
        $debugScreenshotPath = Join-Path $OutputDirectory ("debug-" + $Case.ScreenshotName)
        try {
            if ($process.MainWindowHandle -ne 0) {
                Save-WindowScreenshot -WindowHandle $process.MainWindowHandle -Path $debugScreenshotPath
                Write-Log "Saved failure screenshot to $debugScreenshotPath"
            }
        } catch {
        }

        throw
    } finally {
        try {
            if (-not $process.HasExited) {
                $null = $process.CloseMainWindow()
                Start-Sleep -Seconds 1
            }
        } catch {
        }

        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }

        Start-Sleep -Milliseconds 500

        if (Test-Path -LiteralPath $WorkspaceDataPath) {
            $postRunWorkspacePath = Join-Path $OutputDirectory ("workspace-after-" + $Case.Name + ".json")
            Write-Utf8File -Path $postRunWorkspacePath -Content (Get-Content -Path $WorkspaceDataPath -Raw)
        }
    }
}

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Published desktop executable was not found at $ExecutablePath"
}

if (-not (Test-Path -LiteralPath $SeedWorkspacePath)) {
    throw "Seed workspace file was not found at $SeedWorkspacePath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$workspaceDataPath = Join-Path $env:LOCALAPPDATA "Meridian\workspace-data.json"
$workspaceDataBackupPath = Join-Path $OutputDirectory "workspace-data.pre-robinhood-options-smoke-live.json"
$workspaceDataOriginal = if (Test-Path -LiteralPath $workspaceDataPath) {
    Get-Content -Path $workspaceDataPath -Raw
} else {
    $null
}

$operatingContextPath = Join-Path $env:LOCALAPPDATA "Meridian\workstation-operating-context.json"
$operatingContextBackupPath = Join-Path $OutputDirectory "workstation-operating-context.pre-robinhood-options-smoke-live.json"
$operatingContextOriginal = if (Test-Path -LiteralPath $operatingContextPath) {
    Get-Content -Path $operatingContextPath -Raw
} else {
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

$baseWorkspaceJson = if ($null -ne $workspaceDataOriginal) {
    $workspaceDataOriginal
} else {
    Get-Content -Path $SeedWorkspacePath -Raw
}

$baseWorkspaceData = $baseWorkspaceJson | ConvertFrom-Json -AsHashtable -Depth 100

$rawSelectionValue = if ($baseWorkspaceData.ContainsKey("lastSelectedFundProfileId") -and -not [string]::IsNullOrWhiteSpace([string]$baseWorkspaceData["lastSelectedFundProfileId"])) {
    [string]$baseWorkspaceData["lastSelectedFundProfileId"]
} else {
    "alpha-credit"
}

if ($rawSelectionValue.StartsWith("Fund:", [System.StringComparison]::OrdinalIgnoreCase)) {
    $fundProfileId = $rawSelectionValue.Substring(5)
    $operatingContextKey = $rawSelectionValue
} else {
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
            $robinhoodText = Wait-ForElementByNames -Root $root -Names @("Robinhood") -TimeoutSeconds 10 -FailureMessage "Robinhood provider card text was not found."
            Invoke-OrClickElement -Element $robinhoodText
            Wait-ForElementByNames -Root $root -Names @("Robinhood Access Token") -TimeoutSeconds 10 -FailureMessage "Robinhood credential prompt did not appear after selecting the provider card." | Out-Null
        }
    },
    @{
        Name = "Options"
        WorkspaceId = "data-operations"
        PageTag = "Options"
        PageTitle = "Options / Derivatives"
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
        ReadyMarkers = @("Position Blotter")
        ExpectMarkers = @("Position Blotter", "Upsize", "Close", "Refresh")
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
            -Case $case
    }
} finally {
    Stop-MeridianProcesses

    if ($null -ne $workspaceDataOriginal) {
        Write-Utf8File -Path $workspaceDataPath -Content $workspaceDataOriginal
        Write-Log "Restored the user's original workspace-data.json."
    } elseif (Test-Path -LiteralPath $workspaceDataPath) {
        Remove-Item -LiteralPath $workspaceDataPath -Force
        Write-Log "Removed the temporary workspace-data.json created for smoke testing."
    }

    if ($null -ne $operatingContextOriginal) {
        Write-Utf8File -Path $operatingContextPath -Content $operatingContextOriginal
        Write-Log "Restored the user's original workstation-operating-context.json."
    } elseif (Test-Path -LiteralPath $operatingContextPath) {
        Remove-Item -LiteralPath $operatingContextPath -Force
        Write-Log "Removed the temporary workstation-operating-context.json created for smoke testing."
    }
}

$resultsPath = Join-Path $OutputDirectory "robinhood-options-smoke-results.json"
Save-JsonHashtable -Path $resultsPath -Value $results
Write-Log "Wrote smoke results to $resultsPath"


