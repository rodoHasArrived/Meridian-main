using Meridian.Contracts.DirectLending;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class DirectLendingViewModelTests
{
    [Fact]
    public void CanPostAccrualForState_ShouldRequireSelectedIdleLoan()
    {
        var loan = BuildLoan();

        DirectLendingViewModel.CanPostAccrualForState(null, isDetailLoading: false, isPostingAccrual: false).Should().BeFalse();
        DirectLendingViewModel.CanPostAccrualForState(loan, isDetailLoading: true, isPostingAccrual: false).Should().BeFalse();
        DirectLendingViewModel.CanPostAccrualForState(loan, isDetailLoading: false, isPostingAccrual: true).Should().BeFalse();
        DirectLendingViewModel.CanPostAccrualForState(loan, isDetailLoading: false, isPostingAccrual: false).Should().BeTrue();
    }

    [Fact]
    public void BuildPostAccrualTooltip_ShouldExplainDisabledReasons()
    {
        var loan = BuildLoan();

        DirectLendingViewModel.BuildPostAccrualTooltip(null, isDetailLoading: false, isPostingAccrual: false)
            .Should().Contain("Select a direct-lending loan");
        DirectLendingViewModel.BuildPostAccrualTooltip(loan, isDetailLoading: true, isPostingAccrual: false)
            .Should().Contain("Wait for the selected loan detail");
        DirectLendingViewModel.BuildPostAccrualTooltip(loan, isDetailLoading: false, isPostingAccrual: true)
            .Should().Contain("already running");
        DirectLendingViewModel.BuildPostAccrualTooltip(loan, isDetailLoading: false, isPostingAccrual: false)
            .Should().Contain("Northwind Senior Secured");
    }

    [Fact]
    public void BuildLoanInspector_ShouldExposeLoanFactsAndUnavailableDetail()
    {
        var inspector = DirectLendingViewModel.BuildLoanInspector(
            BuildLoan(LoanStatus.NonPerforming),
            contract: null,
            servicing: null,
            isDetailLoading: false);

        inspector.Title.Should().Be("Northwind Senior Secured");
        inspector.Subtitle.Should().Be("Northwind Capital");
        inspector.Detail.Should().Contain("Loan detail is unavailable");
        inspector.Badge!.Value.Should().Be("NonPerforming");
        inspector.Facts.Should().Contain(fact => fact.Label == "Principal" && fact.Value == "$12,000,000");
        inspector.Facts.Should().Contain(fact => fact.Label == "Last accrual" && fact.Value.Contains("2026"));
    }

    [Fact]
    public void BuildLoanActionInspector_ShouldFailClosedWithoutSelection()
    {
        var blocked = DirectLendingViewModel.BuildLoanActionInspector(
            selected: null,
            isDetailLoading: false,
            isPostingAccrual: false,
            accrualResultText: string.Empty);
        var ready = DirectLendingViewModel.BuildLoanActionInspector(
            BuildLoan(),
            isDetailLoading: false,
            isPostingAccrual: false,
            accrualResultText: "Accrual posted.");

        blocked.Title.Should().Be("Loan actions unavailable");
        blocked.Detail.Should().Contain("disabled until");
        blocked.Facts.Should().Contain(fact => fact.Label == "Post accrual" && fact.Value == "Select loan");
        ready.Badge!.Value.Should().Be("Ready");
        ready.Facts.Should().Contain(fact => fact.Label == "Result" && fact.Value == "Accrual posted.");
    }

    [Fact]
    public void DirectLendingPageSource_ShouldUseCompactDenseTablesInspectorsAndActionTooltips()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DirectLendingPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\DirectLendingViewModel.cs"));

        xaml.Should().NotContain("<DataGrid");
        xaml.Should().NotContain("SectionCardStyle");
        xaml.Should().Contain("DirectLendingActionStrip");
        xaml.Should().Contain("DirectLendingFilterBar");
        xaml.Should().Contain("DirectLendingWorkbench");
        xaml.Should().Contain("DenseDataGridControl");
        xaml.Should().Contain("InspectorPanelControl");
        xaml.Should().Contain("DirectLendingLoansGrid");
        xaml.Should().Contain("DirectLendingAccrualsGrid");
        xaml.Should().Contain("DirectLendingCashTransactionsGrid");
        xaml.Should().Contain("DirectLendingCollateralGrid");
        xaml.Should().Contain("DirectLendingStatusPostureGrid");
        xaml.Should().Contain("DirectLendingExceptionsGrid");
        xaml.Should().Contain("DirectLendingEvidenceGrid");
        xaml.Should().Contain("DirectLendingCloseBlockersGrid");
        xaml.Should().Contain("DirectLendingServicerStatementsGrid");
        xaml.Should().Contain("DirectLendingLoanInspector");
        xaml.Should().Contain("DirectLendingLoanActionInspector");
        xaml.Should().Contain("ToolTip=\"{Binding PostAccrualTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        viewModel.Should().Contain("public WorkstationTableModel<LoanSummaryDto> LoansTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<DailyAccrualEntryDto> AccrualsTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<CashTransactionDto> CashTransactionsTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<DirectLendingOperationsCollateralCoverageDto> CollateralCoverageTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<DirectLendingOperationsStatusPostureDto> StatusPostureTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<DirectLendingOperationsReconciliationExceptionDto> ExceptionsTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<DirectLendingOperationsEvidenceItemDto> EvidenceTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<DirectLendingOperationsCloseBlockerDto> CloseBlockersTable { get; }");
        viewModel.Should().Contain("public WorkstationTableModel<DirectLendingOperationsServicerStatementDto> ServicerStatementsTable { get; }");
        viewModel.Should().Contain("public InspectorPanelModel SelectedLoanInspector");
        viewModel.Should().Contain("public InspectorPanelModel LoanActionInspector");
        viewModel.Should().Contain("public bool CanPostAccrual");
        viewModel.Should().Contain("\"/api/loans/operations\"");
    }

    private static LoanSummaryDto BuildLoan(LoanStatus status = LoanStatus.Active)
        => new(
            Guid.Parse("8b24c79e-050e-48b5-88bd-b7f9823fa2c1"),
            "Northwind Senior Secured",
            Guid.Parse("7da052be-a6f5-452e-908e-845f063a9354"),
            "Northwind Capital",
            status,
            CurrencyCode.USD,
            25_000_000m,
            12_000_000m,
            4_250.25m,
            0m,
            13_000_000m,
            new DateOnly(2025, 1, 15),
            new DateOnly(2029, 1, 15),
            new DateOnly(2026, 6, 7),
            null);
}
