param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ForwardedArgs
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$canonicalScript = Join-Path $repoRoot 'scripts\dev\robinhood-options-smoke.ps1'
& $canonicalScript @ForwardedArgs
exit $LASTEXITCODE
