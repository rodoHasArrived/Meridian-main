[CmdletBinding()]
param(
    [string[]]$Path = @(
        "src/Meridian/bin/Debug/net10.0",
        "src/Meridian/bin/Release/net10.0",
        "src/Meridian.Ui/dashboard/node_modules",
        "src/Meridian.Ui/wwwroot/workstation",
        "artifacts/publish",
        "src",
        "tests"
    ),

    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..\..")).Path

function Get-DirectorySize {
    param([string]$LiteralPath)

    $sum = 0L
    $count = 0

    Get-ChildItem -LiteralPath $LiteralPath -Recurse -Force -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $sum += $_.Length
            $count++
        }

    return [pscustomobject]@{
        Bytes = $sum
        Count = $count
    }
}

$rows = foreach ($entry in $Path) {
    $resolvedPath = if ([System.IO.Path]::IsPathRooted($entry)) {
        [System.IO.Path]::GetFullPath($entry)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $entry))
    }

    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        [pscustomobject]@{
            Path = $entry
            Exists = $false
            FileCount = 0
            MB = 0
            LinkType = $null
        }
        continue
    }

    $item = Get-Item -LiteralPath $resolvedPath -Force
    $size = if ($item.PSIsContainer) {
        Get-DirectorySize -LiteralPath $resolvedPath
    }
    else {
        [pscustomobject]@{
            Bytes = $item.Length
            Count = 1
        }
    }

    [pscustomobject]@{
        Path = $entry
        Exists = $true
        FileCount = $size.Count
        MB = [math]::Round($size.Bytes / 1MB, 2)
        LinkType = $item.LinkType
    }
}

if ($AsJson) {
    $rows | ConvertTo-Json -Depth 3
}
else {
    $rows | Format-Table -AutoSize
}
