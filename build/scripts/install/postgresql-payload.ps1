# Shared approval, staging, and integrity checks for bundled PostgreSQL.
Set-StrictMode -Version Latest
$PostgreSqlRequiredTools = @('postgres.exe', 'pg_ctl.exe', 'initdb.exe', 'psql.exe', 'pg_dump.exe', 'pg_restore.exe')

function Read-PostgreSqlApproval {
    param([Parameter(Mandatory)][string]$ApprovalPath)
    $approval = Get-Content -LiteralPath $ApprovalPath -Raw -ErrorAction Stop | ConvertFrom-Json
    if ($approval.schemaVersion -ne 1 -or $approval.runtime -cne 'win-x64' -or
        $approval.version -cnotmatch '^[1-9][0-9]*\.[0-9]+$' -or
        $approval.source.kind -cne 'github-hosted-runner' -or
        [string]::IsNullOrWhiteSpace($approval.source.path) -or
        $approval.source.reference -cnotmatch '^https://github.com/actions/runner-images/blob/[a-f0-9]{40}/images/windows/Windows2025-Readme.md#postgresql$' -or
        (@($approval.components) -join ',') -cne 'bin,lib,share' -or
        (@($approval.noticeFiles) -join ',') -cne 'commandlinetools_3rd_party_licenses.txt,server_license.txt') {
        throw 'Invalid PostgreSQL payload approval. Review build/config/postgresql-payload.json.'
    }
    return $approval
}

function Get-PostgreSqlToolVersion {
    param([Parameter(Mandatory)][string]$Path)
    $output = @(& $Path --version 2>&1)
    $toolName = [regex]::Escape([IO.Path]::GetFileNameWithoutExtension($Path))
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1 -or
        [string]$output[0] -cnotmatch "^$toolName \(PostgreSQL\) ([0-9]+\.[0-9]+)$") {
        throw "Cannot identify PostgreSQL tool version: $Path ($output)"
    }
    return $Matches[1]
}

function Assert-PostgreSqlLayout {
    param([string]$PayloadPath, [object]$Approval)
    foreach ($component in $Approval.components) {
        if (-not (Test-Path -LiteralPath (Join-Path $PayloadPath $component) -PathType Container)) { throw "Missing approved PostgreSQL component: $component" }
    }
    foreach ($notice in $Approval.noticeFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $PayloadPath $notice) -PathType Leaf)) { throw "Missing PostgreSQL distribution notice: $notice" }
    }
}

function Assert-PostgreSqlTools {
    param([string]$PayloadPath, [string]$Version)
    foreach ($name in $PostgreSqlRequiredTools) {
        $path = Join-Path $PayloadPath "bin/$name"
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing PostgreSQL tool: $path" }
        $actual = Get-PostgreSqlToolVersion -Path $path
        if ($actual -cne $Version) { throw "PostgreSQL version mismatch for ${path}: approved $Version, found $actual. No fallback is allowed." }
        [ordered]@{ path = "bin/$name"; version = $actual }
    }
}

