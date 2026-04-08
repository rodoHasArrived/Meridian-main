param(
    [string]$ExecutablePath = "C:\Users\Andrew James Rowden\OneDrive\Documents\OneDrive\Documents\Desktop\Meridian-main\publish-fixed\win-x64\desktop\Meridian.Desktop.exe",
    [string]$OutputDirectory = "C:\Users\Andrew James Rowden\OneDrive\Documents\OneDrive\Documents\Desktop\Meridian-main\output\manual-captures"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[smoke $timestamp] $Message"
}

function Read-JsonHashtable {
    param([string]$Path)
    return (Get-Content -Path $Path -Raw | ConvertFrom-Json -AsHashtable -Depth 100)
}

function Clone-JsonObject {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100)
}

function Save-JsonHashtable {
    param(
        [string]$Path,
        [object]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 100
    Set-Content -Path $Path -Value $json -Encoding UTF8
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

function Wait-ForElementByNames {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string[]]$Names,
        [int]$TimeoutSeconds = 20,
        [string]$FailureMessage = "Timed out waiting for a target UI element."
    )

    return Wait-Until -TimeoutSeconds $TimeoutSeconds -FailureMessage $FailureMessage -Condition {
        foreach ($name in $Names) {
            $element = Find-ElementByExactName -Root $Root -Name $name
            if ($null -ne $element) {
                return $element
            }
        }

        return $null
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

        if ($current.TryGetClickablePoint([ref]([System.Windows.Point]$clickPoint = [System.Windows.Point]::new()))) {
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

function New-SessionState {
    param(
        [hashtable]$BaseData,
        [string]$WorkspaceId,
        [string]$PageTag,
        [string]$PageTitle
    )

    $session = if ($BaseData.ContainsKey("lastSession") -and $null -ne $BaseData["lastSession"]) {
        Clone-JsonObject -Value $BaseData["lastSession"]
    } else {
        @{}
    }

    $session["activePageTag"] = $PageTag
    $session["activeWorkspaceId"] = $WorkspaceId
    $session["openPages"] = @(
        @{
            pageTag = $PageTag
            title = $PageTitle
            isDefault = $true
            scrollPosition = 0
            pageState = @{}
        }
    )
    $session["recentPages"] = @($PageTag)
    $session["widgetLayout"] = @{}
    $session["activeFilters"] = @{}
    $session["workspaceContext"] = @{}
    $session["savedLayoutPresets"] = @()
    $session["savedAt"] = (Get-Date).ToUniversalTime().ToString("o")

    return $session
}

function New-SmokeWorkspaceData {
    param(
        [hashtable]$BaseData,
        [string]$WorkspaceId,
        [string]$PageTag,
        [string]$PageTitle,
        [string]$FundProfileId,
        [string]$OperatingContextKey
    )

    $data = Clone-JsonObject -Value $BaseData
    $session = New-SessionState -BaseData $BaseData -WorkspaceId $WorkspaceId -PageTag $PageTag -PageTitle $PageTitle

    $data["activeWorkspaceId"] = $WorkspaceId
    $data["lastSession"] = $session
    $data["lastSelectedFundProfileId"] = $OperatingContextKey

    if (-not $data.ContainsKey("sessionsByFundProfileId") -or $null -eq $data["sessionsByFundProfileId"]) {
        $data["sessionsByFundProfileId"] = @{}
    }

    $data["sessionsByFundProfileId"][$OperatingContextKey] = Clone-JsonObject -Value $session
    $data["sessionsByFundProfileId"][$FundProfileId] = Clone-JsonObject -Value $session

    foreach ($workspace in @($data["workspaces"])) {
        if ($workspace["id"] -eq $WorkspaceId) {
            $workspace["lastActivePageTag"] = $PageTag
            $workspace["recentPageTags"] = @($PageTag)
            $workspace["sessionSnapshot"] = Clone-JsonObject -Value $session
        }
    }

    return $data
}

function Invoke-SmokeCase {
    param(
        [hashtable]$BaseWorkspaceData,
        [string]$WorkspaceDataPath,
        [string]$FundProfileId,
        [string]$OperatingContextKey,
        [hashtable]$Case
    )

    Write-Log "Preparing session for $($Case.Name) ($($Case.PageTag))."
    $seededData = New-SmokeWorkspaceData `
        -BaseData $BaseWorkspaceData `
        -WorkspaceId $Case.WorkspaceId `
        -PageTag $Case.PageTag `
        -PageTitle $Case.PageTitle `
        -FundProfileId $FundProfileId `
        -OperatingContextKey $OperatingContextKey
    Save-JsonHashtable -Path $WorkspaceDataPath -Value $seededData

    Write-Log "Launching published desktop executable for $($Case.Name)."
    $process = Start-Process -FilePath $ExecutablePath -PassThru

    try {
        $root = Get-WindowAutomationRoot -Process $process

        $initialMarkers = @("Fund Profile Selection", "Choose Fund Profile") + @($Case.ReadyMarkers)
        $fundPage = Wait-ForElementByNames -Root $root -TimeoutSeconds 18 -Names $initialMarkers -FailureMessage "Timed out waiting for fund selection or target page."
        if ($fundPage.Current.Name -in @("Fund Profile Selection", "Choose Fund Profile")) {
            Write-Log "Fund selection detected. Entering the selected fund profile."
            $enterFundButton = Wait-ForElementByNames -Root $root -Names @("Enter Fund") -TimeoutSeconds 10 -FailureMessage "Fund selection page appeared, but the Enter Fund button was not found."
            Invoke-OrClickElement -Element $enterFundButton
        }

        $pageReady = Wait-ForElementByNames -Root $root -Names $Case.ReadyMarkers -TimeoutSeconds 25 -FailureMessage "Timed out waiting for the $($Case.Name) page to load."
        Write-Log "$($Case.Name) page is visible via '$($pageReady.Current.Name)'."

        if ($Case.ContainsKey("Action") -and $Case["Action"] -is [scriptblock]) {
            & $Case["Action"] $root
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
            name = $Case.Name
            pageTag = $Case.PageTag
            screenshot = $screenshotPath
            readyMarker = $pageReady.Current.Name
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
    }
}

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Published desktop executable was not found at $ExecutablePath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$workspaceDataPath = Join-Path $env:LOCALAPPDATA "Meridian\workspace-data.json"
$workspaceDataBackupPath = Join-Path $OutputDirectory "workspace-data.pre-robinhood-options-smoke-live.json"
$workspaceDataOriginal = if (Test-Path -LiteralPath $workspaceDataPath) {
    Get-Content -Path $workspaceDataPath -Raw
} else {
    $null
}

if ($null -ne $workspaceDataOriginal) {
    Set-Content -Path $workspaceDataBackupPath -Value $workspaceDataOriginal -Encoding UTF8
    Write-Log "Backed up the current session file to $workspaceDataBackupPath"
}

$baseWorkspaceData = if ($null -ne $workspaceDataOriginal) {
    $workspaceDataOriginal | ConvertFrom-Json -AsHashtable -Depth 100
} else {
    Read-JsonHashtable -Path "C:\Users\Andrew James Rowden\OneDrive\Documents\OneDrive\Documents\Desktop\Meridian-main\output\manual-captures\workspace-data.after-fix-smoke.json"
}

$fundProfileId = if ($baseWorkspaceData.ContainsKey("lastSelectedFundProfileId") -and -not [string]::IsNullOrWhiteSpace($baseWorkspaceData["lastSelectedFundProfileId"])) {
    $rawLastSelected = [string]$baseWorkspaceData["lastSelectedFundProfileId"]
    if ($rawLastSelected.StartsWith("Fund:", [StringComparison]::OrdinalIgnoreCase)) {
        $rawLastSelected.Substring(5)
    } else {
        $rawLastSelected
    }
} else {
    "alpha-credit"
}

$operatingContextKey = "Fund:$fundProfileId"

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
            param($root)
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
            param($root)
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
            -BaseWorkspaceData $baseWorkspaceData `
            -WorkspaceDataPath $workspaceDataPath `
            -FundProfileId $fundProfileId `
            -OperatingContextKey $operatingContextKey `
            -Case $case
    }
} finally {
    Stop-MeridianProcesses

    if ($null -ne $workspaceDataOriginal) {
        Set-Content -Path $workspaceDataPath -Value $workspaceDataOriginal -Encoding UTF8
        Write-Log "Restored the user's original workspace-data.json."
    } elseif (Test-Path -LiteralPath $workspaceDataPath) {
        Remove-Item -LiteralPath $workspaceDataPath -Force
        Write-Log "Removed the temporary workspace-data.json created for smoke testing."
    }
}

$resultsPath = Join-Path $OutputDirectory "robinhood-options-smoke-results.json"
Save-JsonHashtable -Path $resultsPath -Value $results
Write-Log "Wrote smoke results to $resultsPath"
