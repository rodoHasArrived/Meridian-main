using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData("ledger")]
    [InlineData("portfolio")]
    [InlineData("security-instrument")]
    [InlineData("report-line-provenance")]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorers_ShouldReturnStableSharedShape(string explorerId)
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-run", withBreaks: false));

        using var payload = await ReadJsonAsync(app.GetTestClient(), $"/api/workstation/financial-record-explorers/{explorerId}");
        var root = payload.RootElement;

        root.GetProperty("explorerId").GetString().Should().Be(explorerId);
        root.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("sourceState").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("isBlocked").GetBoolean().Should().BeFalse();
        root.GetProperty("savedViews").EnumerateArray().Should().Contain(view =>
            view.GetProperty("isSystem").GetBoolean() &&
            view.GetProperty("isActive").GetBoolean());
        root.GetProperty("summaryItems").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("columns").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("rows").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("recordGraph").GetProperty("nodes").ValueKind.Should().Be(JsonValueKind.Array);

        if (explorerId is "ledger" or "portfolio")
        {
            root.GetProperty("rows").GetArrayLength().Should().BeGreaterThan(0);
            var selected = root.GetProperty("selectedRecord");
            selected.ValueKind.Should().Be(JsonValueKind.Object);
            selected.GetProperty("usedIn").ValueKind.Should().Be(JsonValueKind.Array);
            selected.GetProperty("impacts").ValueKind.Should().Be(JsonValueKind.Array);
            selected.GetProperty("proofActions").ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportLineProvenanceExplorer_ShouldExposeEndToEndDrillThroughChain()
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices);
        var client = app.GetTestClient();
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var delivery = app.Services.GetRequiredService<ReportPackDeliveryService>();
        var line = new ReportPackLineProvenanceDto(
            LineKey: "trial-balance.cash",
            SourceKind: "ledger",
            SourceId: "ledger-entry-1",
            EvidenceId: "ledger-evidence-1",
            RunId: "run-1",
            LedgerEntryId: "ledger-entry-1",
            ReconciliationCaseId: "recon-case-1",
            ReportValue: "100.00",
            SourceSessionId: "provider-session-1",
            ReconciliationRunId: "recon-run-1",
            ProviderEventId: "provider-event-1",
            SecurityMasterId: "security-master-1",
            SecurityDefinitionId: "security-definition-1",
            ReconciliationOutcome: "matched",
            ApprovalId: "approval-1");

        var created = workflow.Create(
            "fund-alpha",
            "acct-cash",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "report.author",
            [line]);
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = workflow.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:report-pack",
            "manifest-board-pack-202603",
            "vault/report-packs/manifest-board-pack-202603.json",
            BuildCompleteReportLineEvidenceLinks());
        var attempt = delivery.Deliver(
            published.ReportId,
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                Actor: "controller",
                DeliveryReference: "board-delivery-202603",
                Note: "Board pack delivered with retained evidence graph.",
                EvidenceLinks:
                [
                    new("delivery-ticket-1", "Delivery ticket", "/evidence/delivery-ticket-1", "delivery")
                ],
                DeliveryMode: ReportPackDeliveryModeDto.SecurePortal),
            "controller");
        workflow.Restate(
            published.ReportId,
            "controller",
            "approver",
            "source-correction",
            "chief-approver",
            published.ReportId,
            [
                new(
                    "trial-balance.cash",
                    "100.00",
                    "105.00",
                    [
                        new("restatement-evidence-1", "Restatement worksheet", "/evidence/restatement-evidence-1", "restatement")
                    ])
            ]);

        var explorer = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/report-line-provenance",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.ExplorerId.Should().Be("report-line-provenance");
        explorer.Rows.Should().ContainSingle();
        explorer.SummaryItems.Select(static item => item.Label).Should().Contain(
            ["Report lines", "Source records", "Reconciliations", "Journals", "Approvals", "Deliveries", "Restatements"]);
        var row = explorer.Rows.Single();
        row.Label.Should().Be("trial-balance.cash");
        row.Status.Should().Be("Restated");
        row.Detail.Fields.Select(static field => field.Label).Should().Contain(
            ["Source record", "Reconciliation", "Journal", "Approval", "Delivery history", "Restatement"]);

        var actions = row.Detail.ProofActions.ToDictionary(static action => action.ActionId, StringComparer.OrdinalIgnoreCase);
        actions.Values.Should().OnlyContain(static action => action.IsEnabled);
        actions["open-source-record"].Href.Should().Contain("/api/workstation/financial-record-explorers/ledger");
        actions["open-reconciliation"].Href.Should().Contain("/api/workstation/reconciliation/runs/recon-run-1");
        actions["open-journal"].Href.Should().Contain("/api/workstation/runs/run-1/ledger/journal");
        actions["open-approval-evidence"].Href.Should().Contain("/api/workstation/evidence/subjects/approval/approval-1/packet");
        actions["open-delivery-history"].Href.Should().Contain($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries");
        actions["open-delivery-evidence-graph"].Href.Should().Contain("/api/workstation/evidence/subjects/report-pack-delivery/");
        actions["open-delivery-evidence-graph"].Href.Should().Contain(Uri.EscapeDataString($"{published.ReportId:D}:{attempt.AttemptId:D}"));
        actions["open-restatement-evidence"].Href.Should().Contain("restatement-evidence-1");
        row.Detail.UsedIn.Select(static relationship => relationship.Label).Should().Contain(
            ["Published report pack", "Delivery history", "Delivery evidence graph", "Restatement record"]);
        row.Detail.Impacts.Select(static relationship => relationship.Label).Should().Contain(
            ["Source record", "Reconciliation", "Journal", "Approval", "Delivery history", "Restatement evidence"]);
        explorer.RecordGraph.Nodes.Select(static node => node.Label).Should().Contain("trial-balance.cash");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerUnknownId_ShouldReturnNotFound()
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices);
        var response = await app.GetTestClient().GetAsync("/api/workstation/financial-record-explorers/not-real");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerSavedViews_ShouldPersistAndReloadForExplorer()
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices);
        var client = app.GetTestClient();

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "Material trial-balance view",
                "Operator-created saved view for ledger review.",
                "Cash",
                [new("account-type", "Account Type", "Asset")]),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.IsSystem.Should().BeFalse();

        using var payload = await ReadJsonAsync(client, "/api/workstation/financial-record-explorers/ledger");
        payload.RootElement.GetProperty("savedViews").EnumerateArray().Should().Contain(view =>
            view.GetProperty("viewId").GetString() == saved.ViewId &&
            view.GetProperty("label").GetString() == "Material trial-balance view" &&
            !view.GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerSavedViews_ShouldPartitionByRequestTenant()
    {
        var savedViewRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "financial-record-explorers",
            Guid.NewGuid().ToString("N"));

        await using var alphaApp = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services, savedViewRoot),
            currentUserCompanyId: "tenant-alpha");
        await using var betaApp = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services, savedViewRoot),
            currentUserCompanyId: "tenant-beta");

        var alphaClient = alphaApp.GetTestClient();
        var betaClient = betaApp.GetTestClient();

        var saveResponse = await alphaClient.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "Alpha-only ledger view",
                "Tenant alpha operator view.",
                "Cash",
                [new("account-type", "Account Type", "Asset")]),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();

        using var alphaPayload = await ReadJsonAsync(alphaClient, "/api/workstation/financial-record-explorers/ledger");
        alphaPayload.RootElement.GetProperty("savedViews").EnumerateArray().Should().Contain(view =>
            view.GetProperty("viewId").GetString() == saved!.ViewId &&
            view.GetProperty("label").GetString() == "Alpha-only ledger view" &&
            !view.GetProperty("isSystem").GetBoolean());

        using var betaPayload = await ReadJsonAsync(betaClient, "/api/workstation/financial-record-explorers/ledger");
        betaPayload.RootElement.GetProperty("savedViews").EnumerateArray().Should().NotContain(view =>
            view.GetProperty("viewId").GetString() == saved!.ViewId ||
            view.GetProperty("label").GetString() == "Alpha-only ledger view");
    }

    private static void RegisterFinancialRecordExplorerTestServices(IServiceCollection services, string? savedViewRoot = null)
    {
        RegisterRunReadServices(services);
        services.AddSingleton<ReportPackWorkflowService>();
        services.AddSingleton<ReportPackDeliveryService>();
        services.AddSingleton<IFinancialRecordExplorerSavedViewStore>(_ =>
            new FileFinancialRecordExplorerSavedViewStore(
                savedViewRoot ?? Path.Combine(Path.GetTempPath(), "meridian-tests", "financial-record-explorers", Guid.NewGuid().ToString("N")),
                NullLogger<FileFinancialRecordExplorerSavedViewStore>.Instance));
        services.AddSingleton<FinancialRecordExplorerReadService>();
    }

    private static IReadOnlyList<ReportPackEvidenceLinkDto> BuildCompleteReportLineEvidenceLinks()
    {
        string[] evidenceIds =
        [
            "ledger-evidence-1",
            "ledger-entry-1",
            "run-1",
            "provider-session-1",
            "recon-case-1",
            "recon-run-1",
            "provider-event-1",
            "security-master-1",
            "security-definition-1",
            "approval-1"
        ];

        return evidenceIds
            .Select(static id => new ReportPackEvidenceLinkDto(id, id, $"/evidence/{id}", "report-line-provenance"))
            .ToArray();
    }
}
