using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerAmountProvenanceServiceTests
{
    private static readonly Guid AaplSecurityId = Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D");

    [Fact]
    public async Task GetAsync_BuildsLedgerAmountDrilldownFromReportPackLineage()
    {
        var reportId = Guid.NewGuid();
        var snapshot = BuildSnapshot(reportId);
        var reportRepository = new InMemoryReportPackRepository(snapshot);
        var root = CreateTempRoot();
        try
        {
            var breakRepository = new FileReconciliationBreakQueueRepository(
                root,
                NullLogger<FileReconciliationBreakQueueRepository>.Instance);
            await breakRepository.CreateIfMissingAsync(new ReconciliationBreakQueueItem(
                BreakId: "case-aapl-001",
                RunId: "provider-ledger-run",
                StrategyName: "Provider ledger reconciliation",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 25m,
                Reason: "AAPL securities value differs from custodian statement.",
                AssignedTo: "fund-accounting",
                DetectedAt: snapshot.GeneratedAt.AddMinutes(-10),
                LastUpdatedAt: snapshot.GeneratedAt.AddMinutes(-5),
                Severity: ReconciliationBreakSeverity.High,
                ExceptionRoute: "accounting/reconciliation/provider-ledger",
                RequiredSignoffRole: "Fund accounting",
                SignoffStatus: "pending-signoff",
                FundAccountId: "fund-ops",
                ExplainabilitySummary: "account=Securities, symbol=AAPL, variance=25",
                RoutingTarget: "/api/fund-accounts/account-a/brokerage-sync/reconciliation/latest",
                RoutingDetail: "Securities:AAPL",
                RecommendedAction: "Review provider-ledger variance and sign off.",
                ExternalAccountId: "PA-LEDGER",
                CustodianId: "alpaca",
                UpstreamSyncCursor: "alpaca|PA-LEDGER|provider-position-aapl",
                LastUpstreamSyncAt: snapshot.GeneratedAt.AddMinutes(-6),
                Team: "Accounting"));

            var service = new LedgerAmountProvenanceService(reportRepository, breakRepository);

            var detail = await service.GetAsync(reportId, "Securities:AAPL");

            detail.Should().NotBeNull();
            detail!.ReportId.Should().Be(reportId);
            detail.ScopeKey.Should().Be("Securities:AAPL");
            detail.AccountName.Should().Be("Securities");
            detail.Symbol.Should().Be("AAPL");
            detail.Amount.Should().Be(400m);
            detail.Currency.Should().Be("USD");
            detail.Evidence.Should().Contain(item =>
                item.EvidenceType == "ledger-account" &&
                item.RelatedEvidenceIds!.Single() == "ledger-line-1");
            detail.Evidence.Should().Contain(item =>
                item.EvidenceType == "run" &&
                item.EvidenceId == "run-report-001");
            detail.StrategyRuns.Should().NotBeNull();
            var runLink = detail.StrategyRuns!.Should().ContainSingle(item => item.RunId == "run-report-001").Subject;
            runLink.DisplayLabel.Should().Be("Report Strategy (run-report-001)");
            runLink.Route.Should().Be("/api/workstation/runs/run-report-001/continuity");
            runLink.SourceSystem.Should().Be("strategy-run");
            runLink.IsLineScoped.Should().BeFalse();
            detail.SecurityMaster.Should().NotBeNull();
            detail.SecurityMaster!.Symbol.Should().Be("AAPL");
            detail.SecurityMaster.RelatedEvidenceIds.Should().Contain("journal-entry-1");
            detail.Reconciliation.ReconciliationRunCount.Should().Be(2);
            detail.Reconciliation.OpenBreakCount.Should().Be(1);
            detail.Reconciliation.RelatedCaseIds.Should().Contain("case-aapl-001");
            detail.Reconciliation.RelatedCases.Should().NotBeNull();
            var relatedCase = detail.Reconciliation.RelatedCases!.Should()
                .ContainSingle(item => item.CaseId == "case-aapl-001")
                .Subject;
            relatedCase.Status.Should().Be(nameof(ReconciliationBreakQueueStatus.Open));
            relatedCase.LifecycleState.Should().Be(nameof(ReconciliationCaseLifecycleState.Open));
            relatedCase.Owner.Should().Be("fund-accounting");
            relatedCase.Team.Should().Be("Accounting");
            relatedCase.RequiredSignoffRole.Should().Be("Fund accounting");
            relatedCase.SignoffStatus.Should().Be("pending-signoff");
            relatedCase.ExceptionRoute.Should().Be("accounting/reconciliation/provider-ledger");
            relatedCase.RecommendedAction.Should().Be("Review provider-ledger variance and sign off.");
            detail.Evidence.Should().Contain(item =>
                item.EvidenceType == "provider-event" &&
                item.EvidenceId == "alpaca|PA-LEDGER|provider-position-aapl" &&
                item.SourceSystem == "alpaca" &&
                item.RelatedEvidenceIds!.Contains("case-aapl-001"));
            detail.Approval.ReportStatus.Should().Be(GovernanceReportPackStatusDto.Approved);
            detail.Approval.LatestApprovalActor.Should().Be("controller");
            detail.ReportUsage.ReportRoute.Should().Be($"/api/fund-structure/report-packs/{reportId:D}");
            detail.Warnings.Should().NotContain("No retained provider-event pointer is attached to this ledger amount.");
            detail.Warnings.Should().NotContain("No durable reconciliation case is linked to this ledger amount.");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetAsync_WhenScopeKeyHasNoLedgerLine_ReturnsNull()
    {
        var reportId = Guid.NewGuid();
        var service = new LedgerAmountProvenanceService(new InMemoryReportPackRepository(BuildSnapshot(reportId)));

        var detail = await service.GetAsync(reportId, "Cash");

        detail.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_SurfacesProviderCorporateActionCaseMetadataAsStructuredEvidence()
    {
        var reportId = Guid.NewGuid();
        var snapshot = BuildSnapshot(reportId);
        var root = CreateTempRoot();
        try
        {
            var breakRepository = new FileReconciliationBreakQueueRepository(
                root,
                NullLogger<FileReconciliationBreakQueueRepository>.Instance);
            await breakRepository.CreateIfMissingAsync(new ReconciliationBreakQueueItem(
                BreakId: "case-provider-factor-aapl",
                RunId: "provider-ledger-run",
                StrategyName: "Provider corporate-action evidence",
                Category: ReconciliationBreakCategory.MissingPortfolioCoverage,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 0m,
                Reason: "Provider factor event requires controller review.",
                AssignedTo: "security-master-steward",
                DetectedAt: snapshot.GeneratedAt.AddMinutes(-10),
                LastUpdatedAt: snapshot.GeneratedAt.AddMinutes(-5),
                Severity: ReconciliationBreakSeverity.Medium,
                ExceptionRoute: "accounting/reconciliation/provider-ledger/corporate-actions",
                RequiredSignoffRole: "Security Master steward",
                SignoffStatus: "pending-signoff",
                FundAccountId: "fund-ops",
                ExplainabilitySummary: "provider=alpaca, externalAccount=PA-LEDGER, candidate=FactorScheduleEvent, providerEventId=factor-aapl-20260501, symbol=AAPL, securityId=35d27d8e-4460-4b17-92b8-6e5f53773d1d, requiredFeed=factor-schedule, evidenceSource=provider-corporate-action, ledgerEffect=FactorScheduleValuationInput, amount=0.9825, principalAmount=0, incomeAmount=0, journalLines=0, status=Break",
                RoutingTarget: "/api/fund-accounts/account-a/brokerage-sync/reconciliation/latest",
                RoutingDetail: "provider-corporate-action:alpaca:pa-ledger:factorscheduleevent:factor-aapl-20260501:aapl",
                ExternalAccountId: "PA-LEDGER",
                CustodianId: "alpaca",
                UpstreamSyncCursor: "alpaca|PA-LEDGER|factor-aapl-20260501",
                LastUpstreamSyncAt: snapshot.GeneratedAt.AddMinutes(-6)));

            var service = new LedgerAmountProvenanceService(new InMemoryReportPackRepository(snapshot), breakRepository);

            var detail = await service.GetAsync(reportId, "Securities:AAPL");

            detail.Should().NotBeNull();
            var providerEvidence = detail!.Evidence.Should().ContainSingle(item =>
                item.EvidenceType == "provider-event" &&
                item.ProviderEventId == "factor-aapl-20260501").Subject;
            providerEvidence.ProviderEventType.Should().Be("FactorScheduleEvent");
            providerEvidence.ProviderEvidenceSource.Should().Be("provider-corporate-action");
            providerEvidence.RequiredFeed.Should().Be("factor-schedule");
            providerEvidence.SecurityId.Should().Be("35d27d8e-4460-4b17-92b8-6e5f53773d1d");
            providerEvidence.LedgerEffectKind.Should().Be("FactorScheduleValuationInput");
            providerEvidence.PrincipalAmount.Should().Be(0m);
            providerEvidence.IncomeAmount.Should().Be(0m);
            providerEvidence.JournalPreviewLineCount.Should().Be(0);
            providerEvidence.SourceSystem.Should().Be("alpaca");
            providerEvidence.Route.Should().Be("/api/fund-accounts/account-a/brokerage-sync/reconciliation/latest");
            detail.Warnings.Should().NotContain("No retained provider-event pointer is attached to this ledger amount.");
            detail.Reconciliation.RelatedCaseIds.Should().Contain("case-provider-factor-aapl");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetAsync_LinksSecurityMasterCaseworkByRetainedSecurityId()
    {
        var reportId = Guid.NewGuid();
        var snapshot = BuildSnapshot(reportId, includeSecurityId: true);
        var root = CreateTempRoot();
        try
        {
            var breakRepository = new FileReconciliationBreakQueueRepository(
                root,
                NullLogger<FileReconciliationBreakQueueRepository>.Instance);
            await breakRepository.CreateIfMissingAsync(new ReconciliationBreakQueueItem(
                BreakId: $"security-master:override:{AaplSecurityId:N}",
                RunId: "security-master-overrides",
                StrategyName: "Security Master exception casework",
                Category: ReconciliationBreakCategory.ClassificationGap,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 0m,
                Reason: "AAPL operator override requires steward approval.",
                AssignedTo: "security-master-steward",
                DetectedAt: snapshot.GeneratedAt.AddMinutes(-12),
                LastUpdatedAt: snapshot.GeneratedAt.AddMinutes(-4),
                Severity: ReconciliationBreakSeverity.High,
                ExceptionRoute: "security-master/operator-overrides",
                RequiredSignoffRole: "Security Master steward",
                SignoffStatus: "pending-signoff",
                ExplainabilitySummary: $"securityId={AaplSecurityId:D}, approvalStatus=Pending, overrideCount=1",
                RoutingTarget: $"/api/security-master/{AaplSecurityId:D}/operator-overrides",
                RoutingDetail: AaplSecurityId.ToString("D"),
                RecommendedAction: "Approve, reject, or remove the operator override before report publication.",
                Team: "Security Master",
                UpstreamSyncCursor: $"security-master-override:{AaplSecurityId:N}:Pending"));

            var service = new LedgerAmountProvenanceService(new InMemoryReportPackRepository(snapshot), breakRepository);

            var detail = await service.GetAsync(reportId, "Securities:AAPL");

            detail.Should().NotBeNull();
            detail!.SecurityMaster.Should().NotBeNull();
            detail.SecurityMaster!.SecurityId.Should().Be(AaplSecurityId);
            detail.SecurityMaster.RelatedEvidenceIds.Should().Contain(AaplSecurityId.ToString("D"));
            detail.Reconciliation.RelatedCaseIds.Should().Contain($"security-master:override:{AaplSecurityId:N}");
            var relatedCase = detail.Reconciliation.RelatedCases.Should().NotBeNull().And
                .ContainSingle(item => item.CaseId == $"security-master:override:{AaplSecurityId:N}")
                .Subject;
            relatedCase.Team.Should().Be("Security Master");
            relatedCase.RequiredSignoffRole.Should().Be("Security Master steward");
            relatedCase.SignoffStatus.Should().Be("pending-signoff");
            relatedCase.ExceptionRoute.Should().Be("security-master/operator-overrides");
            detail.Warnings.Should().NotContain("No durable reconciliation case is linked to this ledger amount.");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_GetLedgerProvenance_ReturnsProviderLedgerSecurityReconciliationApprovalAndUsage()
    {
        var reportId = Guid.NewGuid();
        var snapshot = BuildSnapshot(reportId, includeProviderEvent: true);
        await using var app = await CreateEndpointAppAsync(snapshot);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/fund-structure/report-packs/{reportId:D}/ledger-provenance?scopeKey={Uri.EscapeDataString("Securities:AAPL")}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<LedgerAmountProvenanceDetailDto>();

        detail.Should().NotBeNull();
        detail!.Evidence.Should().Contain(item =>
            item.EvidenceType == "provider-event" &&
            item.SourceSystem == "provider" &&
            item.EvidenceId == "alpaca-position-aapl");
        detail.Evidence.Should().Contain(item => item.EvidenceType == "ledger-account");
            detail.SecurityMaster.Should().NotBeNull();
            detail.SecurityMaster!.Symbol.Should().Be("AAPL");
            detail.Reconciliation.OpenBreakCount.Should().Be(1);
            detail.Approval.ReportStatus.Should().Be(GovernanceReportPackStatusDto.Approved);
            detail.ReportUsage.ReportRoute.Should().Be($"/api/fund-structure/report-packs/{reportId:D}");
            detail.StrategyRuns.Should().ContainSingle(item => item.RunId == "run-report-001");
            detail.Warnings.Should().NotContain("No retained provider-event pointer is attached to this ledger amount.");
        }

    private static async Task<WebApplication> CreateEndpointAppAsync(FundReportPackSnapshotDto snapshot)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IGovernanceReportPackRepository>(new InMemoryReportPackRepository(snapshot));
        builder.Services.AddSingleton<LedgerAmountProvenanceService>();

        var app = builder.Build();
        app.MapFundStructureEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }

    private static FundReportPackSnapshotDto BuildSnapshot(
        Guid reportId,
        bool includeProviderEvent = false,
        bool includeSecurityId = false)
    {
        var asOf = new DateTimeOffset(2026, 5, 28, 16, 0, 0, TimeSpan.Zero);
        var generatedAt = asOf.AddMinutes(5);
        var lineagePointers = new List<FundReportPackLineagePointerDto>
        {
            new(
                "report",
                "summary",
                "run",
                "run-report-001",
                DisplayLabel: "Report Strategy (run-report-001)",
                Route: "/api/workstation/runs/run-report-001/continuity",
                SourceSystem: "strategy-run"),
            new(
                "line",
                "Securities:AAPL",
                "ledger-account",
                "Securities",
                DisplayLabel: "Securities / AAPL ledger line",
                Route: "/api/workstation/runs/run-report-001/ledger/trial-balance?accountName=Securities&symbol=AAPL",
                SourceSystem: "ledger",
                RelatedEvidenceIds: ["ledger-line-1"],
                EvidenceCount: 1,
                Amount: 400m,
                CapturedAt: asOf.AddMinutes(-2)),
            new(
                "line",
                "Securities:AAPL",
                "security",
                "AAPL",
                DisplayLabel: "AAPL",
                Route: "/api/workstation/security-master/search?query=AAPL",
                SourceSystem: "security-master",
                RelatedEvidenceIds: includeSecurityId
                    ? ["journal-entry-1", AaplSecurityId.ToString("D")]
                    : ["journal-entry-1"],
                EvidenceCount: includeSecurityId ? 2 : 1,
                Amount: 400m,
                CapturedAt: asOf.AddMinutes(-2)),
            new(
                "section",
                "reconciliation",
                "reconciliation-summary",
                "runs:2;open-breaks:1",
                DisplayLabel: "2 reconciliation run(s), 1 open break(s)",
                Route: "/api/workstation/reconciliation/runs",
                SourceSystem: "reconciliation")
        };

        if (includeProviderEvent)
        {
            lineagePointers.Add(new FundReportPackLineagePointerDto(
                "line",
                "Securities:AAPL",
                "provider-event",
                "alpaca-position-aapl",
                DisplayLabel: "Alpaca AAPL position snapshot",
                Route: "/api/fund-accounts/account-a/brokerage-sync/reconciliation/latest",
                SourceSystem: "provider",
                RelatedEvidenceIds: ["provider-position-aapl"],
                EvidenceCount: 1,
                Amount: 400m,
                CapturedAt: asOf.AddMinutes(-3)));
        }

        return new FundReportPackSnapshotDto(
            ReportId: reportId,
            FundProfileId: "fund-ops",
            DisplayName: "Fund Operations Trial Balance",
            ReportKind: GovernanceReportKindDto.TrialBalance,
            Currency: "USD",
            AsOf: asOf,
            GeneratedAt: generatedAt,
            TotalNetAssets: 400m,
            AuditActor: "ops",
            CorrelationId: "corr-ledger-provenance",
            DecisionRationale: "monthly close",
            Provenance: new FundReportPackProvenanceDto(
                RelatedRunIds: ["run-report-001"],
                JournalEntryCount: 1,
                LedgerEntryCount: 1,
                TrialBalanceLineCount: 1,
                ReconciliationRunCount: 2,
                OpenReconciliationBreakCount: 1,
                SecurityResolvedCount: 1,
                SecurityMissingCount: 0,
                LineagePointers: lineagePointers,
                SourceSnapshotHash: new string('a', 64)),
            Artifacts: [],
            Warnings: [])
        {
            Status = GovernanceReportPackStatusDto.Approved,
            LifecycleEvents =
            [
                new FundReportPackLifecycleEventDto(
                    GovernanceReportPackStatusDto.Generated,
                    GovernanceReportPackStatusDto.Approved,
                    generatedAt,
                    "controller",
                    "approved for close",
                    "corr-ledger-provenance")
            ]
        };
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-ledger-provenance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class InMemoryReportPackRepository(FundReportPackSnapshotDto snapshot) : IGovernanceReportPackRepository
    {
        public Task<FundReportPackSnapshotDto> SaveAsync(
            FundReportPackSnapshotDto snapshot,
            IReadOnlyList<GovernanceReportPackArtifactContent> artifacts,
            CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<IReadOnlyList<FundReportPackHistoryItemDto>> GetHistoryAsync(
            string fundProfileId,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FundReportPackHistoryItemDto>>([]);

        public Task<FundReportPackSnapshotDto?> GetAsync(Guid reportId, CancellationToken ct = default)
            => Task.FromResult(reportId == snapshot.ReportId ? snapshot : null);

        public Task<FundReportPackSnapshotDto?> FindLatestByRunIdAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<FundReportPackSnapshotDto?>(null);
    }
}
