param(
    [string]$Configuration = "Release",
    [string]$DateStamp = (Get-Date).ToString("yyyy-MM-dd"),
    [string]$OutputRoot = "artifacts/provider-validation/_automation",
    [string]$OperatorSignoffPath = "",
    [string]$CheckpointPath = "",
    [string[]]$ForceCheckpointStep = @(),
    [switch]$AllowCheckpointInputMismatch
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharedPreflight.ps1')

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $PSScriptRoot "SharedCheckpoint.ps1")
. (Join-Path $PSScriptRoot "ProviderValidationEvidence.ps1")
$summaryDir = Join-Path $repoRoot $OutputRoot | Join-Path -ChildPath $DateStamp
New-Item -ItemType Directory -Force -Path $summaryDir | Out-Null
$preflight = Invoke-MeridianPreflight `
    -Scenario 'wave1-provider-validation' `
    -RequiredCommands @('dotnet', 'pwsh') `
    -RequiredPaths @(
        (Join-Path $repoRoot "tests/Meridian.Tests/Meridian.Tests.csproj"),
        (Join-Path $repoRoot "scripts/dev/generate-dk1-pilot-parity-packet.ps1")
    ) `
    -WritableDirectories @($summaryDir) `
    -EmitJson `
    -AllowWarnings

if (-not [string]::IsNullOrWhiteSpace($OperatorSignoffPath) -and -not (Test-Path -LiteralPath $OperatorSignoffPath)) {
    $preflight.blockingChecks += [pscustomobject]@{
        check = "path.operatorSignoff"
        message = "Operator sign-off file was not found: $OperatorSignoffPath"
        recommendation = "Provide a valid operator sign-off path or omit -OperatorSignoffPath."
    }
    $preflight.status = 'blocked'
    $preflight.nextAction = 'Resolve blocking checks and rerun preflight.'
}

if ($preflight.status -eq 'blocked') {
    $preflightPath = Join-Path $summaryDir 'preflight.json'
    $preflight | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $preflightPath -Encoding utf8
    throw "Preflight failed. See '$preflightPath' for diagnostics."
}

$testProject = "tests/Meridian.Tests/Meridian.Tests.csproj"
$commonTestArgs = @(
    "test",
    $testProject,
    "-c", $Configuration,
    "--no-build",
    "--nologo",
    "--verbosity", "minimal",
    "/p:EnableWindowsTargeting=true",
    "-maxcpucount:1"
)

# One inventory drives both executable filters and the provider evidence receipt.
$providerEvidence = [ordered]@{
    Alpaca = @(
        "AlpacaBrokerageGatewayTests", "AlpacaCorporateActionProviderTests",
        "AlpacaCredentialAndReconnectTests", "AlpacaHistoricalDataProviderTests",
        "AlpacaMessageParsingTests", "AlpacaQuotePipelineGoldenTests", "AlpacaQuoteRoutingTests",
        "ProviderFactoryCredentialContextTests",
        "ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam"
    )
    Robinhood = @(
        "RobinhoodBrokerageGatewayTests", "RobinhoodMarketDataClientTests",
        "RobinhoodHistoricalDataProviderTests", "RobinhoodSymbolSearchProviderTests",
        "HostedBrokerageGatewayRegistrationTests",
        "ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam"
    )
    Yahoo = @(
        "YahooFinanceHistoricalDataProviderTests", "YahooFinanceIntradayContractTests",
        "ProviderCapabilityDescriptorCatalogTests.Scenario_UnsupportedYahooStreaming_RuntimeCatalogKeepsHistoricalFallbackOnly"
    )
}

$crossCuttingEvidence = [ordered]@{
    Checkpoint = @("BackfillStatusStoreTests", "ParallelBackfillServiceTests", "GapBackfillServiceTests", "CheckpointEndpointTests")
    Execution = @(
        "PaperSessionPersistenceServiceTests.TradeStationExecutionSlice_CreateUpdateCancelAndFillReconciliation_ProducesDeterministicCanonicalEvidence",
        "PaperSessionPersistenceServiceTests.TradeStationExecutionSlice_DelayedOutOfOrderEvents_RemainsIdempotentAndDeterministic"
    )
    Parquet = @("ParquetStorageSinkTests", "ParquetConversionServiceTests")
}

