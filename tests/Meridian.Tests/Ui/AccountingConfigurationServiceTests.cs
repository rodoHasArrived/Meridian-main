using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
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
    public async Task Scenario_ManualJournalEntry_TypedDraftsPreserveAccrualPrepaidAndAmortizationTypes()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var accrued = BalancedManualJournalEntry() with
        {
            EntryType = ManualJournalEntryTypeDto.AccruedBalance,
            Memo = "Accrued balance close entry",
            Lines =
            [
                new ManualJournalEntryLineDto("debit-expense", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Expenses:Operating Expenses"),
                new ManualJournalEntryLineDto("credit-accrual", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Liabilities:Accrued Expenses")
            ]
        };
        var prepaid = BalancedManualJournalEntry() with
        {
            EntryType = ManualJournalEntryTypeDto.PrepaidExpense,
            Memo = "Prepaid insurance entry",
            Lines =
            [
                new ManualJournalEntryLineDto("debit-prepaid", AccountingTemplateLineSideDto.Debit, 250m, "USD", "Assets:Prepaid Expenses"),
                new ManualJournalEntryLineDto("credit-cash", AccountingTemplateLineSideDto.Credit, 250m, "USD", "Assets:Cash")
            ]
        };
        var amortization = BalancedManualJournalEntry() with
        {
            EntryType = ManualJournalEntryTypeDto.Amortization,
            Memo = "Monthly amortization entry",
            Lines =
            [
                new ManualJournalEntryLineDto("debit-amortization-expense", AccountingTemplateLineSideDto.Debit, 75m, "USD", "Expenses:Amortization Expense"),
                new ManualJournalEntryLineDto("credit-accumulated-amortization", AccountingTemplateLineSideDto.Credit, 75m, "USD", "Assets:Accumulated Amortization")
            ]
        };

        var savedAccrued = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(accrued, "ops-user"));
        var savedPrepaid = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(prepaid, "ops-user"));
        var savedAmortization = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(amortization, "ops-user"));
        var workbench = await service.GetWorkbenchAsync("fund-alpha");

        savedAccrued.EntryType.Should().Be(ManualJournalEntryTypeDto.AccruedBalance);
        savedPrepaid.EntryType.Should().Be(ManualJournalEntryTypeDto.PrepaidExpense);
        savedAmortization.EntryType.Should().Be(ManualJournalEntryTypeDto.Amortization);
        workbench.Drafts.Select(draft => draft.EntryType).Should().Contain(new[]
        {
            ManualJournalEntryTypeDto.AccruedBalance,
            ManualJournalEntryTypeDto.PrepaidExpense,
            ManualJournalEntryTypeDto.Amortization
        });
        workbench.Drafts.Should().OnlyContain(draft => draft.ValidationIssues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical));
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_PrivateCapitalTreasuryContextSavesAndSubmitsApproval()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry() with
        {
            EntryType = ManualJournalEntryTypeDto.CapitalCall,
            Memo = "Capital call for Fund Alpha LP",
            Lines =
            [
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash"),
                new ManualJournalEntryLineDto("credit-capital", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Equity:Capital Contributions")
            ],
            TreasuryContext = new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "manual-je:fund-alpha:capital-call:20260630",
                FundEventId: "fund-event:fund-alpha:capital-call:20260630",
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                PaymentIntentId: "payment:fund-alpha:capital-call:20260630",
                SettlementReference: "settlement:fund-alpha:capital-call:20260630")
        };

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version));

        saved.EntryType.Should().Be(ManualJournalEntryTypeDto.CapitalCall);
        saved.TreasuryContext.Should().NotBeNull();
        saved.TreasuryContext!.IdempotencyKey.Should().Be("manual-je:fund-alpha:capital-call:20260630");
        saved.TreasuryContext.CapitalAccountId.Should().Be("capital-account:fund-alpha:lp-1");
        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        submitted.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.PrivateCapitalActivity.Should().NotBeNull();
        workbench.PrivateCapitalActivity!.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.EntryType == ManualJournalEntryTypeDto.CapitalCall &&
            item.JournalStatus == ManualJournalEntryStatusDto.Submitted &&
            item.NetCapitalActivity == 100m);
        workbench.PrivateCapitalActivity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.Contributions == 100m &&
            item.NetActivity == 100m);
        workbench.PrivateCapitalActivity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.NetCapitalActivity == 100m &&
            item.RunningNetActivity == 100m);
        workbench.PrivateCapitalActivity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.TotalDebits == 100m &&
            item.TotalCredits == 100m &&
            item.IsBalanced &&
            item.IsPostingReady &&
            item.Lines.Count == 2);
        workbench.PrivateCapitalActivity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.ReportOutputType == "CapitalCallNotice" &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.EvidenceLinkCount == 1 &&
            item.IsReportReady);
        workbench.PrivateCapitalActivity.SubmittedFundEventCount.Should().Be(1);
        workbench.PrivateCapitalActivity.ApprovalQueueCount.Should().Be(1);
        var directActivity = await service.GetPrivateCapitalActivityAsync("fund-alpha");
        directActivity.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.JournalStatus == ManualJournalEntryStatusDto.Submitted);
        directActivity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.NetActivity == 100m);
        directActivity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.NetCapitalActivity == 100m &&
            item.RunningNetActivity == 100m);
        directActivity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.TotalDebits == 100m &&
            item.TotalCredits == 100m &&
            item.IsPostingReady);
        directActivity.ReportOutputs.Should().ContainSingle(item =>
            item.ReportOutputType == "CapitalCallNotice" &&
            item.IsReportReady);
    }

    [Fact]
    public async Task Scenario_PrivateCapitalActivityProjection_IncludesPostedFundEventLedgerCapitalAccountAndPublishedReportOutput()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.NewGuid();
        var cashLedgerEntryId = Guid.NewGuid();
        var capitalLedgerEntryId = Guid.NewGuid();
        var reportPackId = Guid.NewGuid();
        const string fundEventId = "fund-event:fund-alpha:capital-call:posted";
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            "Posted Fund Alpha capital call",
            [
                new LedgerEntry(
                    cashLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    250000m,
                    0m,
                    "Posted Fund Alpha capital call"),
                new LedgerEntry(
                    capitalLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Capital Contributions", LedgerAccountType.Equity, FinancialAccountId: "capital-account:fund-alpha:lp-1"),
                    0m,
                    250000m,
                    "Posted Fund Alpha capital call")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "posted:fund-alpha:capital-call:20260630",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                PaymentIntentId: "payment:fund-alpha:posted-capital-call",
                SettlementReference: "settlement:fund-alpha:posted-capital-call",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "/api/workstation/evidence/subjects/private-capital/capital-call-source",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:capital-call-controller",
                    ["approvedBy"] = "controller"
                }));
        var journalStore = new PostedPrivateCapitalLedgerJournalStore(
            new LedgerBookRecord(
                ledgerBookId,
                "fund-alpha",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Fund Alpha GAAP book",
                "USD",
                timestamp,
                timestamp),
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                6,
                "2026-06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Closed",
                timestamp,
                timestamp,
                1),
            new LedgerJournalEntryRecord(
                journal,
                Guid.NewGuid(),
                periodId,
                CommandId: null,
                CorrelationId: null,
                GlobalSequence: 1,
                CreatedAt: timestamp));
        var workflowService = new ReportPackWorkflowService(new StaticReportPackWorkflowRecordStore(
            new ReportPackWorkflowRecordDto(
                reportPackId,
                "fund-alpha",
                "capital-account:fund-alpha:lp-1",
                "2026-06",
                new VersionedReportTemplateIdDto("CapitalAccountStatement", 1),
                ReportPackWorkflowStateDto.Published,
                3,
                timestamp,
                "controller",
                timestamp,
                [new ReportPackAuditEventDto(timestamp, "controller", "publish", ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.Published)],
                null,
                LineProvenance:
                [
                    new ReportPackLineProvenanceDto(
                        "capital-account.contribution",
                        "ledger",
                        fundEventId,
                        "ledger-evidence-1",
                        LedgerEntryId: capitalLedgerEntryId.ToString("D"),
                        ReportValue: "250000",
                        ApprovalId: "approval:capital-call-controller")
                ],
                Publication: new ReportPackPublicationManifestDto(
                    "manifest-capital-call-1",
                    "/retained/report-packs/capital-call-1.json",
                    "sha256:capital-call",
                    "controller",
                    timestamp,
                    [new ReportPackEvidenceLinkDto("publication-evidence-1", "Publication manifest", "/api/workstation/evidence/report-packs/capital-call-1", "EvidenceVault", timestamp)]))));
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore, workflowService);

        var activity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ledgerBookId);

        activity.FundEventCount.Should().Be(1);
        activity.PostedFundEventCount.Should().Be(1);
        activity.PublishedReportOutputCount.Should().Be(1);
        activity.NetCapitalActivity.Should().Be(250000m);
        activity.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.JournalStatus == ManualJournalEntryStatusDto.Approved &&
            item.IsPosted &&
            item.NetCapitalActivity == 250000m &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        activity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.Contributions == 250000m &&
            item.NetActivity == 250000m);
        activity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            item.IsPosted &&
            item.NetCapitalActivity == 250000m &&
            item.RunningNetActivity == 250000m &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        activity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            item.IsPostingReady &&
            item.LineCount == 2);
        activity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.ReportOutputType == "GovernedReportPack" &&
            item.DisplayName == "CapitalAccountStatement v1" &&
            item.IsReportReady &&
            item.IsPublished &&
            item.ReportPackId == reportPackId.ToString("D") &&
            item.ReportWorkflowState == ReportPackWorkflowStateDto.Published.ToString() &&
            item.PublicationManifestId == "manifest-capital-call-1" &&
            item.RetainedManifestPath == "/retained/report-packs/capital-call-1.json" &&
            item.PublicationEvidenceHash == "sha256:capital-call" &&
            item.PublishedAtUtc == timestamp &&
            item.PublishedBy == "controller" &&
            item.ReportLineProvenanceCount == 1 &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/report-packs/capital-call-1"));
        activity.ValidationIssues.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_PrivateCapitalDraftRequiresTreasuryContextBeforeApproval()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry() with
        {
            EntryType = ManualJournalEntryTypeDto.Distribution,
            Lines =
            [
                new ManualJournalEntryLineDto("debit-distribution", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Equity:Distributions"),
                new ManualJournalEntryLineDto("credit-cash", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Assets:Cash")
            ],
            TreasuryContext = null
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
        validated.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.treasury-context-missing");
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.PrivateCapitalActivity.Should().NotBeNull();
        workbench.PrivateCapitalActivity!.FundEvents.Should().BeEmpty();
        workbench.PrivateCapitalActivity.LedgerImpacts.Should().BeEmpty();
        workbench.PrivateCapitalActivity.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.private-capital-context-pending");
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
        IAccountingConfigurationService configurationService,
        ILedgerJournalStore? journalStore = null,
        ReportPackWorkflowService? reportPackWorkflowService = null)
    {
        return new ManualJournalEntryWorkbenchService(
            new InMemoryManualJournalEntryDraftStore(),
            configurationService,
            new InMemoryAccountingActionAuditStore(),
            journalStore: journalStore,
            reportPackWorkflowService: reportPackWorkflowService);
    }

    private sealed class PostedPrivateCapitalLedgerJournalStore(
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        LedgerJournalEntryRecord record) : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            throw new NotSupportedException("Test store is read-only.");

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                periodId == period.PeriodId ? [record] : []);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                record.AggregateId == aggregateId ? [record] : []);
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(periodId == period.PeriodId ? period : null);
        }

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches =
                (!ledgerBookId.HasValue || period.LedgerBookId == ledgerBookId.Value) &&
                (string.IsNullOrWhiteSpace(status) || string.Equals(period.Status, status, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value);
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(matches ? [period] : []);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Test store is read-only.");

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerBookRecord?>(ledgerBookId == book.LedgerBookId ? book : null);
        }

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var matches =
                (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value) &&
                (!fundStructureNodeKind.HasValue || book.FundStructureNodeKind == fundStructureNodeKind.Value);
            return Task.FromResult<IReadOnlyList<LedgerBookRecord>>(matches ? [book] : []);
        }

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default) =>
            throw new NotSupportedException("Test store is read-only.");
    }

    private sealed class StaticReportPackWorkflowRecordStore(params ReportPackWorkflowRecordDto[] records) : IReportPackWorkflowRecordStore
    {
        private IReadOnlyList<ReportPackWorkflowRecordDto> _records = records;

        public IReadOnlyList<ReportPackWorkflowRecordDto> Load() => _records;

        public void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records) => _records = records.ToArray();
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
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("accrued-expenses", "Liabilities:Accrued Expenses", "Accrued Expenses", "Liability"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("prepaid-expenses", "Assets:Prepaid Expenses", "Prepaid Expenses", "Asset"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("operating-expenses", "Expenses:Operating Expenses", "Operating Expenses", "Expense"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("amortization-expense", "Expenses:Amortization Expense", "Amortization Expense", "Expense"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("accumulated-amortization", "Assets:Accumulated Amortization", "Accumulated Amortization", "ContraAsset"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("capital-contributions", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
            Actor: "ops-user"));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("distributions", "Equity:Distributions", "Distributions", "Equity"),
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
