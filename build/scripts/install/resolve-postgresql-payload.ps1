[CmdletBinding()]
param(
    [string]$ApprovalPath = (Join-Path $PSScriptRoot '../../config/postgresql-payload.json'),
    [string]$OutputRoot = 'artifacts/postgresql-payload',
    [string]$RuntimeIdentifier = 'win-x64'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'postgresql-payload.ps1')
Resolve-PostgreSqlPayload -ApprovalPath $ApprovalPath -OutputRoot $OutputRoot -RuntimeIdentifier $RuntimeIdentifier
