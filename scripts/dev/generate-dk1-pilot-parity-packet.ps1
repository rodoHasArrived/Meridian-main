param(
    [string]$DateStamp = (Get-Date).ToString("yyyy-MM-dd"),
    [string]$OutputRoot = "artifacts/provider-validation/_automation",
    [string]$SummaryJsonPath = "",
    [switch]$AllowFailedSummary
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$summaryDir = Join-Path (Join-Path $repoRoot $OutputRoot) $DateStamp

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $summaryDir "wave1-validation-summary.json"
}
else {
    $SummaryJsonPath = [System.IO.Path]::GetFullPath($SummaryJsonPath)
    $summaryDir = Split-Path -Parent $SummaryJsonPath
}

if (-not (Test-Path -LiteralPath $SummaryJsonPath)) {
    throw "Wave 1 validation summary was not found: $SummaryJsonPath"
}

$summary = Get-Content -Raw -LiteralPath $SummaryJsonPath | ConvertFrom-Json

$requiredSamples = @(
    [ordered]@{
        id = "DK1-ALPACA-QUOTE-GOLDEN"
        provider = "Alpaca"
        requiredStep = "Alpaca core provider confidence"
        requiredEvidenceAnchors = @(
            "tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json",
            "AlpacaQuotePipelineGoldenTests"
        )
    },
    [ordered]@{
        id = "DK1-ALPACA-PARSER-EDGE-CASES"
        provider = "Alpaca"
        requiredStep = "Alpaca core provider confidence"
        requiredEvidenceAnchors = @(
            "AlpacaMessageParsingTests",
            "AlpacaQuoteRoutingTests",
            "AlpacaCredentialAndReconnectTests"
        )
    },
    [ordered]@{
        id = "DK1-ROBINHOOD-SUPPORTED-SURFACE"
        provider = "Robinhood"
        requiredStep = "Robinhood supported surface"
        requiredEvidenceAnchors = @(
            "RobinhoodMarketDataClientTests",
            "RobinhoodBrokerageGatewayTests",
            "artifacts/provider-validation/robinhood/2026-04-09/manifest.json"
        )
    },
    [ordered]@{
        id = "DK1-YAHOO-HISTORICAL-FALLBACK"
        provider = "Yahoo"
        requiredStep = "Yahoo historical-only core provider"
        requiredEvidenceAnchors = @(
            "YahooFinanceHistoricalDataProviderTests",
            "YahooFinanceIntradayContractTests"
        )
    }
)

$requiredDocs = @(
    [ordered]@{
        name = "DK1 pilot parity runbook"
        path = "docs/status/dk1-pilot-parity-runbook.md"
        gate = "parity"
        requiredTokens = @(
            "DK1-ALPACA-QUOTE-GOLDEN",
            "DK1-ALPACA-PARSER-EDGE-CASES",
            "DK1-ROBINHOOD-SUPPORTED-SURFACE",
            "DK1-YAHOO-HISTORICAL-FALLBACK",
            "ready-for-operator-review"
        )
    },
    [ordered]@{
        name = "DK1 trust rationale mapping"
        path = "docs/status/dk1-trust-rationale-mapping.md"
        gate = "explainability"
        requiredTokens = @(
            "signalSource",
            "reasonCode",
            "recommendedAction",
            "HEALTHY_BASELINE",
            "PROVIDER_STREAM_DEGRADED",
            "RECONNECT_INSTABILITY",
            "ERROR_RATE_SPIKE",
            "LATENCY_REGRESSION",
            "PARITY_DRIFT_DETECTED",
            "DATA_COMPLETENESS_GAP",
            "CALIBRATION_STALE"
        )
    },
    [ordered]@{
        name = "DK1 baseline trust thresholds"
        path = "docs/status/dk1-baseline-trust-thresholds.md"
        gate = "calibration"
        requiredTokens = @(
            "Composite trust score",
            "Connection stability score",
            "Error-rate score",
            "Latency score",
            "Reconnect score",
            "False-positive / false-negative review process",
            "FP rate",
            "FN rate",
            "Exit criterion for DK1 calibration gate"
        )
    },
    [ordered]@{
        name = "Provider validation matrix"
        path = "docs/status/provider-validation-matrix.md"
        gate = "parity"
        requiredTokens = @(
            "Alpaca core provider confidence",
            "Robinhood supported surface",
            "Yahoo historical and fallback confidence",
            "Checkpoint reliability",
            "Parquet L2 flush behavior",
            "pilotReplaySampleSet"
        )
    }
)

