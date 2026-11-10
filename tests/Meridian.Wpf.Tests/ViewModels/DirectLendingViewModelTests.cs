using Meridian.Contracts.DirectLending;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class DirectLendingViewModelTests
{
    [Fact]
    public void BuildPortfolioStatusText_ShouldKeepCurrencyGroupsAndUnavailableLegacyTotalsExplicit()
    {
        var mixed = new LoanPortfolioSummaryDto(2, 2, 0, 0, 0, null, null, null, null, null, null, [],
            [new(CurrencyCode.USD, 1_000m, 100m, 0m, 0m, 900m, 0m),
             new(CurrencyCode.EUR, 2_000m, 200m, 0m, 0m, 1_800m, 0m)]);
        DirectLendingViewModel.BuildPortfolioStatusText(mixed).Should()
            .Contain("USD 100; EUR 200").And.NotContain("$");
        DirectLendingViewModel.BuildPortfolioStatusText(mixed with
        {
            CurrencyTotals = null, TotalCurrency = CurrencyCode.EUR, TotalPrincipalOutstanding = 200m
        }).Should().Contain("EUR 200").And.NotContain("$");
        DirectLendingViewModel.BuildPortfolioStatusText(mixed with { CurrencyTotals = null })
            .Should().Contain("unavailable (currency not reported)").And.NotContain("$");
        DirectLendingViewModel.BuildPortfolioStatusText(mixed with
        {
            CurrencyTotals = [new(CurrencyCode.Other, null, null, null, null, null, null)]
        }).Should().Contain("Other unavailable");
    }

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

    // ── PRD-115: apply or refund unapplied cash ─────────────────────────────

    [Fact]
    public void ResolveUnappliedCashCommand_RetryOfTheSameRequest_ReusesTheCommandId()
    {
        var loanId = Guid.NewGuid();
        var key = new DirectLendingViewModel.UnappliedCashCommandKey(loanId, IsRefund: false, 100m, null, new DateOnly(2026, 4, 1));
        var first = DirectLendingViewModel.ResolveUnappliedCashCommand(null, key, Guid.NewGuid);

        var retry = DirectLendingViewModel.ResolveUnappliedCashCommand(first, key with { }, () => throw new InvalidOperationException("must not mint a new id"));

        retry.CommandId.Should().Be(first.CommandId, "the server recognises a committed request whose response was lost");
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("action")]
    [InlineData("reference")]
    [InlineData("loan")]
    [InlineData("day")]
    public void ResolveUnappliedCashCommand_ChangedRequest_StartsANewCommand(string changed)
    {
        var key = new DirectLendingViewModel.UnappliedCashCommandKey(Guid.NewGuid(), IsRefund: true, 100m, "wire-1", new DateOnly(2026, 4, 1));
        var pending = DirectLendingViewModel.ResolveUnappliedCashCommand(null, key, Guid.NewGuid);
        var next = changed switch
        {
            "amount" => key with { Amount = 101m },
            "action" => key with { IsRefund = false },
            "reference" => key with { ExternalRef = "wire-2" },
            "loan" => key with { LoanId = Guid.NewGuid() },
            _ => key with { EffectiveDate = key.EffectiveDate.AddDays(1) },
        };

        DirectLendingViewModel.ResolveUnappliedCashCommand(pending, next, Guid.NewGuid).CommandId.Should().NotBe(pending.CommandId);
    }

    [Theory]
    [InlineData(null, false, false, "500", "", false, "Select a loan")]
    [InlineData(2_000.0, true, false, "500", "", false, "finish loading")]
    [InlineData(2_000.0, false, true, "500", "", false, "already running")]
    [InlineData(0.0, false, false, "500", "", false, "holds no unapplied cash")]
    [InlineData(2_000.0, false, false, "", "", false, "positive amount")]
    [InlineData(2_000.0, false, false, "-5", "", false, "positive amount")]
    [InlineData(2_000.0, false, false, "2500", "", false, "exceeds")]
    [InlineData(2_000.0, false, false, "500", " ", true, "external reference")]
    public void ValidateUnappliedCashAction_ShouldExplainWhyTheActionIsBlocked(
        double? held, bool isDetailLoading, bool isSubmitting, string amountText, string externalRef, bool isRefund, string expectedReason)
    {
        var servicing = held is null ? null : BuildServicing((decimal)held.Value);

        var reason = DirectLendingViewModel.ValidateUnappliedCashAction(
            servicing, isDetailLoading, isSubmitting, amountText, externalRef, isRefund, out var amount);

        reason.Should().Contain(expectedReason);
        amount.Should().Be(0m);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "wire-out-1")]
    public void ValidateUnappliedCashAction_ShouldAllowAPositiveAmountWithinTheHeldCash(bool isRefund, string externalRef)
    {
        var reason = DirectLendingViewModel.ValidateUnappliedCashAction(
            BuildServicing(2_000m), isDetailLoading: false, isSubmitting: false, "2000", externalRef, isRefund, out var amount);

        reason.Should().BeNull();
        amount.Should().Be(2_000m);
    }

    [Fact]
    public void BuildUnappliedCashResultText_ShouldReportWhatMovedAndWhatIsStillHeld()
    {
        DirectLendingViewModel.BuildUnappliedCashResultText(false, 1_500m, BuildServicing(2_000m), true, BuildServicing(1_000m), null, CurrencyCode.USD)
            .Should().Be("Applied 1,000.00 USD to amounts owed; 1,000.00 USD still held.");
        DirectLendingViewModel.BuildUnappliedCashResultText(true, 400m, BuildServicing(1_000m), true, BuildServicing(600m), null, CurrencyCode.USD)
            .Should().Be("Refunded 400.00 USD; 600.00 USD still held.");
        DirectLendingViewModel.BuildUnappliedCashResultText(true, 400m, BuildServicing(1_000m), false, null, "Amount to refund exceeds the held cash.", CurrencyCode.USD)
            .Should().Be("Refund failed: Amount to refund exceeds the held cash.");
    }

    [Fact]
    public void UnappliedCashMessages_UseTheLoansCurrency_NotTheWorkstations()
    {
        // PRD-115: the operator enters an amount in the loan's currency, so a EUR loan's held cash
        // must not be labelled with the workstation's currency symbol.
        DirectLendingViewModel.BuildUnappliedCashResultText(false, 100m, BuildServicing(1_000m), true, BuildServicing(900m), null, CurrencyCode.EUR)
            .Should().Be("Applied 100.00 EUR to amounts owed; 900.00 EUR still held.");
        DirectLendingViewModel.ValidateUnappliedCashAction(BuildServicing(1_000m), false, false, "1500", null, false, out _, CurrencyCode.EUR)
            .Should().Be("The amount exceeds the 1,000.00 EUR held.");
    }

    [Fact]
    public void DirectLendingPageSource_ShouldExposeUnappliedCashActions()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DirectLendingPage.xaml"));

        xaml.Should().Contain("DirectLendingUnappliedCashPanel");
        xaml.Should().Contain("Command=\"{Binding ApplyUnappliedCashCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RefundUnappliedCashCommand}\"");
        xaml.Should().Contain("ToolTip=\"{Binding ApplyUnappliedCashTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding RefundUnappliedCashTooltip}\"");
        xaml.Should().Contain("Text=\"{Binding UnappliedCashAmountText, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("Text=\"{Binding UnappliedCashExternalRef, UpdateSourceTrigger=PropertyChanged}\"");
    }

    private static LoanServicingStateDto BuildServicing(decimal unappliedCash)
        => new(
            Guid.Parse("8b24c79e-050e-48b5-88bd-b7f9823fa2c1"),
            LoanStatus.Active,
            25_000_000m,
            12_000_000m,
            13_000_000m,
            new OutstandingBalancesDto(12_000_000m, 4_250.25m, 0m, 0m, 0m, UnappliedCash: unappliedCash),
            [],
            null,
            null,
            null,
            1,
            [],
            []);

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
