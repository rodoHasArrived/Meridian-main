#!/usr/bin/env pwsh
<#
.SYNOPSIS
Prepares an isolated, explicitly unaccepted LOT/MARK/SEAM/RECON operator packet.
.DESCRIPTION
Run under a dedicated Windows test user or disposable VM. This script creates scaffolding only:
it does not provision financial records, change credentials, start clients or grant acceptance.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [ValidateRange(1024, 65535)]
    [int]$Port = 18080
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $target) {
    throw 'OutputRoot must be a new directory; an existing installation or acceptance packet is never overwritten.'
}
$candidate = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $candidate -notmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve candidate commit.' }
$dirty = -not [string]::IsNullOrWhiteSpace((& git -C $repoRoot status --porcelain | Out-String))
$populationSource = Join-Path $repoRoot 'docs/testing/evidence/w10-615abde9/population.json'
$populationHash = (Get-FileHash -LiteralPath $populationSource -Algorithm SHA256).Hash.ToLowerInvariant()
$dataRoot = Join-Path $target 'data'
$null = New-Item -ItemType Directory -Path $dataRoot
$configPath = Join-Path $target 'appsettings.json'
@{
    DataRoot = $dataRoot
    DataSource = 'Synthetic'
    TenantScopeEnforcement = 'fail-closed'
    ProviderConnections = @{ Connections = @(); Bindings = @(); RuntimeCredentials = @() }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
Copy-Item -LiteralPath $populationSource -Destination (Join-Path $target 'population-specification.json')
$manifest = [ordered]@{
    schema = 'meridian.next-work-acceptance-preparation.v1'
    preparedAt = [DateTimeOffset]::UtcNow.ToString('O')
    candidate = $candidate
    dirtyWorktree = $dirty
    machine = [Environment]::MachineName
    operatingSystem = [Environment]::OSVersion.VersionString
    hostUrl = "http://localhost:$Port"
    configPath = $configPath
    dataRoot = $dataRoot
    populationSpecificationSha256 = $populationHash
    populationProvisioned = $false
    windowsRuntimeExercised = $false
    accepted = $false
    retainedScope = $null
    workstationVersions = @{ host = $null; browser = $null; wpf = $null }
    implementationOrder = @('LOT', 'MARK', 'SEAM', 'RECON')
    jointLiveSessionOrder = @('LOT', 'SEAM', 'MARK', 'RECON')
    decisions = @('LOT', 'MARK', 'SEAM', 'RECON') | ForEach-Object { @{ criterion = $_; verdict = 'pending'; evidence = @() } }
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $target 'preparation.json') -Encoding utf8NoBOM
$instructions = @"
This directory is preparation, not financial evidence or acceptance.

Use a dedicated Windows test user or disposable Windows x64 VM. WPF also writes per-user
state outside DataRoot. Do not point an existing production desktop session at this packet.

1. Build host and WPF from the same clean candidate commit; replace this packet after source changes.
2. Configure a dedicated nonproduction PostgreSQL database using the maintained persistence runbooks.
   No database credential or connection string is copied into this packet.
3. Provision the population through the owning services following
   docs/testing/w10-mark-seam-operator-acceptance.md. Replace logical keys with returned retained IDs.
   The copied population is a specification; its historical candidate is not this packet's evidence.
4. Launch the host explicitly with --config "$configPath" --mode desktop --http-port $Port.
   Synthetic market data stays labeled. It is not provider or retained-account evidence.
5. Connect browser and WPF to http://localhost:$Port and record their actual versions and scope.
6. Exercise LOT, then SEAM before MARK in the joint live session, then RECON. Retain commands,
   screenshots, evidence hashes, independent approvals and an attributed verdict per criterion.

No financial records have been provisioned. No live run or verdict is recorded by this script.
"@
$instructions | Set-Content -LiteralPath (Join-Path $target 'NEXT-STEPS.txt') -Encoding utf8NoBOM
Write-Output (Join-Path $target 'preparation.json')
