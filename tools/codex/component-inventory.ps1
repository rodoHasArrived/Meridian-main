param(
    [string]$MarkdownPath
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$items = New-Object System.Collections.Generic.List[object]
$componentDirs = @('src/Meridian.Wpf/Controls', 'src/Meridian.Wpf/Templates', 'src/Meridian.Wpf/Behaviors', 'src/Meridian.Wpf/Views')
$allXaml = @(Get-CodexFiles -Root $root -Directories @('src/Meridian.Wpf') -Extensions @('.xaml'))

foreach ($dir in $componentDirs) {
    $fullDir = Join-Path $root $dir
    if (-not (Test-Path -LiteralPath $fullDir)) {
        continue
    }

    Get-ChildItem -LiteralPath $fullDir -Recurse -File -Include '*.xaml', '*.cs' | Where-Object { -not (Test-CodexExcludedPath $_.FullName) } | ForEach-Object {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
        $usageCount = 0
        foreach ($xaml in $allXaml) {
            if ($xaml.FullName -eq $_.FullName) {
                continue
            }
            if (Select-String -LiteralPath $xaml.FullName -Pattern $name -Quiet) {
                $usageCount++
            }
        }

        $purpose = switch -Regex ($name) {
            'Grid|Table|List|Blotter' { 'Dense data or list component'; break }
            'Inspector|Detail|Panel' { 'Inspector or detail panel'; break }
            'Status|Badge|Health' { 'Status or health indicator'; break }
            'Timeline|Audit|Activity' { 'Timeline or audit component'; break }
            'Toolbar|Command|Action' { 'Command or toolbar primitive'; break }
            default { 'Desktop component or screen surface' }
        }

        $items.Add([pscustomobject]@{
            Component = $name
            Location = Get-CodexRelativePath $root $_.FullName
            Purpose = $purpose
            ScreensUsingIt = $usageCount
            RefactoringCandidate = if ($usageCount -eq 0 -and $_.FullName -match '\\Controls\\|\\Templates\\') { 'unused-or-dynamic-use' } elseif ($usageCount -gt 3) { 'promote-document-contract' } else { '' }
        })
    }
}

Write-Host 'Codex Component Inventory'
Write-Host ("Components: {0}" -f $items.Count)
$items | Sort-Object Purpose, Component | Format-Table -AutoSize

if ($MarkdownPath) {
    $resolved = if ([System.IO.Path]::IsPathRooted($MarkdownPath)) { $MarkdownPath } else { Join-Path $root $MarkdownPath }
    $parent = Split-Path -Parent $resolved
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Codex Component Inventory')
    $lines.Add('')
    $lines.Add('| Component | Location | Purpose | Screens using it | Refactoring candidate |')
    $lines.Add('| --- | --- | --- | ---: | --- |')
    foreach ($item in ($items | Sort-Object Purpose, Component)) {
        $lines.Add("| $($item.Component) | `$($item.Location)` | $($item.Purpose) | $($item.ScreensUsingIt) | $($item.RefactoringCandidate) |")
    }
    Set-Content -LiteralPath $resolved -Value $lines -Encoding UTF8
    Write-Host "Wrote report: $resolved"
}

exit 0