$steps = @(
    [ordered]@{
        Name = "Meridian.Tests build"
        Kind = "build"
        Command = @(
            "dotnet",
            "build",
            $testProject,
            "-c", $Configuration,
            "--nologo",
            "--verbosity", "minimal",
            "/p:EnableWindowsTargeting=true",
            "-maxcpucount:1"
        )
    },
    [ordered]@{
        Name = "Alpaca core provider confidence"
        Kind = "test"
        Evidence = $providerEvidence.Alpaca
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            (($providerEvidence.Alpaca | ForEach-Object { "FullyQualifiedName~$_" }) -join "|")
        )
    },
    [ordered]@{
        Name = "Robinhood supported surface"
        Kind = "test"
        Evidence = $providerEvidence.Robinhood
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            (($providerEvidence.Robinhood | ForEach-Object { "FullyQualifiedName~$_" }) -join "|")
        )
    },
    [ordered]@{
        Name = "Yahoo historical-only core provider"
        Kind = "test"
        Evidence = $providerEvidence.Yahoo
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            (($providerEvidence.Yahoo | ForEach-Object { "FullyQualifiedName~$_" }) -join "|")
        )
    },
    [ordered]@{
        Name = "Checkpoint reliability and gap handling"
        Kind = "test"
        Evidence = @($crossCuttingEvidence.Checkpoint) + @($crossCuttingEvidence.Execution)
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            ((( @($crossCuttingEvidence.Checkpoint) + @($crossCuttingEvidence.Execution) ) | ForEach-Object { "FullyQualifiedName~$_" }) -join "|")
        )
    },
    [ordered]@{
        Name = "Parquet sink and conversion"
        Kind = "test"
        Evidence = @($crossCuttingEvidence.Parquet)
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            ((( @($crossCuttingEvidence.Parquet) ) | ForEach-Object { "FullyQualifiedName~$_" }) -join "|")
        )
    }
)

$activeProviderRows = @(
    [ordered]@{
        name = "Alpaca"
        posture = "repo-closed"
        lane = "core provider confidence"
        offlineEvidence = $providerEvidence.Alpaca
        runtimeEvidence = @()
        notes = @(
            "Active Wave 1 core provider row.",
            "Closed by checked-in provider and stable execution seam tests."
        )
    },
    [ordered]@{
        name = "Robinhood"
        posture = "bounded"
        lane = "supported surface"
        offlineEvidence = $providerEvidence.Robinhood
        runtimeEvidence = @(
            "artifacts/provider-validation/robinhood/2026-04-09/auth-session/summary.md",
            "artifacts/provider-validation/robinhood/2026-04-09/quote-polling/summary.md",
            "artifacts/provider-validation/robinhood/2026-04-09/order-submit-cancel/summary.md",
            "artifacts/provider-validation/robinhood/2026-04-09/throttling-reconnect/summary.md"
        )
        notes = @(
            "Only active provider row that remains runtime-bounded.",
            "Confidence is polling-oriented and execution-adjacent, not websocket-validated."
        )
    },
    [ordered]@{
        name = "Yahoo"
        posture = "repo-closed"
        lane = "historical and fallback confidence"
        offlineEvidence = $providerEvidence.Yahoo
        runtimeEvidence = @()
        notes = @(
            "Active historical-only core provider row.",
            "Not part of Meridian's live runtime-provider claim for Wave 1."
        )
    }
)


function Convert-ToSummarySafeText {
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string]$Text
    )

    $normalized = $Text
    if (-not [string]::IsNullOrWhiteSpace($repoRoot)) {
        $escapedRepoRoot = [Regex]::Escape($repoRoot)
        $normalized = [Regex]::Replace($normalized, $escapedRepoRoot, '{repo-root}', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }

    return $normalized
}

