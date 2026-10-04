using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Identity.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    // W9-GOV-008: each explorer is authorized by the family its own builder reads -- the three
    // ledger and portfolio by the strategy permissions alone, security-instrument by those or the
    // Security Master pair, and report-line provenance by the reporting permissions. These tests cover
    // all four and assert payload shape, so the caller holds what an operator working the whole surface
    // would rather than only the default ModifySecurityMaster.
    private const UserPermission ExplorerOperatorPermissions =
        UserPermission.ModifySecurityMaster |
        UserPermission.ViewSecurityMaster |
        UserPermission.ViewDirectLending |
        UserPermission.ViewReporting |
        UserPermission.ViewStrategies;

    private static readonly Guid FinancialRecordExplorerAaplSecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FinancialRecordExplorerLedgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly Guid FinancialRecordExplorerPositionId = Guid.Parse("11111111-1111-1111-1111-111111111113");
    private static readonly Guid FinancialRecordExplorerEventId = Guid.Parse("11111111-1111-1111-1111-111111111114");
    private static readonly Guid FinancialRecordExplorerPeriodId = Guid.Parse("11111111-1111-1111-1111-111111111121");
    private static readonly Guid FinancialRecordExplorerJournalId = Guid.Parse("11111111-1111-1111-1111-111111111120");
    private static readonly Guid FinancialRecordExplorerDebitLineId = Guid.Parse("11111111-1111-1111-1111-111111111123");
    private static readonly Guid FinancialRecordExplorerCreditLineId = Guid.Parse("11111111-1111-1111-1111-111111111124");

    [Theory]
    [InlineData("ledger")]
    [InlineData("portfolio")]
    [InlineData("security-instrument")]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorers_ShouldNotServeAnotherTenantsRun(string explorerId)
    {
        // A run's detail is the explorer's entire source -- the trial balance, the positions, the
        // security references it renders. Selecting the newest qualifying run globally served one
        // tenant's book to another whenever the other owned the newest run, and the tenant id reaching
        // only saved-view persistence made that invisible from the call site.
        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterFinancialRecordExplorerTestServices(services);

                // Registered before the fixture's own current-scope registry, which resolves every
                // fund to the calling tenant and so cannot express a foreign owner at all.
                services.AddSingleton<IFundProfileTenancyRegistry>(
                    new ForeignOwnerFundProfileTenancyRegistry("northwind-income", "another-tenant"));
            },
            currentUserPermissions: ExplorerOperatorPermissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-foreign-run", withBreaks: false));

        using var response = await app.GetTestClient().GetAsync(
            $"/api/workstation/financial-record-explorers/{explorerId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var explorer = await response.Content.ReadFromJsonAsync<FinancialRecordExplorerDto>(ServerJsonOptions);
        explorer.Should().NotBeNull();
        explorer!.Rows.Should().BeEmpty(
            "the only qualifying run belongs to another tenant, so this tenant has no source-backed projection");
        explorer.SourceState.Should().Contain(
            "No source-backed",
            "an empty explorer must say it has no source rather than imply the tenant's book is empty");
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("portfolio")]
    [InlineData("security-instrument")]
    [InlineData("report-line-provenance")]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorers_ShouldReturnStableSharedShape(string explorerId)
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);

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
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);

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

    /// <summary>
    /// The ledger explorer answered for whichever run was newest, whatever run the screen was
    /// showing, so an operator reading an older run's trial balance saw that run's rows under the
    /// newest run's header, proof links and scope. It now answers for the run it is asked for.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerLedger_ShouldAnswerForTheRequestedRun()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("explorer-ledger-run-older", withBreaks: false) with
        {
            FundProfileId = "fund-core"
        });
        await store.RecordRunAsync(BuildActivePaperRun("explorer-ledger-run-newer", withBreaks: false) with
        {
            FundProfileId = "fund-core"
        });

        var client = app.GetTestClient();

        foreach (var runId in new[] { "explorer-ledger-run-older", "explorer-ledger-run-newer" })
        {
            var explorer = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
                $"/api/workstation/financial-record-explorers/ledger?filter={Uri.EscapeDataString($"run:{runId}")}",
                ServerJsonOptions);

            explorer.Should().NotBeNull();
            explorer!.SourceState.Should().Contain(runId);
            explorer.Filters.Should().Contain(filter => filter.FilterId == "run" && filter.Value == runId);
            explorer.Rows.Should().OnlyContain(row => row.SourceRunId == runId);
            explorer.ProofActions.Should().Contain(action => action.Href.Contains(runId, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The screen's run picker read run ids out of the rows, and the explorer composes its rows
    /// from exactly one run — so it offered one option and every older run was unreachable. Each
    /// run the caller may read is published as a system view carrying its own run filter.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerLedger_ShouldPublishEveryReadableRunAsAView()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("explorer-ledger-view-a", withBreaks: false) with
        {
            FundProfileId = "fund-core"
        });
        await store.RecordRunAsync(BuildActivePaperRun("explorer-ledger-view-b", withBreaks: false) with
        {
            FundProfileId = "fund-core"
        });

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/ledger",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        var runViews = explorer!.SavedViews
            .Select(view => view.Filters.FirstOrDefault(filter => filter.FilterId == "run")?.Value)
            .Where(runId => !string.IsNullOrEmpty(runId))
            .ToList();

        runViews.Should().Contain(["explorer-ledger-view-a", "explorer-ledger-view-b"]);
        explorer.SavedViews.Should().ContainSingle(view =>
            view.IsActive && view.Filters.Any(filter => filter.FilterId == "run"));
    }

    /// <summary>
    /// The picker is bounded so it stays a picker, but a run named explicitly must still resolve
    /// past that bound — otherwise the explorer answers for a newer run while the screen's own
    /// trial-balance and journal requests use the one the URL asked for, recombining evidence
    /// from two different runs.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerLedger_ShouldResolveARunOutsideThePickerBound()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        // One past the 50-run candidate bound, so the requested run cannot be reached through the
        // candidate list alone. Every fixture run shares one StartedAt, so the ordering falls
        // through to the run id ASCENDING -- making "-050" the run outside the bound and "-000"
        // the first candidate inside it. Requesting "-000" here tests nothing.
        for (var index = 0; index <= 50; index++)
        {
            await store.RecordRunAsync(BuildActivePaperRun($"explorer-ledger-bounded-{index:D3}", withBreaks: false) with
            {
                FundProfileId = "fund-core"
            });
        }

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            $"/api/workstation/financial-record-explorers/ledger?filter={Uri.EscapeDataString("run:explorer-ledger-bounded-050")}",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.SavedViews.Should().HaveCountLessThanOrEqualTo(50, "the picker stays bounded");
        explorer.SourceState.Should().Contain("explorer-ledger-bounded-050");
        explorer.Rows.Should().OnlyContain(row => row.SourceRunId == "explorer-ledger-bounded-050");

        // And the run being displayed owns the active view. Without it the client fell back to the
        // first candidate -- a newer run -- so the picker and any link copied from it identified a
        // different run than the rows on screen.
        var activeRunId = explorer.SavedViews
            .Where(view => view.IsActive)
            .SelectMany(view => view.Filters)
            .Where(filter => filter.FilterId == "run")
            .Select(filter => filter.Value)
            .ToList();
        activeRunId.Should().ContainSingle().Which.Should().Be("explorer-ledger-bounded-050");
    }

    /// <summary>
    /// Resolving a run past the picker's bound read the tenant's entire run history a second time
    /// -- and reloaded the promotion lookup with it -- so the older the deep link, the more the
    /// request cost, growing with retained history. One scan answers both questions.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerLedger_ForARunOutsideTheBound_ShouldScanRunHistoryOnce()
    {
        RunHistoryScanCountingRepository? counter = null;
        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterFinancialRecordExplorerTestServices(services);
                // Registered after the fixture's own store so this decorator is the one resolved,
                // counting the full-history reads the explorer makes while forwarding to it.
                services.AddSingleton<IStrategyRepository>(_ =>
                    counter = new RunHistoryScanCountingRepository(new StrategyRunStore()));
            },
            currentUserPermissions: ExplorerOperatorPermissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        for (var index = 0; index <= 50; index++)
        {
            await store.RecordRunAsync(BuildActivePaperRun($"explorer-ledger-scan-{index:D3}", withBreaks: false) with
            {
                FundProfileId = "fund-core"
            });
        }

        counter.Should().NotBeNull();
        counter!.Reset();

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            $"/api/workstation/financial-record-explorers/ledger?filter={Uri.EscapeDataString("run:explorer-ledger-scan-050")}",
            ServerJsonOptions);

        // The run is genuinely outside the bound, so this is the case that used to scan twice.
        explorer.Should().NotBeNull();
        explorer!.SourceState.Should().Contain("explorer-ledger-scan-050");
        counter.FullHistoryScans.Should().Be(
            1,
            "the candidate scan already holds the requested run, so resolving it must not re-read the history");
    }

    /// <summary>
    /// A run id can arrive from a bookmark long after the run was pruned, or name another tenant's
    /// run. Neither may leave the screen with nothing where a readable ledger exists, and neither
    /// may serve the named run: the explorer resolves against the caller's own runs and names the
    /// one it resolved.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerLedger_ForAnUnreachableRun_ShouldFallBackAndSaySo()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("explorer-ledger-reachable", withBreaks: false) with
        {
            FundProfileId = "fund-core"
        });

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            $"/api/workstation/financial-record-explorers/ledger?filter={Uri.EscapeDataString("run:no-such-run")}",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.SourceState.Should().Contain("explorer-ledger-reachable");
        explorer.SourceState.Should().NotContain("no-such-run");
        explorer.Rows.Should().OnlyContain(row => row.SourceRunId == "explorer-ledger-reachable");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportLineProvenanceExplorer_ShouldExposeEndToEndDrillThroughChain()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
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
            [line],
            accessContext: BoundReportAccessContext());
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
            ["Reported lines", "Instruments", "Positions / transactions", "Source records", "Reconciliations", "Ledger references", "Approvals", "Deliveries", "Audit links", "Restatements"]);
        var row = explorer.Rows.Single();
        row.Label.Should().Be("trial-balance.cash");
        row.Status.Should().Be("Restated");
        row.Detail.Fields.Select(static field => field.Label).Should().Contain(
            ["Source record", "Instrument", "Position / transaction", "Reconciliation", "Ledger provenance", "Approval", "Evidence and audit links", "Delivery history", "Restatement"]);

        var actions = row.Detail.ProofActions.ToDictionary(static action => action.ActionId, StringComparer.OrdinalIgnoreCase);
        actions.Values.Should().OnlyContain(static action => action.IsEnabled);
        actions["open-source-record"].Href.Should().Contain("/api/workstation/financial-record-explorers/portfolio");
        actions["open-instrument"].Href.Should().Contain("/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111");
        actions["open-position-transaction"].Href.Should().Contain("/api/workstation/evidence/subjects/provider-event/provider-event-position-aapl/packet");
        actions["open-reconciliation"].Href.Should().Contain("/api/workstation/reconciliation/runs/recon-run-1");
        actions.Should().NotContainKey("open-journal");
        actions["open-approval-evidence"].Href.Should().Contain("/api/workstation/evidence/subjects/approval/approval-1/packet");
        actions["open-evidence-audit-links"].Href.Should().Contain("/api/workstation/evidence/subjects/report-line/ledger-evidence-1/packet");
        actions["open-delivery-history"].Href.Should().Contain(
            "/api/workstation/evidence/subjects/report-pack-delivery/");
        row.Cells.Single(cell => cell.ColumnId == "report").LinkHref.Should().Be(
            $"/api/fund-structure/report-packs/{published.ReportId:D}");
        explorer.ProofActions.Should().Contain(action =>
            action.ActionId == "open-reporting-runs" &&
            action.Href == "/api/fund-structure/reporting/runs");
        row.Detail.ProofActions.Select(static action => action.Href).Should().NotContain(href =>
            href.Contains("/api/fund-structure/reporting/packs/", StringComparison.Ordinal));
        actions["open-delivery-evidence-graph"].Href.Should().Contain("/api/workstation/evidence/subjects/report-pack-delivery/");
        actions["open-delivery-evidence-graph"].Href.Should().Contain(Uri.EscapeDataString($"{published.ReportId:D}:{attempt.AttemptId:D}"));
        actions["open-restatement-evidence"].Href.Should().Contain("restatement-evidence-1");
        row.Detail.UsedIn.Select(static relationship => relationship.Label).Should().Contain(
            ["Restated report pack (read-only)", "Delivery history", "Delivery evidence graph", "Restatement record"]);
        row.Detail.Impacts.Select(static relationship => relationship.Label).Should().Contain(
            ["Source record", "Instrument", "Position / transaction", "Reconciliation", "Ledger provenance reference", "Approval", "Delivery history", "Evidence and audit links", "Restatement evidence"]);
        explorer.RecordGraph.Nodes.Select(static node => node.Label).Should().Contain(
            ["Instrument", "Position / transaction", "Reconciliation", "trial-balance.cash", "Restated report pack (read-only)", "Evidence and audit links", "Evidence", "Audit event"]);
        explorer.RecordGraph.Nodes.Should().NotContain(static node => node.NodeType == "journal");
        explorer.RecordGraph.Edges.Select(static edge => edge.Label).Should().Contain(
            ["feeds", "reconciles", "reported", "included in", "retains audit", "retains evidence", "audits"]);
        row.Detail.ProofActions.Select(static action => action.Href).Should().NotContain(static href =>
            href.Contains("/ledger/journal", StringComparison.OrdinalIgnoreCase) ||
            href.Contains("ledgerEntryId=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityInstrumentExplorer_ShouldExposePassportOperationsAndReportUsage()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();

        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-security-run", withBreaks: false));
        var line = new ReportPackLineProvenanceDto(
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
            ApprovalId: "approval-aapl");
        var created = workflow.Create(
            "northwind-income",
            "acct-investments",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "report.author",
            [line],
            accessContext: BoundReportAccessContext());
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:aapl-report-pack",
            "manifest-aapl-202603",
            "vault/report-packs/manifest-aapl-202603.json",
            BuildCompleteReportLineEvidenceLinks(line));

        var explorer = await client.GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.ExplorerId.Should().Be("security-instrument");
        explorer.SummaryItems.Select(static item => item.Label).Should().Contain(
            ["Passports", "Operations", "Terms", "Cash Flows", "Reconciliations", "Accounting Projections", "Posted Journals", "Reported Usage", "Evidence", "Audit Events"]);
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
                "Accounting Projection",
                "Projected Accounting Effect",
                "Reconciliation",
                "Corporate Action Evidence",
                "Role / Position",
                "Accounting Projection",
                "Posting Candidate",
                "Approved",
                "Posted Journal",
                "Journal Totals",
                "Journal Currency",
                "Balanced Journal Lines",
                "Reported",
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
            ["Portfolio position", "Ledger trial balance", "Report-line provenance", "AssetOperations reconciliation", "Accounting projection proof", "Instrument-to-posted-journal proof"]);
        row.Detail.Impacts.Select(static relationship => relationship.Label).Should().Contain(
            [
                "Position / transaction",
                "Instrument passport",
                "Terms / obligations",
                "AssetOperations readiness",
                "Projected cash flows",
                "Reconciliation",
                "Accounting projection",
                "Projected accounting effect",
                "Posted Journal",
                "Corporate action evidence",
                "Role / position",
                "Accounting projection",
                "Posting candidate",
                "Independent approval",
                "Reported line",
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
        actions["open-journal-impact"].Href.Should().Contain("ledgerEntryId=11111111-1111-1111-1111-111111111120");
        actions["open-report-line-provenance"].IsEnabled.Should().BeTrue();
        actions["open-report-line-provenance"].Href.Should().Contain("/api/workstation/financial-record-explorers/report-line-provenance");
        actions["open-report-line-provenance"].Href.Should().Contain("lineKey=holdings.aapl.market-value");
        actions["open-evidence"].IsEnabled.Should().BeTrue();
        actions["open-evidence"].Href.Should().Contain("/api/workstation/evidence/subjects/report-line/position-aapl-evidence/packet");
        actions["open-audit-trail"].IsEnabled.Should().BeTrue();
        actions["open-audit-trail"].Href.Should().Contain($"/api/workstation/evidence/subjects/security-instrument/{FinancialRecordExplorerAaplSecurityId:D}/graph");

        explorer.RecordGraph.Nodes.Select(static node => node.Label).Should().Contain(
            ["Apple Inc.", "Position / transaction", "Reconciliation", "Posted Journal", "Corporate action evidence", "Role / position", "Accounting projection", "Posting candidate", "Independent approval", "Reported line", "Evidence", "Audit event"]);
        explorer.RecordGraph.Edges.Select(static edge => edge.Label).Should().Contain(
            ["referenced by", "reconciles", "posts", "reported", "supports", "projects", "proposes", "authorizes", "retains evidence", "audits"]);

        var journalStore = app.Services.GetRequiredService<FinancialRecordExplorerJournalStore>();
        journalStore.LastQuery.Should().Be(new LedgerJournalEntryQuery(
            LedgerBookId: FinancialRecordExplorerLedgerBookId,
            AggregateId: FinancialRecordExplorerLedgerBookId,
            SourceEventId: FinancialRecordExplorerEventId));
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("portfolio")]
    public async Task MapWorkstationEndpoints_RunBackedExplorers_ForSecurityMasterOnlyOperator_ShouldRefuse(string explorerId)
    {
        // Ledger and portfolio build entirely from StrategyRunReadService -- trial balances, positions,
        // run identifiers and proof links. GetRunLedger, GetRunLedgerTrialBalance and
        // GetRunLedgerJournal serve that data directly and admit only the strategy permissions, so
        // Security Master access alone is not a claim on strategy-run financial records.
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: UserPermission.ViewSecurityMaster | UserPermission.ModifySecurityMaster);

        var response = await app.GetTestClient()
            .GetAsync($"/api/workstation/financial-record-explorers/{explorerId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityInstrumentExplorer_ForSecurityMasterOnlyOperator_ShouldWithholdRunIdentity()
    {
        // The explorer admits Security Master callers on their own basis, and that basis is not a claim
        // on which strategy run touched an instrument. The run routes serving run id, strategy name and
        // mode admit only ViewStrategies and ManageStrategies, so those must not arrive as scope items,
        // as a run-addressed evidence action, or embedded in the description.
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: UserPermission.ViewSecurityMaster | UserPermission.ModifySecurityMaster);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-security-run", withBreaks: false));

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.Rows.Should().NotBeEmpty("the Security Master references themselves are what this caller is entitled to");

        explorer.ScopeItems.Select(static item => item.Label).Should().NotContain(["Run", "Strategy", "Mode"]);
        explorer.ScopeItems.Select(static item => item.Label).Should().Contain(["As of", "Source"]);
        explorer.Description.Should().NotContain("financial-record-explorer-security-run");
        explorer.ProofActions.Should().NotContain(static action => action.ActionId == "open-evidence");
        explorer.ProofActions.Should().Contain(static action => action.ActionId == "open-source");

        var row = explorer.Rows[0];
        row.Detail.UsedIn.Should().NotContain(static relationship =>
            relationship.RelationshipId == "portfolio-position" || relationship.RelationshipId == "ledger-line");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityInstrumentExplorer_ForSecurityMasterOnlyOperator_ShouldStillRead()
    {
        // The same caller keeps security-instrument: that explorer is the Security Master coverage
        // surface, so a Security Master permission is one of its two bases. Splitting the run-backed
        // explorers away must not take this one with them.
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: UserPermission.ViewSecurityMaster | UserPermission.ModifySecurityMaster);

        var response = await app.GetTestClient()
            .GetAsync("/api/workstation/financial-record-explorers/security-instrument");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityInstrumentExplorer_ForStrategyOnlyOperator_ShouldWithholdOtherFamiliesEnrichments()
    {
        // The rows are the Security Master references a strategy run touched, so ViewStrategies -- the
        // set the built-in ReadOnly role carries -- admits this explorer. Admission is not a claim on
        // what decorates each row: the passport answers to ViewSecurityMaster/ModifySecurityMaster and
        // AssetOperations to the trading, lending, security-master and admin set, neither of which
        // includes a strategy permission. A caller holding only ViewStrategies must see the references
        // and nothing sourced from a family it could not fetch head-on.
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-security-run", withBreaks: false));

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.Rows.Should().Contain(
            item => item.RecordId == $"security:{FinancialRecordExplorerAaplSecurityId:D}",
            "the run-derived references are what a strategy permission does entitle");

        // Each of these summary items is emitted only when its family produced a payload, so their
        // absence is the assertion that nothing was loaded rather than loaded and blanked.
        explorer.SummaryItems.Select(static item => item.Label).Should().NotContain(
            ["Passports", "Operations", "Direct Lending", "Terms", "Cash Flows", "Reconciliations", "Accounting Projections", "Posted Journals", "Reported Usage"]);

        var row = explorer.Rows.Single(item => item.RecordId == $"security:{FinancialRecordExplorerAaplSecurityId:D}");
        row.Cells.Should().NotContain(static cell => cell.ColumnId == "trust" && cell.DisplayValue == "Trusted");
        row.Cells.Should().NotContain(static cell => cell.ColumnId == "operations" && cell.DisplayValue == "Ready");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityInstrumentExplorer_ShouldRemainProjectionOnlyWithoutTypedSpine()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        app.Services.GetRequiredService<FinancialRecordExplorerAssetAccountingEventSpineService>().ReturnSpine = false;

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-security-run", withBreaks: false));

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument",
            ServerJsonOptions);

        var row = explorer!.Rows.Single(item => item.RecordId == $"security:{FinancialRecordExplorerAaplSecurityId:D}");
        row.Detail.Fields.Should().Contain(static field => field.Label == "Accounting Projection");
        row.Detail.Fields.Should().NotContain(static field =>
            field.Label == "Posting Candidate" ||
            field.Label == "Approved" ||
            field.Label == "Posted Journal" ||
            field.Label == "Journal Totals" ||
            field.Label == "Journal Currency" ||
            field.Label == "Balanced Journal Lines");
        row.Detail.Impacts.Should().NotContain(static impact =>
            impact.RelationshipId == "posting-candidate" ||
            impact.RelationshipId == "approval" ||
            impact.RelationshipId == "journal");
        row.Detail.UsedIn.Should().NotContain(static relationship => relationship.RelationshipId == "instrument-journal-proof");
        row.Detail.ProofActions.Should().NotContain(static action => action.ActionId == "open-journal-impact");
        explorer.RecordGraph.Nodes.Should().NotContain(static node => node.NodeType == "journal");
        explorer.RecordGraph.Edges.Should().NotContain(static edge => edge.Label == "posts");
        row.Detail.ProofActions.Select(static action => action.Href).Should().NotContain(static href =>
            href.Contains("ledgerEntryId=", StringComparison.OrdinalIgnoreCase));
        app.Services.GetRequiredService<FinancialRecordExplorerJournalStore>().LastQuery.Should().BeNull();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityInstrumentExplorer_ShouldExcludeNonCanonicalProjectionLineageWithoutTypedSpine()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        app.Services.GetRequiredService<FinancialRecordExplorerAssetOperationsQueryService>().ProjectionEventType =
            "ThirdPartyUnregisteredPnLMark";
        app.Services.GetRequiredService<FinancialRecordExplorerAssetAccountingEventSpineService>().ReturnSpine = false;

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildActivePaperRun("financial-record-explorer-unregistered-lineage", withBreaks: false));

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument",
            ServerJsonOptions);

        var row = explorer!.Rows.Single(item => item.RecordId == $"security:{FinancialRecordExplorerAaplSecurityId:D}");
        row.Detail.Fields.Should().NotContain(static field =>
            field.Label == "Source Evidence" || field.Label == "Corporate Action Evidence" || field.Label == "Role / Position");
        row.Detail.Impacts.Should().NotContain(static impact =>
            impact.RelationshipId == "factor-evidence" || impact.RelationshipId == "instrument-role-position" || impact.RelationshipId == "economic-projection");
        row.Detail.UsedIn.Should().NotContain(static relationship =>
            relationship.RelationshipId == "accounting-projection-proof");
        explorer.RecordGraph.Nodes.Should().NotContain(static node =>
            node.Label == "Source evidence" || node.Label == "Role / position");
        explorer.RecordGraph.Edges.Should().NotContain(static edge =>
            edge.Label == "supports" || edge.Label == "projects");
        app.Services.GetRequiredService<FinancialRecordExplorerJournalStore>().LastQuery.Should().BeNull();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportLineProvenanceExplorer_ShouldExcludeApprovedButUnpublishedRecords()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var created = workflow.Create(
            "fund-alpha",
            "acct-cash",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "report.author",
            [new ReportPackLineProvenanceDto("trial-balance.cash", "position", "position-aapl", "ledger-evidence-1", RunId: "run-1", LedgerEntryId: "ledger-entry-1")],
            accessContext: BoundReportAccessContext());
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/report-line-provenance",
            ServerJsonOptions);

        explorer!.Rows.Should().BeEmpty();
        explorer.SummaryItems.Should().Contain(item => item.Label == "Reported lines" && item.Value == "0");
        explorer.RecordGraph.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerUnknownId_ShouldReturnNotFound()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        var response = await app.GetTestClient().GetAsync("/api/workstation/financial-record-explorers/not-real");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorerSavedViews_ShouldPersistAndReloadForExplorer()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        var client = app.GetTestClient();

        var saveResponse = await client.PostAsJsonAsync(
            "/api/workstation/financial-record-explorers/ledger/saved-views",
            new FinancialRecordExplorerSavedViewSaveRequestDto(
                "Material trial-balance view",
                "Operator-created saved view for ledger review.",
                "Cash",
                [new("account-type", "Account Type", "Asset")],
                ["accountName", "balance"]),
            ServerJsonOptions);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<FinancialRecordExplorerSavedViewDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.IsSystem.Should().BeFalse();
        saved.ColumnIds.Should().Equal(["accountName", "balance"]);

        using var payload = await ReadJsonAsync(client, "/api/workstation/financial-record-explorers/ledger");
        payload.RootElement.GetProperty("savedViews").EnumerateArray().Should().Contain(view =>
            view.GetProperty("viewId").GetString() == saved.ViewId &&
            view.GetProperty("label").GetString() == "Material trial-balance view" &&
            !view.GetProperty("isSystem").GetBoolean() &&
            view.GetProperty("columnIds").EnumerateArray().Select(column => column.GetString()).SequenceEqual(new[] { "accountName", "balance" }));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorer_ShouldApplySavedViewOnServer()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
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
    public async Task MapWorkstationEndpoints_FinancialRecordExplorer_ShouldApplyDimensionFilterOnServer()
    {
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
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
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
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
            currentUserPermissions: ExplorerOperatorPermissions,
            currentUserCompanyId: "tenant-alpha");
        await using var betaApp = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services, savedViewRoot),
            currentUserPermissions: ExplorerOperatorPermissions,
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

    [Fact]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorers_ShouldNotServeAnUnattributedRun()
    {
        // A run with no fund profile is attributable to no tenant, so under active tenancy it cannot
        // become the source for all of them -- and it only takes being the newest qualifying run.
        // Distinct from an unbound fund, which the registry means as "nobody has claimed it yet".
        await using var app = await CreateAppAsync(
            services =>
            {
                RegisterFinancialRecordExplorerTestServices(services);
                services.AddSingleton<IFundProfileTenancyRegistry>(
                    new ForeignOwnerFundProfileTenancyRegistry("unused-fund", "another-tenant"));
            },
            currentUserPermissions: ExplorerOperatorPermissions);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(
            BuildActivePaperRun("financial-record-explorer-unattributed", withBreaks: false) with
            {
                FundProfileId = null,
                FundDisplayName = null
            });

        using var response = await app.GetTestClient().GetAsync(
            "/api/workstation/financial-record-explorers/ledger");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var explorer = await response.Content.ReadFromJsonAsync<FinancialRecordExplorerDto>(ServerJsonOptions);
        explorer.Should().NotBeNull();
        explorer!.Rows.Should().BeEmpty("an unattributed run is nobody's source while tenancy is enforced");
    }

    /// <summary>
    /// Reports one fund profile as owned by a tenant other than the caller's, and every other fund as
    /// unbound. Enough to prove the explorer refuses a foreign run without standing in for the real
    /// registry's binding rules.
    /// </summary>
    private sealed class ForeignOwnerFundProfileTenancyRegistry(string fundProfileId, string ownerTenantId)
        : IFundProfileTenancyRegistry
    {
        public Task<FundProfileOwnership> BindAsync(
            string requestedFundProfileId,
            string requestedTenantId,
            string? requestedCompanyId = null,
            CancellationToken ct = default)
            => Task.FromResult(new FundProfileOwnership(requestedFundProfileId, requestedTenantId, requestedCompanyId));

        public Task<FundProfileOwnership?> ResolveAsync(string requestedFundProfileId, CancellationToken ct = default)
            => Task.FromResult<FundProfileOwnership?>(
                string.Equals(requestedFundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)
                    ? new FundProfileOwnership(fundProfileId, ownerTenantId, ownerTenantId)
                    : null);

        public async Task<bool> IsAccessibleAsync(
            string requestedFundProfileId,
            string requestedTenantId,
            string? requestedCompanyId = null,
            CancellationToken ct = default)
        {
            var owner = await ResolveAsync(requestedFundProfileId, ct).ConfigureAwait(false);
            return owner is null || owner.IsHeldBy(requestedTenantId);
        }
    }

    /// <summary>
    /// Forwards to a real store while counting the unbounded run-history reads the ledger explorer
    /// makes. Only <c>Limit == int.MaxValue</c> is counted: that is the full-history scan, as
    /// distinct from the bounded reads other surfaces issue in the same request.
    /// </summary>
    private sealed class RunHistoryScanCountingRepository(IStrategyRepository inner) : IStrategyRepository
    {
        private int _fullHistoryScans;

        public int FullHistoryScans => Volatile.Read(ref _fullHistoryScans);

        public void Reset() => Volatile.Write(ref _fullHistoryScans, 0);

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
            => inner.RecordRunAsync(entry, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, CancellationToken ct = default)
            => inner.GetRunsAsync(strategyId, ct);

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default)
            => inner.GetLatestRunAsync(strategyId, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default)
            => inner.GetAllRunsAsync(ct);

        public Task<IReadOnlyList<StrategyRunEntry>> QueryVisibleRunsAsync(
            StrategyRunRepositoryQuery query,
            StrategyRunRepositoryScope? scope,
            CancellationToken ct = default)
        {
            if (query.Limit == int.MaxValue)
            {
                Interlocked.Increment(ref _fullHistoryScans);
            }

            return inner.QueryVisibleRunsAsync(query, scope, ct);
        }
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
        services.AddSingleton<FinancialRecordExplorerAssetOperationsQueryService>(
            new FinancialRecordExplorerAssetOperationsQueryService(FinancialRecordExplorerAaplSecurityId));
        services.AddSingleton<IAssetOperationsQueryService>(sp =>
            sp.GetRequiredService<FinancialRecordExplorerAssetOperationsQueryService>());
        services.AddSingleton<FinancialRecordExplorerJournalStore>();
        services.AddSingleton<ILedgerJournalStore>(sp => sp.GetRequiredService<FinancialRecordExplorerJournalStore>());
        services.AddSingleton<FinancialRecordExplorerAssetAccountingEventSpineService>();
        services.AddSingleton<IAssetAccountingEventSpineService>(sp => sp.GetRequiredService<FinancialRecordExplorerAssetAccountingEventSpineService>());
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

    private static IReadOnlyList<ReportPackEvidenceLinkDto> BuildCompleteReportLineEvidenceLinks(
        params ReportPackLineProvenanceDto[] lines)
        => lines
            .SelectMany(static line => new[]
            {
                line.EvidenceId,
                line.SourceId,
                line.RunId,
                line.SourceSessionId,
                line.LedgerEntryId,
                line.ReconciliationCaseId,
                line.ReconciliationRunId,
                line.ProviderEventId,
                line.SecurityMasterId,
                line.SecurityDefinitionId,
                line.ApprovalId
            })
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static id => new ReportPackEvidenceLinkDto(id!, id!, $"/evidence/{id}", "report-line-provenance"))
            .ToArray();

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

        public string ProjectionEventType { get; set; } = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction);

        public Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid requestedSecurityId, CancellationToken ct = default)
            => Task.FromResult<AssetOperationsDetailDto?>(requestedSecurityId == securityId ? CreateDetail() : null);

        public Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid requestedSecurityId, CancellationToken ct = default)
            => Task.FromResult<AssetOperationsReadinessDto?>(requestedSecurityId == securityId ? CreateReadiness() : null);

        private AssetOperationsDetailDto CreateDetail()
        {
            var readiness = CreateReadiness();
            var projectionRunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var reconciliationRunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var detail = new AssetOperationsDetailDto(
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
            var roleId = Guid.Parse("11111111-1111-1111-1111-111111111115");
            var economicEvent = new EconomicEventReferenceDto(
                FinancialRecordExplorerEventId,
                ProjectionEventType,
                1,
                new DateOnly(2026, 3, 22),
                _now,
                "SecurityMaster",
                "factor-row-aapl",
                SourceContentHash: new string('a', 64),
                EvidenceLinks: ["/evidence/factor-row-aapl"])
            {
                SecurityId = securityId,
                BookPositionId = FinancialRecordExplorerPositionId
            };
            var lineage = new ProjectionLineageDto(
                Guid.Parse("11111111-1111-1111-1111-111111111116"),
                Guid.Parse("11111111-1111-1111-1111-111111111117"),
                "equity-corporate-action",
                "1.0.0",
                "factor-paydown-projection-v1",
                "Base",
                economicEvent.EffectiveDate,
                _now,
                "AssetOperations",
                FinancialRecordExplorerPositionId.ToString("D"),
                economicEvent,
                EvidenceLinks: economicEvent.EvidenceLinks)
            {
                BookPositionId = FinancialRecordExplorerPositionId
            };
            var dimensions = new LedgerDimensionSetDto(
                "northwind-income",
                "entity-book",
                InstrumentId: securityId,
                BookId: FinancialRecordExplorerLedgerBookId.ToString("D"))
            {
                PositionId = FinancialRecordExplorerPositionId
            };
            var bookContext = new AccountingBookContextDto(
                FinancialRecordExplorerLedgerBookId,
                "northwind-income",
                Guid.Parse("11111111-1111-1111-1111-111111111118"),
                FundStructureNodeKindDto.Fund,
                "Northwind GAAP",
                "USD",
                AccountingBasisKindDto.Gaap,
                "gaap-mbs-v1",
                "v1",
                FinancialRecordExplorerPeriodId,
                Dimensions: dimensions);
            var state = new PositionEconomicStateDto(
                Guid.Parse("11111111-1111-1111-1111-111111111119"),
                FinancialRecordExplorerPositionId,
                economicEvent.EffectiveDate,
                "USD",
                5,
                ParAmount: 100_000m,
                OriginalFaceAmount: 100_000m,
                CurrentFaceAmount: 96_250m,
                PriorFactor: 0.9800m,
                CurrentFactor: 0.9625m,
                SourceEvent: economicEvent,
                EvidenceLinks: economicEvent.EvidenceLinks)
            {
                ProjectionLineage = lineage
            };
            return detail with
            {
                InstrumentRoles =
                [
                    new InstrumentRoleDto(
                        roleId,
                        securityId,
                        "northwind-income",
                        "Fund",
                        InstrumentRoleKinds.Holder,
                        InstrumentAccountingSides.Debit,
                        InstrumentEconomicSides.Asset,
                        new DateOnly(2026, 1, 1),
                        Version: 2,
                        EvidenceLinks: ["/evidence/position-aapl"])
                ],
                BookPositions =
                [
                    new BookPositionDto(
                        FinancialRecordExplorerPositionId,
                        securityId,
                        roleId,
                        bookContext,
                        BookPositionSides.Long,
                        "Active",
                        new DateOnly(2026, 1, 1),
                        Version: 4,
                        CurrentEconomicState: state,
                        ProjectionLineage: lineage,
                        EvidenceLinks: ["/evidence/position-aapl"])
                ],
                PositionEconomicStates = [state],
                ProjectionLineages = [lineage]
            };
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

    private sealed class FinancialRecordExplorerAssetAccountingEventSpineService : IAssetAccountingEventSpineService
    {
        private static readonly DateTimeOffset EventTimestamp = DateTimeOffset.Parse("2026-03-22T15:00:00Z");
        private static readonly DateTimeOffset PostedTimestamp = DateTimeOffset.Parse("2026-03-22T16:00:00Z");

        public bool ReturnSpine { get; set; } = true;

        public Task<AssetAccountingEventSpineDto?> GetLatestAsync(
            Guid eventId,
            long eventVersion,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReturnSpine || eventId != FinancialRecordExplorerEventId || eventVersion != 1)
            {
                return Task.FromResult<AssetAccountingEventSpineDto?>(null);
            }

            var effectiveDate = new DateOnly(2026, 3, 22);
            var sourceHash = new string('a', 64);
            var evidence = new RetainedEvidenceIdentityDto(
                "factor-row-aapl",
                "https://evidence.example.test/factor-row-aapl",
                new string('a', 64),
                "SecurityMaster",
                "factor-row-aapl",
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
                "accounting-controller",
                EventTimestamp,
                effectiveDate,
                1,
                EventTimestamp,
                "evidence-vault",
                AssetAccountingEvidenceSubjects.Event,
                FinancialRecordExplorerEventId.ToString("D"));
            var economicEvent = new EconomicEventReferenceDto(
                FinancialRecordExplorerEventId,
                AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
                1,
                effectiveDate,
                EventTimestamp,
                "SecurityMaster",
                "factor-row-aapl",
                SourceContentHash: sourceHash,
                EvidenceLinks: ["/evidence/factor-row-aapl"])
            {
                SecurityId = FinancialRecordExplorerAaplSecurityId,
                BookPositionId = FinancialRecordExplorerPositionId
            };
            var lineage = new ProjectionLineageDto(
                Guid.Parse("11111111-1111-1111-1111-111111111116"),
                Guid.Parse("11111111-1111-1111-1111-111111111117"),
                "equity-corporate-action",
                "1.0.0",
                "factor-paydown-projection-v1",
                "Base",
                effectiveDate,
                EventTimestamp,
                "AssetOperations",
                FinancialRecordExplorerPositionId.ToString("D"),
                economicEvent,
                EvidenceLinks: economicEvent.EvidenceLinks)
            {
                BookPositionId = FinancialRecordExplorerPositionId
            };
            var dimensions = new LedgerDimensionSetDto(
                "northwind-income",
                "entity-book",
                InstrumentId: FinancialRecordExplorerAaplSecurityId,
                BookId: FinancialRecordExplorerLedgerBookId.ToString("D"))
            {
                PositionId = FinancialRecordExplorerPositionId
            };
            const string approvalReferenceId = "approval-aapl-factor";
            var draftedCandidate = new PostingRuleJournalCandidateRequestDto(
                "northwind-income",
                AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
                1_750m,
                "USD",
                effectiveDate,
                "accountant",
                FinancialRecordExplorerLedgerBookId,
                FinancialRecordExplorerPeriodId,
                EventTimestamp,
                "AAPL factor paydown drafted candidate",
                AccountingBasisKindDto.Gaap,
                LedgerBookId: FinancialRecordExplorerLedgerBookId,
                SourceEventId: FinancialRecordExplorerEventId);
            var draftedCandidateResult = new PostingRuleJournalCandidateResultDto(
                new RuleDryRunResultDto(
                    "northwind-income",
                    FinancialRecordExplorerLedgerBookId,
                    AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
                    effectiveDate,
                    1_750m,
                    "USD",
                    true,
                    null,
                    [],
                    [],
                    []),
                null,
                null,
                [],
                null,
                null,
                1_750m,
                1_750m,
                0m,
                true,
                false,
                true,
                false,
                [],
                []);
            var draftedCandidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(draftedCandidate);
            var draftedCandidateResultFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(draftedCandidateResult);
            var postingApprovalEvidence = new RetainedEvidenceIdentityDto(
                "approval-evidence-aapl",
                "https://evidence.example.test/approval-aapl",
                new string('c', 64),
                "GovernedEvidenceVault",
                "vault://approval-aapl",
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
                "controller",
                EventTimestamp,
                effectiveDate,
                1,
                EventTimestamp,
                "evidence-vault",
                AssetAccountingEvidenceSubjects.PostingApproval,
                AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
                    FinancialRecordExplorerEventId,
                    1,
                    "northwind-income",
                    FinancialRecordExplorerLedgerBookId,
                    FinancialRecordExplorerPeriodId,
                    AccountingBasisKindDto.Gaap,
                    approvalReferenceId,
                    draftedCandidateFingerprint,
                    null,
                    null));
            var stages = new[]
            {
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Expected, EventTimestamp, "asset-operations", [evidence], "expected-aapl"),
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Projected, EventTimestamp, "projection-engine", [evidence], lineage.ProjectionRunId.ToString("D")),
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Drafted, EventTimestamp.AddMinutes(10), "accountant", [evidence], "posting-candidate-aapl"),
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Approved, EventTimestamp.AddMinutes(30), "controller", [postingApprovalEvidence], approvalReferenceId),
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Posted, PostedTimestamp, "ledger-poster", [postingApprovalEvidence], FinancialRecordExplorerEventId.ToString("D"))
            };
            var projected = new ProjectedAccountingEffectDto(
                lineage.ProjectionRunId,
                lineage.ModelKey,
                lineage.ModelVersion,
                effectiveDate,
                1_750m,
                1_750m,
                "USD",
                [
                    new ProjectedAccountingEffectLineDto(LedgerAccounts.Cash.ToString(), 1_750m, 0m, "USD"),
                    new ProjectedAccountingEffectLineDto(LedgerAccounts.Securities("AAPL").ToString(), 0m, 1_750m, "USD")
                ]);
            var posted = new PostedJournalImpactDto(
                FinancialRecordExplorerJournalId,
                FinancialRecordExplorerLedgerBookId,
                FinancialRecordExplorerPeriodId,
                AccountingBasisKindDto.Gaap,
                PostedTimestamp,
                JournalPostingStatusDto.Posted,
                "USD",
                1_750m,
                1_750m,
                [
                    new PostedJournalImpactLineDto(FinancialRecordExplorerDebitLineId, LedgerAccounts.Cash.ToString(), 1_750m, 0m, "USD", Dimensions: dimensions),
                    new PostedJournalImpactLineDto(FinancialRecordExplorerCreditLineId, LedgerAccounts.Securities("AAPL").ToString(), 0m, 1_750m, "USD", Dimensions: dimensions)
                ]);
            var spine = new AssetAccountingEventSpineDto(
                FinancialRecordExplorerEventId,
                AssetAccountingEventKindDto.CorporateAction,
                1,
                5,
                effectiveDate,
                1_750m,
                "USD",
                new AssetAccountingEventScopeDto(
                    FinancialRecordExplorerAaplSecurityId,
                    3,
                    FinancialRecordExplorerPositionId,
                    4,
                    FinancialRecordExplorerLedgerBookId,
                    FinancialRecordExplorerPeriodId,
                    AccountingBasisKindDto.Gaap,
                    "northwind-income",
                    Dimensions: dimensions),
                economicEvent,
                lineage,
                [evidence],
                stages,
                projected,
                posted,
                DraftedCandidate: draftedCandidate,
                DraftedCandidateResult: draftedCandidateResult,
                DraftedCandidateFingerprint: draftedCandidateFingerprint,
                DraftedCandidateResultFingerprint: draftedCandidateResultFingerprint);
            AssetAccountingEventSpineValidator.IsValid(spine).Should().BeTrue();
            return Task.FromResult<AssetAccountingEventSpineDto?>(spine);
        }

        public Task<AssetAccountingPostingCandidateDto> BuildPostingCandidateAsync(
            AssetAccountingPostingCandidateRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AssetAccountingEventSpineAppendResultDto> ProjectAsync(
            ProjectAssetAccountingEventRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AssetAccountingEventSpineAppendResultDto> AppendLifecycleStageAsync(
            AppendAssetAccountingLifecycleStageRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FinancialRecordExplorerJournalStore : ILedgerJournalStore
    {
        public LedgerJournalEntryQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastQuery = query;
            var timestamp = DateTimeOffset.Parse("2026-03-22T16:00:00Z");
            var dimensions = new LedgerLineDimensionSet(
                FundId: "northwind-income",
                EntityId: "entity-book",
                InstrumentId: FinancialRecordExplorerAaplSecurityId,
                BookId: FinancialRecordExplorerLedgerBookId.ToString("D"))
            {
                PositionId = FinancialRecordExplorerPositionId
            };
            var entry = new JournalEntry(
                FinancialRecordExplorerJournalId,
                timestamp,
                "MBS principal paydown",
                [
                    new LedgerEntry(FinancialRecordExplorerDebitLineId, FinancialRecordExplorerJournalId, timestamp, LedgerAccounts.Cash, 1_750m, 0m, "MBS principal paydown", dimensions, new LedgerEntryCurrency("USD", "USD", 1_750m, 0m, 1m)),
                    new LedgerEntry(FinancialRecordExplorerCreditLineId, FinancialRecordExplorerJournalId, timestamp, LedgerAccounts.Securities("AAPL"), 0m, 1_750m, "MBS principal paydown", dimensions, new LedgerEntryCurrency("USD", "USD", 0m, 1_750m, 1m))
                ],
                new JournalEntryMetadata(
                    ActivityType: "MbsFactorPaydown",
                    SecurityId: FinancialRecordExplorerAaplSecurityId,
                    LedgerBook: FinancialRecordExplorerLedgerBookId.ToString("D"),
                    EffectiveDate: new DateOnly(2026, 3, 22),
                    Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sourceEventId"] = FinancialRecordExplorerEventId.ToString("D"),
                        ["sourceEventType"] = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
                        ["sourceEventVersion"] = "1",
                        ["sourceEventDomain"] = "SecurityMaster",
                        ["sourceEventEntityId"] = "factor-row-aapl",
                        ["sourceEventContentHash"] = new string('a', 64),
                        ["securityId"] = FinancialRecordExplorerAaplSecurityId.ToString("D"),
                        ["bookPositionId"] = FinancialRecordExplorerPositionId.ToString("D"),
                        ["projectionRunId"] = "11111111-1111-1111-1111-111111111116",
                        ["projectionEventId"] = "11111111-1111-1111-1111-111111111117",
                        ["projectionModelKey"] = "equity-corporate-action",
                        ["projectionModelVersion"] = "1.0.0",
                        ["projectionEngineVersion"] = "factor-paydown-projection-v1",
                        ["projectionScenario"] = "Base"
                    }));
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
            [
                new LedgerJournalEntryRecord(
                    entry,
                    FinancialRecordExplorerLedgerBookId,
                    FinancialRecordExplorerPeriodId,
                    Guid.Parse("11111111-1111-1111-1111-111111111122"),
                    null,
                    42,
                    timestamp,
                    AccountingBasisKindDto.Gaap,
                    "gaap-mbs-v1",
                    "v1",
                    "posting.mbs-factor-paydown",
                    "v1",
                    FinancialRecordExplorerEventId)
            ]);
        }

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(Guid? ledgerBookId = null, string? status = null, string? fundProfileId = null, Guid? fundStructureNodeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LedgerAccountingPeriod> SavePeriodAsync(LedgerAccountingPeriod period, long expectedVersion, PeriodCloseEventRecord? closeEvent = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(string? fundProfileId = null, Guid? fundStructureNodeId = null, FundStructureNodeKindDto? fundStructureNodeKind = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ReportLineProvenanceExplorer_ShouldNotServeAnotherTenantsRecords()
    {
        // ListRecords is not tenant-partitioned at its source -- it returns every workflow record the
        // host retains -- and the explorer used to build from it with no access context at all, which
        // the builder treats as the legacy unbound caller and answers unfiltered. The reporting routes
        // serve these same records under RequireBoundScope, so the explorer must not be the way round
        // them.
        await using var app = await CreateAppAsync(
            services => RegisterFinancialRecordExplorerTestServices(services),
            currentUserPermissions: ExplorerOperatorPermissions);
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();

        var foreign = workflow.Create(
            "fund-foreign",
            "acct-foreign",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "report.author",
            [
                new ReportPackLineProvenanceDto(
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
                    ApprovalId: "approval-1")
            ],
            accessContext: new ReportAccessQueryContext(
                ActorPrincipalId: "foreign-operator",
                CompanyId: "tenant-foreign",
                TenantId: "tenant-foreign",
                RequireBoundScope: true));
        workflow.Transition(foreign.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(foreign.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(
            foreign.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:foreign-pack",
            "manifest-foreign-202603",
            "vault/report-packs/manifest-foreign-202603.json",
            BuildCompleteReportLineEvidenceLinks());

        var explorer = await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/report-line-provenance",
            ServerJsonOptions);

        explorer.Should().NotBeNull();
        explorer!.Rows.Should().BeEmpty(
            "the only retained record is bound to another tenant, and this caller resolved tenant-test");
    }

    /// <summary>
    /// The access context a request-serving create carries. Records made without one are legacy-shaped
    /// -- no tenant, no company, no policy snapshot -- and the reporting routes refuse them under
    /// RequireBoundScope, so a fixture that seeds them is not exercising what the explorer serves.
    /// Tenant and company match <c>CreateAppAsync</c>'s defaults.
    /// </summary>
    private static ReportAccessQueryContext BoundReportAccessContext()
        => new(
            ActorPrincipalId: "ops-user",
            CompanyId: "tenant-test",
            TenantId: "tenant-test",
            RequireBoundScope: true);

}
