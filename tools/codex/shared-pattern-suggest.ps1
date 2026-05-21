param(
    [string]$MarkdownPath,
    [switch]$FailOnWarning
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$findings = New-Object System.Collections.Generic.List[object]
$files = @(Get-CodexFiles -Root $root -Directories @('src/Meridian.Wpf') -Extensions @('.cs', '.xaml'))
$patterns = @(
    @{Name='toolbar-command-strip'; Regex='Toolbar|CommandBar|PrimaryCommands|SecondaryCommands|RefreshCommand|ExportCommand'},
    @{Name='status-badge'; Regex='StatusBadge|HealthBadge|Severity|Degraded|Healthy|Failed'},
    @{Name='loading-error-empty-state'; Regex='IsLoading|IsBusy|ErrorMessage|EmptyState|No.*Available|Try again'},
    @{Name='detail-inspector-panel'; Regex='Inspector|DetailPanel|Selected.*Detail|DetailTabs|TabControl'},
    @{Name='diagnostics-panel'; Regex='Diagnostics|Health|Telemetry|Incident|RecoveryAction'},
    @{Name='audit-timeline'; Regex='Audit|Timeline|Activity|Evidence|CorrelationId'},
    @{Name='dense-table'; Regex='DataGrid|ListView|GridView|Columns|SelectedItem'}
)

foreach ($pattern in $patterns) {
    $matches = @()
    foreach ($file in $files) {
        if (Select-String -LiteralPath $file.FullName -Pattern $pattern.Regex -Quiet) {
            $matches += (Get-CodexRelativePath $root $file.FullName)
        }
    }

    if ($matches.Count -ge 3) {
        $sample = ($matches | Select-Object -First 5) -join ', '
        $findings.Add((New-CodexFinding -Severity Warning -Rule $pattern.Name -Path 'src/Meridian.Wpf' -Message "Pattern appears in $($matches.Count) files. Consider a shared primitive. Sample: $sample"))
    }
}

$viewModels = @($files | Where-Object { $_.Name -like '*ViewModel.cs' })
$suffixGroups = $viewModels | Group-Object { $_.BaseName -replace '^(.*?)(Provider|Health|Ledger|Timeline|Inspector|Grid|Panel|Workflow).*$','$2' } | Where-Object { $_.Name -and $_.Count -ge 3 }
foreach ($group in $suffixGroups) {
    $sample = ($group.Group | Select-Object -First 5 | ForEach-Object { Get-CodexRelativePath $root $_.FullName }) -join ', '
    $findings.Add((New-CodexFinding -Severity Info -Rule 'similar-view-model-family' -Path 'src/Meridian.Wpf/ViewModels' -Message "View model family '$($group.Name)' has $($group.Count) files. Check for shared state/command helpers. Sample: $sample"))
}

Write-CodexFindingReport -Title 'Codex Shared Pattern Suggestions' -Findings $findings.ToArray() -MarkdownPath $MarkdownPath
exit (Get-CodexExitCode -Findings $findings.ToArray() -FailOnWarning:$FailOnWarning)
