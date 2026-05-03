#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Profile,
    [string]$ProfileRoot = 'scripts/dev/workflow-profiles',
    [switch]$NoFixture,
    [switch]$ReuseExistingApp,
    [switch]$EmitJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')

$profileEnvelope = Get-MeridianWorkflowProfile -RepoRoot $repoRoot -ProfileName $Profile -ProfileRoot $ProfileRoot
$validation = Test-MeridianWorkflowProfile -ProfileData $profileEnvelope.data -NoFixture:$NoFixture -ReuseExistingApp:$ReuseExistingApp

$build = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'build' -Fallback @{}
$projectPath = [string](Get-MeridianWorkflowProfileValue -Table $build -Key 'projectPath' -Fallback '')
$resolvedProjectPath = if ([string]::IsNullOrWhiteSpace($projectPath)) {
    ''
}
elseif ([System.IO.Path]::IsPathRooted($projectPath)) {
    [System.IO.Path]::GetFullPath($projectPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $projectPath))
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $validation.errors += @("Required command 'dotnet' is not available in PATH.")
}

if (-not [string]::IsNullOrWhiteSpace($resolvedProjectPath) -and -not (Test-Path -LiteralPath $resolvedProjectPath)) {
    $validation.errors += @("Configured build.projectPath does not exist: '$resolvedProjectPath'.")
}

$validation.isValid = $validation.errors.Count -eq 0
$result = [ordered]@{
    profile = $Profile
    profilePath = $profileEnvelope.path
    isValid = $validation.isValid
    errors = $validation.errors
    warnings = $validation.warnings
    resolved = [ordered]@{
        projectPath = $resolvedProjectPath
        fixtureRequired = $validation.resolved.fixtureRequired
        screenshotOutputRoot = $validation.resolved.outputRoot
        retention = $validation.resolved.retention
    }
}

if ($EmitJson) {
    $result | ConvertTo-Json -Depth 10
}
else {
    Write-Host "Profile: $($result.profile)"
    Write-Host "Manifest: $($result.profilePath)"

    if ($result.warnings.Count -gt 0) {
        foreach ($warning in $result.warnings) {
            Write-Warning $warning
        }
    }

    if (-not $result.isValid) {
        foreach ($error in $result.errors) {
            Write-Error $error
        }
    }
    else {
        Write-Host 'Profile validation succeeded.' -ForegroundColor Green
    }
}

if (-not $result.isValid) {
    exit 1
}
