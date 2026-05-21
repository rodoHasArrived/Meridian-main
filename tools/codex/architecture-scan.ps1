param(
    [string]$MarkdownPath,
    [switch]$FailOnWarning
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$findings = New-Object System.Collections.Generic.List[object]

$wpfFiles = @(Get-CodexFiles -Root $root -Directories @('src/Meridian.Wpf') -Extensions @('.cs', '.xaml'))
$viewFiles = @($wpfFiles | Where-Object { $_.FullName -match '\\Views\\' -and $_.Extension -eq '.xaml' })
$viewModelFiles = @($wpfFiles | Where-Object { $_.FullName -match '\\ViewModels\\' -and $_.Name -like '*ViewModel.cs' })
$codeBehindFiles = @($wpfFiles | Where-Object { $_.Name -like '*.xaml.cs' })

foreach ($file in $viewFiles) {
    $lines = Get-CodexLineCount $file.FullName
    if ($lines -gt 500) {
        $findings.Add((New-CodexFinding -Severity Warning -Rule 'large-view' -Path (Get-CodexRelativePath $root $file.FullName) -Message "View has $lines lines; consider composition or shared controls."))
    }
}

foreach ($file in $viewModelFiles) {
    $lines = Get-CodexLineCount $file.FullName
    if ($lines -gt 800) {
        $findings.Add((New-CodexFinding -Severity Warning -Rule 'large-view-model' -Path (Get-CodexRelativePath $root $file.FullName) -Message "View model has $lines lines; consider services, child view models, or shared command/state helpers."))
    }

    $className = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $testMatches = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests/Meridian.Wpf.Tests') -Recurse -File -Filter '*Tests.cs' -ErrorAction SilentlyContinue | Select-String -Pattern ([regex]::Escape($className)) -Quiet)
    if ($testMatches.Count -eq 0) {
        $findings.Add((New-CodexFinding -Severity Info -Rule 'missing-view-model-test-reference' -Path (Get-CodexRelativePath $root $file.FullName) -Message "No WPF test references $className. Verify coverage before changing behavior."))
    }
}

foreach ($file in $codeBehindFiles) {
    $lines = Get-CodexLineCount $file.FullName
    if ($lines -gt 160) {
        $findings.Add((New-CodexFinding -Severity Warning -Rule 'large-code-behind' -Path (Get-CodexRelativePath $root $file.FullName) -Message "Code-behind has $lines lines; inspect for workflow or business logic."))
    }
}

Add-CodexPatternFindings $findings $root $codeBehindFiles 'GetRequiredService|new\s+\w*Service|IMarketDataClient|IHistoricalDataProvider|ProviderSdk|IOrderGateway' 'direct-service-call-from-view' 'Code-behind appears to reach services/providers directly; keep workflow in services/view models.' Warning
Add-CodexPatternFindings $findings $root $wpfFiles 'File\.(ReadAll|WriteAll|AppendAll|Open|Create)|Directory\.(GetFiles|EnumerateFiles)' 'sync-io-ui-project' 'Synchronous file or directory I/O in WPF project; verify it is off the UI path or convert to async/bounded workflow.' Warning
Add-CodexPatternFindings $findings $root $wpfFiles 'Thread\.Sleep|\.Wait\(\)|\.Result\b|GetAwaiter\(\)\.GetResult\(' 'blocking-wait' 'Blocking wait found; avoid UI-thread stalls and sync-over-async.' Warning
Add-CodexPatternFindings $findings $root $wpfFiles 'new\s+(ObservableCollection|List|Dictionary)<[^>]+>\s*\(\s*\)' 'possibly-unbounded-collection' 'Collection allocation may be unbounded; verify paging, virtualization, or bounded lifecycle for large data.' Info

$projectFiles = @(Get-CodexFiles -Root $root -Directories @('src') -Extensions @('.csproj'))
$projectNames = @{}
foreach ($project in $projectFiles) {
    $projectNames[$project.FullName] = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
}
$edges = @{}
foreach ($project in $projectFiles) {
    $edges[$project.FullName] = New-Object System.Collections.Generic.List[string]
    Select-String -LiteralPath $project.FullName -Pattern '<ProjectReference Include="([^"]+)"' | ForEach-Object {
        $target = Join-Path (Split-Path -Parent $project.FullName) $_.Matches[0].Groups[1].Value
        $resolved = [System.IO.Path]::GetFullPath($target)
        if ($projectNames.ContainsKey($resolved)) {
            $edges[$project.FullName].Add($resolved)
        }
    }
}
foreach ($source in $edges.Keys) {
    foreach ($target in $edges[$source]) {
        if ($edges.ContainsKey($target) -and $edges[$target].Contains($source)) {
            $findings.Add((New-CodexFinding -Severity Error -Rule 'circular-project-reference' -Path (Get-CodexRelativePath $root $source) -Message "Project has direct circular reference with $(Get-CodexRelativePath $root $target)."))
        }
    }
}

Write-CodexFindingReport -Title 'Codex Architecture Scan' -Findings $findings.ToArray() -MarkdownPath $MarkdownPath
exit (Get-CodexExitCode -Findings $findings.ToArray() -FailOnWarning:$FailOnWarning)
