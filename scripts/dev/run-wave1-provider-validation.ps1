param(
    [string]$Configuration = "Release",
    [string]$DateStamp = (Get-Date).ToString("yyyy-MM-dd"),
    [string]$OutputRoot = "artifacts/provider-validation/_automation"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$summaryDir = Join-Path $repoRoot $OutputRoot | Join-Path -ChildPath $DateStamp
New-Item -ItemType Directory -Force -Path $summaryDir | Out-Null

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
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~AlpacaBrokerageGatewayTests|FullyQualifiedName~AlpacaCorporateActionProviderTests|FullyQualifiedName~AlpacaCredentialAndReconnectTests|FullyQualifiedName~AlpacaMessageParsingTests|FullyQualifiedName~AlpacaQuotePipelineGoldenTests|FullyQualifiedName~AlpacaQuoteRoutingTests|FullyQualifiedName~AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam"
        )
    },
    [ordered]@{
        Name = "Robinhood supported surface"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~RobinhoodBrokerageGatewayTests|FullyQualifiedName~RobinhoodMarketDataClientTests|FullyQualifiedName~RobinhoodHistoricalDataProviderTests|FullyQualifiedName~RobinhoodSymbolSearchProviderTests|FullyQualifiedName~RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam"
        )
    },
    [ordered]@{
        Name = "Yahoo historical-only core provider"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~YahooFinanceHistoricalDataProviderTests|FullyQualifiedName~YahooFinanceIntradayContractTests"
        )
    },
    [ordered]@{
        Name = "Checkpoint reliability and gap handling"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~BackfillStatusStoreTests|FullyQualifiedName~ParallelBackfillServiceTests|FullyQualifiedName~GapBackfillServiceTests|FullyQualifiedName~CheckpointEndpointTests"
        )
    },
    [ordered]@{
        Name = "Parquet sink and conversion"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~ParquetStorageSinkTests|FullyQualifiedName~ParquetConversionServiceTests"
        )
    }
)

$activeProviderRows = @(
    [ordered]@{
        name = "Alpaca"
        posture = "repo-closed"
        lane = "core provider confidence"
        offlineEvidence = @(
            "AlpacaBrokerageGatewayTests",
            "AlpacaCorporateActionProviderTests",
            "AlpacaCredentialAndReconnectTests",
            "AlpacaMessageParsingTests",
            "AlpacaQuotePipelineGoldenTests",
            "AlpacaQuoteRoutingTests",
            "ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam"
        )
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
        offlineEvidence = @(
            "RobinhoodBrokerageGatewayTests",
            "RobinhoodMarketDataClientTests",
            "RobinhoodHistoricalDataProviderTests",
            "RobinhoodSymbolSearchProviderTests",
            "ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam"
        )
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
        offlineEvidence = @(
            "YahooFinanceHistoricalDataProviderTests",
            "YahooFinanceIntradayContractTests"
        )
        runtimeEvidence = @()
        notes = @(
            "Active historical-only core provider row.",
            "Not part of Meridian's live runtime-provider claim for Wave 1."
        )
    }
)

$crossCuttingClosures = @(
    [ordered]@{
        name = "Checkpoint reliability"
        posture = "repo-closed"
        evidence = @(
            "BackfillStatusStoreTests",
            "ParallelBackfillServiceTests",
            "GapBackfillServiceTests",
            "CheckpointEndpointTests"
        )
    },
    [ordered]@{
        name = "Parquet L2 flush behavior"
        posture = "repo-closed"
        evidence = @(
            "ParquetStorageSinkTests",
            "ParquetConversionServiceTests"
        )
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
    $commandText = ($Step.Command | ForEach-Object {
        if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ }
    }) -join ' '

    Write-Host "==> $($Step.Name)"
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = @()
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Step.Command[0] @($Step.Command[1..($Step.Command.Count - 1)]) 2>&1 |
            Tee-Object -FilePath $logPath |
            ForEach-Object { $output += $_ }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    [ordered]@{
        name = $Step.Name
        kind = $Step.Kind
        status = if ($exitCode -eq 0) { "passed" } else { "failed" }
        exitCode = $exitCode
        durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        command = $commandText
        logPath = $logPath.Substring($repoRoot.Length + 1).Replace('\', '/')
        tail = ($output | Select-Object -Last 20) -join [Environment]::NewLine
    }
}

$results = foreach ($step in $steps) {
    Invoke-Step -Step $step
}

$failedResults = @($results | Where-Object status -eq "failed")

$summary = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    dateStamp = $DateStamp
    configuration = $Configuration
    repoRoot = $repoRoot
    scope = "Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof"
    result = if ($failedResults.Count -eq 0) { "passed" } else { "failed" }
    activeProviderRows = $activeProviderRows
    crossCuttingClosures = $crossCuttingClosures
    deferredProviders = $deferredProviders
    steps = $results
}

$jsonPath = Join-Path $summaryDir "wave1-validation-summary.json"
$mdPath = Join-Path $summaryDir "wave1-validation-summary.md"
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonPath

$md = @(
    "# Wave 1 Validation Summary",
    "",
    "- Generated: $($summary.generatedAtUtc)",
    "- Configuration: $Configuration",
    "- Scope: $($summary.scope)",
    "- Overall result: $($summary.result)",
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
    "| Step | Kind | Status | Duration (s) | Log |",
    "|---|---|---|---:|---|"
)

foreach ($result in $results) {
    $logRef = $result.logPath.Replace('\', '/')
    $md += "| $($result.name) | $($result.kind) | $($result.status) | $($result.durationSeconds) | ``$logRef`` |"
}

$md += @(
    "",
    "## Deferred Provider Inventory",
    "",
    "- $(($deferredProviders -join ", "))"
)

$md -join [Environment]::NewLine | Set-Content -Path $mdPath

Write-Host ""
Write-Host "Wave 1 validation summary written to:"
Write-Host "  $jsonPath"
Write-Host "  $mdPath"

if ($summary.result -eq "failed") {
    $failedSteps = ($failedResults | ForEach-Object name) -join ", "
    throw "Wave 1 provider validation failed: $failedSteps"
}