$script:SummarySteps = if ($summary.PSObject.Properties.Name -contains "steps") { @($summary.steps) } else { @() }

function ConvertTo-RelativePath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($repoRoot.Length + 1).Replace('\', '/')
    }

    return $fullPath
}

function Get-StepStatus {
    param([Parameter(Mandatory)][string]$Name)

    $step = @($script:SummarySteps | Where-Object { $_.name -eq $Name } | Select-Object -First 1)
    if ($step.Count -eq 0) {
        return "missing"
    }

    return [string]$step[0].status
}

function Get-ObjectPropertyValue {
    param(
        [object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $Name)) {
        return $null
    }

    return $Object.PSObject.Properties[$Name].Value
}

function Get-SampleMissingRequirements {
    param(
        [object]$Sample,
        [Parameter(Mandatory)]$RequiredSample
    )

    $missingRequirements = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Sample) {
        $missingRequirements.Add("sample")
        return $missingRequirements.ToArray()
    }

    $provider = Get-ObjectPropertyValue -Object $Sample -Name "provider"
    $automationStep = Get-ObjectPropertyValue -Object $Sample -Name "automationStep"
    $lane = Get-ObjectPropertyValue -Object $Sample -Name "lane"
    $sampleWindow = Get-ObjectPropertyValue -Object $Sample -Name "sampleWindow"
    $sampleUniverseValue = Get-ObjectPropertyValue -Object $Sample -Name "sampleUniverse"
    $evidenceAnchorValue = Get-ObjectPropertyValue -Object $Sample -Name "evidenceAnchors"
    $acceptanceCheck = Get-ObjectPropertyValue -Object $Sample -Name "acceptanceCheck"

    if ([string]$provider -ne [string]$RequiredSample.provider) {
        $missingRequirements.Add("provider:$($RequiredSample.provider)")
    }

    if ([string]$automationStep -ne [string]$RequiredSample.requiredStep) {
        $missingRequirements.Add("automationStep:$($RequiredSample.requiredStep)")
    }

    if ([string]::IsNullOrWhiteSpace([string]$lane)) {
        $missingRequirements.Add("lane")
    }

    if ([string]::IsNullOrWhiteSpace([string]$sampleWindow)) {
        $missingRequirements.Add("sampleWindow")
    }

    $sampleUniverse = @($sampleUniverseValue | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($sampleUniverse.Count -eq 0) {
        $missingRequirements.Add("sampleUniverse")
    }

    $evidenceAnchors = @($evidenceAnchorValue | ForEach-Object { [string]$_ })
    foreach ($anchor in @($RequiredSample.requiredEvidenceAnchors)) {
        if ($evidenceAnchors -notcontains $anchor) {
            $missingRequirements.Add("evidenceAnchor:$anchor")
        }
    }

    if ([string]::IsNullOrWhiteSpace([string]$acceptanceCheck)) {
        $missingRequirements.Add("acceptanceCheck")
    }

    return $missingRequirements.ToArray()
}

function Get-SampleStatus {
    param(
        [Parameter(Mandatory)][bool]$Observed,
        [Parameter(Mandatory)][string]$StepStatus,
        [string[]]$MissingRequirements = @()
    )

    if (-not $Observed) {
        return "missing"
    }

    if ($StepStatus -ne "passed") {
        return "blocked"
    }

    if ($MissingRequirements.Count -gt 0) {
        return "incomplete"
    }

    return "ready"
}

