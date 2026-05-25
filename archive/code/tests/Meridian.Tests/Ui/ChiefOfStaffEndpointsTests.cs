using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ChiefOfStaffEndpointsTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task MapWorkstationEndpoints_ChiefOfStaffSessionLifecycle_ShouldCreateRouteApproveAndExportTrace()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "chief-of-staff", Guid.NewGuid().ToString("N"));
        await using var app = await CreateAppAsync(root);
        var client = app.GetTestClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/workstation/chief-of-staff/sessions",
            new ChiefOfStaffStartRequestDto("Reconcile yesterday's trades", OperatorId: "operator"),
            ServerJsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<ChiefOfStaffSessionDto>(ServerJsonOptions);
        created.Should().NotBeNull();
        created!.IntentKind.Should().Be(ChiefOfStaffIntentKindDto.AccountingReconciliationReview);
        created.Actions.Should().NotBeEmpty();

        var listResponse = await client.GetAsync("/api/workstation/chief-of-staff/sessions?limit=10");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<ChiefOfStaffSessionSummaryDto>>(ServerJsonOptions);
        listed.Should().Contain(summary => summary.SessionId == created.SessionId);

        var approveResponse = await client.PostAsJsonAsync(
            $"/api/workstation/chief-of-staff/sessions/{created.SessionId:D}/decisions",
            new ChiefOfStaffDecisionRequestDto(
                ChiefOfStaffDecisionKindDto.Approve,
                Actor: "operator",
                SelectedActionId: created.Actions[0].ActionId,
                Rationale: "Looks good."),
            ServerJsonOptions);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ChiefOfStaffSessionDto>(ServerJsonOptions);
        approved!.Status.Should().Be(ChiefOfStaffSessionStatusDto.Approved);

        var exportResponse = await client.PostAsJsonAsync(
            $"/api/workstation/chief-of-staff/sessions/{created.SessionId:D}/export-trace",
            new ChiefOfStaffTraceExportRequestDto("operator", "audit export"),
            ServerJsonOptions);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<ChiefOfStaffEvidenceExportDto>(ServerJsonOptions);
        export.Should().NotBeNull();
        export!.Manifest.Retained.Should().BeTrue();
        File.Exists(Path.Combine(root, export.Manifest.ManifestPath.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ChiefOfStaffHealth_ShouldReturnRuntimeHealth()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "chief-of-staff", Guid.NewGuid().ToString("N"));
        await using var app = await CreateAppAsync(root);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/chief-of-staff/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var health = await response.Content.ReadFromJsonAsync<ChiefOfStaffRuntimeHealthDto>(ServerJsonOptions);
        health.Should().NotBeNull();
        health!.Status.Should().Be(ChiefOfStaffRuntimeHealthStatusDto.Healthy);
    }

    [Fact]
    public async Task EvidenceEndpoints_ChiefOfStaffSession_ShouldExportManifestForSessionSubject()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "chief-of-staff-evidence", Guid.NewGuid().ToString("N"));
        await using var app = await CreateAppAsync(root);
        var client = app.GetTestClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/workstation/chief-of-staff/sessions",
            new ChiefOfStaffStartRequestDto("Reconcile yesterday's trades", OperatorId: "operator"),
            ServerJsonOptions);
        var session = await createResponse.Content.ReadFromJsonAsync<ChiefOfStaffSessionDto>(ServerJsonOptions);
        session.Should().NotBeNull();
        var subjectId = session!.SessionId.ToString("N");

        var packetResponse = await client.GetAsync($"/api/workstation/evidence/subjects/chief-of-staff-session/{subjectId}/packet");
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        packet!.Nodes.Should().Contain(node => node.Kind == "chief-of-staff-trace");

        var exportResponse = await client.PostAsJsonAsync(
            $"/api/workstation/evidence/subjects/chief-of-staff-session/{subjectId}/export-manifest",
            new EvidencePacketExportRequest("operator", "chief-of-staff trace"),
            ServerJsonOptions);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(ServerJsonOptions);
        export!.Retained.Should().BeTrue();
    }

    private static async Task<WebApplication> CreateAppAsync(string root)
    {
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
        builder.Services.AddSingleton<IEvidenceArtifactStore>(_ => new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance));
        builder.Services.AddSingleton<IOperatorInboxService, InMemoryOperatorInboxService>();
        builder.Services.AddSingleton<IChiefOfStaffRuntimeClient, FakeChiefOfStaffRuntimeClient>();
        builder.Services.AddSingleton<IChiefOfStaffApprovalRouter, FakeChiefOfStaffApprovalRouter>();
        builder.Services.AddSingleton<IChiefOfStaffTraceStore>(_ => new FileChiefOfStaffTraceStore(root, NullLogger<FileChiefOfStaffTraceStore>.Instance));
        builder.Services.AddSingleton<ChiefOfStaffSessionService>();
        builder.Services.AddSingleton<IChiefOfStaffSessionService>(sp => sp.GetRequiredService<ChiefOfStaffSessionService>());
        builder.Services.AddSingleton<EvidenceTemplateRegistry>();
        builder.Services.AddSingleton<EvidencePacketValidationService>();
        builder.Services.AddSingleton<EvidenceSubjectResolver>();
        builder.Services.AddSingleton<EvidenceGraphService>();
        builder.Services.AddSingleton<IEvidenceContributor, ChiefOfStaffEvidenceContributor>();
        builder.Services.Configure<ChiefOfStaffOptions>(_ => { });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ModifySecurityMaster;
            await next().ConfigureAwait(false);
        });
        app.MapWorkstationEndpoints(ServerJsonOptions);
        app.MapEvidenceEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }

    private sealed class FakeChiefOfStaffRuntimeClient : IChiefOfStaffRuntimeClient
    {
        public Task<ChiefOfStaffRuntimeResponseDto> ExecuteIntentAsync(ChiefOfStaffRuntimeRequestDto request, CancellationToken ct = default)
        {
            var action = new ChiefOfStaffActionCandidateDto(
                ActionId: "cos-action-1",
                Label: "Approve reconciliation review",
                TargetWorkflow: "accounting-reconciliation-review",
                TargetRoute: "/accounting",
                TargetPageTag: "FundReconciliation",
                RequiredSignoffRole: "operator",
                ApprovalRequired: true,
                ImpactSummary: "Routes approval through existing governance workflow.",
                EvidencePrerequisites: []);
            var recommendation = new ChiefOfStaffRecommendationDto(
                RecommendationId: "rec-1",
                Title: "Review reconciliation evidence",
                Detail: "Validate breaks and route approval if evidence is complete.",
                Tone: OperatorWorkItemToneDto.Warning,
                Actions: [action]);
            return Task.FromResult(new ChiefOfStaffRuntimeResponseDto(
                IntentKind: request.IntentKind,
                MarkdownSummary: "### Chief of Staff\n\nReconciliation review is ready for operator decision.",
                StructuredPayload: JsonSerializer.SerializeToElement(new { status = "review", intent = request.IntentKind.ToString() }),
                Recommendations: [recommendation],
                Actions: [action],
                EvidenceBundle: null,
                GeneratedAt: DateTimeOffset.UtcNow,
                Warnings: []));
        }

        public Task<ChiefOfStaffRuntimeHealthDto> GetHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new ChiefOfStaffRuntimeHealthDto(
                ChiefOfStaffRuntimeHealthStatusDto.Healthy,
                "Fake runtime healthy.",
                DateTimeOffset.UtcNow));
    }

    private sealed class FakeChiefOfStaffApprovalRouter : IChiefOfStaffApprovalRouter
    {
        public Task<ChiefOfStaffApprovalResultDto> RouteApprovalAsync(ChiefOfStaffApprovalCommandDto command, CancellationToken ct = default)
            => Task.FromResult(new ChiefOfStaffApprovalResultDto(
                Success: true,
                Routed: true,
                Status: "Approved",
                Detail: "Approval routed by fake router.",
                WorkflowReference: "workflow-1"));
    }
}
