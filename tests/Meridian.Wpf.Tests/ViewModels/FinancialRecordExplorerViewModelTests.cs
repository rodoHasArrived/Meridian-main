using Meridian.Contracts.Workstation;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FinancialRecordExplorerViewModelTests
{
    [Fact]
    public void BuildPresentation_ShouldMapDtoRowsToDenseTableAndInspectorFields()
    {
        var dto = CreateExplorerDto(
            rows:
            [
                CreateRow(
                    recordId: "ledger:test:cash",
                    label: "Cash",
                    cells:
                    [
                        new("accountName", "Cash"),
                        new("balance", "$1,000.00", "1000", FinancialRecordExplorerTone.Success)
                    ])
            ]);

        var presentation = FinancialRecordExplorerViewModel.BuildPresentation(dto);
        var inspector = FinancialRecordExplorerViewModel.BuildRecordInspector(dto.SelectedRecord!);

        presentation.Rows.Should().ContainSingle();
        presentation.Table.Columns.Select(column => column.BindingPath)
            .Should().Contain(["Cells[accountName]", "Cells[balance]"]);
        presentation.Rows[0].Cells["balance"].Should().Be("$1,000.00");
        presentation.SelectedRecord.Should().NotBeNull();
        inspector.Title.Should().Be("Cash");
        inspector.Facts.Select(fact => fact.Label).Should().Contain(["Balance", "Source"]);
    }

    [Fact]
    public void BuildExplorerRoute_WithSavedView_ShouldPreserveSharedEndpointAndEncodeViewId()
    {
        var route = FinancialRecordExplorerViewModel.BuildExplorerRoute("ledger", "operator cash/GAAP view");

        route.Should().Be("/api/workstation/financial-record-explorers/ledger?viewId=operator%20cash%2FGAAP%20view");
    }

    [Fact]
    public void BuildPresentation_ShouldCarrySavedViewActiveStateForServerScopedProjection()
    {
        var dto = CreateExplorerDto(
            rows:
            [
                CreateRow(
                    recordId: "ledger:test:cash",
                    label: "Cash",
                    cells:
                    [
                        new("accountName", "Cash"),
                        new("balance", "$1,000.00", "1000", FinancialRecordExplorerTone.Success)
                    ])
            ]) with
        {
            SavedViews =
            [
                new("system-ledger", "Default", "Default view.", IsSystem: true, IsActive: false, Filters: []),
                new("operator-cash", "Cash scoped", "Server-side cash filter.", IsSystem: false, IsActive: true, Filters: [new("accountName", "Account", "Cash")])
            ],
            SummaryItems =
            [
                new("Visible records", "1", "Showing 1 of 2 records after view operator-cash.", FinancialRecordExplorerTone.Info)
            ]
        };

        var presentation = FinancialRecordExplorerViewModel.BuildPresentation(dto);

        presentation.SavedViews.Should().ContainSingle(view => view.ViewId == "operator-cash" && view.IsActive);
        presentation.SummaryItems.Should().ContainSingle(item => item.Label == "Visible records" && item.Value == "1");
    }

    [Fact]
    public void FinancialRecordExplorerPage_ShouldExposeSavedViewApplyCommand()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Meridian.Wpf",
            "Views",
            "FinancialRecordExplorerPage.xaml"));

        xaml.Should().Contain("FinancialRecordExplorerActiveSavedViewText");
        xaml.Should().Contain("ApplySavedViewCommand");
        xaml.Should().Contain("CommandParameter=\"{Binding ViewId}\"");
    }

    [Fact]
    public void BuildPresentation_BlockedExplorer_ShouldShowBlockedEmptyStateAndDisabledActions()
    {
        var dto = CreateExplorerDto(
            isBlocked: true,
            blockedReason: "Strategy run read service is not registered.",
            rows: []);

        var presentation = FinancialRecordExplorerViewModel.BuildPresentation(dto);

        presentation.Rows.Should().BeEmpty();
        presentation.Table.EmptyTitle.Should().Be("Financial record source blocked");
        presentation.Table.EmptyDetail.Should().Contain("Strategy run read service");
        presentation.ExplorerActions.Should().ContainSingle();
        presentation.ExplorerActions[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildPresentation_ReportLineProvenanceChain_ShouldCarryInstrumentActivityAndAuditLinks()
    {
        const string instrumentHref = "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111";
        const string activityHref = "/api/workstation/evidence/subjects/provider-event/provider-event-position-aapl/packet";
        const string auditHref = "/api/workstation/evidence/subjects/report-line/ledger-evidence-1/packet";
        var selected = new FinancialRecordExplorerSelectedRecordDto(
            "report-line:test:cash",
            "Report line",
            "trial-balance.cash",
            "2026-03 - board-pack v1",
            "Governed report line with retained instrument, position, reconciliation, journal, report, evidence, and audit links.",
            FinancialRecordExplorerTone.Success,
            Fields:
            [
                new("Instrument", "11111111-1111-1111-1111-111111111111", instrumentHref, FinancialRecordExplorerTone.Success),
                new("Position / transaction", "provider-event-position-aapl", activityHref, FinancialRecordExplorerTone.Info),
                new("Evidence and audit links", "ledger-evidence-1", auditHref, FinancialRecordExplorerTone.Info)
            ],
            ProofActions:
            [
                new("open-instrument", "Open instrument", "Open retained instrument evidence.", instrumentHref, Tone: FinancialRecordExplorerTone.Success),
                new("open-position-transaction", "Open position/transaction evidence", "Open retained position or transaction evidence.", activityHref, Tone: FinancialRecordExplorerTone.Info),
                new("open-evidence-audit-links", "Open evidence and audit links", "Open retained audit packet.", auditHref, Tone: FinancialRecordExplorerTone.Info)
            ],
            UsedIn: [],
            Impacts:
            [
                new("instrument", "Instrument", "Instrument identity anchors the report line.", instrumentHref, FinancialRecordExplorerTone.Success),
                new("position-transaction", "Position / transaction", "Provider event feeds reconciliation before journal support.", activityHref, FinancialRecordExplorerTone.Info),
                new("evidence-audit-links", "Evidence and audit links", "Evidence id anchors the retained audit packet.", auditHref, FinancialRecordExplorerTone.Info)
            ],
            FullRecordHref: "/api/workstation/financial-record-explorers/portfolio?lineKey=trial-balance.cash&sourceId=position-aapl");
        var dto = new FinancialRecordExplorerDto(
            "report-line-provenance",
            "Report-Line Provenance Explorer",
            "Review retained report-line proof chains.",
            "Source-backed",
            IsBlocked: false,
            BlockedReason: string.Empty,
            ScopeItems: [new("Report packs", "1")],
            SavedViews: [new("system-report-line-provenance-default", "Report lines", "Default view.", IsSystem: true, IsActive: true, Filters: [])],
            SummaryItems: [new("Report lines", "1", "Governed report lines with retained provenance.")],
            Filters: [],
            Columns:
            [
                new("lineKey", "Report Line", Width: 200),
                new("instrument", "Instrument", Width: 160),
                new("activity", "Position / Transaction", Width: 190)
            ],
            Rows:
            [
                new(
                    "report-line:test:cash",
                    "report-line",
                    "trial-balance.cash",
                    "position:position-aapl",
                    "Published",
                    FinancialRecordExplorerTone.Success,
                    Cells:
                    [
                        new("lineKey", "trial-balance.cash"),
                        new("instrument", "11111111-1111-1111-1111-111111111111", LinkHref: instrumentHref),
                        new("activity", "provider-event-position-aapl", LinkHref: activityHref)
                    ],
                    selected)
            ],
            SelectedRecord: selected,
            ProofActions: [],
            RecordGraph: new FinancialRecordExplorerRecordGraphDto([], []));

        var presentation = FinancialRecordExplorerViewModel.BuildPresentation(dto);
        var inspector = FinancialRecordExplorerViewModel.BuildRecordInspector(dto.SelectedRecord!);
        var actions = dto.SelectedRecord!.ProofActions
            .Select(FinancialRecordExplorerViewModel.FinancialRecordExplorerActionModel.FromDto)
            .ToArray();

        presentation.Table.Columns.Select(static column => column.BindingPath)
            .Should().Contain(["Cells[instrument]", "Cells[activity]"]);
        presentation.Rows.Single().Cells["activity"].Should().Be("provider-event-position-aapl");
        inspector.Facts.Select(static fact => fact.Label).Should().Contain(
            ["Instrument", "Position / transaction", "Evidence and audit links"]);
        actions.Select(static action => action.TargetPageTag).Should().Contain(["SecurityMaster", "FundAuditTrail"]);
    }


    [Fact]
    public void BuildPresentation_SecurityInstrumentExplorer_ShouldPreserveSharedDtoParityFields()
    {
        var dto = CreateSecurityInstrumentExplorerDto();

        var presentation = FinancialRecordExplorerViewModel.BuildPresentation(dto);
        var inspector = FinancialRecordExplorerViewModel.BuildRecordInspector(dto.SelectedRecord!);
        var actions = dto.SelectedRecord!.ProofActions
            .Select(FinancialRecordExplorerViewModel.FinancialRecordExplorerActionModel.FromDto)
            .ToArray();

        presentation.Rows.Should().ContainSingle(row =>
            row.Cells["security"] == "Apple Inc." &&
            row.Cells["identifierConfidence"] == "96%" &&
            row.Cells["operations"] == "Ready" &&
            row.Cells["ledger"] == "1 projection");
        presentation.Table.Columns.Select(static column => column.BindingPath).Should().Contain(
            ["Cells[security]", "Cells[identifierConfidence]", "Cells[operations]", "Cells[cashFlow]", "Cells[ledger]"]);
        inspector.Facts.Select(static fact => fact.Label).Should().Contain(
            ["Instrument Identity", "Provider Evidence", "AssetOperations Readiness", "Ledger Impact", "Report Usage"]);
        actions.Select(static action => action.TargetPageTag).Should().Contain(["SecurityMaster", "FundAuditTrail", "ReportLineProvenanceExplorer"]);
    }

    [Theory]
    [InlineData("LedgerExplorer", "ledger")]
    [InlineData("PortfolioExplorer", "portfolio")]
    [InlineData("SecurityInstrumentExplorer", "security-instrument")]
    [InlineData("ReportLineProvenanceExplorer", "report-line-provenance")]
    public void ResolveExplorerId_ShouldMapShellPageTagsToSharedExplorerIds(string pageTag, string explorerId)
    {
        FinancialRecordExplorerViewModel.ResolveExplorerId(pageTag).Should().Be(explorerId);
    }

    [Theory]
    [InlineData("/api/workstation/financial-record-explorers/ledger?lineKey=trial-balance.nav&sourceId=ledger-entry-1", "LedgerExplorer")]
    [InlineData("/api/workstation/financial-record-explorers/portfolio?lineKey=positions.aapl&sourceId=position-aapl", "PortfolioExplorer")]
    [InlineData("/api/workstation/financial-record-explorers/security-instrument?lineKey=security.aapl&sourceId=security-aapl", "SecurityInstrumentExplorer")]
    [InlineData("/api/workstation/financial-record-explorers/report-line-provenance?lineKey=trial-balance.cash&sourceId=ledger-entry-1", "ReportLineProvenanceExplorer")]
    public void ProofActions_ShouldMapSharedFinancialRecordExplorerRoutesToShellPageTags(string href, string expectedPageTag)
    {
        var action = FinancialRecordExplorerViewModel.FinancialRecordExplorerActionModel.FromDto(
            new FinancialRecordExplorerProofActionDto(
                "open-report-line",
                "Open report line",
                "Open retained report-line source.",
                href));

        action.IsEnabled.Should().BeTrue();
        action.TargetPageTag.Should().Be(expectedPageTag);
        action.DisabledReason.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/fund-structure/reporting/packs/09f97410-0e4e-4c39-9db8-d79de196d2db/deliveries", "FundReportPack")]
    [InlineData("/api/workstation/reconciliation/runs/recon-run-1", "FundReconciliation")]
    [InlineData("/api/ledger/journal-entry-workbench?ledgerEntryId=ledger-entry-1", "FundAccountingConfigure")]
    [InlineData("/api/workstation/evidence/subjects/approval/approval-1/packet", "FundAuditTrail")]
    [InlineData("/api/workstation/evidence/subjects/report-line/ledger-evidence-1/packet", "FundAuditTrail")]
    [InlineData("/api/workstation/evidence/subjects/provider-event/provider-event-position-aapl/packet", "FundAuditTrail")]
    [InlineData("/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111", "SecurityMaster")]
    [InlineData("/api/workstation/evidence/subjects/report-pack-delivery/report%3Aattempt/graph", "FundAuditTrail")]
    public void ProofActions_ReportLineProvenanceDrillThroughRoutes_ShouldMapToShellPageTags(string href, string expectedPageTag)
    {
        var action = FinancialRecordExplorerViewModel.FinancialRecordExplorerActionModel.FromDto(
            new FinancialRecordExplorerProofActionDto(
                "open-proof",
                "Open proof",
                "Open retained proof.",
                href));

        action.IsEnabled.Should().BeTrue();
        action.TargetPageTag.Should().Be(expectedPageTag);
        action.DisabledReason.Should().BeEmpty();
    }


    private static FinancialRecordExplorerDto CreateSecurityInstrumentExplorerDto()
    {
        const string securityHref = "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111";
        const string passportHref = "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111/passport";
        const string operationsHref = "/api/workstation/assets/11111111-1111-1111-1111-111111111111/operations";
        const string reportHref = "/api/workstation/financial-record-explorers/report-line-provenance?lineKey=holdings.aapl.market-value&sourceId=AAPL";
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            "security:11111111-1111-1111-1111-111111111111",
            "security-instrument",
            "Apple Inc.",
            "AAPL · USD · Equity",
            "Security Master identity with provider, operations, ledger, and report proof.",
            FinancialRecordExplorerTone.Success,
            Fields:
            [
                new("Instrument Identity", "Apple Inc. / AAPL", "SecurityId 11111111-1111-1111-1111-111111111111.", FinancialRecordExplorerTone.Success),
                new("Provider Evidence", "Polygon primary · 96%", passportHref, FinancialRecordExplorerTone.Success),
                new("AssetOperations Readiness", "Ready", operationsHref, FinancialRecordExplorerTone.Success),
                new("Ledger Impact", "1 projection", "Ledger projection retained for the instrument.", FinancialRecordExplorerTone.Info),
                new("Report Usage", "1 report line", reportHref, FinancialRecordExplorerTone.Info)
            ],
            ProofActions:
            [
                new("open-security-master", "Open Security Master", "Open Security Master detail.", securityHref, Tone: FinancialRecordExplorerTone.Success),
                new("open-instrument-passport", "Open instrument passport", "Open provider evidence.", passportHref, Tone: FinancialRecordExplorerTone.Success),
                new("open-asset-operations", "Open AssetOperations", "Open operations readiness.", operationsHref, Tone: FinancialRecordExplorerTone.Info),
                new("open-report-line-provenance", "Open report-line provenance", "Open report usage.", reportHref, Tone: FinancialRecordExplorerTone.Info)
            ],
            UsedIn:
            [
                new("portfolio", "Portfolio position", "Portfolio uses Apple Inc.", "/api/workstation/financial-record-explorers/portfolio", FinancialRecordExplorerTone.Info),
                new("ledger", "Ledger trial balance", "Ledger projection uses Apple Inc.", "/api/workstation/financial-record-explorers/ledger", FinancialRecordExplorerTone.Info),
                new("report", "Report-line provenance", "Board pack reports Apple Inc.", reportHref, FinancialRecordExplorerTone.Info)
            ],
            Impacts:
            [
                new("passport", "Instrument passport", "Provider confidence and identity evidence.", passportHref, FinancialRecordExplorerTone.Success),
                new("operations", "AssetOperations readiness", "Cash-flow and reconciliation readiness.", operationsHref, FinancialRecordExplorerTone.Success),
                new("ledger", "Ledger projection", "Ledger impact is retained.", "/api/workstation/runs/run-1/ledger/journal", FinancialRecordExplorerTone.Info)
            ],
            FullRecordHref: securityHref);

        return new FinancialRecordExplorerDto(
            "security-instrument",
            "Security & Instrument Explorer",
            "Explore Security Master references used by retained accounting and portfolio records.",
            "Source-backed Security Master references from run run-1.",
            IsBlocked: false,
            BlockedReason: string.Empty,
            ScopeItems: [new("Run", "run-1")],
            SavedViews: [new("system-security-instrument-default", "Security references", "Default security view.", IsSystem: true, IsActive: true, Filters: [])],
            SummaryItems: [new("Securities", "1", "Distinct resolved instruments.")],
            Filters: [new("coverage", "Coverage", "Resolved")],
            Columns:
            [
                new("security", "Security", Width: 220),
                new("identifierConfidence", "Identifier Confidence", Width: 170),
                new("operations", "Operations", Width: 140),
                new("cashFlow", "Cash Flow", Width: 120),
                new("ledger", "Ledger", Width: 120)
            ],
            Rows:
            [
                new(
                    "security:11111111-1111-1111-1111-111111111111",
                    "security-instrument",
                    "Apple Inc.",
                    "AAPL",
                    "Resolved",
                    FinancialRecordExplorerTone.Success,
                    Cells:
                    [
                        new("security", "Apple Inc.", "Apple Inc.", FinancialRecordExplorerTone.Success, securityHref),
                        new("identifierConfidence", "96%", "0.96", FinancialRecordExplorerTone.Success, passportHref),
                        new("operations", "Ready", "Ready", FinancialRecordExplorerTone.Success, operationsHref),
                        new("cashFlow", "1 projected", "1", FinancialRecordExplorerTone.Info, operationsHref),
                        new("ledger", "1 projection", "1", FinancialRecordExplorerTone.Info, "/api/workstation/runs/run-1/ledger/journal")
                    ],
                    detail)
            ],
            SelectedRecord: detail,
            ProofActions: [],
            RecordGraph: new FinancialRecordExplorerRecordGraphDto([], []));
    }

    private static FinancialRecordExplorerDto CreateExplorerDto(
        IReadOnlyList<FinancialRecordExplorerRowDto> rows,
        bool isBlocked = false,
        string blockedReason = "")
        => new(
            "ledger",
            "Ledger Explorer",
            "Review retained ledger records.",
            isBlocked ? "Blocked" : "Source-backed",
            isBlocked,
            blockedReason,
            ScopeItems: [new("Run", "test-run")],
            SavedViews: [new("system-ledger", "Default", "Default view.", IsSystem: true, IsActive: true, Filters: [])],
            SummaryItems: [new("Rows", rows.Count.ToString(), "Retained source rows.")],
            Filters: [new("run", "Run", "test-run")],
            Columns:
            [
                new("accountName", "Account", Width: 180),
                new("balance", "Balance", "money", 120, IsRightAligned: true)
            ],
            rows,
            rows.FirstOrDefault()?.Detail,
            ProofActions:
            [
                new(
                    "open-ledger",
                    "Open ledger",
                    "Open source ledger.",
                    "/api/workstation/runs/test-run/ledger",
                    IsEnabled: !isBlocked,
                    DisabledReason: isBlocked ? blockedReason : string.Empty)
            ],
            RecordGraph: new FinancialRecordExplorerRecordGraphDto([], []));

    private static FinancialRecordExplorerRowDto CreateRow(
        string recordId,
        string label,
        IReadOnlyList<FinancialRecordExplorerCellDto> cells)
    {
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Ledger account",
            label,
            "Trial balance",
            "Source-backed trial-balance row.",
            FinancialRecordExplorerTone.Success,
            Fields:
            [
                new("Balance", "$1,000.00", "Source-backed balance.", FinancialRecordExplorerTone.Success),
                new("Source", "test-ledger")
            ],
            ProofActions:
            [
                new("open-ledger", "Open ledger", "Open source ledger.", "/api/workstation/runs/test-run/ledger")
            ],
            UsedIn:
            [
                new("trial-balance", "Trial balance", "Used in trial-balance review.", "/api/workstation/accounting")
            ],
            Impacts:
            [
                new("balance-sheet", "Balance sheet", "Impacts balance-sheet totals.")
            ],
            FullRecordHref: "/api/workstation/runs/test-run/ledger/trial-balance");

        return new FinancialRecordExplorerRowDto(
            recordId,
            "ledger",
            label,
            "test-ledger",
            "Ready",
            FinancialRecordExplorerTone.Success,
            cells,
            detail);
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate Meridian repository root.");
    }
}
