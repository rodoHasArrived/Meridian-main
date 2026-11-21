using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationCaseSummaryDto = Meridian.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldJoinSessionReplayAuditControlsAndPromotion()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness", Guid.NewGuid().ToString("N"));
        var automationRoot = Path.Combine(rootPath, "provider-validation", "_automation");
        const string promotionEvidenceReference = "evidence://evidence-vault/ev-000000000000000000000001";
        WriteReadyDk1Packet(automationRoot);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton(new Dk1TrustGateReadinessOptions(automationRoot));
            services.AddSingleton<Dk1TrustGateReadinessService>();
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
            services.AddSingleton<IGovernanceReportPackRepository>(_ => new FileGovernanceReportPackRepository(
                Path.Combine(rootPath, "report-packs"),
                NullLogger<FileGovernanceReportPackRepository>.Instance));
            services.AddSingleton<PromotionService>(sp => new PromotionService(
                sp.GetRequiredService<IStrategyRepository>(),
                sp.GetRequiredService<BacktestToLivePromoter>(),
                sp.GetRequiredService<IPromotionRecordStore>(),
                NullLogger<PromotionService>.Instance,
                operatorControls: sp.GetRequiredService<ExecutionOperatorControlService>(),
                auditTrail: sp.GetRequiredService<ExecutionAuditTrailService>()));
        }, currentUserPermissions: UserPermission.ViewTrades);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        const string fundProfileId = "wave2-readiness-fund";
        await store.RecordRunAsync(BuildRun(
            runId: "run-wave2-backtest",
            strategyId: "strat-wave2",
            strategyName: "Wave 2 Acceptance",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 24, 15, 30, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities",
            fundProfileId: fundProfileId).Complete(BuildBacktestResultWithSymbol("AAPL")) with
        {
            RunId = "run-wave2-backtest",
            AuditReference = "audit-run-wave2-backtest",
            FundProfileId = fundProfileId,
            FundDisplayName = "Wave 2 Readiness Fund",
            ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-test",
                ["workstationCompanyId"] = "tenant-test"
            },
            RetainedEvidenceReferences = [promotionEvidenceReference]
        });

        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-wave2",
            StrategyName: "Wave 2 Acceptance",
            InitialCash: 250_000m,
            Symbols: ["AAPL"]));
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-wave2", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-wave2", "AAPL", 10m, 190m));
        var verification = await persistence.VerifyReplayAsync(session.SessionId);

        var controls = app.Services.GetRequiredService<ExecutionOperatorControlService>();
        await controls.SetDefaultPositionLimitAsync(
            100_000m,
            "risk.lead",
            "Daily paper desk limit accepted for Wave 2.");

        var decision = await app.Services.GetRequiredService<PromotionService>().ApproveAsync(new PromotionApprovalRequest(
            RunId: "run-wave2-backtest",
            ApprovedBy: "ops.lead",
            ApprovalReason: "Replay, audit, and paper controls accepted for Wave 2.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: BuildPaperPromotionEvidenceReferences(promotionEvidenceReference)),
            new StrategyRunReadScope("tenant-test", "tenant-test"));

        decision.Success.Should().BeTrue();
        var reportPackRepository = app.Services.GetRequiredService<IGovernanceReportPackRepository>();
        var reportPack = await reportPackRepository.SaveAsync(
            new FundReportPackSnapshotDto(
                ReportId: Guid.NewGuid(),
                FundProfileId: fundProfileId,
                DisplayName: "Wave 2 Readiness Fund",
                ReportKind: GovernanceReportKindDto.TrialBalance,
                Currency: "USD",
                AsOf: new DateTimeOffset(2026, 4, 24, 16, 0, 0, TimeSpan.Zero),
                GeneratedAt: new DateTimeOffset(2026, 4, 24, 16, 5, 0, TimeSpan.Zero),
                TotalNetAssets: 250_000m,
                AuditActor: "ops.lead",
                CorrelationId: "wave2-report-pack",
                DecisionRationale: "Wave 2 readiness evidence accepted.",
                Provenance: new FundReportPackProvenanceDto(
                    RelatedRunIds: ["run-wave2-backtest"],
                    JournalEntryCount: 1,
                    LedgerEntryCount: 2,
                    TrialBalanceLineCount: 2,
                    ReconciliationRunCount: 0,
                    OpenReconciliationBreakCount: 0,
                    SecurityResolvedCount: 1,
                    SecurityMissingCount: 0,
                    LineagePointers: [],
                    SourceSnapshotHash: new string('a', 64)),
                Artifacts: [],
                Warnings: [])
            {
                Status = GovernanceReportPackStatusDto.Validated,
                ValidationIssues = [],
                LifecycleEvents =
                [
                    new FundReportPackLifecycleEventDto(
                        FromStatus: GovernanceReportPackStatusDto.Draft,
                        ToStatus: GovernanceReportPackStatusDto.Generated,
                        ChangedAt: new DateTimeOffset(2026, 4, 24, 16, 5, 0, TimeSpan.Zero),
                        Actor: "ops.lead",
                        Reason: "Test report pack generated.",
                        CorrelationId: "wave2-report-pack"),
                    new FundReportPackLifecycleEventDto(
                        FromStatus: GovernanceReportPackStatusDto.Generated,
                        ToStatus: GovernanceReportPackStatusDto.Validated,
                        ChangedAt: new DateTimeOffset(2026, 4, 24, 16, 5, 0, TimeSpan.Zero),
                        Actor: "ops.lead",
                        Reason: "Test report pack validated.",
                        CorrelationId: "wave2-report-pack")
                ]
            },
            [new GovernanceReportPackArtifactContent(
                "trial-balance",
                GovernanceReportArtifactFormatDto.Json,
                "trial-balance.json",
                Encoding.UTF8.GetBytes("""{"status":"ready"}"""))]);
        (await reportPackRepository.FindLatestByRunIdAsync("run-wave2-backtest"))
            .Should()
            .NotBeNull();

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
        readiness.Replay.LastPersistedFillAt.Should().NotBeNull();
        readiness.Replay.LastPersistedOrderUpdateAt.Should().NotBeNull();
        readiness.Replay.VerificationAuditId.Should().Be(verification!.VerificationAuditId);
        readiness.Controls.CircuitBreakerOpen.Should().BeFalse();
        readiness.Controls.ManualOverrideCount.Should().Be(0);
        readiness.Promotion.Should().NotBeNull();
        readiness.Promotion!.ApprovalStatus.Should().Be(PromotionDecisionKinds.Approved);
        readiness.Promotion.ApprovedBy.Should().Be("ops.lead");
        readiness.Promotion.AuditReference.Should().Be(decision.AuditReference);
        readiness.Promotion.ApprovalChecklist.Should().BeEquivalentTo(PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper));
        readiness.Controls.DefaultMaxPositionSize.Should().Be(100_000m);
        readiness.Controls.ExplainableEvidenceCount.Should().Be(1);
        readiness.Controls.UnexplainedEvidenceCount.Should().Be(0);
        readiness.Controls.RecentEvidence.Should().ContainSingle(evidence =>
            evidence.Action == "DefaultPositionLimitUpdated" &&
            evidence.Actor == "risk.lead" &&
            evidence.Scope == "default-position-limit" &&
            evidence.Reason.Contains("Daily paper desk limit", StringComparison.OrdinalIgnoreCase) &&
            evidence.IsExplained);
        readiness.TrustGate.Status.Should().Be("ready-for-operator-review");
        readiness.TrustGate.ReadyForOperatorReview.Should().BeTrue();
        readiness.TrustGate.RequiredSampleCount.Should().Be(4);
        readiness.TrustGate.ReadySampleCount.Should().Be(4);
        readiness.TrustGate.ValidatedEvidenceDocumentCount.Should().Be(4);
        readiness.TrustGate.SampleReviews.Should().ContainSingle(sample =>
            sample.SampleId == "DK1-ALPACA-QUOTE-GOLDEN" &&
            sample.Provider == "Alpaca" &&
            sample.AcceptanceCheck.Contains("golden", StringComparison.OrdinalIgnoreCase));
        readiness.TrustGate.EvidenceDocuments.Should().ContainSingle(document =>
            document.Gate == "explainability" &&
            document.Status == "validated" &&
            document.Path == "docs/status/evidence/dk1-trust-rationale-mapping.md");
        readiness.TrustGate.TrustRationaleContract.Should().NotBeNull();
        readiness.TrustGate.TrustRationaleContract!.Status.Should().Be("validated");
        readiness.TrustGate.TrustRationaleContract.RequiredReasonCodes.Should().Contain("PROVIDER_STREAM_DEGRADED");
        readiness.TrustGate.BaselineThresholdContract.Should().NotBeNull();
        readiness.TrustGate.BaselineThresholdContract!.Status.Should().Be("validated");
        readiness.TrustGate.BaselineThresholdContract.FpFnReviewRequired.Should().BeTrue();
        readiness.TrustGate.Detail.Should().Contain("explainability validated").And.Contain("calibration validated");
        readiness.TrustGate.OperatorSignoffStatus.Should().Be("pending");
        readiness.TrustGate.OperatorSignoff.Should().NotBeNull();
        readiness.TrustGate.OperatorSignoff!.MissingOwners.Should().BeEquivalentTo(
            ["Data", "Provider Reliability", "Trading"]);
        readiness.TrustGate.OperatorSignoff.SignedOwners.Should().BeEmpty();
        readiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.ReadyForPaperOperation.Should().BeFalse();
        readiness.AcceptanceGates.Should().HaveCount(9);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.SessionId == session.SessionId);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == verification!.VerificationAuditId);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "risk-rules" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "promotion" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == decision.AuditReference);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "dk1-trust" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.Detail.Contains("sign-off remains pending", StringComparison.OrdinalIgnoreCase));
        readiness.ReportPack.Should().NotBeNull();
        readiness.ReportPack!.Detail.Should().Contain("run-wave2-backtest");
        readiness.ReportPack.ReportId.Should().Be(reportPack.ReportId);
        readiness.ReportPack.RelatedRunIds.Should().Contain("run-wave2-backtest");
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "report-pack" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == reportPack.ReportId.ToString("D"));
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "reconciliation" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "brokerage-sync" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready);
        readiness.EvidenceCompleteness.Should().NotBeNull();
        readiness.EvidenceCompleteness!.ReadyGateCount.Should().Be(6);
        readiness.EvidenceCompleteness.TotalGateCount.Should().Be(9);
        readiness.EvidenceCompleteness.ReviewGateIds.Should().BeEquivalentTo(["risk-rules", "dk1-trust", "reconciliation"]);
        readiness.WorkItems.Should().ContainSingle(item =>
            item.Kind == OperatorWorkItemKindDto.ProviderTrustGate &&
            item.Tone == OperatorWorkItemToneDto.Warning &&
            item.Detail.Contains("sign-off remains pending", StringComparison.OrdinalIgnoreCase));
        readiness.WorkItems.Should().NotContain(item => item.Tone == OperatorWorkItemToneDto.Critical);
        readiness.WorkItems.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.PaperReplay);
        readiness.WorkItems.Should().NotContain(item => item.Kind == OperatorWorkItemKindDto.PromotionReview);

        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");
        var embeddedReadiness = trading.RootElement.GetProperty("readiness");
        embeddedReadiness
            .GetProperty("activeSession")
            .GetProperty("sessionId")
            .GetString()
            .Should()
            .Be(session.SessionId);
        embeddedReadiness.GetProperty("overallStatus").GetString().Should().Be(readiness.OverallStatus.ToString());
        embeddedReadiness.GetProperty("acceptanceGates").GetArrayLength().Should().Be(readiness.AcceptanceGates.Count);
    }

    [Fact]
    public async Task Scenario_BacktestToPaperCockpitReadiness_ApiPromotionSessionReplayAndStaleRecoveryStayTraceable()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-backtest-paper", Guid.NewGuid().ToString("N"));
        const string promotionEvidenceReference = "evidence://evidence-vault/ev-000000000000000000000002";

        await using var app = await CreateAppAsync(
            services =>
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
            },
            mapExecutionApi: true,
            mapPromotionApi: true,
            currentUserPermissions: UserPermission.ViewTrades | UserPermission.ManageStrategies | UserPermission.ExecuteTrades | UserPermission.ManageOrders);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-api-backtest",
            strategyId: "strat-api-paper",
            strategyName: "API Paper Continuity",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 25, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")) with
        {
            RunId = "run-api-backtest",
            AuditReference = "audit-run-api-backtest",
            ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-test",
                ["workstationCompanyId"] = "tenant-test"
            },
            RetainedEvidenceReferences = [promotionEvidenceReference]
        });

        var client = app.GetTestClient();

        var approveResponse = await client.PostAsync(
            UiApiRoutes.PromotionApprove,
            JsonContent(new PromotionApprovalRequest(
                RunId: "run-api-backtest",
                ReviewNotes: "Backtest qualifies for paper acceptance.",
                ApprovedBy: "spoofed",
                ApprovalReason: "Backtest evidence approved for paper cockpit validation.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
                EvidenceReferences: BuildPaperPromotionEvidenceReferences(promotionEvidenceReference))));
        approveResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var approval = await ReadAsync<PromotionDecisionResult>(approveResponse);
        approval.Success.Should().BeTrue();
        approval.NewRunId.Should().NotBeNullOrWhiteSpace();
        approval.ApprovedBy.Should().Be("ops-user");

        var wrongSessionResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-other",
                StrategyName: "Unrelated Strategy",
                InitialCash: 100_000m,
                Symbols: ["MSFT"])));
        wrongSessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var mismatchReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        mismatchReadiness.Should().NotBeNull();
        mismatchReadiness!.ActiveSession.Should().BeNull();
        mismatchReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "session" &&
            gate.Status == TradingAcceptanceGateStatusDto.Blocked &&
            gate.RunId == approval.NewRunId);
        mismatchReadiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-session-mismatch-{approval.NewRunId!.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Critical &&
            item.Detail.Contains("strat-api-paper", StringComparison.OrdinalIgnoreCase));

        var sessionResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-api-paper",
                StrategyName: "API Paper Continuity",
                InitialCash: 125_000m,
                Symbols: ["AAPL"])));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await ReadAsync<PaperSessionSummaryDto>(sessionResponse);

        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-api-1", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-api-1", "AAPL", 10m, 190m));

        var controls = app.Services.GetRequiredService<ExecutionOperatorControlService>();
        await controls.SetDefaultPositionLimitAsync(
            100_000m,
            "risk.lead",
            "Paper cockpit risk budget accepted for API workflow.");

        var replayResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await ReadAsync<PaperSessionReplayVerificationDto>(replayResponse);
        replay.IsConsistent.Should().BeTrue();
        replay.ComparedFillCount.Should().Be(1);
        replay.ComparedOrderCount.Should().Be(1);
        replay.VerificationAuditId.Should().NotBeNullOrWhiteSpace();

        var readyReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        readyReadiness.Should().NotBeNull();
        readyReadiness!.ActiveSession.Should().NotBeNull();
        readyReadiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        readyReadiness.Replay.Should().NotBeNull();
        readyReadiness.Replay!.VerificationAuditId.Should().Be(replay.VerificationAuditId);
        readyReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == replay.VerificationAuditId);
        readyReadiness.Controls.ExplainableEvidenceCount.Should().Be(1);
        readyReadiness.Controls.UnexplainedEvidenceCount.Should().Be(0);
        readyReadiness.Controls.RecentEvidence.Should().ContainSingle(evidence =>
            evidence.Action == "DefaultPositionLimitUpdated" &&
            evidence.Actor == "risk.lead" &&
            evidence.Scope == "default-position-limit" &&
            evidence.Reason.Contains("risk budget", StringComparison.OrdinalIgnoreCase) &&
            evidence.IsExplained);
        readyReadiness.Promotion.Should().NotBeNull();
        readyReadiness.Promotion!.SourceRunId.Should().Be("run-api-backtest");
        readyReadiness.Promotion.TargetRunId.Should().Be(approval.NewRunId);
        readyReadiness.Promotion.ApprovedBy.Should().Be("ops-user");
        readyReadiness.Promotion.AuditReference.Should().Be(approval.AuditReference);
        readyReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "promotion" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.RunId == "run-api-backtest" &&
            gate.AuditReference == approval.AuditReference,
            "actual promotion gates: {0}",
            string.Join(
                " | ",
                readyReadiness.AcceptanceGates
                    .Where(static gate => gate.GateId == "promotion")
                    .Select(static gate =>
                        $"status={gate.Status},run={gate.RunId},audit={gate.AuditReference},detail={gate.Detail}")));

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-api-2", "AAPL", 2m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-api-2", "AAPL", 2m, 191m));

        var staleReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        staleReadiness.Should().NotBeNull();
        staleReadiness!.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.AuditReference == replay.VerificationAuditId &&
            gate.Detail.Contains("stale", StringComparison.OrdinalIgnoreCase));
        staleReadiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Warning);

        var recoveryResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        recoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovery = await ReadAsync<PaperSessionReplayVerificationDto>(recoveryResponse);
        recovery.IsConsistent.Should().BeTrue();
        recovery.ComparedFillCount.Should().Be(2);
        recovery.ComparedOrderCount.Should().Be(2);
        recovery.VerificationAuditId.Should().NotBe(replay.VerificationAuditId);

        var recoveredReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            UiApiRoutes.WorkstationTradingReadiness,
            ServerJsonOptions);
        recoveredReadiness.Should().NotBeNull();
        recoveredReadiness!.Replay.Should().NotBeNull();
        recoveredReadiness.Replay!.VerificationAuditId.Should().Be(recovery.VerificationAuditId);
        recoveredReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == recovery.VerificationAuditId);
        recoveredReadiness.WorkItems.Should().NotContain(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}");

        var detailResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionById.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ReadAsync<PaperSessionDetailDto>(detailResponse);
        detail.Symbols.Should().Equal("AAPL");
        detail.OrderHistory.Should().HaveCount(2);
        detail.LedgerEntryCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldRefreshAfterReplayTrustAndPromotionStateChanges()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-refresh", Guid.NewGuid().ToString("N"));
        var automationRoot = Path.Combine(rootPath, "provider-validation", "_automation");
        const string promotionEvidenceReference = "evidence://evidence-vault/ev-000000000000000000000003";
        WriteReadyDk1Packet(automationRoot);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterPromotionServices(services, Path.Combine(rootPath, "promotions"));
            services.AddSingleton(new Dk1TrustGateReadinessOptions(automationRoot));
            services.AddSingleton<Dk1TrustGateReadinessService>();
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
        }, currentUserPermissions: UserPermission.ViewTrades);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-refresh-backtest",
            strategyId: "strat-refresh",
            strategyName: "Refresh Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 28, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")) with
        {
            RetainedEvidenceReferences = [promotionEvidenceReference]
        });

        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto("strat-refresh", "Refresh Strategy", 100_000m, ["AAPL"]));
        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("refresh-order-1", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("refresh-order-1", "AAPL", 10m, 190m));
        var verification = await persistence.VerifyReplayAsync(session.SessionId);

        var client = app.GetTestClient();
        var before = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(UiApiRoutes.WorkstationTradingReadiness, ServerJsonOptions);
        before.Should().NotBeNull();
        before!.Replay!.VerificationAuditId.Should().Be(verification!.VerificationAuditId);
        before.Promotion!.ApprovalStatus.Should().BeNull();
        before.TrustGate.OperatorSignoffStatus.Should().Be("pending");

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("refresh-order-2", "AAPL", 5m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("refresh-order-2", "AAPL", 5m, 191m));
        await app.Services.GetRequiredService<IPromotionRecordStore>().AppendAsync(new StrategyPromotionRecord(
            PromotionId: "promotion-refresh-backtest",
            StrategyId: "strat-refresh",
            StrategyName: "Refresh Strategy",
            SourceRunType: RunType.Backtest,
            TargetRunType: RunType.Paper,
            SourceRunId: "run-refresh-backtest",
            TargetRunId: "run-refresh-paper",
            QualifyingSharpe: 1.2d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: new DateTimeOffset(2026, 4, 28, 16, 0, 0, TimeSpan.Zero),
            ApprovalReason: "Ready after review.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: BuildPaperPromotionEvidenceReferences(promotionEvidenceReference),
            AuditReference: "audit-refresh-promotion",
            ApprovedBy: "ops.refresh"));
        WriteReadyDk1Packet(
            automationRoot,
            """
            {
              "requiredOwners": [ "Data", "Provider Reliability", "Trading" ],
              "signedOwners": [ "Data", "Provider Reliability", "Trading" ],
              "status": "signed",
              "requiredBeforeDk1Exit": true,
              "signedAtUtc": "2026-04-28T16:00:00Z"
            }
            """);

        var after = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(UiApiRoutes.WorkstationTradingReadiness, ServerJsonOptions);
        after.Should().NotBeNull();
        after!.ActiveSession!.OrderCount.Should().Be(2);
        after.Replay!.ComparedOrderCount.Should().Be(1);
        after.AcceptanceGates.Should().ContainSingle(g => g.GateId == "replay" && g.Status == TradingAcceptanceGateStatusDto.ReviewRequired);
        after.Promotion!.ApprovalStatus.Should().Be(PromotionDecisionKinds.Approved);
        after.TrustGate.OperatorSignoffStatus.Should().Be("signed");
        after.SnapshotVersion.Should().NotBe(before.SnapshotVersion);
        after.SnapshotMaterializedAt.Should().BeAfter(before.SnapshotMaterializedAt);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldNotUseStalePromotionHistoryForLatestRun()
    {
        var promotionRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "promotion-history",
            Guid.NewGuid().ToString("N"));
        const string stalePromotionEvidenceReference = "evidence://evidence-vault/ev-000000000000000000000004";
        const string currentRunEvidenceReference = "evidence://evidence-vault/ev-000000000000000000000005";

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            RegisterPromotionServices(services, promotionRoot);
        }, currentUserPermissions: UserPermission.ViewTrades);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-stale-backtest",
            strategyId: "stale-strategy",
            strategyName: "Stale Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 22, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("MSFT")) with
        {
            RetainedEvidenceReferences = [stalePromotionEvidenceReference]
        });

        await app.Services.GetRequiredService<IPromotionRecordStore>().AppendAsync(new StrategyPromotionRecord(
            PromotionId: "promotion-stale-backtest",
            StrategyId: "stale-strategy",
            StrategyName: "Stale Strategy",
            SourceRunType: RunType.Backtest,
            TargetRunType: RunType.Paper,
            SourceRunId: "run-stale-backtest",
            TargetRunId: "run-stale-paper",
            QualifyingSharpe: 1.2d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: new DateTimeOffset(2026, 4, 22, 15, 0, 0, TimeSpan.Zero),
            ApprovalReason: "Older promotion was approved for a previous cockpit run.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: BuildPaperPromotionEvidenceReferences(stalePromotionEvidenceReference),
            AuditReference: "audit-stale-promotion",
            ApprovedBy: "ops.archive"));

        await store.RecordRunAsync(BuildRun(
            runId: "run-current-backtest",
            strategyId: "current-strategy",
            strategyName: "Current Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 4, 24, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")) with
        {
            RetainedEvidenceReferences = [currentRunEvidenceReference]
        });

        var readiness = await app
            .GetTestClient()
            .GetFromJsonAsync<TradingOperatorReadinessDto>(
                "/api/workstation/trading/readiness",
                ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.Promotion.Should().NotBeNull();
        readiness.Promotion!.SourceRunId.Should().Be("run-current-backtest");
        readiness.Promotion.ApprovalStatus.Should().BeNull();
        readiness.Promotion.AuditReference.Should().Be("audit-run-current-backtest");
        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "promotion" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.RunId == "run-current-backtest" &&
            gate.AuditReference == "audit-run-current-backtest");
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "promotion-trace-incomplete-run-current-backtest" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview &&
            item.AuditReference == "audit-run-current-backtest");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldRequireReplayRefreshWhenSessionChangesAfterVerification()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-stale-replay", Guid.NewGuid().ToString("N"));

        await using var app = await CreateAppAsync(services =>
        {
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
        }, currentUserPermissions: UserPermission.ViewTrades);

        var client = app.GetTestClient();
        var persistence = app.Services.GetRequiredService<PaperSessionPersistenceService>();

        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: "strat-stale-replay",
            StrategyName: "Stale Replay",
            InitialCash: 100_000m,
            Symbols: ["AAPL", "MSFT"]));

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-before-replay", "AAPL", 10m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-before-replay", "AAPL", 10m, 190m));

        var verification = await persistence.VerifyReplayAsync(session.SessionId);
        verification.Should().NotBeNull();
        verification!.VerificationAuditId.Should().NotBeNullOrWhiteSpace();

        await persistence.RecordOrderUpdateAsync(session.SessionId, CreateExecutionOrderState("order-after-replay", "MSFT", 5m));
        await persistence.RecordFillAsync(session.SessionId, CreateExecutionFill("order-after-replay", "MSFT", 5m, 320m));

        var staleReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);

        staleReadiness.Should().NotBeNull();
        staleReadiness!.ActiveSession.Should().NotBeNull();
        staleReadiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        staleReadiness.ActiveSession.FillCount.Should().Be(2);
        staleReadiness.ActiveSession.OrderCount.Should().Be(2);
        staleReadiness.ActiveSession.LedgerEntryCount.Should().BeGreaterThan(verification.ComparedLedgerEntryCount);
        staleReadiness.Replay.Should().NotBeNull();
        staleReadiness.Replay!.ComparedFillCount.Should().Be(1);
        staleReadiness.Replay.ComparedOrderCount.Should().Be(1);
        staleReadiness.Replay.VerificationAuditId.Should().Be(verification.VerificationAuditId);
        staleReadiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        staleReadiness.ReadyForPaperOperation.Should().BeFalse();
        staleReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.AuditReference == verification.VerificationAuditId &&
            gate.Detail.Contains("stale", StringComparison.OrdinalIgnoreCase));
        staleReadiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Warning &&
            item.AuditReference == verification.VerificationAuditId &&
            item.Detail.Contains("orders active=2, verified=1", StringComparison.OrdinalIgnoreCase));

        var staleInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);
        staleInbox.Should().NotBeNull();
        staleInbox!.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}" &&
            item.AuditReference == verification.VerificationAuditId &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Tone == OperatorWorkItemToneDto.Warning);

        var refreshedVerification = await persistence.VerifyReplayAsync(session.SessionId);
        refreshedVerification.Should().NotBeNull();
        refreshedVerification!.ComparedFillCount.Should().Be(2);
        refreshedVerification.ComparedOrderCount.Should().Be(2);
        refreshedVerification.VerificationAuditId.Should().NotBe(verification.VerificationAuditId);

        var recoveredReadiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);
        recoveredReadiness.Should().NotBeNull();
        recoveredReadiness!.Replay.Should().NotBeNull();
        recoveredReadiness.Replay!.VerificationAuditId.Should().Be(refreshedVerification.VerificationAuditId);
        recoveredReadiness.Replay.ComparedFillCount.Should().Be(2);
        recoveredReadiness.Replay.ComparedOrderCount.Should().Be(2);
        recoveredReadiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "replay" &&
            gate.Status == TradingAcceptanceGateStatusDto.Ready &&
            gate.AuditReference == refreshedVerification.VerificationAuditId);
        recoveredReadiness.WorkItems.Should().NotContain(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}");

        var recoveredInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);
        recoveredInbox.Should().NotBeNull();
        recoveredInbox!.WorkItems.Should().NotContain(item =>
            item.WorkItemId == $"paper-replay-stale-{session.SessionId.ToLowerInvariant()}");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldFlagUnexplainedRiskControlAuditEvidence()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-risk", Guid.NewGuid().ToString("N"));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(_ => new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
                NullLogger<ExecutionAuditTrailService>.Instance));
        }, currentUserPermissions: UserPermission.ViewTrades);

        await app.Services.GetRequiredService<ExecutionAuditTrailService>().RecordAsync(new ExecutionAuditEntry(
            AuditId: "audit-risk-missing-context",
            Category: "Order",
            Action: "OrderRejected",
            Outcome: "Rejected",
            OccurredAt: new DateTimeOffset(2026, 4, 26, 18, 0, 0, TimeSpan.Zero)));

        var readiness = await app
            .GetTestClient()
            .GetFromJsonAsync<TradingOperatorReadinessDto>(
                "/api/workstation/trading/readiness",
                ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.Controls.UnexplainedEvidenceCount.Should().Be(1);
        readiness.Controls.ExplainableEvidenceCount.Should().Be(0);
        var evidence = readiness.Controls.RecentEvidence.Should().ContainSingle().Which;
        evidence.AuditId.Should().Be("audit-risk-missing-context");
        evidence.IsExplained.Should().BeFalse();
        evidence.MissingFields.Should().BeEquivalentTo(["actor", "scope", "reason"]);
        readiness.Controls.ExplainabilityWarnings.Should().ContainSingle(warning =>
            warning.Contains("OrderRejected", StringComparison.OrdinalIgnoreCase) &&
            warning.Contains("actor, scope, reason", StringComparison.OrdinalIgnoreCase));

        readiness.AcceptanceGates.Should().ContainSingle(gate =>
            gate.GateId == "audit-controls" &&
            gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
            gate.AuditReference == "audit-risk-missing-context");
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "execution-evidence-incomplete" &&
            item.Kind == OperatorWorkItemKindDto.ExecutionControl &&
            item.AuditReference == "audit-risk-missing-context" &&
            item.Detail.Contains("actor, scope, reason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_ShouldExposeRequiredContractFieldsAndDegradeWhenEvidenceIsMissing()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "meridian-tests", "workstation-readiness-contract", Guid.NewGuid().ToString("N"));
        var automationRoot = Path.Combine(rootPath, "provider-validation", "_automation");

        WriteReadyDk1Packet(
            automationRoot,
            packetStatus: "not-ready",
            blockersJson: """
                [
                  "DK1 trust packet requires refreshed provider parity evidence before operator review."
                ]
                """);

        try
        {
            await using var app = await CreateAppAsync(services =>
            {
                RegisterRunReadServices(services);
                RegisterPromotionServices(services, Path.Combine(rootPath, "promotions"));
                services.AddSingleton(new Dk1TrustGateReadinessOptions(automationRoot));
                services.AddSingleton<Dk1TrustGateReadinessService>();
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
            }, currentUserPermissions: UserPermission.ViewTrades);

            var store = app.Services.GetRequiredService<IStrategyRepository>();
            await store.RecordRunAsync(BuildRun(
                runId: "run-contract-backtest",
                strategyId: "strat-contract",
                strategyName: "Contract Coverage",
                runType: RunType.Backtest,
                startedAt: new DateTimeOffset(2026, 4, 28, 13, 0, 0, TimeSpan.Zero),
                datasetReference: "dataset/us/equities",
                feedReference: "synthetic:equities").Complete(BuildBacktestResultWithSymbol("AAPL")));

            var session = await app.Services.GetRequiredService<PaperSessionPersistenceService>().CreateSessionAsync(new CreatePaperSessionDto(
                StrategyId: "strat-contract",
                StrategyName: "Contract Coverage",
                InitialCash: 125_000m,
                Symbols: ["AAPL"]));

            var client = app.GetTestClient();
            using var response = await client.GetAsync(UiApiRoutes.WorkstationTradingReadiness);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var payload = await ReadJsonAsync(client, UiApiRoutes.WorkstationTradingReadiness);
            var root = payload.RootElement;
            root.TryGetProperty("overallStatus", out var overallStatus).Should().BeTrue();
            root.TryGetProperty("readyForPaperOperation", out var readyForPaperOperation).Should().BeTrue();
            root.TryGetProperty("trustGate", out var trustGate).Should().BeTrue();
            root.TryGetProperty("replay", out var replay).Should().BeTrue();
            root.TryGetProperty("promotion", out var promotion).Should().BeTrue();
            root.TryGetProperty("acceptanceGates", out var acceptanceGates).Should().BeTrue();
            root.TryGetProperty("workItems", out var workItems).Should().BeTrue();
            overallStatus.ValueKind.Should().Be(JsonValueKind.String);
            readyForPaperOperation.ValueKind.Should().Be(JsonValueKind.False);
            trustGate.ValueKind.Should().Be(JsonValueKind.Object);
            replay.ValueKind.Should().Be(JsonValueKind.Null);
            promotion.ValueKind.Should().Be(JsonValueKind.Object);
            acceptanceGates.ValueKind.Should().Be(JsonValueKind.Array);
            workItems.ValueKind.Should().Be(JsonValueKind.Array);

            var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
                UiApiRoutes.WorkstationTradingReadiness,
                ServerJsonOptions);
            readiness.Should().NotBeNull();

            readiness!.ActiveSession.Should().NotBeNull();
            readiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
            readiness.TrustGate.Should().NotBeNull();
            readiness.TrustGate.Status.Should().Be("not-ready");
            readiness.TrustGate.Blockers.Should().NotBeEmpty();
            readiness.Replay.Should().BeNull();
            readiness.Promotion.Should().NotBeNull();
            readiness.Promotion!.RequiresReview.Should().BeTrue();
            readiness.Promotion.ApprovalStatus.Should().BeNull();
            readiness.Promotion.State.Should().NotBeNullOrWhiteSpace();
            readiness.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);

            readiness.AcceptanceGates.Should().ContainSingle(gate =>
                gate.GateId == "dk1-trust" &&
                gate.Status == TradingAcceptanceGateStatusDto.Blocked);
            readiness.AcceptanceGates.Should().ContainSingle(gate =>
                gate.GateId == "replay" &&
                gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
                gate.SessionId == session.SessionId);
            readiness.AcceptanceGates.Should().ContainSingle(gate =>
                gate.GateId == "promotion" &&
                gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired &&
                gate.RunId == "run-contract-backtest");
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.ProviderTrustGate &&
                item.Tone == OperatorWorkItemToneDto.Critical);
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.PaperReplay &&
                item.Tone == OperatorWorkItemToneDto.Warning);
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.PromotionReview &&
                item.Tone == OperatorWorkItemToneDto.Warning);

            // Contract-level matrix for required readiness dimensions and deterministic critical-before-warning triage.
            trustGate.TryGetProperty("status", out var trustGateStatus).Should().BeTrue();
            trustGate.TryGetProperty("operatorSignoffStatus", out var trustGateSignoffStatus).Should().BeTrue();
            trustGateStatus.ValueKind.Should().Be(JsonValueKind.String);
            trustGateSignoffStatus.ValueKind.Should().Be(JsonValueKind.String);
            promotion.TryGetProperty("approvalChecklist", out var promotionChecklist).Should().BeTrue();
            promotionChecklist.ValueKind.Should().Be(JsonValueKind.Array);
            root.TryGetProperty("activeSession", out var activeSession).Should().BeTrue();
            activeSession.ValueKind.Should().Be(JsonValueKind.Object);
            root.TryGetProperty("brokerageSync", out var brokerageSync).Should().BeTrue();
            brokerageSync.ValueKind.Should().Be(JsonValueKind.Null);
            var readinessWorkItems = readiness.WorkItems.ToList();
            var firstCritical = readinessWorkItems.FindIndex(item => item.Tone == OperatorWorkItemToneDto.Critical);
            var firstWarning = readinessWorkItems.FindIndex(item => item.Tone == OperatorWorkItemToneDto.Warning);
            firstCritical.Should().BeGreaterThanOrEqualTo(0);
            firstWarning.Should().BeGreaterThanOrEqualTo(0);
            firstCritical.Should().BeLessThan(firstWarning, "critical readiness blockers should be triaged before warning items");
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadinessWithoutRegisteredReadinessService_ShouldUseSharedReadinessBuilder()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewTrades);

        var readiness = await app
            .GetTestClient()
            .GetFromJsonAsync<TradingOperatorReadinessDto>(
                "/api/workstation/trading/readiness",
                ServerJsonOptions);

        readiness.Should().NotBeNull();
        readiness!.OverallStatus.Should().Be(TradingAcceptanceGateStatusDto.Blocked);
        readiness.ReadyForPaperOperation.Should().BeFalse();
        readiness.AcceptanceGates
            .Select(static gate => gate.GateId)
            .Should()
            .Equal(
                "session",
                "replay",
                "audit-controls",
                "risk-rules",
                "promotion",
                "dk1-trust",
                "report-pack",
                "reconciliation",
                "brokerage-sync");
        readiness.ReportPack.Should().NotBeNull();
        readiness.ReportPack!.Status.Should().Be(TradingAcceptanceGateStatusDto.ReviewRequired);
        readiness.EvidenceCompleteness.Should().NotBeNull();
        readiness.EvidenceCompleteness!.ReviewGateIds.Should().Contain("report-pack");
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.Label == "No active paper session" &&
            item.Tone == OperatorWorkItemToneDto.Critical);
        readiness.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == "promotion-decision-missing" &&
            item.Label == "Promotion decision required");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingReadiness_WithFundAccountId_ShouldSurfaceBrokerageSyncFreshnessAndDivergencePosture()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-readiness", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            var status = await brokerageSync.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));

            await using var app = await CreateAppAsync(services =>
            {
                RegisterRunReadServices(services);
                services.AddSingleton(brokerageSync);
            }, currentUserPermissions: UserPermission.ViewTrades);
            var client = app.GetTestClient();

            var readiness = await client
                .GetFromJsonAsync<TradingOperatorReadinessDto>(
                    $"/api/workstation/trading/readiness?fundAccountId={fundAccountId:D}",
                    ServerJsonOptions);

            readiness.Should().NotBeNull();
            readiness!.BrokerageSync.Should().NotBeNull();
            readiness.BrokerageSync!.FundAccountId.Should().Be(fundAccountId);
            readiness.BrokerageSync.Health.Should().Be(WorkstationBrokerageSyncHealth.Failed);
            readiness.BrokerageSync.SecurityMissingCount.Should().BeGreaterThanOrEqualTo(0);
            readiness.BrokerageSync.Warnings.Should().Contain(w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == fundAccountId &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#alpaca-provider-setup");

            var trading = await client.GetFromJsonAsync<WorkstationTradingPayload>(
                $"/api/workstation/trading?fundAccountId={fundAccountId:D}",
                ServerJsonOptions);
            trading.Should().NotBeNull();
            trading!.Readiness.BrokerageSync.Should().NotBeNull();
            trading.Readiness.BrokerageSync!.FundAccountId.Should().Be(fundAccountId);
            trading.Readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == fundAccountId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldProjectTradingReadinessWorkItemsWithNavigation()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewTrades);

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.CriticalCount.Should().BeGreaterThanOrEqualTo(1);
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
        inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.Workspace == "Trading" &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness &&
            item.TargetPageTag == "TradingShell");
        inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == "promotion-decision-missing" &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness &&
            item.TargetPageTag == "TradingShell");
    }
}
