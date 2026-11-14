using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
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
using NSubstitute;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationCaseSummaryDto = Meridian.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_PortfolioWorkspace_WithoutRunReadService_ShouldReturnEmptyRunsAndStableShape()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var portfolio = await ReadJsonAsync(client, "/api/workstation/portfolio");

        portfolio.RootElement.GetProperty("metrics").GetArrayLength().Should().Be(4);
        portfolio.RootElement.GetProperty("positions").GetArrayLength().Should().Be(0);
        portfolio.RootElement.GetProperty("runs").GetArrayLength().Should().Be(0);
        portfolio.RootElement.GetProperty("risk").GetProperty("state").GetString().Should().Be("Healthy");
        portfolio.RootElement.GetProperty("brokerage").GetProperty("connection").GetString().Should().NotBeNullOrEmpty();

        // CashFlow summary is always present (zeroed when there is no run data) so the
        // Portfolio screen never has to discriminate between "no data" and "missing field".
        var cashFlow = portfolio.RootElement.GetProperty("cashFlow");
        cashFlow.ValueKind.Should().NotBe(JsonValueKind.Null);
        cashFlow.GetProperty("runsWithCashSignals").GetInt32().Should().Be(0);
        cashFlow.GetProperty("runsWithCashVariance").GetInt32().Should().Be(0);
        cashFlow.GetProperty("tone").GetString().Should().Be("default");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingWorkspace_ShouldReturnIndependentTypedCapability()
    {
        var capability = ReadyReportingDeploymentCapability();
        var readiness = Substitute.For<IReportingDeploymentReadinessService>();
        readiness.Evaluate().Returns(capability);
        await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(readiness);
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog()));
            },
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewReporting);
        var client = app.GetTestClient();

        using var reporting = await ReadJsonAsync(client, "/api/workstation/reporting");

        reporting.RootElement.TryGetProperty("metrics", out _).Should().BeFalse();
        reporting.RootElement.TryGetProperty("workspace", out _).Should().BeFalse();
        reporting.RootElement.TryGetProperty("reporting", out _).Should().BeFalse();
        var reportingSection = reporting.RootElement;
        reportingSection.GetProperty("profileCount").GetInt32().Should().BeGreaterThan(0);
        reportingSection.GetProperty("summary").GetString().Should().Contain("profiles are available");
        var deploymentCapability = reportingSection.GetProperty("deploymentCapability");
        deploymentCapability.GetProperty("isReady").GetBoolean().Should().BeTrue();
        deploymentCapability.GetProperty("durableReconciliationEvidence").GetBoolean()
            .Should().BeTrue();
        reportingSection.TryGetProperty("reportPackTargets", out _).Should().BeFalse();
        var distributions = reportingSection.GetProperty("reportPackDistributions").EnumerateArray().ToArray();
        distributions.Should().NotBeEmpty();
        distributions.Select(distribution => distribution.GetProperty("recipient").GetString())
            .Should().Contain("Board reporting committee").And.Contain("Investor relations").And.Contain("Compliance archive");
        distributions.Should().AllSatisfy(distribution =>
        {
            distribution.GetProperty("channel").GetString().Should().NotBeNullOrWhiteSpace();
            distribution.GetProperty("state").GetString().Should().NotBeNullOrWhiteSpace();
            distribution.GetProperty("pendingSummary").GetString().Should().NotBeNullOrWhiteSpace();
        });

        var profiles = reportingSection.GetProperty("profiles").EnumerateArray().ToArray();
        profiles.Should().NotBeEmpty();
        profiles.Should().AllSatisfy(profile =>
        {
            profile.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
            profile.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
            profile.GetProperty("format").GetString().Should().NotBeNullOrEmpty();
        });

        var recommended = reportingSection.GetProperty("recommendedProfiles").EnumerateArray()
            .Select(r => r.GetString())
            .ToArray();
        recommended.Should().Contain(value => value == "excel" || value == "python-pandas");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingWorkspace_ProjectsCanonicalRunsAndImmutableDeliveryReceipts()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var scope = new ReportingOperationalScope(
            "tenant-test",
            "organization-test",
            "tenant-test",
            "fund-test",
            "book-test",
            "2026-07");
        var access = new ReportingAccessScope(
            "policy-company-test",
            "1",
            ReportingGovernanceAccessMode.CompanyWide,
            "ops-user",
            AllowOwnerAccess: true,
            Principals: [],
            PolicyHash: new string('a', 64));
        var manifest = new ReportingOutputManifest(
            "canonical-run-001",
            "investor-monthly-statement",
            new DateOnly(2026, 7, 25),
            ReportingRunStatus.Released,
            [],
            ["artifact://canonical-run-001/investor-statement.pdf"],
            1,
            ReportingRunTrigger.AdHoc,
            OperationalScope: scope,
            ImmutableAccessScope: access);
        var snapshot = new ReportingRunSnapshot(
            manifest,
            [new ReportingRunAuditEntry(
                manifest.RunId,
                now.AddMinutes(-5),
                "Released",
                "release.officer",
                "Canonical package released.")],
            now);
        var runStore = Substitute.For<IReportingRunStore>();
        runStore
            .ListRuns("tenant-test", "tenant-test", Arg.Any<int>(), Arg.Any<int>())
            .Returns([snapshot]);

        const string packageId = "report-package-canonical-001";
        var release = new ReportingDeliveryReleaseAuthorization(
            "release-receipt-001",
            ReportingReleaseState.Released,
            "tenant-test",
            packageId,
            manifest.RunId,
            "revision-1",
            new string('b', 64),
            [new ReportingReleasedArtifactReference(
                "investor-statement.pdf",
                "reporting-artifact://tenant-test/investor-statement.pdf",
                new string('c', 64),
                2048)],
            ["release-evidence:001"],
            now.AddMinutes(-4),
            "release.officer",
            "release-proof-001");
        var receipt = new ReportingDeliveryReceipt(
            "provider-receipt-001",
            ReportingDeliveryReceiptKind.Delivered,
            now,
            "email-relay",
            "provider-message-001",
            "provider-evidence:001",
            "Delivered to the governed recipient.");
        var job = new ReportingDeliveryJobRecord(
            "delivery-canonical-001",
            "tenant-test",
            packageId,
            "investor-relations",
            "email-relay",
            release,
            "controller.user",
            new string('d', 64),
            new ReportingDeliveryPayload(
                "Investor relations",
                "Investor relations",
                "investor@example.test",
                "Your report is ready",
                "Use the secure portal.",
                $"/portal/reporting/secure/packages/{manifest.RunId}"),
            ReportingDeliveryState.Delivered,
            1,
            3,
            now.AddMinutes(-3),
            now,
            null,
            null,
            null,
            null,
            null,
            "provider-message-001",
            null,
            [receipt]);
        var deliveryStore = Substitute.For<IReportingDeliveryStore>();
        deliveryStore
            .ListByRunAsync("tenant-test", manifest.RunId, Arg.Any<CancellationToken>())
            .Returns([job]);

        var legacyWorkflow = new ReportPackWorkflowService();
        legacyWorkflow.Create(
            "legacy-fund",
            "legacy-account",
            "2026-07",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "legacy.author",
            accessPolicy: new ReportAccessPolicyDto(
                ReportAccessModeDto.CompanyWide,
                CompanyId: "tenant-test"),
            accessContext: new ReportAccessQueryContext(
                "legacy.author",
                CompanyId: "tenant-test",
                TenantId: "tenant-test",
                RequireBoundScope: true));

        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog(),
                    runStore,
                    legacyWorkflow,
                    canonicalDeliveryStore: deliveryStore));
            },
            currentUserPermissions: UserPermission.ViewReporting);

        var payload = await app.GetTestClient()
            .GetFromJsonAsync<WorkstationReportingPayload>(
                "/api/workstation/reporting",
                ServerJsonOptions);

        payload.Should().NotBeNull();
        payload!.RecentRuns.Should().ContainSingle(run => run.RunId == manifest.RunId);
        payload.RecentRuns.Should().NotContain(run =>
            run.RunId.StartsWith("report-pack:", StringComparison.Ordinal));
        payload.DeliveryAttempts.Should().BeEmpty(
            "legacy GUID-based attempts are compatibility-only in production composition");
        var canonical = payload.CanonicalDeliveries.Should().ContainSingle().Subject;
        canonical.JobId.Should().Be(job.JobId);
        canonical.PackageId.Should().Be(packageId);
        canonical.ArtifactManifestHashSha256.Should().Be(release.ArtifactManifestHashSha256);
        canonical.Receipts.Should().ContainSingle(projected =>
            projected.ReceiptId == receipt.ReceiptId
            && projected.Kind == nameof(ReportingDeliveryReceiptKind.Delivered)
            && projected.ProviderReference == receipt.ProviderReference);
        payload.ReportPackDistributions.Should().ContainSingle(distribution =>
            distribution.DistributionId == job.DistributionId
            && distribution.LastSentAtUtc == receipt.OccurredAtUtc);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingWorkspace_WithoutDurableAuthority_ShouldFailClosed()
    {
        var readiness = Substitute.For<IReportingDeploymentReadinessService>();
        readiness.Evaluate().Returns(new ReportingDeploymentCapabilityDto(
            IsReady: false,
            DurableGovernance: true,
            DurableArtifacts: true,
            DurableReconciliationEvidence: true,
            DurableRuns: false,
            DurableScheduling: false,
            DurableDelivery: true,
            RecipientDestinationsConfigured: false,
            ClientDocumentsConfigured: true,
            MigrationsManaged: true,
            Components:
            [
                new("runs", "Run history", false, "Certified reporting runs are not backed by the PostgreSQL authority."),
                new("scheduling", "Scheduling", false, "Reporting schedules are not backed by the PostgreSQL authority.")
            ],
            BlockingReasons:
            [
                "Certified reporting runs are not backed by the PostgreSQL authority.",
                "Reporting schedules are not backed by the PostgreSQL authority."
            ]));
        await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(readiness);
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog()));
            },
            currentUserPermissions: UserPermission.ViewReporting);

        var response = await app.GetTestClient().GetAsync("/api/workstation/reporting");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ServerJsonOptions);
        problem.GetProperty("detail").GetString().Should().Contain("PostgreSQL authority");
    }

    [Theory]
    [InlineData("/api/workstation/reporting")]
    [InlineData("/api/workstation/reporting/structured-exports/investment-portfolio-cuts")]
    public async Task MapWorkstationEndpoints_ReportingAuthority_WithoutTenantCompanyScope_ShouldDenyBeforeRead(
        string route)
    {
        var runStore = Substitute.For<IReportingRunStore>();
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog(),
                    runStore));
            },
            currentUserPermissions: UserPermission.ViewReporting,
            currentUserCompanyId: null,
            currentUserTenantId: "tenant-test");

        var response = await app.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        runStore.ReceivedCalls().Should().BeEmpty(
            "tenant/company validation must run before any reporting-store read");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingStructuredExport_WhenReadinessProbeThrows_ShouldFailClosed()
    {
        var readiness = Substitute.For<IReportingDeploymentReadinessService>();
        readiness.Evaluate().Returns(_ =>
            throw new InvalidOperationException("Simulated reporting readiness probe failure."));
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton(readiness);
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog()));
            },
            currentUserPermissions: UserPermission.ViewReporting);

        var response = await app.GetTestClient().GetAsync(
            "/api/workstation/reporting/structured-exports/investment-portfolio-cuts");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ServerJsonOptions);
        problem.GetProperty("detail").GetString()
            .Should().Contain("durable reporting deployment is ready");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingWorkspace_WhenReadinessProbeThrows_ShouldFailClosedBeforeStoreRead()
    {
        var readiness = Substitute.For<IReportingDeploymentReadinessService>();
        readiness.Evaluate().Returns(_ =>
            throw new InvalidOperationException("Simulated reporting readiness probe failure."));
        var runStore = Substitute.For<IReportingRunStore>();
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton(readiness);
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog(),
                    runStore));
            },
            currentUserPermissions: UserPermission.ViewReporting);

        var response = await app.GetTestClient().GetAsync("/api/workstation/reporting");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ServerJsonOptions);
        problem.GetProperty("detail").GetString()
            .Should().Contain("reporting deployment capability");
        runStore.ReceivedCalls().Should().BeEmpty(
            "the independent readiness gate must fail before any authoritative reporting-store read");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingStoreFailure_ShouldFailReportingClosedAndDegradeAccountingSection()
    {
        var runStore = Substitute.For<IReportingRunStore>();
        runStore
            .ListRuns(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(_ => throw new InvalidOperationException("Simulated reporting store failure."));
        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterRunReadServices(services);
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog(),
                    runStore));
            },
            currentUserPermissions: UserPermission.ViewReporting | UserPermission.ViewTrades);
        var client = app.GetTestClient();

        using var reportingResponse = await client.GetAsync("/api/workstation/reporting");
        using var accountingResponse = await client.GetAsync("/api/workstation/accounting");

        reportingResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        accountingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var accounting = await JsonDocument.ParseAsync(
            await accountingResponse.Content.ReadAsStreamAsync());
        accounting.RootElement.GetProperty("metrics").ValueKind.Should().Be(JsonValueKind.Array);
        var reporting = accounting.RootElement.GetProperty("reporting");
        reporting.GetProperty("profileCount").GetInt32().Should().Be(0);
        reporting.GetProperty("recentRuns").GetArrayLength().Should().Be(0);
        var capability = reporting.GetProperty("deploymentCapability");
        capability.GetProperty("isReady").GetBoolean().Should().BeFalse();
        capability.GetProperty("durableReconciliationEvidence").GetBoolean()
            .Should().BeTrue();
        capability.GetProperty("blockingReasons").EnumerateArray()
            .Select(reason => reason.GetString())
            .Should().Contain("The authoritative reporting store is temporarily unavailable.");
    }

    private static ReportingDeploymentCapabilityDto ReadyReportingDeploymentCapability()
    {
        var components = new[]
        {
            new ReportingDeploymentComponentDto("governance", "Governance", true, "Ready."),
            new ReportingDeploymentComponentDto("artifacts", "Artifact vault", true, "Ready."),
            new ReportingDeploymentComponentDto(
                "reconciliation-evidence",
                "Reconciliation evidence",
                true,
                "Ready."),
            new ReportingDeploymentComponentDto("runs", "Run history", true, "Ready."),
            new ReportingDeploymentComponentDto("scheduling", "Scheduling", true, "Ready."),
            new ReportingDeploymentComponentDto("delivery", "Delivery", true, "Ready."),
            new ReportingDeploymentComponentDto("recipient-destinations", "Recipient destinations", true, "Ready."),
            new ReportingDeploymentComponentDto("client-documents", "Client documents", true, "Ready."),
            new ReportingDeploymentComponentDto("migrations", "Reporting migrations", true, "Ready.")
        };
        return new ReportingDeploymentCapabilityDto(
            IsReady: true,
            DurableGovernance: true,
            DurableArtifacts: true,
            DurableReconciliationEvidence: true,
            DurableRuns: true,
            DurableScheduling: true,
            DurableDelivery: true,
            RecipientDestinationsConfigured: true,
            ClientDocumentsConfigured: true,
            MigrationsManaged: true,
            Components: components,
            BlockingReasons: []);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportingStructuredExport_ShouldReturnJsonAndCsvFromRetainedReportingPayload()
    {
        var workflow = new ReportPackWorkflowService();
        workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            [
                PortfolioReportingLine("portfolio.gross-exposure", "evidence-gross", "2500000"),
                PortfolioReportingLine("portfolio.net-exposure", "evidence-net", "1800000"),
                PortfolioReportingLine("portfolio.cash", "evidence-cash", "375000"),
                PortfolioReportingLine("portfolio.realized-pnl", "evidence-realized", "42000"),
                PortfolioReportingLine("portfolio.unrealized-pnl", "evidence-unrealized", "18000"),
                PortfolioReportingLine("portfolio.shadow-nav", "evidence-shadow-nav", "2935000"),
                PortfolioReportingLine("portfolio.reported-nav", "evidence-reported-nav", "2920000")
            ],
            accessPolicy: new ReportAccessPolicyDto(
                ReportAccessModeDto.Restricted,
                Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")]),
            accessContext: new ReportAccessQueryContext(
                ActorPrincipalId: "report.author",
                CompanyId: "tenant-test",
                TenantId: "tenant-test",
                RequireBoundScope: true));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ReportPackRunReadService(
                new DefaultReportingTemplateCatalog(),
                workflowService: workflow));
        },
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewReporting,
            currentUserRoleProfileName: "ops-control");
        var client = app.GetTestClient();
        var requestedRoutes = new List<string>();

        var jsonResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/investment-portfolio-cuts");
        var csvResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/investment-portfolio-cuts?format=csv");
        var xlsxResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/investment-portfolio-cuts?format=xlsx");
        var xlsAliasResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/investment-portfolio-cuts?format=xls");
        var analyticsJsonResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics");
        var analyticsCsvResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics?format=csv");
        var crossFundJsonResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/cross-fund-consolidation");
        var crossFundXlsxResponse = await GetAndRecordAsync(client, "/api/workstation/reporting/structured-exports/cross-fund-consolidation?format=xlsx");

        requestedRoutes.Should().Contain("/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics");
        requestedRoutes.Should().Contain("/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics?format=csv");
        requestedRoutes.Should().Contain("/api/workstation/reporting/structured-exports/cross-fund-consolidation");
        requestedRoutes.Should().Contain("/api/workstation/reporting/structured-exports/cross-fund-consolidation?format=xlsx");
        requestedRoutes.Should().Contain("/api/workstation/reporting/structured-exports/investment-portfolio-cuts?format=xls");

        jsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        jsonResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var payload = await jsonResponse.Content.ReadFromJsonAsync<StructuredReportingExportPayloadDto>(ServerJsonOptions);
        payload.Should().NotBeNull();
        payload!.Export.ExportId.Should().Be("investment-portfolio-cuts");
        payload.Export.Route.Should().Contain("/api/workstation/reporting/structured-exports/investment-portfolio-cuts");
        payload.Columns.Select(static column => column.Name).Should().ContainInOrder(
            "cutId",
            "label",
            "kind",
            "currency",
            "grossExposure",
            "netExposure",
            "totalCash",
            "pendingSettlement",
            "totalPnl",
            "shadowNav",
            "shadowNavVariance",
            "sourceCount",
            "versionStamp");
        payload.DataDictionary.Should().NotBeNull();
        payload.DataDictionary!.Should().Contain(field =>
            field.Name == "shadowNav" &&
            field.DataType == "decimal");
        payload.ValidationChecks.Should().NotBeNull();
        payload.ValidationChecks!.Should().Contain(check =>
            check.CheckId == "readiness" &&
            check.Status == "Passed");
        payload.RowLineage.Should().NotBeNull();
        payload.RowLineage!.Should().Contain(lineage =>
            lineage.RowNumber == 1 &&
            lineage.RowKey == "reporting-portfolio-cut:fund-alpha" &&
            lineage.RowHashSha256.Length == 64);
        payload.Export.RowLineageCount.Should().Be(payload.RowLineage.Count);
        payload.Rows.Should().Contain(row =>
            row["cutId"] == "reporting-portfolio-cut:fund-alpha" &&
            row["grossExposure"] == "2500000" &&
            row["totalPnl"] == "60000" &&
            row["shadowNav"] == "2935000");

        csvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        csvResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        csvResponse.Content.Headers.ContentDisposition?.FileName.Should().EndWith(".csv");
        var csv = await csvResponse.Content.ReadAsStringAsync();
        csv.Should().StartWith("cutId,label,kind,currency,grossExposure,netExposure,totalCash,pendingSettlement,totalPnl,shadowNav,shadowNavVariance,sourceCount,versionStamp");
        csv.Should().Contain("reporting-portfolio-cut:fund-alpha");
        xlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsxResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using (var workbook = new ZipArchive(new MemoryStream(await xlsxResponse.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read))
        {
            workbook.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet3.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet4.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet5.xml").Should().NotBeNull();
            using var workbookXmlReader = new StreamReader(workbook.GetEntry("xl/workbook.xml")!.Open());
            var workbookXml = workbookXmlReader.ReadToEnd();
            workbookXml.Should().Contain("portfolio-reporting-cuts");
            workbookXml.Should().Contain("Metadata");
            workbookXml.Should().Contain("DataDictionary");
            workbookXml.Should().Contain("Validation");
            workbookXml.Should().Contain("RowLineage");
        }
        xlsAliasResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsAliasResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsAliasResponse.Content.Headers.ContentDisposition?.FileName.Should().EndWith(".xlsx");
        using (var workbook = new ZipArchive(new MemoryStream(await xlsAliasResponse.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read))
        {
            workbook.GetEntry("xl/workbook.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        }
        analyticsJsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var analyticsPayload = await analyticsJsonResponse.Content.ReadFromJsonAsync<StructuredReportingExportPayloadDto>(ServerJsonOptions);
        analyticsPayload.Should().NotBeNull();
        analyticsPayload!.Export.ExportId.Should().Be("investment-topn-contribution-analytics");
        analyticsPayload.Export.Route.Should().Contain("/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics");
        analyticsPayload.Columns.Select(static column => column.Name).Should().ContainInOrder(
            "analyticsId",
            "kind",
            "scope",
            "rank",
            "label",
            "totalPnl",
            "contributionPercent",
            "heatMapIntensity");
        analyticsPayload.Rows.Should().Contain(row =>
            row["kind"] == "TopWinner" &&
            row["totalPnl"] == "60000" &&
            row["contributionPercent"] == "100");
        analyticsCsvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        analyticsCsvResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        analyticsCsvResponse.Content.Headers.ContentDisposition?.FileName.Should().EndWith(".csv");
        var analyticsCsv = await analyticsCsvResponse.Content.ReadAsStringAsync();
        analyticsCsv.Should().StartWith("analyticsId,kind,scope,rank,label,symbol,classification,currency,realizedPnl,unrealizedPnl,totalPnl,contributionPercent,heatMapIntensity");
        analyticsCsv.Should().Contain("TopWinner");
        crossFundJsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var crossFundPayload = await crossFundJsonResponse.Content.ReadFromJsonAsync<StructuredReportingExportPayloadDto>(ServerJsonOptions);
        crossFundPayload.Should().NotBeNull();
        crossFundPayload!.Export.ExportId.Should().Be("cross-fund-consolidation");
        crossFundPayload.Export.Route.Should().Contain("/api/workstation/reporting/structured-exports/cross-fund-consolidation");
        crossFundPayload.Columns.Select(static column => column.Name).Should().ContainInOrder(
            "consolidationId",
            "label",
            "scope",
            "grossExposure",
            "netExposure",
            "totalPnl",
            "shadowNav",
            "shadowNavVariance");
        crossFundPayload.Rows.Should().Contain(row =>
            row["consolidationId"] == "reporting-cross-fund:company" &&
            row["grossExposure"] == "2500000" &&
            row["shadowNav"] == "2935000");
        crossFundPayload.RowLineage.Should().NotBeNull();
        crossFundPayload.RowLineage!.Should().Contain(lineage =>
            lineage.RowKey == "reporting-cross-fund:company" &&
            lineage.RowHashSha256.Length == 64);
        crossFundPayload.Export.RowLineageCount.Should().Be(crossFundPayload.RowLineage.Count);
        crossFundXlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        crossFundXlsxResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using (var workbook = new ZipArchive(new MemoryStream(await crossFundXlsxResponse.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read))
        {
            workbook.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            using var workbookXmlReader = new StreamReader(workbook.GetEntry("xl/workbook.xml")!.Open());
            var workbookXml = workbookXmlReader.ReadToEnd();
            workbookXml.Should().Contain("cross-fund-reporting-consolidat");
            workbookXml.Should().Contain("Metadata");
            workbookXml.Should().Contain("DataDictionary");
        }

        await using var strangerApp = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ReportPackRunReadService(
                new DefaultReportingTemplateCatalog(),
                workflowService: workflow));
        },
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewReporting);
        var strangerClient = strangerApp.GetTestClient();
        var strangerResponse = await strangerClient.GetAsync("/api/workstation/reporting/structured-exports/investment-portfolio-cuts");
        var strangerAnalyticsResponse = await strangerClient.GetAsync("/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics");
        var strangerCrossFundResponse = await strangerClient.GetAsync("/api/workstation/reporting/structured-exports/cross-fund-consolidation");
        strangerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        strangerAnalyticsResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        strangerCrossFundResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        async Task<HttpResponseMessage> GetAndRecordAsync(HttpClient httpClient, string route)
        {
            requestedRoutes.Add(route);
            return await httpClient.GetAsync(route);
        }

        static ReportPackLineProvenanceDto PortfolioReportingLine(
            string lineKey,
            string evidenceId,
            string reportValue) =>
            new(
                lineKey,
                "portfolio",
                "run-1",
                evidenceId,
                RunId: "run-1",
                ReportValue: reportValue);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_WithLiveQuote_ReplacesPlaceholderMarkAndComputesUnrealizedPnl()
    {
        var publisher = new Meridian.Tests.TestHelpers.TestMarketEventPublisher();
        var quotes = new Meridian.Domain.Collectors.QuoteCollector(publisher);
        quotes.OnQuote(new Meridian.Contracts.Domain.Models.MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            BidPrice: 199.90m,
            BidSize: 100,
            AskPrice: 200.10m,
            AskSize: 100,
            StreamId: "TEST",
            Venue: "TEST", Source: "TEST"));

        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "AAPL",
            Quantity: 100,
            AverageCostBasis: 150m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
            services.AddSingleton(quotes);
        }, currentUserPermissions: UserPermission.ViewTrades);

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var rows = trading.RootElement.GetProperty("positions").EnumerateArray().ToArray();
        rows.Should().NotBeEmpty();
        var row = rows[0];
        row.GetProperty("positionKey").GetString().Should().Be("AAPL");
        row.GetProperty("symbol").GetString().Should().Be("AAPL");

        // Mid of (199.90, 200.10) = 200.00
        row.GetProperty("markPrice").GetString().Should().Be("200.00");

        // (200 - 150) * 100 = +5,000 → FormatCurrency renders "+$5K"
        var unrealized = row.GetProperty("unrealizedPnl").GetString();
        unrealized.Should().NotBe("—");
        unrealized.Should().Be("+$5K");

        // |100 * 200| = 20,000 → "+$20K"
        var exposure = row.GetProperty("exposure").GetString();
        exposure.Should().NotBe("—");
        exposure.Should().Be("+$20K");

        // Risk panel net/gross exposure should also reflect live mark, not cost basis
        // (cost-basis exposure would have been |100 * 150| = 15,000 → "+$15K")
        var risk = trading.RootElement.GetProperty("risk");
        risk.GetProperty("netExposure").GetString().Should().Be("+$20K");
        risk.GetProperty("grossExposure").GetString().Should().Be("+$20K");
        // Buying power = Cash (100K, default) → 20,000 / 100,000 = 20% utilisation.
        risk.GetProperty("buyingPowerUsed").GetString().Should().Be("+20.0%");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_WithoutQuote_FallsBackToLastTradePrice()
    {
        var publisher = new Meridian.Tests.TestHelpers.TestMarketEventPublisher();
        var quotes = new Meridian.Domain.Collectors.QuoteCollector(publisher);
        var trades = new Meridian.Domain.Collectors.TradeDataCollector(publisher, quotes);
        trades.OnTrade(new Meridian.Domain.Models.MarketTradeUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 175m,
            Size: 50,
            Aggressor: Meridian.Contracts.Domain.Enums.AggressorSide.Buy,
            SequenceNumber: 1,
            StreamId: "TEST",
            Venue: "TEST", Source: "TEST"));

        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "AAPL",
            Quantity: 100,
            AverageCostBasis: 150m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
            services.AddSingleton(quotes);
            services.AddSingleton(trades);
        }, currentUserPermissions: UserPermission.ViewTrades);

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var row = trading.RootElement.GetProperty("positions").EnumerateArray().First();
        row.GetProperty("markPrice").GetString().Should().Be("175.00");
        // (175 - 150) * 100 = +2,500 → "+$2.5K"
        row.GetProperty("unrealizedPnl").GetString().Should().Be("+$2.5K");
        // |100 * 175| = 17,500 → "+$17.5K"
        row.GetProperty("exposure").GetString().Should().Be("+$17.5K");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_TradingPayload_WithoutAnyMarketData_RetainsPlaceholderMark()
    {
        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "AAPL",
            Quantity: 100,
            AverageCostBasis: 150m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
        }, currentUserPermissions: UserPermission.ViewTrades);

        var client = app.GetTestClient();
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");

        var row = trading.RootElement.GetProperty("positions").EnumerateArray().First();
        row.GetProperty("markPrice").GetString().Should().Be("—");
        row.GetProperty("exposure").GetString().Should().Be("—");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_PortfolioPayload_WithLiveQuote_SurfacesLiveMarkAndExposure()
    {
        var publisher = new Meridian.Tests.TestHelpers.TestMarketEventPublisher();
        var quotes = new Meridian.Domain.Collectors.QuoteCollector(publisher);
        quotes.OnQuote(new Meridian.Contracts.Domain.Models.MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "MSFT",
            BidPrice: 419.50m,
            BidSize: 200,
            AskPrice: 420.50m,
            AskSize: 200,
            StreamId: "TEST",
            Venue: "TEST", Source: "TEST"));

        var position = new Meridian.Execution.Models.ExecutionPosition(
            Symbol: "MSFT",
            Quantity: 50,
            AverageCostBasis: 400m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new LiveMarkTestPortfolioState(position));
            services.AddSingleton(quotes);
        }, currentUserPermissions: UserPermission.ViewTrades);

        var client = app.GetTestClient();
        using var portfolio = await ReadJsonAsync(client, "/api/workstation/portfolio");

        var row = portfolio.RootElement.GetProperty("positions").EnumerateArray().First();
        // Mid of (419.50, 420.50) = 420.00
        row.GetProperty("markPrice").GetString().Should().Be("420.00");
        // (420 - 400) * 50 = +1,000 → "+$1K"
        row.GetProperty("unrealizedPnl").GetString().Should().Be("+$1K");
        // |50 * 420| = 21,000 → "+$21K"
        row.GetProperty("exposure").GetString().Should().Be("+$21K");

        var risk = portfolio.RootElement.GetProperty("risk");
        risk.GetProperty("grossExposure").GetString().Should().Be("+$21K");
        // Buying power = Cash (100K, default) → 21,000 / 100,000 = 21% utilisation.
        risk.GetProperty("buyingPowerUsed").GetString().Should().Be("+21.0%");
    }
}