$pilotReplaySampleSet = @(
    [ordered]@{
        id = "DK1-ALPACA-QUOTE-GOLDEN"
        provider = "Alpaca"
        lane = "repo-closed quote pipeline parity"
        sampleUniverse = @("AAPL")
        sampleWindow = "2026-03-19T14:30:00Z"
        evidenceAnchors = @(
            "tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json",
            "AlpacaQuotePipelineGoldenTests"
        )
        automationStep = "Alpaca core provider confidence"
        acceptanceCheck = "Parser, canonical publisher, and JSONL sink output match the committed golden subset."
    },
    [ordered]@{
        id = "DK1-ALPACA-PARSER-EDGE-CASES"
        provider = "Alpaca"
        lane = "repo-closed trade and quote parser coverage"
        sampleUniverse = @("AAPL", "MSFT", "QQQ", "SPY")
        sampleWindow = "2024-06-15 parser fixture window"
        evidenceAnchors = @(
            "AlpacaMessageParsingTests",
            "AlpacaQuoteRoutingTests",
            "AlpacaCredentialAndReconnectTests"
        )
        automationStep = "Alpaca core provider confidence"
        acceptanceCheck = "Trade and quote edge cases preserve symbol separation, timestamp handling, duplicate suppression, routing, and reconnect behavior."
    },
    [ordered]@{
        id = "DK1-ROBINHOOD-SUPPORTED-SURFACE"
        provider = "Robinhood"
        lane = "bounded polling and brokerage surface"
        sampleUniverse = @("AAPL", "MSFT")
        sampleWindow = "2026-04-09 bounded runtime packet plus offline polling fixtures"
        evidenceAnchors = @(
            "RobinhoodMarketDataClientTests",
            "RobinhoodBrokerageGatewayTests",
            "artifacts/provider-validation/robinhood/2026-04-09/manifest.json"
        )
        automationStep = "Robinhood supported surface"
        acceptanceCheck = "Offline polling, symbol search, historical bars, and execution seam tests pass; runtime evidence remains explicitly bounded."
    },
    [ordered]@{
        id = "DK1-YAHOO-HISTORICAL-FALLBACK"
        provider = "Yahoo"
        lane = "repo-closed historical-only fallback"
        sampleUniverse = @("AAPL", "SPY")
        sampleWindow = "2024-01-01 through 2024-01-02 daily fixtures and 2024-01-02 intraday session fixtures"
        evidenceAnchors = @(
            "YahooFinanceHistoricalDataProviderTests",
            "YahooFinanceIntradayContractTests"
        )
        automationStep = "Yahoo historical-only core provider"
        acceptanceCheck = "Daily, adjusted daily, and intraday aggregate fixtures deserialize into stable historical/fallback bars without implying live-provider readiness."
    }
)

$crossCuttingClosures = @(
    [ordered]@{
        name = "Checkpoint reliability"
        posture = "repo-closed"
        evidence = $crossCuttingEvidence.Checkpoint
    },
    [ordered]@{
        name = "Parquet L2 flush behavior"
        posture = "repo-closed"
        evidence = $crossCuttingEvidence.Parquet
    }
)

$deferredProviders = @(
    "Polygon",
    "Interactive Brokers",
    "NYSE",
    "StockSharp"
)