function Get-MissingDocumentTokens {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$RequiredTokens
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return @($RequiredTokens)
    }

    $content = Get-Content -Raw -LiteralPath $Path
    $missingTokens = New-Object System.Collections.Generic.List[string]
    foreach ($token in $RequiredTokens) {
        if ($content.IndexOf($token, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            $missingTokens.Add($token)
        }
    }

    return $missingTokens.ToArray()
}

function Get-DocumentStatus {
    param(
        [Parameter(Mandatory)][bool]$Exists,
        [string[]]$MissingRequirements = @()
    )

    if (-not $Exists) {
        return "missing"
    }

    if ($MissingRequirements.Count -gt 0) {
        return "incomplete"
    }

    return "validated"
}

$observedSamples = if ($summary.PSObject.Properties.Name -contains "pilotReplaySampleSet") { @($summary.pilotReplaySampleSet) } else { @() }
$observedSampleIds = @($observedSamples | ForEach-Object { [string]$_.id })
$missingSamples = @($requiredSamples | Where-Object { $observedSampleIds -notcontains $_.id })
$duplicateSamples = @(
    $observedSampleIds |
        Group-Object |
        Where-Object { $_.Count -gt 1 } |
        ForEach-Object { $_.Name }
)

$sampleReviews = foreach ($required in $requiredSamples) {
    $sample = @($observedSamples | Where-Object { $_.id -eq $required.id } | Select-Object -First 1)
    $observed = $sample.Count -gt 0
    $observedSample = if ($observed) { $sample[0] } else { $null }
    $stepStatus = Get-StepStatus -Name $required.requiredStep
    $missingRequirements = @(Get-SampleMissingRequirements -Sample $observedSample -RequiredSample $required)
    [ordered]@{
        id = $required.id
        provider = $required.provider
        requiredStep = $required.requiredStep
        stepStatus = $stepStatus
        observed = $observed
        status = Get-SampleStatus -Observed $observed -StepStatus $stepStatus -MissingRequirements $missingRequirements
        missingRequirements = $missingRequirements
        evidenceAnchors = if ($observed) { @(Get-ObjectPropertyValue -Object $observedSample -Name "evidenceAnchors") } else { @() }
        acceptanceCheck = if ($observed) { [string](Get-ObjectPropertyValue -Object $observedSample -Name "acceptanceCheck") } else { "" }
    }
}

$docReviews = foreach ($doc in $requiredDocs) {
    $absolutePath = Join-Path $repoRoot $doc.path
    $exists = Test-Path -LiteralPath $absolutePath
    $missingRequirements = @(Get-MissingDocumentTokens -Path $absolutePath -RequiredTokens @($doc.requiredTokens))
    [ordered]@{
        name = $doc.name
        gate = $doc.gate
        path = $doc.path
        exists = $exists
        status = Get-DocumentStatus -Exists $exists -MissingRequirements $missingRequirements
        missingRequirements = $missingRequirements
    }
}

$summaryResult = if ($summary.PSObject.Properties.Name -contains "result") { [string]$summary.result } else { "missing" }
$failedSteps = @($script:SummarySteps | Where-Object { $_.status -ne "passed" } | ForEach-Object { [string]$_.name })
$missingDocs = @($docReviews | Where-Object { -not $_.exists } | ForEach-Object { [string]$_.path })
$incompleteDocs = @($docReviews | Where-Object { $_.status -eq "incomplete" })
$incompleteSamples = @($sampleReviews | Where-Object { $_.status -eq "incomplete" })

