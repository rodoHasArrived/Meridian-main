#!/usr/bin/env pwsh
<#
.SYNOPSIS
    End-to-end smoke for the Meridian WebWorkstation installer mode.

.DESCRIPTION
    Runs build/scripts/install/install.ps1 -Mode WebWorkstation into an isolated
    install root, starts the installed Meridian.exe from that installed copy, and
    verifies that /startupz, /healthz, and /workstation/ respond over HTTP. The smoke configures
    a temporary hash-backed operator for the launched process so authenticated workstation access
    is validated without weakening packaged authentication. The installed lifecycle supervisor owns
    both the Meridian host and its bundled dedicated PostgreSQL process.

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
    # The outer probe clock starts at supervisor launch, but the supervisor's readiness
    # window (installed manifest startupTimeoutSeconds = 300, covering single-file
    # self-extraction plus first migrations) only starts after dedicated PostgreSQL
    # initialization (databaseTimeoutSeconds = 60). The outer budget must therefore exceed
    # readiness + database + launcher overhead — 300 + 60 + 30 — or the smoke can stop a
    # host still inside its readiness contract. Probes exit early on success, so the
    # budget only costs time on real failures.
    [int]$TimeoutSeconds = 390,

    [string]$PostgreSqlPayloadRoot = $env:MDC_POSTGRES_PAYLOAD_ROOT,

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
        "-OutputRoot", $OutputRoot,
        "-PostgreSqlPayloadRoot", $PostgreSqlPayloadRoot
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
        [string]$ContentPattern = "",
        [Microsoft.PowerShell.Commands.WebRequestSession]$WebSession
    )

    Write-Step "Probe $Name"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $requestParameters = @{
                Uri = $Uri
                UseBasicParsing = $true
                TimeoutSec = 5
                MaximumRedirection = 0
            }
            if ($null -ne $WebSession) {
                $requestParameters.WebSession = $WebSession
            }
            $response = Invoke-WebRequest @requestParameters
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
$startupUri = "$baseUrl/startupz"
$loginUri = "$baseUrl/api/auth/login"
$supervisorProcess = $null
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
$previousPostgreSqlPayloadRoot = $env:MDC_POSTGRES_PAYLOAD_ROOT

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

