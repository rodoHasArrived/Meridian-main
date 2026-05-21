param(
    [Parameter(Mandatory = $true)][string[]]$TargetFiles,
    [Parameter(Mandatory = $true)][string]$DesiredEndState,
    [string[]]$BehaviorConstraints = @(),
    [string]$MarkdownPath
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$missing = @()
foreach ($target in $TargetFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $target)) -and -not (Test-Path -LiteralPath $target)) {
        $missing += $target
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Safe Refactor Plan')
$lines.Add('')
$lines.Add("Desired end state: $DesiredEndState")
$lines.Add('')
$lines.Add('## Target Files')
foreach ($target in $TargetFiles) {
    $lines.Add("- ``$target``")
}
$lines.Add('')
$lines.Add('## Behavior Preservation Constraints')
if ($BehaviorConstraints.Count -eq 0) {
    $lines.Add('- Preserve public behavior, routes, automation IDs, command names, serialization, and operator copy unless explicitly changed.')
} else {
    foreach ($constraint in $BehaviorConstraints) {
        $lines.Add("- $constraint")
    }
}
$lines.Add('')
$lines.Add('## Risk Assessment')
$lines.Add('- Check current test coverage before editing.')
$lines.Add('- Treat code-behind logic, provider calls, synchronous I/O, event subscriptions, and dense collections as higher-risk.')
$lines.Add('- Avoid mixing feature work with refactoring.')
$lines.Add('')
$lines.Add('## Step-by-Step Plan')
$lines.Add('1. Inventory call sites, tests, docs, routes, and public contracts.')
$lines.Add('2. Add characterization tests for uncovered behavior.')
$lines.Add('3. Extract the smallest shared seam behind the current behavior.')
$lines.Add('4. Migrate one call site and run focused tests.')
$lines.Add('5. Migrate remaining call sites and remove obsolete duplication.')
$lines.Add('6. Run MVVM, resource, and test-gap scans plus focused tests.')
$lines.Add('')
$lines.Add('## Test Plan')
$lines.Add('- Run existing focused WPF tests for the target files.')
$lines.Add('- Add tests for loading, empty, error, disabled, busy, cancel, retry, selected, and success states where applicable.')
$lines.Add('- Run `pwsh ./tools/codex/mvvm-compliance-check.ps1` and `pwsh ./tools/codex/resource-review.ps1`.')
$lines.Add('')
$lines.Add('## Rollback Plan')
$lines.Add('- Keep each extraction in a separate commit or patch-sized step.')
$lines.Add('- Revert the latest step if focused tests or behavior evidence fails.')

if ($missing.Count -gt 0) {
    $lines.Add('')
    $lines.Add('## Missing Files')
    foreach ($target in $missing) {
        $lines.Add("- ``$target``")
    }
}

foreach ($line in $lines) {
    Write-Host $line
}

if ($MarkdownPath) {
    $resolved = if ([System.IO.Path]::IsPathRooted($MarkdownPath)) { $MarkdownPath } else { Join-Path $root $MarkdownPath }
    $parent = Split-Path -Parent $resolved
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Set-Content -LiteralPath $resolved -Value $lines -Encoding UTF8
    Write-Host "Wrote report: $resolved"
}

if ($missing.Count -gt 0) {
    exit 1
}
exit 0
