using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private static readonly Guid FinancialRecordExplorerAaplSecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("ledger")]
    [InlineData("portfolio")]
    [InlineData("security-instrument")]
    [InlineData("report-line-provenance")]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorers_ShouldReturnStableSharedShape(string explorerId)
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));

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
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerLedger_ShouldExposeCanonicalDimensions()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-ledger-dimensions", withBreaks: false) with
        {
            FundProfileId = "fund-core",
            ParameterSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountScopeId"] = "acct-ops",
                ["entityScopeId"] = "entity-book",
                ["sleeveScopeId"] = "sleeve-alpha"
            }
        });

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/ledger",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.Columns.Select(static column => column.ColumnId).Should().Contain(
            ["fundId", "entityId", "bookId", "accountId"]);
        explorer.Filters.Should().Contain(filter =>
            filter.Label == "Fund" &&
            filter.Value == "fund-core" &&
            filter.Tone == FinancialRecordExplorerTone.Info);
        explorer.Filters.Should().Contain(filter =>
            filter.Label == "Entity" &&
            filter.Value == "entity-book");
        explorer.Filters.Should().Contain(filter =>
            filter.Label == "Sleeve" &&
            filter.Value == "sleeve-alpha");
        explorer.Filters.Should().Contain(filter =>
            filter.Label == "Strategy" &&
            filter.Value == "workflow-paper-strategy");
        explorer.Filters.Should().Contain(filter =>
            filter.Label == "Portfolio" &&
            filter.Value == "workflow-paper-portfolio");
        explorer.Filters.Should().Contain(filter =>
            filter.Label == "Account Scope" &&
            filter.Value == "acct-ops");

        var row = explorer.Rows.Should().ContainSingle(row =>
            row.Cells.Any(cell => cell.ColumnId == "accountName" && cell.DisplayValue == "Cash")).Subject;
        row.Cells.Should().Contain(cell =>
            cell.ColumnId == "fundId" &&
            cell.DisplayValue == "fund-core" &&
            cell.RawValue == "fund-core");
        row.Cells.Should().Contain(cell =>
            cell.ColumnId == "entityId" &&
            cell.DisplayValue == "entity-book" &&
            cell.RawValue == "entity-book");
        row.Cells.Should().Contain(cell =>
            cell.ColumnId == "accountId" &&
            cell.DisplayValue == "acct-ops" &&
            cell.RawValue == "acct-ops");
        row.Detail.Fields.Should().Contain(field =>
            field.Label == "Fund" &&
            field.Value == "fund-core" &&
            field.Tone == FinancialRecordExplorerTone.Info);
        row.Detail.Fields.Should().Contain(field =>
            field.Label == "Entity" &&
            field.Value == "entity-book");
        row.Detail.Fields.Should().Contain(field =>
            field.Label == "Account Scope" &&
            field.Value == "acct-ops");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportLineProvenanceExplorer_ShouldExposeEndToEndDrillThroughChain()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var client = app.GetTestClient();
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var delivery = app.Services.GetRequiredService<ReportPackDeliveryService>();
        var line = new ReportPackLineProvenanceDto(
            LineKey: "trial-balance.cash",
            SourceKind: "position",
            SourceId: "position-aapl",
            EvidenceId: "ledger-evidence-1",
            RunId: "run-1",
            LedgerEntryId: "ledger-entry-1",
            ReconciliationCaseId: "recon-case-1",
            ReportValue: "100.00",
            SourceSessionId: "provider-session-1",
            ReconciliationRunId: "recon-run-1",
            ProviderEventId: "provider-event-position-aapl",
            SecurityMasterId: "11111111-1111-1111-1111-111111111111",
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
            ["Report lines", "Instruments", "Positions / transactions", "Source records", "Reconciliations", "Journals", "Approvals", "Deliveries", "Audit links", "Restatements"]);
        var row = explorer.Rows.Single();
        row.Label.Should().Be("trial-balance.cash");
        row.Status.Should().Be("Restated");
        row.Detail.Fields.Select(static field => field.Label).Should().Contain(
            ["Source record", "Instrument", "Position / transaction", "Reconciliation", "Journal", "Approval", "Evidence and audit links", "Delivery history", "Restatement"]);

        var actions = row.Detail.ProofActions.ToDictionary(static action => action.ActionId, StringComparer.OrdinalIgnoreCase);
        actions.Values.Should().OnlyContain(static action => action.IsEnabled);
        actions["open-source-record"].Href.Should().Contain("/api/workstation/financial-record-explorers/portfolio");
        actions["open-instrument"].Href.Should().Contain("/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111");
        actions["open-position-transaction"].Href.Should().Contain("/api/workstation/evidence/subjects/provider-event/provider-event-position-aapl/packet");
        actions["open-reconciliation"].Href.Should().Contain("/api/workstation/reconciliation/runs/recon-run-1");
        actions["open-journal"].Href.Should().Contain("/api/workstation/runs/run-1/ledger/journal");
        actions["open-approval-evidence"].Href.Should().Contain("/api/workstation/evidence/subjects/approval/approval-1/packet");
        actions["open-evidence-audit-links"].Href.Should().Contain("/api/workstation/evidence/subjects/report-line/ledger-evidence-1/packet");
        actions["open-delivery-history"].Href.Should().Contain($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries");
        actions["open-delivery-evidence-graph"].Href.Should().Contain("/api/workstation/evidence/subjects/report-pack-delivery/");
        actions["open-delivery-evidence-graph"].Href.Should().Contain(Uri.EscapeDataString($"{published.ReportId:D}:{attempt.AttemptId:D}"));
        actions["open-restatement-evidence"].Href.Should().Contain("restatement-evidence-1");
        row.Detail.UsedIn.Select(static relationship => relationship.Label).Should().Contain(
            ["Published report pack", "Delivery history", "Delivery evidence graph", "Restatement record"]);
        row.Detail.Impacts.Select(static relationship => relationship.Label).Should().Contain(
            ["Source record", "Instrument", "Position / transaction", "Reconciliation", "Journal", "Approval", "Delivery history", "Evidence and audit links", "Restatement evidence"]);
        explorer.RecordGraph.Nodes.Select(static node => node.Label).Should().Contain(
            ["Instrument", "Position / transaction", "Reconciliation", "Journal", "trial-balance.cash", "Published report pack", "Evidence and audit links", "Evidence", "Audit event"]);
        explorer.RecordGraph.Edges.Select(static edge => edge.Label).Should().Contain(
            ["feeds", "reconciles", "posts", "reports", "included in", "retains audit", "retains evidence", "audits"]);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityInstrumentExplorer_ShouldExposePassportOperationsAndReportUsage()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();

        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-security-run", withBreaks: false));
        workflow.Create(
            "northwind-income",
            "acct-investments",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "report.author",
            [
                new ReportPackLineProvenanceDto(
                    LineKey: "holdings.aapl.market-value",
                    SourceKind: "position",
                    SourceId: "AAPL",
                    EvidenceId: "position-aapl-evidence",
                    RunId: "financial-record-explorer-security-run",
                    LedgerEntryId: "ledger-aapl-position",
                    ReconciliationCaseId: "recon-aapl",
                    ReportValue: "400.00",
                    SourceSessionId: "provider-session-aapl",
                    ReconciliationRunId: "recon-run-aapl",
                    ProviderEventId: "provider-event-aapl",
                    SecurityMasterId: FinancialRecordExplorerAaplSecurityId.ToString("D"),
                    SecurityDefinitionId: "AAPL",
                    ReconciliationOutcome: "matched",
                    ApprovalId: "approval-aapl")
            ]);

        var explorer = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.ExplorerId.Should().Be("security-instrument");
        explorer.SummaryItems.Select(static item => item.Label).Should().Contain(
            ["Passports", "Operations", "Terms", "Cash Flows", "Reconciliations", "Journal Impacts", "Report Usage", "Evidence", "Audit Events"]);
        explorer.Columns.Select(static column => column.ColumnId).Should().Contain(
            ["trust", "identifierConfidence", "operations", "cashFlow", "ledger", "terms", "reportUsage", "evidence", "auditTrail"]);

        var row = explorer.Rows.Should()
            .ContainSingle(item => item.RecordId == $"security:{FinancialRecordExplorerAaplSecurityId:D}")
            .Subject;
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "trust" && cell.DisplayValue == "Trusted");
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "identifierConfidence" && cell.DisplayValue.Contains("96%", StringComparison.Ordinal));
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "operations" && cell.DisplayValue == "Ready");
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "cashFlow" && cell.DisplayValue == "1 projected");
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "ledger" && cell.DisplayValue == "1 projection");
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "terms" && cell.DisplayValue == "1 term / 1 obligation");
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "reportUsage" && cell.DisplayValue == "holdings.aapl.market-value");
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "evidence" && cell.DisplayValue.Contains("evidence anchor", StringComparison.Ordinal));
        row.Cells.Should().ContainSingle(cell => cell.ColumnId == "auditTrail" && cell.DisplayValue.Contains("audit event", StringComparison.Ordinal));

        row.Detail.Fields.Select(static field => field.Label).Should().Contain(
            [
                "Instrument Identity",
                "Identifier Map",
                "Accounting Classification",
                "Trust Posture",
                "Identifier Confidence",
                "Conflict Posture",
                "Terms / Obligations",
                "AssetOperations Readiness",
                "Projected Cash Flows",
                "Ledger Projection",
                "Ledger / Journal Impact",
                "Reconciliation",
                "Report Usage",
                "Evidence",
                "Audit Trail"
            ]);
        row.Detail.Fields.Should().Contain(field =>
            field.Label == "Evidence" &&
            field.Detail.Contains("position-aapl-evidence", StringComparison.Ordinal));
        row.Detail.Fields.Should().Contain(field =>
            field.Label == "Audit Trail" &&
            field.Detail.Contains("audit event", StringComparison.OrdinalIgnoreCase));
        row.Detail.UsedIn.Select(static relationship => relationship.Label).Should().Contain(
            ["Portfolio position", "Ledger trial balance", "Report-line provenance", "AssetOperations reconciliation"]);
        row.Detail.Impacts.Select(static relationship => relationship.Label).Should().Contain(
            [
                "Position / transaction",
                "Instrument passport",
                "Terms / obligations",
                "AssetOperations readiness",
                "Projected cash flows",
                "Reconciliation",
                "Ledger projection",
                "Journal",
                "Report line",
                "Evidence",
                "Audit event"
            ]);

        var actions = row.Detail.ProofActions.ToDictionary(static action => action.ActionId, StringComparer.OrdinalIgnoreCase);
        actions["open-security-master"].IsEnabled.Should().BeTrue();
        actions["open-instrument-passport"].IsEnabled.Should().BeTrue();
        actions["open-instrument-passport"].Href.Should().Contain($"/api/workstation/security-master/securities/{FinancialRecordExplorerAaplSecurityId:D}/passport");
        actions["open-asset-operations"].IsEnabled.Should().BeTrue();
        actions["open-asset-operations"].Href.Should().Contain($"/api/workstation/assets/{FinancialRecordExplorerAaplSecurityId:D}/operations");
        actions["open-position-transaction"].IsEnabled.Should().BeTrue();
        actions["open-position-transaction"].Href.Should().Contain("/api/workstation/financial-record-explorers/portfolio");
        actions["open-position-transaction"].Href.Should().Contain($"securityId={FinancialRecordExplorerAaplSecurityId:D}");
        actions["open-reconciliation"].IsEnabled.Should().BeTrue();
        actions["open-reconciliation"].Href.Should().Contain("/api/workstation/reconciliation/runs/recon-run-aapl");
        actions["open-journal-impact"].IsEnabled.Should().BeTrue();
        actions["open-journal-impact"].Href.Should().Contain("/api/workstation/runs/financial-record-explorer-security-run/ledger/journal");
        actions["open-journal-impact"].Href.Should().Contain("ledgerEntryId=ledger-aapl-position");
        actions["open-report-line-provenance"].IsEnabled.Should().BeTrue();
        actions["open-report-line-provenance"].Href.Should().Contain("/api/workstation/financial-record-explorers/report-line-provenance");
        actions["open-report-line-provenance"].Href.Should().Contain("lineKey=holdings.aapl.market-value");
        actions["open-evidence"].IsEnabled.Should().BeTrue();
        actions["open-evidence"].Href.Should().Contain("/api/workstation/evidence/subjects/report-line/position-aapl-evidence/packet");
        actions["open-audit-trail"].IsEnabled.Should().BeTrue();
        actions["open-audit-trail"].Href.Should().Contain($"/api/workstation/evidence/subjects/security-instrument/{FinancialRecordExplorerAaplSecurityId:D}/graph");

        explorer.RecordGraph.Nodes.Select(static node => node.Label).Should().Contain(
            ["Apple Inc.", "Position / transaction", "Reconciliation", "Journal", "Report line", "Evidence", "Audit event"]);
        explorer.RecordGraph.Edges.Select(static edge => edge.Label).Should().Contain(
            ["referenced by", "reconciles", "posts", "reports", "retains evidence", "audits"]);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerUnknownId_ShouldReturnNotFound()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var response = await app.GetTestClient().GetAsync("/api/workstation/financial-record-explorers/not-real");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerSavedViews_ShouldPersistAndReloadForExplorer()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var client = app.GetTestClient();

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "Material trial-balance view",
                "Operator-created saved view for ledger review.",
                "Cash",
                [new("account-type", "Account Type", "Asset")],
                ["accountName", "balance"],
                "ledger:selected:cash"),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.IsSystem.Should().BeFalse();
        saved.ColumnIds.Should().Equal(["accountName", "balance"]);
        saved.SelectedRecordId.Should().Be("ledger:selected:cash");

        using var payload = await ReadJsonAsync(client, "/api/workstation/financial-record-explorers/ledger");
        payload.RootElement.GetProperty("savedViews").EnumerateArray().Should().Contain(view =>
            view.GetProperty("viewId").GetString() == saved.ViewId &&
            view.GetProperty("label").GetString() == "Material trial-balance view" &&
            !view.GetProperty("isSystem").GetBoolean() &&
            view.GetProperty("columnIds").EnumerateArray().Select(column => column.GetString()).SequenceEqual(new[] { "accountName", "balance" }) &&
            view.GetProperty("selectedRecordId").GetString() == "ledger:selected:cash");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorer_ShouldApplySavedViewOnServer()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-saved-view-query", withBreaks: false));

        var full = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/ledger",
            ServerJsonOptions);
        full.Should().NotBeNull();
        full!.Rows.Should().HaveCountGreaterThan(1);

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "Cash-only ledger view",
                "Server-applied saved view for cash ledger rows.",
                "Cash",
                [new("account-type", "Account Type", "Asset")],
                ["accountName", "balance"]),
            ServerJsonOptions);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();

        var scoped = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            $"/api/workstation/financial-record-explorers/ledger?viewId={Uri.EscapeDataString(saved!.ViewId)}",
            ServerJsonOptions);

        scoped.Should().NotBeNull();
        scoped!.Rows.Should().NotBeEmpty();
        scoped.Rows.Should().OnlyContain(row =>
            row.Cells.Any(cell => cell.ColumnId == "accountName" && cell.DisplayValue.Contains("Cash", StringComparison.OrdinalIgnoreCase)));
        scoped.Rows.Should().HaveCountLessThan(full.Rows.Count);
        scoped.SelectedRecord!.RecordId.Should().Be(scoped.Rows[0].RecordId);
        scoped.SavedViews.Single(view => view.ViewId == saved.ViewId).IsActive.Should().BeTrue();
        scoped.SummaryItems.Should().Contain(item =>
            item.Label == "Visible records" &&
            item.Value == scoped.Rows.Count.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorer_ShouldRestoreSavedViewSelectedRecord()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-selected-record-view", withBreaks: false));

        var full = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/ledger",
            ServerJsonOptions);
        full.Should().NotBeNull();
        full!.Rows.Should().HaveCountGreaterThan(1);
        var targetRow = full.Rows.Last();

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "Selected ledger row",
                "Server-applied saved view with selected proof row.",
                "",
                [],
                ["accountName", "balance"],
                targetRow.RecordId),
            ServerJsonOptions);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.SelectedRecordId.Should().Be(targetRow.RecordId);

        var scoped = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            $"/api/workstation/financial-record-explorers/ledger?viewId={Uri.EscapeDataString(saved.ViewId)}",
            ServerJsonOptions);

        scoped.Should().NotBeNull();
        scoped!.Rows.Should().Contain(row => row.RecordId == targetRow.RecordId);
        scoped.SelectedRecord!.RecordId.Should().Be(targetRow.RecordId);
        scoped.SavedViews.Single(view => view.ViewId == saved.ViewId).SelectedRecordId.Should().Be(targetRow.RecordId);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorer_ShouldApplyDimensionFilterOnServer()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-dimension-query", withBreaks: false) with
        {
            FundProfileId = "fund-core",
            ParameterSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountScopeId"] = "acct-ops",
                ["entityScopeId"] = "entity-book",
                ["ledgerBookId"] = "book-gaap"
            }
        });

        var matching = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/ledger?filter=fundId:fund-core&filter=bookId:book-gaap",
            ServerJsonOptions);

        matching.Should().NotBeNull();
        matching!.Rows.Should().NotBeEmpty();
        matching.Rows.Should().OnlyContain(row =>
            row.Cells.Any(cell => cell.ColumnId == "fundId" && cell.RawValue == "fund-core") &&
            row.Cells.Any(cell => cell.ColumnId == "bookId" && cell.RawValue == "book-gaap"));

        var missing = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/ledger?filter=fundId:fund-missing",
            ServerJsonOptions);

        missing.Should().NotBeNull();
        missing!.Rows.Should().BeEmpty();
        missing.SelectedRecord.Should().BeNull();
        missing.SummaryItems.Should().Contain(item =>
            item.Label == "Visible records" &&
            item.Value == "0" &&
            item.Tone == FinancialRecordExplorerTone.Warning);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerSavedViews_ShouldNormalizeNullableFiltersAndColumns()
    {
        await using var app = await CreateAppAsync(services => RegisterFinancialRecordExplorerTestServices(services));
        var client = app.GetTestClient();

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "  Normalized ledger view  ",
                "  trims saved view inputs  ",
                "  Cash  ",
                [null!, new(" account-type ", " Account Type ", " Asset ", " ")],
                [null!, " balance ", "BALANCE", " "]),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.Label.Should().Be("Normalized ledger view");
        saved.Description.Should().Be("trims saved view inputs");
        saved.SearchText.Should().Be("Cash");
        saved.Filters.Should().ContainSingle();
        saved.Filters[0].FilterId.Should().Be("account-type");
        saved.Filters[0].Label.Should().Be("Account Type");
        saved.Filters[0].Value.Should().Be("Asset");
        saved.Filters[0].Operator.Should().Be("equals");
        saved.ColumnIds.Should().Equal(["balance"]);
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

    private static void RegisterFinancialRecordExplorerTestServices(IServiceCollection services)
        => RegisterFinancialRecordExplorerTestServices(
            services,
            Path.Combine(Path.GetTempPath(), "meridian-tests", "financial-record-explorers", Guid.NewGuid().ToString("N")));

    private static void RegisterFinancialRecordExplorerTestServices(IServiceCollection services, string savedViewRoot)
    {
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: FinancialRecordExplorerAaplSecurityId,
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL",
            SubType: "CommonShare",
            MatchedIdentifierKind: "Ticker",
            MatchedIdentifierValue: "AAPL",
            MatchedProvider: "alpaca",
            ResolutionReason: "Resolved through retained Security Master test fixture.",
            LookupSource: "Security Master"));

        services.AddSingleton<ISecurityReferenceLookup>(lookup);
        RegisterRunReadServices(services);
        services.AddSingleton<ReportPackWorkflowService>();
        services.AddSingleton<ReportPackDeliveryService>();
        services.AddSingleton<ISecurityMasterWorkbenchQueryService>(
            new FinancialRecordExplorerSecurityMasterWorkbenchQueryService(FinancialRecordExplorerAaplSecurityId));
        services.AddSingleton<IAssetOperationsQueryService>(
            new FinancialRecordExplorerAssetOperationsQueryService(FinancialRecordExplorerAaplSecurityId));
        services.AddSingleton<IFinancialRecordExplorerSavedViewStore>(_ =>
            new FileFinancialRecordExplorerSavedViewStore(
                savedViewRoot,
                NullLogger<FileFinancialRecordExplorerSavedViewStore>.Instance));
        services.AddSingleton<FinancialRecordExplorerReadService>();
    }

    private static IReadOnlyList<ReportPackEvidenceLinkDto> BuildCompleteReportLineEvidenceLinks()
    {
        string[] evidenceIds =
        [
            "ledger-evidence-1",
            "position-aapl",
            "ledger-entry-1",
            "run-1",
            "provider-session-1",
            "recon-case-1",
            "recon-run-1",
            "provider-event-position-aapl",
            "11111111-1111-1111-1111-111111111111",
            "security-definition-1",
            "approval-1"
        ];

        return evidenceIds
            .Select(static id => new ReportPackEvidenceLinkDto(id, id, $"/evidence/{id}", "report-line-provenance"))
            .ToArray();
    }

    private sealed class FinancialRecordExplorerSecurityMasterWorkbenchQueryService(Guid securityId) : ISecurityMasterWorkbenchQueryService
    {
        private readonly DateTimeOffset _now = new(2026, 3, 22, 15, 0, 0, TimeSpan.Zero);

        public Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
            Guid requestedSecurityId,
            string? fundProfileId,
            CancellationToken ct = default)
            => Task.FromResult<SecurityMasterTrustSnapshotDto?>(null);

        public Task<InstrumentPassportDto?> GetInstrumentPassportAsync(
            Guid requestedSecurityId,
            string? fundProfileId,
            CancellationToken ct = default)
            => Task.FromResult<InstrumentPassportDto?>(requestedSecurityId == securityId ? CreatePassport() : null);

        public Task<SecurityMasterOperatingModelDto?> GetOperatingModelAsync(
            Guid requestedSecurityId,
            string? fundProfileId,
            CancellationToken ct = default)
            => Task.FromResult<SecurityMasterOperatingModelDto?>(
                requestedSecurityId == securityId ? CreatePassport().OperatingModel : null);

        public Task<BulkResolveSecurityMasterConflictsResult> BulkResolveConflictsAsync(
            BulkResolveSecurityMasterConflictsRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Conflict resolution is not used by Financial Record Explorer tests.");

        private InstrumentPassportDto CreatePassport()
        {
            var identifier = new SecurityIdentifierDto(
                SecurityIdentifierKind.Ticker,
                "AAPL",
                true,
                _now.AddYears(-1),
                Provider: "alpaca");
            var providerMapping = new SecurityMasterProviderSymbolMappingDto(
                MappingSource: "alpaca",
                MappingKind: "Ticker",
                Value: "AAPL",
                NormalizedValue: "AAPL",
                Provider: "alpaca",
                NormalizedProvider: "ALPACA",
                IsPrimary: true,
                IsEnabled: true,
                ValidFrom: _now.AddYears(-1),
                ValidTo: null,
                IsActive: true);
            var identifierSummary = new SecurityMasterIdentifierSummaryDto(
                PrimaryIdentifierKind: "Ticker",
                PrimaryIdentifierValue: "AAPL",
                ActiveIdentifierCount: 1,
                ActiveAliasCount: 0,
                ProviderMappingCount: 1,
                DistinctProviderCount: 1,
                HasPrimaryIdentifier: true,
                HasProviderMappings: true,
                Summary: "Primary ticker AAPL is mapped to active provider evidence.",
                ProviderMappings: [providerMapping]);
            var trustPosture = new SecurityMasterTrustPostureDto(
                SecurityMasterTrustTone.Trusted,
                TrustScore: 96,
                Summary: "Passport trusted with provider-confirmed identifier evidence.",
                GoldenCopySource: "Security Master",
                GoldenCopyRule: "provider-primary",
                TradingParametersStatus: "Complete",
                CorporateActionReadiness: "Ready",
                HasOpenConflicts: false,
                OpenConflictCount: 0,
                TradingParametersComplete: true,
                HasUpcomingCorporateActions: false,
                CorporateActionsTrusted: true);
            var passport = new InstrumentPassportDto(
                securityId,
                new SecurityIdentityDrillInDto(
                    securityId,
                    "Apple Inc.",
                    "Equity",
                    SecurityStatusDto.Active,
                    Version: 1,
                    EffectiveFrom: _now.AddYears(-1),
                    EffectiveTo: null,
                    Identifiers: [identifier],
                    Aliases: []),
                new SecurityMasterEconomicDefinitionDrillInDto(
                    securityId,
                    AssetClass: "Equity",
                    Currency: "USD",
                    Version: 1,
                    EffectiveFrom: _now.AddYears(-1),
                    EffectiveTo: null,
                    AssetFamily: "PublicEquity",
                    SubType: "CommonShare",
                    IssuerType: "Corporate",
                    RiskCountry: "US",
                    WinningSourceSystem: "security-master",
                    WinningSourceRecordId: "AAPL",
                    WinningSourceAsOf: _now,
                    WinningSourceUpdatedBy: "steward",
                    WinningSourceReason: "Golden-copy provider mapping."),
                identifierSummary,
                [providerMapping],
                LifecycleEvents: [],
                CorporateActions: [],
                Pricing: new InstrumentPassportPricingDto(
                    Status: "Ready",
                    Summary: "Trading parameters retained.",
                    TradingParameters: null,
                    LotSize: 1m,
                    TickSize: 0.01m,
                    ContractMultiplier: 1m,
                    TradingHoursUtc: "14:30-21:00",
                    CircuitBreakerThresholdPct: null),
                Usage: new SecurityMasterDownstreamImpactDto(
                    FundProfileId: "northwind-income",
                    IsScoped: true,
                    Severity: SecurityMasterImpactSeverity.Low,
                    Summary: "Used by portfolio, ledger, reconciliation, and report provenance.",
                    PortfolioExposureSummary: "1 portfolio position",
                    LedgerExposureSummary: "1 ledger line",
                    ReconciliationExposureSummary: "1 reconciliation result",
                    ReportPackExposureSummary: "1 report line",
                    MatchedRunCount: 1,
                    PortfolioExposureCount: 1,
                    LedgerExposureCount: 1,
                    ReconciliationExposureCount: 1,
                    ReportPackExposureCount: 1,
                    Links: []),
                TrustPosture: trustPosture,
                RetrievedAtUtc: _now)
            {
                ProviderConfidence =
                [
                    new InstrumentPassportProviderConfidenceDto(
                        Provider: "alpaca",
                        ProviderSource: "alpaca",
                        MappingKind: "Ticker",
                        Symbol: "AAPL",
                        NormalizedSymbol: "AAPL",
                        IsPrimary: true,
                        IsActive: true,
                        FreshnessAsOf: _now.AddMinutes(-5),
                        FreshnessMinutes: 5,
                        ConfidenceScore: 0.96m,
                        ConfidenceReason: "Fresh provider mapping agrees with the golden-copy ticker.",
                        IdentifierConflictIds: [],
                        IdentifierConflictSummaries: [],
                        OverrideHistory: [])
                ]
            };

            return passport;
        }
    }

    private sealed class FinancialRecordExplorerAssetOperationsQueryService(Guid securityId) : IAssetOperationsQueryService
    {
        private readonly DateTimeOffset _now = new(2026, 3, 22, 15, 0, 0, TimeSpan.Zero);

        public Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid requestedSecurityId, CancellationToken ct = default)
            => Task.FromResult<AssetOperationsDetailDto?>(requestedSecurityId == securityId ? CreateDetail() : null);

        public Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid requestedSecurityId, CancellationToken ct = default)
            => Task.FromResult<AssetOperationsReadinessDto?>(requestedSecurityId == securityId ? CreateReadiness() : null);

        private AssetOperationsDetailDto CreateDetail()
        {
            var readiness = CreateReadiness();
            var projectionRunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var reconciliationRunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            return new AssetOperationsDetailDto(
                new AssetOperationSubjectDto(
                    securityId,
                    AssetClass: "Equity",
                    DisplayName: "Apple Inc.",
                    PrimaryIdentifier: "AAPL",
                    OperationalProfile: ["cash-flow", "ledger-projection"]),
                TermsHistory:
                [
                    new AssetTermsVersionDto(
                        TermsVersionId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        SecurityId: securityId,
                        VersionNumber: 1,
                        TermsHash: "sha256:aapl-terms",
                        EffectiveDate: new DateOnly(2026, 3, 22),
                        RecordedAt: _now,
                        SourceDomain: "security-master",
                        SourceEntityId: securityId.ToString("D"),
                        Summary: "Common equity terms retained.")
                ],
                LifecycleEvents: [],
                CashFlowProjectionRuns:
                [
                    new AssetCashFlowProjectionRunDto(
                        projectionRunId,
                        securityId,
                        ProjectionAsOf: new DateOnly(2026, 3, 22),
                        EngineVersion: "test-v1",
                        Status: "Generated",
                        GeneratedAt: _now,
                        SourceDomain: "asset-operations",
                        SourceEntityId: "projection-aapl")
                ],
                ProjectedCashFlows:
                [
                    new AssetProjectedCashFlowDto(
                        ProjectedCashFlowId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                        ProjectionRunId: projectionRunId,
                        SecurityId: securityId,
                        SequenceNumber: 1,
                        FlowType: "Dividend",
                        DueDate: new DateOnly(2026, 4, 15),
                        Amount: 1.20m,
                        Currency: "USD",
                        Status: "Projected")
                ],
                ActualActivity: [],
                ReconciliationRuns:
                [
                    new AssetReconciliationRunDto(
                        reconciliationRunId,
                        securityId,
                        ProjectionRunId: projectionRunId,
                        Status: "Matched",
                        RequestedAt: _now.AddMinutes(-20),
                        CompletedAt: _now.AddMinutes(-10),
                        SourceDomain: "asset-operations",
                        SourceEntityId: "recon-aapl")
                ],
                ReconciliationResults:
                [
                    new AssetReconciliationResultDto(
                        ReconciliationResultId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
                        ReconciliationRunId: reconciliationRunId,
                        SecurityId: securityId,
                        MatchStatus: "Matched",
                        ExpectedAmount: 1.20m,
                        ActualAmount: 1.20m,
                        VarianceAmount: 0m,
                        ExpectedDate: new DateOnly(2026, 4, 15),
                        ActualDate: new DateOnly(2026, 4, 15),
                        SourceDomain: "asset-operations",
                        SourceEntityId: "recon-result-aapl",
                        EvidenceLink: "/evidence/recon-result-aapl")
                ],
                LedgerProjections:
                [
                    new AssetLedgerProjectionDto(
                        LedgerProjectionId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                        SecurityId: securityId,
                        ProjectionType: "DividendIncome",
                        AccountingDate: new DateOnly(2026, 4, 15),
                        LedgerBasis: "GAAP",
                        Status: "Ready",
                        DebitAmount: 1.20m,
                        CreditAmount: null,
                        Currency: "USD",
                        SourceDomain: "asset-operations",
                        SourceEntityId: "ledger-projection-aapl",
                        LedgerReferenceId: "journal-preview-aapl")
                ],
                Readiness: readiness,
                WorkflowAudit:
                [
                    new AssetLifecycleEventDto(
                        LifecycleEventId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
                        SecurityId: securityId,
                        EventType: "AuditEvent",
                        LifecycleState: "Approved",
                        EffectiveDate: new DateOnly(2026, 3, 22),
                        RecordedAt: _now.AddMinutes(-2),
                        SourceDomain: "asset-operations",
                        SourceEntityId: "audit-aapl",
                        Summary: "Projection, reconciliation, and journal proof chain reviewed.")
                ]);
        }

        private AssetOperationsReadinessDto CreateReadiness()
            => new(
                securityId,
                Status: "Ready",
                Capabilities: ["cash-flow", "ledger-projection", "reconciliation"],
                ReadyCapabilities: ["cash-flow", "ledger-projection", "reconciliation"],
                MissingCapabilities: [],
                Warnings: [],
                EvaluatedAt: _now,
                SourceDomain: "asset-operations",
                SourceEntityId: "aapl-readiness");
    }
}
