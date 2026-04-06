#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$ProjectPath = 'src/Meridian.Wpf/Meridian.Wpf.csproj',
    [string]$Configuration = 'Release',
    [string]$Framework = 'net9.0-windows10.0.19041.0',
    [string]$ExeName = 'Meridian.Desktop.exe',
    [string]$OutputDir = 'docs/screenshots/desktop',
    [switch]$SkipBuild,
    [switch]$KeepAppOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runnerPath = Join-Path $PSScriptRoot 'run-desktop-workflow.ps1'
if (-not (Test-Path -LiteralPath $runnerPath)) {
    throw "Desktop workflow runner was not found at '$runnerPath'."
}

$runnerArguments = @{
    Workflow = 'screenshot-catalog'
    ProjectPath = $ProjectPath
    Configuration = $Configuration
    Framework = $Framework
    ExeName = $ExeName
    ScreenshotDirectory = $OutputDir
}

if ($SkipBuild) {
    $runnerArguments.SkipBuild = $true
}

if ($KeepAppOpen) {
    $runnerArguments.KeepAppOpen = $true
}

& $runnerPath @runnerArguments
