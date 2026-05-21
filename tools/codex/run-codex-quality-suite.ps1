param(
    [string]$MarkdownPath = 'artifacts/codex/codex-quality-suite.md',
    [switch]$FailOnWarning
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$reportRoot = Join-Path $root 'artifacts/codex'
if (-not (Test-Path -LiteralPath $reportRoot)) {
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
}

$commands = @(
    @{Name='Architecture scan'; Script='architecture-scan.ps1'; Report='architecture-scan.md'},
    @{Name='MVVM compliance'; Script='mvvm-compliance-check.ps1'; Report='mvvm-compliance-check.md'},
    @{Name='Resource review'; Script='resource-review.ps1'; Report='resource-review.md'},
    @{Name='Shared pattern suggestions'; Script='shared-pattern-suggest.ps1'; Report='shared-pattern-suggest.md'},
    @{Name='Test gap scan'; Script='test-gap-scan.ps1'; Report='test-gap-scan.md'},
    @{Name='Component inventory'; Script='component-inventory.ps1'; Report='component-inventory.md'}
)

$results = New-Object System.Collections.Generic.List[object]
foreach ($command in $commands) {
    $scriptPath = Join-Path $PSScriptRoot $command.Script
    $reportPath = Join-Path $reportRoot $command.Report
    Write-Host "Running $($command.Name)..."
    if ($FailOnWarning -and $command.Script -ne 'component-inventory.ps1') {
        & $scriptPath -MarkdownPath $reportPath -FailOnWarning
    } else {
        & $scriptPath -MarkdownPath $reportPath
    }
    $exitCode = $LASTEXITCODE
    $results.Add([pscustomobject]@{
        Name = $command.Name
        Script = "tools/codex/$($command.Script)"
        Report = Get-CodexRelativePath $root $reportPath
        ExitCode = $exitCode
    })
}

$resolved = if ([System.IO.Path]::IsPathRooted($MarkdownPath)) { $MarkdownPath } else { Join-Path $root $MarkdownPath }
$parent = Split-Path -Parent $resolved
if ($parent -and -not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Codex Quality Suite')
$lines.Add('')
$lines.Add('| Check | Script | Report | Exit code |')
$lines.Add('| --- | --- | --- | ---: |')
foreach ($result in $results) {
    $lines.Add("| $($result.Name) | `$($result.Script)` | `$($result.Report)` | $($result.ExitCode) |")
}
Set-Content -LiteralPath $resolved -Value $lines -Encoding UTF8
Write-Host "Wrote combined report: $resolved"

$failed = @($results | Where-Object ExitCode -ne 0)
if ($failed.Count -gt 0) {
    exit 1
}

exit 0
