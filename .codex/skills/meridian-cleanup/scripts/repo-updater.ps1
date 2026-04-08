param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")
$updaterPath = Join-Path $repoRoot "build\scripts\ai-repo-updater.py"

if (-not (Test-Path $updaterPath)) {
    Write-Error "Could not find ai-repo-updater.py at $updaterPath"
    exit 1
}

$env:PYTHONIOENCODING = "utf-8"
python $updaterPath @Args