$trustDocReview = @($docReviews | Where-Object { $_.path -eq "docs/status/dk1-trust-rationale-mapping.md" } | Select-Object -First 1)
$thresholdDocReview = @($docReviews | Where-Object { $_.path -eq "docs/status/dk1-baseline-trust-thresholds.md" } | Select-Object -First 1)
$trustContractStatus = if ($trustDocReview.Count -eq 0) { "missing" } else { [string]$trustDocReview[0]["status"] }
$thresholdContractStatus = if ($thresholdDocReview.Count -eq 0) { "missing" } else { [string]$thresholdDocReview[0]["status"] }
$trustContractMissingRequirements = if ($trustDocReview.Count -eq 0) { @() } else { @($trustDocReview[0]["missingRequirements"]) }
$thresholdContractMissingRequirements = if ($thresholdDocReview.Count -eq 0) { @() } else { @($thresholdDocReview[0]["missingRequirements"]) }

$blockers = New-Object System.Collections.Generic.List[string]
if ($summaryResult -ne "passed") {
    $blockers.Add("Wave 1 validation summary result is '$summaryResult'.")
}
if ($script:SummarySteps.Count -eq 0) {
    $blockers.Add("Wave 1 validation summary has no step results.")
}
foreach ($sample in $missingSamples) {
    $blockers.Add("Missing required DK1 sample '$($sample.id)'.")
}
foreach ($sampleId in $duplicateSamples) {
    $blockers.Add("Duplicate DK1 sample id '$sampleId'.")
}
foreach ($sample in $incompleteSamples) {
    $missingList = @($sample.missingRequirements) -join ", "
    $blockers.Add("Required DK1 sample '$($sample.id)' is incomplete: missing $missingList.")
}
foreach ($stepName in $failedSteps) {
    $blockers.Add("Validation step did not pass: $stepName.")
}
foreach ($docPath in $missingDocs) {
    $blockers.Add("Required DK1 evidence document is missing: $docPath.")
}
foreach ($doc in $incompleteDocs) {
    $missingList = @($doc.missingRequirements) -join ", "
    $blockers.Add("Required DK1 evidence document is incomplete: $($doc.path) missing $missingList.")
}

$packetStatus = if ($blockers.Count -eq 0) { "ready-for-operator-review" } else { "blocked" }

$packet = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    dateStamp = if ($summary.PSObject.Properties.Name -contains "dateStamp") { [string]$summary.dateStamp } else { $DateStamp }
    sourceSummary = ConvertTo-RelativePath -Path $SummaryJsonPath
    sourceResult = $summaryResult
    status = $packetStatus
    requiredPilotSamples = $requiredSamples
    sampleReview = [ordered]@{
        requiredCount = $requiredSamples.Count
        observedCount = $observedSamples.Count
        missingSampleIds = @($missingSamples | ForEach-Object { [string]$_.id })
        duplicateSampleIds = $duplicateSamples
        samples = @($sampleReviews)
    }
    trustRationaleContract = [ordered]@{
        documentPath = "docs/status/dk1-trust-rationale-mapping.md"
        requiredPayloadFields = @("signalSource", "reasonCode", "recommendedAction")
        requiredReasonCodes = @(
            "HEALTHY_BASELINE",
            "PROVIDER_STREAM_DEGRADED",
            "RECONNECT_INSTABILITY",
            "ERROR_RATE_SPIKE",
            "LATENCY_REGRESSION",
            "PARITY_DRIFT_DETECTED",
            "DATA_COMPLETENESS_GAP",
            "CALIBRATION_STALE"
        )
        status = $trustContractStatus
        missingRequirements = $trustContractMissingRequirements
    }
    baselineThresholdContract = [ordered]@{
        documentPath = "docs/status/dk1-baseline-trust-thresholds.md"
        requiredMetrics = @(
            "Composite trust score",
            "Connection stability score",
            "Error-rate score",
            "Latency score",
            "Reconnect score"
        )
        fpFnReviewRequired = $true
        status = $thresholdContractStatus
        missingRequirements = $thresholdContractMissingRequirements
    }
    evidenceDocuments = @($docReviews)
    operatorSignoff = [ordered]@{
        requiredOwners = @("Data Operations", "Provider Reliability", "Trading")
        status = "pending"
        requiredBeforeDk1Exit = $true
    }
    blockers = @($blockers)
}

