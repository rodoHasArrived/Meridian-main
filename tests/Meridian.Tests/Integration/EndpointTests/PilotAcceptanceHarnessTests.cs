using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Services.Acceptance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Acceptance harness for the pilot golden path: trusted dataset/run evidence, comparison,
/// paper-session replay, promotion audit, reconciliation, and governed report-pack lineage.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PilotAcceptanceHarnessTests
{
    private const string DatasetEvidenceId = "dataset/pilot/golden-aapl-2026-04-11";
    private const string FeedEvidenceId = "provider-evidence/dk1/unit-ready";

    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task PilotGoldenPath_ShouldRetainEvidenceIdsFromDataToReportPack()
    {
        await using var pilot = await CreatePilotAppAsync();
        var client = pilot.App.GetTestClient();

        var seed = await SeedPilotWorkspaceAsync(pilot.App.Services);

        var persistence = pilot.App.Services.GetRequiredService<PaperSessionPersistenceService>();
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto(
            StrategyId: seed.StrategyId,
            StrategyName: seed.StrategyName,
            InitialCash: 250_000m,
            Symbols: ["AAPL"]));
        await persistence.RecordOrderUpdateAsync(
            session.SessionId,
            CreateExecutionOrderState("pilot-order-001", "AAPL", 10m));
        await persistence.RecordFillAsync(
            session.SessionId,
            CreateExecutionFill("pilot-order-001", "AAPL", 10m, 190m));

        var replay = await persistence.VerifyReplayAsync(session.SessionId);
        replay.Should().NotBeNull();
        replay!.IsConsistent.Should().BeTrue();

        var promotion = await pilot.App.Services.GetRequiredService<PromotionService>().ApproveAsync(
            new PromotionApprovalRequest(
                RunId: seed.BacktestRunId,
                ApprovedBy: "pilot.operator",
                ApprovalReason: "Pilot harness replay, controls, and dataset evidence accepted.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)));
        promotion.Success.Should().BeTrue();

        var research = await client.GetFromJsonAsync<ResearchBriefingDto>(
            "/api/workstation/research/briefing",
            ServerJsonOptions);
        research.Should().NotBeNull();
        research!.RecentRuns.Should().Contain(run =>
            run.RunId == seed.PaperRunId &&
            run.Dataset == DatasetEvidenceId);

        var comparisonResponse = await client.PostAsJsonAsync(
            "/api/workstation/runs/compare",
            new { runIds = new[] { seed.BacktestRunId, seed.PaperRunId } },
            ServerJsonOptions);
        comparisonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var comparison = await comparisonResponse.Content.ReadFromJsonAsync<RunComparisonDto[]>(
            ServerJsonOptions);
        comparison.Should().NotBeNull();
        comparison!.Select(row => row.RunId).Should().BeEquivalentTo(seed.BacktestRunId, seed.PaperRunId);

        var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);
        readiness.Should().NotBeNull();
        readiness!.ActiveSession.Should().NotBeNull();
        readiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        readiness.Replay.Should().NotBeNull();
        readiness.Replay!.VerificationAuditId.Should().Be(replay.VerificationAuditId);
        readiness.Promotion.Should().NotBeNull();
        readiness.Promotion!.SourceRunId.Should().Be(seed.BacktestRunId);
        readiness.Promotion.ApprovalStatus.Should().Be(PromotionDecisionKinds.Approved);
        readiness.Promotion.AuditReference.Should().Be(promotion.AuditReference);

        var reconciliationResponse = await client.PostAsJsonAsync(
            "/api/workstation/reconciliation/runs",
            new ReconciliationRunRequest(seed.PaperRunId),
            ServerJsonOptions);
        reconciliationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconciliation = await reconciliationResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(
            ServerJsonOptions);
        reconciliation.Should().NotBeNull();
        reconciliation!.Summary.RunId.Should().Be(seed.PaperRunId);

        var continuity = await client.GetFromJsonAsync<StrategyRunContinuityDetail>(
            $"/api/workstation/runs/{seed.PaperRunId}/continuity",
            ServerJsonOptions);
        continuity.Should().NotBeNull();
        continuity!.Run.Summary.RunId.Should().Be(seed.PaperRunId);
        continuity.Lineage.ParentRunId.Should().Be(seed.BacktestRunId);
        continuity.ContinuityStatus.HasPortfolio.Should().BeTrue();
        continuity.ContinuityStatus.HasLedger.Should().BeTrue();
        continuity.ContinuityStatus.HasReconciliation.Should().BeTrue();
        var portfolioEvidenceId = continuity.Run.Summary.PortfolioId;
        var ledgerEvidenceId = continuity.Run.Summary.LedgerReference;
        portfolioEvidenceId.Should().NotBeNullOrWhiteSpace();
        ledgerEvidenceId.Should().NotBeNullOrWhiteSpace();

        var evidencePacket = await client.GetFromJsonAsync<EvidencePacketDto>(
            $"/api/workstation/evidence/subjects/{EvidenceSubjectResolver.StrategyRunKind}/{Uri.EscapeDataString(seed.PaperRunId)}/packet",
            ServerJsonOptions);
        evidencePacket.Should().NotBeNull();
        var ledgerNode = evidencePacket!.Nodes.Single(node => node.Kind == "run-ledger");
        var ledgerJournalRoute = $"/api/workstation/runs/{Uri.EscapeDataString(seed.PaperRunId)}/ledger/journal";
        var ledgerTrialBalanceRoute = $"/api/workstation/runs/{Uri.EscapeDataString(seed.PaperRunId)}/ledger/trial-balance";
        ledgerNode.Status.Should().Be(EvidenceStatusDto.Ready);
        var ledgerArtifactRefs = ledgerNode.ArtifactRefs;
        AssertLedgerArtifactRefs(ledgerArtifactRefs, ledgerJournalRoute, ledgerTrialBalanceRoute);

        var reportPackResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/report-packs",
            new FundReportPackGenerateRequestDto(
                FundProfileId: seed.FundProfileId,
                AuditActor: "pilot.operator",
                AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
                Formats: [GovernanceReportArtifactFormatDto.Json],
                ExpectedSchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion),
            ServerJsonOptions);
        reportPackResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var reportPack = await reportPackResponse.Content.ReadFromJsonAsync<FundReportPackSnapshotDto>(
            ServerJsonOptions);
        reportPack.Should().NotBeNull();
        reportPack!.Provenance.RelatedRunIds.Should().Contain(seed.BacktestRunId);
        reportPack.Provenance.RelatedRunIds.Should().Contain(seed.PaperRunId);

        var workflow = PublishPilotReportPackWorkflow(
            pilot.App.Services.GetRequiredService<ReportPackWorkflowService>(),
            seed,
            reportPack.ReportId,
            reconciliation.Summary.ReconciliationRunId,
            ledgerEvidenceId!,
            reportPack.Provenance.SourceSnapshotHash);
        workflow.State.Should().Be(ReportPackWorkflowStateDto.Published);
        workflow.Publication.Should().NotBeNull();
        workflow.AuditTrail.Should().Contain(audit => audit.ToState == ReportPackWorkflowStateDto.Approved);

        var w4Evidence = BuildW4AcceptanceEvidence(
            seed,
            reconciliation.Summary.ReconciliationRunId,
            workflow,
            reportPack.ReportId.ToString("D"),
            reportPack.Artifacts);
        var w4Filter = pilot.App.Services.GetRequiredService<W4AcceptanceFilter>();

        var stageGates = BuildPilotStageGates(
            seed,
            promotion.AuditReference,
            session.SessionId,
            replay.VerificationAuditId,
            reconciliation.Summary.ReconciliationRunId,
            continuity.Run.Summary.RunId,
            portfolioEvidenceId!,
            ledgerEvidenceId!,
            reportPack.ReportId.ToString("D"));
        var evidenceGraph = BuildPilotEvidenceGraph(
            seed,
            promotion.AuditReference,
            session.SessionId,
            replay.VerificationAuditId,
            reconciliation.Summary.ReconciliationRunId,
            continuity.Run.Summary.RunId,
            portfolioEvidenceId!,
            ledgerEvidenceId!,
            reportPack.ReportId.ToString("D"))
            .Concat(BuildW4AcceptanceGraph(workflow, reconciliation.Summary.ReconciliationRunId))
            .ToArray();

        var artifact = w4Filter.ApplyToArtifact(new PilotReadinessArtifactDto(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ProviderEvidenceId: FeedEvidenceId,
            DatasetEvidenceId: DatasetEvidenceId,
            ResearchRunId: seed.BacktestRunId,
            ComparedRunIds: [seed.BacktestRunId, seed.PaperRunId],
            PromotionAuditId: promotion.AuditReference,
            PaperSessionId: session.SessionId,
            ReplayVerificationAuditId: replay.VerificationAuditId,
            ReconciliationRunId: reconciliation.Summary.ReconciliationRunId,
            ContinuityRunId: continuity.Run.Summary.RunId,
            PortfolioEvidenceId: portfolioEvidenceId,
            LedgerEvidenceId: ledgerEvidenceId,
            ReportPackId: reportPack.ReportId.ToString("D"),
            ReportPackRelatedRunIds: reportPack.Provenance.RelatedRunIds,
            StageGates: stageGates,
            EvidenceGraph: evidenceGraph)
        {
            LedgerArtifactRefs = ledgerArtifactRefs,
            W4Evidence = w4Evidence
        });
        artifact.W4Acceptance.Should().NotBeNull();
        artifact.W4Acceptance!.IsDone.Should().BeTrue();

        var artifactPath = await WritePilotReadinessArtifactAsync(artifact);
        var markdownPath = Path.Combine(Path.GetDirectoryName(artifactPath)!, "pilot-readiness.md");
        using var artifactDocument = await JsonDocument.ParseAsync(File.OpenRead(artifactPath));
        artifactDocument.RootElement.GetProperty("datasetEvidenceId").GetString().Should().Be(DatasetEvidenceId);
        artifactDocument.RootElement.GetProperty("paperSessionId").GetString().Should().Be(session.SessionId);
        artifactDocument.RootElement.GetProperty("portfolioEvidenceId").GetString().Should().Be(portfolioEvidenceId);
        artifactDocument.RootElement.GetProperty("ledgerEvidenceId").GetString().Should().Be(ledgerEvidenceId);
        File.Exists(markdownPath).Should().BeTrue();
        var markdown = await File.ReadAllTextAsync(markdownPath);
        markdown.Should().Contain("trusted data -> strategy run -> paper promotion");
        markdown.Should().Contain("| TrustedData | W2, W3, W4 | Ready |");
        markdown.Should().Contain(reportPack.ReportId.ToString("D"));
        AssertSerializedLedgerArtifactRefs(artifactDocument.RootElement, ledgerJournalRoute, ledgerTrialBalanceRoute);
        artifactDocument.RootElement.GetProperty("allStagesReady").GetBoolean().Should().BeTrue();
        artifactDocument.RootElement.GetProperty("readyStageCount").GetInt32().Should().Be(8);
        artifactDocument.RootElement.GetProperty("w4Acceptance").GetProperty("isDone").GetBoolean().Should().BeTrue();
        artifactDocument.RootElement.GetProperty("w4Acceptance").GetProperty("supportEvidence").EnumerateArray().Should().NotBeEmpty();
        var stageNames = artifactDocument.RootElement.GetProperty("stageGates")
            .EnumerateArray()
            .Select(item => item.GetProperty("stage").GetString())
            .ToArray();
        stageNames.Should().Contain("TrustedData");
        stageNames.Should().Contain("GovernedReportPack");
        artifactDocument.RootElement.GetProperty("stageGates")
            .EnumerateArray()
            .SelectMany(stage => stage.GetProperty("evidenceIds").EnumerateArray())
            .Select(evidenceId => evidenceId.GetString())
            .Should()
            .OnlyContain(evidenceId =>
                !string.IsNullOrWhiteSpace(evidenceId) &&
                !evidenceId.Contains("missing", StringComparison.OrdinalIgnoreCase));
        var graphRelationships = artifactDocument.RootElement.GetProperty("evidenceGraph")
            .EnumerateArray()
            .Select(item => item.GetProperty("relationship").GetString())
            .ToArray();
        graphRelationships.Should().Contain("feeds-run");
        graphRelationships.Should().Contain("produces-portfolio");
        graphRelationships.Should().Contain("books-ledger");
        graphRelationships.Should().Contain("checked-against");
        graphRelationships.Should().Contain("reconciled-by");
        graphRelationships.Should().Contain("summarized-by");
        graphRelationships.Should().Contain("closes-into");
        graphRelationships.Should().Contain("approved-by");
        graphRelationships.Should().Contain("published-by");
        artifactDocument.RootElement.GetProperty("evidenceGraph")
            .EnumerateArray()
            .Should()
            .NotContain(edge =>
                string.Equals(
                    edge.GetProperty("fromEvidenceId").GetString(),
                    edge.GetProperty("toEvidenceId").GetString(),
                    StringComparison.Ordinal));
        artifactDocument.RootElement.GetProperty("reportPackRelatedRunIds")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(seed.PaperRunId);
    }

    [Fact]
    public void W4AcceptanceFilter_SupportEvidenceOnly_CannotReportDone()
    {
        var filter = new W4AcceptanceFilter();
        var supportOnly = new[]
        {
            new PilotAcceptanceEvidenceDto(
                PilotAcceptanceEvidenceCategoryDto.EvidenceVaultManifestExportSupport,
                PilotAcceptanceEvidenceRoleDto.Support,
                "evidence-vault/pilot-report/manifest.json",
                "Evidence-vault retained manifest")
        };

        var evaluation = filter.Evaluate(supportOnly);
        var gate = filter.ApplyToGovernedReportPackGate(CreateGovernedReportPackGate(), evaluation);

        evaluation.IsDone.Should().BeFalse();
        evaluation.Status.Should().Be(PilotReadinessStageStatusDto.ReviewRequired);
        evaluation.SupportEvidence.Should().ContainSingle();
        evaluation.MissingAcceptanceCategories.Should().BeEquivalentTo(W4AcceptanceFilter.RequiredAcceptanceCategories);
        gate.Status.Should().Be(PilotReadinessStageStatusDto.ReviewRequired);
        gate.EvidenceIds.Should().BeEmpty("support manifests are not W4 acceptance evidence");
        gate.SupportEvidenceIds.Should().Contain("evidence-vault/pilot-report/manifest.json");
    }

    [Fact]
    public void W4AcceptanceFilter_ArtifactPathSupportEvidenceOnly_DemotesGovernedReportPackGate()
    {
        var filter = new W4AcceptanceFilter();
        var supportOnly = new[]
        {
            new PilotAcceptanceEvidenceDto(
                PilotAcceptanceEvidenceCategoryDto.EvidenceVaultManifestExportSupport,
                PilotAcceptanceEvidenceRoleDto.Support,
                "evidence-vault/pilot-report/manifest.json",
                "Evidence-vault retained manifest")
        };

        var artifact = filter.ApplyToArtifact(CreatePilotReadinessArtifact(
            [CreateGovernedReportPackGate()],
            supportOnly));

        artifact.AllStagesReady.Should().BeFalse();
        artifact.W4Acceptance.Should().NotBeNull();
        artifact.W4Acceptance!.IsDone.Should().BeFalse();
        var governedReportPackGate = artifact.StageGates.Should().ContainSingle().Which;
        governedReportPackGate.Status.Should().Be(PilotReadinessStageStatusDto.ReviewRequired);
        governedReportPackGate.EvidenceIds.Should().BeEmpty("support evidence must not be promoted into the acceptance evidence lane");
        governedReportPackGate.SupportEvidenceIds.Should().Contain("evidence-vault/pilot-report/manifest.json");
    }

    [Theory]
    [MemberData(nameof(MissingEndToEndAcceptanceEvidence))]
    public void W4AcceptanceFilter_MissingEndToEndAcceptanceCategory_CannotReportDone(
        PilotAcceptanceEvidenceCategoryDto missingCategory)
    {
        var filter = new W4AcceptanceFilter();
        var evidence = CreateCompleteW4Evidence()
            .Where(item => item.Category != missingCategory)
            .ToArray();

        var evaluation = filter.Evaluate(evidence);
        var gate = filter.ApplyToGovernedReportPackGate(CreateGovernedReportPackGate(), evaluation);

        evaluation.IsDone.Should().BeFalse();
        evaluation.Status.Should().Be(PilotReadinessStageStatusDto.ReviewRequired);
        evaluation.MissingAcceptanceCategories.Should().Contain(missingCategory);
        gate.Status.Should().Be(PilotReadinessStageStatusDto.ReviewRequired);
        gate.Blockers.Should().Contain(blocker => blocker.Contains(missingCategory.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void W4AcceptanceFilter_EndToEndCaseCloseApprovalPublicationEvidence_CanReportDone()
    {
        var filter = new W4AcceptanceFilter();
        var evidence = CreateCompleteW4Evidence();

        var evaluation = filter.Evaluate(evidence);
        var gate = filter.ApplyToGovernedReportPackGate(CreateGovernedReportPackGate(), evaluation);

        evaluation.IsDone.Should().BeTrue();
        evaluation.MissingAcceptanceCategories.Should().BeEmpty();
        evaluation.MissingSupportCategories.Should().BeEmpty();
        evaluation.AcceptanceEvidence.Select(item => item.Category).Should().BeEquivalentTo(W4AcceptanceFilter.RequiredAcceptanceCategories);
        gate.Status.Should().Be(PilotReadinessStageStatusDto.Ready);
        gate.EvidenceIds.Should().Contain(new[]
        {
            "casework/recon-run-001",
            "close-checklist/fund-001/2026-04-11",
            "approval/report-pack-001",
            "publication/report-pack-001",
            "restatement-ready/report-pack-001"
        });
        gate.SupportEvidenceIds.Should().Contain("evidence-vault/report-pack-001/manifest.json");
    }

    private static async Task<PilotTestApp> CreatePilotAppAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "pilot-acceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, CreateMinimalConfig(root));

        var automationRoot = Path.Combine(root, "provider-validation", "_automation");
        WriteReadyDk1Packet(automationRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test"
        });
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(new Meridian.Ui.Shared.Services.ConfigStore(configPath));
        builder.Services.AddSingleton(new Dk1TrustGateReadinessOptions(automationRoot));
        builder.Services.AddSingleton<IGovernanceReportPackRepository>(_ =>
            new FileGovernanceReportPackRepository(
                Path.Combine(root, "report-packs"),
                NullLogger<FileGovernanceReportPackRepository>.Instance));
        builder.Services.AddSingleton<IReconciliationBreakQueueRepository>(_ =>
            new FileReconciliationBreakQueueRepository(
                Path.Combine(root, "break-queue"),
                NullLogger<FileReconciliationBreakQueueRepository>.Instance));
        builder.Services.AddSingleton<IPromotionRecordStore>(_ =>
            new JsonlPromotionRecordStore(
                Path.Combine(root, "promotions"),
                NullLogger<JsonlPromotionRecordStore>.Instance));

        using (InMemoryGovernanceFixtureProfile.Enable())
        {
            builder.Services.AddUiSharedServices(configPath);
        }

        builder.Services.AddSingleton(_ => new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(root, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance));
        builder.Services.AddSingleton<IPaperSessionStore>(_ =>
            new JsonlFilePaperSessionStore(
                Path.Combine(root, "sessions"),
                NullLogger<JsonlFilePaperSessionStore>.Instance));
        builder.Services.AddSingleton<PaperSessionPersistenceService>(sp => new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            sp.GetRequiredService<IPaperSessionStore>(),
            sp.GetRequiredService<ExecutionAuditTrailService>()));
        builder.Services.AddSingleton<ExecutionOperatorControlService>(sp => new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(root, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            sp.GetRequiredService<ExecutionAuditTrailService>()));
        builder.Services.AddSingleton<PromotionService>(sp => new PromotionService(
            sp.GetRequiredService<IStrategyRepository>(),
            sp.GetRequiredService<BacktestToLivePromoter>(),
            sp.GetRequiredService<IPromotionRecordStore>(),
            NullLogger<PromotionService>.Instance,
            operatorControls: sp.GetRequiredService<ExecutionOperatorControlService>(),
            auditTrail: sp.GetRequiredService<ExecutionAuditTrailService>()));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "pilot.operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.AdminMaintenance;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "pilot-acceptance-tenant";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "pilot-acceptance-tenant";
            await next();
        });
        app.MapWorkstationEndpoints(ServerJsonOptions);
        app.MapEvidenceEndpoints(ServerJsonOptions);
        app.MapExecutionEndpoints(ServerJsonOptions);
        app.MapFundStructureEndpoints(ServerJsonOptions);
        await app.StartAsync();

        return new PilotTestApp(app, root);
    }

    private static async Task<PilotSeed> SeedPilotWorkspaceAsync(IServiceProvider services)
    {
        var fundProfileId = $"pilot-fund-{Guid.NewGuid():N}";
        var fundDisplayName = "Pilot Acceptance Fund";
        var fundId = TranslateFundProfileId(fundProfileId);
        var strategyId = $"pilot-strategy-{Guid.NewGuid():N}"[..22];
        const string strategyName = "Pilot Acceptance Strategy";
        var backtestRunId = $"pilot-backtest-{Guid.NewGuid():N}";
        var paperRunId = $"pilot-paper-{Guid.NewGuid():N}";

        var accountService = services.GetRequiredService<IFundAccountService>();
        var account = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: $"PILOT-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            DisplayName: "Pilot Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "pilot-harness",
            FundId: fundId,
            LedgerReference: "PILOT-TB",
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "1234567890",
                BankName: "Meridian Bank",
                BranchName: null,
                Iban: null,
                BicSwift: null,
                RoutingNumber: null,
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: null,
                BeneficiaryAddress: null)));

        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: account.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 250_000m,
            Source: "pilot-harness",
            RecordedBy: "pilot-harness",
            PendingSettlement: 0m));

        await accountService.IngestBankStatementAsync(new IngestBankStatementRequest(
            BatchId: Guid.NewGuid(),
            AccountId: account.AccountId,
            StatementDate: new DateOnly(2026, 4, 11),
            BankName: "Meridian Bank",
            Notes: "pilot acceptance harness",
            Lines:
            [
                new BankStatementLineDto(
                    LineId: Guid.NewGuid(),
                    BatchId: Guid.NewGuid(),
                    AccountId: account.AccountId,
                    TransactionDate: new DateOnly(2026, 4, 11),
                    ValueDate: new DateOnly(2026, 4, 11),
                    Amount: 250_000m,
                    Currency: "USD",
                    TransactionType: "Contribution",
                    Description: "Pilot capital contribution",
                    Reference: "PILOT-BANK-001",
                    ClosingBalance: 250_000m)
            ],
            LoadedBy: "pilot-harness"));

        var repository = services.GetRequiredService<IStrategyRepository>();
        await repository.RecordRunAsync(BuildPilotRun(
            runId: backtestRunId,
            strategyId: strategyId,
            strategyName: strategyName,
            runType: RunType.Backtest,
            fundProfileId: fundProfileId,
            fundDisplayName: fundDisplayName,
            parentRunId: null));
        await repository.RecordRunAsync(BuildPilotRun(
            runId: paperRunId,
            strategyId: strategyId,
            strategyName: strategyName,
            runType: RunType.Paper,
            fundProfileId: fundProfileId,
            fundDisplayName: fundDisplayName,
            parentRunId: backtestRunId));

        return new PilotSeed(fundProfileId, account.AccountId, strategyId, strategyName, backtestRunId, paperRunId);
    }

    private static StrategyRunEntry BuildPilotRun(
        string runId,
        string strategyId,
        string strategyName,
        RunType runType,
        string fundProfileId,
        string fundDisplayName,
        string? parentRunId)
    {
        var startedAt = new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var result = BuildPilotBacktestResult(startedAt, completedAt);

        return StrategyRunEntry.Start(strategyId, strategyName, runType).Complete(result) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            DatasetReference = DatasetEvidenceId,
            FeedReference = FeedEvidenceId,
            PortfolioId = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-ledger",
            AuditReference = $"audit-{runId}",
            ParentRunId = parentRunId,
            FundProfileId = fundProfileId,
            FundDisplayName = fundDisplayName
        };
    }

    private static BacktestResult BuildPilotBacktestResult(DateTimeOffset startedAt, DateTimeOffset completedAt)
    {
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 249_600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 250_000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 249_600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 250_000m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 250_000m,
            FinalEquity: 250_000m,
            GrossPnl: 0m,
            NetPnl: 0m,
            TotalReturn: 0m,
            AnnualizedReturn: 0m,
            SharpeRatio: 1d,
            SortinoRatio: 1d,
            CalmarRatio: 1d,
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

        return new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 4, 10),
                To: new DateOnly(2026, 4, 11),
                Symbols: ["AAPL"],
                InitialCash: 250_000m,
                DataRoot: "./data"),
            Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: CreatePilotLedger(startedAt, completedAt),
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 42);
    }

    private static global::Meridian.Ledger.Ledger CreatePilotLedger(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, startedAt, "Pilot capital",
        [
            (LedgerAccounts.Cash, 250_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 250_000m)
        ]);
        PostBalancedEntry(ledger, completedAt, "Buy AAPL",
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

    private static ExecutionReport CreateExecutionFill(
        string orderId,
        string symbol,
        decimal quantity,
        decimal fillPrice) => new()
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

    private static ReportPackWorkflowRecordDto PublishPilotReportPackWorkflow(
        ReportPackWorkflowService workflowService,
        PilotSeed seed,
        Guid generatedReportId,
        string reconciliationRunId,
        string ledgerEvidenceId,
        string sourceSnapshotHash)
    {
        var lineProvenance = new[]
        {
            new ReportPackLineProvenanceDto(
                "trial-balance:cash",
                "ledger",
                ledgerEvidenceId,
                ledgerEvidenceId,
                RunId: seed.PaperRunId,
                LedgerEntryId: ledgerEvidenceId,
                ReconciliationCaseId: $"casework/{reconciliationRunId}",
                ReportValue: "250000.00",
                ReconciliationRunId: reconciliationRunId,
                ProviderEventId: $"provider-event/{seed.AccountId:D}/PILOT-BANK-001",
                SecurityMasterId: "security-master/cash/USD",
                SecurityDefinitionId: "security-definition/cash/USD",
                ReconciliationOutcome: "matched",
                ApprovalId: $"approval/{generatedReportId:D}/pilot.approver")
        };
        var workflow = workflowService.Create(
            seed.FundProfileId,
            seed.AccountId.ToString("D"),
            "2026-04-11",
            new VersionedReportTemplateIdDto("pilot-governed-report-pack", 1),
            "pilot.operator",
            lineProvenance);
        workflow = workflowService.Transition(workflow.ReportId, ReportPackWorkflowStateDto.Validated, "pilot.validator", "validator", "Pilot report pack validated.");
        workflow = workflowService.Transition(workflow.ReportId, ReportPackWorkflowStateDto.PendingApproval, "pilot.reviewer", "reviewer", "Submitted for W4 acceptance approval.");
        workflow = workflowService.Transition(workflow.ReportId, ReportPackWorkflowStateDto.Approved, "pilot.approver", "approver", "Approved for pilot W4 publication.");
        return workflowService.Publish(
            workflow.ReportId,
            "pilot.publisher",
            "publisher",
            "pilot.approver",
            sourceSnapshotHash,
            $"manifest/{generatedReportId:D}",
            $"evidence-vault/report-packs/{generatedReportId:D}/manifest.json",
            [
                new ReportPackEvidenceLinkDto(ledgerEvidenceId, "Ledger evidence", $"/api/workstation/runs/{Uri.EscapeDataString(seed.PaperRunId)}/ledger/journal", "ledger"),
                new ReportPackEvidenceLinkDto(seed.PaperRunId, "Paper strategy run", $"/api/workstation/runs/{Uri.EscapeDataString(seed.PaperRunId)}", "strategy"),
                new ReportPackEvidenceLinkDto($"casework/{reconciliationRunId}", "Reconciliation casework", $"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(reconciliationRunId)}", "reconciliation"),
                new ReportPackEvidenceLinkDto(reconciliationRunId, "Reconciliation run", $"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(reconciliationRunId)}", "reconciliation"),
                new ReportPackEvidenceLinkDto($"provider-event/{seed.AccountId:D}/PILOT-BANK-001", "Provider event", null, "provider"),
                new ReportPackEvidenceLinkDto("security-master/cash/USD", "Security Master identity", null, "security-master"),
                new ReportPackEvidenceLinkDto("security-definition/cash/USD", "Security definition", null, "security-master"),
                new ReportPackEvidenceLinkDto($"approval/{generatedReportId:D}/pilot.approver", "Accounting approval", null, "accounting"),
                new ReportPackEvidenceLinkDto($"close-checklist/{seed.AccountId:D}/2026-04-11", "Accounting close checklist", null, "accounting-close"),
                new ReportPackEvidenceLinkDto($"restatement-ready/{generatedReportId:D}", "Restatement readiness controls", null, "reporting-governance")
            ],
            "Published with retained W4 pilot acceptance evidence.");
    }

    private static IReadOnlyList<PilotAcceptanceEvidenceDto> BuildW4AcceptanceEvidence(
        PilotSeed seed,
        string reconciliationRunId,
        ReportPackWorkflowRecordDto workflow,
        string reportPackId,
        IReadOnlyList<FundReportPackArtifactDto> artifacts)
    {
        var approvalEvent = workflow.AuditTrail.Last(audit => audit.ToState == ReportPackWorkflowStateDto.Approved);
        var publication = workflow.Publication ?? throw new InvalidOperationException("Published report-pack workflow requires publication metadata.");
        var manifestSupportId = string.IsNullOrWhiteSpace(publication.RetainedManifestPath)
            ? $"evidence-vault/{reportPackId}/manifest.json"
            : publication.RetainedManifestPath;
        var exportSupportId = artifacts.FirstOrDefault()?.RelativePath ?? manifestSupportId;

        return
        [
            new(PilotAcceptanceEvidenceCategoryDto.ReconciliationCasework, PilotAcceptanceEvidenceRoleDto.Acceptance, $"casework/{reconciliationRunId}", "Reconciliation casework", $"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(reconciliationRunId)}"),
            new(PilotAcceptanceEvidenceCategoryDto.AccountingCloseChecklist, PilotAcceptanceEvidenceRoleDto.Acceptance, $"close-checklist/{seed.AccountId:D}/2026-04-11", "Accounting close checklist"),
            new(PilotAcceptanceEvidenceCategoryDto.ReportingReviewApproval, PilotAcceptanceEvidenceRoleDto.Acceptance, $"approval/{workflow.ReportId:D}/{approvalEvent.At:yyyyMMddHHmmss}", "Reporting review approval"),
            new(PilotAcceptanceEvidenceCategoryDto.GovernedReportPackPublication, PilotAcceptanceEvidenceRoleDto.Acceptance, $"publication/{workflow.ReportId:D}/{publication.ManifestId}", "Governed report-pack publication", publication.RetainedManifestPath),
            new(PilotAcceptanceEvidenceCategoryDto.RestatementReadiness, PilotAcceptanceEvidenceRoleDto.Acceptance, $"restatement-ready/{reportPackId}", "Restatement readiness controls"),
            new(PilotAcceptanceEvidenceCategoryDto.EvidenceVaultManifestExportSupport, PilotAcceptanceEvidenceRoleDto.Support, manifestSupportId, "Evidence-vault retained manifest/export", exportSupportId)
        ];
    }

    private static IReadOnlyList<PilotEvidenceGraphEdgeDto> BuildW4AcceptanceGraph(
        ReportPackWorkflowRecordDto workflow,
        string reconciliationRunId) =>
    [
        new($"casework/{reconciliationRunId}", $"close-checklist/{workflow.FundAccountId}/2026-04-11", "closes-into"),
        new($"close-checklist/{workflow.FundAccountId}/2026-04-11", $"approval/{workflow.ReportId:D}", "approved-by"),
        new($"approval/{workflow.ReportId:D}", $"publication/{workflow.ReportId:D}", "published-by")
    ];

    public static IEnumerable<object[]> MissingEndToEndAcceptanceEvidence =>
        W4AcceptanceFilter.RequiredAcceptanceCategories.Select(category => new object[] { category });

    private static IReadOnlyList<PilotAcceptanceEvidenceDto> CreateCompleteW4Evidence() =>
    [
        new(PilotAcceptanceEvidenceCategoryDto.ReconciliationCasework, PilotAcceptanceEvidenceRoleDto.Acceptance, "casework/recon-run-001", "Reconciliation casework"),
        new(PilotAcceptanceEvidenceCategoryDto.AccountingCloseChecklist, PilotAcceptanceEvidenceRoleDto.Acceptance, "close-checklist/fund-001/2026-04-11", "Accounting close checklist"),
        new(PilotAcceptanceEvidenceCategoryDto.ReportingReviewApproval, PilotAcceptanceEvidenceRoleDto.Acceptance, "approval/report-pack-001", "Reporting approval"),
        new(PilotAcceptanceEvidenceCategoryDto.GovernedReportPackPublication, PilotAcceptanceEvidenceRoleDto.Acceptance, "publication/report-pack-001", "Governed report-pack publication"),
        new(PilotAcceptanceEvidenceCategoryDto.RestatementReadiness, PilotAcceptanceEvidenceRoleDto.Acceptance, "restatement-ready/report-pack-001", "Restatement readiness"),
        new(PilotAcceptanceEvidenceCategoryDto.EvidenceVaultManifestExportSupport, PilotAcceptanceEvidenceRoleDto.Support, "evidence-vault/report-pack-001/manifest.json", "Manifest/export support")
    ];

    private static PilotReadinessArtifactDto CreatePilotReadinessArtifact(
        IReadOnlyList<PilotReadinessStageGateDto> stageGates,
        IReadOnlyList<PilotAcceptanceEvidenceDto> w4Evidence) =>
        new(
            GeneratedAtUtc: DateTimeOffset.Parse("2026-04-11T16:00:00Z"),
            ProviderEvidenceId: FeedEvidenceId,
            DatasetEvidenceId: DatasetEvidenceId,
            ResearchRunId: "backtest-run-001",
            ComparedRunIds: ["backtest-run-001", "paper-run-001"],
            PromotionAuditId: "promotion-audit-001",
            PaperSessionId: "paper-session-001",
            ReplayVerificationAuditId: "replay-verification-001",
            ReconciliationRunId: "recon-run-001",
            ContinuityRunId: "continuity-run-001",
            PortfolioEvidenceId: "portfolio-evidence-001",
            LedgerEvidenceId: "ledger-evidence-001",
            ReportPackId: "report-pack-001",
            ReportPackRelatedRunIds: ["backtest-run-001", "paper-run-001"],
            StageGates: stageGates,
            EvidenceGraph: [])
        {
            W4Evidence = w4Evidence
        };

    private static PilotReadinessStageGateDto CreateGovernedReportPackGate() =>
        new(
            PilotReadinessStageDto.GovernedReportPack,
            "Governed report pack lineage",
            PilotReadinessStageStatusDto.Ready,
            ["evidence-vault/report-pack-001/manifest.json"],
            [],
            "Support manifest exists.")
        {
            WaveClaims = ["W4"],
            SupportEvidenceIds = ["evidence-vault/report-pack-001/manifest.json"]
        };

    private static IReadOnlyList<PilotReadinessStageGateDto> BuildPilotStageGates(
        PilotSeed seed,
        string? promotionAuditId,
        string paperSessionId,
        string? replayVerificationAuditId,
        string reconciliationRunId,
        string continuityRunId,
        string portfolioEvidenceId,
        string ledgerEvidenceId,
        string reportPackId) =>
    [
        new(
            PilotReadinessStageDto.TrustedData,
            "Trusted provider and dataset evidence",
            PilotReadinessStageStatusDto.Ready,
            [FeedEvidenceId, DatasetEvidenceId],
            [],
            "DK1 packet fixture and dataset references seeded by PilotAcceptanceHarnessTests.")
        {
            WaveClaims = ["W2", "W3", "W4"]
        },
        new(
            PilotReadinessStageDto.ResearchRun,
            "Strategy run evidence retained",
            PilotReadinessStageStatusDto.Ready,
            [seed.BacktestRunId, DatasetEvidenceId],
            [],
            "Strategy briefing returned the retained backtest run and dataset evidence.")
        {
            WaveClaims = ["W3"]
        },
        new(
            PilotReadinessStageDto.RunComparison,
            "Baseline and candidate run comparison",
            PilotReadinessStageStatusDto.Ready,
            [.. seed.ComparedRunIds],
            [],
            "Shared run comparison endpoint accepted the baseline and paper run IDs.")
        {
            WaveClaims = ["W3"]
        },
        new(
            PilotReadinessStageDto.PaperPromotion,
            "Paper promotion approval audit",
            PilotReadinessStageStatusDto.Ready,
            [seed.BacktestRunId, promotionAuditId ?? "promotion-audit-missing"],
            [],
            "PromotionService approved the backtest run with the required checklist.")
        {
            WaveClaims = ["W2", "W3"]
        },
        new(
            PilotReadinessStageDto.PaperSession,
            "Paper session replay verification",
            PilotReadinessStageStatusDto.Ready,
            [paperSessionId, replayVerificationAuditId ?? "replay-audit-missing"],
            [],
            "PaperSessionPersistenceService replay verification returned consistent counts.")
        {
            WaveClaims = ["W2"]
        },
        new(
            PilotReadinessStageDto.PortfolioLedgerReview,
            "Portfolio and ledger continuity",
            PilotReadinessStageStatusDto.Ready,
            [continuityRunId, portfolioEvidenceId, ledgerEvidenceId],
            [],
            "Run continuity detail confirmed portfolio, ledger, and reconciliation coverage.")
        {
            WaveClaims = ["W3", "W4"]
        },
        new(
            PilotReadinessStageDto.Reconciliation,
            "Reconciliation run casework",
            PilotReadinessStageStatusDto.Ready,
            [reconciliationRunId, seed.PaperRunId],
            [],
            "Reconciliation run endpoint retained run-scoped reconciliation detail.")
        {
            WaveClaims = ["W3", "W4"]
        },
        new(
            PilotReadinessStageDto.GovernedReportPack,
            "Governed report pack lineage",
            PilotReadinessStageStatusDto.Ready,
            [reportPackId, seed.BacktestRunId, seed.PaperRunId],
            [],
            "Fund report-pack generation retained provenance links to both pilot runs.")
        {
            WaveClaims = ["W4"]
        }
    ];

    private static IReadOnlyList<PilotEvidenceGraphEdgeDto> BuildPilotEvidenceGraph(
        PilotSeed seed,
        string? promotionAuditId,
        string paperSessionId,
        string? replayVerificationAuditId,
        string reconciliationRunId,
        string continuityRunId,
        string portfolioEvidenceId,
        string ledgerEvidenceId,
        string reportPackId) =>
    [
        new(FeedEvidenceId, DatasetEvidenceId, "supports-dataset"),
        new(DatasetEvidenceId, seed.BacktestRunId, "feeds-run"),
        new(seed.BacktestRunId, seed.PaperRunId, "compared-to"),
        new(seed.BacktestRunId, promotionAuditId ?? "promotion-audit-missing", "approved-by"),
        new(promotionAuditId ?? "promotion-audit-missing", paperSessionId, "promotes-to-session"),
        new(paperSessionId, replayVerificationAuditId ?? "replay-audit-missing", "verified-by"),
        new(seed.PaperRunId, portfolioEvidenceId, "produces-portfolio"),
        new(seed.PaperRunId, ledgerEvidenceId, "books-ledger"),
        new(portfolioEvidenceId, ledgerEvidenceId, "checked-against"),
        new(ledgerEvidenceId, reconciliationRunId, "reconciled-by"),
        new(seed.BacktestRunId, reportPackId, "summarized-by"),
        new(seed.PaperRunId, reportPackId, "summarized-by"),
        new(reconciliationRunId, reportPackId, "summarized-by")
    ];

    private static async Task<string> WritePilotReadinessArtifactAsync(PilotReadinessArtifactDto artifact)
    {
        var artifactDirectory = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "pilot-acceptance",
            "latest");
        var artifactPath = Path.Combine(artifactDirectory, "pilot-readiness.json");
        var markdownPath = Path.Combine(artifactDirectory, "pilot-readiness.md");
        Directory.CreateDirectory(artifactDirectory);
        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(artifact, new JsonSerializerOptions(ServerJsonOptions)
            {
                WriteIndented = true
            }));
        await File.WriteAllTextAsync(markdownPath, BuildPilotReadinessMarkdown(artifact));
        return artifactPath;
    }

    private static string BuildPilotReadinessMarkdown(PilotReadinessArtifactDto artifact)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Meridian Pilot Readiness");
        builder.AppendLine();
        builder.AppendLine("Canonical lifecycle: `trusted data -> strategy run -> paper promotion -> paper session -> replay -> portfolio/ledger review -> reconciliation -> governed report pack`.");
        builder.AppendLine();
        builder.AppendLine($"- Generated UTC: `{artifact.GeneratedAtUtc:O}`");
        builder.AppendLine($"- Overall status: `{(artifact.AllStagesReady ? "Ready" : "ReviewRequired")}`");
        builder.AppendLine($"- Ready stages: `{artifact.ReadyStageCount}/{artifact.TotalStageCount}`");
        builder.AppendLine($"- Validation command: `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter \"FullyQualifiedName~PilotAcceptanceHarnessTests\" --logger \"console;verbosity=normal\"`");
        builder.AppendLine($"- Report pack: `{artifact.ReportPackId}`");
        builder.AppendLine();
        builder.AppendLine("| Stage | W2-W4 claims | Status | Acceptance evidence IDs | Support evidence IDs | Blockers | Validation |");
        builder.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var gate in artifact.StageGates)
        {
            builder.Append("| ");
            builder.Append(gate.Stage);
            builder.Append(" | ");
            builder.Append(gate.WaveClaims.Count == 0
                ? "None"
                : EscapeMarkdownCell(string.Join(", ", gate.WaveClaims)));
            builder.Append(" | ");
            builder.Append(gate.Status);
            builder.Append(" | ");
            builder.Append(EscapeMarkdownCell(string.Join("<br>", gate.EvidenceIds.Select(static id => $"`{id}`"))));
            builder.Append(" | ");
            builder.Append(gate.SupportEvidenceIds.Count == 0
                ? "None"
                : EscapeMarkdownCell(string.Join("<br>", gate.SupportEvidenceIds.Select(static id => $"`{id}`"))));
            builder.Append(" | ");
            builder.Append(gate.Blockers.Count == 0
                ? "None"
                : EscapeMarkdownCell(string.Join("<br>", gate.Blockers)));
            builder.Append(" | ");
            builder.Append(EscapeMarkdownCell(gate.Validation));
            builder.AppendLine(" |");
        }

        return builder.ToString();
    }

    private static string EscapeMarkdownCell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static void AssertLedgerArtifactRefs(
        IReadOnlyList<EvidenceArtifactRefDto> artifactRefs,
        string ledgerJournalRoute,
        string ledgerTrialBalanceRoute)
    {
        artifactRefs.Should().HaveCount(2);
        artifactRefs.Should().Contain(artifact =>
            artifact.Kind == "ledger-journal" &&
            artifact.Route == ledgerJournalRoute &&
            artifact.Path == null &&
            artifact.Hash == null);
        artifactRefs.Should().Contain(artifact =>
            artifact.Kind == "ledger-trial-balance" &&
            artifact.Route == ledgerTrialBalanceRoute &&
            artifact.Path == null &&
            artifact.Hash == null);
    }

    private static void AssertSerializedLedgerArtifactRefs(
        JsonElement artifactRoot,
        string ledgerJournalRoute,
        string ledgerTrialBalanceRoute)
    {
        var ledgerArtifactRefs = artifactRoot.GetProperty("ledgerArtifactRefs")
            .EnumerateArray()
            .ToArray();

        ledgerArtifactRefs.Should().HaveCount(2);
        ledgerArtifactRefs.Should().Contain(artifact =>
            IsSerializedLedgerArtifact(artifact, "ledger-journal", ledgerJournalRoute));
        ledgerArtifactRefs.Should().Contain(artifact =>
            IsSerializedLedgerArtifact(artifact, "ledger-trial-balance", ledgerTrialBalanceRoute));
    }

    private static bool IsSerializedLedgerArtifact(JsonElement artifact, string kind, string route)
        => artifact.GetProperty("kind").GetString() == kind &&
           artifact.GetProperty("route").GetString() == route &&
           artifact.GetProperty("path").ValueKind == JsonValueKind.Null &&
           artifact.GetProperty("hash").ValueKind == JsonValueKind.Null;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static Guid TranslateFundProfileId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId.Trim())));

    private static string CreateMinimalConfig(string root)
    {
        var config = new
        {
            DataRoot = Path.Combine(root, "data"),
            Compress = false,
            DataSource = "IB",
            Symbols = new[]
            {
                new
                {
                    Symbol = "AAPL",
                    SubscribeTrades = true,
                    SubscribeDepth = false,
                    DepthLevels = 10,
                    SecurityType = "STK",
                    Exchange = "SMART",
                    Currency = "USD"
                }
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                IncludeProvider = false
            },
            DataSources = new
            {
                Sources = new[]
                {
                    new
                    {
                        Id = "pilot-provider",
                        Name = "Pilot Provider",
                        Provider = "Alpaca",
                        Enabled = true,
                        Type = "RealTime",
                        Priority = 10,
                        Description = "Pilot acceptance provider"
                    }
                },
                DefaultRealTimeSourceId = "pilot-provider",
                EnableFailover = true,
                FailoverTimeoutSeconds = 30,
                HealthCheckIntervalSeconds = 10,
                AutoRecover = true,
                FailoverRules = Array.Empty<object>()
            },
            Backfill = new
            {
                Enabled = false,
                Provider = "stooq",
                Symbols = new[] { "AAPL" }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteReadyDk1Packet(string automationRoot)
    {
        var packetDirectory = Path.Combine(automationRoot, "unit-ready");
        Directory.CreateDirectory(packetDirectory);
        File.WriteAllText(
            Path.Combine(packetDirectory, "dk1-pilot-parity-packet.json"),
            """
            {
              "generatedAtUtc": "2026-04-25T20:28:38Z",
              "sourceSummary": "artifacts/provider-validation/_automation/unit-ready/wave1-validation-summary.json",
              "status": "ready-for-operator-review",
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
              "operatorSignoff": {
                "requiredOwners": [ "Data", "Provider Reliability", "Trading" ],
                "status": "pending",
                "requiredBeforeDk1Exit": true
              },
              "blockers": []
            }
            """);
    }

    private sealed record PilotTestApp(WebApplication App, string Root) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
        }
    }

    private sealed class InMemoryGovernanceFixtureProfile : IDisposable
    {
        private readonly string? _originalAspNetCoreEnvironment;
        private readonly string? _originalDotnetEnvironment;
        private readonly string? _originalUseInMemoryGovernance;

        private InMemoryGovernanceFixtureProfile()
        {
            _originalAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            _originalDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            _originalUseInMemoryGovernance = Environment.GetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE");

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        }

        public static InMemoryGovernanceFixtureProfile Enable() => new();

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspNetCoreEnvironment);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnvironment);
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", _originalUseInMemoryGovernance);
        }
    }

    private sealed record PilotSeed(
        string FundProfileId,
        Guid AccountId,
        string StrategyId,
        string StrategyName,
        string BacktestRunId,
        string PaperRunId)
    {
        public IReadOnlyList<string> ComparedRunIds => [BacktestRunId, PaperRunId];
    }
}
