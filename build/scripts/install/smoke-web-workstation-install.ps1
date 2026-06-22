#!/usr/bin/env pwsh
<#
.SYNOPSIS
    End-to-end smoke for the Meridian WebWorkstation installer mode.

.DESCRIPTION
    Runs build/scripts/install/install.ps1 -Mode WebWorkstation into an isolated
    install root, starts the installed Meridian.exe from that installed copy, and
    verifies that /healthz and /workstation/ respond over HTTP. The smoke disables
    auth only for the launched smoke process so the static workstation route can
    be validated without local credentials.

.EXAMPLE
    .\build\scripts\install\smoke-web-workstation-install.ps1

.EXAMPLE
    .\build\scripts\install\smoke-web-workstation-install.ps1 -SkipDashboardBuild
#>

[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [ValidateRange(0, 65535)]
    [int]$Port = 0,

    [ValidateRange(10, 600)]
    [int]$TimeoutSeconds = 90,

    [string]$OutputRoot = "artifacts/install-smoke/web-workstation",

    [switch]$SkipDashboardBuild,

    [switch]$SkipNpmInstall,

    [switch]$SkipHostPublish,

    [switch]$EnableTrimmedPublish,

    [switch]$KeepInstalledCopy,

    [switch]$KeepHostOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if (-not $pwsh) {
        throw "This smoke harness requires PowerShell 7 or newer. Install pwsh, then rerun the script."
    }

    $argList = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $PSCommandPath,
        "-RuntimeIdentifier", $RuntimeIdentifier,
        "-Port", $Port.ToString(),
        "-TimeoutSeconds", $TimeoutSeconds.ToString(),
        "-OutputRoot", $OutputRoot
    )

    if ($SkipDashboardBuild) { $argList += "-SkipDashboardBuild" }
    if ($SkipNpmInstall) { $argList += "-SkipNpmInstall" }
    if ($SkipHostPublish) { $argList += "-SkipHostPublish" }
    if ($EnableTrimmedPublish) { $argList += "-EnableTrimmedPublish" }
    if ($KeepInstalledCopy) { $argList += "-KeepInstalledCopy" }
    if ($KeepHostOpen) { $argList += "-KeepHostOpen" }

    & $pwsh.Source @argList
    exit $LASTEXITCODE
}

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ""
    Write-Host "[STEP] $Message" -ForegroundColor Cyan
}

function Write-Info {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Gray
}

function Write-Ok {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[OK]   $Message" -ForegroundColor Green
}

function Write-Warn {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Resolve-RepoPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Path))
}

function Get-PowerShellExecutable {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    $windowsPowerShell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($windowsPowerShell) {
        return $windowsPowerShell.Source
    }

    throw "Neither pwsh nor Windows PowerShell was found on PATH."
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Format-CommandText {
    param([Parameter(Mandatory)][string[]]$Command)

    return ($Command | ForEach-Object {
            if ($_ -match "\s") {
                '"{0}"' -f ($_.Replace('"', '\"'))
            }
            else {
                $_
            }
        }) -join " "
}

function Invoke-LoggedCommand {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Command,
        [Parameter(Mandatory)][string]$LogPath
    )

    Write-Step $Name
    Write-Info (Format-CommandText -Command $Command)

    $output = New-Object System.Collections.Generic.List[string]
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $exitCode = 0

    try {
        & $Command[0] @($Command[1..($Command.Count - 1)]) 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object {
                $line = $_.ToString()
                $output.Add($line) | Out-Null
            }

        $lastExit = Get-Variable -Name LASTEXITCODE -ErrorAction SilentlyContinue
        if ($null -ne $lastExit -and $lastExit.Value -is [int]) {
            $exitCode = [int]$lastExit.Value
        }
        elseif (-not $?) {
            $exitCode = 1
        }
    }
    catch {
        $exitCode = 1
        $message = $_.Exception.Message
        Add-Content -LiteralPath $LogPath -Value $message
        $output.Add($message) | Out-Null
    }
    finally {
        $stopwatch.Stop()
    }

    $step = [ordered]@{
        name = $Name
        command = Format-CommandText -Command $Command
        exitCode = $exitCode
        durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        logPath = $LogPath
        tail = ($output | Select-Object -Last 25) -join [Environment]::NewLine
    }

    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode. See $LogPath."
    }

    Write-Ok "$Name completed"
    return $step
}

