using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

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
        // ClosingEntry is automation-only (period-close rolls temporary balances into retained
        // earnings) and is intentionally excluded from manual journal presets; every other entry
        // type must be exposed as a manual preset.
        harness.ViewModel.ManualJournalEntryTypeOptions.Should().Equal(
            Enum.GetValues<ManualJournalEntryTypeDto>()
                .Where(static type => type != ManualJournalEntryTypeDto.ClosingEntry));
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
        harness.ViewModel.SetupReadinessRows.Should().Contain(row =>
            row.Name == "Selected ledger book"
            && row.Status == "Alpha Fund primary book"
            && row.Detail.Contains("Primary basis", StringComparison.OrdinalIgnoreCase)
            && row.Evidence == "7e0be005-49e1-46eb-9d4f-89d75e2328bd");
        harness.ViewModel.SetupReadinessRows.Should().Contain(row =>
            row.Name == "Activation readiness"
            && row.Status == "Ready"
            && row.Detail.Contains("no critical blockers", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.LedgerBookAdministrationText.Should()
            .Be("1 ledger book(s) registered; selected Alpha Fund primary book.");
        harness.ViewModel.LedgerBookRows.Should().ContainSingle(row =>
            row.Name == "Alpha Fund primary book"
            && row.Status == "Selected"
            && row.Detail.Contains("Primary basis", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("USD", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("alpha-fund / Fund 9bf8609d-d4d0-4ff6-bf1f-31d2205710d7", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("Updated 2026-06-01", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("WPF accounting configure test book", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("legacy-v1/legacy-v1", StringComparison.OrdinalIgnoreCase)
            && row.Key == "7e0be005-49e1-46eb-9d4f-89d75e2328bd");
        harness.ViewModel.ProductionReadinessStatusText.Should().Contain("/100");
        harness.ViewModel.ProductionReadinessDetailText.Should().Contain("component");
        harness.ViewModel.ProductionReadinessLedgerBookText.Should().Contain("book");
        harness.ViewModel.ProductionReadinessExternalGlText.Should().Contain("live posting disabled");
        harness.ViewModel.TenantAdministrationProfileStatusText.Should().Contain("No retained tenant administration setup profile");
        harness.ViewModel.TenantAdministrationProfileScopeText.Should().Be("Tenant alpha-fund; company Alpha Fund LP.");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Tenant config" && row.Status == "Missing");
        harness.ViewModel.ProductionReadinessComponentRows.Should().Contain(row =>
            row.Name == "Ledger books"
            && row.Detail.Contains("ledger book", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessComponentRows.Should().Contain(row =>
            row.Name == "Rules Studio"
            && row.Detail.Contains("rule", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessComponentRows.Should().Contain(row =>
            row.Name == "External GL"
            && row.Detail.Contains("certified", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessWorkflowRows.Should().HaveCount(10);
        harness.ViewModel.ProductionReadinessWorkflowRows.Should().Contain(row =>
            row.Name == "Ledger-book scope"
            && row.Status == "Complete"
            && row.Detail.Contains("7e0be005-49e1-46eb-9d4f-89d75e2328bd", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessWorkflowRows.Should().Contain(row =>
            row.Name == "Posting rules"
            && row.Status == "Missing"
            && row.Detail.Contains("Retain posting-rules", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessWorkflowRows.Should().Contain(row =>
            row.Name == "Close/reporting"
            && row.Key == "ledger-book-workflows:close-reporting");
        harness.ViewModel.ProductionReadinessDimensionalControlRows.Should().HaveCount(9);
        harness.ViewModel.ProductionReadinessDimensionalControlRows.Should().Contain(row =>
            row.Name == "Ledger-book scope"
            && row.Status == "Complete"
            && row.Detail.Contains("7e0be005-49e1-46eb-9d4f-89d75e2328bd", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessDimensionalControlRows.Should().Contain(row =>
            row.Name == "Journal filters"
            && row.Status == "Missing"
            && row.Detail.Contains("journal-query", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessDimensionalControlRows.Should().Contain(row =>
            row.Name == "Report package provenance"
            && row.Key == "dimensional-reporting:report-package");
        harness.ViewModel.ProductionReadinessIssueRows.Should().Contain(row =>
            row.Name == "ledger-books.external-gl-not-certified"
            && row.Status == AccountingConfigurationValidationSeverityDto.Critical.ToString());
        harness.ViewModel.ProductionReadinessGapRows.Should().HaveCount(5);
        harness.ViewModel.ProductionReadinessGapRows.Should().Contain(row =>
            row.Name == "Configurable multi-ledger accounting"
            && row.Status.Contains("Blocked", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("Ledger Books", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("ledger-books.workflow-evidence-missing", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("Attach retained evidence identifying the selected ledger book", StringComparison.OrdinalIgnoreCase)
            && row.Key.Contains("multi-ledger-native-workflows", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessGapRows.Should().Contain(row =>
            row.Name == "Enterprise accounting configuration studio"
            && row.Evidence.Contains("tenant-admin", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessGapRows.Should().Contain(row =>
            row.Name == "External GL guarded integration"
            && row.Evidence.Contains("live external posting disabled", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessGapRows.Should().Contain(row =>
            row.Name == "Dimensional ledger and reporting"
            && row.Detail.Contains("Dimensional Accounting", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessGapRows.Should().Contain(row =>
            row.Name == "Production controls and rollout hardening"
            && row.Evidence.Contains("migration", StringComparison.OrdinalIgnoreCase));
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
        harness.ViewModel.ChartAccountNodeId = "wpf-management-fees";
        harness.ViewModel.ChartAccountPath = "Expenses:Management Fees:Configured";
        harness.ViewModel.ChartAccountName = "Configured Management Fees";
        harness.ViewModel.ChartAccountType = "Expense";
        harness.ViewModel.ChartAccountParentPath = "Expenses:Management Fees";
        harness.ViewModel.ChartAccountFinancialAccountId = "gl-6100-management-fees";
        harness.ViewModel.ChartAccountEvidenceText = "evidence://wpf/chart/management-fees";
        harness.ViewModel.CanSaveChartAccount.Should().BeTrue();
        harness.ViewModel.SaveChartAccountCommand.CanExecute(null).Should().BeTrue();

        harness.ViewModel.ChartAccountEvidenceText = "   ";
        harness.ViewModel.CanSaveChartAccount.Should().BeFalse();
        harness.ViewModel.SaveChartAccountCommand.CanExecute(null).Should()
            .BeFalse("chart authoring commands must share the retained-evidence guard");
        harness.ViewModel.ChartAccountEvidenceText = "evidence://wpf/chart/management-fees";
        harness.ViewModel.SaveChartAccountCommand.CanExecute(null).Should().BeTrue();

        await harness.ViewModel.SaveChartAccountAsync();

        harness.ViewModel.ChartAccountSetupStatusText.Should().Contain("Chart account Expenses:Management Fees:Configured saved");
        harness.ViewModel.ChartRows.Should().Contain(row =>
            row.Name == "Expenses:Management Fees:Configured"
            && row.Status == "Expense"
            && row.Detail == "Configured Management Fees"
            && row.Evidence == "Active");
        var retainedWorkspace = await harness.ConfigurationService.GetWorkspaceAsync(profile.FundProfileId, harness.LedgerBookService.Book.LedgerBookId);
        retainedWorkspace.ChartOfAccounts.Should().Contain(node =>
            node.NodeId == "wpf-management-fees"
            && node.Path == "Expenses:Management Fees:Configured"
            && node.ParentPath == "Expenses:Management Fees"
            && node.FinancialAccountId == "gl-6100-management-fees");
        retainedWorkspace.AuditTrail.Should().Contain(audit =>
            audit.Action == "chart.upsert"
            && audit.LedgerBookId == harness.LedgerBookService.Book.LedgerBookId
            && audit.EvidenceLinks.Contains("evidence://wpf/chart/management-fees"));
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
        harness.ViewModel.RulesStudioStatusText.Should().Contain("active rule");
        harness.ViewModel.RulesStudioDetailText.Should().Contain("effective-dated");
        harness.ViewModel.RulesStudioRuleRows.Should().Contain(row =>
            row.Name == "manual-capital-call-policy-v1"
            && row.Detail.Contains("ManualJournalEntry.CapitalCall", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("generated line", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.PostingRuleDraftRuleId = "wpf-configured-fee-accrual-rule";
        harness.ViewModel.PostingRuleDraftDisplayName = "WPF configured fee accrual";
        harness.ViewModel.PostingRuleDraftSourceEventType = "ManualJournalEntry.ManagementFee";
        harness.ViewModel.PostingRuleDraftTemplateId = "desktop-management-fee-v1";
        harness.ViewModel.PostingRuleDraftDescription = "Retained management-fee accrual rule from desktop Configure.";
        harness.ViewModel.PostingRuleDraftPriority = 35;
        harness.ViewModel.PostingRuleDraftEvidenceText = "evidence://wpf/accounting/rules/management-fee-accrual";
        harness.ViewModel.CanSavePostingRuleDraft.Should().BeTrue();
        harness.ViewModel.SavePostingRuleDraftCommand.CanExecute(null).Should().BeTrue();

        harness.ViewModel.PostingRuleDraftTemplateId = "   ";
        harness.ViewModel.CanSavePostingRuleDraft.Should().BeFalse();
        harness.ViewModel.SavePostingRuleDraftCommand.CanExecute(null).Should()
            .BeFalse("posting-rule authoring commands must share the template and evidence guard");
        harness.ViewModel.PostingRuleDraftTemplateId = "desktop-management-fee-v1";
        harness.ViewModel.SavePostingRuleDraftCommand.CanExecute(null).Should().BeTrue();

        await harness.ViewModel.SavePostingRuleDraftAsync();

        harness.ViewModel.PostingRuleDraftStatusText.Should().Contain("wpf-configured-fee-accrual-rule");
        harness.ViewModel.PostingRuleRows.Should().Contain(row =>
            row.Name == "wpf-configured-fee-accrual-rule"
            && row.Status == "ManualJournalEntry.ManagementFee"
            && row.Detail == "WPF configured fee accrual"
            && row.Evidence == "desktop-management-fee-v1");
        harness.ViewModel.RulesStudioRuleRows.Should().Contain(row =>
            row.Name == "wpf-configured-fee-accrual-rule"
            && row.Detail.Contains("ManualJournalEntry.ManagementFee", StringComparison.OrdinalIgnoreCase));
        var retainedRuleWorkspace = await harness.ConfigurationService.GetWorkspaceAsync(profile.FundProfileId, harness.LedgerBookService.Book.LedgerBookId);
        var retainedRule = retainedRuleWorkspace.PostingRules.Should()
            .ContainSingle(rule => rule.RuleId == "wpf-configured-fee-accrual-rule")
            .Subject;
        retainedRule.DisplayName.Should().Be("WPF configured fee accrual");
        retainedRule.TemplateId.Should().Be("desktop-management-fee-v1");
        retainedRule.Priority.Should().Be(35);
        retainedRule.RequiresPromotionApproval.Should().BeTrue();
        retainedRule.Scope.Should().NotBeNull();
        retainedRule.Scope!.FundId.Should().Be(profile.FundProfileId);
        retainedRule.Scope.BookId.Should().Be(harness.LedgerBookService.Book.LedgerBookId.ToString("D"));
        retainedRuleWorkspace.AuditTrail.Should().Contain(audit =>
            audit.Action == "posting-rule.upsert"
            && audit.LedgerBookId == harness.LedgerBookService.Book.LedgerBookId
            && audit.EvidenceLinks.Contains("evidence://wpf/accounting/rules/management-fee-accrual"));
        harness.ViewModel.RulesStudioPromotionRows.Should().NotBeNull();
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
        var reloadedWorkspace = await reloadedService.GetWorkspaceAsync(
            profile.FundProfileId,
            harness.PostingCandidateService.LastRequest.LedgerBookId);
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
            && draft.PeriodId == harness.LedgerBookService.OpenPeriod.PeriodId.ToString("D")
            && draft.TotalDebits == 250m
            && draft.TotalCredits == 250m);
    }

    [Fact]
    public async Task ProductionCertificationProfile_LoadedTypedEvidenceRemainsReadOnlyInDesktopEditor()
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
            IsDefault: true));
        await fundContext.SelectFundProfileAsync(profile.FundProfileId);
        var harness = CreateHarness(fundContext);
        var loadedProfile = CreateGovernedProductionCertificationProfile(
            harness.LedgerBookService.Book.LedgerBookId);
        await harness.ProductionCertificationProfileStore.UpsertAsync(
            new AccountingProductionCertificationProfileUpsertRequestDto(
                loadedProfile,
                "governed-certification-controller",
                "seed-governed-production-certification",
                EvidenceLinks: loadedProfile.EvidenceReferences,
                RetainedEvidence: loadedProfile.RetainedEvidence));

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.SeedBaselineConfigurationAsync();
        var retainedBefore = await harness.ProductionCertificationProfileStore.GetAsync(
            "alpha-fund",
            "Alpha Fund LP",
            "alpha-fund",
            harness.LedgerBookService.Book.LedgerBookId);

        harness.ViewModel.PostingRulesLedgerBookNativeCertified.Should().BeTrue();
        harness.ViewModel.JournalLifecycleLedgerBookNativeCertified.Should().BeFalse();
        harness.ViewModel.CanSaveProductionCertificationProfile.Should().BeFalse();
        harness.ViewModel.SaveProductionCertificationProfileCommand.CanExecute(null).Should().BeFalse();
        harness.ViewModel.ProductionCertificationSaveGuidanceText.Should()
            .Contain("cannot create, change, or revoke production certification");

        harness.ViewModel.ProductionCertificationEvidenceText =
            "evidence://diagnostic-index/alpha-fund/production-certification";

        await harness.ViewModel.SaveProductionCertificationProfileAsync();

        var retained = await harness.ProductionCertificationProfileStore.GetAsync(
            "alpha-fund",
            "Alpha Fund LP",
            "alpha-fund",
            harness.LedgerBookService.Book.LedgerBookId);
        retained.Should().NotBeNull();
        retained.Should().BeEquivalentTo(retainedBefore,
            "diagnostic references must never overwrite a retained certification snapshot");
        retained!.EvidenceReferences.Should().NotContain(
            "evidence://diagnostic-index/alpha-fund/production-certification");
        harness.ViewModel.ProductionCertificationProfileStatusText.Should()
            .Contain("cannot create, change, or revoke production certification");

        var secondProfile = await fundContext.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "beta-fund",
            DisplayName: "Beta Fund",
            LegalEntityName: "Beta Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "accounting",
            DefaultLandingPageTag: "FundAccountingConfigure",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            EntityIds: ["entity-beta"],
            IsDefault: false));
        await fundContext.SelectFundProfileAsync(secondProfile.FundProfileId);

        await harness.ViewModel.LoadAsync();

        harness.ViewModel.PostingRulesLedgerBookNativeCertified.Should().BeFalse();
        harness.ViewModel.JournalLifecycleLedgerBookNativeCertified.Should().BeFalse();
        harness.ViewModel.ProductionCertificationEvidenceText.Should().BeEmpty();
    }

    [Fact]
    public async Task ProductionCertificationProfile_StringOnlyEvidenceCannotEnableOrSave()
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
            IsDefault: true));
        await fundContext.SelectFundProfileAsync(profile.FundProfileId);
        var harness = CreateHarness(fundContext);

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.SeedBaselineConfigurationAsync();

        harness.ViewModel.ProductionCertificationEvidenceText =
            "evidence://diagnostic-index/alpha-fund/string-only-production-certification";
        harness.ViewModel.PostingRulesLedgerBookNativeCertified = true;
        harness.ViewModel.JournalLifecycleLedgerBookNativeCertified = true;
        harness.ViewModel.PeriodReportDimensionQueriesCertified = true;

        harness.ViewModel.CanSaveProductionCertificationProfile.Should().BeFalse();
        harness.ViewModel.SaveProductionCertificationProfileCommand.CanExecute(null).Should().BeFalse();
        harness.ViewModel.ProductionCertificationSaveGuidanceText.Should()
            .Contain("URI and reference text is diagnostic only");

        await harness.ViewModel.SaveProductionCertificationProfileAsync();

        harness.ViewModel.ProductionCertificationProfileStatusText.Should()
            .Contain("URI and reference text is diagnostic only");
        var retained = await harness.ProductionCertificationProfileStore.GetAsync(
            "alpha-fund",
            "Alpha Fund LP",
            "alpha-fund",
            harness.LedgerBookService.Book.LedgerBookId);
        retained.Should().BeNull("string locators cannot stand in for complete typed retained evidence authority");
    }

    [Fact]
    public async Task TenantAdministrationProfile_SaveRetainsSharedControlsAndRefreshesProductionReadiness()
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
            IsDefault: true));
        await fundContext.SelectFundProfileAsync(profile.FundProfileId);
        var harness = CreateHarness(fundContext);
        await harness.MigrationRunArtifactStore.UpsertAsync(new AccountingMigrationRunArtifactUpsertRequestDto(
            new AccountingMigrationRunArtifactDto(
                "migration-run-dimensional-backfill-alpha-fund",
                AccountingMigrationRunKindDto.DimensionalBackfill,
                AccountingMigrationRunStatusDto.Certified,
                DateTimeOffset.Parse("2026-06-30T12:00:00Z", CultureInfo.InvariantCulture),
                CompletedAtUtc: DateTimeOffset.Parse("2026-06-30T12:15:00Z", CultureInfo.InvariantCulture),
                MigratedRecordCount: 128,
                IssueCount: 0,
                EvidenceReferences: ["evidence://migration/tenant/alpha-fund/company/Alpha Fund LP/fund/alpha-fund/ledger-book/7e0be005-49e1-46eb-9d4f-89d75e2328bd/dimensional-backfill/certified"],
                FundProfileId: "alpha-fund",
                LedgerBookId: Guid.Parse("7e0be005-49e1-46eb-9d4f-89d75e2328bd"),
                Summary: "Dimensional backfill certified for Alpha Fund primary book.",
                Dimensions: new LedgerDimensionSetDto(
                    FundId: "alpha-fund",
                    EntityId: "entity-alpha",
                    SleeveId: "sleeve-credit",
                    StrategyId: "strategy-income",
                    InvestorId: "investor-lp",
                    CapitalAccountId: "capital-account-alpha",
                    InstrumentId: Guid.Parse("0f92e649-013f-4e7f-99bf-2b14396701e8"),
                    TaxLotId: "tax-lot-alpha",
                    BookId: "7e0be005-49e1-46eb-9d4f-89d75e2328bd",
                    AccountId: "account-cash",
                    OrganizationId: "organization-alpha",
                    PortfolioId: "portfolio-credit",
                    CustomerId: "customer-alpha",
                    VendorId: "vendor-admin",
                    ProjectId: "project-ledger-hardening",
                    CostCenterId: "fund-accounting",
                    CounterpartyId: "administrator",
                    ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Department"] = "FundAccounting"
                    }),
                TenantId: "alpha-fund",
                CompanyId: "Alpha Fund LP"),
            "controller",
            CorrelationId: "wpf-dimensional-backfill-alpha-fund",
            EvidenceLinks: ["approval:dimensional-backfill:alpha-fund"]));
        await harness.MigrationRunWorkerPlanStore.UpsertAsync(new AccountingMigrationRunWorkerPlanDto(
            "worker-plan-historical-alpha-fund",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill,
            "alpha-fund",
            Guid.Parse("7e0be005-49e1-46eb-9d4f-89d75e2328bd"),
            SourceRecordCount: 275,
            MigratedRecordCount: 275,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "alpha-fund",
                EntityId: "entity-alpha",
                SleeveId: "sleeve-credit",
                StrategyId: "strategy-income",
                InvestorId: "investor-lp",
                CapitalAccountId: "capital-account-alpha",
                InstrumentId: Guid.Parse("0f92e649-013f-4e7f-99bf-2b14396701e8"),
                TaxLotId: "tax-lot-alpha",
                BookId: "7e0be005-49e1-46eb-9d4f-89d75e2328bd",
                AccountId: "account-cash",
                CostCenterId: "fund-accounting",
                CounterpartyId: "administrator",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "FundAccounting"
                }),
            EvidenceReferences: ["evidence://migration-worker-plan/historical/alpha-fund"],
            TenantId: "alpha-fund",
            CompanyId: "Alpha Fund LP",
            Summary: "Historical journal worker plan reconciles source and migrated rows."));

        await harness.ViewModel.LoadAsync();
        harness.ViewModel.TenantAdministrationProfileScopeText.Should()
            .Be("Tenant alpha-fund; company Alpha Fund LP.");
        harness.ViewModel.CanSaveTenantAdministrationProfile.Should().BeTrue();
        harness.ViewModel.SaveTenantAdministrationProfileCommand.CanExecute(null).Should().BeTrue();

        harness.ViewModel.ApprovalQueueStudioConfigured = true;
        harness.ViewModel.ApprovalQueueId = string.Empty;
        harness.ViewModel.CanSaveTenantAdministrationProfile.Should()
            .BeFalse("a configured approval queue studio must have a typed queue payload before desktop save is enabled");
        harness.ViewModel.SaveTenantAdministrationProfileCommand.CanExecute(null).Should()
            .BeFalse("the command must share the same approval queue payload guard as the desktop save button");

        harness.ViewModel.ApprovalQueueId = "configuration-promotion-queue";
        harness.ViewModel.ApprovalQueueDisplayName = "Configuration promotion queue";
        harness.ViewModel.ApprovalQueueWorkflowKind = "ConfigurationPromotion";
        harness.ViewModel.ApprovalQueueRequiredApprovalRole = "Controller";
        harness.ViewModel.ApprovalQueueRequiredApprovalCount = 2;
        harness.ViewModel.ApprovalQueueSegregationPolicy = "Preparer cannot approve own configuration change.";
        harness.ViewModel.ApprovalQueueEvidenceRequirement = "approval-queue;configuration-approval;segregation-review";
        harness.ViewModel.DimensionMappingStudioConfigured = true;
        harness.ViewModel.DimensionMappingId = string.Empty;
        harness.ViewModel.CanSaveTenantAdministrationProfile.Should()
            .BeFalse("a configured dimension mapping studio must have a typed mapping payload before desktop save is enabled");
        harness.ViewModel.SaveTenantAdministrationProfileCommand.CanExecute(null).Should()
            .BeFalse("the command must share the same dimension mapping payload guard as the desktop save button");

        harness.ViewModel.DimensionMappingId = "qbo-canonical-dimension-map";
        harness.ViewModel.DimensionMappingDisplayName = "QuickBooks canonical dimension map";
        harness.ViewModel.DimensionMappingProviderId = "quickbooks-fixture";
        harness.ViewModel.DimensionMappingMeridianDimensionsText =
            $"fundId={profile.FundProfileId}\nbookId={harness.LedgerBookService.Book.LedgerBookId:D}\ncustomerId=investor-alpha\nProject=direct-lending";
        harness.ViewModel.DimensionMappingProviderDimensionsText =
            $"bookId=Book:{harness.LedgerBookService.Book.LedgerBookId:D}\ncustomerId=qbo-customer-alpha\nProject=qbo-project-credit";
        harness.ViewModel.DimensionMappingEvidenceRequirement = "dimension-mapping;external-dimension-mapping;gl-dimension-mapping";
        harness.ViewModel.CanSaveTenantAdministrationProfile.Should().BeTrue();
        harness.ViewModel.SaveTenantAdministrationProfileCommand.CanExecute(null).Should().BeTrue();

        harness.ViewModel.TenantScopeConfigured = true;
        harness.ViewModel.AdminRoleProfileConfigured = true;
        harness.ViewModel.ScopedAccessPoliciesConfigured = true;
        harness.ViewModel.ReportingGroupsConfigured = true;
        harness.ViewModel.AccountingAdminSurfaceConfigured = true;
        harness.ViewModel.WpfAccountingAdminSurfaceConfigured = true;
        harness.ViewModel.ChartAdministrationStudioConfigured = true;
        harness.ViewModel.RuleTestPromotionStudioConfigured = true;
        harness.ViewModel.CloseSetupStudioConfigured = true;
        harness.ViewModel.ProviderMappingStudioConfigured = true;
        harness.ViewModel.TenantCompanyReportGroupSetupStudioConfigured = true;
        harness.ViewModel.LedgerBookAdministrationStudioConfigured = true;
        harness.ViewModel.TenantAdministrationEvidenceText =
            "evidence://tenant-admin/alpha-fund/Alpha Fund LP/setup\nEVIDENCE://tenant-admin/alpha-fund/Alpha Fund LP/setup\nevidence://tenant-admin/full/alpha-fund/Alpha Fund LP/control-set\nevidence://tenant-admin/alpha-fund/Alpha Fund LP/operator-surface\nevidence://tenant-admin/alpha-fund/Alpha Fund LP/dimension-mapping";

        await harness.ViewModel.SaveTenantAdministrationProfileAsync();

        var retained = await harness.TenantAdministrationProfileStore.GetAsync("alpha-fund", "Alpha Fund LP");
        retained.Should().NotBeNull();
        retained!.TenantScopeConfigured.Should().BeTrue();
        retained.AdminRoleProfileConfigured.Should().BeTrue();
        retained.ScopedAccessPoliciesConfigured.Should().BeTrue();
        retained.ReportingGroupsConfigured.Should().BeTrue();
        retained.AccountingAdminSurfaceConfigured.Should().BeTrue();
        retained.BrowserAccountingAdminSurfaceConfigured.Should().BeFalse();
        retained.WpfAccountingAdminSurfaceConfigured.Should().BeTrue();
        retained.ChartAdministrationStudioConfigured.Should().BeTrue();
        retained.RuleTestPromotionStudioConfigured.Should().BeTrue();
        retained.CloseSetupStudioConfigured.Should().BeTrue();
        retained.ProviderMappingStudioConfigured.Should().BeTrue();
        retained.TenantCompanyReportGroupSetupStudioConfigured.Should().BeTrue();
        retained.UpdatedBy.Should().Be("desktop-controller");
        retained.CorrelationId.Should().StartWith("wpf-accounting-tenant-admin-");
        retained.LedgerBookAdministrationStudioConfigured.Should().BeTrue();
        retained.ApprovalQueueStudioConfigured.Should().BeTrue();
        retained.ApprovalQueueConfigurations.Should().ContainSingle(queue =>
            queue.QueueId == "configuration-promotion-queue" &&
            queue.DisplayName == "Configuration promotion queue" &&
            queue.WorkflowKind == "ConfigurationPromotion" &&
            queue.RequiredApprovalRole == "Controller" &&
            queue.RequiredApprovalCount == 2 &&
            queue.SegregationPolicy == "Preparer cannot approve own configuration change." &&
            queue.EvidenceRequirement == "approval-queue;configuration-approval;segregation-review");
        retained.DimensionMappingStudioConfigured.Should().BeTrue();
        retained.DimensionMappingConfigurations.Should().ContainSingle(mapping =>
            mapping.MappingId == "qbo-canonical-dimension-map" &&
            mapping.DisplayName == "QuickBooks canonical dimension map" &&
            mapping.ProviderId == "quickbooks-fixture" &&
            mapping.MeridianDimensions.FundId == profile.FundProfileId &&
            mapping.MeridianDimensions.BookId == harness.LedgerBookService.Book.LedgerBookId.ToString("D") &&
            mapping.MeridianDimensions.CustomerId == "investor-alpha" &&
            mapping.MeridianDimensions.ExternalGlDimensions["Project"] == "direct-lending" &&
            mapping.ProviderDimensions.BookId == $"Book:{harness.LedgerBookService.Book.LedgerBookId:D}" &&
            mapping.ProviderDimensions.CustomerId == "qbo-customer-alpha" &&
            mapping.ProviderDimensions.ExternalGlDimensions["Project"] == "qbo-project-credit" &&
            mapping.EvidenceRequirement == "dimension-mapping;external-dimension-mapping;gl-dimension-mapping");
        retained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/setup");
        retained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/operator-surface");
        retained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/dimension-mapping");
        retained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/ledger-book-administration/ledgerBookId=7e0be005-49e1-46eb-9d4f-89d75e2328bd");
        retained.EvidenceReferences.Should().Contain(item =>
            item.StartsWith("correlation:wpf-accounting-tenant-admin-", StringComparison.OrdinalIgnoreCase));
        retained.EvidenceReferences.Count(item =>
                string.Equals(item, "evidence://tenant-admin/alpha-fund/Alpha Fund LP/setup", StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(1);

        harness.ViewModel.TenantAdministrationProfileStatusText.Should()
            .Contain("Tenant administration setup profile saved");
        harness.ViewModel.ProductionReadinessTenantAdminText.Should()
            .Contain("tenant admin control");
        harness.ViewModel.ProductionReadinessTenantAdminText.Should()
            .Contain("6 retained evidence link");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Tenant config" && row.Status == "Configured");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Reporting groups" && row.Status == "Configured");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Chart admin" && row.Status == "Configured");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Close setup" && row.Status == "Configured");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Provider maps" && row.Status == "Configured");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Dimension maps" &&
            row.Status == "Configured" &&
            row.Detail.Contains("quickbooks-fixture", StringComparison.OrdinalIgnoreCase) &&
            row.Evidence == "qbo-canonical-dimension-map");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Audit review" && row.Status == "Missing");
        harness.ViewModel.ProductionReadinessMigrationPlanRows.Should().Contain(row =>
            row.Name == "Dimensional backfill"
            && row.Evidence.Contains("migration.dimensional-backfill-not-certified", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessMigrationWorkerPlanRows.Should().Contain(row =>
            row.Name == "Historical journal backfill"
            && row.Status == "Reconciled"
            && row.Detail.Contains("tenant alpha-fund", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("company Alpha Fund LP", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("275 source record", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("275 migrated record", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("fund alpha-fund", StringComparison.OrdinalIgnoreCase)
            && row.Detail.Contains("external Department=FundAccounting", StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("evidence://migration-worker-plan/historical/alpha-fund", StringComparison.OrdinalIgnoreCase)
            && row.Key == "worker-plan-historical-alpha-fund");

        harness.ViewModel.CanRetainImplementationSandboxProof.Should().BeTrue();

        await harness.ViewModel.RetainImplementationSandboxProofAsync();

        var sandboxRetained = await harness.TenantAdministrationProfileStore.GetAsync("alpha-fund", "Alpha Fund LP");
        sandboxRetained.Should().NotBeNull();
        sandboxRetained!.ImplementationSandboxConfigured.Should().BeTrue();
        sandboxRetained.ApprovalQueueStudioConfigured.Should().BeTrue();
        sandboxRetained.ApprovalQueueConfigurations.Should().ContainSingle(queue =>
            queue.QueueId == "configuration-promotion-queue"
            && queue.WorkflowKind == "ConfigurationPromotion"
            && queue.RequiredApprovalCount == 2);
        sandboxRetained.DimensionMappingStudioConfigured.Should().BeTrue();
        sandboxRetained.DimensionMappingConfigurations.Should().ContainSingle(mapping =>
            mapping.MappingId == "qbo-canonical-dimension-map"
            && mapping.ProviderId == "quickbooks-fixture"
            && mapping.EvidenceRequirement.Contains("dimension-mapping", StringComparison.OrdinalIgnoreCase));
        sandboxRetained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/implementation-sandbox/ledgerBookId=7e0be005-49e1-46eb-9d4f-89d75e2328bd");
        sandboxRetained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/sandbox-validation/ledgerBookId=7e0be005-49e1-46eb-9d4f-89d75e2328bd");
        sandboxRetained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/fixture-validation/ledgerBookId=7e0be005-49e1-46eb-9d4f-89d75e2328bd");
        sandboxRetained.EvidenceReferences.Should().Contain("evidence://tenant-admin/alpha-fund/Alpha Fund LP/implementation-fixture/ledgerBookId=7e0be005-49e1-46eb-9d4f-89d75e2328bd");
        harness.ViewModel.ImplementationSandboxProofStatusText.Should().Contain("Implementation sandbox proof retained");
        harness.ViewModel.TenantAdministrationControlRows.Should().Contain(row =>
            row.Name == "Sandbox proof" && row.Status == "Configured");
    }

    [Fact]
    public async Task ExternalGlMappingProfile_SaveRetainsCertifiedProviderMappingAndRefreshesReadiness()
    {
        Directory.CreateDirectory(_root);
        var fundContext = new FundContextService(Path.Combine(_root, "fund-context.json"));
        var fundProfile = await fundContext.UpsertProfileAsync(new FundProfileDetail(
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
        await fundContext.SelectFundProfileAsync(fundProfile.FundProfileId);
        var harness = CreateHarness(fundContext);
        await harness.ConfigurationService.GetWorkspaceAsync(
            fundProfile.FundProfileId,
            harness.LedgerBookService.Book.LedgerBookId,
            CancellationToken.None);

        await harness.ViewModel.LoadAsync();
        harness.ViewModel.CanSaveExternalGlMappingProfile.Should().BeTrue();
        harness.ViewModel.SaveExternalGlMappingProfileCommand.CanExecute(null).Should().BeTrue();

        harness.ViewModel.ExternalGlMappingProviderId = "quickbooks-fixture";
        harness.ViewModel.ExternalGlMappingProfileId = "qbo-alpha-book-primary";
        harness.ViewModel.ExternalGlMappingDisplayName = "Alpha QuickBooks guarded export mapping";
        harness.ViewModel.ExternalGlMappingAccountMappingsText =
            "Assets:Cash:Operating=qbo-1000\nIncome:Investment Income=qbo-4000";
        harness.ViewModel.ExternalGlMappingMeridianDimensionsText =
            $"fundId={fundProfile.FundProfileId}\nbookId={harness.LedgerBookService.Book.LedgerBookId:D}\ncustomerId=investor-alpha\nProject=direct-lending";
        harness.ViewModel.ExternalGlMappingExternalDimensionsText =
            $"bookId=Book:{harness.LedgerBookService.Book.LedgerBookId:D}\ncustomerId=qbo-customer-alpha\nProject=qbo-project-credit";
        harness.ViewModel.ExternalGlMappingEvidenceText =
            "approval:external-gl-mapping:qbo-alpha-book-primary";
        harness.ViewModel.ExternalGlMappingProfileCertified = true;
        harness.ViewModel.SaveExternalGlMappingProfileCommand.CanExecute(null).Should().BeTrue();

        harness.ViewModel.ExternalGlMappingAccountMappingsText = "   ";
        harness.ViewModel.CanSaveExternalGlMappingProfile.Should().BeFalse();
        harness.ViewModel.SaveExternalGlMappingProfileCommand.CanExecute(null).Should()
            .BeFalse("external GL provider mapping commands must share the account-mapping and evidence guard");
        harness.ViewModel.ExternalGlMappingAccountMappingsText =
            "Assets:Cash:Operating=qbo-1000\nIncome:Investment Income=qbo-4000";
        harness.ViewModel.SaveExternalGlMappingProfileCommand.CanExecute(null).Should().BeTrue();

        await harness.ViewModel.SaveExternalGlMappingProfileAsync();

        var retained = await harness.AccountingSystemIntegrationService.ListMappingProfilesAsync(
            "quickbooks-fixture",
            fundProfile.FundProfileId,
            harness.LedgerBookService.Book.LedgerBookId,
            tenantId: fundProfile.FundProfileId,
            companyId: fundProfile.LegalEntityName);
        retained.Should().ContainSingle(profile => profile.ProfileId == "qbo-alpha-book-primary");
        var profile = retained.Single(profile => profile.ProfileId == "qbo-alpha-book-primary");
        profile.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        profile.AccountMappings["Assets:Cash:Operating"].Should().Be("qbo-1000");
        profile.DimensionMappings.Should().ContainSingle(mapping =>
            mapping.CertificationState == AccountingCertificationStateDto.Certified &&
            mapping.MeridianDimensions.FundId == fundProfile.FundProfileId &&
            mapping.MeridianDimensions.BookId == harness.LedgerBookService.Book.LedgerBookId.ToString("D") &&
            mapping.MeridianDimensions.CustomerId == "investor-alpha" &&
            mapping.MeridianDimensions.ExternalGlDimensions["Project"] == "direct-lending" &&
            mapping.ExternalDimensions.BookId == $"Book:{harness.LedgerBookService.Book.LedgerBookId:D}" &&
            mapping.ExternalDimensions.CustomerId == "qbo-customer-alpha" &&
            mapping.ExternalDimensions.ExternalGlDimensions["Project"] == "qbo-project-credit");
        harness.ViewModel.ExternalGlMappingProfileStatusText.Should()
            .Contain("External GL mapping profile qbo-alpha-book-primary saved as Certified");
        harness.ViewModel.ExternalGlMappingProfileRows.Should().Contain(row =>
            row.Name == "Alpha QuickBooks guarded export mapping" &&
            row.Status == "Certified" &&
            row.Detail.Contains("2 account mapping", StringComparison.OrdinalIgnoreCase));
        harness.ViewModel.ProductionReadinessExternalGlText.Should()
            .Contain("1 certified mapping profile");
    }

    [Fact]
    public async Task RulesStudioActions_RunSharedRegressionSuiteAndApprovePromotion()
    {
        Directory.CreateDirectory(_root);
        var ledgerBookId = Guid.Parse("7e0be005-49e1-46eb-9d4f-89d75e2328bd");
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
        await harness.ConfigurationService.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: profile.FundProfileId,
            Rule: new PostingRuleDto(
                RuleId: "wpf-promotion-gated-capital-call",
                DisplayName: "WPF promotion-gated capital call",
                SourceEventType: "ManualJournalEntry.CapitalCall",
                TemplateId: "",
                RuleVersion: "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 500,
                Scope: new LedgerDimensionSetDto(FundId: profile.FundProfileId, EntityId: "entity-alpha"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-capital", "Equity:Capital Contributions", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ],
                RequiresPromotionApproval: true),
            Actor: "controller",
            CorrelationId: "wpf-rules-studio-promotion-seed",
            EvidenceLinks: ["evidence://accounting/rules/wpf-promotion-gated-capital-call/v1"],
            LedgerBookId: ledgerBookId));
        await harness.ConfigurationService.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: profile.FundProfileId,
            TestCase: new AccountingRuleTestCaseDto(
                "wpf-promotion-gated-capital-call-happy-path",
                "WPF promotion-gated capital call happy path",
                new RuleDryRunRequestDto(
                    FundProfileId: profile.FundProfileId,
                    SourceEventType: "ManualJournalEntry.CapitalCall",
                    EventAmount: 250m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    LedgerBookId: ledgerBookId,
                    Dimensions: new LedgerDimensionSetDto(FundId: profile.FundProfileId, EntityId: "entity-alpha")),
                ExpectedRuleId: "wpf-promotion-gated-capital-call",
                ExpectedRuleVersion: "v1",
                EvidenceLinks: ["evidence://accounting/rule-tests/wpf-promotion-gated-capital-call-happy-path/wpf-promotion-gated-capital-call/v1/regression-evidence"]),
            Actor: "controller",
            CorrelationId: "wpf-rules-studio-testcase-seed",
            LedgerBookId: ledgerBookId));

        await harness.ViewModel.LoadAsync();

        harness.ViewModel.RulesStudioPromotionRows.Should().ContainSingle(row =>
            row.Name == "wpf-promotion-gated-capital-call" &&
            row.Status == "Not requested" &&
            row.Evidence.Contains("1 regression test", StringComparison.OrdinalIgnoreCase));

        await harness.ViewModel.ExecuteRulesStudioTestsAsync();

        harness.ViewModel.RulesStudioActionText.Should().Contain("Executed 1 saved rule test case");
        harness.ViewModel.RulesStudioActionRows.Should().Contain(row =>
            row.Name == "Regression suite" &&
            row.Status == "1/1 passed" &&
            row.Key == ledgerBookId.ToString("D"));
        harness.ViewModel.RulesStudioActionRows.Should().Contain(row =>
            row.Name == "wpf-promotion-gated-capital-call-happy-path" &&
            row.Status == "Passed" &&
            row.Detail.Contains("wpf-promotion-gated-capital-call/v1", StringComparison.OrdinalIgnoreCase));

        await harness.ViewModel.ApproveRulesStudioPromotionAsync();

        harness.ViewModel.RulesStudioActionText.Should().Contain("Approved posting-rule promotion wpf-promotion-gated-capital-call/v1");
        harness.ViewModel.RulesStudioActionRows.Should().ContainSingle(row =>
            row.Name == "wpf-promotion-gated-capital-call" &&
            row.Status == "Approved" &&
            row.Evidence.Contains("/v1/", StringComparison.OrdinalIgnoreCase));
        var reloaded = await harness.ConfigurationService.GetWorkspaceAsync(profile.FundProfileId, ledgerBookId);
        reloaded.PostingRules.Should().ContainSingle(rule =>
            rule.RuleId == "wpf-promotion-gated-capital-call" &&
            rule.PromotionApproval != null &&
            rule.PromotionApproval.ApprovalState == ManualJournalEntryStatusDto.Approved);
        reloaded.AuditTrail.Should().Contain(audit =>
            audit.Action == "posting-rule.promotion-approve" &&
            audit.LedgerBookId == ledgerBookId);
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
            new WorkstationAccountingApiClientStub(configurationService),
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
        var harness = CreateHarness(fundContext);

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.SeedBaselineConfigurationAsync();
        harness.ViewModel.SelectedEntryType = ManualJournalEntryTypeDto.CapitalCall;
        harness.ViewModel.DraftAmount = 275m;
        await harness.ViewModel.SaveManualJournalDraftAsync();
        await harness.ViewModel.ValidateManualJournalDraftAsync();
        await harness.ViewModel.SubmitManualJournalDraftAsync();

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
        reversed.LifecycleTransitions.Should().Contain(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Reverse &&
            transition.Actor == "desktop-controller");
        retainedDrafts.Should().ContainSingle(draft =>
            draft.EntryType == ManualJournalEntryTypeDto.Reversal
            && draft.ReversalOfJournalEntryId == reversed.JournalEntryId
            && draft.Status == ManualJournalEntryStatusDto.Draft);
    }

    [Fact]
    public async Task LoadAsync_WithoutFundContext_FailsClosedAndKeepsRowsEmpty()
    {
        Directory.CreateDirectory(_root);
        var harness = CreateHarness(new FundContextService(Path.Combine(_root, "fund-context.json")));

        await harness.ViewModel.LoadAsync();

        harness.ViewModel.ConfigurationStatusText.Should().Be("Locked");
        harness.ViewModel.ActiveFundText.Should().Be("No fund selected");
        harness.ViewModel.SetupReadinessRows.Should().BeEmpty();
        harness.ViewModel.LedgerBookRows.Should().BeEmpty();
        harness.ViewModel.LedgerBookAdministrationText.Should().Contain("Locked");
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
    public async Task LoadAsync_WithMissingLedgerBookSetup_SurfacesBlockingSetupReadiness()
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

        var missingLedgerBookId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var workspace = new AccountingConfigurationWorkspaceDto(
            profile.FundProfileId,
            missingLedgerBookId,
            AccountingConfigurationStatusDto.Draft,
            "draft",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts: [],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues:
            [
                new AccountingConfigurationValidationIssueDto(
                    "configuration.ledger-book-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Accounting configuration targets ledger book '{missingLedgerBookId:D}', but no matching ledger book setup was found.",
                    missingLedgerBookId.ToString("D", CultureInfo.InvariantCulture),
                    "Create or select the ledger book before activating book-scoped accounting configuration.")
            ],
            AuditTrail: [],
            RuleTestCases: [],
            RulesStudio: null);
        var viewModel = new AccountingConfigureViewModel(
            fundContext,
            new StaticAccountingConfigurationService(workspace),
            new StaticManualJournalEntryWorkbenchService(CreatePostedPrivateCapitalProjection(profile.FundProfileId)));

        await viewModel.LoadAsync();

        viewModel.SetupReadinessRows.Should().Contain(row =>
            row.Name == "Selected ledger book"
            && row.Status == "Missing"
            && row.Detail.Contains(missingLedgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && row.Evidence.Contains("Create or select the ledger book", StringComparison.OrdinalIgnoreCase)
            && row.Key == missingLedgerBookId.ToString("D"));
        viewModel.SetupReadinessRows.Should().Contain(row =>
            row.Name == "Activation readiness"
            && row.Status == "Blocked"
            && row.Detail.Contains("1 critical", StringComparison.OrdinalIgnoreCase)
            && row.Key == "critical:1");
        viewModel.ConfigurationDetailText.Should().Contain("1 critical configuration issue");
    }

    [Fact]
    public async Task CreateLedgerBookSetupAsync_WithSharedCandidate_CreatesBookAndRefreshesConfiguration()
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

        var missingLedgerBookId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var ledgerBookService = new TestLedgerBookService();
        var setupCandidate = new LedgerBookSetupCandidateDto(
            profile.FundProfileId,
            ledgerBookService.Book.FundStructureNodeId,
            ledgerBookService.Book.FundStructureNodeKind,
            "Alpha Fund governed book",
            "USD",
            AccountingBasisKindDto.Primary,
            "legacy-v1",
            "legacy-v1",
            "Create a ledger book using the registered fund-structure scope before activation.",
            Description: "Created from WPF Accounting Configure setup readiness.",
            SourceLedgerBookId: ledgerBookService.Book.LedgerBookId,
            RequestedLedgerBookId: missingLedgerBookId);
        var missingWorkspace = new AccountingConfigurationWorkspaceDto(
            profile.FundProfileId,
            missingLedgerBookId,
            AccountingConfigurationStatusDto.Draft,
            "draft",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts: [],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues:
            [
                new AccountingConfigurationValidationIssueDto(
                    "configuration.ledger-book-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Accounting configuration targets ledger book '{missingLedgerBookId:D}', but no matching ledger book setup was found.",
                    missingLedgerBookId.ToString("D", CultureInfo.InvariantCulture),
                    setupCandidate.SuggestedAction)
            ],
            AuditTrail: [],
            RuleTestCases: [],
            RulesStudio: null,
            LedgerBookSetupCandidate: setupCandidate);
        var refreshedWorkspace = missingWorkspace with
        {
            LedgerBookId = ledgerBookService.Book.LedgerBookId,
            LedgerBooks = [ledgerBookService.Book with { DisplayName = setupCandidate.DisplayName }],
            ValidationIssues = [],
            LedgerBookSetupCandidate = null
        };
        var viewModel = new AccountingConfigureViewModel(
            fundContext,
            new StaticAccountingConfigurationService(missingWorkspace, refreshedWorkspace),
            new StaticManualJournalEntryWorkbenchService(CreatePostedPrivateCapitalProjection(profile.FundProfileId)),
            ledgerBookService: ledgerBookService);

        await viewModel.LoadAsync();
        await viewModel.CreateLedgerBookSetupAsync();

        ledgerBookService.LastCreateRequest.Should().NotBeNull();
        ledgerBookService.LastCreateRequest!.FundProfileId.Should().Be(profile.FundProfileId);
        ledgerBookService.LastCreateRequest.FundStructureNodeId.Should().Be(setupCandidate.FundStructureNodeId);
        ledgerBookService.LastCreateRequest.FundStructureNodeKind.Should().Be(setupCandidate.FundStructureNodeKind);
        ledgerBookService.LastCreateRequest.DisplayName.Should().Be(setupCandidate.DisplayName);
        ledgerBookService.LastCreateRequest.AccountingPolicyId.Should().Be(setupCandidate.AccountingPolicyId);
        viewModel.LedgerBookSetupStatusText.Should().Contain("Created ledger book Alpha Fund governed book");
        viewModel.SetupReadinessRows.Should().Contain(row =>
            row.Name == "Selected ledger book" &&
            row.Status == "Alpha Fund governed book" &&
            row.Detail.Contains("legacy-v1", StringComparison.OrdinalIgnoreCase));
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
        xaml.Should().Contain("AccountingConfigureCreateLedgerBookSetupButton");
        xaml.Should().Contain("AccountingConfigureLedgerBookSetupStatusText");
        xaml.Should().Contain("AccountingLedgerBookAdministrationText");
        xaml.Should().Contain("AccountingLedgerBookAdministrationGrid");
        xaml.Should().Contain("LedgerBookRows");
        xaml.Should().Contain("AccountingProductionReadinessStatusText");
        xaml.Should().Contain("AccountingProductionReadinessComponentGrid");
        xaml.Should().Contain("AccountingProductionReadinessIssueGrid");
        xaml.Should().Contain("AccountingProductionReadinessGapGrid");
        xaml.Should().Contain("ProductionReadinessGapRows");
        xaml.Should().Contain("AccountingManualJournalPeriodText");
        xaml.Should().Contain("ManualJournalDraftPeriodText");
        xaml.Should().Contain("AccountingProductionReadinessWorkflowGrid");
        xaml.Should().Contain("ProductionReadinessWorkflowRows");
        xaml.Should().Contain("AccountingProductionReadinessDimensionalControlGrid");
        xaml.Should().Contain("ProductionReadinessDimensionalControlRows");
        xaml.Should().Contain("AccountingProductionReadinessMigrationArtifactGrid");
        xaml.Should().Contain("AccountingProductionReadinessMigrationWorkerPlanGrid");
        xaml.Should().Contain("AccountingPostingCandidateButton");
        xaml.Should().Contain("AccountingConfigurationSetupReadinessGrid");
        xaml.Should().Contain("SetupReadinessRows");
        xaml.Should().Contain("AccountingChartAccountSetupEditor");
        xaml.Should().Contain("AccountingChartAccountNodeIdTextBox");
        xaml.Should().Contain("AccountingChartAccountPathTextBox");
        xaml.Should().Contain("AccountingChartAccountFinancialAccountIdTextBox");
        xaml.Should().Contain("AccountingChartAccountSaveButton");
        xaml.Should().Contain("SaveChartAccountCommand");
        xaml.Should().Contain("AccountingPostingRuleDraftEditor");
        xaml.Should().Contain("AccountingPostingRuleDraftRuleIdTextBox");
        xaml.Should().Contain("AccountingPostingRuleDraftTemplateIdTextBox");
        xaml.Should().Contain("AccountingPostingRuleDraftEvidenceTextBox");
        xaml.Should().Contain("AccountingPostingRuleDraftSaveButton");
        xaml.Should().Contain("SavePostingRuleDraftCommand");
        xaml.Should().Contain("AccountingDimensionMappingSetupEditor");
        xaml.Should().Contain("AccountingDimensionMappingIdTextBox");
        xaml.Should().Contain("AccountingDimensionMappingMeridianDimensionsTextBox");
        xaml.Should().Contain("AccountingDimensionMappingProviderDimensionsTextBox");
        xaml.Should().Contain("AccountingDimensionMappingEvidenceRequirementTextBox");
        xaml.Should().Contain("AccountingPostingCandidateGrid");
        xaml.Should().Contain("Posting Candidate Preview");
        xaml.Should().Contain("AccountingRulesStudioRuleGrid");
        xaml.Should().Contain("AccountingRulesStudioPromotionGrid");
        xaml.Should().Contain("RulesStudioStatusText");
        xaml.Should().Contain("RulesStudioDetailText");
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
        xaml.Should().Contain("Diagnostic evidence references");
        xaml.Should().Contain("URI and reference text helps operators locate retained support");
        xaml.Should().Contain("AccountingProductionCertificationEvidenceGuidanceText");
        xaml.Should().Contain("ProductionCertificationSaveGuidanceText");
        xaml.Should().MatchRegex("IsEnabled=\"False\"\\s+AutomationProperties.AutomationId=\"AccountingProductionCertificationProfileControlPanel\"");
        xaml.Should().MatchRegex("IsReadOnly=\"True\"\\s+AutomationProperties.Name=\"Diagnostic production certification evidence references; not certification authority\"");
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

    private static AccountingProductionCertificationProfileDto CreateGovernedProductionCertificationProfile(
        Guid ledgerBookId)
    {
        const string certificationId = "workflow-certification:alpha-fund:posting-rules:v7";
        const string evidenceUri = "evidence://governed-certification/alpha-fund/posting-rules/v7";
        var reviewedAtUtc = DateTimeOffset.Parse(
            "2026-07-20T15:00:00Z",
            CultureInfo.InvariantCulture);
        var retainedAtUtc = reviewedAtUtc.AddMinutes(5);
        var retainedEvidence = new RetainedEvidenceIdentityDto(
            EvidenceId: "evidence:workflow-certification:alpha-fund:posting-rules:v7",
            EvidenceUri: evidenceUri,
            ContentHashSha256: new string('a', 64),
            SourceSystem: "GovernedCertificationRunner",
            SourceReference: "certification-run://alpha-fund/posting-rules/v7",
            ReviewStatus: RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            ReviewedBy: "independent-controller",
            ReviewedAtUtc: reviewedAtUtc,
            EffectiveDate: new DateOnly(2026, 7, 20),
            EvidenceVersion: 7,
            RetainedAtUtc: retainedAtUtc,
            RetainedBy: "governed-evidence-store",
            SubjectType: AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
            SubjectId: certificationId);
        var workflowArtifact = new AccountingWorkflowCertificationArtifactDto(
            CertificationId: certificationId,
            Status: AccountingCertificationArtifactStatusDto.Certified,
            TenantId: "alpha-fund",
            CompanyId: "Alpha Fund LP",
            FundProfileId: "alpha-fund",
            LedgerBookId: ledgerBookId,
            CertifiedBy: "independent-controller",
            CertifiedAtUtc: reviewedAtUtc,
            SourceService: "governed-certification-runner",
            Lanes:
            [
                new AccountingWorkflowCertificationLaneDto(
                    AccountingWorkflowCertificationLaneKindDto.PostingRules,
                    AccountingCertificationArtifactLaneStatusDto.Passed,
                    [evidenceUri])
            ],
            EvidenceReferences: [evidenceUri],
            CorrelationId: "governed-certification-run-v7");

        return new AccountingProductionCertificationProfileDto(
            FundProfileId: "alpha-fund",
            LedgerBookId: ledgerBookId,
            PostingRulesLedgerBookNativeCertified: true,
            JournalLifecycleLedgerBookNativeCertified: false,
            CloseReportingLedgerBookNativeCertified: false,
            ExternalGlLedgerBookNativeCertified: false,
            PeriodReportDimensionQueriesCertified: false,
            CrossPeriodReportDimensionQueriesCertified: false,
            JournalQueryDimensionFiltersCertified: false,
            ExternalExportDimensionMappingCertified: false,
            UpdatedAtUtc: retainedAtUtc,
            UpdatedBy: "governed-certification-controller",
            EvidenceReferences: [evidenceUri],
            CorrelationId: "governed-certification-run-v7",
            TenantId: "alpha-fund",
            CompanyId: "Alpha Fund LP",
            WorkflowCertificationArtifacts: [workflowArtifact],
            RetainedEvidence: [retainedEvidence]);
    }

    private AccountingConfigureHarness CreateHarness(FundContextService fundContext)
    {
        var configurationPath = Path.Combine(_root, "accounting-configuration.json");
        var draftsPath = Path.Combine(_root, "manual-journal-drafts.json");
        var configurationStore = new FileAccountingConfigurationStore(configurationPath);
        var ledgerBookService = new TestLedgerBookService();
        var configurationService = new AccountingConfigurationService(configurationStore, configurationStore, ledgerBookService);
        var draftStore = new FileManualJournalEntryDraftStore(draftsPath);
        var ledgerJournalStore = new RecordingLedgerJournalStore(ledgerBookService.Book);
        var manualJournalService = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            configurationStore,
            journalStore: ledgerJournalStore);
        var capitalAccountWorkbenchService = new CapitalAccountWorkbenchService(manualJournalService);
        var accountingSystemIntegrationService = new AccountingSystemIntegrationService(
            [new QuickBooksFixtureAccountingProvider()]);
        var policyService = new AccountingPolicyService();
        var postingCandidateService = new TestAccountingPostingCandidateService();
        var productionCertificationProfileStore = new InMemoryAccountingProductionCertificationProfileStore();
        var tenantAdministrationProfileStore = new InMemoryAccountingTenantAdministrationProfileStore();
        var migrationRunArtifactStore = new InMemoryAccountingMigrationRunArtifactStore();
        var migrationRunWorkerPlanStore = new InMemoryAccountingMigrationRunWorkerPlanStore();
        var services = new ServiceCollection();
        services.AddSingleton<ILedgerBookService>(ledgerBookService);
        services.AddSingleton<IAccountingConfigurationService>(configurationService);
        services.AddSingleton<IManualJournalEntryWorkbenchService>(manualJournalService);
        services.AddSingleton<IManualJournalEntryLifecycleService>(manualJournalService);
        services.AddSingleton<ILedgerJournalStore>(ledgerJournalStore);
        services.AddSingleton(accountingSystemIntegrationService);
        services.AddSingleton<IAccountingProductionCertificationProfileStore>(productionCertificationProfileStore);
        services.AddSingleton<IAccountingTenantAdministrationProfileStore>(tenantAdministrationProfileStore);
        services.AddSingleton<IAccountingMigrationRunArtifactStore>(migrationRunArtifactStore);
        services.AddSingleton<IAccountingMigrationRunWorkerPlanStore>(migrationRunWorkerPlanStore);
        services.AddSingleton<IAccountingMigrationRunWorkerPlanWriter>(migrationRunWorkerPlanStore);
        var productionReadinessService = new AccountingProductionReadinessService(services.BuildServiceProvider());
        var viewModel = new AccountingConfigureViewModel(
            fundContext,
            new WorkstationAccountingApiClientStub(configurationService),
            manualJournalService,
            configurationStore,
            draftStore,
            accountingSystemIntegrationService,
            fundOperationsWorkspaceReadService: null,
            policyService,
            capitalAccountWorkbenchService,
            postingCandidateService,
            ledgerBookService: ledgerBookService,
            accountingProductionReadinessService: productionReadinessService,
            productionCertificationProfileStore: productionCertificationProfileStore,
            tenantAdministrationProfileStore: tenantAdministrationProfileStore,
            migrationRunWorkerPlanStore: migrationRunWorkerPlanStore);

        return new AccountingConfigureHarness(
            viewModel,
            configurationPath,
            draftsPath,
            configurationService,
            postingCandidateService,
            productionCertificationProfileStore,
            tenantAdministrationProfileStore,
            migrationRunArtifactStore,
            migrationRunWorkerPlanStore,
            accountingSystemIntegrationService,
            ledgerBookService);
    }

    private sealed record AccountingConfigureHarness(
        AccountingConfigureViewModel ViewModel,
        string ConfigurationPath,
        string DraftsPath,
        AccountingConfigurationService ConfigurationService,
        TestAccountingPostingCandidateService PostingCandidateService,
        IAccountingProductionCertificationProfileStore ProductionCertificationProfileStore,
        IAccountingTenantAdministrationProfileStore TenantAdministrationProfileStore,
        IAccountingMigrationRunArtifactStore MigrationRunArtifactStore,
        IAccountingMigrationRunWorkerPlanWriter MigrationRunWorkerPlanStore,
        AccountingSystemIntegrationService AccountingSystemIntegrationService,
        TestLedgerBookService LedgerBookService);


    private sealed record PresetExpectation(
        ManualJournalEntryTypeDto EntryType,
        string Label,
        string Memo,
        string DebitAccountPath,
        string CreditAccountPath,
        string EvidenceLink);

    private sealed class RecordingLedgerJournalStore : ILedgerJournalStore
    {
        private readonly List<LedgerJournalEntryRecord> _entries = [];
        private readonly LedgerBookRecord _book;

        public RecordingLedgerJournalStore(LedgerBookDto book)
        {
            _book = new LedgerBookRecord(
                book.LedgerBookId,
                book.FundProfileId,
                book.FundStructureNodeId,
                book.FundStructureNodeKind,
                book.DisplayName,
                book.BaseCurrency,
                book.CreatedAt,
                book.UpdatedAt,
                book.Description,
                book.AccountingBasis,
                book.AccountingPolicyId,
                book.AccountingPolicyVersion);
        }

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Entry.IsBalanced)
            {
                throw new LedgerValidationException("Journal entry must be balanced.");
            }

            _entries.Add(new LedgerJournalEntryRecord(
                entry.Entry,
                entry.AggregateId,
                entry.PeriodId,
                entry.CommandId,
                entry.CorrelationId,
                _entries.Count + 1,
                DateTimeOffset.UtcNow,
                entry.AccountingBasis,
                entry.AccountingPolicyId,
                entry.AccountingPolicyVersion,
                entry.RuleId,
                entry.RuleVersion,
                entry.SourceEventId,
                entry.SourceJournalEntryId,
                entry.PostingKind,
                entry.AdjustmentApproval));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _entries.Where(entry => entry.PeriodId == periodId).ToArray());
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _entries.Where(entry => entry.AggregateId == aggregateId).ToArray());
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(new LedgerAccountingPeriod(
                periodId,
                _book.LedgerBookId,
                2026,
                6,
                "2026-06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Open",
                DateTimeOffset.UtcNow,
                null,
                1));
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if ((ledgerBookId.HasValue && ledgerBookId != _book.LedgerBookId) ||
                (!string.IsNullOrWhiteSpace(fundProfileId) && !string.Equals(fundProfileId, _book.FundProfileId, StringComparison.OrdinalIgnoreCase)) ||
                (fundStructureNodeId.HasValue && fundStructureNodeId != _book.FundStructureNodeId) ||
                (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([]);
            }

            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(
            [
                new LedgerAccountingPeriod(
                    Guid.Parse("84ee5cf3-d598-4540-89f5-c8650d6cfef5"),
                    _book.LedgerBookId,
                    2026,
                    6,
                    "2026-06",
                    new DateOnly(2026, 6, 1),
                    new DateOnly(2026, 6, 30),
                    "Open",
                    DateTimeOffset.UtcNow,
                    null,
                    1)
            ]);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(period);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(ledgerBookId == _book.LedgerBookId ? _book : null);
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            Meridian.Contracts.FundStructure.FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches =
                (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(fundProfileId, _book.FundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                (!fundStructureNodeId.HasValue || fundStructureNodeId == _book.FundStructureNodeId) &&
                (!fundStructureNodeKind.HasValue || fundStructureNodeKind == _book.FundStructureNodeKind);
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>(matches ? [_book] : []);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(book);
        }
    }

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

        public LedgerBookDto Book => _book;

        public LedgerPeriodDto OpenPeriod { get; } = new(
            Guid.Parse("84ee5cf3-d598-4540-89f5-c8650d6cfef5"),
            Guid.Parse("7e0be005-49e1-46eb-9d4f-89d75e2328bd"),
            2026,
            6,
            "2026-06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            LedgerPeriodStatusDto.Open,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture),
            null,
            1,
            AccountingBasisKindDto.Primary,
            "legacy-v1",
            "legacy-v1");

        public CreateLedgerBookRequest? LastCreateRequest { get; private set; }

        public Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastCreateRequest = request;
            return Task.FromResult(_book with
            {
                FundProfileId = string.IsNullOrWhiteSpace(request.FundProfileId) ? _book.FundProfileId : request.FundProfileId,
                BaseCurrency = string.IsNullOrWhiteSpace(request.BaseCurrency) ? _book.BaseCurrency : request.BaseCurrency,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? _book.DisplayName : request.DisplayName,
                Description = request.Description ?? _book.Description,
                AccountingBasis = request.AccountingBasis,
                AccountingPolicyId = request.AccountingPolicyId,
                AccountingPolicyVersion = request.AccountingPolicyVersion
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

        public Task<LedgerBookRolloutAssessmentDto> AssessRolloutAsync(
            LedgerBookRolloutAssessmentRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matchesFund = string.IsNullOrWhiteSpace(request.FundProfileId) ||
                string.Equals(request.FundProfileId, _book.FundProfileId, StringComparison.OrdinalIgnoreCase);
            var matchesNode = !request.FundStructureNodeId.HasValue ||
                request.FundStructureNodeId == _book.FundStructureNodeId ||
                request.FundStructureNodeId == _book.LedgerBookId;
            var matchesKind = !request.FundStructureNodeKind.HasValue ||
                request.FundStructureNodeKind == _book.FundStructureNodeKind;
            var matchesBasis = !request.AccountingBasis.HasValue ||
                request.AccountingBasis == _book.AccountingBasis;
            IReadOnlyList<LedgerBookRolloutBookStatusDto> books = matchesFund && matchesNode && matchesKind && matchesBasis
                ? new[]
                {
                    new LedgerBookRolloutBookStatusDto(
                        _book.LedgerBookId,
                        _book.FundProfileId,
                        _book.FundStructureNodeId,
                        _book.FundStructureNodeKind,
                        _book.AccountingBasis,
                        _book.AccountingPolicyId,
                        _book.AccountingPolicyVersion,
                        PeriodCount: 0,
                        OpenPeriodCount: 0,
                        SoftClosedPeriodCount: 0,
                        HardClosedPeriodCount: 0,
                        FirstPeriodStart: null,
                        LastPeriodEnd: null)
                }
                : [];

            return Task.FromResult(new LedgerBookRolloutAssessmentDto(
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(request.FundProfileId) ? _book.FundProfileId : request.FundProfileId,
                request.FundStructureNodeId,
                request.FundStructureNodeKind,
                request.AccountingBasis,
                books,
                []));
        }

        public Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default)
            => Task.FromException<LedgerPeriodDto>(new NotSupportedException("Accounting configure tests do not create ledger periods."));

        public Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches =
                (!query.LedgerBookId.HasValue || query.LedgerBookId == OpenPeriod.LedgerBookId) &&
                (!query.Status.HasValue || query.Status == OpenPeriod.Status) &&
                (!query.AccountingBasis.HasValue || query.AccountingBasis == OpenPeriod.AccountingBasis) &&
                (!query.OpenOnly || OpenPeriod.Status == LedgerPeriodStatusDto.Open);
            return Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(matches ? [OpenPeriod] : []);
        }

        public Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(
                !ledgerBookId.HasValue || ledgerBookId == OpenPeriod.LedgerBookId
                    ? [OpenPeriod]
                    : []);
        }

        public Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerPeriodSummaryDto?>(null);
        }

        public Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(Guid periodId, CloseLedgerPeriodRequest request, CancellationToken ct = default)
            => Task.FromException<LedgerPeriodCloseResultDto>(new NotSupportedException("Accounting configure tests do not close ledger periods."));
    }

    /// <summary>
    /// Adapts an in-process configuration service to the desktop's HTTP-client seam so the
    /// ViewModel under test keeps exercising local store semantics.
    /// </summary>
    private sealed class WorkstationAccountingApiClientStub : IWorkstationAccountingApiClient
    {
        private readonly IAccountingConfigurationService _inner;

        public WorkstationAccountingApiClientStub(IAccountingConfigurationService inner) => _inner = inner;

        public Task<AccountingConfigurationWorkspaceDto> GetWorkspaceAsync(
            string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => _inner.GetWorkspaceAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);

        public Task<AccountingConfigurationWorkspaceDto> UpsertChartNodeAsync(UpsertChartOfAccountsNodeRequest request, CancellationToken ct = default)
            => _inner.UpsertChartNodeAsync(request, ct);

        public Task<AccountingConfigurationWorkspaceDto> UpsertTemplateAsync(UpsertJournalEntryTemplateRequest request, CancellationToken ct = default)
            => _inner.UpsertTemplateAsync(request, ct);

        public Task<AccountingConfigurationWorkspaceDto> UpsertPostingRuleAsync(UpsertPostingRuleRequest request, CancellationToken ct = default)
            => _inner.UpsertPostingRuleAsync(request, ct);

        public Task<AccountingConfigurationWorkspaceDto> ApprovePostingRulePromotionAsync(ApprovePostingRulePromotionRequest request, CancellationToken ct = default)
            => _inner.ApprovePostingRulePromotionAsync(request, ct);

        public Task<AccountingConfigurationWorkspaceDto> UpsertRuleTestCaseAsync(UpsertAccountingRuleTestCaseRequest request, CancellationToken ct = default)
            => _inner.UpsertRuleTestCaseAsync(request, ct);

        public Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(PreviewJournalTemplateRequest request, CancellationToken ct = default)
            => _inner.PreviewTemplateAsync(request, ct);

        public Task<RuleDryRunResultDto> DryRunPostingRuleAsync(RuleDryRunRequestDto request, CancellationToken ct = default)
            => _inner.DryRunPostingRuleAsync(request, ct);

        public Task<AccountingRuleTestSuiteResultDto> ExecuteRuleTestCasesAsync(ExecuteAccountingRuleTestCasesRequestDto request, CancellationToken ct = default)
            => _inner.ExecuteRuleTestCasesAsync(request, ct);

        public Task<AccountingConfigurationWorkspaceDto> ActivateAsync(ActivateAccountingConfigurationRequest request, CancellationToken ct = default)
            => _inner.ActivateAsync(request, ct);

        public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
            string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => _inner.ListAuditAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);
    }

    private sealed class StaticAccountingConfigurationService : IWorkstationAccountingApiClient
    {
        private readonly AccountingConfigurationWorkspaceDto _workspace;
        private readonly AccountingConfigurationWorkspaceDto? _refreshedWorkspace;

        public StaticAccountingConfigurationService(
            AccountingConfigurationWorkspaceDto workspace,
            AccountingConfigurationWorkspaceDto? refreshedWorkspace = null)
        {
            _workspace = workspace;
            _refreshedWorkspace = refreshedWorkspace;
        }

        public Task<AccountingConfigurationWorkspaceDto> GetWorkspaceAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
        {
            ct.ThrowIfCancellationRequested();
            var selectedWorkspace = _refreshedWorkspace is not null &&
                ledgerBookId.HasValue &&
                _refreshedWorkspace.LedgerBookId == ledgerBookId
                    ? _refreshedWorkspace
                    : _workspace;
            return Task.FromResult(selectedWorkspace with
            {
                FundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? selectedWorkspace.FundProfileId : fundProfileId.Trim(),
                LedgerBookId = ledgerBookId ?? selectedWorkspace.LedgerBookId
            });
        }

        public Task<AccountingConfigurationWorkspaceDto> UpsertChartNodeAsync(UpsertChartOfAccountsNodeRequest request, CancellationToken ct = default)
            => Task.FromException<AccountingConfigurationWorkspaceDto>(new NotSupportedException("Static accounting configuration service is read-only."));

        public Task<AccountingConfigurationWorkspaceDto> UpsertTemplateAsync(UpsertJournalEntryTemplateRequest request, CancellationToken ct = default)
            => Task.FromException<AccountingConfigurationWorkspaceDto>(new NotSupportedException("Static accounting configuration service is read-only."));

        public Task<AccountingConfigurationWorkspaceDto> UpsertPostingRuleAsync(UpsertPostingRuleRequest request, CancellationToken ct = default)
            => Task.FromException<AccountingConfigurationWorkspaceDto>(new NotSupportedException("Static accounting configuration service is read-only."));

        public Task<AccountingConfigurationWorkspaceDto> ApprovePostingRulePromotionAsync(ApprovePostingRulePromotionRequest request, CancellationToken ct = default)
            => Task.FromException<AccountingConfigurationWorkspaceDto>(new NotSupportedException("Static accounting configuration service is read-only."));

        public Task<AccountingConfigurationWorkspaceDto> UpsertRuleTestCaseAsync(UpsertAccountingRuleTestCaseRequest request, CancellationToken ct = default)
            => Task.FromException<AccountingConfigurationWorkspaceDto>(new NotSupportedException("Static accounting configuration service is read-only."));

        public Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(PreviewJournalTemplateRequest request, CancellationToken ct = default)
            => Task.FromException<AccountingJournalTemplatePreviewDto>(new NotSupportedException("Static accounting configuration service does not preview templates."));

        public Task<RuleDryRunResultDto> DryRunPostingRuleAsync(RuleDryRunRequestDto request, CancellationToken ct = default)
            => Task.FromException<RuleDryRunResultDto>(new NotSupportedException("Static accounting configuration service does not dry-run rules."));

        public Task<AccountingRuleTestSuiteResultDto> ExecuteRuleTestCasesAsync(ExecuteAccountingRuleTestCasesRequestDto request, CancellationToken ct = default)
            => Task.FromException<AccountingRuleTestSuiteResultDto>(new NotSupportedException("Static accounting configuration service does not execute rule tests."));

        public Task<AccountingConfigurationWorkspaceDto> ActivateAsync(ActivateAccountingConfigurationRequest request, CancellationToken ct = default)
            => Task.FromException<AccountingConfigurationWorkspaceDto>(new NotSupportedException("Static accounting configuration service is read-only."));

        public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_workspace.AuditTrail);
        }
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
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
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
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
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