try {
    Assert-RequiredPath -Path $installScript -Description "Root installer script"
    if ([string]::IsNullOrWhiteSpace($PostgreSqlPayloadRoot)) {
        throw "A runtime-specific PostgreSQL payload is required. Set -PostgreSqlPayloadRoot or MDC_POSTGRES_PAYLOAD_ROOT."
    }
    $resolvedPostgreSqlPayloadRoot = [System.IO.Path]::GetFullPath($PostgreSqlPayloadRoot)
    foreach ($requiredTool in @("postgres.exe", "pg_ctl.exe", "initdb.exe")) {
        Assert-RequiredPath `
            -Path (Join-Path $resolvedPostgreSqlPayloadRoot "$RuntimeIdentifier\bin\$requiredTool") `
            -Description "PostgreSQL $RuntimeIdentifier $requiredTool"
    }
    $env:MDC_POSTGRES_PAYLOAD_ROOT = $resolvedPostgreSqlPayloadRoot

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
    $supervisorPath = Join-Path $installRoot "Meridian.LifecycleSupervisor.exe"
    $configPath = Join-Path $appDataRoot "appsettings.json"
    $installedIndexPath = Join-Path $installRoot "wwwroot\workstation\index.html"

    Assert-RequiredPath -Path $exePath -Description "Installed Meridian.exe"
    Assert-RequiredPath -Path $supervisorPath -Description "Installed lifecycle supervisor"
    Assert-RequiredPath -Path (Join-Path $installRoot "database\bin\postgres.exe") -Description "Bundled PostgreSQL server"
    Assert-RequiredPath -Path $configPath -Description "Installed appsettings.json"
    Assert-RequiredPath -Path $installedIndexPath -Description "Installed workstation index.html"

    Write-Step "Start installed lifecycle supervisor"
    $smokeUsername = "lifecycle-smoke"
    $smokePassword = "test-password"
    $smokePasswordHash = 'pbkdf2-sha256$210000$oOQU8zfLm/Pzwrl8VZlatQ==$ePPcBmch9qAIfhbablmoBT/tKPGb/TKmFBHlFWKV1uU='
    $env:MDC_AUTH_MODE = "required"
    $env:MDC_DISABLE_RATE_LIMIT = "true"
    $env:MDC_USERS = $null
    $env:MDC_USERNAME = $smokeUsername
    $env:MDC_PASSWORD_HASH = $smokePasswordHash
    $supervisorProcess = Start-Process `
        -FilePath $supervisorPath `
        -ArgumentList "start" `
        -WorkingDirectory $installRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $hostStdoutPath `
        -RedirectStandardError $hostStderrPath `
        -PassThru

    Write-Ok "Started lifecycle supervisor PID $($supervisorProcess.Id)"

    $probes.Add((Invoke-EndpointProbe -Name "startupz" -Uri $startupUri -TimeoutSeconds $TimeoutSeconds -ContentPattern '"readiness":"(Ready|Degraded)"')) | Out-Null

    $probes.Add((Invoke-EndpointProbe -Name "healthz" -Uri $healthUri -TimeoutSeconds $TimeoutSeconds -ContentPattern "healthy")) | Out-Null

    Write-Step "Authenticate installed workstation smoke operator"
    $smokeSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $loginBody = @{ username = $smokeUsername; password = $smokePassword; returnUrl = "/workstation/" } | ConvertTo-Json -Compress
    $loginResponse = Invoke-WebRequest `
        -Uri $loginUri `
        -Method Post `
        -ContentType "application/json" `
        -Body $loginBody `
        -WebSession $smokeSession `
        -UseBasicParsing `
        -TimeoutSec 10
    if ($loginResponse.StatusCode -ne 200) {
        throw "Installed workstation authentication failed with HTTP $($loginResponse.StatusCode)."
    }
    Write-Ok "Installed workstation issued an authenticated smoke session"

    $workstationProbe = Invoke-EndpointProbe `
        -Name "workstation shell" `
        -Uri $workstationUri `
        -TimeoutSeconds $TimeoutSeconds `
        -ContentPattern "Meridian Web Workstation|/workstation/assets/|id=`"root`"" `
        -WebSession $smokeSession
    $probes.Add($workstationProbe) | Out-Null

    $workstationResponse = Invoke-WebRequest -Uri $workstationUri -WebSession $smokeSession -UseBasicParsing -TimeoutSec 5
    $assetUri = Get-FirstWorkstationAssetUri -Html ([string]$workstationResponse.Content) -BaseUrl $baseUrl
    if (-not $assetUri) {
        throw "No workstation asset reference was found in /workstation/; the response may not be the installed workstation shell."
    }

    $probes.Add((Invoke-EndpointProbe -Name "first workstation asset" -Uri $assetUri -TimeoutSeconds 15 -WebSession $smokeSession)) | Out-Null

    if (-not $KeepHostOpen) {
        Write-Step "Stop installed lifecycle session"
        & $supervisorPath stop
        if ($LASTEXITCODE -notin @(0, 3)) {
            throw "Lifecycle supervisor stop failed with exit code $LASTEXITCODE."
        }
        if (-not $supervisorProcess.WaitForExit(($TimeoutSeconds + 15) * 1000)) {
            throw "Lifecycle supervisor did not exit within the shutdown deadline."
        }
        $receiptRoot = Join-Path $appDataRoot "data\runtime\lifecycle\receipts"
        $sessionReceipt = Get-ChildItem -LiteralPath $receiptRoot -Filter "session-*.json" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $sessionReceipt) {
            throw "The lifecycle supervisor did not persist a terminal session receipt under $receiptRoot."
        }
        $probes.Add([ordered]@{
            name = "shutdown receipt"
            uri = $sessionReceipt.FullName
            statusCode = 200
            contentLength = $sessionReceipt.Length
            contentType = "application/json"
        }) | Out-Null
        Write-Ok "Lifecycle supervisor stopped host and dedicated database with a persisted receipt"
    }

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
    $env:MDC_POSTGRES_PAYLOAD_ROOT = $previousPostgreSqlPayloadRoot

    if ($null -ne $supervisorProcess -and -not $supervisorProcess.HasExited -and -not $KeepHostOpen) {
        try {
            Write-Info "Requesting supervisor-owned shutdown for installed session PID $($supervisorProcess.Id)..."
            $supervisorPath = Join-Path $installRoot "Meridian.LifecycleSupervisor.exe"
            & $supervisorPath stop
            [void]$supervisorProcess.WaitForExit(($TimeoutSeconds + 15) * 1000)
        }
        catch {
            Write-Warn "Failed to stop installed lifecycle session PID $($supervisorProcess.Id): $($_.Exception.Message)"
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
                supervisorStdout = $hostStdoutPath
                supervisorStderr = $hostStderrPath
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