function Invoke-Step {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Step
    )

    $slug = ($Step.Name.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    $logPath = Join-Path $summaryDir "$slug.log"
    $command = @($Step.Command)
    $trxPath = Join-Path $summaryDir "$slug.trx"
    if ($Step.Kind -eq "test") {
        Remove-Item -LiteralPath $trxPath -Force -ErrorAction SilentlyContinue
        $command += @("--logger", "trx;LogFileName=$slug.trx", "--results-directory", $summaryDir)
    }
    $commandText = ($command | ForEach-Object {
        if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ }
    }) -join ' '
    $commandText = Convert-ToSummarySafeText -Text $commandText

    Write-Host "==> $($Step.Name)"
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = @()
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $command[0] @($command[1..($command.Count - 1)]) 2>&1 |
            Tee-Object -FilePath $logPath |
            ForEach-Object { $output += $_ }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    $testEvidence = $null
    if ($Step.Kind -eq "test") {
        $expected = if ($Step.Contains("Evidence")) { @($Step.Evidence) } else { @() }
        $testEvidence = Get-MeridianProviderTestEvidence -TrxPath $trxPath -ExpectedEvidence $expected
    }
    $passed = $exitCode -eq 0 -and ($null -eq $testEvidence -or $testEvidence.success)

    [ordered]@{
        name = $Step.Name
        kind = $Step.Kind
        status = if ($passed) { "passed" } else { "failed" }
        exitCode = $exitCode
        testEvidence = $testEvidence
        trxPath = if ($Step.Kind -eq "test") { "$slug.trx" } else { $null }
        durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        command = $commandText
        logPath = $logPath.Substring($repoRoot.Length + 1).Replace('\', '/')
        tail = Convert-ToSummarySafeText -Text (($output | Select-Object -Last 20) -join [Environment]::NewLine)
    }
}

if ([string]::IsNullOrWhiteSpace($CheckpointPath)) {
    $CheckpointPath = Join-Path $summaryDir "run-wave1-provider-validation.checkpoint.json"
}

