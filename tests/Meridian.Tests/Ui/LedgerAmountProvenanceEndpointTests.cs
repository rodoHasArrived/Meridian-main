using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerAmountProvenanceEndpointTests
{
    private static readonly Guid AaplSecurityId = Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D");
    private static readonly ReconciliationBreakQueueScope TestScope =
        new("tenant-test", "company-test");
    private static readonly ReconciliationBreakQueueScope ForeignScope =
        new("tenant-foreign", "company-foreign");

    [Fact]
    public async Task Endpoint_GetLedgerProvenance_LegacyLabelDoesNotProduceProof()
    {
        var reportId = Guid.NewGuid();
        await using var app = await CreateEndpointAppAsync(BuildSnapshot(reportId, includeProviderEvent: true));
        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/report-packs/{reportId:D}/ledger-provenance?scopeKey=Securities%3AAAPL");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Endpoint_GetLedgerProvenance_RequiresTenantAndCompanyScope()
    {
        var reportId = Guid.NewGuid();
        await using var app = await CreateEndpointAppAsync(
            BuildSnapshot(reportId, includeProviderEvent: true),
            includeTenantCompanyScope: false);

        var response = await app.GetTestClient().GetAsync(
            $"/api/fund-structure/report-packs/{reportId:D}/ledger-provenance?scopeKey={Uri.EscapeDataString("Securities:AAPL")}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateEndpointAppAsync(
        FundReportPackSnapshotDto snapshot,
        bool includeTenantCompanyScope = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        var reportRepository = new InMemoryReportPackRepository(snapshot);
        var queueRoot = CreateTempRoot();
        var breakRepository = new FileReconciliationBreakQueueRepository(
            queueRoot,
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var securityMaster = new NullSecurityMasterQueryService();
        var fundGuard = Substitute.For<IFundProfileTenantGuard>();
        fundGuard.EvaluateAsync(
                Arg.Any<WorkstationTenantContext>(),
                "fund-ops",
                Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Allow("owned by the test tenant"));

        builder.Services.AddSingleton<IGovernanceReportPackRepository>(reportRepository);
        builder.Services.AddSingleton<IReconciliationBreakQueueRepository>(breakRepository);
        builder.Services.AddSingleton(new FundOperationsWorkspaceReadService(
            new InMemoryFundAccountService(),
            new StrategyRunStore(),
            new PortfolioReadService(),
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster),
            reportPackRepository: reportRepository));
        builder.Services.AddSingleton<LedgerAmountProvenanceService>();
        builder.Services.AddSingleton(fundGuard);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "reporting-test-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewReporting;
            if (includeTenantCompanyScope)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = TestScope.TenantId;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = TestScope.CompanyId;
            }
            await next();
        });
        app.MapFundStructureEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        app.Lifetime.ApplicationStopped.Register(() => DeleteTempRoot(queueRoot));
        await app.StartAsync();
        return app;
    }

    private static ReconciliationBreakQueueItem BuildScopedCase(
        FundReportPackSnapshotDto snapshot,
        string breakId,
        ReconciliationBreakQueueScope scope)
        => new(
            BreakId: breakId,
            RunId: "provider-ledger-run",
            StrategyName: "Provider ledger reconciliation",
            Category: ReconciliationBreakCategory.AmountMismatch,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 25m,
            Reason: "AAPL securities value differs from retained provider evidence.",
            AssignedTo: "fund-accounting",
            DetectedAt: snapshot.GeneratedAt.AddMinutes(-10),
            LastUpdatedAt: snapshot.GeneratedAt.AddMinutes(-5),
            Severity: ReconciliationBreakSeverity.High,
            ExceptionRoute: "accounting/reconciliation/provider-ledger",
            FundAccountId: "fund-ops",
            ExplainabilitySummary: "account=Securities, symbol=AAPL, variance=25",
            RoutingDetail: "Securities:AAPL",
            Team: "Accounting")
        {
            TenantId = scope.TenantId,
            CompanyId = scope.CompanyId
        };

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
            Artifacts:
            [
                new FundReportPackArtifactDto(
                    "manifest",
                    GovernanceReportArtifactFormatDto.Json,
                    "fund-ops/report-id/manifest.json",
                    512,
                    new string('b', 64))
            ],
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
            ],
            AuditPackReadiness = new FundAuditPackReadinessDto(
                IsComplete: true,
                GeneratedInSeconds: 1.25,
                SlaTargetSeconds: 60,
                SlaMet: true,
                MissingEvidenceCategories: [],
                Warnings: [],
                EvidenceCategorySummaries:
                [
                    new FundAuditEvidenceCategorySummaryDto(
                        FundAuditEvidenceCategoryKeyDto.Exports,
                        "Exports",
                        true,
                        "Export artifact retained.",
                        1,
                        ["fund-ops/report-id/manifest.json"],
                        "/api/fund-structure/report-packs")
                ])
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

        public Task<FundReportPackEvidenceBundleDto> SaveEvidenceBundleAsync(
            FundReportPackSnapshotDto snapshot,
            FundReportPackEvidenceBundleDto bundle,
            CancellationToken ct = default)
            => Task.FromResult(bundle);
    }
}
