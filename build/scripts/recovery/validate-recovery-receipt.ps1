#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Recomputes recovery objectives from a receipt and optional bound completion evidence.
.DESCRIPTION
    A passed backup/restore operation is insufficient. RPO is recoverable-point
    age at simulated loss; RTO ends at explicit operator acceptance after
    reconciliation. Missing or invalid evidence is unproven and exits nonzero.
    Source receipts are never edited. -OutputPath writes a separate evaluated
    receipt; without it the evaluated receipt is emitted on standard output.
    Independent policy caps are 3600/7200 seconds; explicit arguments may tighten
    these limits. Receipt budgets cannot weaken this policy.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReceiptPath,
    [string]$RecoveryEvidencePath,
    [string]$OutputPath,
    [ValidateRange(0.0000001, 3600)][double]$MaximumRpoSeconds = 3600,
    [ValidateRange(0.0000001, 7200)][double]$MaximumRtoSeconds = 7200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'recovery-evidence.ps1')

try {
    $sourcePath = [IO.Path]::GetFullPath($ReceiptPath)
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $destination = [IO.Path]::GetFullPath($OutputPath)
        if ([StringComparer]::OrdinalIgnoreCase.Equals($sourcePath, $destination)) {
            throw 'OutputPath must differ from ReceiptPath; immutable source receipts are never overwritten.'
        }
        if (Test-Path -LiteralPath $destination) {
            throw 'OutputPath already exists; use a new evaluated receipt path.'
        }
    }
    $receipt = Read-RecoveryJson $sourcePath
    if (-not [string]::IsNullOrWhiteSpace($RecoveryEvidencePath)) {
        $completion = Read-RecoveryJson ([IO.Path]::GetFullPath($RecoveryEvidencePath))
        $bindings = @('sourceCommit', 'backupId', 'simulatedLossAtUtc', 'lossDeclaredAtUtc')
        $completionFields = @('reconciliationCompletedAtUtc', 'reconciliationEvidence', 'operatorAcceptedAtUtc', 'operatorAcceptedBy', 'operatorAcceptanceEvidence')
        foreach ($field in $completion.Keys) {
            if ($field -notin ($bindings + $completionFields + @('schemaVersion'))) {
                throw "Completion evidence cannot change immutable receipt field: $field"
            }
        }
        if ($completion.Contains('schemaVersion') -and (-not (Test-RecoveryNumber $completion['schemaVersion']) -or $completion['schemaVersion'] -ne 2)) {
            throw 'Completion evidence schemaVersion must be 2 when supplied.'
        }
        foreach ($field in $bindings) {
            if ($receipt[$field] -isnot [string] -or [string]::IsNullOrWhiteSpace($receipt[$field]) -or
                $completion[$field] -isnot [string] -or -not [StringComparer]::Ordinal.Equals($receipt[$field], $completion[$field])) {
                throw "Completion evidence $field must exactly match the source receipt."
            }
        }
        foreach ($field in $completionFields) {
            if ($completion.Contains($field)) {
                if ($null -ne $receipt[$field] -and ($receipt[$field] -isnot [string] -or $completion[$field] -isnot [string] -or
                    -not [StringComparer]::Ordinal.Equals($receipt[$field], $completion[$field]))) {
                    throw "Completion evidence cannot replace existing $field; retain the original receipt."
                }
                $receipt[$field] = $completion[$field]
            }
        }
    }
    $evaluation = Get-RecoveryObjectiveEvidence -Receipt $receipt -MaximumRpoSeconds $MaximumRpoSeconds -MaximumRtoSeconds $MaximumRtoSeconds
    foreach ($field in $evaluation.Keys) { $receipt[$field] = $evaluation[$field] }
    $json = $receipt | ConvertTo-Json -Depth 32
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $stream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + [Environment]::NewLine)
            $stream.Write($bytes, 0, $bytes.Length)
        }
        finally { $stream.Dispose() }
    }
    Write-Output $json
    if ($evaluation.objectiveStatus -ne 'proven') { exit 1 }
}
catch {
    [Console]::Error.WriteLine("[recovery-evidence] $($_.Exception.Message)")
    exit 1
}
