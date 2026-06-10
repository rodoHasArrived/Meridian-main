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

    [Theory]
    [InlineData("LedgerExplorer", "ledger")]
    [InlineData("PortfolioExplorer", "portfolio")]
    [InlineData("SecurityInstrumentExplorer", "security-instrument")]
    public void ResolveExplorerId_ShouldMapShellPageTagsToSharedExplorerIds(string pageTag, string explorerId)
    {
        FinancialRecordExplorerViewModel.ResolveExplorerId(pageTag).Should().Be(explorerId);
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
}