$jsonPath = Join-Path $summaryDir "dk1-pilot-parity-packet.json"
$mdPath = Join-Path $summaryDir "dk1-pilot-parity-packet.md"
$packet | ConvertTo-Json -Depth 7 | Set-Content -Path $jsonPath

$md = @(
    "# DK1 Pilot Parity Packet",
    "",
    "- Generated: $($packet.generatedAtUtc)",
    "- Source summary: ``$($packet.sourceSummary)``",
    "- Source result: $($packet.sourceResult)",
    "- Packet status: $($packet.status)",
    "",
    "## Pilot Sample Review",
    "",
    "| Sample ID | Provider | Required step | Step status | Review status | Missing requirements | Evidence anchors |",
    "|---|---|---|---|---|---|---|"
)

foreach ($sample in $packet.sampleReview.samples) {
    $anchorValues = @($sample.evidenceAnchors)
    $anchors = if ($anchorValues.Count -eq 0) { "Missing" } else { $anchorValues -join "<br>" }
    $missingRequirements = @($sample.missingRequirements)
    $missingText = if ($missingRequirements.Count -eq 0) { "none" } else { $missingRequirements -join "<br>" }
    $md += "| $($sample.id) | $($sample.provider) | $($sample.requiredStep) | $($sample.stepStatus) | $($sample.status) | $missingText | $anchors |"
}

$md += @(
    "",
    "## Evidence Documents",
    "",
    "| Document | Gate | Status | Missing requirements | Path |",
    "|---|---|---|---|---|"
)

foreach ($doc in $packet.evidenceDocuments) {
    $missingRequirements = @($doc.missingRequirements)
    $missingText = if ($missingRequirements.Count -eq 0) { "none" } else { $missingRequirements -join "<br>" }
    $md += "| $($doc.name) | $($doc.gate) | $($doc.status) | $missingText | ``$($doc.path)`` |"
}

$requiredReasonCodesText = (@($packet.trustRationaleContract.requiredReasonCodes) | ForEach-Object { "``$_``" }) -join "; "
$requiredMetricsText = (@($packet.baselineThresholdContract.requiredMetrics) | ForEach-Object { "``$_``" }) -join "; "

$md += @(
    "",
    "## Explainability Contract",
    "",
    "- Status: $($packet.trustRationaleContract.status)",
    "- Required alert payload fields: ``signalSource``, ``reasonCode``, ``recommendedAction``",
    "- Required reason codes: $requiredReasonCodesText",
    "",
    "## Calibration Contract",
    "",
    "- Status: $($packet.baselineThresholdContract.status)",
    "- Required metrics: $requiredMetricsText",
    "- FP/FN review required before DK1 calibration pass: $($packet.baselineThresholdContract.fpFnReviewRequired)",
    "",
    "## Operator Sign-off",
    "",
    "- Required owners: $($packet.operatorSignoff.requiredOwners -join ', ')",
    "- Status: $($packet.operatorSignoff.status)",
    "",
    "## Blockers",
    ""
)

if ($packet.blockers.Count -eq 0) {
    $md += "- none"
}
else {
    foreach ($blocker in $packet.blockers) {
        $md += "- $blocker"
    }
}

$md -join [Environment]::NewLine | Set-Content -Path $mdPath

Write-Host "DK1 pilot parity packet written to:"
Write-Host "  $jsonPath"
Write-Host "  $mdPath"

if ($packet.status -eq "blocked" -and -not $AllowFailedSummary) {
    throw "DK1 pilot parity packet is blocked: $($packet.blockers -join '; ')"
}
