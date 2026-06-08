using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AccountingConfigureViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-accounting-configure-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Scenario_FundAccountingConfigure_LoadsDurableConfigurationManualJournalsExternalEvidenceAndPolicies()
    {
        Directory.CreateDirectory(_root);
        var fundContext = new FundContextService(Path.Combine(_root, "fund-context.json"));
        var profile = await fundContext.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-fund",
            DisplayName: "Alpha Fund",
            LegalEntityName: "Alpha Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "accounting",
            DefaultLandingPageTag: "FundAccountingConfigure",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            EntityIds: ["entity-alpha"],
            SleeveIds: ["sleeve-credit"],
            VehicleIds: ["vehicle-master"],
            IsDefault: true));
        await fundContext.SelectFundProfileAsync(profile.FundProfileId);
        var harness = CreateHarness(fundContext);

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.SeedBaselineConfigurationAsync();
        harness.ViewModel.ManualJournalEntryTypeOptions.Should().Contain([
            ManualJournalEntryTypeDto.AccruedBalance,
            ManualJournalEntryTypeDto.AccruedExpense,
            ManualJournalEntryTypeDto.PrepaidExpense,
            ManualJournalEntryTypeDto.Expense,
            ManualJournalEntryTypeDto.Amortization,
            ManualJournalEntryTypeDto.Deferral,
            ManualJournalEntryTypeDto.Reclassification,
            ManualJournalEntryTypeDto.Reversal
        ]);
        harness.ViewModel.ManualJournalEntryTypeRows.Should().Contain(row =>
            row.Status == ManualJournalEntryTypeDto.Amortization.ToString()
            && row.Detail.Contains("Expenses:Amortization Expense", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("Assets:Accumulated Amortization", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.SelectedEntryType = ManualJournalEntryTypeDto.Amortization;
        harness.ViewModel.DraftAmount = 125m;
        await harness.ViewModel.SaveManualJournalDraftAsync();
        await harness.ViewModel.ValidateManualJournalDraftAsync();
        await harness.ViewModel.RefreshExternalGlAsync();
        await harness.ViewModel.CreateFundScopedPolicyAsync();

        harness.ViewModel.ConfigurationStatusText.Should().Contain("Draft");
        harness.ViewModel.ChartRows.Select(static row => row.Name)
            .Should()
            .Contain([
                "Assets:Cash",
                "Income:Investment Income",
                "Expenses:Investment Fees",
                "Liabilities:Accrued Expenses",
                "Assets:Prepaid Expenses",
                "Expenses:Operating Expenses",
                "Expenses:Amortization Expense",
                "Assets:Accumulated Amortization",
                "Liabilities:Deferred Revenue"
            ]);
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-manual-adjustment-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-accrued-balance-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-prepaid-expense-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-expense-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-amortization-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-adjustment-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-accrued-balance-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-prepaid-expense-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-expense-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-amortization-policy-v1");
        harness.ViewModel.AuditRows.Should().NotBeEmpty();
        harness.ViewModel.ManualJournalDraftRows.Should().ContainSingle(row =>
            row.Name.Contains("Amortization", StringComparison.OrdinalIgnoreCase)
            && row.Name.Contains("Amortization entry", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ValidationRows.Should().Contain(row => row.Name == "manual-je.book-missing");
        harness.ViewModel.ProviderRows.Should().Contain(row => row.Evidence == "quickbooks-fixture");
        harness.ViewModel.ProviderRows.Should().Contain(row => row.Evidence == "quickbooks" && row.Status == "Planned");
        harness.ViewModel.ExternalGlRows.Should().NotBeEmpty();
        harness.ViewModel.PostingPostureText.ToLowerInvariant().Should().Contain("source of all ledger truth");
        harness.ViewModel.PolicyRows.Should().Contain(row => row.Evidence == "alpha-fund");
        harness.ViewModel.StoragePostureText.Should().Contain(nameof(FileAccountingConfigurationStore));
        harness.ViewModel.StoragePostureText.Should().Contain(nameof(FileManualJournalEntryDraftStore));

        var reloadedStore = new FileAccountingConfigurationStore(harness.ConfigurationPath);
        var reloadedService = new AccountingConfigurationService(reloadedStore, reloadedStore);
        var reloadedWorkspace = await reloadedService.GetWorkspaceAsync(profile.FundProfileId);
        reloadedWorkspace.ChartOfAccounts.Should().Contain(node => node.Path == "Assets:Cash");
        reloadedWorkspace.JournalTemplates.Should().Contain(template => template.TemplateId == "desktop-manual-adjustment-v1");
        reloadedWorkspace.JournalTemplates.Should().Contain(template => template.TemplateId == "desktop-amortization-v1");
        reloadedWorkspace.PostingRules.Should().Contain(rule => rule.RuleId == "manual-adjustment-policy-v1");
        reloadedWorkspace.PostingRules.Should().Contain(rule => rule.RuleId == "manual-amortization-policy-v1");
        reloadedWorkspace.AuditTrail.Select(static audit => audit.Action)
            .Should()
            .Contain(["chart.upsert", "template.upsert", "posting-rule.upsert", "manual-je.save-draft"]);

        var reloadedDraftStore = new FileManualJournalEntryDraftStore(harness.DraftsPath);
        var reloadedDrafts = await reloadedDraftStore.ListAsync(profile.FundProfileId);
        reloadedDrafts.Should().ContainSingle(draft =>
            draft.EntryType == ManualJournalEntryTypeDto.Amortization
            && draft.Memo == "Amortization entry"
            && draft.TotalDebits == 125m
            && draft.TotalCredits == 125m);
    }

    [Fact]
    public async Task LoadAsync_WithoutFundContext_FailsClosedAndKeepsRowsEmpty()
    {
        Directory.CreateDirectory(_root);
        var harness = CreateHarness(new FundContextService(Path.Combine(_root, "fund-context.json")));

        await harness.ViewModel.LoadAsync();

        harness.ViewModel.ConfigurationStatusText.Should().Be("Locked");
        harness.ViewModel.ActiveFundText.Should().Be("No fund selected");
        harness.ViewModel.ChartRows.Should().BeEmpty();
        harness.ViewModel.ManualJournalDraftRows.Should().BeEmpty();
        harness.ViewModel.ExternalGlRows.Should().BeEmpty();
        harness.ViewModel.PolicyRows.Should().BeEmpty();
        harness.ViewModel.StatusText.ToLowerInvariant().Should().Contain("unlock accounting configure");
    }

    [Fact]
    public void FundAccountingConfigureRoute_ShouldUseDedicatedConfigurePage()
    {
        var descriptor = ShellNavigationCatalog.GetPage("FundAccountingConfigure");

        descriptor.Should().NotBeNull();
        descriptor!.PageType.Should().Be(typeof(AccountingConfigurePage));
        descriptor.WorkspaceId.Should().Be("accounting");
        descriptor.Subtitle.ToLowerInvariant().Should().Contain("manual journals");
        ShellNavigationCatalog.GetRegisteredPageTypes().Should().Contain(typeof(AccountingConfigurePage));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AccountingConfigureHarness CreateHarness(FundContextService fundContext)
    {
        var configurationPath = Path.Combine(_root, "accounting-configuration.json");
        var draftsPath = Path.Combine(_root, "manual-journal-drafts.json");
        var configurationStore = new FileAccountingConfigurationStore(configurationPath);
        var configurationService = new AccountingConfigurationService(configurationStore, configurationStore);
        var draftStore = new FileManualJournalEntryDraftStore(draftsPath);
        var manualJournalService = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            configurationStore);
        var accountingSystemIntegrationService = new AccountingSystemIntegrationService(
            [new QuickBooksFixtureAccountingProvider()]);
        var policyService = new AccountingPolicyService();
        var viewModel = new AccountingConfigureViewModel(
            fundContext,
            configurationService,
            manualJournalService,
            configurationStore,
            draftStore,
            accountingSystemIntegrationService,
            fundOperationsWorkspaceReadService: null,
            policyService);

        return new AccountingConfigureHarness(viewModel, configurationPath, draftsPath);
    }

    private sealed record AccountingConfigureHarness(
        AccountingConfigureViewModel ViewModel,
        string ConfigurationPath,
        string DraftsPath);
}
