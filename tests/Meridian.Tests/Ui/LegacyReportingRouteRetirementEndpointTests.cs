using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class LegacyReportingRouteRetirementEndpointTests
{
    private const string ClientSuppliedActor = "spoofed-client-actor";
    private const string ClientSuppliedApprover = "spoofed-client-approver";
    private const string ClientSuppliedHash = "sha256:client-supplied-hash";
    private const string ClientSuppliedPath = "c:/caller/chosen/manifest.json";
    private const string QueryToken = "query-token-must-never-be-echoed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task UnsafeLegacyReportingRoutes_ReturnGoneWithoutBindingCallerEvidenceOrMutatingLegacyState()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var reportId = Guid.Parse("18af2abc-7d46-41f1-ac0f-f0bc69aac5f2");
        var attemptId = Guid.Parse("ac337a4c-686d-4926-ac7b-bd6ce5f325c6");
        var routes = new (HttpMethod Method, string Route, string Guidance)[]
        {
            (HttpMethod.Post, "/api/fund-structure/report-pack-preview", "/api/fund-structure/reporting/runs/readiness"),
            (HttpMethod.Post, "/api/fund-structure/report-packs", "/api/fund-structure/reporting/runs"),
            (HttpMethod.Post, "/api/fund-structure/reporting/packs/create", "/api/fund-structure/reporting/runs"),
            (HttpMethod.Post, "/api/fund-structure/reporting/packs", "/api/fund-structure/reporting/runs"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/validate", "/api/fund-structure/reporting/runs/{runId}/validate"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/submit", "/api/fund-structure/reporting/runs/{runId}/submit"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/approve", "/api/fund-structure/reporting/runs/{runId}/approve"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/reject", "/api/fund-structure/reporting/runs"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/publish", "/api/fund-structure/reporting/runs/{runId}/release"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/restatements", "/api/fund-structure/reporting/runs/{runId}/restatement-requests"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/restate", "/api/fund-structure/reporting/runs/{runId}/restatement-requests"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/archive", "/api/fund-structure/reporting/runs/{runId}"),
            (HttpMethod.Get, $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries", "/api/fund-structure/reporting/distribution/packages/{runId}/deliveries"),
            (HttpMethod.Get, $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/{attemptId:D}/package?token={QueryToken}", "/portal/reporting/access-grants/{grantId}/exchange"),
            (HttpMethod.Get, $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/{attemptId:D}/artifacts/board-pack.pdf?token={QueryToken}", "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}"),
            (HttpMethod.Get, $"/portal/reporting/packages/package-legacy?token={QueryToken}", "/portal/reporting/access-grants/{grantId}/exchange"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries", "/api/fund-structure/reporting/distribution/deliveries"),
            (HttpMethod.Post, $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/failures", "/api/fund-structure/reporting/distribution/deliveries/{jobId}"),
            (HttpMethod.Get, $"/api/fund-structure/report-packs/{reportId:D}/evidence-bundle", "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}"),
            (HttpMethod.Post, "/api/fund-structure/reporting/schedules/run-due", "internal reporting scheduler worker")
        };

        const string callerControlledPayload = $$"""
            {
              "actor": "{{ClientSuppliedActor}}",
              "approver": "{{ClientSuppliedApprover}}",
              "signedOffBy": "{{ClientSuppliedApprover}}",
              "evidenceHash": "{{ClientSuppliedHash}}",
              "retainedManifestPath": "{{ClientSuppliedPath}}"
            }
            """;

        foreach (var (method, route, guidance) in routes)
        {
            using var request = new HttpRequestMessage(method, route);
            if (method == HttpMethod.Post)
            {
                request.Content = new StringContent(callerControlledPayload, Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.Gone, route);
            response.Headers.CacheControl.Should().NotBeNull(route);
            response.Headers.CacheControl!.NoStore.Should().BeTrue(route);
            body.Should().Contain("Legacy reporting route retired", route);
            body.Should().Contain(guidance, route);
            body.Should().NotContain(ClientSuppliedActor, route);
            body.Should().NotContain(ClientSuppliedApprover, route);
            body.Should().NotContain(ClientSuppliedHash, route);
            body.Should().NotContain(ClientSuppliedPath, route);
            body.Should().NotContain(QueryToken, route);
        }

        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var delivery = app.Services.GetRequiredService<ReportPackDeliveryService>();
        workflow.GetRecord(reportId).Should().BeNull();
        workflow.GetHistory("2026-03", "acct-1").Should().BeEmpty();
        delivery.ListAttempts().Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyPackHistoryRead_RequiresImmutableTenantCompanyAndAccessSnapshotEvenForAdmin()
    {
        var workflow = new ReportPackWorkflowService();
        var sameTenant = workflow.Create(
            "fund-alpha",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author.alpha",
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.CompanyWide, CompanyId: "company-alpha"),
            accessContext: BoundAccess("author.alpha", "tenant-alpha", "company-alpha"));
        workflow.Create(
            "fund-beta",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author.beta",
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.CompanyWide, CompanyId: "company-beta"),
            accessContext: BoundAccess("author.beta", "tenant-beta", "company-beta"));
        workflow.Create(
            "legacy-unbound",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "legacy.author");

        await using var app = await CreateAppAsync(workflow);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            "/api/fund-structure/reporting/packs/history?period=2026-03&fundAccountId=acct-1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto[]>(JsonOptions);

        records.Should().ContainSingle();
        records![0].ReportId.Should().Be(sameTenant.ReportId);
        records[0].TenantId.Should().Be("tenant-alpha");
        records[0].CompanyId.Should().Be("company-alpha");
        records[0].AccessPolicySnapshotHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LegacyPackHistoryRead_WithoutLegacyRepository_ReturnsGoneWithCanonicalGuidance()
    {
        await using var app = await CreateAppAsync();

        using var response = await app.GetTestClient().GetAsync(
            "/api/fund-structure/reporting/packs/history?period=2026-03&fundAccountId=acct-1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        body.Should().Contain("/api/fund-structure/reporting/runs");
        body.Should().Contain("Legacy reporting route retired");
    }

    [Fact]
    public async Task CanonicalRunHistory_ProjectsTenantScopedRunAndImmutableDeliveryReceipts()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var manifest = new ReportingOutputManifest(
            "canonical-run-history-001",
            "investor-monthly-statement",
            new DateOnly(2026, 7, 25),
            ReportingRunStatus.Released,
            [],
            ["artifact://canonical-run-history-001/investor-statement.pdf"],
            1,
            ReportingRunTrigger.AdHoc,
            OperationalScope: new ReportingOperationalScope(
                "tenant-alpha",
                "organization-alpha",
                "company-alpha",
                "fund-alpha",
                "book-alpha",
                "2026-07"),
            ImmutableAccessScope: new ReportingAccessScope(
                "policy-company-alpha",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                "admin.alpha",
                AllowOwnerAccess: true,
                Principals: [],
                PolicyHash: new string('a', 64)));
        var snapshot = new ReportingRunSnapshot(manifest, [], now);
        var runStore = Substitute.For<IReportingRunStore>();
        runStore
            .ListRuns("tenant-alpha", "company-alpha", Arg.Any<int>(), Arg.Any<int>())
            .Returns([snapshot]);

        const string packageId = "report-package-history-001";
        var receipt = new ReportingDeliveryReceipt(
            "receipt-history-001",
            ReportingDeliveryReceiptKind.Delivered,
            now,
            "secure-email",
            "provider-history-001",
            "provider-evidence-history-001");
        var release = new ReportingDeliveryReleaseAuthorization(
            "release-history-001",
            ReportingReleaseState.Released,
            "tenant-alpha",
            packageId,
            manifest.RunId,
            "revision-1",
            new string('b', 64),
            [],
            ["release-evidence-history-001"],
            now.AddMinutes(-1),
            "release.officer",
            "release-proof-history-001");
        var job = new ReportingDeliveryJobRecord(
            "delivery-history-001",
            "tenant-alpha",
            packageId,
            "investor-relations",
            "secure-email",
            release,
            "controller.user",
            new string('c', 64),
            new ReportingDeliveryPayload(
                "Investor relations",
                "Investor relations",
                "investor@example.test",
                "Report ready",
                "Use the secure portal.",
                $"/portal/reporting/secure/packages/{manifest.RunId}"),
            ReportingDeliveryState.Delivered,
            1,
            3,
            now.AddMinutes(-1),
            now,
            null,
            null,
            null,
            null,
            null,
            "provider-history-001",
            null,
            [receipt]);
        var deliveryStore = Substitute.For<IReportingDeliveryStore>();
        deliveryStore
            .ListByRunAsync("tenant-alpha", manifest.RunId, Arg.Any<CancellationToken>())
            .Returns([job]);
        var readiness = Substitute.For<IReportingDeploymentReadinessService>();
        readiness.Evaluate().Returns(new ReportingDeploymentCapabilityDto(
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
            Components: [],
            BlockingReasons: []));

        await using var app = await CreateAppAsync(
            configureServices: services =>
            {
                services.AddSingleton(readiness);
                services.AddSingleton(new ReportPackRunReadService(
                    new DefaultReportingTemplateCatalog(),
                    runStore,
                    canonicalDeliveryStore: deliveryStore));
            });

        var history = await app.GetTestClient()
            .GetFromJsonAsync<WorkstationReportingHistoryPayload>(
                "/api/fund-structure/reporting/runs?limit=10",
                JsonOptions);

        history.Should().NotBeNull();
        history!.Runs.Should().ContainSingle(run => run.RunId == manifest.RunId);
        var delivery = history.Deliveries.Should().ContainSingle().Subject;
        delivery.JobId.Should().Be(job.JobId);
        delivery.Receipts.Should().ContainSingle(projected =>
            projected.ReceiptId == receipt.ReceiptId
            && projected.ProviderReference == receipt.ProviderReference);
    }

    private static ReportAccessQueryContext BoundAccess(string actor, string tenantId, string companyId) =>
        new(
            ActorPrincipalId: actor,
            GroupPrincipalIds: [],
            CompanyId: companyId,
            HasGlobalOverride: false,
            TenantId: tenantId,
            RequireBoundScope: true);

    private static async Task<WebApplication> CreateAppAsync(
        ReportPackWorkflowService? workflow = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(workflow ?? new ReportPackWorkflowService());
        builder.Services.AddSingleton<ReportPackDeliveryService>();
        if (workflow is not null)
        {
            builder.Services.AddSingleton(Substitute.For<IGovernanceReportPackRepository>());
        }

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "admin.alpha";
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Admin;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-alpha";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-alpha";
            await next();
        });
        app.MapFundStructureEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }
}
