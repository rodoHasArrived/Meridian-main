#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [string]$WorkflowName = '',
    [switch]$UseFixture,
    [switch]$SkipBuild,
    [switch]$ReuseExistingApp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedBundlePath = [System.IO.Path]::GetFullPath($BundlePath)
$resolvedManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
$stageStatusPath = Join-Path $resolvedBundlePath 'stage-status.json'
$summaryPath = Join-Path $resolvedBundlePath 'bundle-summary.md'

$stageEntries = @()
if (Test-Path -LiteralPath $stageStatusPath) {
    $parsed = Get-Content -LiteralPath $stageStatusPath -Raw | ConvertFrom-Json
    if ($null -ne $parsed) {
        $stageEntries = @($parsed)
    }
}

$manifest = $null
if (Test-Path -LiteralPath $resolvedManifestPath) {
    $manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
}

$firstFailingStage = $stageEntries | Where-Object { $_.status -eq 'failed' } | Select-Object -First 1
$topFailureCause = if ($null -ne $firstFailingStage -and -not [string]::IsNullOrWhiteSpace($firstFailingStage.message)) {
    [string]$firstFailingStage.message
}
elseif ($null -ne $manifest -and $null -ne $manifest.run -and -not [string]::IsNullOrWhiteSpace($manifest.run.error)) {
    [string]$manifest.run.error
}
else {
    'No failure recorded.'
}

if ([string]::IsNullOrWhiteSpace($WorkflowName) -and $null -ne $manifest -and $null -ne $manifest.workflow) {
    $WorkflowName = [string]$manifest.workflow.name
}

$rerunArgs = @('-File', 'scripts/dev/run-desktop-workflow.ps1')
if (-not [string]::IsNullOrWhiteSpace($WorkflowName)) {
    $rerunArgs += @('-Workflow', $WorkflowName)
}
if (-not $UseFixture) {
    $rerunArgs += '-NoFixture'
}
if ($SkipBuild) {
    $rerunArgs += '-SkipBuild'
}
if ($ReuseExistingApp) {
    $rerunArgs += '-ReuseExistingApp'
}
$rerunCommand = 'pwsh ' + ($rerunArgs -join ' ')

$summary = @(
    '# Desktop Workflow Bundle Summary'
    ''
    "- Generated (UTC): $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')"
    "- Bundle path: `$resolvedBundlePath`"
    "- Manifest path: `$resolvedManifestPath`"
    ''
    '## Outcome'
    "- Status: $($manifest.run.status)"
    "- Top failure cause: $topFailureCause"
    "- First failing stage: $(if ($firstFailingStage) { [string]$firstFailingStage.stage } else { 'none' })"
    ''
    '## Suggested rerun command'
    '```powershell'
    $rerunCommand
    '```'
)

$summary -join [Environment]::NewLine | Set-Content -LiteralPath $summaryPath -Encoding utf8
$summaryPath
