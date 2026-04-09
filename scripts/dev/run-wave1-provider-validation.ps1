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
        Name = "Polygon replay and parsing"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~PolygonRecordedSessionReplayTests|FullyQualifiedName~PolygonMessageParsingTests|FullyQualifiedName~PolygonSubscriptionTests|FullyQualifiedName~PolygonMarketDataClientTests"
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
        Name = "Interactive Brokers guidance and version bounds"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~IBRuntimeGuidanceTests|FullyQualifiedName~IBOrderSampleTests|FullyQualifiedName~IBApiVersionValidatorTests|FullyQualifiedName~IBSimulationClientContractTests"
        )
    },
    [ordered]@{
        Name = "NYSE shared lifecycle and bounded runtime seams"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~NyseSharedLifecycleTests|FullyQualifiedName~NyseMarketDataClientTests|FullyQualifiedName~NYSECredentialAndRateLimitTests|FullyQualifiedName~NYSEMessageParsingTests|FullyQualifiedName~NyseTaqCollectorIntegrationTests"
        )
    },
    [ordered]@{
        Name = "StockSharp validated adapter baseline"
        Kind = "test"
        Command = @(
            "dotnet"
        ) + $commonTestArgs + @(
            "--filter",
            "FullyQualifiedName~StockSharpSubscriptionTests|FullyQualifiedName~StockSharpMessageConversionTests|FullyQualifiedName~StockSharpConnectorFactoryTests"
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
    },
    [ordered]@{
        Name = "IBApi compile-only smoke build"
        Kind = "script"
        Command = @(
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", (Join-Path $repoRoot "scripts/dev/build-ibapi-smoke.ps1"),
            "-Configuration", $Configuration
        )
    }
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
    result = if ($failedResults.Count -eq 0) { "passed" } else { "failed" }
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
    "- Overall result: $($summary.result)",
    "",
    "| Step | Kind | Status | Duration (s) | Log |",
    "|---|---|---|---:|---|"
)

foreach ($result in $results) {
    $logRef = $result.logPath.Replace('\', '/')
    $md += "| $($result.name) | $($result.kind) | $($result.status) | $($result.durationSeconds) | ``$logRef`` |"
}

$md -join [Environment]::NewLine | Set-Content -Path $mdPath

Write-Host ""
Write-Host "Wave 1 validation summary written to:"
Write-Host "  $jsonPath"
Write-Host "  $mdPath"

if ($summary.result -eq "failed") {
    $failedSteps = ($failedResults | ForEach-Object name) -join ", "
    throw "Wave 1 provider validation failed: $failedSteps"
}
