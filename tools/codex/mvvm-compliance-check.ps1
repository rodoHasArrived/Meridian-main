param(
    [string]$MarkdownPath,
    [switch]$FailOnWarning
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$findings = New-Object System.Collections.Generic.List[object]
$wpfFiles = @(Get-CodexFiles -Root $root -Directories @('src/Meridian.Wpf') -Extensions @('.cs', '.xaml'))
$codeBehindFiles = @($wpfFiles | Where-Object { $_.Name -like '*.xaml.cs' })
$views = @($wpfFiles | Where-Object { $_.FullName -match '\\Views\\' -and $_.Extension -eq '.xaml' })

Add-CodexPatternFindings $findings $root $codeBehindFiles 'ICommand|RelayCommand|DelegateCommand|AsyncCommand|CanExecute|Execute\(' 'command-in-code-behind' 'Command behavior appears in code-behind; move command state to the view model.' Warning
Add-CodexPatternFindings $findings $root $codeBehindFiles 'if\s*\(|switch\s*\(|foreach\s*\(|while\s*\(' 'logic-in-code-behind' 'Branching or loops in code-behind may indicate workflow logic; verify it is view-only glue.' Info
Add-CodexPatternFindings $findings $root $codeBehindFiles 'Provider|MarketData|HistoricalData|OrderGateway|Ledger|Storage|Repository|HttpClient' 'domain-access-from-view' 'View code appears to touch provider/domain/storage concepts; keep those in services/view models.' Warning
Add-CodexPatternFindings $findings $root $wpfFiles 'File\.(ReadAll|WriteAll|AppendAll|Open|Create)|HttpClient|SqlConnection' 'io-in-ui-surface' 'I/O surface found in WPF code; verify it is service-owned and async.' Warning

foreach ($view in $views) {
    $viewName = [System.IO.Path]::GetFileNameWithoutExtension($view.Name)
    $expected = $viewName -replace 'Page$', 'ViewModel' -replace 'View$', 'ViewModel'
    $viewModel = Get-ChildItem -LiteralPath (Join-Path $root 'src/Meridian.Wpf') -Recurse -File -Filter "$expected.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $viewModel) {
        $findings.Add((New-CodexFinding -Severity Info -Rule 'missing-conventional-view-model' -Path (Get-CodexRelativePath $root $view.FullName) -Message "No conventional $expected found. Verify the view has an explicit view-model owner."))
    }
}

$viewModels = @($wpfFiles | Where-Object { $_.Name -like '*ViewModel.cs' })
Add-CodexPatternFindings $findings $root $viewModels 'IsLoading|IsBusy|Error|Empty|Validation|DisabledReason' 'state-surface-present' 'State marker found; verify loading/error/empty/disabled behavior has tests.' Info

Write-CodexFindingReport -Title 'Codex MVVM Compliance Check' -Findings $findings.ToArray() -MarkdownPath $MarkdownPath
exit (Get-CodexExitCode -Findings $findings.ToArray() -FailOnWarning:$FailOnWarning)
