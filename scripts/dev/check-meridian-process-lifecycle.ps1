#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reports Meridian-owned process lifecycle state.

.DESCRIPTION
    Lists running Meridian and dotnet processes, reads Meridian runtime-state files,
    probes local lifecycle endpoints when a shutdown token is available, and optionally
    cleans up only tracked Meridian-owned processes. The default mode is report-only.

.EXAMPLE
    pwsh ./scripts/dev/check-meridian-process-lifecycle.ps1

.EXAMPLE
    pwsh ./scripts/dev/check-meridian-process-lifecycle.ps1 -CleanupOwned
#>

[CmdletBinding()]
param(
    [switch]$CleanupOwned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ProcessCommandLine {
    param([Parameter(Mandatory)][int]$ProcessId)

    try {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
        return [string]$process.CommandLine
    }
    catch {
        return ""
    }
}

function Read-RuntimeState {
    param([Parameter(Mandatory)][string]$Path)

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Could not read runtime state '$Path': $($_.Exception.Message)"
        return $null
    }
}

function Test-TrackedProcessMatches {
    param([Parameter(Mandatory)]$Runtime)

    if ($null -eq $Runtime.processId) {
        return $false
    }

    try {
        $process = Get-Process -Id ([int]$Runtime.processId) -ErrorAction Stop
    }
    catch {
        return $false
    }

    if ($Runtime.exePath -and $process.Path) {
        if (-not [string]::Equals([string]$process.Path, [string]$Runtime.exePath, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    if ($Runtime.startedAtUtc) {
        try {
            $runtimeStart = [DateTimeOffset]::Parse([string]$Runtime.startedAtUtc).UtcDateTime
            if ($process.StartTime.ToUniversalTime() -lt $runtimeStart.AddSeconds(-5)) {
                return $false
            }
        }
        catch {
            return $false
        }
    }

    return $process.ProcessName -like "Meridian*"
}

function Invoke-GracefulShutdown {
    param([Parameter(Mandatory)]$Runtime)

    if ($null -eq $Runtime.port -or [string]::IsNullOrWhiteSpace([string]$Runtime.shutdownToken)) {
        return $false
    }

    $uri = "http://localhost:$($Runtime.port)/api/system/shutdown"
    try {
        Invoke-WebRequest `
            -Uri $uri `
            -Method Post `
            -UseBasicParsing `
            -TimeoutSec 5 `
            -Headers @{ "X-Meridian-Shutdown-Token" = [string]$Runtime.shutdownToken } | Out-Null
        return $true
    }
    catch {
        Write-Warning "Graceful shutdown probe failed for $uri: $($_.Exception.Message)"
        return $false
    }
}

function Find-RuntimeStateFiles {
    $candidates = New-Object System.Collections.Generic.List[string]
    $localAppData = [Environment]::GetFolderPath("LocalApplicationData")
    foreach ($name in @("Meridian", "Meridian-Test")) {
        $path = Join-Path $localAppData "$name\service\web-workstation-runtime.json"
        if (Test-Path -LiteralPath $path) {
            $candidates.Add($path)
        }
    }

    return $candidates.ToArray()
}

Write-Host "Meridian process lifecycle report"
Write-Host ""

$interestingProcesses = Get-Process |
    Where-Object { $_.ProcessName -like "Meridian*" -or $_.ProcessName -eq "dotnet" } |
    Sort-Object ProcessName, Id

if (-not $interestingProcesses) {
    Write-Host "No Meridian or dotnet processes are currently visible."
}
else {
    foreach ($process in $interestingProcesses) {
        $commandLine = Get-ProcessCommandLine -ProcessId $process.Id
        Write-Host ("{0,8}  {1,-12}  {2}" -f $process.Id, $process.ProcessName, $commandLine)
    }
}

Write-Host ""
Write-Host "Runtime state files"
$runtimeFiles = Find-RuntimeStateFiles
if (-not $runtimeFiles) {
    Write-Host "No Meridian web-workstation runtime state files were found."
}

foreach ($runtimeFile in $runtimeFiles) {
    $runtime = Read-RuntimeState -Path $runtimeFile
    if ($null -eq $runtime) {
        continue
    }

    $matches = Test-TrackedProcessMatches -Runtime $runtime
    Write-Host "- $runtimeFile"
    Write-Host "  PID: $($runtime.processId)"
    Write-Host "  Port: $($runtime.port)"
    Write-Host "  Config: $($runtime.configPath)"
    Write-Host "  Metadata match: $matches"

    if ($CleanupOwned) {
        if (-not $matches) {
            Write-Warning "Skipping cleanup for PID $($runtime.processId); runtime metadata did not match the current process."
            continue
        }

        $requested = Invoke-GracefulShutdown -Runtime $runtime
        Start-Sleep -Seconds 3
        $stillMatches = Test-TrackedProcessMatches -Runtime $runtime
        if ($stillMatches) {
            Write-Warning "Tracked process $($runtime.processId) did not exit after graceful request; terminating owned process."
            Stop-Process -Id ([int]$runtime.processId) -Force
        }

        if (Test-Path -LiteralPath $runtimeFile) {
            Remove-Item -LiteralPath $runtimeFile -Force
        }

        Write-Host "  Cleanup requested: $requested"
    }
}
