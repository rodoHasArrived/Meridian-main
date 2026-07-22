#if WINDOWS
using System.Windows.Controls;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StrategyRunLedgerViewModelTests
{
    [Fact]
    public void OpenSelectedSecurityCommand_NavigatesToSecurityMasterWithResolvedSecurityId()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-security-id"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-security-id";
            await viewModel.CurrentLoadTask;

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.Single(line => line.Symbol == "AAPL");
            viewModel.TrialBalanceTable.Rows.Should().BeSameAs(viewModel.TrialBalance);
            viewModel.JournalTable.Rows.Should().BeSameAs(viewModel.Journal);
            viewModel.SelectedTrialBalanceInspector.Title.Should().Be("Securities");
            viewModel.SelectedTrialBalanceInspector.Subtitle.Should().Be("Apple Inc.");
            viewModel.SelectedTrialBalanceInspector.Facts.Should().Contain(f => f.Label == "Symbol" && f.Value == "AAPL");
            viewModel.SelectedSecurityTooltip.Should().Contain("Apple Inc.");
            viewModel.RunDrillInTooltip.Should().Contain("run-ledger-security-id");
            viewModel.CanOpenSelectedSecurity.Should().BeTrue();

            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        });
    }

    [Fact]
    public void OpenSelectedSecurityCommand_FallsBackToSymbolWhenCoverageIsMissing()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-symbol"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-symbol";
            await viewModel.CurrentLoadTask;

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.Single(line => line.Symbol == "TSLA");
            viewModel.OpenSelectedSecurityCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("SecurityMaster");
            navigation.GetBreadcrumbs().First().Parameter.Should().Be("TSLA");
        });
    }

    [Fact]
    public void OpenSelectedSecurityCommand_DisablesForNonSecurityTrialBalanceLines()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var store = new Meridian.Strategies.Storage.StrategyRunStore();
            await store.RecordRunAsync(StrategyRunWorkspaceTestData.BuildRun("run-ledger-no-security"));

            var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
            var workspaceService = new StrategyRunWorkspaceService(
                store,
                new Meridian.Strategies.Services.PortfolioReadService(lookup),
                new Meridian.Strategies.Services.LedgerReadService(lookup));
            var viewModel = new StrategyRunLedgerViewModel(workspaceService, navigation);

            viewModel.Parameter = "run-ledger-no-security";
            await viewModel.CurrentLoadTask;

            viewModel.SelectedTrialBalanceLine = viewModel.TrialBalance.First(line => string.IsNullOrWhiteSpace(line.Symbol));

            viewModel.HasSelectedTrialBalanceLine.Should().BeTrue();
            viewModel.CanOpenSelectedSecurity.Should().BeFalse();
            viewModel.OpenSelectedSecurityCommand.CanExecute(null).Should().BeFalse();
            viewModel.SelectedSecurityTooltip.Should().Be("Select a security-linked trial-balance line before opening Security Master.");
        });
    }

    [Fact]
    public void InitialState_ExposesSelectionGuidanceForDenseLedgerWorkbench()
    {
        var store = new Meridian.Strategies.Storage.StrategyRunStore();
        var lookup = StrategyRunWorkspaceTestData.CreateLookupWithApple();
        var workspaceService = new StrategyRunWorkspaceService(
            store,
            new Meridian.Strategies.Services.PortfolioReadService(lookup),
            new Meridian.Strategies.Services.LedgerReadService(lookup));
        var viewModel = new StrategyRunLedgerViewModel(workspaceService, NavigationService.Instance);

        viewModel.TrialBalanceTable.Title.Should().Be("Run trial balance");
        viewModel.TrialBalanceTable.EmptyTitle.Should().Be("No trial-balance lines retained");
        viewModel.JournalTable.Title.Should().Be("Run journal");
        viewModel.SelectedTrialBalanceInspector.Title.Should().Be("No trial-balance line selected");
        viewModel.HasSelectedTrialBalanceLine.Should().BeFalse();
        viewModel.CanOpenSelectedSecurity.Should().BeFalse();
        viewModel.SelectedSecurityTooltip.Should().Be("Select a trial-balance line before opening Security Master.");
        viewModel.RunDrillInTooltip.Should().Be("Select a retained strategy run before opening related run drill-ins.");
        viewModel.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenPortfolioCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenCashFlowCommand.CanExecute(null).Should().BeFalse();
        viewModel.OpenSelectedSecurityCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void BuildTrialBalanceInspector_ProjectsCanonicalLedgerDimensions()
    {
        var line = new LedgerTrialBalanceLine(
            AccountName: "Receivable",
            AccountType: "Asset",
            Symbol: null,
            FinancialAccountId: "acct-receivable",
            Balance: 1_250m,
            EntryCount: 3,
            AccountScopeDisplayName: "Legacy account scope",
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                SleeveId: "sleeve-credit",
                StrategyId: "strategy-income",
                InvestorId: "investor-lp-1",
                CapitalAccountId: "capital-lp-1",
                InstrumentId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TaxLotId: "taxlot-2026-01",
                CostCenterId: "cost-center-ops",
                CounterpartyId: "counterparty-bank",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "InvestmentOps",
                    ["Class"] = "PrivateCredit"
                },
                OrganizationId: "org-ops",
                PortfolioId: "portfolio-credit",
                BookId: "book-gaap",
                AccountId: "acct-receivable",
                CustomerId: "customer-investor-services",
                VendorId: "vendor-custodian",
                ProjectId: "project-close"));

        var inspector = StrategyRunLedgerViewModel.BuildTrialBalanceInspector(line);

        inspector.Facts.Should().Contain(f => f.Label == "Fund" && f.Value == "fund-alpha");
        inspector.Facts.Should().Contain(f => f.Label == "Entity" && f.Value == "entity-master");
        inspector.Facts.Should().Contain(f => f.Label == "Cost center" && f.Value == "cost-center-ops");
        inspector.Facts.Should().Contain(f => f.Label == "Organization" && f.Value == "org-ops");
        inspector.Facts.Should().Contain(f => f.Label == "Portfolio" && f.Value == "portfolio-credit");
        inspector.Facts.Should().Contain(f => f.Label == "Book" && f.Value == "book-gaap");
        inspector.Facts.Should().Contain(f => f.Label == "Account scope" && f.Value == "acct-receivable");
        inspector.Facts.Should().Contain(f => f.Label == "Customer" && f.Value == "customer-investor-services");
        inspector.Facts.Should().Contain(f => f.Label == "Vendor" && f.Value == "vendor-custodian");
        inspector.Facts.Should().Contain(f => f.Label == "Project" && f.Value == "project-close");
        inspector.Facts.Should().Contain(f =>
            f.Label == "External GL" &&
            f.Value.Contains("Class: PrivateCredit", StringComparison.Ordinal) &&
            f.Value.Contains("Department: InvestmentOps", StringComparison.Ordinal));
        inspector.Facts.Should().Contain(f =>
            f.Label == "Scope" &&
            f.Value.Contains("fund-alpha", StringComparison.Ordinal) &&
            f.Value.Contains("org-ops", StringComparison.Ordinal) &&
            f.Value.Contains("acct-receivable", StringComparison.Ordinal));
        inspector.Facts.Single(f => f.Label == "Scope").Value.Should().NotContain("Legacy account scope");
    }

    [Fact]
    public void RunLedgerPageSource_UsesCompactDenseTablesAndSelectionInspector()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunLedgerPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\StrategyRunLedgerViewModel.cs"));

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("RunLedgerActionStrip");
        xaml.Should().Contain("workstation:DenseDataGridControl");
        xaml.Should().Contain("RunLedgerTrialBalanceGrid");
        xaml.Should().Contain("RunLedgerJournalGrid");
        xaml.Should().Contain("RunLedgerSelectionInspector");
        xaml.Should().Contain("RunLedgerActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("ToolTip=\"{Binding SelectedSecurityTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding RunDrillInTooltip}\"");
        viewModel.Should().Contain("public WorkstationTableModel<LedgerTrialBalanceLine> TrialBalanceTable");
        viewModel.Should().Contain("public WorkstationTableModel<LedgerJournalLine> JournalTable");
        viewModel.Should().Contain("public InspectorPanelModel SelectedTrialBalanceInspector");
        viewModel.Should().Contain("new(\"Fund\", \"Dimensions.FundId\"");
        viewModel.Should().Contain("new(\"Organization\", \"Dimensions.OrganizationId\"");
        viewModel.Should().Contain("new(\"Customer\", \"Dimensions.CustomerId\"");
        viewModel.Should().Contain("new(\"Vendor\", \"Dimensions.VendorId\"");
        viewModel.Should().Contain("new(\"Project\", \"Dimensions.ProjectId\"");
        viewModel.Should().Contain("BuildExternalGlDimensionText");
    }
}
#endif
