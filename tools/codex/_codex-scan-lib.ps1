Set-StrictMode -Version Latest

function Get-CodexRepoRoot {
    param([string]$StartPath = (Get-Location).Path)

    $current = (Resolve-Path -LiteralPath $StartPath).Path
    while ($current) {
        if (Test-Path -LiteralPath (Join-Path $current 'Meridian.sln')) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }
        $current = $parent
    }

    throw "Could not find Meridian.sln above $StartPath. Run from the repository root."
}

function Get-CodexRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    if ($resolvedPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $resolvedPath.Substring($rootPath.Length).TrimStart('\').Replace('\', '/')
    }

    return $resolvedPath.Replace('\', '/')
}

function Test-CodexExcludedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return $Path -match '\\(\.git|bin|obj|artifacts|node_modules|coverage|TestResults)\\'
}

function Get-CodexFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string[]]$Directories = @('src', 'tests'),
        [string[]]$Extensions = @('.cs', '.xaml', '.ps1', '.fs', '.fsproj', '.csproj')
    )

    foreach ($directory in $Directories) {
        $fullDirectory = Join-Path $Root $directory
        if (-not (Test-Path -LiteralPath $fullDirectory)) {
            continue
        }

        Get-ChildItem -LiteralPath $fullDirectory -Recurse -File |
            Where-Object {
                -not (Test-CodexExcludedPath $_.FullName) -and
                ($Extensions -contains $_.Extension)
            }
    }
}

function Get-CodexLineCount {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    return (Get-Content -LiteralPath $Path -ReadCount 1000 | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
}

function New-CodexFinding {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Info', 'Warning', 'Error')][string]$Severity,
        [Parameter(Mandatory = $true)][string]$Rule,
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$Line = 0,
        [Parameter(Mandatory = $true)][string]$Message
    )

    [pscustomobject]@{
        Severity = $Severity
        Rule = $Rule
        Path = $Path
        Line = $Line
        Message = $Message
    }
}

function Add-CodexPatternFindings {
    param(
        [System.Collections.Generic.List[object]]$Findings,
        [Parameter(Mandatory = $true)][string]$Root,
        [object[]]$Files = @(),
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Rule,
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet('Info', 'Warning', 'Error')][string]$Severity = 'Warning'
    )

    foreach ($file in $Files) {
        Select-String -LiteralPath $file.FullName -Pattern $Pattern -AllMatches | ForEach-Object {
            $Findings.Add((New-CodexFinding -Severity $Severity -Rule $Rule -Path (Get-CodexRelativePath -Root $Root -Path $file.FullName) -Line $_.LineNumber -Message $Message))
        }
    }
}

function Write-CodexFindingReport {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [object[]]$Findings = @(),
        [string]$MarkdownPath
    )

    $errorCount = @($Findings | Where-Object Severity -eq 'Error').Count
    $warningCount = @($Findings | Where-Object Severity -eq 'Warning').Count
    $infoCount = @($Findings | Where-Object Severity -eq 'Info').Count

    Write-Host "$Title"
    Write-Host ("Findings: {0} error(s), {1} warning(s), {2} info item(s)." -f $errorCount, $warningCount, $infoCount)

    if ($Findings.Count -gt 0) {
        $displayFindings = @($Findings |
            Sort-Object Severity, Path, Line, Rule |
            Select-Object -First 100 Severity, Rule, Path, Line, Message)
        $displayFindings |
            Format-Table -AutoSize
        if ($Findings.Count -gt $displayFindings.Count) {
            Write-Host ("Console output truncated to first {0} finding(s); Markdown report contains all findings." -f $displayFindings.Count)
        }
    }

    if ($MarkdownPath) {
        $resolved = if ([System.IO.Path]::IsPathRooted($MarkdownPath)) { $MarkdownPath } else { Join-Path (Get-Location).Path $MarkdownPath }
        $parent = Split-Path -Parent $resolved
        if ($parent -and -not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }

        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add("# $Title")
        $lines.Add("")
        $lines.Add(("Findings: {0} error(s), {1} warning(s), {2} info item(s)." -f $errorCount, $warningCount, $infoCount))
        $lines.Add("")
        $lines.Add("| Severity | Rule | Location | Message |")
        $lines.Add("| --- | --- | --- | --- |")
        foreach ($finding in ($Findings | Sort-Object Severity, Path, Line, Rule)) {
            $location = if ($finding.Line -gt 0) { "$($finding.Path):$($finding.Line)" } else { $finding.Path }
            $message = $finding.Message.Replace('|', '\|')
            $lines.Add("| $($finding.Severity) | $($finding.Rule) | `$location` | $message |")
        }
        Set-Content -LiteralPath $resolved -Value $lines -Encoding UTF8
        Write-Host "Wrote report: $resolved"
    }
}

function Get-CodexExitCode {
    param(
        [object[]]$Findings = @(),
        [switch]$FailOnWarning
    )

    if (@($Findings | Where-Object Severity -eq 'Error').Count -gt 0) {
        return 1
    }

    if ($FailOnWarning -and @($Findings | Where-Object Severity -eq 'Warning').Count -gt 0) {
        return 1
    }

    return 0
}