function Invoke-EndpointProbe {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [string]$ContentPattern = ""
    )

    Write-Step "Probe $Name"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5 -MaximumRedirection 0
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                $content = [string]$response.Content
                if (-not [string]::IsNullOrWhiteSpace($ContentPattern) -and $content -notmatch $ContentPattern) {
                    $lastError = "HTTP $($response.StatusCode), but response content did not match '$ContentPattern'."
                }
                else {
                    Write-Ok "$Name returned HTTP $($response.StatusCode)"
                    return [ordered]@{
                        name = $Name
                        uri = $Uri
                        statusCode = [int]$response.StatusCode
                        contentLength = $content.Length
                        contentType = [string]$response.Headers["Content-Type"]
                    }
                }
            }
            else {
                $lastError = "HTTP $($response.StatusCode)"
            }
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 1
    }

    throw "$Name did not pass within $TimeoutSeconds second(s): $lastError"
}

function Get-FirstWorkstationAssetUri {
    param(
        [Parameter(Mandatory)][string]$Html,
        [Parameter(Mandatory)][string]$BaseUrl
    )

    $assetMatches = [regex]::Matches($Html, '(?:src|href)=["''](?<path>/workstation/[^"'']+)["'']')
    foreach ($match in $assetMatches) {
        $path = $match.Groups["path"].Value
        if ($path -and $path -ne "/workstation/") {
            return ($BaseUrl.TrimEnd("/") + $path)
        }
    }

    return $null
}

function Assert-RequiredPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description was not found: $Path"
    }
}

