using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.FundAccounts;
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
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
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
            reportPack.ReportId.ToString("D"));

        var artifact = new PilotReadinessArtifactDto(
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
            EvidenceGraph: evidenceGraph);

        var artifactPath = await WritePilotReadinessArtifactAsync(artifact);
        using var artifactDocument = await JsonDocument.ParseAsync(File.OpenRead(artifactPath));
        artifactDocument.RootElement.GetProperty("datasetEvidenceId").GetString().Should().Be(DatasetEvidenceId);
        artifactDocument.RootElement.GetProperty("paperSessionId").GetString().Should().Be(session.SessionId);
        artifactDocument.RootElement.GetProperty("portfolioEvidenceId").GetString().Should().Be(portfolioEvidenceId);
        artifactDocument.RootElement.GetProperty("ledgerEvidenceId").GetString().Should().Be(ledgerEvidenceId);
        artifactDocument.RootElement.GetProperty("allStagesReady").GetBoolean().Should().BeTrue();
        artifactDocument.RootElement.GetProperty("readyStageCount").GetInt32().Should().Be(8);
        var stageNames = artifactDocument.RootElement.GetProperty("stageGates")
            .EnumerateArray()
            .Select(item => item.GetProperty("stage").GetString())
            .ToArray();
        stageNames.Should().Contain("TrustedData");
        stageNames.Should().Contain("GovernedReportPack");
        var graphRelationships = artifactDocument.RootElement.GetProperty("evidenceGraph")
            .EnumerateArray()
            .Select(item => item.GetProperty("relationship").GetString())
            .ToArray();
        graphRelationships.Should().Contain("feeds-run");
        graphRelationships.Should().Contain("produces-portfolio");
        graphRelationships.Should().Contain("books-ledger");
        graphRelationships.Should().Contain("summarized-by");
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

        builder.Services.AddUiSharedServices(configPath);

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
        app.MapWorkstationEndpoints(ServerJsonOptions);
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

        return new PilotSeed(fundProfileId, strategyId, strategyName, backtestRunId, paperRunId);
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
            "DK1 packet fixture and dataset references seeded by PilotAcceptanceHarnessTests."),
        new(
            PilotReadinessStageDto.ResearchRun,
            "Research run evidence retained",
            PilotReadinessStageStatusDto.Ready,
            [seed.BacktestRunId, DatasetEvidenceId],
            [],
            "Research briefing returned the retained backtest run and dataset evidence."),
        new(
            PilotReadinessStageDto.RunComparison,
            "Baseline and candidate run comparison",
            PilotReadinessStageStatusDto.Ready,
            [.. seed.ComparedRunIds],
            [],
            "Shared run comparison endpoint accepted the baseline and paper run IDs."),
        new(
            PilotReadinessStageDto.PaperPromotion,
            "Paper promotion approval audit",
            PilotReadinessStageStatusDto.Ready,
            [seed.BacktestRunId, promotionAuditId ?? "promotion-audit-missing"],
            [],
            "PromotionService approved the backtest run with the required checklist."),
        new(
            PilotReadinessStageDto.PaperSession,
            "Paper session replay verification",
            PilotReadinessStageStatusDto.Ready,
            [paperSessionId, replayVerificationAuditId ?? "replay-audit-missing"],
            [],
            "PaperSessionPersistenceService replay verification returned consistent counts."),
        new(
            PilotReadinessStageDto.PortfolioLedgerReview,
            "Portfolio and ledger continuity",
            PilotReadinessStageStatusDto.Ready,
            [continuityRunId, portfolioEvidenceId, ledgerEvidenceId],
            [],
            "Run continuity detail confirmed portfolio, ledger, and reconciliation coverage."),
        new(
            PilotReadinessStageDto.Reconciliation,
            "Reconciliation run casework",
            PilotReadinessStageStatusDto.Ready,
            [reconciliationRunId, seed.PaperRunId],
            [],
            "Reconciliation run endpoint retained run-scoped reconciliation detail."),
        new(
            PilotReadinessStageDto.GovernedReportPack,
            "Governed report pack lineage",
            PilotReadinessStageStatusDto.Ready,
            [reportPackId, seed.BacktestRunId, seed.PaperRunId],
            [],
            "Fund report-pack generation retained provenance links to both pilot runs.")
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
        var artifactPath = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "pilot-acceptance",
            "latest",
            "pilot-readiness.json");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(artifact, new JsonSerializerOptions(ServerJsonOptions)
            {
                WriteIndented = true
            }));
        return artifactPath;
    }

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
                { "name": "DK1 pilot parity runbook", "gate": "parity", "path": "docs/status/dk1-pilot-parity-runbook.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "DK1 trust rationale mapping", "gate": "explainability", "path": "docs/status/dk1-trust-rationale-mapping.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "DK1 baseline trust thresholds", "gate": "calibration", "path": "docs/status/dk1-baseline-trust-thresholds.md", "exists": true, "status": "validated", "missingRequirements": [] },
                { "name": "Provider validation matrix", "gate": "parity", "path": "docs/status/provider-validation-matrix.md", "exists": true, "status": "validated", "missingRequirements": [] }
              ],
              "trustRationaleContract": {
                "documentPath": "docs/status/dk1-trust-rationale-mapping.md",
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
                "documentPath": "docs/status/dk1-baseline-trust-thresholds.md",
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
                "requiredOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
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

    private sealed record PilotSeed(
        string FundProfileId,
        string StrategyId,
        string StrategyName,
        string BacktestRunId,
        string PaperRunId)
    {
        public IReadOnlyList<string> ComparedRunIds => [BacktestRunId, PaperRunId];
    }
}
