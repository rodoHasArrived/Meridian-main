using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class AccountingConfigurationServiceTests
{
    [Fact]
    public async Task Scenario_MonthEndSetup_ConfigurationMutationsWriteAuditTrail()
    {
        var service = CreateService();

        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto(
                NodeId: "cash",
                Path: "Assets:Cash",
                AccountName: "Cash",
                AccountType: "Asset"),
            Actor: "ops-user",
            CorrelationId: "config-golden-chart"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto(
                NodeId: "interest-income",
                Path: "Income:Interest",
                AccountName: "Interest Income",
                AccountType: "Revenue"),
            Actor: "ops-user",
            CorrelationId: "config-golden-chart"));
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: BalancedInterestAccrualTemplate(),
            Actor: "ops-user",
            CorrelationId: "config-golden-template"));
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-accrual",
                DisplayName: "Daily interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "template-interest-accrual",
                Description: "Maps daily interest accrual events to the balanced interest template."),
            Actor: "ops-user",
            CorrelationId: "config-golden-rule"));

        var activated = await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            CorrelationId: "config-golden-activate"));
        var audit = await service.ListAuditAsync("fund-alpha");

        activated.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activated.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        audit.Select(item => item.Action).Should().Contain(new[]
        {
            "chart.upsert",
            "template.upsert",
            "posting-rule.upsert",
            "configuration.activate"
        });
        audit.Should().OnlyContain(item => item.Actor.Length > 0);
        audit.Should().OnlyContain(item => item.BeforeHash.Length > 0 && item.AfterHash.Length > 0);
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_BalancedDraftSavesAndSubmitsApproval()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry();

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version));

        saved.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        saved.TotalDebits.Should().Be(100m);
        saved.TotalCredits.Should().Be(100m);
        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        submitted.ApprovalId.Should().Be("manual-je-approval-" + submitted.JournalEntryId.ToString("N"));
        submitted.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.Drafts.Should().ContainSingle(item => item.JournalEntryId == submitted.JournalEntryId && item.Status == ManualJournalEntryStatusDto.Submitted);
        workbench.AuditTrail.Select(item => item.Action).Should().Contain(new[] { "manual-je.save-draft", "manual-je.submit-approval" });
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_UnbalancedDraftCannotSubmitApproval()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry() with
        {
            Lines =
            [
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash"),
                new ManualJournalEntryLineDto("credit-income", AccountingTemplateLineSideDto.Credit, 90m, "USD", "Income:Interest")
            ]
        };

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        var act = async () => await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version));

        saved.Status.Should().Be(ManualJournalEntryStatusDto.NeedsFix);
        saved.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.unbalanced");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_SourceEvidenceRequiredBeforeApproval()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry() with
        {
            EvidenceLinks = [],
            EvidenceAttachments = []
        };

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        var act = async () => await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version));

        saved.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");
        var validated = await service.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(saved, "controller"));
        validated.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.evidence-missing");
    }

    [Fact]
    public async Task Scenario_MonthEndSetup_UnbalancedTemplateBlocksActivationAndPreviewIsNonPosting()
    {
        var service = CreateService();

        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("interest-income", "Income:Interest", "Interest Income", "Revenue"),
            Actor: "ops-user"));
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: new JournalEntryTemplateDto(
                TemplateId: "template-interest-accrual",
                DisplayName: "Interest accrual",
                Description: "Deliberately unbalanced template.",
                Lines:
                [
                    new JournalEntryTemplateLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 100m),
                    new JournalEntryTemplateLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, 90m)
                ]),
            Actor: "ops-user"));
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto("rule-interest-accrual", "Daily interest accrual", "InterestAccrual", "template-interest-accrual"),
            Actor: "ops-user"));

        var preview = await service.PreviewTemplateAsync(new PreviewJournalTemplateRequest(
            FundProfileId: "fund-alpha",
            TemplateId: "template-interest-accrual",
            Actor: "ops-user"));
        var act = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller"));

        preview.IsBalanced.Should().BeFalse();
        preview.TotalDebits.Should().Be(100m);
        preview.TotalCredits.Should().Be(90m);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");
        var audit = await service.ListAuditAsync("fund-alpha");
        audit.Should().NotContain(item => item.Action == "configuration.activate");
    }

    private static AccountingConfigurationService CreateService()
    {
        return new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
    }

    private static ManualJournalEntryWorkbenchService CreateManualJournalEntryWorkbenchService(
        IAccountingConfigurationService configurationService)
    {
        return new ManualJournalEntryWorkbenchService(
            new InMemoryManualJournalEntryDraftStore(),
            configurationService,
            new InMemoryAccountingActionAuditStore());
    }

    private static async Task SeedBalancedConfigurationAsync(AccountingConfigurationService service)
    {
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("interest-income", "Income:Interest", "Interest Income", "Revenue"),
            Actor: "ops-user"));
    }

    private static ManualJournalEntryDraftDto BalancedManualJournalEntry()
    {
        var now = DateTimeOffset.UtcNow;
        return new ManualJournalEntryDraftDto(
            JournalEntryId: Guid.NewGuid(),
            Status: ManualJournalEntryStatusDto.Draft,
            FundProfileId: "fund-alpha",
            LedgerBookId: Guid.NewGuid(),
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingDate: new DateOnly(2026, 6, 30),
            PeriodId: "2026-06",
            EntityId: "entity-master",
            FundNodeId: "fund-alpha",
            Currency: "USD",
            Memo: "Manual close adjustment",
            PreparedBy: "ops-user",
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            Version: 0,
            Lines:
            [
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash", SecurityId: Guid.NewGuid()),
                new ManualJournalEntryLineDto("credit-income", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Income:Interest")
            ],
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je"],
            ValidationIssues: [],
            EvidenceAttachments:
            [
                new ManualJournalEntryEvidenceAttachmentDto(
                    "source-doc-1",
                    "Controller support package",
                    "SourceDocument",
                    "/api/workstation/evidence/subjects/accounting-record/manual-je",
                    "EvidenceVault",
                    now,
                    "ops-user")
            ]);
    }

    private static JournalEntryTemplateDto BalancedInterestAccrualTemplate()
    {
        return new JournalEntryTemplateDto(
            TemplateId: "template-interest-accrual",
            DisplayName: "Interest accrual",
            Description: "Recognize daily accrued interest.",
            Lines:
            [
                new JournalEntryTemplateLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 100m),
                new JournalEntryTemplateLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, 100m)
            ]);
    }
}