function Remove-SmokeDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot)
    $trimChars = @([char][System.IO.Path]::DirectorySeparatorChar, [char][System.IO.Path]::AltDirectorySeparatorChar)
    $rootWithSeparator = $resolvedRoot.TrimEnd($trimChars) + [System.IO.Path]::DirectorySeparatorChar

    if (-not $resolvedPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove $resolvedPath because it is outside the smoke run root $resolvedRoot."
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    return $true
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$repoRoot = $script:RepoRoot
Set-Location $repoRoot

if (-not ($IsWindows -or $env:OS -eq "Windows_NT")) {
    throw "The WebWorkstation install smoke runs the Windows installer and requires Windows."
}

$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resolvedOutputRoot = Resolve-RepoPath $OutputRoot
$runRoot = Join-Path $resolvedOutputRoot $runStamp
$installRoot = Join-Path $runRoot "installed-app"
$appDataRoot = Join-Path $runRoot "appdata"
$summaryJsonPath = Join-Path $runRoot "web-workstation-install-smoke.json"
$summaryMarkdownPath = Join-Path $runRoot "web-workstation-install-smoke.md"
$installLogPath = Join-Path $runRoot "install.log"
$hostStdoutPath = Join-Path $runRoot "host.stdout.log"
$hostStderrPath = Join-Path $runRoot "host.stderr.log"
$installScript = Join-Path $repoRoot "build\scripts\install\install.ps1"
$portToUse = if ($Port -eq 0) { Get-FreeTcpPort } else { $Port }
$baseUrl = "http://localhost:$portToUse"
$healthUri = "$baseUrl/healthz"
$workstationUri = "$baseUrl/workstation/"
$shutdownUri = "$baseUrl/api/system/shutdown"
$hostProcess = $null
$shutdownToken = ([guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N"))
$result = "failed"
$failure = $null
$steps = New-Object System.Collections.Generic.List[object]
$probes = New-Object System.Collections.Generic.List[object]
$installedCopyRemoved = $false
$appDataRemoved = $false
$previousAuthMode = $env:MDC_AUTH_MODE
$previousDisableRateLimit = $env:MDC_DISABLE_RATE_LIMIT
$previousUsers = $env:MDC_USERS
$previousUsername = $env:MDC_USERNAME
$previousPasswordHash = $env:MDC_PASSWORD_HASH
$previousShutdownToken = $env:MDC_SHUTDOWN_TOKEN

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

try {
    Assert-RequiredPath -Path $installScript -Description "Root installer script"

    $powershell = Get-PowerShellExecutable
    $installCommand = @(
        $powershell,
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $installScript,
        "-Mode", "WebWorkstation",
        "-WebInstallRoot", $installRoot,
        "-WebAppDataRoot", $appDataRoot,
        "-WebPort", $portToUse.ToString(),
        "-WebRuntimeIdentifier", $RuntimeIdentifier,
        "-NoDesktopShortcut",
        "-NoStartMenuShortcut"
    )

    if ($SkipDashboardBuild) { $installCommand += "-SkipDashboardBuild" }
    if ($SkipNpmInstall) { $installCommand += "-SkipNpmInstall" }
    if ($SkipHostPublish) { $installCommand += "-SkipHostPublish" }
    if ($EnableTrimmedPublish) { $installCommand += "-EnableTrimmedPublish" }

    $steps.Add((Invoke-LoggedCommand -Name "Install isolated WebWorkstation copy" -Command $installCommand -LogPath $installLogPath)) | Out-Null

    $exePath = Join-Path $installRoot "Meridian.exe"
    $configPath = Join-Path $appDataRoot "appsettings.json"
    $installedIndexPath = Join-Path $installRoot "wwwroot\workstation\index.html"

    Assert-RequiredPath -Path $exePath -Description "Installed Meridian.exe"
    Assert-RequiredPath -Path $configPath -Description "Installed appsettings.json"
    Assert-RequiredPath -Path $installedIndexPath -Description "Installed workstation index.html"

    Write-Step "Start installed Meridian host"
    $env:MDC_AUTH_MODE = "optional"
    $env:MDC_DISABLE_RATE_LIMIT = "true"
    $env:MDC_USERS = $null
    $env:MDC_USERNAME = $null
    $env:MDC_PASSWORD_HASH = $null
    $env:MDC_SHUTDOWN_TOKEN = $shutdownToken
    $hostArgumentLine = '--mode workstation --http-port {0} --config "{1}"' -f $portToUse, $configPath.Replace('"', '\"')
    $hostProcess = Start-Process `
        -FilePath $exePath `
        -ArgumentList $hostArgumentLine `
        -WorkingDirectory $installRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $hostStdoutPath `
        -RedirectStandardError $hostStderrPath `
        -PassThru

    Write-Ok "Started installed host PID $($hostProcess.Id)"

    $probes.Add((Invoke-EndpointProbe -Name "healthz" -Uri $healthUri -TimeoutSeconds $TimeoutSeconds -ContentPattern "healthy")) | Out-Null

    $workstationProbe = Invoke-EndpointProbe `
        -Name "workstation shell" `
        -Uri $workstationUri `
        -TimeoutSeconds $TimeoutSeconds `
        -ContentPattern "Meridian Web Workstation|/workstation/assets/|id=`"root`""
    $probes.Add($workstationProbe) | Out-Null

    $workstationResponse = Invoke-WebRequest -Uri $workstationUri -UseBasicParsing -TimeoutSec 5
    $assetUri = Get-FirstWorkstationAssetUri -Html ([string]$workstationResponse.Content) -BaseUrl $baseUrl
    if (-not $assetUri) {
        throw "No workstation asset reference was found in /workstation/; the response may not be the installed workstation shell."
    }

    $probes.Add((Invoke-EndpointProbe -Name "first workstation asset" -Uri $assetUri -TimeoutSeconds 15)) | Out-Null

    $result = "passed"
}
catch {
    $failure = $_.Exception.Message
    Write-Host "[FAIL] $failure" -ForegroundColor Red
    throw
}
finally {
    $env:MDC_AUTH_MODE = $previousAuthMode
    $env:MDC_DISABLE_RATE_LIMIT = $previousDisableRateLimit
    $env:MDC_USERS = $previousUsers
    $env:MDC_USERNAME = $previousUsername
    $env:MDC_PASSWORD_HASH = $previousPasswordHash
    $env:MDC_SHUTDOWN_TOKEN = $previousShutdownToken

    if ($null -ne $hostProcess -and -not $hostProcess.HasExited -and -not $KeepHostOpen) {
        try {
            Write-Info "Requesting graceful shutdown for installed host PID $($hostProcess.Id)..."
            Invoke-WebRequest `
                -Uri $shutdownUri `
                -Method Post `
                -UseBasicParsing `
                -TimeoutSec 5 `
                -Headers @{ "X-Meridian-Shutdown-Token" = $shutdownToken } | Out-Null
            if (-not $hostProcess.WaitForExit(15000)) {
                Write-Warn "Graceful shutdown timed out; terminating installed host PID $($hostProcess.Id)."
                Stop-Process -Id $hostProcess.Id -Force -ErrorAction Stop
                $hostProcess.WaitForExit()
            }
        }
        catch {
            Write-Warn "Failed to stop installed host PID $($hostProcess.Id): $($_.Exception.Message)"
            if (-not $hostProcess.HasExited) {
                Write-Warn "Terminating installed host PID $($hostProcess.Id) after graceful shutdown failure."
                Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
                $hostProcess.WaitForExit()
            }
        }
    }

    if ($result -eq "passed" -and -not $KeepInstalledCopy) {
        try {
            $installedCopyRemoved = Remove-SmokeDirectory -Path $installRoot -AllowedRoot $runRoot
            $appDataRemoved = Remove-SmokeDirectory -Path $appDataRoot -AllowedRoot $runRoot
        }
        catch {
            Write-Warn $_.Exception.Message
        }
    }

    try {
        $stepsSnapshot = @($steps.ToArray())
        $probesSnapshot = @($probes.ToArray())
        $summary = [ordered]@{
            generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
            result = $result
            failure = $failure
            repoRoot = $repoRoot
            runtimeIdentifier = $RuntimeIdentifier
            port = $portToUse
            baseUrl = $baseUrl
            healthUri = $healthUri
            workstationUri = $workstationUri
            installRoot = $installRoot
            appDataRoot = $appDataRoot
            installedCopyRemoved = $installedCopyRemoved
            appDataRemoved = $appDataRemoved
            keepHostOpen = $KeepHostOpen.IsPresent
            keepInstalledCopy = $KeepInstalledCopy.IsPresent
            skipDashboardBuild = $SkipDashboardBuild.IsPresent
            skipNpmInstall = $SkipNpmInstall.IsPresent
            skipHostPublish = $SkipHostPublish.IsPresent
            enableTrimmedPublish = $EnableTrimmedPublish.IsPresent
            logs = [ordered]@{
                install = $installLogPath
                hostStdout = $hostStdoutPath
                hostStderr = $hostStderrPath
            }
            steps = $stepsSnapshot
            probes = $probesSnapshot
        }

        $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

        $markdown = @(
            "# Web Workstation Install Smoke",
            "",
            "- Generated: $($summary.generatedAtUtc)",
            "- Result: $result",
            "- Runtime: $RuntimeIdentifier",
            "- Base URL: $baseUrl",
            ('- Install root: `{0}`' -f $installRoot),
            ('- App data root: `{0}`' -f $appDataRoot),
            "- Installed copy removed: $installedCopyRemoved",
            "- App data removed: $appDataRemoved",
            "",
            "| Check | Result | Detail |",
            "|---|---|---|"
        )

        foreach ($step in $stepsSnapshot) {
            $relativeLogPath = [System.IO.Path]::GetRelativePath($repoRoot, $step.logPath).Replace('\', '/')
            $markdown += ('| {0} | exit {1} | `{2}` |' -f $step.name, $step.exitCode, $relativeLogPath)
        }

        foreach ($probe in $probesSnapshot) {
            $markdown += "| $($probe.name) | HTTP $($probe.statusCode) | $($probe.uri) |"
        }

        if ($failure) {
            $markdown += "| Failure | failed | $failure |"
        }

        $markdown -join [Environment]::NewLine | Set-Content -LiteralPath $summaryMarkdownPath -Encoding UTF8

        Write-Host ""
        Write-Host "Smoke artifacts:" -ForegroundColor Green
        Write-Host "  $summaryJsonPath"
        Write-Host "  $summaryMarkdownPath"
    }
    catch {
        Write-Warn "Failed to write smoke summary artifacts: $($_.Exception.Message)"
    }
}
