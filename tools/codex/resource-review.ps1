param(
    [string]$MarkdownPath,
    [switch]$FailOnWarning
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$findings = New-Object System.Collections.Generic.List[object]
$files = @(Get-CodexFiles -Root $root -Directories @('src/Meridian.Wpf', 'src/Meridian.Ui.Services', 'src/Meridian.Application', 'src/Meridian.Infrastructure') -Extensions @('.cs', '.xaml'))

Add-CodexPatternFindings $findings $root $files 'Thread\.Sleep|\.Wait\(\)|\.Result\b|GetAwaiter\(\)\.GetResult\(' 'blocking-async' 'Blocking wait or sleep found; avoid UI stalls and thread-pool starvation.' Warning
Add-CodexPatternFindings $findings $root $files 'File\.(ReadAll|WriteAll|AppendAll|Open|Create)|Directory\.(GetFiles|EnumerateFiles)' 'sync-io' 'Synchronous I/O found; verify it is off UI paths or replace with async/bounded access.' Warning
Add-CodexPatternFindings $findings $root $files '\.ToList\(\)|\.ToArray\(\)' 'materialization' 'Collection materialization found; verify dataset is bounded or streaming/paging is impractical.' Info
Add-CodexPatternFindings $findings $root $files 'new\s+(DispatcherTimer|Timer|System\.Threading\.Timer)' 'timer-lifecycle' 'Timer allocation found; verify interval, stop/dispose, and lifecycle ownership.' Warning
Add-CodexPatternFindings $findings $root $files '\+=\s*[^;]+;' 'event-subscription' 'Event subscription found; verify unsubscribe, weak event, or disposable lifecycle.' Info
Add-CodexPatternFindings $findings $root $files 'Task\.Run\(|async\s+void|_\s*=\s*[^;]*Async\(' 'unsupervised-async' 'Potential unsupervised async work; verify cancellation, exception handling, and lifecycle ownership.' Warning
Add-CodexPatternFindings $findings $root $files 'CancellationToken' 'cancellation-token-present' 'Cancellation token marker found; verify it is forwarded to provider, I/O, and long-running calls.' Info
Add-CodexPatternFindings $findings $root $files 'EnableRowVirtualization="False"|VirtualizingPanel\.IsVirtualizing="False"' 'disabled-virtualization' 'Virtualization appears disabled; verify the dataset is small and bounded.' Warning

Write-CodexFindingReport -Title 'Codex Resource Review' -Findings $findings.ToArray() -MarkdownPath $MarkdownPath
exit (Get-CodexExitCode -Findings $findings.ToArray() -FailOnWarning:$FailOnWarning)
