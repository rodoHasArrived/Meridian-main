#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Shared resolution for Windows 10/11 SDK command-line tools.

.DESCRIPTION
    makeappx.exe and signtool.exe ship in the Windows SDK and are not on PATH on the GitHub
    windows-latest image, so `Get-Command signtool.exe` fails there. Dot-source this file and call
    Resolve-WindowsSdkTool instead of relying on PATH.
#>

function Resolve-WindowsSdkTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName,

        [string]$ExplicitPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "Windows SDK tool not found: $ExplicitPath"
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $onPath = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($null -ne $onPath) {
        return $onPath.Source
    }

    $sdkRoots = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $sdkRoots += Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $sdkRoots += Join-Path $env:ProgramFiles "Windows Kits\10\bin"
    }

    foreach ($sdkRoot in ($sdkRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $sdkRoot -PathType Container)) {
            continue
        }

        $versionDirectories = Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending

        foreach ($versionDirectory in $versionDirectories) {
            foreach ($toolArchitecture in @("x64", "x86", "arm64")) {
                $candidate = Join-Path $versionDirectory.FullName "$toolArchitecture\$ToolName"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return $candidate
                }
            }
        }

        $certificationKitCandidate = Join-Path (Split-Path -Parent $sdkRoot) "App Certification Kit\$ToolName"
        if (Test-Path -LiteralPath $certificationKitCandidate -PathType Leaf) {
            return $certificationKitCandidate
        }
    }

    throw "$ToolName was not found on PATH or in an installed Windows 10/11 SDK."
}