$checkpoint = Initialize-MeridianCheckpoint `
    -Workflow "run-wave1-provider-validation" `
    -CheckpointPath $CheckpointPath `
    -InputObject ([ordered]@{
        configuration = $Configuration
        dateStamp = $DateStamp
        outputRoot = $OutputRoot
        operatorSignoffPath = $OperatorSignoffPath
        sourceRevision = (& git -C $repoRoot rev-parse HEAD)
        evidenceVersion = 2
        steps = @($steps | ForEach-Object { [ordered]@{ name = $_.Name; command = @($_.Command) } })
    }) `
    -ForceStep $ForceCheckpointStep `
    -AllowInputMismatch:$AllowCheckpointInputMismatch

$results = New-Object System.Collections.Generic.List[object]
foreach ($step in $steps) {
    $stepSlug = ($step.Name.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    # A retained success cannot prove that current working-tree sources still match
    # an old build. Reexecute build and test steps, retaining checkpoints as run status.

    Start-MeridianCheckpointStep -Context $checkpoint -StepId $stepSlug -Description $step.Name
    try {
        $stepResult = Invoke-Step -Step $step
        if ($stepResult.status -eq "failed") {
            Fail-MeridianCheckpointStep -Context $checkpoint -StepId $stepSlug -Message "Step failed process or test-evidence validation (exit code $($stepResult.exitCode))."
        }
        else {
            $pointers = @((Join-Path $summaryDir "$stepSlug.log"))
            if ($step.Kind -eq "test") { $pointers += Join-Path $summaryDir "$stepSlug.trx" }
            Complete-MeridianCheckpointStep -Context $checkpoint -StepId $stepSlug -ArtifactPointers $pointers
        }

        $results.Add($stepResult) | Out-Null
    }
    catch {
        Fail-MeridianCheckpointStep -Context $checkpoint -StepId $stepSlug -Message $_.Exception.Message
        throw
    }
}

$failedResults = @($results | Where-Object status -eq "failed")

$jsonPath = Join-Path $summaryDir "wave1-validation-summary.json"
$mdPath = Join-Path $summaryDir "wave1-validation-summary.md"

$signoffMetadataPath = ""
if (-not [string]::IsNullOrWhiteSpace($OperatorSignoffPath)) {
    $resolvedSignoffPath = [System.IO.Path]::GetFullPath($OperatorSignoffPath)
    if ($resolvedSignoffPath.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        $signoffMetadataPath = $resolvedSignoffPath.Substring($repoRoot.Length + 1).Replace('\', '/')
    }
    else {
        $signoffMetadataPath = $resolvedSignoffPath.Replace('\', '/')
    }
}

$evidenceSchema = [ordered]@{
    version = "1.0"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    summaryJsonPath = $jsonPath.Substring($repoRoot.Length + 1).Replace('\', '/')
    summaryMarkdownPath = $mdPath.Substring($repoRoot.Length + 1).Replace('\', '/')
    packetJsonPath = (Join-Path $summaryDir "dk1-pilot-parity-packet.json").Substring($repoRoot.Length + 1).Replace('\', '/')
    packetMarkdownPath = (Join-Path $summaryDir "dk1-pilot-parity-packet.md").Substring($repoRoot.Length + 1).Replace('\', '/')
    signoffMetadataPath = $signoffMetadataPath
}

$summary = [ordered]@{
    schemaVersion = "provider-validation-evidence/v1"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    dateStamp = $DateStamp
    runId = "wave1-provider-validation-$DateStamp"
    configuration = $Configuration
    repoRoot = "{repo-root}"
    scope = "Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof"
    result = if ($failedResults.Count -eq 0) { "passed" } else { "failed" }
    readinessImpact = [ordered]@{
        trustDecisionTarget = "GET /api/workstation/trading/readiness and GET /api/workstation/operator/inbox"
        promotionRecommendation = if ($failedResults.Count -eq 0) { "eligible-with-operator-signoff" } else { "blocked" }
        summary = if ($failedResults.Count -eq 0) { "No blocking provider-validation failures detected." } else { "One or more provider-validation lanes failed; keep trust posture degraded until remediated." }
    }
    calibration = [ordered]@{
        kernelVersion = "dk1-baseline-v1"
        sourceDocument = "docs/status/evidence/dk1-baseline-trust-thresholds.md"
    }
    testRunIds = @($results | ForEach-Object { ($_.name.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-') })
    activeProviderRows = $activeProviderRows
    pilotReplaySampleSet = $pilotReplaySampleSet
    crossCuttingClosures = $crossCuttingClosures
    deferredProviders = $deferredProviders
    evidenceSchema = $evidenceSchema
    steps = $results
}

Start-MeridianCheckpointStep -Context $checkpoint -StepId "write-wave1-summary-json" -Description "Write wave1-validation-summary.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath
Complete-MeridianCheckpointStep -Context $checkpoint -StepId "write-wave1-summary-json" -ArtifactPointers @($jsonPath)

$md = @(
    "# Wave 1 Validation Summary",
    "",
    "- Generated: $($summary.generatedAtUtc)",
    "- Configuration: $Configuration",
    "- Scope: $($summary.scope)",
    "- Overall result: $($summary.result)",
    "- Schema version: $($summary.schemaVersion)",
    "- Run ID: $($summary.runId)",
    "- Calibration kernel version: $($summary.calibration.kernelVersion)",
    "- Promotion recommendation: $($summary.readinessImpact.promotionRecommendation)",
    "",
    "## Evidence Schema",
    "",
    "- Summary JSON: ``$($evidenceSchema.summaryJsonPath)``",
    "- Summary Markdown: ``$($evidenceSchema.summaryMarkdownPath)``",
    "- DK1 packet JSON: ``$($evidenceSchema.packetJsonPath)``",
    "- DK1 packet Markdown: ``$($evidenceSchema.packetMarkdownPath)``",
    "- Sign-off metadata: ``$([string]::IsNullOrWhiteSpace($evidenceSchema.signoffMetadataPath) ? "(not supplied)" : $evidenceSchema.signoffMetadataPath)``",
    "",
    "## Active Provider Set",
    "",
    "| Provider | Posture | Lane | Runtime evidence | Notes |",
    "|---|---|---|---|---|"
)

foreach ($provider in $activeProviderRows) {
    $runtimeEvidence = if ($provider.runtimeEvidence.Count -eq 0) {
        "Not required"
    }
    else {
        ($provider.runtimeEvidence -join "<br>")
    }

    $notes = $provider.notes -join " "
    $md += "| $($provider.name) | $($provider.posture) | $($provider.lane) | $runtimeEvidence | $notes |"
}

$md += @(
    "",
    "## DK1 Pilot Replay / Sample Standard",
    "",
    "| Sample ID | Provider | Lane | Sample universe | Replay / fixture window | Evidence anchor | Acceptance check |",
    "|---|---|---|---|---|---|---|"
)

foreach ($sample in $pilotReplaySampleSet) {
    $sampleUniverse = $sample.sampleUniverse -join "<br>"
    $evidenceAnchors = $sample.evidenceAnchors -join "<br>"
    $md += "| $($sample.id) | $($sample.provider) | $($sample.lane) | $sampleUniverse | $($sample.sampleWindow) | $evidenceAnchors | $($sample.acceptanceCheck) |"
}

$md += @(
    "",
    "## Cross-Cutting Closures",
    "",
    "| Closure | Posture | Evidence |",
    "|---|---|---|"
)

foreach ($closure in $crossCuttingClosures) {
    $md += "| $($closure.name) | $($closure.posture) | $(($closure.evidence -join "<br>")) |"
}

$md += @(
    "",
    "| Step | Kind | Status | Tests passed/executed | Duration (s) | Log |",
    "|---|---|---|---:|---:|---|"
)

foreach ($result in $results) {
    $logRef = $result.logPath.Replace('\', '/')
    $testCounts = if ($null -ne $result.testEvidence) { "$($result.testEvidence.passed)/$($result.testEvidence.executed)" } else { "n/a" }
    $md += "| $($result.name) | $($result.kind) | $($result.status) | $testCounts | $($result.durationSeconds) | ``$logRef`` |"
}

$md += @(
    "",
    "## Deferred Provider Inventory",
    "",
    "- $(($deferredProviders -join ", "))"
)


Start-MeridianCheckpointStep -Context $checkpoint -StepId "write-wave1-summary-md" -Description "Write wave1-validation-summary.md"
$md -join [Environment]::NewLine | Set-Content -Path $mdPath
Complete-MeridianCheckpointStep -Context $checkpoint -StepId "write-wave1-summary-md" -ArtifactPointers @($mdPath)

$packetScript = Join-Path $PSScriptRoot "generate-dk1-pilot-parity-packet.ps1"
if (Test-Path -LiteralPath $packetScript) {
    $packetArgs = @{
        SummaryJsonPath = $jsonPath
    }
    if (-not [string]::IsNullOrWhiteSpace($OperatorSignoffPath)) {
        $packetArgs.OperatorSignoffPath = $OperatorSignoffPath
    }

    # Tests are rerun on resume; regenerate downstream evidence from this invocation.
    Start-MeridianCheckpointStep -Context $checkpoint -StepId "generate-dk1-packet" -Description "Generate DK1 parity packet."
    try {
        if ($summary.result -ne "passed") {
            $packetArgs.AllowFailedSummary = $true
            & $packetScript @packetArgs
        }
        else {
            & $packetScript @packetArgs
        }
        Complete-MeridianCheckpointStep -Context $checkpoint -StepId "generate-dk1-packet" -ArtifactPointers @($summaryDir)
    }
    catch {
        Fail-MeridianCheckpointStep -Context $checkpoint -StepId "generate-dk1-packet" -Message $_.Exception.Message
        throw
    }
}

Write-Host ""
Write-Host "Wave 1 validation summary written to:"
Write-Host "  $jsonPath"
Write-Host "  $mdPath"

if ($summary.result -eq "failed") {
    $failedSteps = ($failedResults | ForEach-Object name) -join ", "
    throw "Wave 1 provider validation failed: $failedSteps"
}
