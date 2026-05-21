#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$projectPath = 'src/Meridian.Infrastructure/Meridian.Infrastructure.csproj'
$command = @(
    'dotnet',
    'build',
    $projectPath,
    '-c',
    $Configuration,
    '-p:EnableWindowsTargeting=true',
    '-p:EnableIbApiSmoke=true',
    '-maxcpucount:1'
)

Write-Host 'Running Interactive Brokers compile-only smoke build with the local IBApi stub...'
& $command[0] @($command[1..($command.Count - 1)])
if ($LASTEXITCODE -ne 0) {
    throw "IBApi smoke build failed with exit code $LASTEXITCODE. Command: $($command -join ' ')."
}

Write-Host 'IBApi smoke build completed successfully.'
