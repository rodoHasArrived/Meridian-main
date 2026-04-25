using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Meridian.ProviderSdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed class WorkstationEndpointsTests
{
    // Must match the JsonSerializerOptions used in CreateAppAsync so that enum fields
    // serialized as strings by the server (via JsonStringEnumConverter) can be round-tripped.
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldReturnServiceBackedBootstrapPayloads()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-latest",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-prior",
            strategyId: "meanrev-1",
            strategyName: "Mean Reversion",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities"));

        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Carry Pair Desk");
        session.RootElement.GetProperty("role").GetString().Should().Be("Research Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("operations");
        session.RootElement.GetProperty("latestRun").GetProperty("runId").GetString().Should().Be("run-latest");
        session.RootElement.GetProperty("workspaceSummary").GetProperty("totalRuns").GetInt32().Should().Be(2);
        session.RootElement.GetProperty("workspaceSummary").GetProperty("ledgerCoverage").GetInt32().Should().Be(2);
        session.RootElement.GetProperty("workspaceSummary").GetProperty("portfolioCoverage").GetInt32().Should().Be(2);

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        research.RootElement.GetProperty("workspace").GetProperty("totalRuns").GetInt32().Should().Be(2);
        research.RootElement.GetProperty("workspace").GetProperty("latestRunId").GetString().Should().Be("run-latest");
        research.RootElement.GetProperty("workspace").GetProperty("promotionCandidates").GetInt32().Should().Be(2);

        var runs = research.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(2);

        var latestRun = runs[0];
        latestRun.GetProperty("id").GetString().Should().Be("run-latest");
        latestRun.GetProperty("strategyName").GetString().Should().Be("Carry Pair");
        latestRun.GetProperty("mode").GetString().Should().Be("paper");
        latestRun.GetProperty("status").GetString().Should().Be("Completed");
        latestRun.GetProperty("dataset").GetString().Should().Be("dataset/fx/spot");
        latestRun.GetProperty("notes").GetString().Should().Contain("review");
        latestRun.GetProperty("drillIn").GetProperty("fills").GetString().Should().Be("/api/workstation/runs/run-latest/fills");

        var comparisons = research.RootElement.GetProperty("comparisons");
        comparisons.GetArrayLength().Should().BeGreaterThan(0);
        comparisons[0].GetProperty("modes").EnumerateArray()
            .Should()
            .Contain(mode => mode.GetProperty("drillIn").GetProperty("attribution").GetString() != null);
        research.RootElement.GetProperty("timeline").GetArrayLength().Should().Be(2);

        research.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "active-runs" &&
                               metric.GetProperty("value").GetString() == "0");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnFallbackPayloads()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        session.RootElement.GetProperty("displayName").GetString().Should().Be("Meridian Operator");
        session.RootElement.GetProperty("role").GetString().Should().Be("Research Lead");
        session.RootElement.GetProperty("environment").GetString().Should().Be("paper");
        session.RootElement.GetProperty("activeWorkspace").GetString().Should().Be("research");
        session.RootElement.GetProperty("commandCount").GetInt32().Should().Be(6);

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        research.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "active-runs" &&
                               metric.GetProperty("value").GetString() == "24");

        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");
        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "open-breaks" &&
                               metric.GetProperty("value").GetString() == "4");
        governance.RootElement.GetProperty("reconciliationQueue").GetArrayLength().Should().Be(1);

        var runs = research.RootElement.GetProperty("runs");
        runs.GetArrayLength().Should().Be(1);
        runs[0].GetProperty("id").GetString().Should().Be("run-research-001");
        runs[0].GetProperty("strategyName").GetString().Should().Be("Mean Reversion FX");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsProviderMetrics_ShouldExposeDk1TrustRationale()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-metrics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_status"));
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var metrics = new ProviderMetricsStatus(
            Timestamp: new DateTimeOffset(2026, 4, 24, 17, 0, 0, TimeSpan.Zero),
            Providers:
            [
                new ProviderMetrics(
                    ProviderId: "yahoo",
                    ProviderType: "Historical bars",
                    IsConnected: true,
                    TradesReceived: 0,
                    DepthUpdatesReceived: 0,
                    QuotesReceived: 2400,
                    ConnectionAttempts: 1,
                    ConnectionFailures: 0,
                    MessagesDropped: 0,
                    ActiveSubscriptions: 4,
                    AverageLatencyMs: 42,
                    MinLatencyMs: 25,
                    MaxLatencyMs: 80,
                    DataQualityScore: 0.96,
                    ConnectionSuccessRate: 1,
                    Timestamp: new DateTimeOffset(2026, 4, 24, 16, 59, 0, TimeSpan.Zero)),
                new ProviderMetrics(
                    ProviderId: "alpaca",
                    ProviderType: "Streaming equities",
                    IsConnected: false,
                    TradesReceived: 12,
                    DepthUpdatesReceived: 0,
                    QuotesReceived: 30,
                    ConnectionAttempts: 4,
                    ConnectionFailures: 4,
                    MessagesDropped: 0,
                    ActiveSubscriptions: 2,
                    AverageLatencyMs: 510,
                    MinLatencyMs: 90,
                    MaxLatencyMs: 900,
                    DataQualityScore: 0.62,
                    ConnectionSuccessRate: 0,
                    Timestamp: new DateTimeOffset(2026, 4, 24, 16, 58, 0, TimeSpan.Zero))
            ],
            TotalProviders: 2,
            HealthyProviders: 1);
        var metricsJson = JsonSerializer.Serialize(metrics, ServerJsonOptions);
        await File.WriteAllTextAsync(Path.Combine(root, "_status", "providers.json"), metricsJson);

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
        });
        var client = app.GetTestClient();

        using var dataOperations = await ReadJsonAsync(client, "/api/workstation/data-operations");
        var providers = dataOperations.RootElement.GetProperty("providers");
        providers.GetArrayLength().Should().Be(2);

        var healthy = providers[0];
        healthy.GetProperty("provider").GetString().Should().Be("yahoo");
        healthy.GetProperty("status").GetString().Should().Be("Healthy");
        healthy.GetProperty("trustScore").GetString().Should().Be("96%");
        healthy.GetProperty("reasonCode").GetString().Should().Be("HEALTHY_BASELINE");
        healthy.GetProperty("recommendedAction").GetString().Should().Contain("no DK1 action");

        var degraded = providers[1];
        degraded.GetProperty("provider").GetString().Should().Be("alpaca");
        degraded.GetProperty("status").GetString().Should().Be("Degraded");
        degraded.GetProperty("trustScore").GetString().Should().Be("62%");
        degraded.GetProperty("signalSource").GetString().Should().Be("Provider quote/trade stream health telemetry");
        degraded.GetProperty("reasonCode").GetString().Should().Be("PROVIDER_STREAM_DEGRADED");
        degraded.GetProperty("recommendedAction").GetString().Should().Contain("Verify provider connectivity");
        degraded.GetProperty("gateImpact").GetString().Should().Be("Critical");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_KernelObservability_ShouldSurfaceActiveAndHistoricalAlertMetrics()
    {
        var observability = CreateRecoveredKernelObservability();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(observability);
        });

        var client = app.GetTestClient();

        using var dataOperations = await ReadJsonAsync(client, "/api/workstation/data-operations");
        dataOperations.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric =>
                metric.GetProperty("id").GetString() == "kernel-critical-jumps" &&
                metric.GetProperty("value").GetString() == "0" &&
                metric.GetProperty("delta").GetString() == "1 total" &&
                metric.GetProperty("tone").GetString() == "success");

        var dataOpsKernel = dataOperations.RootElement.GetProperty("kernelObservability");
        dataOpsKernel.GetProperty("activeAlerts").GetInt32().Should().Be(0);
        dataOpsKernel.GetProperty("totalAlerts").GetInt32().Should().Be(1);
        dataOpsKernel.GetProperty("alerts").GetInt32().Should().Be(1);
        dataOpsKernel.GetProperty("determinismChecksEnabled").GetBoolean().Should().BeTrue();
        dataOpsKernel.GetProperty("domains").GetArrayLength().Should().Be(1);

        var domain = dataOpsKernel.GetProperty("domains")[0];
        domain.GetProperty("domain").GetString().Should().Be(nameof(ProviderCapabilityKind.HistoricalBars));
        domain.GetProperty("throughputPerMinute").GetDouble().Should().BeGreaterThan(0);
        domain.GetProperty("latencyMs").GetProperty("p95").GetDouble()
            .Should()
            .BeGreaterThanOrEqualTo(domain.GetProperty("latencyMs").GetProperty("p50").GetDouble());
        domain.GetProperty("drift").GetProperty("methodology").GetString().Should().Be("totalVariationDistance");
        domain.GetProperty("lastUpdatedUtc").ValueKind.Should().Be(JsonValueKind.String);

        var criticalSeverityRate = domain.GetProperty("criticalSeverityRate");
        criticalSeverityRate.GetProperty("jumpAlertActive").GetBoolean().Should().BeFalse();
        criticalSeverityRate.GetProperty("jumpAlertCount").GetInt32().Should().Be(1);
        criticalSeverityRate.GetProperty("shortWindowSamples").GetInt32().Should().Be(30);
        criticalSeverityRate.GetProperty("longWindowSamples").GetInt32().Should().Be(90);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("minimumSampleCount").GetInt32().Should().Be(20);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("minimumShortRate").GetDouble().Should().Be(0.25);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("zeroBaselineShortRate").GetDouble().Should().Be(0.35);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("relativeMultiplier").GetDouble().Should().Be(2.0);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("absoluteIncrease").GetDouble().Should().Be(0.15);

        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");
        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric =>
                metric.GetProperty("id").GetString() == "kernel-critical-jumps" &&
                metric.GetProperty("value").GetString() == "0" &&
                metric.GetProperty("tone").GetString() == "success");

        var governanceKernel = governance.RootElement.GetProperty("kernelObservability");
        governanceKernel.GetProperty("activeAlerts").GetInt32().Should().Be(0);
        governanceKernel.GetProperty("totalAlerts").GetInt32().Should().Be(1);
        governanceKernel.GetProperty("domains")[0]
            .GetProperty("criticalSeverityRate")
            .GetProperty("jumpAlertCount")
            .GetInt32()
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldReturnTypedResearchBriefing()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-latest",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-prior",
            strategyId: "meanrev-1",
            strategyName: "Mean Reversion",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/research/briefing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var briefing = await response.Content.ReadFromJsonAsync<ResearchBriefingDto>(ServerJsonOptions);

        briefing.Should().NotBeNull();
        briefing!.Workspace.TotalRuns.Should().Be(2);
        briefing.Workspace.LatestRunId.Should().Be("run-latest");
        briefing.Workspace.HasLedgerCoverage.Should().BeTrue();
        briefing.InsightFeed.Widgets.Should().HaveCount(2);
        briefing.RecentRuns.Should().HaveCount(2);
        briefing.RecentRuns[0].RunId.Should().Be("run-latest");
        briefing.RecentRuns[0].DrillIn.Continuity.Should().Be("/api/workstation/runs/run-latest/continuity");
        briefing.SavedComparisons.Should().NotBeEmpty();
        briefing.Alerts.Should().NotBeEmpty();
        briefing.Watchlists.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithoutStrategyReadService_ShouldReturnFallbackResearchBriefing()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/research/briefing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var briefing = await response.Content.ReadFromJsonAsync<ResearchBriefingDto>(ServerJsonOptions);

        briefing.Should().NotBeNull();
        briefing!.Workspace.TotalRuns.Should().Be(24);
        briefing.Workspace.LatestRunId.Should().Be("run-research-001");
        briefing.InsightFeed.Widgets.Should().HaveCount(3);
        briefing.Watchlists.Should().HaveCount(2);
        briefing.RecentRuns.Should().ContainSingle(run => run.RunId == "run-research-001");
        briefing.Alerts.Should().NotBeEmpty();
        briefing.WhatChanged.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithoutContext_ShouldPrioritizeChooseContextForTradingAndGovernance()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var client = app.GetTestClient();

        var summary = await ReadWorkflowSummaryAsync(client, "/api/workstation/workflow-summary");

        GetWorkspace(summary, "trading").NextAction.Label.Should().Be("Choose Context");
        GetWorkspace(summary, "trading").NextAction.TargetPageTag.Should().Be("TradingShell");
        GetWorkspace(summary, "governance").NextAction.Label.Should().Be("Choose Context");
        GetWorkspace(summary, "governance").NextAction.TargetPageTag.Should().Be("GovernanceShell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithPaperCandidate_ShouldReflectResearchToTradingHandoff()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("workflow-backtest-candidate") with
        {
            FundProfileId = "northwind-income",
            FundDisplayName = "Northwind Income"
        });

        var client = app.GetTestClient();
        var summary = await ReadWorkflowSummaryAsync(
            client,
            "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=Northwind%20Income&fundProfileId=northwind-income&fundDisplayName=Northwind%20Income");

        var research = GetWorkspace(summary, "research");
        research.StatusLabel.Should().Be("Candidate for paper review");
        research.NextAction.Label.Should().Be("Send to Trading Review");
        research.NextAction.TargetPageTag.Should().Be("TradingShell");

        var trading = GetWorkspace(summary, "trading");
        trading.StatusLabel.Should().Be("Candidate awaiting paper review");
        trading.NextAction.Label.Should().Be("Review Candidate for Paper");
        trading.NextAction.TargetPageTag.Should().Be("TradingShell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithActivePaperRunAndNoBreaks_ShouldKeepTradingActiveAndGovernanceReady()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("workflow-paper-active", withBreaks: false));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("workflow-paper-active"));

        var client = app.GetTestClient();
        var summary = await ReadWorkflowSummaryAsync(
            client,
            "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=Northwind%20Income&fundProfileId=northwind-income&fundDisplayName=Northwind%20Income");

        var trading = GetWorkspace(summary, "trading");
        trading.StatusLabel.Should().Be("Active paper cockpit");
        trading.NextAction.Label.Should().Be("Open Active Cockpit");

        var governance = GetWorkspace(summary, "governance");
        governance.StatusLabel.Should().Be("Governance review ready");
        governance.NextAction.Label.Should().Be("Open Governance Shell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithReconciliationBreaks_ShouldEscalateGovernanceNextAction()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("workflow-paper-breaks", withBreaks: true));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("workflow-paper-breaks"));

        var client = app.GetTestClient();
        var summary = await ReadWorkflowSummaryAsync(
            client,
            "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=Northwind%20Income&fundProfileId=northwind-income&fundDisplayName=Northwind%20Income");

        var governance = GetWorkspace(summary, "governance");
        governance.StatusLabel.Should().Be("Reconciliation breaks require review");
        governance.NextAction.Label.Should().Be("Review Reconciliation Breaks");
        governance.NextAction.TargetPageTag.Should().Be("FundReconciliation");
        governance.PrimaryBlocker.IsBlocking.Should().BeTrue();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummaryWithoutRuns_ShouldReturnStableNonNullContracts()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/workflow-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();
        summary!.Workspaces.Should().HaveCount(4);
        foreach (var workspace in summary.Workspaces)
        {
            workspace.NextAction.Should().NotBeNull();
            workspace.PrimaryBlocker.Should().NotBeNull();
            workspace.Evidence.Should().NotBeNull();
        }
        GetWorkspace(summary, "research").NextAction.Label.Should().Be("Start Backtest");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_ShouldSurfacePaperGatewayBrokerGap()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton(new BrokerageConfiguration
            {
                Gateway = "paper",
                LiveExecutionEnabled = true
            });
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-paper-live-review",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var brokerage = trading.RootElement.GetProperty("brokerage");
        brokerage.GetProperty("provider").GetString().Should().Be("Paper trading");
        brokerage.GetProperty("notes").GetString().Should().Contain("blocked");
        brokerage.GetProperty("notes").GetString().Should().Contain("paper trading");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldJoinSessionReplayAuditControlsAndPromotion()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness", Guid.NewGuid().ToString("N"));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton(_ => new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                NullLogger<ExecutionAuditTrailService>.Instance));
            services.AddSingleton<IPaperSessionStore>(_ => new JsonlFilePaperSessionStore(
                Path.Combine(rootPath, "sessions"),
                NullLogger<JsonlFilePaperSessionStore>.Instance));
            services.AddSingleton<PaperSessionPersistenceService>(sp => new PaperSessionPersistenceService(
                NullLogger<PaperSessionPersistenceService>.Instance,
                sp.GetRequiredService<IPaperSessionStore>(),
                sp.GetRequiredService<ExecutionAuditTrailService>()));
            services.AddSingleton<ExecutionOperatorControlService>(sp => new ExecutionOperatorControlService(
                new ExecutionOperatorControlOptions(Path.Combine(rootPath, "controls")),
                NullLogger<ExecutionOperatorControlService>.Instance,
                sp.GetRequiredService<ExecutionAuditTrailService>()));
            services.AddSingleton<BacktestToLivePromoter>();
            services.AddSingleton<IPromotionRecordStore>(_ => new JsonlPromotionRecordStore(
                Path.Combine(rootPath, "promotions"),
                NullLogger<JsonlPromotionRecordStore>.Instance));
            services.AddSingleton<PromotionService>(sp => new PromotionService(
                sp.GetRequiredService<IStrategyRepository>(),
                sp.GetRequiredService<BacktestToLivePromoter>(),
                sp.GetRequiredService<IPromotionRecordStore>(),
                NullLogger<PromotionService>.Instance,
                operatorControls: sp.GetRequiredService<ExecutionOperatorControlService>(),
                auditTrail: sp.GetRequiredService<ExecutionAuditTrailService>()));
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-wave2-backtest",
            strategyId: "strat-wave2",
            strategyName: "Wave 2 Acceptance",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 24, 15, 30, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")));

        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-wave2",
            StrategyName: "Wave 2 Acceptance",
            InitialCash: 250_000m,
            Symbols: ["AAPL"]));
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-wave2", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-wave2", "AAPL", 10m, 190m));
        var verification = await persistence.VerifyReplayAsync(session.SessionId);

        var decision = await app.Services.GetRequiredService<PromotionService>().ApproveAsync(new PromotionApprovalRequest(
            RunId: "run-wave2-backtest",
            ApprovedBy: "ops.lead",
            ApprovalReason: "Replay, audit, and paper controls accepted for Wave 2."));

        decision.Success.Should().BeTrue();

        var client = app.GetTestClient();
        var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.ActiveSession.Should().NotBeNull();
        readiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        readiness.ActiveSession.OrderCount.Should().Be(1);
        readiness.Replay.Should().NotBeNull();
        readiness.Replay!.IsConsistent.Should().BeTrue();
        readiness.Replay.ComparedFillCount.Should().Be(1);
        readiness.Replay.ComparedOrderCount.Should().Be(1);
        readiness.Replay.VerificationAuditId.Should().Be(verification!.VerificationAuditId);
        readiness.Controls.CircuitBreakerOpen.Should().BeFalse();
        readiness.Controls.ManualOverrideCount.Should().Be(0);
        readiness.Promotion.Should().NotBeNull();
        readiness.Promotion!.ApprovalStatus.Should().Be(PromotionDecisionKinds.Approved);
        readiness.Promotion.ApprovedBy.Should().Be("ops.lead");
        readiness.Promotion.AuditReference.Should().Be(decision.AuditReference);
        readiness.WorkItems.Should().NotContain(item => item.Tone == OperatorWorkItemToneDto.Critical);
        readiness.WorkItems.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.PaperReplay);
        readiness.WorkItems.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.PromotionReview);

        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");
        trading.RootElement
            .GetProperty("readiness")
            .GetProperty("activeSession")
            .GetProperty("sessionId")
            .GetString()
            .Should()
            .Be(session.SessionId);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithSecurityLookup_ShouldExposeSecurityCoverageInBootstrapPayloads()
    {
        await using var app = await CreateAppAsync(services =>
        {
            var lookup = new StubSecurityReferenceLookup();
            lookup.Register("AAPL", new WorkstationSecurityReference(
                SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                DisplayName: "Apple Inc.",
                AssetClass: "Equity",
                Currency: "USD",
                Status: SecurityStatusDto.Active,
                PrimaryIdentifier: "AAPL"));

            services.AddSingleton<IStrategyRepository>(new StrategyRunStore());
            services.AddSingleton<ISecurityReferenceLookup>(lookup);
            services.AddSingleton<PortfolioReadService>();
            services.AddSingleton<LedgerReadService>();
            services.AddSingleton<StrategyRunReadService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-security",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx").Complete(BuildBacktestResultWithSymbol("AAPL")));

        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        var latestCoverage = session.RootElement.GetProperty("latestRun").GetProperty("securityCoverage");
        latestCoverage.GetProperty("portfolioResolved").GetInt32().Should().Be(1);
        latestCoverage.GetProperty("portfolioMissing").GetInt32().Should().Be(0);
        latestCoverage.GetProperty("ledgerResolved").GetInt32().Should().Be(1);
        latestCoverage.GetProperty("ledgerMissing").GetInt32().Should().Be(0);
        latestCoverage.GetProperty("hasIssues").GetBoolean().Should().BeFalse();
        latestCoverage.GetProperty("tone").GetString().Should().Be("success");
        latestCoverage.GetProperty("resolvedReferences").GetArrayLength().Should().Be(2);
        latestCoverage.GetProperty("missingReferences").GetArrayLength().Should().Be(0);
        latestCoverage.GetProperty("summary").GetString().Should().Contain("no unresolved symbols");

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        var runCoverage = research.RootElement.GetProperty("runs")[0].GetProperty("securityCoverage");
        runCoverage.GetProperty("portfolioResolved").GetInt32().Should().Be(1);
        runCoverage.GetProperty("ledgerResolved").GetInt32().Should().Be(1);
        runCoverage.GetProperty("hasIssues").GetBoolean().Should().BeFalse();
        runCoverage.GetProperty("resolvedReferences").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("source").GetString() == "portfolio" &&
                item.GetProperty("symbol").GetString() == "AAPL" &&
                item.GetProperty("displayName").GetString() == "Apple Inc.");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithGovernanceServices_ShouldExposeGovernanceWorkspacePayload()
    {
        await using var app = await CreateAppAsync(services =>
        {
            var lookup = new StubSecurityReferenceLookup();
            lookup.Register("AAPL", new WorkstationSecurityReference(
                SecurityId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DisplayName: "Apple Inc.",
                AssetClass: "Equity",
                Currency: "USD",
                Status: SecurityStatusDto.Active,
                PrimaryIdentifier: "AAPL"));

            services.AddSingleton<IStrategyRepository>(new StrategyRunStore());
            services.AddSingleton<ISecurityReferenceLookup>(lookup);
            services.AddSingleton<PortfolioReadService>();
            services.AddSingleton<LedgerReadService>();
            services.AddSingleton<StrategyRunReadService>();
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-governance-balanced"));
        await store.RecordRunAsync(BuildReconciliationMismatchRun("run-governance-breaks"));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-governance-balanced"));
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-governance-breaks"));

        var client = app.GetTestClient();
        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");

        var workspace = governance.RootElement.GetProperty("workspace");
        workspace.GetProperty("totalRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("ledgerReadyRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("reconciledRuns").GetInt32().Should().Be(2);
        workspace.GetProperty("openBreaks").GetInt32().Should().BeGreaterThan(0);
        workspace.GetProperty("securityIssues").GetInt32().Should().BeGreaterThan(0);

        var cashFlow = governance.RootElement.GetProperty("cashFlow");
        cashFlow.GetProperty("runsWithCashSignals").GetInt32().Should().Be(2);
        cashFlow.GetProperty("runsWithCashVariance").GetInt32().Should().Be(1);
        cashFlow.GetProperty("netVariance").GetDecimal().Should().Be(-100m);

        var reporting = governance.RootElement.GetProperty("reporting");
        reporting.GetProperty("profileCount").GetInt32().Should().BeGreaterThan(0);
        reporting.GetProperty("profiles").EnumerateArray()
            .Should()
            .Contain(profile => profile.GetProperty("id").GetString() == "excel");
        reporting.GetProperty("recommendedProfiles").EnumerateArray()
            .Select(profile => profile.GetString())
            .Should()
            .Contain("excel");

        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric => metric.GetProperty("id").GetString() == "open-breaks" &&
                               metric.GetProperty("value").GetString() != "0");

        var queue = governance.RootElement.GetProperty("reconciliationQueue");
        queue.GetArrayLength().Should().Be(2);

        var breakRun = queue.EnumerateArray()
            .Single(item => item.GetProperty("runId").GetString() == "run-governance-breaks");
        breakRun.GetProperty("reconciliationStatus").GetString().Should().Be("BreaksOpen");
        breakRun.GetProperty("openBreakCount").GetInt32().Should().BeGreaterThan(0);
        breakRun.GetProperty("securityCoverage").GetProperty("hasIssues").GetBoolean().Should().BeTrue();
        breakRun.GetProperty("securityCoverage").GetProperty("missingReferences").GetArrayLength().Should().BeGreaterThan(0);
        breakRun.GetProperty("cashFlow").GetProperty("cashVariance").GetDecimal().Should().Be(-100m);
        breakRun.GetProperty("latestReconciliation").GetProperty("hasSecurityCoverageIssues").GetBoolean().Should().BeTrue();
        breakRun.GetProperty("latestReconciliation").GetProperty("securityIssueCount").GetInt32().Should().BeGreaterThan(0);

        var balancedRun = queue.EnumerateArray()
            .Single(item => item.GetProperty("runId").GetString() == "run-governance-balanced");
        balancedRun.GetProperty("reconciliationStatus").GetString().Should().Be("SecurityCoverageOpen");
        balancedRun.GetProperty("breakCount").GetInt32().Should().Be(0);
        balancedRun.GetProperty("cashFlow").GetProperty("cashVariance").GetDecimal().Should().Be(0m);
        balancedRun.GetProperty("latestReconciliation").GetProperty("hasSecurityCoverageIssues").GetBoolean().Should().BeTrue();
        balancedRun.GetProperty("securityCoverage").GetProperty("missingReferences").EnumerateArray()
            .Should()
            .Contain(item => item.GetProperty("symbol").GetString() == "TSLA");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldCreateAndFetchRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-recon"));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/reconciliation/runs", new ReconciliationRunRequest("run-recon"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.Summary.RunId.Should().Be("run-recon");
        created.Summary.BreakCount.Should().Be(0);
        created.Matches.Should().Contain(match => match.CheckId == "cash-balance");

        var latestResponse = await client.GetAsync("/api/workstation/runs/run-recon/reconciliation");
        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await latestResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be(created.Summary.ReconciliationRunId);

        var byIdResponse = await client.GetAsync($"/api/workstation/reconciliation/runs/{created.Summary.ReconciliationRunId}");
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var byId = await byIdResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        byId.Should().NotBeNull();
        byId!.Summary.MatchCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldReturnNotFoundWhenNoRunExists()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var client = app.GetTestClient();

        var createResponse = await client.PostAsJsonAsync("/api/workstation/reconciliation/runs", new ReconciliationRunRequest("missing-run"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var latestResponse = await client.GetAsync("/api/workstation/runs/missing-run/reconciliation");
        latestResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationHistoryRoute_ShouldReturnDescendingHistory()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var repository = app.Services.GetRequiredService<IReconciliationRunRepository>();
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-1",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            matchCount: 1,
            breakCount: 0));
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-2",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            matchCount: 2,
            breakCount: 1));
        await repository.SaveAsync(BuildReconciliationDetail(
            reconciliationRunId: "recon-3",
            runId: "run-history",
            createdAt: new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero),
            matchCount: 3,
            breakCount: 0));

        var client = app.GetTestClient();
        var historyResponse = await client.GetAsync("/api/workstation/runs/run-history/reconciliation/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await historyResponse.Content.ReadFromJsonAsync<List<ReconciliationRunSummary>>();
        history.Should().NotBeNull();
        history!.Should().HaveCount(3);
        history.Select(item => item.ReconciliationRunId).Should().ContainInOrder("recon-2", "recon-3", "recon-1");
        history[0].RunId.Should().Be("run-history");
        history[0].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero));
        history[0].BreakCount.Should().Be(1);
        history[1].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero));
        history[2].CreatedAt.Should().Be(new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationHistoryRoute_ShouldHandleEmptyHistory()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("run-no-history"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/run-no-history/reconciliation/history");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await response.Content.ReadFromJsonAsync<List<ReconciliationRunSummary>>();
        history.Should().NotBeNull();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReconciliationRoutes_ShouldReturnBreaksForMismatchedRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun("run-breaks"));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/reconciliation/runs", new ReconciliationRunRequest("run-breaks"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.Summary.BreakCount.Should().BeGreaterThan(0);
        created.Breaks.Should().NotBeEmpty();
        created.Breaks.Should().Contain(breakRow =>
            breakRow.Category == ReconciliationBreakCategory.AmountMismatch ||
            breakRow.Category == ReconciliationBreakCategory.MissingLedgerCoverage);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueRoute_ShouldHydrateQueueWithoutGovernanceBootstrap()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-queue-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/reconciliation/break-queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = await response.Content.ReadFromJsonAsync<List<ReconciliationBreakQueueItem>>(ServerJsonOptions);
        queue.Should().NotBeNull();
        queue!.Should().Contain(item =>
            item.RunId == runId &&
            reconciliation!.Breaks.Any(reconciliationBreak => item.BreakId == $"{runId}:{reconciliationBreak.CheckId}"));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueReviewRoute_ShouldHydrateQueueWithoutListBootstrap()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-review-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating the mismatch."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<ReconciliationBreakQueueItem>(ServerJsonOptions);
        updated.Should().NotBeNull();
        updated!.RunId.Should().Be(runId);
        updated.Status.Should().Be(ReconciliationBreakQueueStatus.InReview);
        updated.AssignedTo.Should().Be("ops-review");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_BreakQueueResolveRoute_ShouldRequireReviewBeforeResolve()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var runId = $"run-break-resolve-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        var reconciliation = await reconciliationService.RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();

        var invalidResolve = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/resolve",
            new ResolveReconciliationBreakRequest(
                BreakId: breakId,
                Status: ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "qa-resolve",
                ResolutionNote: "Skipping review should fail."));
        invalidResolve.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var review = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating."));
        review.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolve = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/resolve",
            new ResolveReconciliationBreakRequest(
                BreakId: breakId,
                Status: ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: "qa-resolve",
                ResolutionNote: "Issue resolved."));
        resolve.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolved = await resolve.Content.ReadFromJsonAsync<ReconciliationBreakQueueItem>(ServerJsonOptions);
        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
        resolved.ResolvedBy.Should().Be("qa-resolve");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunContinuityRoute_ShouldReturnSharedContinuityPayload()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun("run-continuity"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-continuity-paper",
            strategyId: "recon-strategy",
            strategyName: "Reconciliation Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero)) with
        {
            ParentRunId = "run-continuity",
            FundProfileId = "alpha-credit",
            FundDisplayName = "Alpha Credit"
        });

        var reconciliationService = app.Services.GetRequiredService<IReconciliationRunService>();
        await reconciliationService.RunAsync(new ReconciliationRunRequest("run-continuity"));

        var client = app.GetTestClient();
        using var continuity = await ReadJsonAsync(client, "/api/workstation/runs/run-continuity/continuity");

        continuity.RootElement.GetProperty("run").GetProperty("summary").GetProperty("runId").GetString().Should().Be("run-continuity");
        continuity.RootElement.GetProperty("run").GetProperty("summary").GetProperty("fundProfileId").GetString().Should().Be("alpha-credit");
        continuity.RootElement.GetProperty("lineage").GetProperty("childRuns").GetArrayLength().Should().Be(1);
        continuity.RootElement.GetProperty("lineage").GetProperty("childRuns")[0].GetProperty("runId").GetString().Should().Be("run-continuity-paper");
        continuity.RootElement.GetProperty("cashFlow").GetProperty("totalEntries").GetInt32().Should().Be(3);
        continuity.RootElement.GetProperty("cashFlow").GetProperty("projectedNetPosition").GetDecimal().Should().Be(-376m);
        continuity.RootElement.GetProperty("reconciliation").GetProperty("runId").GetString().Should().Be("run-continuity");
        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasCashFlow").GetBoolean().Should().BeTrue();
        continuity.RootElement.GetProperty("continuityStatus").GetProperty("hasReconciliation").GetBoolean().Should().BeTrue();
        continuity.RootElement
            .GetProperty("continuityStatus")
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(static warning => warning.GetProperty("code").GetString())
            .Should()
            .Contain("security-coverage");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunContinuityRoute_ShouldReturnNotFoundForMissingRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/no-such-run/continuity");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterRoutes_ShouldReturnSearchAndDetailPayloads()
    {
        var securityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var queryService = new StubSecurityMasterQueryService();
        queryService.Register(
            new SecuritySummaryDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                PrimaryIdentifier: "AAPL",
                Currency: "USD",
                Version: 4),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                Currency: "USD",
                CommonTerms: JsonSerializer.SerializeToElement(new { lotSize = 1 }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { primaryExchange = "NASDAQ" }),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Ticker,
                        "AAPL",
                        true,
                        new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero),
                        null,
                        null)
                ],
                Aliases: [],
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(queryService);
        });

        var client = app.GetTestClient();

        using var search = await ReadJsonAsync(client, "/api/workstation/security-master/securities?query=AAPL&take=5&activeOnly=true");
        var rows = search.RootElement;
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.SecurityId))).GetGuid().Should().Be(securityId);
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Status))).GetString().Should().Be("Active");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Classification))).GetProperty(CamelCase(nameof(SecurityClassificationSummaryDto.AssetClass))).GetString().Should().Be("Equity");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.Classification))).GetProperty(CamelCase(nameof(SecurityClassificationSummaryDto.PrimaryIdentifierValue))).GetString().Should().Be("AAPL");
        rows[0].GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.EffectiveFrom))).ValueKind.Should().Be(JsonValueKind.Null);

        using var detail = await ReadJsonAsync(client, $"/api/workstation/security-master/securities/{securityId}");
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.SecurityId))).GetGuid().Should().Be(securityId);
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.Currency))).GetString().Should().Be("USD");
        detail.RootElement.GetProperty(CamelCase(nameof(SecurityMasterWorkstationDto.EconomicDefinition))).GetProperty(CamelCase(nameof(SecurityEconomicDefinitionSummaryDto.Version))).GetInt64().Should().Be(4);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterSearch_ShouldRequireQuery()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(new StubSecurityMasterQueryService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/security-master/securities");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterHistory_ShouldReturnHistoryPayload()
    {
        var securityId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterHistory(
            securityId,
            [
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 101,
                    SecurityId: securityId,
                    StreamVersion: 2,
                    EventType: "SecurityAmended",
                    EventTimestamp: new DateTimeOffset(2026, 3, 22, 12, 0, 0, TimeSpan.Zero),
                    Actor: "governance.user",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(new { field = "displayName" }),
                    Metadata: JsonSerializer.SerializeToElement(new { source = "test" })),
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 100,
                    SecurityId: securityId,
                    StreamVersion: 1,
                    EventType: "SecurityCreated",
                    EventTimestamp: new DateTimeOffset(2026, 3, 21, 12, 0, 0, TimeSpan.Zero),
                    Actor: "governance.user",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(new { displayName = "Sample Security" }),
                    Metadata: JsonSerializer.SerializeToElement(new { source = "test" }))
            ]);

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(queryService);
        });

        var client = app.GetTestClient();
        using var history = await ReadJsonAsync(client, $"/api/workstation/security-master/securities/{securityId}/history?take=1");
        history.RootElement.GetArrayLength().Should().Be(1);
        history.RootElement[0].GetProperty("eventType").GetString().Should().Be("SecurityAmended");
        history.RootElement[0].GetProperty("streamVersion").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterHistory_ShouldReturnNotFoundWithoutEvents()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(new StubSecurityMasterQueryService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{Guid.NewGuid()}/history");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnTypedWinningSourceProvenance()
    {
        var securityId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "golden-edm",
                    sourceRecordId = "EDM-123",
                    asOf = "2026-04-20T09:30:00Z",
                    updatedBy = "workflow.bot",
                    reason = "golden-copy"
                })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.EconomicDefinition.WinningSourceSystem.Should().Be("golden-edm");
        snapshot.EconomicDefinition.WinningSourceRecordId.Should().Be("EDM-123");
        snapshot.EconomicDefinition.WinningSourceUpdatedBy.Should().Be("workflow.bot");
        snapshot.ProvenanceCandidates.Should().ContainSingle(candidate =>
            candidate.IsWinningSource &&
            candidate.SourceSystem == "golden-edm" &&
            candidate.SourceRecordId == "EDM-123");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnOnlySelectedSecurityChallengers()
    {
        var selectedSecurityId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var otherSecurityId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var selectedConflictId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(selectedSecurityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(selectedSecurityId, "Apple Inc.", "AAPL"));
        queryService.RegisterSecurity(
            CreateSecuritySummary(otherSecurityId, "Microsoft Corp.", "MSFT"),
            CreateSecurityDetail(otherSecurityId, "Microsoft Corp.", "MSFT"));
        queryService.RegisterEconomicDefinition(
            selectedSecurityId,
            CreateEconomicDefinitionRecord(
                selectedSecurityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflicts = new[]
        {
            new SecurityMasterConflict(
                ConflictId: selectedConflictId,
                SecurityId: selectedSecurityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "AAPL",
                ProviderB: "vendor-b",
                ValueB: "AAPL.O",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                SecurityId: otherSecurityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "MSFT",
                ProviderB: "vendor-c",
                ValueB: "MSFT.O",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.Zero),
                Status: "Open")
        };

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService(conflicts));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{selectedSecurityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ProvenanceCandidates.Count(candidate => !candidate.IsWinningSource).Should().Be(1);
        snapshot.ProvenanceCandidates.Should().OnlyContain(candidate =>
            candidate.IsWinningSource || candidate.ConflictId == selectedConflictId);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldPreserveWinnerByDefault()
    {
        var securityId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        var conflictId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL US"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL US"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL US",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflict = new SecurityMasterConflict(
            ConflictId: conflictId,
            SecurityId: securityId,
            ConflictKind: "IdentifierMismatch",
            FieldPath: "Identifiers.Primary",
            ProviderA: "golden-edm",
            ValueA: "AAPL US",
            ProviderB: "vendor-b",
            ValueB: "AAPL UW",
            DetectedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero),
            Status: "Open");

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([conflict]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ConflictAssessments[0].Recommendation.Should().Be(SecurityMasterConflictRecommendationKind.PreserveWinner);
        snapshot.ConflictAssessments[0].RecommendedResolution.Should().Be("AcceptA");
        snapshot.ConflictAssessments[0].RecommendedWinner.Should().Contain("Preserve golden-edm");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldDismissEquivalentNormalizedValues()
    {
        var securityId = Guid.Parse("cccccccc-1111-1111-1111-111111111111");
        var conflictId = Guid.Parse("cccccccc-2222-2222-2222-222222222222");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Berkshire Hathaway", "BRK-B"),
            CreateSecurityDetail(securityId, "Berkshire Hathaway", "BRK-B"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Berkshire Hathaway",
                "BRK-B",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflict = new SecurityMasterConflict(
            ConflictId: conflictId,
            SecurityId: securityId,
            ConflictKind: "IdentifierMismatch",
            FieldPath: "Identifiers.Primary",
            ProviderA: "golden-edm",
            ValueA: "BRK-B",
            ProviderB: "vendor-b",
            ValueB: "BRK/B",
            DetectedAt: new DateTimeOffset(2026, 4, 20, 12, 30, 0, TimeSpan.Zero),
            Status: "Open");

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([conflict]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ConflictAssessments[0].Recommendation.Should().Be(SecurityMasterConflictRecommendationKind.DismissAsEquivalent);
        snapshot.ConflictAssessments[0].RecommendedResolution.Should().Be("Dismiss");
        snapshot.ConflictAssessments[0].IsBulkEligible.Should().BeTrue();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterBulkResolve_ShouldResolveOnlyEligibleConflicts()
    {
        var securityId = Guid.Parse("dddddddd-1111-1111-1111-111111111111");
        var equivalentConflictId = Guid.Parse("dddddddd-2222-2222-2222-222222222222");
        var blankWinnerConflictId = Guid.Parse("dddddddd-3333-3333-3333-333333333333");
        var skippedConflictId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "BRK-B"),
            CreateSecurityDetail(securityId, string.Empty, "BRK-B"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                string.Empty,
                "BRK-B",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflictService = new StubSecurityMasterConflictService(
        [
            new SecurityMasterConflict(
                ConflictId: equivalentConflictId,
                SecurityId: securityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "BRK-B",
                ProviderB: "vendor-b",
                ValueB: "BRK/B",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 0, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: blankWinnerConflictId,
                SecurityId: securityId,
                ConflictKind: "FieldMismatch",
                FieldPath: "DisplayName",
                ProviderA: "golden-edm",
                ValueA: "",
                ProviderB: "vendor-b",
                ValueB: "Apple Inc.",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 5, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: skippedConflictId,
                SecurityId: securityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "BRK-B",
                ProviderB: "vendor-b",
                ValueB: "MSFT",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 10, 0, TimeSpan.Zero),
                Status: "Open")
        ]);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, conflictService);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/api/workstation/security-master/conflicts/bulk-resolve",
            new BulkResolveSecurityMasterConflictsRequest(
                ConflictIds: [equivalentConflictId, blankWinnerConflictId, skippedConflictId],
                ResolvedBy: "desktop-user",
                Reason: "bulk assist",
                FundProfileId: "fund-alpha"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkResolveSecurityMasterConflictsResult>(ServerJsonOptions);

        result.Should().NotBeNull();
        result!.Requested.Should().Be(3);
        result.Eligible.Should().Be(2);
        result.Resolved.Should().Be(2);
        result.Skipped.Should().Be(1);
        result.ResolvedConflictIds.Should().Contain([equivalentConflictId, blankWinnerConflictId]);
        result.SkippedReasons.Should().ContainKey(skippedConflictId);
        conflictService.ResolvedRequests.Select(request => request.ConflictId)
            .Should()
            .Contain([equivalentConflictId, blankWinnerConflictId]);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnMetricsForEachRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-1",
            strategyId: "s1",
            strategyName: "Alpha Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-2",
            strategyId: "s2",
            strategyName: "Beta Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds = new[] { "cmp-1", "cmp-2" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var rows = doc.RootElement.EnumerateArray().ToArray();
        rows.Should().HaveCount(2);
        rows.Select(r => r.GetProperty("runId").GetString()).Should().Contain("cmp-1").And.Contain("cmp-2");
        rows.Select(r => r.GetProperty("strategyName").GetString()).Should().Contain("Alpha Strategy").And.Contain("Beta Strategy");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnBadRequestForSingleRunId()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds = new[] { "only-one" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldFilterByModeWhenRequested()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-filter-paper",
            strategyId: "s1",
            strategyName: "Alpha Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-filter-backtest",
            strategyId: "s1",
            strategyName: "Alpha Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new
        {
            runIds = new[] { "cmp-filter-paper", "cmp-filter-backtest" },
            modes = new[] { "paper" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var rows = doc.RootElement.EnumerateArray().ToArray();
        rows.Should().HaveCount(1);
        rows[0].GetProperty("runId").GetString().Should().Be("cmp-filter-paper");
        rows[0].GetProperty("mode").GetString().Should().Be("Paper");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunHistoryAndTimeline_ShouldRespectCrossModeFilteringAndSorting()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "hist-backtest",
            strategyId: "s-history",
            strategyName: "History Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "hist-paper",
            strategyId: "s-history",
            strategyName: "History Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "hist-live",
            strategyId: "s-history",
            strategyName: "History Strategy",
            runType: RunType.Live,
            startedAt: new DateTimeOffset(2026, 3, 22, 10, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        using var history = await ReadJsonAsync(client, "/api/workstation/runs/history?mode=paper,live&limit=10");
        history.RootElement.GetArrayLength().Should().Be(2);
        history.RootElement[0].GetProperty("runId").GetString().Should().Be("hist-live");
        history.RootElement[1].GetProperty("runId").GetString().Should().Be("hist-paper");

        using var timeline = await ReadJsonAsync(client, "/api/workstation/runs/timeline?mode=paper,live&limit=10");
        timeline.RootElement.GetArrayLength().Should().Be(2);
        timeline.RootElement[0].GetProperty("mode").GetString().Should().Be("Live");
        timeline.RootElement[1].GetProperty("mode").GetString().Should().Be("Paper");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DiffRuns_ShouldReturnPositionAndMetricDeltas()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "diff-base",
            strategyId: "s-diff",
            strategyName: "Diff Base",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "diff-target",
            strategyId: "s-diff-2",
            strategyName: "Diff Target",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/diff",
            new { baseRunId = "diff-base", targetRunId = "diff-target" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("baseRunId").GetString().Should().Be("diff-base");
        doc.RootElement.GetProperty("targetRunId").GetString().Should().Be("diff-target");
        doc.RootElement.GetProperty("baseStrategyName").GetString().Should().Be("Diff Base");
        doc.RootElement.GetProperty("targetStrategyName").GetString().Should().Be("Diff Target");
        doc.RootElement.GetProperty("metrics").GetProperty("netPnlDelta").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DiffRuns_ShouldReturnNotFoundWhenRunMissing()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/diff",
            new { baseRunId = "no-such-run", targetRunId = "also-missing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);
        builder.Services.TryAddSingleton<IReconciliationBreakQueueRepository>(_ =>
            new FileReconciliationBreakQueueRepository(
                Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                NullLogger<FileReconciliationBreakQueueRepository>.Instance));

        var app = builder.Build();
        app.MapWorkstationEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

        await app.StartAsync();
        return app;
    }

    private static void RegisterRunReadServices(IServiceCollection services)
    {
        var store = new StrategyRunStore();
        services.AddSingleton<IStrategyRepository>(store);
        services.AddSingleton<PortfolioReadService>();
        services.AddSingleton<LedgerReadService>();
        services.AddSingleton<StrategyRunReadService>();
        services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        services.AddSingleton<IReconciliationBreakQueueRepository>(_ =>
            new FileReconciliationBreakQueueRepository(
                Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                NullLogger<FileReconciliationBreakQueueRepository>.Instance));
        services.AddSingleton<ReconciliationProjectionService>();
        services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        services.AddSingleton<CashFlowProjectionService>();
        services.AddSingleton<StrategyRunContinuityService>();
        services.AddSingleton<WorkstationWorkflowSummaryService>();
    }

    private static void RegisterSecurityMasterWorkbenchServices(
        IServiceCollection services,
        StubSecurityMasterQueryService queryService,
        StubSecurityMasterConflictService conflictService)
    {
        services.AddSingleton<ISecurityMasterQueryService>(queryService);
        services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(queryService);
        services.AddSingleton<ISecurityMasterConflictService>(conflictService);
        services.AddSingleton<ISecurityMasterIngestStatusService>(new StubSecurityMasterIngestStatusService());
        RegisterRunReadServices(services);
        services.AddSingleton<ReportGenerationService>();
        services.AddSingleton<ISecurityMasterWorkbenchQueryService, SecurityMasterWorkbenchQueryService>();
    }

    private static SecuritySummaryDto CreateSecuritySummary(Guid securityId, string displayName, string primaryIdentifier)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            PrimaryIdentifier: primaryIdentifier,
            Currency: "USD",
            Version: 4);

    private static SecurityDetailDto CreateSecurityDetail(Guid securityId, string displayName, string primaryIdentifier)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: "USD",
            CommonTerms: CreateEmptyJson(),
            AssetSpecificTerms: CreateEmptyJson(),
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    primaryIdentifier,
                    true,
                    new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
                    null,
                    null)
            ],
            Aliases: [],
            Version: 4,
            EffectiveFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null);

    private static SecurityEconomicDefinitionRecord CreateEconomicDefinitionRecord(
        Guid securityId,
        string displayName,
        string primaryIdentifier,
        JsonElement provenance)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            AssetFamily: "Public Equity",
            SubType: "CommonStock",
            TypeName: "Common Stock",
            IssuerType: "Corporate",
            RiskCountry: "US",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: "USD",
            Classification: CreateEmptyJson(),
            CommonTerms: CreateEmptyJson(),
            EconomicTerms: CreateEmptyJson(),
            Provenance: provenance,
            Version: 4,
            EffectiveFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    primaryIdentifier,
                    true,
                    new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
                    null,
                    null)
            ],
            LegacyAssetClass: null,
            LegacyAssetSpecificTerms: null);

    private static TradingParametersDto CreateTradingParameters(Guid securityId)
        => new(
            SecurityId: securityId,
            LotSize: 1m,
            TickSize: 0.01m,
            ContractMultiplier: null,
            MarginRequirementPct: null,
            TradingHoursUtc: "13:30-20:00",
            CircuitBreakerThresholdPct: null,
            AsOf: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero));

    private static OrderState CreateExecutionOrderState(string orderId, string symbol, decimal quantity) => new()
    {
        OrderId = orderId,
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = Meridian.Execution.Sdk.OrderType.Market,
        Quantity = quantity,
        Status = Meridian.Execution.Sdk.OrderStatus.Accepted,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow
    };

    private static ExecutionReport CreateExecutionFill(string orderId, string symbol, decimal quantity, decimal fillPrice) => new()
    {
        OrderId = orderId,
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Buy,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = fillPrice,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static JsonElement CreateEmptyJson()
        => JsonDocument.Parse("{}").RootElement.Clone();

    private static KernelObservabilityService CreateRecoveredKernelObservability()
    {
        var observability = new KernelObservabilityService();

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "data-ops",
                Symbol: $"baseline-{index}");
            RecordKernelObservation(
                observability,
                context,
                BuildKernelSuccessResult(context, "route-steady", ["healthy-route"]),
                score: 96);
        }

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "data-ops",
                Symbol: $"critical-{index}");
            RecordKernelObservation(
                observability,
                context,
                BuildKernelCriticalResult(context, "route-review", ["manual-review"]),
                score: 12);
        }

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "data-ops",
                Symbol: $"recovery-{index}");
            RecordKernelObservation(
                observability,
                context,
                BuildKernelSuccessResult(context, "route-steady", ["healthy-route"]),
                score: 97);
        }

        return observability;
    }

    private static void RecordKernelObservation(
        KernelObservabilityService observability,
        ProviderRouteContext context,
        ProviderRouteResult result,
        double score)
    {
        var scope = observability.BeginExecution(context);
        var healthByConnection = result.SelectedDecision is null
            ? new Dictionary<string, ProviderConnectionHealthSnapshot>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProviderConnectionHealthSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [result.SelectedDecision.ConnectionId] = new(
                    result.SelectedDecision.ConnectionId,
                    result.SelectedDecision.ProviderFamilyId,
                    result.SelectedDecision.IsHealthy,
                    result.SelectedDecision.IsHealthy ? "healthy" : "degraded",
                    score,
                    DateTimeOffset.UtcNow)
            };

        observability.RecordResult(context, result, healthByConnection, scope);
    }

    private static ProviderRouteResult BuildKernelSuccessResult(
        ProviderRouteContext context,
        string connectionId,
        IReadOnlyList<string>? reasonCodes = null)
    {
        var selected = new ProviderRouteDecision(
            connectionId,
            "alpha",
            context.Capability,
            ProviderSafetyMode.HealthAwareFailover,
            ScopeRank: 0,
            Priority: 0,
            IsHealthy: true,
            ReasonCodes: reasonCodes ?? [],
            FallbackConnectionIds: []);

        return new ProviderRouteResult(
            context,
            selected,
            Candidates: [selected],
            SkippedCandidates: []);
    }

    private static ProviderRouteResult BuildKernelCriticalResult(
        ProviderRouteContext context,
        string connectionId,
        IReadOnlyList<string>? reasonCodes = null)
    {
        var selected = new ProviderRouteDecision(
            connectionId,
            "beta",
            context.Capability,
            ProviderSafetyMode.ManualApprovalRequired,
            ScopeRank: 0,
            Priority: 0,
            IsHealthy: true,
            ReasonCodes: reasonCodes ?? [],
            FallbackConnectionIds: []);

        return new ProviderRouteResult(
            context,
            selected,
            Candidates: [selected],
            SkippedCandidates: [],
            RequiresManualApproval: true);
    }

    private static string CamelCase(string propertyName) => JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task<OperatorWorkflowHomeSummary> ReadWorkflowSummaryAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();
        return summary!;
    }

    private static WorkspaceWorkflowSummary GetWorkspace(OperatorWorkflowHomeSummary summary, string workspaceId)
        => summary.Workspaces.Single(workspace =>
            string.Equals(workspace.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase));

    private static StrategyRunEntry BuildRun(
        string runId,
        string strategyId,
        string strategyName,
        RunType runType,
        DateTimeOffset startedAt,
        string? datasetReference = null,
        string? feedReference = null)
    {
        return StrategyRunEntry.Start(strategyId, strategyName, runType) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddMinutes(30),
            DatasetReference = datasetReference,
            FeedReference = feedReference,
            PortfolioId = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-ledger",
            AuditReference = $"audit-{runId}"
        };
    }

    private static StrategyRunEntry BuildActivePaperRun(string runId, bool withBreaks)
    {
        if (withBreaks)
        {
            var mismatched = BuildReconciliationMismatchRun(runId);
            return mismatched with
            {
                RunType = RunType.Paper,
                EndedAt = mismatched.EndedAt,
                Engine = "BrokerPaper",
                TerminalStatus = StrategyRunStatus.Running,
                ParentRunId = "workflow-backtest-candidate",
                FundProfileId = "northwind-income",
                FundDisplayName = "Northwind Income",
                AuditReference = $"audit-{runId}"
            };
        }

        var startedAt = new DateTimeOffset(2026, 3, 22, 14, 0, 0, TimeSpan.Zero);
        var portfolioAsOf = startedAt.AddMinutes(30);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 1_000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: portfolioAsOf,
            Date: DateOnly.FromDateTime(portfolioAsOf.UtcDateTime),
            Cash: 600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 1_000m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_000m,
            GrossPnl: 0m,
            NetPnl: 0m,
            TotalReturn: 0m,
            AnnualizedReturn: 0m,
            SharpeRatio: 0d,
            SortinoRatio: 0d,
            CalmarRatio: 0d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 3, 21),
                To: new DateOnly(2026, 3, 22),
                Symbols: ["AAPL"],
                InitialCash: 1_000m,
                DataRoot: "./data"),
            Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: CreateWorkflowBalancedLedger(startedAt, portfolioAsOf),
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 42);

        return StrategyRunEntry.Start("workflow-paper-strategy", "Workflow Paper Strategy", RunType.Paper) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = portfolioAsOf,
            Metrics = result,
            DatasetReference = "dataset/us/equities",
            FeedReference = "synthetic:equities",
            PortfolioId = "workflow-paper-portfolio",
            LedgerReference = "workflow-paper-ledger",
            AuditReference = $"audit-{runId}",
            Engine = "BrokerPaper",
            TerminalStatus = StrategyRunStatus.Running,
            ParentRunId = "workflow-backtest-candidate",
            FundProfileId = "northwind-income",
            FundDisplayName = "Northwind Income"
        };
    }

    private static StrategyRunEntry BuildReconciliationReadyRun(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var ledger = CreateLedger();
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m),
            ["TSLA"] = new("TSLA", -5, 30m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            Equity: 1000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            TotalEquity: 1000m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 20),
            To: new DateOnly(2026, 3, 21),
            Symbols: ["AAPL", "TSLA"],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_000m,
            GrossPnl: 0m,
            NetPnl: 0m,
            TotalReturn: 0m,
            AnnualizedReturn: 0m,
            SharpeRatio: 0d,
            SortinoRatio: 0d,
            CalmarRatio: 0d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 0,
            WinningTrades: 0,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["AAPL", "TSLA"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 100);

        return StrategyRunEntry.Start("recon-strategy", "Reconciliation Strategy", RunType.Backtest) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            DatasetReference = "dataset/us/equities",
            FeedReference = "synthetic:equities",
            PortfolioId = "recon-portfolio",
            LedgerReference = "recon-ledger",
            AuditReference = $"audit-{runId}"
        };
    }

    private static StrategyRunEntry BuildReconciliationMismatchRun(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var ledger = CreateMismatchedLedger();
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m),
            ["TSLA"] = new("TSLA", -5, 30m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            Equity: 1000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            TotalEquity: 1000m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 20),
            To: new DateOnly(2026, 3, 21),
            Symbols: ["AAPL", "TSLA"],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_000m,
            GrossPnl: 0m,
            NetPnl: 0m,
            TotalReturn: 0m,
            AnnualizedReturn: 0m,
            SharpeRatio: 0d,
            SortinoRatio: 0d,
            CalmarRatio: 0d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 0,
            WinningTrades: 0,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["AAPL", "TSLA"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 100);

        return StrategyRunEntry.Start("recon-break-strategy", "Reconciliation Break Strategy", RunType.Backtest) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            DatasetReference = "dataset/us/equities",
            FeedReference = "synthetic:equities",
            PortfolioId = "recon-break-portfolio",
            LedgerReference = "recon-break-ledger",
            AuditReference = $"audit-{runId}"
        };
    }

    private static StrategyRunEntry BuildContinuityRun(string runId)
    {
        var run = BuildReconciliationReadyRun(runId);
        var cashFlows = new CashFlowEntry[]
        {
            new TradeCashFlow(run.StartedAt.AddMinutes(10), -400m, "AAPL", 10L, 40m),
            new CommissionCashFlow(run.StartedAt.AddMinutes(10), -1m, "AAPL", Guid.NewGuid()),
            new DividendCashFlow(run.StartedAt.AddDays(3), 25m, "AAPL", 10L, 2.5m)
        };

        return run with
        {
            Metrics = run.Metrics! with
            {
                CashFlows = cashFlows
            },
            FundProfileId = "alpha-credit",
            FundDisplayName = "Alpha Credit"
        };
    }

    private static BacktestResult BuildBacktestResultWithSymbol(string symbol)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = new(symbol, 10, 40m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 1_150m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 1_150m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 20),
            To: new DateOnly(2026, 3, 21),
            Symbols: [symbol],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_150m,
            GrossPnl: 150m,
            NetPnl: 150m,
            TotalReturn: 0.15m,
            AnnualizedReturn: 0.15m,
            SharpeRatio: 1.0d,
            SortinoRatio: 1.0d,
            CalmarRatio: 1.0d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, startedAt, "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, completedAt, $"Buy {symbol}",
        [
            (LedgerAccounts.Securities(symbol), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>([symbol], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 100);
    }

    private static ReconciliationRunDetail BuildReconciliationDetail(
        string reconciliationRunId,
        string runId,
        DateTimeOffset createdAt,
        int matchCount,
        int breakCount)
    {
        var matches = Enumerable.Range(0, matchCount)
            .Select(index => new ReconciliationMatchDto(
                CheckId: $"match-{index + 1}",
                Label: $"Match {index + 1}",
                ExpectedSource: "portfolio",
                ActualSource: "ledger",
                ExpectedAmount: 100m + index,
                ActualAmount: 100m + index,
                Variance: 0m,
                ExpectedAsOf: createdAt,
                ActualAsOf: createdAt))
            .ToArray();

        var breaks = Enumerable.Range(0, breakCount)
            .Select(index => new ReconciliationBreakDto(
                CheckId: $"break-{index + 1}",
                Label: $"Break {index + 1}",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakStatus.Open,
                MissingSource: "ledger",
                ExpectedAmount: 100m + index,
                ActualAmount: 95m + index,
                Variance: 5m,
                Severity: ReconciliationBreakSeverity.Medium,
                Reason: "Seeded mismatch for history coverage",
                ExpectedAsOf: createdAt,
                ActualAsOf: createdAt))
            .ToArray();

        var summary = new ReconciliationRunSummary(
            ReconciliationRunId: reconciliationRunId,
            RunId: runId,
            CreatedAt: createdAt,
            PortfolioAsOf: createdAt,
            LedgerAsOf: createdAt,
            MatchCount: matches.Length,
            BreakCount: breaks.Length,
            OpenBreakCount: breaks.Length,
            HasTimingDrift: false,
            AmountTolerance: 0.01m,
            MaxAsOfDriftMinutes: 5);

        return new ReconciliationRunDetail(summary, matches, breaks);
    }

    private static global::Meridian.Ledger.Ledger CreateLedger()
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 10, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 20, 0, TimeSpan.Zero), "Open TSLA short",
        [
            (LedgerAccounts.Cash, 150m, 0m),
            (LedgerAccounts.ShortSecuritiesPayable("TSLA"), 0m, 150m)
        ]);
        return ledger;
    }

    private static global::Meridian.Ledger.Ledger CreateMismatchedLedger()
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 17, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 17, 10, 0, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 350m, 0m),
            (LedgerAccounts.Cash, 0m, 350m)
        ]);
        return ledger;
    }

    private static global::Meridian.Ledger.Ledger CreateWorkflowBalancedLedger(
        DateTimeOffset startedAt,
        DateTimeOffset portfolioAsOf)
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, startedAt, "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, portfolioAsOf, "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        return ledger;
    }

    private static void PostBalancedEntry(
        global::Meridian.Ledger.Ledger ledger,
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
    {
        var journalId = Guid.NewGuid();
        var ledgerLines = lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description))
            .ToArray();
        ledger.Post(new JournalEntry(journalId, timestamp, description, ledgerLines));
    }

    // -----------------------------------------------------------------------
    // Drill-in route tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MapWorkstationEndpoints_EquityCurveRoute_ShouldReturnCurveForRunWithSnapshots()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithMultipleSnapshots("drillcurve-1", 50_000m);
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillcurve-1/equity-curve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("runId").GetString().Should().Be("drillcurve-1");
        doc.RootElement.GetProperty("points").GetArrayLength().Should().Be(3);
        doc.RootElement.GetProperty("sharpeRatio").GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_EquityCurveRoute_ShouldReturn404ForMissingRun()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/no-such-run/equity-curve");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FillsRoute_ShouldReturnAllFills()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithFills("drillfills-1", 3);
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillfills-1/fills");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("runId").GetString().Should().Be("drillfills-1");
        doc.RootElement.GetProperty("mode").GetString().Should().Be("Backtest");
        doc.RootElement.GetProperty("totalFills").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("fills").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FillsRoute_ShouldFilterBySymbol()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithFills("drillfills-2", 4);
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        // All fills are for MSFT in the helper; querying for AAPL should return 0.
        var response = await client.GetAsync("/api/workstation/runs/drillfills-2/fills?symbol=AAPL");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("totalFills").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("fills").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_AttributionRoute_ShouldReturnSymbolBreakdown()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var run = StrategyRunDrillInTests_BuildRunWithAttribution("drillattr-1");
        await store.RecordRunAsync(run);

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillattr-1/attribution");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("runId").GetString().Should().Be("drillattr-1");
        doc.RootElement.GetProperty("mode").GetString().Should().Be("Backtest");
        doc.RootElement.GetProperty("bySymbol").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerTrialBalanceRoute_ShouldReturnAllLines()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drilltb-1"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drilltb-1/ledger/trial-balance");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerTrialBalanceRoute_ShouldFilterByAccountType()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drilltb-2"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drilltb-2/ledger/trial-balance?accountType=Asset");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        // Every returned line must be of accountType Asset
        foreach (var line in doc.RootElement.EnumerateArray())
        {
            line.GetProperty("accountType").GetString().Should().Be("Asset");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerJournalRoute_ShouldReturnAllEntries()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drillj-1"));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/runs/drillj-1/ledger/journal");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_LedgerJournalRoute_ShouldFilterByFromDate()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationReadyRun("drillj-2"));

        var client = app.GetTestClient();
        // Use a future date; all ledger entries are at 2026-03-21, so nothing should match.
        var future = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var encoded = Uri.EscapeDataString(future.ToString("O"));
        var response = await client.GetAsync($"/api/workstation/runs/drillj-2/ledger/journal?from={encoded}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_GetStrategyRunsRoute_ShouldReturnRunsForStrategy()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun("strat-run-1", "strat-alpha", "Alpha Strategy", RunType.Backtest, DateTimeOffset.UtcNow.AddHours(-2)));
        await store.RecordRunAsync(BuildRun("strat-run-2", "strat-alpha", "Alpha Strategy", RunType.Paper, DateTimeOffset.UtcNow.AddHours(-1)));
        await store.RecordRunAsync(BuildRun("strat-run-3", "strat-beta", "Beta Strategy", RunType.Backtest, DateTimeOffset.UtcNow));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/strategies/strat-alpha/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var ids = doc.RootElement.EnumerateArray().Select(r => r.GetProperty("runId").GetString()).ToArray();
        ids.Should().Contain("strat-run-1").And.Contain("strat-run-2");
        ids.Should().NotContain("strat-run-3");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_GetStrategyRunsRoute_ShouldFilterByType()
    {
        await using var app = await CreateAppAsync(services => RegisterRunReadServices(services));
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun("typed-run-1", "strat-gamma", "Gamma Strategy", RunType.Backtest, DateTimeOffset.UtcNow.AddHours(-2)));
        await store.RecordRunAsync(BuildRun("typed-run-2", "strat-gamma", "Gamma Strategy", RunType.Paper, DateTimeOffset.UtcNow.AddHours(-1)));

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/strategies/strat-gamma/runs?type=Backtest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var ids = doc.RootElement.EnumerateArray().Select(r => r.GetProperty("runId").GetString()).ToArray();
        ids.Should().Contain("typed-run-1");
        ids.Should().NotContain("typed-run-2");
    }

    // Helper shims to reuse StrategyRunDrillInTests factory logic directly

    private static StrategyRunEntry StrategyRunDrillInTests_BuildRunWithMultipleSnapshots(string runId, decimal initialEquity)
    {
        var startedAt = new DateTimeOffset(2026, 1, 2, 9, 30, 0, TimeSpan.Zero);
        var equities = new[] { initialEquity, initialEquity * 0.95m, initialEquity * 0.98m };
        var snapshots = equities
            .Select((eq, i) => new PortfolioSnapshot(
                Timestamp: startedAt.AddDays(i),
                Date: DateOnly.FromDateTime(startedAt.AddDays(i).UtcDateTime),
                Cash: eq * 0.3m,
                MarginBalance: 0m,
                LongMarketValue: eq * 0.7m,
                ShortMarketValue: 0m,
                TotalEquity: eq,
                DailyReturn: 0m,
                Positions: new Dictionary<string, Position>(),
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
                DayCashFlows: []))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddDays(2).UtcDateTime),
            Symbols: ["SPY"],
            InitialCash: initialEquity);

        var metrics = new BacktestMetrics(
            InitialCapital: initialEquity, FinalEquity: equities[^1],
            GrossPnl: equities[^1] - initialEquity + 50m, NetPnl: equities[^1] - initialEquity,
            TotalReturn: (equities[^1] - initialEquity) / initialEquity,
            AnnualizedReturn: 0.06m, SharpeRatio: 1.1, SortinoRatio: 1.3, CalmarRatio: 0.8,
            MaxDrawdown: initialEquity * 0.05m, MaxDrawdownPercent: 0.05m, MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 1.4, WinRate: 0.55, TotalTrades: 6, WinningTrades: 4, LosingTrades: 2,
            TotalCommissions: 50m, TotalMarginInterest: 10m, TotalShortRebates: 2m, Xirr: 0.07,
            SymbolAttribution: new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
            {
                ["SPY"] = new("SPY", equities[^1] - initialEquity, 0m, 6, 50m, 8m)
            });

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY" },
            Snapshots: snapshots, CashFlows: [], Fills: [], Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(30), TotalEventsProcessed: 300);

        return new StrategyRunEntry(
            RunId: runId, StrategyId: "curve-strat", StrategyName: "Curve Strategy",
            RunType: RunType.Backtest, StartedAt: startedAt, EndedAt: startedAt.AddDays(3),
            Metrics: result);
    }

    private static StrategyRunEntry StrategyRunDrillInTests_BuildRunWithFills(string runId, int fillCount)
    {
        var startedAt = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
        var fills = Enumerable.Range(0, fillCount)
            .Select(i => new FillEvent(
                FillId: Guid.NewGuid(), OrderId: Guid.NewGuid(), Symbol: "MSFT",
                FilledQuantity: 10L, FillPrice: 350m + i, Commission: 1.00m,
                FilledAt: startedAt.AddMinutes(i * 5), AccountId: "default-brokerage"))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddDays(1).UtcDateTime),
            Symbols: ["MSFT"], InitialCash: 100_000m);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 101_000m, GrossPnl: 1_010m, NetPnl: 1_000m,
            TotalReturn: 0.01m, AnnualizedReturn: 0.12m, SharpeRatio: 1.5, SortinoRatio: 1.8,
            CalmarRatio: 0.9, MaxDrawdown: 200m, MaxDrawdownPercent: 0.002m, MaxDrawdownRecoveryDays: 2,
            ProfitFactor: 2.0, WinRate: 0.70, TotalTrades: fillCount, WinningTrades: fillCount,
            LosingTrades: 0, TotalCommissions: fillCount * 1.00m, TotalMarginInterest: 0m,
            TotalShortRebates: 0m, Xirr: 0.10,
            SymbolAttribution: new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
            {
                ["MSFT"] = new("MSFT", 1_000m, 0m, fillCount, fillCount * 1.00m, 0m)
            });

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MSFT" },
            Snapshots: [], CashFlows: [], Fills: fills, Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(5), TotalEventsProcessed: 100);

        return new StrategyRunEntry(
            RunId: runId, StrategyId: "fill-strat", StrategyName: "Fill Strategy",
            RunType: RunType.Backtest, StartedAt: startedAt, EndedAt: startedAt.AddDays(1),
            Metrics: result);
    }

    private static StrategyRunEntry StrategyRunDrillInTests_BuildRunWithAttribution(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);
        var attribution = new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
        {
            ["AAPL"] = new("AAPL", 5_000m, 1_200m, 8, 45m, 20m),
            ["MSFT"] = new("MSFT", 3_000m, -200m, 4, 22m, 10m),
            ["SPY"] = new("SPY", 1_000m, 500m, 2, 10m, 5m)
        };

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddMonths(1).UtcDateTime),
            Symbols: ["AAPL", "MSFT", "SPY"], InitialCash: 100_000m);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 110_500m, GrossPnl: 10_577m, NetPnl: 10_500m,
            TotalReturn: 0.105m, AnnualizedReturn: 0.42m, SharpeRatio: 2.1, SortinoRatio: 2.5,
            CalmarRatio: 1.2, MaxDrawdown: 1_500m, MaxDrawdownPercent: 0.015m, MaxDrawdownRecoveryDays: 4,
            ProfitFactor: 3.2, WinRate: 0.75, TotalTrades: 14, WinningTrades: 11, LosingTrades: 3,
            TotalCommissions: 77m, TotalMarginInterest: 0m, TotalShortRebates: 0m, Xirr: 0.40,
            SymbolAttribution: attribution);

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT", "SPY" },
            Snapshots: [], CashFlows: [], Fills: [], Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(2), TotalEventsProcessed: 500);

        return new StrategyRunEntry(
            RunId: runId, StrategyId: "attr-strat", StrategyName: "Attribution Strategy",
            RunType: RunType.Backtest, StartedAt: startedAt, EndedAt: startedAt.AddMonths(1),
            Metrics: result);
    }

    private sealed class StubSecurityReferenceLookup : ISecurityReferenceLookup
    {
        private readonly Dictionary<string, WorkstationSecurityReference> _references = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string symbol, WorkstationSecurityReference reference)
        {
            _references[symbol] = reference;
        }

        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            _references.TryGetValue(symbol, out var reference);
            return Task.FromResult<WorkstationSecurityReference?>(reference);
        }
    }

    private sealed class StubSecurityMasterQueryService :
        ISecurityMasterQueryService,
        Meridian.Application.SecurityMaster.ISecurityMasterQueryService
    {
        private readonly Dictionary<Guid, SecurityDetailDto> _details = [];
        private readonly Dictionary<Guid, IReadOnlyList<SecurityMasterEventEnvelope>> _history = [];
        private readonly Dictionary<Guid, SecurityEconomicDefinitionRecord> _economicDefinitions = [];
        private readonly Dictionary<Guid, TradingParametersDto> _tradingParameters = [];
        private readonly Dictionary<Guid, IReadOnlyList<CorporateActionDto>> _corporateActions = [];
        private readonly List<SecuritySummaryDto> _summaries = [];

        public void Register(SecuritySummaryDto summary, SecurityDetailDto detail)
            => RegisterSecurity(summary, detail);

        public void RegisterSecurity(SecuritySummaryDto summary, SecurityDetailDto detail)
        {
            _summaries.Add(summary);
            _details[detail.SecurityId] = detail;
        }

        public void RegisterEconomicDefinition(Guid securityId, SecurityEconomicDefinitionRecord record)
        {
            _economicDefinitions[securityId] = record;
        }

        public void RegisterTradingParameters(Guid securityId, TradingParametersDto tradingParameters)
        {
            _tradingParameters[securityId] = tradingParameters;
        }

        public void RegisterCorporateActions(Guid securityId, IReadOnlyList<CorporateActionDto> corporateActions)
        {
            _corporateActions[securityId] = corporateActions;
        }

        public void RegisterHistory(Guid securityId, IReadOnlyList<SecurityMasterEventEnvelope> history)
        {
            _history[securityId] = history;
        }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
        {
            _details.TryGetValue(securityId, out var detail);
            return Task.FromResult<SecurityDetailDto?>(detail);
        }

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default)
        {
            var detail = _details.Values.FirstOrDefault(item =>
                item.Identifiers.Any(id =>
                    id.Kind == identifierKind &&
                    string.Equals(id.Value, identifierValue, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult<SecurityDetailDto?>(detail);
        }

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
        {
            var rows = _summaries
                .Where(item =>
                    item.DisplayName.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ||
                    item.PrimaryIdentifier.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
                .Take(request.Take)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(rows);
        }

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
        {
            if (!_history.TryGetValue(request.SecurityId, out var history))
            {
                return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());
            }

            var rows = history.Take(request.Take).ToArray();
            return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(rows);
        }

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
        {
            _economicDefinitions.TryGetValue(securityId, out var record);
            return Task.FromResult<SecurityEconomicDefinitionRecord?>(record);
        }

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            _tradingParameters.TryGetValue(securityId, out var tradingParameters);
            return Task.FromResult<TradingParametersDto?>(tradingParameters);
        }

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        {
            if (_corporateActions.TryGetValue(securityId, out var corporateActions))
            {
                return Task.FromResult(corporateActions);
            }

            return Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());
        }

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);
        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }

    private sealed class StubSecurityMasterConflictService : ISecurityMasterConflictService
    {
        private readonly Dictionary<Guid, SecurityMasterConflict> _conflicts;

        public StubSecurityMasterConflictService(IReadOnlyList<SecurityMasterConflict> conflicts)
        {
            _conflicts = conflicts.ToDictionary(conflict => conflict.ConflictId);
        }

        public List<ResolveConflictRequest> ResolvedRequests { get; } = [];

        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(
                _conflicts.Values
                    .Where(conflict => string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(conflict => conflict.DetectedAt)
                    .ToArray());

        public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
        {
            _conflicts.TryGetValue(conflictId, out var conflict);
            return Task.FromResult<SecurityMasterConflict?>(conflict);
        }

        public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
        {
            ResolvedRequests.Add(request);
            if (!_conflicts.TryGetValue(request.ConflictId, out var existing))
            {
                return Task.FromResult<SecurityMasterConflict?>(null);
            }

            var updated = existing with
            {
                Status = string.Equals(request.Resolution, "Dismiss", StringComparison.OrdinalIgnoreCase)
                    ? "Dismissed"
                    : "Resolved"
            };
            _conflicts[request.ConflictId] = updated;
            return Task.FromResult<SecurityMasterConflict?>(updated);
        }

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class StubSecurityMasterIngestStatusService : ISecurityMasterIngestStatusService
    {
        public SecurityMasterIngestStatusSnapshot GetSnapshot()
            => new(
                ActiveImport: null,
                LastCompleted: null);
    }
}
