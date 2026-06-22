using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
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
    public void ManualJournalEntryTypePresets_ExposeAndApplyEveryAccountingEventType()
    {
        Directory.CreateDirectory(_root);
        var fundContext = new FundContextService(Path.Combine(_root, "fund-context.json"));
        var harness = CreateHarness(fundContext);
        var expected = new[]
        {
            new PresetExpectation(ManualJournalEntryTypeDto.General, "General adjustment", "Manual accounting adjustment", "Assets:Cash", "Income:Investment Income", "evidence://manual-je/source-document"),
            new PresetExpectation(ManualJournalEntryTypeDto.AccruedBalance, "Accrued balance", "Accrued balance adjustment", "Expenses:Operating Expenses", "Liabilities:Accrued Expenses", "evidence://manual-je/accrual-support"),
            new PresetExpectation(ManualJournalEntryTypeDto.AccruedExpense, "Accrued expense", "Accrued expense entry", "Expenses:Operating Expenses", "Liabilities:Accrued Expenses", "evidence://manual-je/accrued-expense-support"),
            new PresetExpectation(ManualJournalEntryTypeDto.PrepaidExpense, "Prepaid expense", "Prepaid expense entry", "Assets:Prepaid Expenses", "Assets:Cash", "evidence://manual-je/prepaid-expense-support"),
            new PresetExpectation(ManualJournalEntryTypeDto.Expense, "Expense", "Expense recognition entry", "Expenses:Operating Expenses", "Assets:Cash", "evidence://manual-je/expense-support"),
            new PresetExpectation(ManualJournalEntryTypeDto.Amortization, "Amortization", "Amortization entry", "Expenses:Amortization Expense", "Assets:Accumulated Amortization", "evidence://manual-je/amortization-schedule"),
            new PresetExpectation(ManualJournalEntryTypeDto.Deferral, "Deferral", "Deferral entry", "Assets:Cash", "Liabilities:Deferred Revenue", "evidence://manual-je/deferral-schedule"),
            new PresetExpectation(ManualJournalEntryTypeDto.Reclassification, "Reclassification", "Account reclassification entry", "Expenses:Operating Expenses", "Expenses:Investment Fees", "evidence://manual-je/reclassification-support"),
            new PresetExpectation(ManualJournalEntryTypeDto.Reversal, "Reversal", "Reversal entry", "Income:Investment Income", "Assets:Cash", "evidence://manual-je/reversal-approval"),
            new PresetExpectation(ManualJournalEntryTypeDto.CapitalCall, "Capital call", "Capital call entry", "Assets:Cash", "Equity:Capital Contributions", "evidence://manual-je/capital-call-notice"),
            new PresetExpectation(ManualJournalEntryTypeDto.Distribution, "Distribution", "Distribution entry", "Equity:Distributions", "Assets:Cash", "evidence://manual-je/distribution-notice"),
            new PresetExpectation(ManualJournalEntryTypeDto.Subscription, "Subscription", "Subscription entry", "Assets:Subscription Receivable", "Equity:Capital Contributions", "evidence://manual-je/subscription-agreement"),
            new PresetExpectation(ManualJournalEntryTypeDto.Redemption, "Redemption", "Redemption entry", "Equity:Redemptions", "Assets:Cash", "evidence://manual-je/redemption-approval"),
            new PresetExpectation(ManualJournalEntryTypeDto.LpTransfer, "LP transfer", "LP transfer entry", "Equity:LP Transfer Out", "Equity:LP Transfer In", "evidence://manual-je/lp-transfer-agreement"),
            new PresetExpectation(ManualJournalEntryTypeDto.ManagementFee, "Management fee", "Management fee entry", "Expenses:Management Fees", "Assets:Cash", "evidence://manual-je/management-fee-calculation")
        };

        harness.ViewModel.ManualJournalEntryTypeOptions.Should().Equal(expected.Select(static item => item.EntryType));
        harness.ViewModel.ManualJournalEntryTypeOptions.Should().Equal(Enum.GetValues<ManualJournalEntryTypeDto>());
        harness.ViewModel.ManualJournalEntryTypeRows.Should().HaveCount(expected.Length);

        foreach (var item in expected)
        {
            var row = harness.ViewModel.ManualJournalEntryTypeRows.Should()
                .ContainSingle(candidate => candidate.Status == item.EntryType.ToString())
                .Subject;
            row.Name.Should().Be(item.Label);
            row.Detail.Should().Be($"{item.DebitAccountPath} -> {item.CreditAccountPath}");
            row.Evidence.Should().Be(item.EvidenceLink);
            row.Key.Should().Be(item.EntryType.ToString());

            if (item.EntryType == ManualJournalEntryTypeDto.General)
            {
                harness.ViewModel.SelectedEntryType = ManualJournalEntryTypeDto.ManagementFee;
            }

            harness.ViewModel.SelectedEntryType = item.EntryType;
            harness.ViewModel.DraftMemo.Should().Be(item.Memo);
            harness.ViewModel.DraftDebitAccountPath.Should().Be(item.DebitAccountPath);
            harness.ViewModel.DraftCreditAccountPath.Should().Be(item.CreditAccountPath);
            harness.ViewModel.DraftEvidenceLink.Should().Be(item.EvidenceLink);
        }
    }
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
        await harness.ViewModel.BuildPostingCandidateAsync();

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
        harness.ViewModel.CapitalAccountWorkbenchStatusText.Should().Contain("investor account");
        harness.ViewModel.CapitalAccountInvestorRows.Should().ContainSingle(row =>
            row.Name == "capital-account:alpha-fund:default"
            && row.Detail.Contains("+250 USD net", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("evidence categories", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/capital-account-subledger", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.CapitalAccountAllocationRuleRows.Should().Contain(row =>
            row.Name == "Report output"
            && row.Status == "Needs evidence"
            && row.Key.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.CapitalAccountStatementLineageRows.Should().ContainSingle(row =>
            row.Name.Contains("CapitalCallNotice", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("No restatement lineage retained", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.CapitalAccountAuditDrillThroughRows.Should().Contain(row =>
            row.Name.Contains("subledger", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/capital-account-subledger", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.CapitalAccountCapabilityRows.Should().Contain(row =>
            row.Status == "Live" &&
            row.Name == "Investor-level capital account evidence");
        harness.ViewModel.CapitalAccountCapabilityRows.Should().Contain(row =>
            row.Status == "Planned" &&
            row.Name == "Full cap-table administration");
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
            && row.Evidence.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ValidationRows.Should().NotContain(row => row.Name == "manual-je.book-missing");
        harness.ViewModel.ValidationRows.Should().NotContain(row => row.Name == "manual-je.dimension-entity-missing");
        harness.ViewModel.ProviderRows.Should().Contain(row => row.Evidence == "quickbooks-fixture");
        harness.ViewModel.ProviderRows.Should().Contain(row => row.Evidence == "quickbooks" && row.Status == "Planned");
        harness.ViewModel.ExternalGlEvidencePackageRows.Should().Contain(row =>
            row.Name == "External GL import evidence" &&
            row.Status == "Ready" &&
            row.Evidence.Contains("quickbooks-fixture:", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ExternalGlEvidencePackageRows.Should().Contain(row =>
            row.Name == "Meridian ledger evidence" &&
            row.Status == "Missing" &&
            row.Detail.Contains("Load Meridian ledger journal evidence", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ExternalGlRows.Should().NotBeEmpty();
        harness.ViewModel.PostingPostureText.ToLowerInvariant().Should().Contain("source of all ledger truth");
        harness.ViewModel.PolicyRows.Should().Contain(row => row.Evidence == "alpha-fund");
        harness.PostingCandidateService.LastRequest.Should().NotBeNull();
        harness.PostingCandidateService.LastRequest!.FundProfileId.Should().Be("alpha-fund");
        harness.PostingCandidateService.LastRequest.SourceEventType.Should().Be("ManualJournalEntry.CapitalCall");
        harness.PostingCandidateService.LastRequest.Dimensions.Should().NotBeNull();
        harness.PostingCandidateService.LastRequest.Dimensions!.FundId.Should().Be("alpha-fund");
        harness.PostingCandidateService.LastRequest.Dimensions.EntityId.Should().Be("entity-alpha");
        harness.PostingCandidateService.LastRequest.EvidenceLinks.Should().Contain("evidence://manual-je/capital-call-notice");
        harness.PostingCandidateService.LastRequest.EvidenceLinks.Should().Contain("accounting-rule://manual-capital-call-policy-v1/v1");
        harness.ViewModel.PostingCandidateStatusText.Should().Contain("Posting candidate built");
        harness.ViewModel.PostingCandidateDetailText.Should().Contain("posting remains governed by JE lifecycle");
        harness.ViewModel.PostingCandidateRows.Should().Contain(row =>
            row.Name == "manual-capital-call-policy-v1"
            && row.Detail.Contains("correlation", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.PostingCandidateRows.Should().Contain(row =>
            row.Name == "Draft command"
            && row.Status == AccountingPostingApprovalStateDto.Pending.ToString()
            && row.Evidence.Contains("JE lifecycle-gated", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.PostingCandidateRows.Should().Contain(row =>
            row.Name == "Assets:Cash"
            && row.Status == AccountingTemplateLineSideDto.Debit.ToString()
            && row.Detail.Contains("alpha-fund", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("entity-alpha", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.PostingCandidateRows.Should().Contain(row =>
            row.Name == "JOURNAL_DRAFT_APPROVAL_REQUIRED"
            && row.Status == AccountingConfigurationValidationSeverityDto.Info.ToString());
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
        var manualJournalService = new StaticManualJournalEntryWorkbenchService(projection);
        var viewModel = new AccountingConfigureViewModel(
            fundContext,
            configurationService,
            manualJournalService,
            configurationStore,
            capitalAccountWorkbenchService: new CapitalAccountWorkbenchService(manualJournalService));

        await viewModel.LoadAsync();

        viewModel.ManualJournalStatusText.Should().Contain("Private-capital projection");
        viewModel.ManualJournalStatusText.Should().Contain("1 event(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 posted");
        viewModel.ManualJournalStatusText.Should().Contain("1 event record(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 capital account(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 account subledger(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 subledger movement(s)");
        viewModel.ManualJournalStatusText.Should().Contain("1 payment intent(s)");
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
            && row.Evidence.Contains("linked to retained evidence", StringComparison.OrdinalIgnoreCase)
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
        viewModel.CapitalAccountWorkbenchStatusText.Should().Contain("Ready");
        viewModel.CapitalAccountInvestorRows.Should().ContainSingle(row =>
            row.Name == "capital-account:alpha-fund:lp-001"
            && row.Status == "Published"
            && row.Detail.Contains("+250 USD net", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("1 published statement", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/capital-account-subledger", StringComparison.OrdinalIgnoreCase));
        viewModel.CapitalAccountAllocationRuleRows.Should().Contain(row =>
            row.Name == "Report output" &&
            row.Status == "Satisfied" &&
            row.Key.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase));
        viewModel.CapitalAccountStatementLineageRows.Should().ContainSingle(row =>
            row.Name == "Capital Account Statement"
            && row.Status == "Published"
            && row.Detail.Contains("Published", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("No restatement lineage retained", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase));
        viewModel.CapitalAccountAuditDrillThroughRows.Should().Contain(row =>
            row.Status == "Available" &&
            row.Name.Contains("retained manifest", StringComparison.OrdinalIgnoreCase));
        viewModel.CapitalAccountCapabilityRows.Should().Contain(row =>
            row.Status == "Live" &&
            row.Name == "Statement publication, restatement changed-line detail, and restatement evidence lineage");
        viewModel.CapitalAccountCapabilityRows.Should().Contain(row =>
            row.Status == "Planned" &&
            row.Name == "Broad LP portal self-service");
        var paymentIntentRow = viewModel.ManualJournalPaymentIntentRows
            .Should()
            .ContainSingle(row => row.Name == "payment:alpha-fund:capital-call:20260601")
            .Subject;
        paymentIntentRow.Status.Should().Be("Ready, execution deferred");
        paymentIntentRow.Detail.Should().Contain("Inflow 250 USD");
        paymentIntentRow.Detail.Should().Contain("requester fund-controller");
        paymentIntentRow.Evidence.Should().Contain("payee fund:alpha-fund");
        paymentIntentRow.Evidence.Should().Contain("policy Controller approval retained");
        paymentIntentRow.Evidence.Should().Contain("2 source evidence");
        paymentIntentRow.Evidence.Should().Contain("2 approval step");
        paymentIntentRow.Evidence.Should().Contain("1 bank/cash evidence");
        paymentIntentRow.Evidence.Should().Contain("recorded by cash-ops@example.com");
        paymentIntentRow.Evidence.Should().Contain("1 reconciliation link");
        paymentIntentRow.Evidence.Should().Contain("2 audit event");
        paymentIntentRow.Evidence.Should().Contain("Full payment execution is explicitly deferred");
        paymentIntentRow.Key.Should().Contain("/api/workstation/evidence/subjects/payment-intent/");
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
    public async Task ManualJournalLifecycleCommands_RunThroughSharedServiceAndRetainCorrectionDraft()
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
        using var env = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", DesktopAuthenticationSessionTests.HashedDesktopAdminUsersJson())
            .Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null)
            .Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        session.SignIn("desktop-admin", "pw").Succeeded.Should().BeTrue();
        var harness = CreateHarness(fundContext, session);

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.SeedBaselineConfigurationAsync();
        harness.ViewModel.SelectedEntryType = ManualJournalEntryTypeDto.CapitalCall;
        harness.ViewModel.DraftAmount = 275m;
        await harness.ViewModel.SaveManualJournalDraftAsync();
        await harness.ViewModel.SubmitManualJournalDraftAsync();
        await RetainLifecycleEvidenceAsync(harness, profile.FundProfileId);

        await harness.ViewModel.ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Approve);
        harness.ViewModel.ManualJournalLifecycleStatusText.Should().Contain("Approve");
        harness.ViewModel.ManualJournalLifecycleRows.Should().Contain(row =>
            row.Name == JournalEntryLifecycleActionDto.Approve.ToString()
            && row.Status.Contains("Submitted -> Approved", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("evidence", StringComparison.OrdinalIgnoreCase));

        await harness.ViewModel.ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Post);
        harness.ViewModel.ManualJournalLifecycleStatusText.Should().Contain("Post");
        harness.ViewModel.ManualJournalLifecycleRows.Should().Contain(row =>
            row.Name == JournalEntryLifecycleActionDto.Post.ToString()
            && row.Status.Contains("Approved -> Posted", StringComparison.OrdinalIgnoreCase));

        await harness.ViewModel.ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Reverse);
        harness.ViewModel.ManualJournalLifecycleStatusText.Should().Contain("Reverse");
        harness.ViewModel.ManualJournalLifecycleStatusText.Should().Contain("1 correction draft");
        harness.ViewModel.ManualJournalLifecycleRows.Should().Contain(row =>
            row.Name == JournalEntryLifecycleActionDto.Reverse.ToString()
            && row.Status.Contains("Posted -> Reversed", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ManualJournalLifecycleRows.Should().Contain(row =>
            row.Status == ManualJournalEntryStatusDto.Draft.ToString()
            && row.Evidence.Contains("reversal", StringComparison.OrdinalIgnoreCase));

        var reloadedDraftStore = new FileManualJournalEntryDraftStore(harness.DraftsPath);
        var retainedDrafts = await reloadedDraftStore.ListAsync(profile.FundProfileId);
        var reversed = retainedDrafts.Should()
            .ContainSingle(draft => draft.Status == ManualJournalEntryStatusDto.Reversed)
            .Subject;
        retainedDrafts.Should().ContainSingle(draft =>
            draft.EntryType == ManualJournalEntryTypeDto.Reversal
            && draft.ReversalOfJournalEntryId == reversed.JournalEntryId
            && draft.Status == ManualJournalEntryStatusDto.Draft);
    }


    [Fact]
    public async Task ManualJournalLifecycleCommands_WithoutAdminMaintenance_FailClosedBeforeSharedService()
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
        harness.ViewModel.SelectedEntryType = ManualJournalEntryTypeDto.CapitalCall;
        harness.ViewModel.DraftAmount = 275m;
        await harness.ViewModel.SaveManualJournalDraftAsync();
        await harness.ViewModel.SubmitManualJournalDraftAsync();

        await harness.ViewModel.ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Approve);

        harness.ViewModel.ManualJournalLifecycleStatusText.Should().Contain("AdminMaintenance permission is required");
        var reloadedDraftStore = new FileManualJournalEntryDraftStore(harness.DraftsPath);
        var retainedDrafts = await reloadedDraftStore.ListAsync(profile.FundProfileId);
        retainedDrafts.Should().ContainSingle(draft => draft.Status == ManualJournalEntryStatusDto.Submitted);
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
        harness.ViewModel.ManualJournalPaymentIntentRows.Should().BeEmpty();
        harness.ViewModel.ManualJournalLifecycleRows.Should().BeEmpty();
        harness.ViewModel.CapitalAccountInvestorRows.Should().BeEmpty();
        harness.ViewModel.CapitalAccountAllocationRuleRows.Should().BeEmpty();
        harness.ViewModel.CapitalAccountStatementLineageRows.Should().BeEmpty();
        harness.ViewModel.CapitalAccountAuditDrillThroughRows.Should().BeEmpty();
        harness.ViewModel.CapitalAccountCapabilityRows.Should().BeEmpty();
        harness.ViewModel.CapitalAccountWorkbenchStatusText.Should().Contain("Locked");
        harness.ViewModel.ExternalGlEvidencePackageRows.Should().BeEmpty();
        harness.ViewModel.ExternalGlRows.Should().BeEmpty();
        harness.ViewModel.PolicyRows.Should().BeEmpty();
        harness.ViewModel.PostingCandidateRows.Should().BeEmpty();
        harness.ViewModel.PostingCandidateStatusText.Should().Contain("Locked");
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
        xaml.Should().Contain("AccountingPostingCandidateButton");
        xaml.Should().Contain("AccountingPostingCandidateGrid");
        xaml.Should().Contain("Posting Candidate Preview");
        xaml.Should().Contain("ManualJournalDoubleEntryWorkbench");
        xaml.Should().Contain("ManualJournalLifecycleWorkbench");
        xaml.Should().Contain("ManualJournalApproveButton");
        xaml.Should().Contain("ManualJournalPostButton");
        xaml.Should().Contain("ManualJournalReverseButton");
        xaml.Should().Contain("ManualJournalRebookButton");
        xaml.Should().Contain("ManualJournalCloseLockButton");
        xaml.Should().Contain("ManualJournalLifecycleGrid");
        xaml.Should().Contain("MANUAL JOURNAL ENTRY - BALANCED DOUBLE-ENTRY");
        xaml.Should().Contain("ManualJournalDoubleEntryGrid");
        xaml.Should().Contain("ManualJournalDebitAmountPreview");
        xaml.Should().Contain("ManualJournalCreditAmountPreview");
        xaml.Should().Contain("ManualJournalFundEventLedgerRecordGrid");
        xaml.Should().Contain("ManualJournalCapitalAccountGrid");
        xaml.Should().Contain("ManualJournalCapitalAccountSubledgerGrid");
        xaml.Should().Contain("ManualJournalPaymentIntentGrid");
        xaml.Should().Contain("Payment Intent and Cash Evidence");
        xaml.Should().Contain("Capital Account Workbench");
        xaml.Should().Contain("CapitalAccountWorkbenchInvestorGrid");
        xaml.Should().Contain("CapitalAccountWorkbenchAllocationRuleGrid");
        xaml.Should().Contain("CapitalAccountWorkbenchStatementLineageGrid");
        xaml.Should().Contain("CapitalAccountWorkbenchAuditDrillThroughGrid");
        xaml.Should().Contain("CapitalAccountWorkbenchCapabilityGrid");
        xaml.Should().Contain("Subledger Route");
        xaml.Should().Contain("ManualJournalFundEventGrid");
        xaml.Should().Contain("ManualJournalLedgerImpactGrid");
        xaml.Should().Contain("ManualJournalReportOutputGrid");
        xaml.Should().Contain("Report Output Route");
        xaml.Should().Contain("ToolTip=\"{Binding StatusText}\"");
        xaml.Should().Contain("ToolTip=\"{Binding ConfigurationDetailText}\"");
        xaml.Should().Contain("ToolTip=\"{Binding CloseDetailText}\"");
        xaml.Should().Contain("ToolTip=\"{Binding PostingCandidateDetailText}\"");
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

    private AccountingConfigureHarness CreateHarness(FundContextService fundContext, DesktopAuthenticationSession? authenticationSession = null)
    {
        var configurationPath = Path.Combine(_root, "accounting-configuration.json");
        var draftsPath = Path.Combine(_root, "manual-journal-drafts.json");
        var configurationStore = new FileAccountingConfigurationStore(configurationPath);
        var ledgerBookService = new TestLedgerBookService();
        var configurationService = new AccountingConfigurationService(configurationStore, configurationStore, ledgerBookService);
        var draftStore = new FileManualJournalEntryDraftStore(draftsPath);
        var manualJournalService = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            configurationStore);
        var capitalAccountWorkbenchService = new CapitalAccountWorkbenchService(manualJournalService);
        var accountingSystemIntegrationService = new AccountingSystemIntegrationService(
            [new QuickBooksFixtureAccountingProvider()]);
        var policyService = new AccountingPolicyService();
        var postingCandidateService = new TestAccountingPostingCandidateService();
        var viewModel = new AccountingConfigureViewModel(
            fundContext,
            configurationService,
            manualJournalService,
            configurationStore,
            draftStore,
            accountingSystemIntegrationService,
            fundOperationsWorkspaceReadService: null,
            policyService,
            capitalAccountWorkbenchService,
            postingCandidateService,
            authenticationSession: authenticationSession);

        return new AccountingConfigureHarness(viewModel, configurationPath, draftsPath, postingCandidateService);
    }


    private static async Task RetainLifecycleEvidenceAsync(
        AccountingConfigureHarness harness,
        string fundProfileId)
    {
        var draftStore = new FileManualJournalEntryDraftStore(harness.DraftsPath);
        var draft = (await draftStore.ListAsync(fundProfileId))
            .Single(item => item.Status == ManualJournalEntryStatusDto.Submitted);
        var journalId = draft.JournalEntryId.ToString("D");
        var retainedEvidence = draft.EvidenceLinks
            .Concat([
                $"evidence://manual-je/{journalId}/retained-review-approval",
                $"evidence://manual-je/{journalId}/retained-ledger-posting-review",
                $"evidence://manual-je/{journalId}/retained-reversal-correction-review",
                $"evidence://manual-je/{journalId}/retained-period-lock-close-certification"
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var retained = draft with
        {
            EvidenceLinks = retainedEvidence,
            Version = draft.Version + 1
        };

        await draftStore.SaveAsync(retained);
        typeof(AccountingConfigureViewModel)
            .GetField("_selectedDraft", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(harness.ViewModel, retained);
    }

    private sealed record AccountingConfigureHarness(
        AccountingConfigureViewModel ViewModel,
        string ConfigurationPath,
        string DraftsPath,
        TestAccountingPostingCandidateService PostingCandidateService);


    private sealed record PresetExpectation(
        ManualJournalEntryTypeDto EntryType,
        string Label,
        string Memo,
        string DebitAccountPath,
        string CreditAccountPath,
        string EvidenceLink);

    private sealed class TestLedgerBookService : ILedgerBookService
    {
        private readonly LedgerBookDto _book = new(
            Guid.Parse("7e0be005-49e1-46eb-9d4f-89d75e2328bd"),
            "alpha-fund",
            Guid.Parse("9bf8609d-d4d0-4ff6-bf1f-31d2205710d7"),
            Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Fund,
            "Alpha Fund primary book",
            "USD",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture),
            "WPF accounting configure test book",
            AccountingBasisKindDto.Primary,
            "legacy-v1",
            "legacy-v1");

        public Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_book with
            {
                FundProfileId = string.IsNullOrWhiteSpace(request.FundProfileId) ? _book.FundProfileId : request.FundProfileId,
                BaseCurrency = string.IsNullOrWhiteSpace(request.BaseCurrency) ? _book.BaseCurrency : request.BaseCurrency,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? _book.DisplayName : request.DisplayName
            });
        }

        public Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookDto?>(ledgerBookId == _book.LedgerBookId ? _book : null);
        }

        public Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches =
                (string.IsNullOrWhiteSpace(query.FundProfileId) || string.Equals(query.FundProfileId, _book.FundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                (!query.FundStructureNodeId.HasValue || query.FundStructureNodeId == _book.FundStructureNodeId) &&
                (!query.FundStructureNodeKind.HasValue || query.FundStructureNodeKind == _book.FundStructureNodeKind) &&
                (!query.AccountingBasis.HasValue || query.AccountingBasis == _book.AccountingBasis);
            return Task.FromResult<IReadOnlyList<LedgerBookDto>>(matches ? [_book] : []);
        }

        public Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default)
            => Task.FromException<LedgerPeriodDto>(new NotSupportedException("Accounting configure tests do not create ledger periods."));

        public Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerPeriodDto>>([]);
        }

        public Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerPeriodDto>>([]);
        }

        public Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerPeriodSummaryDto?>(null);
        }

        public Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(Guid periodId, CloseLedgerPeriodRequest request, CancellationToken ct = default)
            => Task.FromException<LedgerPeriodCloseResultDto>(new NotSupportedException("Accounting configure tests do not close ledger periods."));
    }

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
        var paymentIntent = new PaymentIntentWorkflowDto(
            fundEvent.PaymentIntentId!,
            fundEvent.SettlementReference,
            fundProfileId,
            null,
            fundEventId,
            journalEntryId,
            "fund-controller",
            updatedAtUtc,
            PaymentIntentWorkflowStatusDto.ExecutionDeferred,
            "Ready, execution deferred",
            "All pre-execution controls are retained before any live payment instruction.",
            "Full payment execution is explicitly deferred in v0.18; this layer only retains intent, control, cash-evidence, reconciliation, and audit history before any bank-side instruction.",
            new PaymentIntentExpectedCashMovementDto(
                fundEvent.PaymentIntentId!,
                PaymentIntentCashDirectionDto.Inflow,
                250m,
                "USD",
                effectiveDate,
                fundEvent.SettlementReference,
                fundEventId,
                "CapitalCall",
                capitalAccountId,
                "investor:lp-001",
                "Capital call for Alpha Fund LP",
                "fund:alpha-fund",
                "fund:alpha-fund / capital-account:alpha-fund:lp-001 / investor:lp-001",
                "Capital call for Alpha Fund LP",
                "Controller approval retained before execution-deferred reliance",
                evidenceLinks),
            UiApiRoutes.WithParam(
                UiApiRoutes.WithParam(
                    UiApiRoutes.WorkstationEvidenceSubjectPacket,
                    "subjectKind",
                    "payment-intent"),
                "subjectId",
                fundEvent.PaymentIntentId!),
            UiApiRoutes.WithQuery(
                UiApiRoutes.LedgerPrivateCapitalActivity,
                $"fundProfileId={Uri.EscapeDataString(fundProfileId)}&paymentIntentId={Uri.EscapeDataString(fundEvent.PaymentIntentId!)}"),
            ApprovalChain:
            [
                new PaymentIntentApprovalStepDto(
                    1,
                    "Requester",
                    "fund-controller",
                    "Approved",
                    updatedAtUtc,
                    fundEventLedgerRecord.ApprovalRoute),
                new PaymentIntentApprovalStepDto(
                    2,
                    "Controller approval",
                    "controller",
                    "Approved",
                    updatedAtUtc,
                    fundEventLedgerRecord.ApprovalRoute)
            ],
            BankEvidence:
            [
                new PaymentIntentBankEvidenceDto(
                    "bank-evidence:alpha-fund:capital-call:20260601",
                    "RetainedCashEvidence",
                    "Retained",
                    "Wire evidence confirms expected capital-call cash movement.",
                    Amount: 250m,
                    Currency: "USD",
                    EffectiveDate: effectiveDate,
                    RecordedAtUtc: updatedAtUtc,
                    ExternalRef: fundEvent.SettlementReference,
                    EvidenceRoute: evidenceLinks[1],
                    RecordedBy: "cash-ops@example.com")
            ],
            ReconciliationLinks:
            [
                new PaymentIntentReconciliationLinkDto(
                    "reconciliation:alpha-fund:capital-call:20260601",
                    "Ready",
                    "Wire evidence reconciles to the posted capital-call ledger record.",
                    EvidenceRoute: evidenceLinks[1])
            ],
            AuditHistory:
            [
                new PaymentIntentAuditEventDto(
                    "payment-intent-requested:alpha-fund:capital-call:20260601",
                    updatedAtUtc,
                    "fund-controller",
                    "payment-intent.requested",
                    "Payment intent was captured from the posted private-capital fund event.",
                    evidenceLinks),
                new PaymentIntentAuditEventDto(
                    "payment-intent-execution-deferred:alpha-fund:capital-call:20260601",
                    updatedAtUtc,
                    "system",
                    "payment-intent.execution-deferred",
                    "Live treasury execution remains deferred.",
                    evidenceLinks)
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
            CapitalAccountSubledgers: [capitalAccountSubledger],
            PaymentIntents: [paymentIntent]);
    }

    private sealed class TestAccountingPostingCandidateService : IAccountingPostingCandidateService
    {
        public PostingRuleJournalCandidateRequestDto? LastRequest { get; private set; }

        public Task<PostingRuleJournalCandidateResultDto> BuildCandidateAsync(
            PostingRuleJournalCandidateRequestDto request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastRequest = request;

            var selectedRuleId = request.SourceEventType.Equals("ManualJournalEntry.CapitalCall", StringComparison.OrdinalIgnoreCase)
                ? "manual-capital-call-policy-v1"
                : "manual-adjustment-policy-v1";
            var generatedLines = new[]
            {
                new GeneratedPostingLineDto(
                    "candidate-debit-cash",
                    "Assets:Cash",
                    AccountingTemplateLineSideDto.Debit,
                    "eventAmount",
                    request.EventAmount,
                    request.Currency,
                    request.Dimensions,
                    "Debit source-event cash."),
                new GeneratedPostingLineDto(
                    "candidate-credit-capital",
                    "Equity:Capital Contributions",
                    AccountingTemplateLineSideDto.Credit,
                    "eventAmount",
                    request.EventAmount,
                    request.Currency,
                    request.Dimensions,
                    "Credit generated capital contribution.")
            };
            var dryRun = new RuleDryRunResultDto(
                request.FundProfileId,
                request.LedgerBookId,
                request.SourceEventType,
                request.EffectiveDate,
                request.EventAmount,
                request.Currency,
                IsPostingBalanced: true,
                selectedRuleId,
                RuleMatches:
                [
                    new AccountingRuleDryRunMatchDto(
                        selectedRuleId,
                        "Capital call policy",
                        "v1",
                        Priority: 10,
                        IsMatched: true,
                        Explanations: ["Matched WPF candidate source event."],
                        ValidationIssues: [])
                ],
                GeneratedLines: [],
                ValidationIssues: [],
                GeneratedPostingLines: generatedLines);
            var commandId = Guid.Parse("7b57c3d4-962f-46c6-82a2-fda9b5dfd58b");
            var journalEntryId = Guid.Parse("3bf39814-d587-4c6f-8ed2-38f22ba77d3b");
            var command = new AccountingPostingCommandDto(
                commandId,
                request.AggregateId,
                request.PeriodId,
                request.EffectiveDate,
                request.AccountingTimestamp,
                $"wpf-candidate:{request.SourceEventType}:{request.SourceEventId}",
                AccountingPostingIntentDto.AutomatedDraft,
                request.SourceEventId,
                request.CorrelationId,
                SourceEventType: request.SourceEventType,
                TreasuryContext: request.TreasuryContext,
                ApprovalState: AccountingPostingApprovalStateDto.Pending,
                ApprovalId: "approval:wpf-candidate",
                OperatorRationale: "WPF candidate preview must be submitted through JE lifecycle.",
                Evidence:
                [
                    new AccountingPostingEvidenceReferenceDto(
                        "candidate-source-evidence",
                        request.EvidenceLinks.FirstOrDefault() ?? "evidence://missing",
                        AccountingPostingEvidenceKindDto.Source,
                        "WPF",
                        request.AccountingTimestamp,
                        request.Actor,
                        SubjectId: request.SourceEventId?.ToString("D"),
                        Description: "Retained source evidence from WPF candidate preview.")
                ]);

            var result = new PostingRuleJournalCandidateResultDto(
                dryRun,
                selectedRuleId,
                "v1",
                generatedLines,
                command,
                journalEntryId,
                request.EventAmount,
                request.EventAmount,
                Imbalance: 0m,
                IsBalanced: true,
                HasBlockingIssues: false,
                CanSubmitForApproval: true,
                CanPostWithoutAdditionalApproval: false,
                request.EvidenceLinks,
                Issues:
                [
                    new PostingRuleJournalCandidateIssueDto(
                        "JOURNAL_DRAFT_APPROVAL_REQUIRED",
                        AccountingConfigurationValidationSeverityDto.Info,
                        "Draft requires controller approval before posting.",
                        BlocksCandidate: false,
                        TargetId: selectedRuleId,
                        SuggestedAction: "Submit through the journal entry lifecycle service.")
                ]);

            return Task.FromResult(result);
        }
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
