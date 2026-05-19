param(
    [string]$DateStamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd"),
    [string]$OutputRoot = "artifacts/provider-validation/_automation",
    [string]$CalibrationInput = "",
    [string]$CandidateKernelVersion = "",
    [string]$BaselineKernelVersion = "default",
    [switch]$SkipWave1
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bundleRoot = Join-Path (Join-Path $repoRoot $OutputRoot) $DateStamp
New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null

$wave1Script = Join-Path $PSScriptRoot "run-wave1-provider-validation.ps1"
$packetScript = Join-Path $PSScriptRoot "generate-dk1-pilot-parity-packet.ps1"
$signoffScript = Join-Path $PSScriptRoot "prepare-dk1-operator-signoff.ps1"

$summaryPath = Join-Path $bundleRoot "wave1-validation-summary.json"
$packetPath = Join-Path $bundleRoot "dk1-pilot-parity-packet.json"
$signoffPath = Join-Path $bundleRoot "dk1-operator-signoff.json"
$calibrationReportPath = Join-Path $bundleRoot "provider-degradation-calibration.md"
$governancePath = Join-Path $bundleRoot "provider-degradation-governance.json"
$bundlePath = Join-Path $bundleRoot "provider-validation-evidence-bundle.json"

if (-not $SkipWave1) {
    & $wave1Script -DateStamp $DateStamp -OutputRoot $OutputRoot
}

& $packetScript -DateStamp $DateStamp -OutputRoot $OutputRoot -SummaryJsonPath $summaryPath
& $signoffScript -OutputPath $signoffPath -PacketPath $packetPath -Validate

$calibration = $null
if (-not [string]::IsNullOrWhiteSpace($CalibrationInput)) {
    if ([string]::IsNullOrWhiteSpace($CandidateKernelVersion)) {
        $CandidateKernelVersion = "kernel-$((Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss'))"
    }

    dotnet run --project src/Meridian/Meridian.csproj -- --calibrate-provider-degradation --calibration-input $CalibrationInput --candidate-kernel-version $CandidateKernelVersion --baseline-kernel-version $BaselineKernelVersion --calibration-report $calibrationReportPath --governance-output $governancePath | Out-Host
    $calibration = Get-Content -Raw -LiteralPath $governancePath | ConvertFrom-Json
}

$summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
$packet = Get-Content -Raw -LiteralPath $packetPath | ConvertFrom-Json
$signoff = Get-Content -Raw -LiteralPath $signoffPath | ConvertFrom-Json

if (-not $summary.generatedAtUtc -or -not $summary.result) { throw "Summary schema invalid: missing generatedAtUtc/result." }
if (-not $packet.generatedAtUtc -or -not $packet.status) { throw "Parity packet schema invalid: missing generatedAtUtc/status." }
if (-not $signoff.packetReview -or -not $signoff.packetReview.validForOperatorReview) { throw "Sign-off schema invalid or packet review not ready." }

$summaryAt = [DateTimeOffset]::Parse($summary.generatedAtUtc)
$packetAt = [DateTimeOffset]::Parse($packet.generatedAtUtc)
if ($packetAt -lt $summaryAt) { throw "Stale packet: packet timestamp predates summary timestamp." }
if ($summary.result -eq "passed" -and $packet.status -ne "ready-for-operator-review") { throw "Gate mismatch: summary passed but packet not ready-for-operator-review." }

$promotion = if ($null -eq $calibration) {
    [ordered]@{ status = "not-run"; recommendation = "Calibration input not provided." }
} else {
    [ordered]@{
        status = if ($calibration.approved) { "candidate-approved" } else { "candidate-rejected" }
        baselineKernelVersion = $BaselineKernelVersion
        candidateKernelVersion = $CandidateKernelVersion
        calibrationPass = [bool]$calibration.calibrationPass
        approved = [bool]$calibration.approved
        recommendation = if ($calibration.approved) { "Promote candidate kernel." } else { "Retain baseline kernel and rerun with updated calibration dataset." }
    }
}

$bundle = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    dateStamp = $DateStamp
    summaryPath = "artifacts/provider-validation/_automation/$DateStamp/wave1-validation-summary.json"
    parityPacketPath = "artifacts/provider-validation/_automation/$DateStamp/dk1-pilot-parity-packet.json"
    signoffPath = "artifacts/provider-validation/_automation/$DateStamp/dk1-operator-signoff.json"
    calibrationGovernancePath = if (Test-Path $governancePath) { "artifacts/provider-validation/_automation/$DateStamp/provider-degradation-governance.json" } else { $null }
    schemaVersion = "provider-validation-bundle/v1"
    gateOutcome = [ordered]@{
        wave1Result = [string]$summary.result
        dk1Status = [string]$packet.status
        operatorSignoffValidForDk1Exit = [bool]$signoff.validForDk1Exit
    }
    promotionPosture = $promotion
}

$bundle | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $bundlePath -Encoding utf8
Write-Host "Evidence bundle created: $bundlePath"
