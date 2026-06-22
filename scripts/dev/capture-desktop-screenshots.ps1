[CmdletBinding()]
param(
  [string]$Profile = 'screenshot-catalog',
  [string]$ProfileRoot = 'scripts/dev/workflow-profiles',
  [string]$ProjectPath,
  [string]$Configuration,
  [string]$Framework,
  [string]$ExeName,
  [string]$OutputDir,
  [string]$OutputRoot,
  [switch]$SkipBuild,
  [switch]$KeepAppOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repoRoot

$workflowRunner = Join-Path $PSScriptRoot 'run-desktop-workflow.ps1'
if (-not (Test-Path -LiteralPath $workflowRunner -PathType Leaf)) {
  throw "Desktop workflow runner was not found at '$workflowRunner'."
}

$workflowName = if ([string]::IsNullOrWhiteSpace($Profile)) { 'screenshot-catalog' } else { $Profile }
$screenshotDirectory = if ($PSBoundParameters.ContainsKey('OutputDir') -and -not [string]::IsNullOrWhiteSpace($OutputDir)) {
  $OutputDir
}
else {
  'docs/screenshots/desktop'
}

$workflowArtifactRoot = if ($PSBoundParameters.ContainsKey('OutputRoot') -and -not [string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot
}
else {
  $runStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  Join-Path 'artifacts/desktop-workflows' ("capture-{0}-{1}" -f $runStamp, $PID)
}

$runnerArgs = @(
  '-File', $workflowRunner,
  '-Workflow', $workflowName,
  '-Profile', $workflowName,
  '-ProfileRoot', $ProfileRoot,
  '-OutputRoot', $workflowArtifactRoot,
  '-ScreenshotDirectory', $screenshotDirectory
)

if ($PSBoundParameters.ContainsKey('ProjectPath')) {
  $runnerArgs += @('-ProjectPath', $ProjectPath)
}

if ($PSBoundParameters.ContainsKey('Configuration')) {
  $runnerArgs += @('-Configuration', $Configuration)
}

if ($PSBoundParameters.ContainsKey('Framework')) {
  $runnerArgs += @('-Framework', $Framework)
}

if ($PSBoundParameters.ContainsKey('ExeName')) {
  $runnerArgs += @('-ExeName', $ExeName)
}

if ($SkipBuild) {
  $runnerArgs += '-SkipBuild'
}

if ($KeepAppOpen) {
  $runnerArgs += '-KeepAppOpen'
}

Write-Host "Routing desktop screenshot capture through shared workflow runner '$workflowName'."
Write-Host "Screenshot output directory: $screenshotDirectory"
Write-Host "Workflow artifact root: $workflowArtifactRoot"

& pwsh -NoProfile @runnerArgs
exit $LASTEXITCODE
