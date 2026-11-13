using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_FeatureCapabilities_ShouldExposeSharedDescriptorOverridesAndPersistToggle()
    {
        var configPath = Path.Combine(Path.GetTempPath(), "meridian-feature-capability-api-" + Guid.NewGuid().ToString("N"), "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, """
            {
              "FeatureCapabilities": {
                "Overrides": {
                  " desktop.data.security-master ": false,
                  "DESKTOP.DATA.SECURITY-MASTER": true
                }
              }
            }
            """);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
            services.AddSingleton<FeatureCapabilitySettingsService>();
        });
        var client = app.GetTestClient();

        var initial = await client.GetFromJsonAsync<FeatureCapabilitySettingsResponse>(
            "/api/workstation/settings/feature-capabilities",
            ServerJsonOptions);

        initial.Should().NotBeNull();
        initial!.Capabilities.Should().Contain(item => item.CapabilityKey == "desktop.settings.capabilities" && item.IsPermanent);
        initial.Capabilities.Should().Contain(item =>
            item.CapabilityKey == "desktop.data.security-master" &&
            item.IsEnabled == false &&
            item.IsOverridden);

        var response = await client.PutAsJsonAsync(
            "/api/workstation/settings/feature-capabilities/desktop.data.security-master",
            new FeatureCapabilityToggleRequest(true),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<FeatureCapabilitySettingsResponse>(ServerJsonOptions);
        updated.Should().NotBeNull();
        updated!.Capabilities.Should().Contain(item =>
            item.CapabilityKey == "desktop.data.security-master" &&
            item.IsEnabled &&
            item.IsOverridden);

        var persisted = Meridian.Application.UI.ConfigStore.LoadConfig(configPath);
        persisted.FeatureCapabilities.Should().NotBeNull();
        persisted.FeatureCapabilities!.Overrides.Should()
            .ContainKey("desktop.data.security-master")
            .WhoseValue.Should().BeTrue();
    }

    private sealed class LiveMarkTestPortfolioState : Meridian.Execution.Models.IPortfolioState
    {
        public LiveMarkTestPortfolioState(params Meridian.Execution.Models.ExecutionPosition[] positions)
        {
            Positions = positions.ToDictionary(
                position => position.Symbol,
                position => (Meridian.Execution.Sdk.IPosition)position,
                StringComparer.OrdinalIgnoreCase);
            UnrealisedPnl = positions.Sum(static p => p.UnrealisedPnl);
            RealisedPnl = positions.Sum(static p => p.RealisedPnl);
        }

        public decimal Cash => 100_000m;
        public decimal PortfolioValue => 250_000m;
        public decimal UnrealisedPnl { get; }
        public decimal RealisedPnl { get; }
        public IReadOnlyDictionary<string, Meridian.Execution.Sdk.IPosition> Positions { get; }
    }

    private sealed class ThrowingReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
    {
        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(
            ReconciliationBreakQueueStatus? status = null,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(
            ReviewReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(
            ResolveReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");

        public Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(
            string breakId,
            CancellationToken ct = default)
            => throw new IOException("Simulated break queue storage failure.");
    }

    private static void WriteReadyDk1Packet(
        string automationRoot,
        string? operatorSignoffJson = null,
        bool includeContractReview = true,
        string packetStatus = "ready-for-operator-review",
        string blockersJson = "[]")
    {
        var packetDirectory = Path.Combine(automationRoot, "unit-ready");
        Directory.CreateDirectory(packetDirectory);
        operatorSignoffJson ??= """
            {
              "requiredOwners": [ "Data", "Provider Reliability", "Trading" ],
              "status": "pending",
              "requiredBeforeDk1Exit": true
            }
            """;
        File.WriteAllText(
            Path.Combine(packetDirectory, "dk1-pilot-parity-packet.json"),
            """
            {
              "generatedAtUtc": "2026-04-25T20:28:38Z",
              "sourceSummary": "artifacts/provider-validation/_automation/unit-ready/wave1-validation-summary.json",
              "status": "__PACKET_STATUS__",
              "sampleReview": {
                "requiredCount": 4,
                "samples": [
                  {
                    "id": "DK1-ALPACA-QUOTE-GOLDEN",
                    "provider": "Alpaca",
                    "requiredStep": "Alpaca core provider confidence",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json",
                      "AlpacaQuotePipelineGoldenTests"
                    ],
                    "acceptanceCheck": "Golden quote pipeline fixture matched the committed output."
                  },
                  {
                    "id": "DK1-ALPACA-PARSER-EDGE-CASES",
                    "provider": "Alpaca",
                    "requiredStep": "Alpaca core provider confidence",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "AlpacaMessageParsingTests",
                      "AlpacaQuoteRoutingTests",
                      "AlpacaCredentialAndReconnectTests"
                    ],
                    "acceptanceCheck": "Parser edge cases preserved routing and reconnect behavior."
                  },
                  {
                    "id": "DK1-ROBINHOOD-SUPPORTED-SURFACE",
                    "provider": "Robinhood",
                    "requiredStep": "Robinhood supported surface",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "RobinhoodMarketDataClientTests",
                      "RobinhoodBrokerageGatewayTests",
                      "artifacts/provider-validation/robinhood/2026-04-09/manifest.json"
                    ],
                    "acceptanceCheck": "Bounded runtime packet and offline provider surface remain aligned."
                  },
                  {
                    "id": "DK1-YAHOO-HISTORICAL-FALLBACK",
                    "provider": "Yahoo",
                    "requiredStep": "Yahoo historical-only core provider",
                    "stepStatus": "passed",
                    "observed": true,
                    "status": "ready",
                    "missingRequirements": [],
                    "evidenceAnchors": [
                      "YahooFinanceHistoricalDataProviderTests",
                      "YahooFinanceIntradayContractTests"
                    ],
                    "acceptanceCheck": "Historical fallback fixtures remain stable without implying live readiness."
                  }
                ]
              },
              "evidenceDocuments": [
                { "name": "DK1 pilot parity runbook", "gate": "parity", "path": "docs/status/evidence/dk1-pilot-parity-runbook.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "DK1 trust rationale mapping", "gate": "explainability", "path": "docs/status/evidence/dk1-trust-rationale-mapping.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "DK1 baseline trust thresholds", "gate": "calibration", "path": "docs/status/evidence/dk1-baseline-trust-thresholds.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "Provider validation matrix", "gate": "parity", "path": "docs/status/provider-validation-matrix.md", "exists": true, "status": "validated", "missingRequirements": [] }
              ],
              __CONTRACT_REVIEW__
              "operatorSignoff": __OPERATOR_SIGNOFF__,
              "blockers": __BLOCKERS__
            }
            """
            .Replace("__PACKET_STATUS__", packetStatus)
            .Replace("__CONTRACT_REVIEW__", includeContractReview
                ? """
                  "trustRationaleContract": {
                    "documentPath": "docs/status/evidence/dk1-trust-rationale-mapping.md",
                    "requiredPayloadFields": [ "signalSource", "reasonCode", "recommendedAction" ],
                    "requiredReasonCodes": [
                      "HEALTHY_BASELINE",
                      "PROVIDER_STREAM_DEGRADED",
                      "RECONNECT_INSTABILITY",
                      "ERROR_RATE_SPIKE",
                      "LATENCY_REGRESSION",
                      "PARITY_DRIFT_DETECTED",
                      "DATA_COMPLETENESS_GAP",
                      "CALIBRATION_STALE"
                    ],
                    "status": "validated",
                    "missingRequirements": []
                  },
                  "baselineThresholdContract": {
                    "documentPath": "docs/status/evidence/dk1-baseline-trust-thresholds.md",
                    "requiredMetrics": [
                      "Composite trust score",
                      "Connection stability score",
                      "Error-rate score",
                      "Latency score",
                      "Reconnect score"
                    ],
                    "fpFnReviewRequired": true,
                    "status": "validated",
                    "missingRequirements": []
                  },
                """
                : string.Empty)
            .Replace("__OPERATOR_SIGNOFF__", operatorSignoffJson)
            .Replace("__BLOCKERS__", blockersJson));
    }

    private static void WriteProviderValidationEvidenceBundle(string automationRoot, string bundleJson)
    {
        var bundleDirectory = Path.Combine(automationRoot, "unit-ready");
        Directory.CreateDirectory(bundleDirectory);
        File.WriteAllText(
            Path.Combine(bundleDirectory, "provider-validation-evidence-bundle.json"),
            bundleJson);
    }

    private static void RegisterSecurityMasterWorkbenchServices(
        IServiceCollection services,
        StubSecurityMasterQueryService queryService,
        StubSecurityMasterConflictService conflictService,
        StubSecurityValidationService? validationService = null)
    {
        services.AddSingleton<ISecurityMasterQueryService>(queryService);
        services.AddSingleton<ISecurityValidationService>(validationService ?? new StubSecurityValidationService());
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

    private sealed class StubSecurityValidationService : ISecurityValidationService
    {
        private readonly Dictionary<Guid, SecurityValidationReportDto> _reports = new();

        public void RegisterReport(Guid securityId, SecurityValidationReportDto report)
            => _reports[securityId] = report;

        public Task<SecurityValidationReportDto> ValidateSecurityAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(
                _reports.TryGetValue(securityId, out var report)
                    ? report
                    : CreateReport(securityId, hasBlockingIssues: false, errorIssueCount: 0, issues: []));

        public Task<SecurityValidationReportDto> ValidateUniverseAsync(CancellationToken ct = default)
            => Task.FromResult(CreateReport(null, hasBlockingIssues: false, errorIssueCount: 0, issues: []));

        public SecurityValidationReportDto ValidateRecord(
            SecurityProjectionRecord record,
            IReadOnlyList<SecurityProjectionRecord> universe,
            DateTimeOffset? evaluatedAtUtc = null)
            => _reports.TryGetValue(record.SecurityId, out var report)
                ? report
                : CreateReport(record.SecurityId, hasBlockingIssues: false, errorIssueCount: 0, issues: []);

        private static SecurityValidationReportDto CreateReport(
            Guid? securityId,
            bool hasBlockingIssues,
            int errorIssueCount,
            IReadOnlyList<SecurityValidationIssueDto> issues)
            => new(
                SecurityId: securityId,
                Scope: securityId.HasValue ? "Security" : "Universe",
                EvaluatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
                HasBlockingIssues: hasBlockingIssues,
                CriticalIssueCount: 0,
                ErrorIssueCount: errorIssueCount,
                Issues: issues);
    }

    private sealed class RecordingReconciliationRunService : IReconciliationRunService
    {
        public int RunAsyncCallCount { get; private set; }
        public int GetLatestForRunCallCount { get; private set; }

        public Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
        {
            RunAsyncCallCount++;
            return Task.FromResult<ReconciliationRunDetail?>(null);
        }

        public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
        {
            GetLatestForRunCallCount++;
            return Task.FromResult<ReconciliationRunDetail?>(null);
        }

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([]);
    }

    private sealed class StaticReconciliationRunService(ReconciliationRunDetail detail) : IReconciliationRunService
    {
        public ReconciliationRunRequest? LastRunRequest { get; private set; }
        public int RunAsyncCallCount { get; private set; }
        public int GetLatestForRunCallCount { get; private set; }

        public Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
        {
            RunAsyncCallCount++;
            LastRunRequest = request;
            return Task.FromResult<ReconciliationRunDetail?>(detail);
        }

        public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default) =>
            string.Equals(reconciliationRunId, detail.Summary.ReconciliationRunId, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult<ReconciliationRunDetail?>(detail)
                : Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
        {
            GetLatestForRunCallCount++;
            return string.Equals(runId, detail.Summary.RunId, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult<ReconciliationRunDetail?>(detail)
                : Task.FromResult<ReconciliationRunDetail?>(null);
        }

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>(
                string.Equals(runId, detail.Summary.RunId, StringComparison.OrdinalIgnoreCase)
                    ? [detail.Summary]
                    : []);
    }

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

    private static async Task<OperationsTransitionResultDto> PostTransitionAsync(HttpClient client, string path, object payload)
    {
        var response = await client.PostAsJsonAsync(path, payload, ServerJsonOptions);
        var responseBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the transition response body was: {0}",
            responseBody);
        var result = JsonSerializer.Deserialize<OperationsTransitionResultDto>(
            responseBody,
            ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue(result.ErrorMessage);
        result.Workflow.Should().NotBeNull();
        return result;
    }

    private static async Task<OperationsContinuityWorkflowDto> AcknowledgeOperationsChecklistAsync(
        HttpClient client, OperationsContinuityWorkflowDto workflow)
    {
        foreach (var taskId in workflow.CloseChecklist
                     .Where(task => task.Gate != OperationsGateKeyDto.Approval)
                     .Select(task => task.TaskId))
        {
            var acknowledged = await PostTransitionAsync(client,
                $"/api/workstation/operations/continuity/{workflow.WorkflowId}/checklist/{taskId}/acknowledge",
                new OperationsChecklistAcknowledgeRequestDto(
                    workflow.Version, "spoofed-user", "Reviewed retained gate evidence."));
            workflow = acknowledged.Workflow!;
        }

        workflow.CloseChecklist.Where(task => task.Gate != OperationsGateKeyDto.Approval)
            .Should().OnlyContain(task => task.AcknowledgedBy == "ops-user" && task.AcknowledgedAtUtc != null);
        return workflow;
    }

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> RequiredOperationsChecklistControlApprovals(
        OperationsContinuityWorkflowDto workflow)
    {
        var controls = workflow.CloseChecklist
            .Where(task => task.Gate != OperationsGateKeyDto.Approval && task.AcknowledgedAtUtc.HasValue)
            .Select(task => new OperationsChecklistControlApprovalDto(
                task.TaskId, task.AcknowledgedBy!, task.AcknowledgedAtUtc!.Value))
            .ToList();
        if (workflow.ApprovalState == OperationsApprovalStateDto.Approved)
        {
            var approval = workflow.Approvals.Last(row => row.Status == OperationsApprovalStateDto.Approved);
            controls.Add(new("close-gate-approval", approval.Operator!, approval.SubmittedAtUtc!.Value));
            controls.Add(new("close-gate-approval", approval.Reviewer!, approval.DecidedAtUtc!.Value));
        }

        return controls;
    }

    private static OperationsBreakCaseDto CreateOperationsContinuityOpenBreak(Guid fundAccountId, string breakId) =>
        new(
            breakId,
            "cash-position-match",
            "Cash",
            "Critical",
            "Open",
            null,
            null,
            "Ledger cash",
            "Custodian cash",
            100m,
            null,
            100m,
            null,
            "CASH",
            "Assign an accountable owner",
            [],
            new OperationsContinuityCorrelationKeysDto(
                RunId: $"run-{breakId}",
                FundAccountId: fundAccountId,
                LedgerBatchId: "ledger-batch-1",
                ReconciliationCaseId: breakId));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, ServerJsonOptions);
        result.Should().NotBeNull($"expected a {typeof(T).Name} in response body, but got: {json}");
        return result!;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload, ServerJsonOptions), Encoding.UTF8, "application/json");

    private static string[] BuildPaperPromotionEvidenceReferences(string retainedEvidenceReference) =>
    [
        $"{PromotionApprovalChecklist.Dk1TrustPacketReviewed}:{retainedEvidenceReference}",
        $"{PromotionApprovalChecklist.RunLineageReviewed}:{retainedEvidenceReference}",
        $"{PromotionApprovalChecklist.PortfolioLedgerContinuityReviewed}:{retainedEvidenceReference}",
        $"{PromotionApprovalChecklist.RiskControlsReviewed}:{retainedEvidenceReference}"
    ];

    private static bool ContainsStringValue(JsonElement element, string expectedValue)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => string.Equals(element.GetString(), expectedValue, StringComparison.Ordinal),
            JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsStringValue(property.Value, expectedValue)),
            JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsStringValue(item, expectedValue)),
            _ => false
        };
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
        string? feedReference = null,
        string? fundProfileId = null)
    {
        var completedAt = startedAt.AddMinutes(30);
        return StrategyRunEntry.Start(strategyId, strategyName, runType) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            LifecycleEventAtUtc = completedAt,
            Reason = "Strategy run completed.",
            DatasetReference = datasetReference,
            FeedReference = feedReference,
            PortfolioId = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-ledger",
            AuditReference = $"audit-{runId}",
            FundProfileId = fundProfileId
        };
    }

    private static readonly ReconciliationBreakQueueScope TestReconciliationQueueScope =
        new("tenant-test", "tenant-test");

    private static async Task<IReadOnlyList<ReconciliationBreakQueueItem>> RetainScopedReconciliationBreaksAsync(
        WebApplication app,
        string runId,
        ReconciliationRunDetail reconciliation)
    {
        var repository = app.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
        var retained = reconciliation.Breaks
            .Select(sourceBreak => BuildBreakQueueItem($"{runId}:{sourceBreak.CheckId}", ledgerBookId: null) with
            {
                RunId = runId,
                StrategyName = "Reconciliation Break Strategy",
                Category = sourceBreak.Category,
                Variance = Math.Abs(sourceBreak.Variance),
                Reason = sourceBreak.Reason,
                Severity = sourceBreak.Severity,
                SourceReference = sourceBreak.CheckId,
                SourceBreakId = sourceBreak.CheckId,
                SourceFingerprint = $"fingerprint-{runId}-{sourceBreak.CheckId}",
                Measures = sourceBreak.Measures
                    ?? ReconciliationBreakQueueProjection.BuildDefaultMeasures(
                        sourceBreak.ExpectedAmount,
                        sourceBreak.ActualAmount,
                        sourceBreak.Variance,
                        reconciliation.Summary.AmountTolerance,
                        "currency")
            })
            .ToArray();

        foreach (var item in retained)
        {
            await repository.CreateIfMissingAsync(TestReconciliationQueueScope, item);
        }

        return retained;
    }

    private static ReconciliationBreakQueueItem BuildBreakQueueItem(string breakId, Guid? ledgerBookId)
    {
        var detectedAt = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        return new ReconciliationBreakQueueItem(
            BreakId: breakId,
            RunId: $"run-{breakId}",
            StrategyName: "Ledger Book Scope",
            Category: ReconciliationBreakCategory.AmountMismatch,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 25m,
            Reason: "Scoped ledger-book break.",
            AssignedTo: null,
            DetectedAt: detectedAt,
            LastUpdatedAt: detectedAt,
            Severity: ReconciliationBreakSeverity.High,
            ExceptionRoute: "accounting-variance-escalation",
            ToleranceProfileId: "critical-zero-tolerance",
            ToleranceBand: 0m,
            RequiredSignoffRole: "Accounting sign-off",
            SignoffStatus: "pending-signoff",
            FundAccountId: "fund-alpha",
            SourceType: "provider-ledger",
            SourceSystem: "provider-ledger-reconciliation",
            SourceReference: breakId,
            SourceBreakId: breakId,
            SourceFingerprint: $"fingerprint-{breakId}",
            LedgerBookId: ledgerBookId,
            Measures:
            [
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.Value,
                    null,
                    null,
                    null,
                    0m,
                    "currency",
                    "The source fixture does not provide a comparable value pair."),
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.Quantity,
                    null,
                    null,
                    null,
                    null,
                    "units",
                    "The source fixture does not provide comparable quantities."),
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.CostBasis,
                    null,
                    null,
                    null,
                    null,
                    "currency",
                    "The source fixture does not provide comparable cost basis.")
            ],
            BlockedOutputs: ["FinalReport", "PeriodClose"])
        {
            TenantId = TestReconciliationQueueScope.TenantId,
            CompanyId = TestReconciliationQueueScope.CompanyId
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
            AuditReference = $"audit-{runId}",
            FundProfileId = "fund-test"
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
            AuditReference = $"audit-{runId}",
            FundProfileId = "fund-test"
        };
    }

    private static StrategyRunEntry BuildContinuityRun(string runId, bool includeCashFlows = true, bool includeFills = false, StrategyRunPromotionState promotionState = StrategyRunPromotionState.None)
    {
        var run = BuildReconciliationReadyRun(runId);
        var cashFlows = includeCashFlows
            ? new CashFlowEntry[]
            {
                new TradeCashFlow(run.StartedAt.AddMinutes(10), -400m, "AAPL", 10L, 40m),
                new CommissionCashFlow(run.StartedAt.AddMinutes(10), -1m, "AAPL", Guid.NewGuid()),
                new DividendCashFlow(run.StartedAt.AddDays(3), 25m, "AAPL", 10L, 2.5m)
            }
            : [];
        var fills = includeFills
            ? new FillEvent[]
            {
                new(
                    FillId: Guid.NewGuid(),
                    OrderId: Guid.NewGuid(),
                    Symbol: "AAPL",
                    FilledQuantity: 10L,
                    FillPrice: 40m,
                    Commission: 1m,
                    FilledAt: run.StartedAt.AddMinutes(10),
                    AccountId: "default-brokerage")
            }
            : [];

        var runType = promotionState is StrategyRunPromotionState.CandidateForLive or StrategyRunPromotionState.LiveManaged
            ? RunType.Paper
            : run.RunType;

        return run with
        {
            RunType = runType,
            Metrics = run.Metrics! with
            {
                CashFlows = cashFlows,
                Fills = fills
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

    private static BacktestResult BuildBacktestResultWithOpenLots(
        string symbol,
        long quantity,
        decimal entryPrice,
        string accountId,
        string accountDisplayName,
        bool isShort = false,
        bool legacyLotWithoutDirection = false,
        IReadOnlyList<OpenLotBasisComponent>? basisComponents = null)
    {
        var startedAt = new DateTimeOffset(2026, 5, 14, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddDays(6);
        var openLot = new OpenLot(
            LotId: Guid.Parse("33333333-cccc-4444-dddd-333333333333"),
            Symbol: symbol,
            Quantity: quantity,
            EntryPrice: entryPrice,
            OpenedAt: startedAt,
            OpenFillId: Guid.Parse("44444444-dddd-4444-eeee-444444444444"),
            AccountId: accountId,
            Notes: "Trust remittance carry lot")
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m,
            IsShort = isShort && !legacyLotWithoutDirection,
            BasisComponents = basisComponents ?? []
        };
        var signedPositionQuantity = isShort ? -quantity : quantity;
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = new(symbol, signedPositionQuantity, openLot.CostBasis() / quantity, 12_500m, 0m, [openLot])
        };
        var longMarketValue = isShort ? 0m : 982_500m;
        var shortMarketValue = isShort ? -957_500m : 0m;
        var snapshotCash = isShort ? 2_190_000m : 250_000m;
        var totalEquity = snapshotCash + longMarketValue + shortMarketValue;
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: accountId,
            DisplayName: accountDisplayName,
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Structured Credit Broker",
            Cash: snapshotCash,
            MarginBalance: 0m,
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            Equity: totalEquity,
            Positions: positions,
            Rules: new FinancialAccountRules(),
            OpenLots: [openLot]);
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: snapshotCash,
            MarginBalance: 0m,
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            TotalEquity: totalEquity,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 5, 14),
            To: new DateOnly(2026, 5, 20),
            Symbols: [symbol],
            InitialCash: 1_220_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_220_000m,
            FinalEquity: 1_232_500m,
            GrossPnl: 12_500m,
            NetPnl: 12_500m,
            TotalReturn: 0.0102459016393442622950819672m,
            AnnualizedReturn: 0.0102459016393442622950819672m,
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
        if (isShort)
        {
            PostBalancedEntry(ledger, startedAt, $"Short sell {symbol}",
            [
                (LedgerAccounts.CashAccount(accountId), 970_000m, 0m),
                (LedgerAccounts.ShortSecuritiesPayable(symbol, accountId), 0m, 970_000m)
            ]);
        }
        else
        {
            PostBalancedEntry(ledger, startedAt, $"Buy {symbol}",
            [
                (LedgerAccounts.Securities(symbol, accountId), 970_000m, 0m),
                (LedgerAccounts.CashAccount(accountId), 0m, 970_000m)
            ]);
        }

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>([symbol], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(45),
            TotalEventsProcessed: 64);
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

    private static ReconciliationRunDetail BuildOperationsContinuityReconciliationDetail(
        string reconciliationRunId,
        string runId)
    {
        var createdAt = new DateTimeOffset(2026, 5, 20, 15, 30, 0, TimeSpan.Zero);
        var breaks = new[]
        {
            new ReconciliationBreakDto(
                CheckId: "bank-break-1",
                Label: "Bank cash missing ledger posting",
                Category: ReconciliationBreakCategory.MissingLedgerCoverage,
                Status: ReconciliationBreakStatus.Open,
                MissingSource: "ledger",
                ExpectedAmount: 250m,
                ActualAmount: null,
                Variance: 250m,
                Severity: ReconciliationBreakSeverity.Critical,
                Reason: "Normalized bank activity has no matching ledger posting.",
                ExpectedAsOf: createdAt,
                ActualAsOf: null)
        };
        var summary = new ReconciliationRunSummary(
            ReconciliationRunId: reconciliationRunId,
            RunId: runId,
            CreatedAt: createdAt,
            PortfolioAsOf: createdAt,
            LedgerAsOf: createdAt,
            MatchCount: 2,
            BreakCount: breaks.Length,
            OpenBreakCount: breaks.Length,
            HasTimingDrift: false,
            AmountTolerance: 0.05m,
            MaxAsOfDriftMinutes: 10,
            SecurityIssueCount: 1,
            HasSecurityCoverageIssues: true,
            BankTransactionCount: 2,
            BankBreakCount: 1,
            ExpectedAccountingEventCount: 3,
            ExpectedJournalPreviewCount: 2,
            SecurityMasterAccountingIssueCount: 1,
            HasSecurityMasterAccountingIssues: true);

        return new ReconciliationRunDetail(
            summary,
            Matches: [],
            Breaks: breaks,
            SecurityCoverageIssues:
            [
                new ReconciliationSecurityCoverageIssueDto(
                    Source: "ledger",
                    Symbol: "BOND1",
                    AccountName: "Fixed income",
                    Reason: "Security Master mapping requires review.",
                    Code: "SM_RECON_SECURITY_UNRESOLVED",
                    Severity: ReconciliationBreakSeverity.High,
                    EvidenceLink: "/workstation/data/security-master")
            ],
            SecurityMasterAccountingIssues:
            [
                new SecurityMasterAccountingIssueDto(
                    Code: "SM_ACCOUNTING_TERMS_INCOMPLETE",
                    Source: "security-master",
                    Symbol: "BOND1",
                    AccountId: "fund-account",
                    Reason: "Coupon day-count terms are incomplete.",
                    Severity: ReconciliationBreakSeverity.Critical,
                    EvidenceLink: "/workstation/data/security-master")
            ]);
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
}
