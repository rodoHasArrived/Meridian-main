#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string[]]$Workflow,
    [string]$DefinitionPath = 'scripts/dev/desktop-workflows.json',
    [string]$OutputPath = 'artifacts/desktop-manuals/desktop-user-manual.md',
    [string]$ScreenshotRoot = 'artifacts/desktop-manuals/screenshots',
    [string]$WorkflowRunRoot = 'artifacts/desktop-manual-runs',
    [switch]$SkipBuild,
    [string]$CheckpointPath = '',
    [string[]]$ForceCheckpointStep = @(),
    [switch]$AllowCheckpointInputMismatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'SharedCheckpoint.ps1')

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Write-Ok([string]$Message) { Write-Host "[ OK ] $Message" -ForegroundColor Green }

function Resolve-RepoPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-ConfigValue {
    param(
        [System.Collections.IDictionary]$Table,
        [string]$Key,
        $Fallback = $null
    )

    if ($null -ne $Table -and $Table.Contains($Key) -and $null -ne $Table[$Key] -and "$($Table[$Key])" -ne '') {
        return $Table[$Key]
    }

    return $Fallback
}

$catalogPath = Resolve-RepoPath $DefinitionPath
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
$allWorkflows = @($catalog.workflows)
$requestedWorkflows = @($Workflow | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if ($PSBoundParameters.ContainsKey('Workflow') -and $requestedWorkflows.Count -gt 0) {
    $selectedWorkflows = foreach ($name in $requestedWorkflows) {
        $match = @($allWorkflows | Where-Object { $_.name -eq $name }) | Select-Object -First 1
        if ($null -eq $match) {
            throw "Workflow '$name' was not found in '$catalogPath'."
        }

        $match
    }
}
else {
    $selectedWorkflows = @($allWorkflows | Where-Object { [bool](Get-ConfigValue -Table $_ -Key 'includeInManual' -Fallback $false) })
}

if (@($selectedWorkflows).Count -eq 0) {
    throw 'No manual workflows were selected.'
}

$runnerPath = Resolve-RepoPath 'scripts/dev/run-desktop-workflow.ps1'
$resolvedOutputPath = Resolve-RepoPath $OutputPath
$resolvedScreenshotRoot = Resolve-RepoPath $ScreenshotRoot
$resolvedWorkflowRunRoot = Resolve-RepoPath $WorkflowRunRoot
$outputDirectory = Split-Path -Parent $resolvedOutputPath

New-Item -ItemType Directory -Force -Path $outputDirectory, $resolvedScreenshotRoot, $resolvedWorkflowRunRoot | Out-Null
if ([string]::IsNullOrWhiteSpace($CheckpointPath)) {
    $CheckpointPath = Join-Path $resolvedWorkflowRunRoot 'desktop-user-manual.checkpoint.json'
}
$checkpoint = Initialize-MeridianCheckpoint `
    -Workflow 'generate-desktop-user-manual' `
    -CheckpointPath $CheckpointPath `
    -InputObject ([ordered]@{
        workflows = @($selectedWorkflows | ForEach-Object { [string]$_.name })
        definitionPath = $catalogPath
        outputPath = $resolvedOutputPath
        screenshotRoot = $resolvedScreenshotRoot
        workflowRunRoot = $resolvedWorkflowRunRoot
        skipBuild = [bool]$SkipBuild
    }) `
    -ForceStep $ForceCheckpointStep `
    -AllowInputMismatch:$AllowCheckpointInputMismatch

$runSummaries = @()
$buildSkipped = $SkipBuild

foreach ($definition in $selectedWorkflows) {
    $checkpointStepId = "capture-$($definition.name)"
    if (-not (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId $checkpointStepId)) {
        Write-Info "Skipping workflow '$($definition.name)' capture (checkpoint resume)."
        continue
    }

    Start-MeridianCheckpointStep -Context $checkpoint -StepId $checkpointStepId -Description "Capture workflow $($definition.name) for manual."
    Write-Info "Capturing workflow '$($definition.name)' for the user manual..."

    $runnerArguments = @{
        Workflow = $definition.name
        DefinitionPath = $catalogPath
        OutputRoot = $resolvedWorkflowRunRoot
        PassThru = $true
    }

    if ($buildSkipped) {
        $runnerArguments.SkipBuild = $true
    }

    $runResult = & $runnerPath @runnerArguments
    $buildSkipped = $true

    $manifest = Get-Content -LiteralPath $runResult.manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $workflowScreenshotDirectory = Join-Path $resolvedScreenshotRoot $definition.name

    if (Test-Path -LiteralPath $workflowScreenshotDirectory) {
        Remove-Item -LiteralPath $workflowScreenshotDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $workflowScreenshotDirectory | Out-Null

    $publishedSteps = @()
    foreach ($step in @($manifest.steps | Where-Object { $_.status -eq 'ok' -and $_.capturePath })) {
        $sourcePath = Resolve-RepoPath $step.capturePath
        $destinationPath = Join-Path $workflowScreenshotDirectory ([System.IO.Path]::GetFileName($sourcePath))
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force

        $publishedSteps += [pscustomobject]@{
            index = $step.index
            title = $step.title
            notes = $step.notes
            pageTag = $step.pageTag
            imagePath = $destinationPath
        }
    }

    $runSummaries += [pscustomobject]@{
        name = $definition.name
        title = $definition.title
        description = $definition.description
        screenshotDirectory = $workflowScreenshotDirectory
        steps = $publishedSteps
    }
    Complete-MeridianCheckpointStep -Context $checkpoint -StepId $checkpointStepId -ArtifactPointers @($workflowScreenshotDirectory, $runResult.manifestPath)
}

$generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
$manualLines = New-Object System.Collections.Generic.List[string]
$manualLines.Add('# Meridian Desktop User Manual')
$manualLines.Add('')
$manualLines.Add("> Auto-generated on $generatedAt by `scripts/dev/generate-desktop-user-manual.ps1`.")
$manualLines.Add('')
$manualLines.Add('This manual is captured from the WPF desktop shell in fixture mode so the walkthrough stays deterministic, offline-friendly, and safe to reproduce while debugging UI workflows.')
$manualLines.Add('')
$manualLines.Add('## Regenerate')
$manualLines.Add('')
$manualLines.Add('```powershell')
$manualLines.Add('pwsh -File scripts/dev/generate-desktop-user-manual.ps1')
$manualLines.Add('```')
$manualLines.Add('')
$manualLines.Add('To capture a single workflow, pass one or more workflow names:')
$manualLines.Add('')
$manualLines.Add('```powershell')
$manualLines.Add('pwsh -File scripts/dev/generate-desktop-user-manual.ps1 -Workflow manual-overview,manual-data-operations')
$manualLines.Add('```')
$manualLines.Add('')

foreach ($workflowSummary in $runSummaries) {
    $manualLines.Add("## $($workflowSummary.title)")
    $manualLines.Add('')
    $manualLines.Add($workflowSummary.description)
    $manualLines.Add('')

    foreach ($step in $workflowSummary.steps) {
        $relativeImagePath = [System.IO.Path]::GetRelativePath($outputDirectory, $step.imagePath).Replace('\', '/')
        $manualLines.Add("### Step $($step.index) - $($step.title)")
        $manualLines.Add('')

        if (-not [string]::IsNullOrWhiteSpace($step.notes)) {
            $manualLines.Add($step.notes)
            $manualLines.Add('')
        }

        $manualLines.Add(('![{0}]({1})' -f $step.title, $relativeImagePath))
        $manualLines.Add('')
    }
}

if (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId 'write-manual') {
    Start-MeridianCheckpointStep -Context $checkpoint -StepId 'write-manual' -Description 'Write desktop user manual markdown.'
    $manualLines | Set-Content -LiteralPath $resolvedOutputPath -Encoding utf8
    Complete-MeridianCheckpointStep -Context $checkpoint -StepId 'write-manual' -ArtifactPointers @($resolvedOutputPath)
}

Write-Ok "User manual written to $resolvedOutputPath"
Write-Ok "Manual screenshots written to $resolvedScreenshotRoot"
