using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
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
            ManualJournalEntryTypeDto.Reversal,
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryTypeDto.Distribution,
            ManualJournalEntryTypeDto.Subscription,
            ManualJournalEntryTypeDto.Redemption,
            ManualJournalEntryTypeDto.LpTransfer,
            ManualJournalEntryTypeDto.ManagementFee
        ]);
        harness.ViewModel.ManualJournalEntryTypeRows.Should().Contain(row =>
            row.Status == ManualJournalEntryTypeDto.Amortization.ToString()
            && row.Detail.Contains("Expenses:Amortization Expense", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("Assets:Accumulated Amortization", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.SelectedEntryType = ManualJournalEntryTypeDto.Amortization;
        harness.ViewModel.DraftAmount = 125m;
        await harness.ViewModel.SaveManualJournalDraftAsync();
        await harness.ViewModel.ValidateManualJournalDraftAsync();
        harness.ViewModel.SelectedEntryType = ManualJournalEntryTypeDto.CapitalCall;
        harness.ViewModel.DraftAmount = 250m;
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
                "Liabilities:Deferred Revenue",
                "Equity:Capital Contributions",
                "Equity:Distributions",
                "Assets:Subscription Receivable",
                "Equity:Redemptions",
                "Equity:LP Transfer Out",
                "Equity:LP Transfer In",
                "Expenses:Management Fees"
            ]);
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-manual-adjustment-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-accrued-balance-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-prepaid-expense-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-expense-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-amortization-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-capital-call-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-distribution-v1");
        harness.ViewModel.TemplateRows.Should().Contain(row => row.Name == "desktop-lp-transfer-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-adjustment-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-accrued-balance-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-prepaid-expense-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-expense-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-amortization-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-capital-call-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-distribution-policy-v1");
        harness.ViewModel.PostingRuleRows.Should().Contain(row => row.Name == "manual-lp-transfer-policy-v1");
        harness.ViewModel.AuditRows.Should().NotBeEmpty();
        harness.ViewModel.ManualJournalDraftRows.Should().ContainSingle(row =>
            row.Name.Contains("Capital call", StringComparison.OrdinalIgnoreCase)
            && row.Name.Contains("Capital call entry", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ManualJournalFundEventLedgerRecordRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("+250 USD", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 subledger", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 ledger", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 report", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ManualJournalCapitalAccountRows.Should().ContainSingle(row =>
            row.Name == "capital-account:alpha-fund:default"
            && row.Status == "+250 USD"
            && row.Evidence.Contains("Calls 250 USD", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ManualJournalCapitalAccountSubledgerRows.Should().ContainSingle(row =>
            row.Name == "capital-account:alpha-fund:default"
            && row.Detail.Contains("+250 USD net", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 event(s)", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("evidence categories", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/capital-account-subledger", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ManualJournalFundEventRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("+250 USD net", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ManualJournalLedgerImpactRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase)
            && row.Status == "Review"
            && row.Detail.Contains("250 USD debit", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("2 line", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ManualJournalReportOutputRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCallNotice", StringComparison.OrdinalIgnoreCase)
            && row.Status == "Review"
            && row.Evidence.Contains("evidence", StringComparison.OrdinalIgnoreCase));
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
        reloadedWorkspace.JournalTemplates.Should().Contain(template => template.TemplateId == "desktop-capital-call-v1");
        reloadedWorkspace.PostingRules.Should().Contain(rule => rule.RuleId == "manual-adjustment-policy-v1");
        reloadedWorkspace.PostingRules.Should().Contain(rule => rule.RuleId == "manual-amortization-policy-v1");
        reloadedWorkspace.PostingRules.Should().Contain(rule => rule.RuleId == "manual-capital-call-policy-v1");
        reloadedWorkspace.AuditTrail.Select(static audit => audit.Action)
            .Should()
            .Contain(["chart.upsert", "template.upsert", "posting-rule.upsert", "manual-je.save-draft"]);

        var reloadedDraftStore = new FileManualJournalEntryDraftStore(harness.DraftsPath);
        var reloadedDrafts = await reloadedDraftStore.ListAsync(profile.FundProfileId);
        reloadedDrafts.Should().ContainSingle(draft =>
            draft.EntryType == ManualJournalEntryTypeDto.CapitalCall
            && draft.TreasuryContext != null
            && draft.TreasuryContext.FundEventType == nameof(ManualJournalEntryTypeDto.CapitalCall)
            && draft.TreasuryContext.CapitalAccountId == "capital-account:alpha-fund:default"
            && draft.TotalDebits == 250m
            && draft.TotalCredits == 250m);
    }

    [Fact]
    public async Task LoadAsync_WithPostedPrivateCapitalProjection_ShowsPostedAndPublishedActivityStatus()
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

        var configurationPath = Path.Combine(_root, "accounting-configuration.json");
        var configurationStore = new FileAccountingConfigurationStore(configurationPath);
        var configurationService = new AccountingConfigurationService(configurationStore, configurationStore);
        var projection = CreatePostedPrivateCapitalProjection(profile.FundProfileId);
        var viewModel = new AccountingConfigureViewModel(
            fundContext,
            configurationService,
            new StaticManualJournalEntryWorkbenchService(projection),
            configurationStore);

        await viewModel.LoadAsync();

        viewModel.ManualJournalStatusText.Should().Contain("Private-capital projection");
        viewModel.ManualJournalStatusText.Should().Contain("1 event(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 posted");
        viewModel.ManualJournalStatusText.Should().Contain("1 event record(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 capital account(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 account subledger(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 subledger movement(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 ledger impact(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 report output(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 published");
        viewModel.ManualJournalFundEventLedgerRecordRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase)
            && row.Status == "Posted"
            && row.Detail.Contains("+250 USD", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("0 USD opening", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("+250 USD ending", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 subledger", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 ledger", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 report", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("GovernedReportPack", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("Published", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 provenance", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("readiness Published", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("published with retained report evidence", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("next Open published report via /api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("2 evidence via /api/workstation/evidence/subjects/private-capital-fund-event/", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("approval approval:capital-call-controller via /api/ledger/journal-entry-workbench", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("fundEventId=fund-event%3A", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("capitalAccountId=capital-account%3A", StringComparison.OrdinalIgnoreCase));
        viewModel.ManualJournalCapitalAccountSubledgerRows.Should().ContainSingle(row =>
            row.Name == "capital-account:alpha-fund:lp-001"
            && row.Status == "Published"
            && row.Detail.Contains("0 USD opening", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("+250 USD net", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("+250 USD ending", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 event(s)", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("0 approval queue", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 posted", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 published report", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("readiness Published", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("retained evidence", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("posting-ready ledger impact", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("next Open published report via /api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("5/5 evidence categories ready", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("Report output Ready", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/capital-account-subledger", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("capitalAccountId=capital-account%3A", StringComparison.OrdinalIgnoreCase));
        viewModel.ManualJournalFundEventRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase)
            && row.Status == "Posted"
            && row.Evidence.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        viewModel.ManualJournalLedgerImpactRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase)
            && row.Status == "Posting ready"
            && row.Evidence.Contains("2 line", StringComparison.OrdinalIgnoreCase));
        viewModel.ManualJournalReportOutputRows.Should().ContainSingle(row =>
            row.Name == "Capital Account Statement"
            && row.Status == "Published"
            && row.Detail.Contains("Published", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("evidence", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 provenance", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("manifest-capital-account-statement", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("readiness Published", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("published with retained report evidence", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("next Open published report via /api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("reportOutputId=report-output%3Afund-event%3A", StringComparison.OrdinalIgnoreCase));
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
        harness.ViewModel.ManualJournalCapitalAccountSubledgerRows.Should().BeEmpty();
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

    [Fact]
    public void AccountingConfigurePageSource_ShouldUseCompactActionChrome()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AccountingConfigurePage.xaml"));

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("AccountingConfigureActionStrip");
        xaml.Should().Contain("AccountingConfigureRefreshButton");
        xaml.Should().Contain("AccountingConfigureSeedBaselineButton");
        xaml.Should().Contain("AccountingConfigureActivateButton");
        xaml.Should().Contain("ManualJournalFundEventLedgerRecordGrid");
        xaml.Should().Contain("ManualJournalCapitalAccountGrid");
        xaml.Should().Contain("ManualJournalCapitalAccountSubledgerGrid");
        xaml.Should().Contain("Subledger Route");
        xaml.Should().Contain("ManualJournalFundEventGrid");
        xaml.Should().Contain("ManualJournalLedgerImpactGrid");
        xaml.Should().Contain("ManualJournalReportOutputGrid");
        xaml.Should().Contain("Report Output Route");
        xaml.Should().Contain("ToolTip=\"{Binding StatusText}\"");
        xaml.Should().Contain("ToolTip=\"{Binding ConfigurationDetailText}\"");
        xaml.Should().Contain("ToolTip=\"{Binding CloseDetailText}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("AccountingConfigureTabs");
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

    private static PrivateCapitalActivityProjectionDto CreatePostedPrivateCapitalProjection(string fundProfileId)
    {
        var journalEntryId = Guid.Parse("24a3dc53-d1f4-46e0-9dde-e276d0bb0d9e");
        var effectiveDate = new DateOnly(2026, 6, 1);
        var updatedAtUtc = new DateTimeOffset(2026, 6, 1, 17, 0, 0, TimeSpan.Zero);
        var fundEventId = $"fund-event:{fundProfileId}:capital-call:20260601";
        var capitalAccountId = $"capital-account:{fundProfileId}:lp-001";
        string[] evidenceLinks =
        [
            "evidence://capital-call/notice-20260601",
            "evidence://capital-call/wire-20260601"
        ];

        var fundEvent = new PrivateCapitalFundEventDto(
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            journalEntryId,
            effectiveDate,
            capitalAccountId,
            "investor:lp-001",
            "USD",
            250m,
            250m,
            "Posted capital call",
            $"payment:{fundProfileId}:capital-call:20260601",
            $"settlement:{fundProfileId}:capital-call:20260601",
            evidenceLinks,
            [],
            updatedAtUtc,
            IsPosted: true,
            ApprovalId: "approval:capital-call-controller");
        var capitalAccount = new PrivateCapitalCapitalAccountActivityDto(
            capitalAccountId,
            "investor:lp-001",
            "USD",
            Contributions: 250m,
            Distributions: 0m,
            Subscriptions: 0m,
            Redemptions: 0m,
            ManagementFees: 0m,
            NetActivity: 250m,
            FundEventCount: 1,
            LastEffectiveDate: effectiveDate,
            LastFundEventType: "CapitalCall",
            FundEventIds: [fundEventId]);
        var subledgerEntry = new PrivateCapitalCapitalAccountSubledgerEntryDto(
            $"capital-account-subledger:{fundEventId}",
            capitalAccountId,
            "investor:lp-001",
            "USD",
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            journalEntryId,
            effectiveDate,
            GrossAmount: 250m,
            NetCapitalActivity: 250m,
            RunningNetActivity: 250m,
            Memo: "Posted capital call",
            EvidenceLinks: evidenceLinks,
            ValidationIssues: [],
            UpdatedAtUtc: updatedAtUtc,
            IsPosted: true);
        var ledgerImpact = new PrivateCapitalLedgerImpactDto(
            $"ledger-impact:{fundEventId}",
            journalEntryId,
            fundEventId,
            "CapitalCall",
            capitalAccountId,
            "investor:lp-001",
            ManualJournalEntryStatusDto.Approved,
            effectiveDate,
            "USD",
            TotalDebits: 250m,
            TotalCredits: 250m,
            Imbalance: 0m,
            LineCount: 2,
            IsBalanced: true,
            IsPostingReady: true,
            EvidenceLinks: evidenceLinks,
            Lines:
            [
                new PrivateCapitalLedgerLineImpactDto(
                    "line-debit",
                    "Assets:Cash",
                    AccountingTemplateLineSideDto.Debit,
                    250m,
                    "USD",
                    "entity-alpha",
                    null,
                    null,
                    evidenceLinks[1]),
                new PrivateCapitalLedgerLineImpactDto(
                    "line-credit",
                    "Equity:Capital Contributions",
                    AccountingTemplateLineSideDto.Credit,
                    250m,
                    "USD",
                    "entity-alpha",
                    null,
                    null,
                    evidenceLinks[0])
            ],
            ValidationIssues: []);
        var reportOutputId = $"report-output:{fundEventId}:capital-account-statement";
        var reportOutputRoute = $"/api/ledger/private-capital/report-output?fundProfileId={fundProfileId}&reportOutputId={Uri.EscapeDataString(reportOutputId)}&fundEventId={Uri.EscapeDataString(fundEventId)}&capitalAccountId={Uri.EscapeDataString(capitalAccountId)}&investorId=investor%3Alp-001";
        var reportOutput = new PrivateCapitalReportOutputDto(
            reportOutputId,
            "GovernedReportPack",
            "Capital Account Statement",
            "/workstation/reporting/report-packs/capital-account-statement",
            fundEventId,
            "CapitalCall",
            capitalAccountId,
            "investor:lp-001",
            ManualJournalEntryStatusDto.Approved,
            effectiveDate,
            "USD",
            250m,
            EvidenceLinkCount: evidenceLinks.Length,
            EvidenceLinks: evidenceLinks,
            IsReportReady: true,
            ValidationIssues: [],
            IsPublished: true,
            ReportPackId: "27da2bb6-55dd-4428-a30e-34e516f3381b",
            ReportWorkflowState: "Published",
            PublicationManifestId: "manifest-capital-account-statement",
            RetainedManifestPath: "/retained/report-packs/capital-account-statement.json",
            PublicationEvidenceHash: "sha256:capital-account-statement",
            PublishedAtUtc: updatedAtUtc,
            PublishedBy: "controller",
            ReportLineProvenanceCount: 1,
            ReportOutputRoute: reportOutputRoute,
            ReadinessLabel: "Published",
            ReadinessReason: "The report output is published with retained report evidence and linked posting-ready fund-event impact.",
            NextAction: "Open published report",
            NextActionRoute: reportOutputRoute);
        var fundEventLedgerRecord = new PrivateCapitalFundEventLedgerRecordDto(
            $"fund-event-ledger-record:{fundEventId}",
            fundEventId,
            "CapitalCall",
            capitalAccountId,
            "investor:lp-001",
            ManualJournalEntryStatusDto.Approved,
            journalEntryId,
            effectiveDate,
            "USD",
            250m,
            250m,
            0m,
            250m,
            "Posted capital call",
            $"payment:{fundProfileId}:capital-call:20260601",
            $"settlement:{fundProfileId}:capital-call:20260601",
            ActivityRoute: $"/api/ledger/private-capital/activity?fundProfileId={fundProfileId}&fundEventId={Uri.EscapeDataString(fundEventId)}&capitalAccountId={Uri.EscapeDataString(capitalAccountId)}&investorId=investor%3Alp-001",
            EvidenceRoute: $"/api/workstation/evidence/subjects/private-capital-fund-event/{Uri.EscapeDataString(fundEventId)}/packet",
            ApprovalId: "approval:capital-call-controller",
            ApprovalRoute: $"/api/ledger/journal-entry-workbench?fundProfileId={fundProfileId}&journalEntryId=24a3dc53-d1f4-46e0-9dde-e276d0bb0d9e&approvalId=approval%3Acapital-call-controller",
            IsPosted: true,
            IsPostingReady: true,
            IsReportReady: true,
            IsPublished: true,
            Readiness: PrivateCapitalFundEventLedgerReadinessDto.Published,
            ReadinessLabel: "Published",
            ReadinessReason: "The event is linked to retained evidence, posting-ready ledger impact, capital-account movement, and published report output.",
            NextAction: "Open published report",
            NextActionRoute: reportOutput.ReportOutputRoute,
            EvidenceLinkCount: evidenceLinks.Length,
            CapitalAccountSubledgerEntryCount: 1,
            LedgerImpactCount: 1,
            ReportOutputCount: 1,
            ValidationIssueCount: 0,
            PrimaryReportOutputId: reportOutput.ReportOutputId,
            PrimaryReportOutputType: reportOutput.ReportOutputType,
            PrimaryReportRoute: reportOutput.ReportOutputRoute,
            ReportWorkflowState: reportOutput.ReportWorkflowState,
            PublicationManifestId: reportOutput.PublicationManifestId,
            RetainedManifestPath: reportOutput.RetainedManifestPath,
            ReportLineProvenanceCount: reportOutput.ReportLineProvenanceCount,
            EvidenceLinks: evidenceLinks,
            FundEvent: fundEvent,
            CapitalAccountSubledgerEntries: [subledgerEntry],
            LedgerImpacts: [ledgerImpact],
            ReportOutputs: [reportOutput],
            ValidationIssues: []);
        var capitalAccountSubledger = new PrivateCapitalCapitalAccountSubledgerDto(
            $"capital-account-subledger:{capitalAccountId}:investor:lp-001:usd",
            fundProfileId,
            LedgerBookId: null,
            ProjectedAtUtc: updatedAtUtc,
            capitalAccountId,
            "investor:lp-001",
            "USD",
            ActivityRoute: $"/api/ledger/private-capital/capital-account-subledger?fundProfileId={fundProfileId}&capitalAccountId={Uri.EscapeDataString(capitalAccountId)}&investorId=investor%3Alp-001&currency=USD",
            Contributions: 250m,
            Distributions: 0m,
            Subscriptions: 0m,
            Redemptions: 0m,
            ManagementFees: 0m,
            OpeningNetActivity: 0m,
            EndingNetActivity: 250m,
            NetCapitalActivity: 250m,
            FundEventCount: 1,
            ApprovalQueueCount: 0,
            PostedFundEventCount: 1,
            PublishedReportOutputCount: 1,
            EvidenceLinkCount: evidenceLinks.Length,
            ValidationIssueCount: 0,
            FirstEffectiveDate: effectiveDate,
            LastEffectiveDate: effectiveDate,
            LastFundEventType: "CapitalCall",
            EvidenceLinks: evidenceLinks,
            CapitalAccount: capitalAccount,
            FundEventRecords: [fundEventLedgerRecord],
            SubledgerEntries: [subledgerEntry],
            LedgerImpacts: [ledgerImpact],
            ReportOutputs: [reportOutput],
            ValidationIssues: [],
            Readiness: PrivateCapitalFundEventLedgerReadinessDto.Published,
            ReadinessLabel: "Published",
            ReadinessReason: "All fund events in this capital-account subledger have retained evidence, posting-ready ledger impact, capital-account movement, and published report output.",
            NextAction: "Open published report",
            NextActionRoute: reportOutput.ReportOutputRoute,
            EvidenceCategories:
            [
                new PrivateCapitalEvidenceCategoryDto(
                    "source-support",
                    "Source support",
                    IsReady: true,
                    "Source support is retained for this capital account's fund events.",
                    evidenceLinks.Length,
                    evidenceLinks,
                    ["Source document or retained evidence link"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "capital-account-subledger",
                    "Capital-account subledger",
                    IsReady: true,
                    "Capital-account impacts are represented in the running subledger.",
                    evidenceLinks.Length,
                    evidenceLinks,
                    ["Capital-account impact"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "ledger-impact",
                    "Ledger impact",
                    IsReady: true,
                    "Ledger impacts are linked to this capital account.",
                    evidenceLinks.Length,
                    evidenceLinks,
                    ["Balanced ledger impact", "Ledger line evidence"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "approval-state",
                    "Approval state",
                    IsReady: true,
                    "Approval references are linked to this capital account's fund events.",
                    EvidenceLinkCount: 2,
                    EvidenceLinks:
                    [
                        "approval:capital-call-controller",
                        $"/api/ledger/journal-entry-workbench?fundProfileId={fundProfileId}&journalEntryId=24a3dc53-d1f4-46e0-9dde-e276d0bb0d9e&approvalId=approval%3Acapital-call-controller"
                    ],
                    RequiredEvidence: ["Approval reference"]),
                new PrivateCapitalEvidenceCategoryDto(
                    "report-output",
                    "Report output",
                    IsReady: true,
                    "Governed report outputs are linked to this capital account.",
                    evidenceLinks.Length,
                    evidenceLinks,
                    ["Governed report output"])
            ]);

        return new PrivateCapitalActivityProjectionDto(
            fundProfileId,
            null,
            updatedAtUtc,
            FundEventCount: 1,
            CapitalAccountCount: 1,
            SubmittedFundEventCount: 1,
            ApprovalQueueCount: 0,
            PostedFundEventCount: 1,
            PublishedReportOutputCount: 1,
            NetCapitalActivity: 250m,
            Currency: "USD",
            FundEvents: [fundEvent],
            CapitalAccounts: [capitalAccount],
            CapitalAccountSubledgerEntries: [subledgerEntry],
            LedgerImpacts: [ledgerImpact],
            ReportOutputs: [reportOutput],
            ValidationIssues: [],
            FundEventRecords: [fundEventLedgerRecord],
            CapitalAccountSubledgers: [capitalAccountSubledger]);
    }

    private sealed class StaticManualJournalEntryWorkbenchService(
        PrivateCapitalActivityProjectionDto privateCapitalActivity) : IManualJournalEntryWorkbenchService
    {
        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([privateCapitalActivity.FundProfileId]);
        }

        public Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var projection = privateCapitalActivity with
            {
                FundProfileId = NormalizeFundProfileId(fundProfileId),
                LedgerBookId = ledgerBookId
            };

            return Task.FromResult(new ManualJournalEntryWorkbenchDto(
                projection.FundProfileId,
                ledgerBookId,
                DateTimeOffset.UtcNow,
                LedgerBooks: [],
                ChartOfAccounts: [],
                Drafts: [],
                AuditTrail: [],
                PrivateCapitalActivity: projection));
        }

        public Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(privateCapitalActivity with
            {
                FundProfileId = NormalizeFundProfileId(fundProfileId),
                LedgerBookId = ledgerBookId
            });
        }

        public Task<ManualJournalEntryDraftDto> SaveDraftAsync(
            SaveManualJournalEntryDraftRequest request,
            CancellationToken ct = default)
            => Task.FromException<ManualJournalEntryDraftDto>(
                new NotSupportedException("Static workbench does not persist manual journal drafts."));

        public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
            ValidateManualJournalEntryDraftRequest request,
            CancellationToken ct = default)
            => Task.FromException<ManualJournalEntryDraftDto>(
                new NotSupportedException("Static workbench does not validate manual journal drafts."));

        public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
            SubmitManualJournalEntryApprovalRequest request,
            CancellationToken ct = default)
            => Task.FromException<ManualJournalEntryDraftDto>(
                new NotSupportedException("Static workbench does not submit manual journal approvals."));

        private string NormalizeFundProfileId(string? fundProfileId)
            => string.IsNullOrWhiteSpace(fundProfileId)
                ? privateCapitalActivity.FundProfileId
                : fundProfileId.Trim();
    }
}
