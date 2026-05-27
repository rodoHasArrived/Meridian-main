param(
    [string]$MarkdownPath,
    [switch]$FailOnWarning
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Get-CodexRepoRoot
Set-Location $root
$wpfRoot = Join-Path $root 'src/Meridian.Wpf'

if (-not (Test-Path -LiteralPath $wpfRoot)) {
    throw "Unable to locate WPF source root at $wpfRoot"
}

$findings = New-Object System.Collections.Generic.List[object]
$wpfFiles = @(Get-CodexFiles -Root $root -Directories @('src/Meridian.Wpf') -Extensions @('.cs', '.xaml'))
$codeBehindFiles = @($wpfFiles | Where-Object {
    $_.Extension -eq '.cs' -and $_.FullName -match '\\Views\\.*\.xaml\.cs$'
})
$views = @($wpfFiles | Where-Object { $_.FullName -match '\\Views\\' -and $_.Extension -eq '.xaml' })

Add-CodexPatternFindings $findings $root $codeBehindFiles 'ICommand|RelayCommand|DelegateCommand|AsyncCommand|CanExecute|Execute\(' 'command-in-code-behind' 'Command behavior appears in code-behind; move command state to the view model.' Warning
Add-CodexPatternFindings $findings $root $codeBehindFiles 'if\s*\(|switch\s*\(|foreach\s*\(|while\s*\(' 'logic-in-code-behind' 'Branching or loops in code-behind may indicate workflow logic; verify it is view-only glue.' Info
Add-CodexPatternFindings $findings $root $codeBehindFiles 'Provider|MarketData|HistoricalData|OrderGateway|Ledger|Storage|Repository|HttpClient' 'domain-access-from-view' 'View code appears to touch provider/domain/storage concepts; keep those in services/view models.' Warning
Add-CodexPatternFindings $findings $root $wpfFiles 'File\.(ReadAll|WriteAll|AppendAll|Open|Create)|HttpClient|SqlConnection' 'io-in-ui-surface' 'I/O surface found in WPF code; verify it is service-owned and async.' Warning

foreach ($view in $views) {
    $viewName = [System.IO.Path]::GetFileNameWithoutExtension($view.Name)
    $expected = $viewName -replace 'Page$', 'ViewModel' -replace 'View$', 'ViewModel'
    $viewModels = Get-ChildItem -LiteralPath $wpfRoot -Recurse -File -Filter "$expected.cs" -ErrorAction SilentlyContinue

    if (-not $viewModels) {
        $findings.Add((New-CodexFinding -Severity Info -Rule 'missing-conventional-view-model' -Path (Get-CodexRelativePath -Root $root -Path $view.FullName) -Message "No conventional $expected found. Verify the view has an explicit view-model owner."))
    }
    elseif (@($viewModels).Count -gt 1) {
        $findings.Add((New-CodexFinding -Severity Warning -Rule 'ambiguous-view-model-convention' -Path (Get-CodexRelativePath -Root $root -Path $view.FullName) -Message "Multiple conventional ViewModel matches for $expected. Confirm intended ownership."))
    }
}

$viewModels = @($wpfFiles | Where-Object { $_.Name -like '*ViewModel.cs' })
Add-CodexPatternFindings $findings $root $viewModels 'IsLoading|IsBusy|Error|Empty|Validation|DisabledReason' 'state-surface-present' 'State marker found; verify loading/error/empty/disabled behavior has tests.' Info

$uniqueFindings = New-Object System.Collections.Generic.List[object]
$findingKeys = @{}
foreach ($finding in $findings) {
    $path = if ($finding.Path) { $finding.Path.Trim() } else { '' }
    $line = [int]$finding.Line
    $key = "$($finding.Severity)|$($finding.Rule)|$path|$line|$($finding.Message)"
    if (-not $findingKeys.ContainsKey($key)) {
        $findingKeys[$key] = $true
        $uniqueFindings.Add($finding)
    }
}

Write-Host ("Validated {0} files under {1}" -f $wpfFiles.Count, (Get-CodexRelativePath -Root $root -Path $wpfRoot))
if ($MarkdownPath) {
    Write-Host ("Report target: $MarkdownPath")
}

Write-CodexFindingReport -Title 'Codex MVVM Compliance Check' -Findings $uniqueFindings.ToArray() -MarkdownPath $MarkdownPath
exit (Get-CodexExitCode -Findings $uniqueFindings.ToArray() -FailOnWarning:$FailOnWarning)