function Get-PostgreSqlPayloadFiles {
    param([Parameter(Mandatory)][string]$PayloadPath)
    $root = (Get-Item -LiteralPath $PayloadPath -ErrorAction Stop).FullName
    $entries = @(Get-ChildItem -LiteralPath $root -Recurse -Force -ErrorAction Stop)
    if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
        throw "PostgreSQL payload must not contain links or reparse points: $root"
    }
    $byPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in $entries | Where-Object { -not $_.PSIsContainer }) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        if ($relative -match '[\r\n]') { throw 'PostgreSQL payload contains an unsafe file name.' }
        $byPath.Add($relative, [ordered]@{
            path = $relative
            sizeBytes = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    $paths = [string[]]@($byPath.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    foreach ($path in $paths) { $byPath[$path] }
}

function Get-PostgreSqlPayloadHash {
    param([Parameter(Mandatory)][object[]]$Files)
    # UTF-8, no BOM, ordinal path order, lowercase SHA256 + two spaces + path + LF.
    $byPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in $Files) { $byPath.Add($file.path, $file) }
    $paths = [string[]]@($byPath.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $canonical = ($paths | ForEach-Object { "$($byPath[$_].sha256)  $_`n" }) -join ''
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
}

function Resolve-PostgreSqlPayload {
    param(
        [Parameter(Mandatory)][string]$ApprovalPath,
        [Parameter(Mandatory)][string]$OutputRoot,
        [string]$RuntimeIdentifier = 'win-x64'
    )
    $approval = Read-PostgreSqlApproval -ApprovalPath $ApprovalPath
    if ($RuntimeIdentifier -cne $approval.runtime) { throw "No approved PostgreSQL payload for $RuntimeIdentifier." }
    $source = $approval.source.path
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Approved PostgreSQL source is unavailable: $source. No fallback is allowed." }
    Assert-PostgreSqlLayout -PayloadPath $source -Approval $approval
    $tools = @(Assert-PostgreSqlTools -PayloadPath $source -Version $approval.version)
    # Validate the complete source before creating any output. Only immutable distribution
    # components are bundled; the runner's data cluster and service configuration are excluded.
    $sourceFiles = @(
      foreach ($component in $approval.components) {
        $item = Get-Item -LiteralPath (Join-Path $source $component)
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'PostgreSQL source component must not be a link.' }
        foreach ($file in Get-PostgreSqlPayloadFiles -PayloadPath $item.FullName) {
            $file.path = "$component/$($file.path)"
            $file
        }
      }
      foreach ($notice in $approval.noticeFiles) {
        $item = Get-Item -LiteralPath (Join-Path $source $notice)
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'PostgreSQL distribution notice must not be a link.' }
        [ordered]@{
            path = $notice
            sizeBytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
      }
    )
    $sourceHash = Get-PostgreSqlPayloadHash -Files $sourceFiles
    $target = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $RuntimeIdentifier
    $receiptPath = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "$RuntimeIdentifier-payload.json"
    if ((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath $receiptPath)) {
        throw "PostgreSQL staging output already exists: $target. Use a clean output directory."
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    try {
        foreach ($component in $approval.components) {
            Copy-Item -LiteralPath (Join-Path $source $component) -Destination $target -Recurse -Force -ErrorAction Stop
        }
        foreach ($notice in $approval.noticeFiles) {
            Copy-Item -LiteralPath (Join-Path $source $notice) -Destination $target -ErrorAction Stop
        }
        $files = @(Get-PostgreSqlPayloadFiles -PayloadPath $target)
        if ((Get-PostgreSqlPayloadHash -Files $files) -cne $sourceHash) {
            throw 'PostgreSQL source changed during staging or copied bytes do not match.'
        }
        $null = @(Assert-PostgreSqlTools -PayloadPath $target -Version $approval.version)
        $receipt = [ordered]@{
            schemaVersion = 1
            runtime = $RuntimeIdentifier
            version = $approval.version
            source = $approval.source
            approvalSha256 = (Get-FileHash -LiteralPath $ApprovalPath -Algorithm SHA256).Hash.ToLowerInvariant()
            payloadSha256 = Get-PostgreSqlPayloadHash -Files $files
            files = $files
            tools = $tools
            runner = [ordered]@{ imageOS = [string]$env:ImageOS; imageVersion = [string]$env:ImageVersion }
        }
        $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
        Write-Host "[postgresql-payload] Approved $($approval.version) / $RuntimeIdentifier; SHA256 $($receipt.payloadSha256); receipt $receiptPath"
        return $receiptPath
    } catch {
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $receiptPath -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Assert-PostgreSqlPayload {
    param(
        [Parameter(Mandatory)][string]$PayloadRoot,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$ApprovalPath
    )
    $approval = Read-PostgreSqlApproval -ApprovalPath $ApprovalPath
    if ($RuntimeIdentifier -cne $approval.runtime) { throw "No approved PostgreSQL payload for $RuntimeIdentifier." }
    $receiptPath = Join-Path $PayloadRoot "$RuntimeIdentifier-payload.json"
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -ErrorAction Stop | ConvertFrom-Json
    if ($receipt.runner.imageOS -isnot [string] -or $receipt.runner.imageVersion -isnot [string]) {
        throw 'PostgreSQL payload receipt runner identity must contain strings.'
    }
    $approvalHash = (Get-FileHash -LiteralPath $ApprovalPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($receipt.schemaVersion -ne 1 -or $receipt.runtime -cne $RuntimeIdentifier -or
        $receipt.version -cne $approval.version -or $receipt.approvalSha256 -cne $approvalHash -or
        $receipt.source.kind -cne $approval.source.kind -or $receipt.source.path -cne $approval.source.path -or
        $receipt.source.reference -cne $approval.source.reference) { throw 'PostgreSQL payload receipt does not match the approved version and source.' }
    $payloadPath = Join-Path $PayloadRoot $RuntimeIdentifier
    Assert-PostgreSqlLayout -PayloadPath $payloadPath -Approval $approval
    $files = @(Get-PostgreSqlPayloadFiles -PayloadPath $payloadPath)
    if ($files.Count -ne @($receipt.files).Count -or
        (Get-PostgreSqlPayloadHash -Files $files) -cne $receipt.payloadSha256 -or
        (Get-PostgreSqlPayloadHash -Files @($receipt.files)) -cne $receipt.payloadSha256) {
        throw 'PostgreSQL payload hash mismatch. Resolve a clean approved payload before packaging.'
    }
    for ($i = 0; $i -lt $files.Count; $i++) {
        if ($files[$i].path -cne $receipt.files[$i].path -or $files[$i].sizeBytes -ne $receipt.files[$i].sizeBytes) {
            throw 'PostgreSQL payload file inventory mismatch.'
        }
    }
    $actualTools = @(Assert-PostgreSqlTools -PayloadPath $payloadPath -Version $approval.version)
    if (@($receipt.tools).Count -ne $actualTools.Count) { throw 'PostgreSQL payload tool inventory mismatch.' }
    for ($i = 0; $i -lt $actualTools.Count; $i++) {
        if ($receipt.tools[$i].path -cne $actualTools[$i].path -or $receipt.tools[$i].version -cne $actualTools[$i].version) {
            throw 'PostgreSQL payload tool inventory mismatch.'
        }
    }
    return $receiptPath
}
