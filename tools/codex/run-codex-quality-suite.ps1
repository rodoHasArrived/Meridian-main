param(
    [string]$MarkdownPath = 'artifacts/codex/codex-quality-suite.md',
    [switch]$FailOnWarning,
    [switch]$Fast,
    [string[]]$Checks
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$reportRoot = Join-Path $root 'artifacts/codex'
if (-not (Test-Path -LiteralPath $reportRoot)) {
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
}

$allCommands = @(
    [pscustomobject]@{Key='architecture'; Name='Architecture scan'; Script='architecture-scan.ps1'; Report='architecture-scan.md'; SupportsFailOnWarning=$true; Fast=$true},
    [pscustomobject]@{Key='mvvm'; Name='MVVM compliance'; Script='mvvm-compliance-check.ps1'; Report='mvvm-compliance-check.md'; SupportsFailOnWarning=$true; Fast=$true},
    [pscustomobject]@{Key='resource'; Name='Resource review'; Script='resource-review.ps1'; Report='resource-review.md'; SupportsFailOnWarning=$true; Fast=$true},
    [pscustomobject]@{Key='shared-pattern'; Name='Shared pattern suggestions'; Script='shared-pattern-suggest.ps1'; Report='shared-pattern-suggest.md'; SupportsFailOnWarning=$true; Fast=$false},
    [pscustomobject]@{Key='test-gap'; Name='Test gap scan'; Script='test-gap-scan.ps1'; Report='test-gap-scan.md'; SupportsFailOnWarning=$true; Fast=$true},
    [pscustomobject]@{Key='inventory'; Name='Component inventory'; Script='component-inventory.ps1'; Report='component-inventory.md'; SupportsFailOnWarning=$false; Fast=$false}
)

if ($Checks -and $Checks.Count -gt 0) {
    $checkSet = @{}
    foreach ($check in $Checks) {
        if ($check) {
            $checkSet[$check.Trim().ToLowerInvariant()] = $true
        }
    }
    $commands = @(
        $allCommands | Where-Object {
            $normalizedName = $_.Name.ToLowerInvariant()
            $normalizedScript = [System.IO.Path]::GetFileNameWithoutExtension($_.Script).ToLowerInvariant()
            $checkSet.ContainsKey($_.Key) -or $checkSet.ContainsKey($normalizedName) -or $checkSet.ContainsKey($normalizedScript)
        }
    )
}
elseif ($Fast) {
    $commands = @($allCommands | Where-Object Fast)
}
else {
    $commands = $allCommands
}

if (-not $commands -or $commands.Count -eq 0) {
    throw 'No Codex quality checks selected. Use -Fast or pass valid -Checks values.'
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($command in $commands) {
    $scriptPath = Join-Path $PSScriptRoot $command.Script
    $reportPath = Join-Path $reportRoot $command.Report
    Write-Host "Running $($command.Name)..."
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Write-Host "Missing script: $scriptPath"
        $results.Add([pscustomobject]@{
            Name = $command.Name
            Script = "tools/codex/$($command.Script)"
            Report = '(not generated)'
            ExitCode = 1
            DurationSeconds = 0
        })
        continue
    }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    if ($FailOnWarning -and $command.SupportsFailOnWarning) {
        & $scriptPath -MarkdownPath $reportPath -FailOnWarning
    } else {
        & $scriptPath -MarkdownPath $reportPath
    }
    $stopwatch.Stop()

    $exitCode = 0
    if ($LASTEXITCODE -ne $null) {
        $exitCode = [int]$LASTEXITCODE
    }
    if (-not $?) {
        $exitCode = if ($exitCode -eq 0) { 1 } else { $exitCode }
    }

    $relativeReport = if (Test-Path -LiteralPath $reportPath) {
        Get-CodexRelativePath $root $reportPath
    } else {
        '(not generated)'
    }

    $results.Add([pscustomobject]@{
        Name = $command.Name
        Script = "tools/codex/$($command.Script)"
        Report = $relativeReport
        ExitCode = $exitCode
        DurationSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
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
$lines.Add(("Mode: {0}" -f (if ($Checks -and $Checks.Count -gt 0) { "custom ($($Checks -join ', '))" } elseif ($Fast) { 'fast' } else { 'full' })))
$lines.Add('')
$lines.Add('| Check | Script | Report | Exit code |')
$lines.Add('| --- | --- | --- | ---: |')
foreach ($result in $results) {
    $lines.Add(('| {0} | `{1}` | `{2}` | {3} |' -f $result.Name, $result.Script, $result.Report, $result.ExitCode))
}
Set-Content -LiteralPath $resolved -Value $lines -Encoding UTF8
Write-Host "Wrote combined report: $resolved"

$failed = @($results | Where-Object ExitCode -ne 0)
if ($failed.Count -gt 0) {
    exit 1
}

exit 0
