#!/usr/bin/env pwsh
[CmdletBinding(DefaultParameterSetName = 'Project')]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory, ParameterSetName = 'Project')]
    [ValidateNotNullOrEmpty()]
    [string]$IBApiProjectPath,

    [Parameter(Mandatory, ParameterSetName = 'Dll')]
    [ValidateNotNullOrEmpty()]
    [string]$IBApiDllPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$SmokeHost,

    [Parameter(Mandatory)]
    [ValidateRange(1, 65535)]
    [int]$SmokePort,

    [ValidateRange(1, 120)]
    [int]$ConnectTimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if ($PSCmdlet.ParameterSetName -eq 'Project' -and -not (Test-Path -LiteralPath $IBApiProjectPath -PathType Leaf)) {
    throw "IBApiProjectPath '$IBApiProjectPath' does not exist. The runtime lane only accepts an official IB API project or DLL."
}

if ($PSCmdlet.ParameterSetName -eq 'Dll' -and -not (Test-Path -LiteralPath $IBApiDllPath -PathType Leaf)) {
    throw "IBApiDllPath '$IBApiDllPath' does not exist. The runtime lane only accepts an official IB API project or DLL."
}

$projectPath = 'src/Meridian.Infrastructure/Meridian.Infrastructure.csproj'
$command = @(
    'dotnet',
    'build',
    $projectPath,
    '-c',
    $Configuration,
    '-p:EnableWindowsTargeting=true',
    '-p:EnableIbApiVendor=true',
    '-maxcpucount:1'
)

if ($PSCmdlet.ParameterSetName -eq 'Project') {
    $command += "-p:IBApiProjectPath=$IBApiProjectPath"
}
else {
    $command += "-p:IBApiDllPath=$IBApiDllPath"
}

Write-Host 'Building the Interactive Brokers runtime configuration against the official SDK...'
& $command[0] @($command[1..($command.Count - 1)])
if ($LASTEXITCODE -ne 0) {
    throw "Official IB API runtime build failed with exit code $LASTEXITCODE. Command: $($command -join ' ')."
}

Write-Host "Checking paper Gateway/TWS socket reachability at $SmokeHost`:$SmokePort..."
$client = [System.Net.Sockets.TcpClient]::new()
try {
    $connect = $client.ConnectAsync($SmokeHost, $SmokePort)
    if (-not $connect.Wait([TimeSpan]::FromSeconds($ConnectTimeoutSeconds))) {
        throw "Timed out after $ConnectTimeoutSeconds seconds while connecting to $SmokeHost`:$SmokePort."
    }

    $connect.GetAwaiter().GetResult()
    if (-not $client.Connected) {
        throw "The TCP connection to $SmokeHost`:$SmokePort did not reach a connected state."
    }
}
finally {
    $client.Dispose()
}

Write-Host 'Official IB API runtime build and paper socket smoke completed successfully.'
