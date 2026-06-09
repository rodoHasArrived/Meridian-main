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
    public void PrivateCapitalActivityProjection_NormalizesOmittedFundEventRecordsToEmptyList()
    {
        var projection = new PrivateCapitalActivityProjectionDto(
            "fund-alpha",
            null,
            new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero),
            FundEventCount: 0,
            CapitalAccountCount: 0,
            SubmittedFundEventCount: 0,
            ApprovalQueueCount: 0,
            PostedFundEventCount: 0,
            PublishedReportOutputCount: 0,
            NetCapitalActivity: 0m,
            Currency: "USD",
            FundEvents: [],
            CapitalAccounts: [],
            CapitalAccountSubledgerEntries: [],
            LedgerImpacts: [],
            ReportOutputs: [],
            ValidationIssues: []);

        projection.FundEventRecords.Should().BeEmpty();
        projection.CapitalAccountSubledgers.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(PrivateCapitalFundEventLedgerReadinessCases))]
    public void PrivateCapitalFundEventLedgerRecordBuilder_DerivesSharedReadinessAndNextAction(
        PrivateCapitalFundEventLedgerReadinessCase testCase)
    {
        var record = BuildPrivateCapitalFundEventLedgerRecord(testCase);

        record.Readiness.Should().Be(testCase.ExpectedReadiness);
        record.ReadinessLabel.Should().Be(testCase.ExpectedLabel);
        record.NextAction.Should().Be(testCase.ExpectedNextAction);
        record.NextActionRoute.Should().Contain(testCase.ExpectedNextActionRouteFragment);
        record.EvidenceLinkCount.Should().Be(testCase.ExpectedEvidenceCount);
        record.ValidationIssueCount.Should().Be(testCase.ExpectedValidationIssueCount);
        record.CapitalAccountSubledgerEntryCount.Should().Be(1);
        record.LedgerImpactCount.Should().Be(testCase.IncludeLedgerImpact ? 1 : 0);
        record.ReportOutputCount.Should().Be(testCase.IncludeReportOutput ? 1 : 0);
    }

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
            item.ApprovalId == submitted.ApprovalId &&
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
        workbench.PrivateCapitalActivity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.JournalEntryId == submitted.JournalEntryId &&
            item.GrossAmount == 100m &&
            item.CapitalAccountOpeningNetActivity == 0m &&
            item.CapitalAccountEndingNetActivity == 100m &&
            item.Memo == "Capital call for Fund Alpha LP" &&
            item.PaymentIntentId == "payment:fund-alpha:capital-call:20260630" &&
            item.SettlementReference == "settlement:fund-alpha:capital-call:20260630" &&
            item.ActivityRoute.Contains("fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase) &&
            item.ActivityRoute.Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.ActivityRoute.Contains("investorId=investor%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.EvidenceRoute.Contains("/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet", StringComparison.OrdinalIgnoreCase) &&
            item.ApprovalId == submitted.ApprovalId &&
            item.ApprovalRoute != null &&
            item.ApprovalRoute.Contains("approvalId=" + submitted.ApprovalId, StringComparison.OrdinalIgnoreCase) &&
            item.ApprovalRoute.Contains("journalEntryId=" + submitted.JournalEntryId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1 &&
            item.ReportOutputCount == 1 &&
            item.ValidationIssueCount == 0 &&
            item.PrimaryReportOutputType == "CapitalCallNotice" &&
            item.PrimaryReportRoute != null &&
            item.PrimaryReportRoute.Contains("fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase) &&
            item.ReportWorkflowState == ManualJournalEntryStatusDto.Submitted.ToString() &&
            item.ReportLineProvenanceCount == 0 &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Ready &&
            item.ReadinessLabel == "Ready" &&
            item.NextAction == "Review report output" &&
            item.NextActionRoute == item.PrimaryReportRoute &&
            item.EvidenceLinkCount == 1 &&
            item.CapitalAccountSubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1 &&
            item.IsPostingReady &&
            item.IsReportReady &&
            !item.IsPublished);
        workbench.PrivateCapitalActivity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.Currency == "USD" &&
            item.Contributions == 100m &&
            item.OpeningNetActivity == 0m &&
            item.EndingNetActivity == 100m &&
            item.NetCapitalActivity == 100m &&
            item.FundEventCount == 1 &&
            item.ApprovalQueueCount == 1 &&
            item.PostedFundEventCount == 0 &&
            item.PublishedReportOutputCount == 0 &&
            item.EvidenceLinkCount == 1 &&
            item.ValidationIssueCount == 0 &&
            item.ActivityRoute.Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.CapitalAccount != null &&
            item.FundEventRecords.Count == 1 &&
            item.SubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
        workbench.PrivateCapitalActivity.SubmittedFundEventCount.Should().Be(1);
        workbench.PrivateCapitalActivity.ApprovalQueueCount.Should().Be(1);
        var directActivity = await service.GetPrivateCapitalActivityAsync("fund-alpha");
        directActivity.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.JournalStatus == ManualJournalEntryStatusDto.Submitted &&
            item.ApprovalId == submitted.ApprovalId);
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
        directActivity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == "fund-event:fund-alpha:capital-call:20260630" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Submitted &&
            item.JournalEntryId == submitted.JournalEntryId &&
            item.CapitalAccountOpeningNetActivity == 0m &&
            item.CapitalAccountEndingNetActivity == 100m &&
            item.ActivityRoute.Contains("fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase) &&
            item.ActivityRoute.Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.EvidenceRoute.Contains("/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet", StringComparison.OrdinalIgnoreCase) &&
            item.ApprovalId == submitted.ApprovalId &&
            item.ApprovalRoute != null &&
            item.ApprovalRoute.Contains("approvalId=" + submitted.ApprovalId, StringComparison.OrdinalIgnoreCase) &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1 &&
            item.ReportOutputCount == 1 &&
            item.PrimaryReportOutputType == "CapitalCallNotice" &&
            item.ReportWorkflowState == ManualJournalEntryStatusDto.Submitted.ToString() &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Ready &&
            item.ReadinessLabel == "Ready" &&
            item.NextAction == "Review report output" &&
            item.NextActionRoute == item.PrimaryReportRoute &&
            item.CapitalAccountSubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
        directActivity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.Contributions == 100m &&
            item.OpeningNetActivity == 0m &&
            item.EndingNetActivity == 100m &&
            item.FundEventRecords.Count == 1 &&
            item.SubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
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
                "EUR",
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
        activity.Currency.Should().Be("EUR");
        activity.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.JournalStatus == ManualJournalEntryStatusDto.Approved &&
            item.ApprovalId == "approval:capital-call-controller" &&
            item.IsPosted &&
            item.Currency == "EUR" &&
            item.NetCapitalActivity == 250000m &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        activity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.Currency == "EUR" &&
            item.Contributions == 250000m &&
            item.NetActivity == 250000m);
        activity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            item.IsPosted &&
            item.Currency == "EUR" &&
            item.NetCapitalActivity == 250000m &&
            item.RunningNetActivity == 250000m &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        activity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            item.Currency == "EUR" &&
            item.Lines.All(line => line.Currency == "EUR") &&
            item.IsPostingReady &&
            item.LineCount == 2);
        activity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.ReportOutputType == "GovernedReportPack" &&
            item.DisplayName == "CapitalAccountStatement v1" &&
            item.Currency == "EUR" &&
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
        activity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.JournalEntryId == journalEntryId &&
            item.Currency == "EUR" &&
            item.GrossAmount == 250000m &&
            item.NetCapitalActivity == 250000m &&
            item.CapitalAccountOpeningNetActivity == 0m &&
            item.CapitalAccountEndingNetActivity == 250000m &&
            item.Memo == "Posted Fund Alpha capital call" &&
            item.PaymentIntentId == "payment:fund-alpha:posted-capital-call" &&
            item.SettlementReference == "settlement:fund-alpha:posted-capital-call" &&
            item.ActivityRoute.Contains("fundEventId=fund-event%3Afund-alpha%3Acapital-call%3Aposted", StringComparison.OrdinalIgnoreCase) &&
            item.ActivityRoute.Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase) &&
            item.EvidenceRoute.Contains("/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3Aposted/packet", StringComparison.OrdinalIgnoreCase) &&
            item.ApprovalId == "approval:capital-call-controller" &&
            item.ApprovalRoute != null &&
            item.ApprovalRoute.Contains("approvalId=approval%3Acapital-call-controller", StringComparison.OrdinalIgnoreCase) &&
            item.IsPosted &&
            item.IsPostingReady &&
            item.IsReportReady &&
            item.IsPublished &&
            item.EvidenceLinkCount == 3 &&
            item.CapitalAccountSubledgerEntryCount == 1 &&
            item.LedgerImpactCount == 1 &&
            item.ReportOutputCount == 1 &&
            item.ValidationIssueCount == 0 &&
            item.PrimaryReportOutputType == "GovernedReportPack" &&
            item.PrimaryReportOutputId == $"report-output:{fundEventId}:{reportPackId:D}".ToLowerInvariant() &&
            item.PrimaryReportRoute == $"/api/fund-structure/report-packs/{reportPackId:D}" &&
            item.ReportWorkflowState == ReportPackWorkflowStateDto.Published.ToString() &&
            item.PublicationManifestId == "manifest-capital-call-1" &&
            item.RetainedManifestPath == "/retained/report-packs/capital-call-1.json" &&
            item.ReportLineProvenanceCount == 1 &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Published &&
            item.ReadinessLabel == "Published" &&
            item.NextAction == "Open published report" &&
            item.NextActionRoute == item.PrimaryReportRoute &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source") &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/report-packs/capital-call-1") &&
            item.EvidenceLinks.Contains("ledger-evidence-1") &&
            item.CapitalAccountSubledgerEntries.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
        activity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.Currency == "EUR" &&
            item.FundEventRecords.Single().Currency == "EUR" &&
            item.SubledgerEntries.Single().Currency == "EUR" &&
            item.LedgerImpacts.Single().Currency == "EUR" &&
            item.ReportOutputs.Single().Currency == "EUR");
        activity.ValidationIssues.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_PrivateCapitalActivityProjection_DoesNotMarkPublishedReportReadyWhenPostedEventIsNotPostingReady()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.NewGuid();
        var cashLedgerEntryId = Guid.NewGuid();
        var payableLedgerEntryId = Guid.NewGuid();
        var reportPackId = Guid.NewGuid();
        const string fundEventId = "fund-event:fund-alpha:capital-call:posted-incomplete";
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            "Incomplete posted Fund Alpha capital call",
            [
                new LedgerEntry(
                    cashLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    100000m,
                    0m,
                    "Incomplete posted Fund Alpha capital call"),
                new LedgerEntry(
                    payableLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Subscription Payable", LedgerAccountType.Liability, FinancialAccountId: "liability:subscription-payable"),
                    0m,
                    100000m,
                    "Incomplete posted Fund Alpha capital call")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "posted:fund-alpha:capital-call:incomplete",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                InvestorId: "investor:lp-1",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "/api/workstation/evidence/subjects/private-capital/incomplete-capital-call-source",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:incomplete-capital-call-controller",
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
                        "ledger-evidence-incomplete",
                        LedgerEntryId: payableLedgerEntryId.ToString("D"),
                        ReportValue: "100000",
                        ApprovalId: "approval:incomplete-capital-call-controller")
                ],
                Publication: new ReportPackPublicationManifestDto(
                    "manifest-incomplete-capital-call",
                    "/retained/report-packs/incomplete-capital-call.json",
                    "sha256:incomplete-capital-call",
                    "controller",
                    timestamp,
                    [new ReportPackEvidenceLinkDto("publication-evidence-incomplete", "Publication manifest", "/api/workstation/evidence/report-packs/incomplete-capital-call", "EvidenceVault", timestamp)]))));
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore, workflowService);

        var activity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ledgerBookId);

        activity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.IsPublished &&
            !item.IsReportReady &&
            item.ReportPackId == reportPackId.ToString("D") &&
            item.ValidationIssues.Any(issue => issue.Code == "private-capital.report-output-posting-not-ready"));
        activity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.IsPublished &&
            !item.IsPostingReady &&
            !item.IsReportReady &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Blocked &&
            item.ValidationIssues.Any(issue => issue.Code == "private-capital.capital-account-missing") &&
            item.ValidationIssues.Any(issue => issue.Code == "private-capital.capital-account-impact-missing") &&
            item.ReportOutputs.Single().ValidationIssues.Any(issue => issue.Code == "private-capital.report-output-posting-not-ready"));
        activity.PublishedReportOutputCount.Should().Be(1);
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

    public static TheoryData<PrivateCapitalFundEventLedgerReadinessCase> PrivateCapitalFundEventLedgerReadinessCases()
        => new()
        {
            new(
                Suffix: "blocked-critical",
                ApprovalState: ManualJournalEntryStatusDto.Submitted,
                IncludeFundEventEvidence: true,
                IncludeLedgerImpact: true,
                LedgerPostingReady: true,
                IncludeReportOutput: true,
                ReportReady: true,
                ReportPublished: false,
                HasCriticalIssue: true,
                ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.Blocked,
                ExpectedLabel: "Blocked",
                ExpectedNextAction: "Repair fund event",
                ExpectedNextActionRouteFragment: "approvalId=approval%3Ablocked-critical",
                ExpectedEvidenceCount: 1,
                ExpectedValidationIssueCount: 1),
            new(
                Suffix: "evidence-missing",
                ApprovalState: ManualJournalEntryStatusDto.Submitted,
                IncludeFundEventEvidence: false,
                IncludeLedgerImpact: true,
                LedgerPostingReady: true,
                IncludeReportOutput: true,
                ReportReady: true,
                ReportPublished: false,
                HasCriticalIssue: false,
                ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing,
                ExpectedLabel: "Evidence missing",
                ExpectedNextAction: "Attach retained evidence",
                ExpectedNextActionRouteFragment: "/api/workstation/evidence/subjects/private-capital-fund-event/",
                ExpectedEvidenceCount: 0,
                ExpectedValidationIssueCount: 0),
            new(
                Suffix: "approval-pending",
                ApprovalState: ManualJournalEntryStatusDto.Draft,
                IncludeFundEventEvidence: true,
                IncludeLedgerImpact: true,
                LedgerPostingReady: true,
                IncludeReportOutput: true,
                ReportReady: true,
                ReportPublished: false,
                HasCriticalIssue: false,
                ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.ApprovalPending,
                ExpectedLabel: "Approval pending",
                ExpectedNextAction: "Submit approval",
                ExpectedNextActionRouteFragment: "/api/ledger/private-capital/activity",
                ExpectedEvidenceCount: 1,
                ExpectedValidationIssueCount: 0),
            new(
                Suffix: "posting-review",
                ApprovalState: ManualJournalEntryStatusDto.Submitted,
                IncludeFundEventEvidence: true,
                IncludeLedgerImpact: true,
                LedgerPostingReady: false,
                IncludeReportOutput: true,
                ReportReady: true,
                ReportPublished: false,
                HasCriticalIssue: false,
                ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.PostingReview,
                ExpectedLabel: "Posting review",
                ExpectedNextAction: "Review ledger impact",
                ExpectedNextActionRouteFragment: "/api/ledger/private-capital/activity",
                ExpectedEvidenceCount: 1,
                ExpectedValidationIssueCount: 0),
            new(
                Suffix: "report-output-missing",
                ApprovalState: ManualJournalEntryStatusDto.Submitted,
                IncludeFundEventEvidence: true,
                IncludeLedgerImpact: true,
                LedgerPostingReady: true,
                IncludeReportOutput: false,
                ReportReady: false,
                ReportPublished: false,
                HasCriticalIssue: false,
                ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.ReportReview,
                ExpectedLabel: "Report output missing",
                ExpectedNextAction: "Prepare report output",
                ExpectedNextActionRouteFragment: "/api/ledger/private-capital/activity",
                ExpectedEvidenceCount: 1,
                ExpectedValidationIssueCount: 0)
        };

    public sealed record PrivateCapitalFundEventLedgerReadinessCase(
        string Suffix,
        ManualJournalEntryStatusDto ApprovalState,
        bool IncludeFundEventEvidence,
        bool IncludeLedgerImpact,
        bool LedgerPostingReady,
        bool IncludeReportOutput,
        bool ReportReady,
        bool ReportPublished,
        bool HasCriticalIssue,
        PrivateCapitalFundEventLedgerReadinessDto ExpectedReadiness,
        string ExpectedLabel,
        string ExpectedNextAction,
        string ExpectedNextActionRouteFragment,
        int ExpectedEvidenceCount,
        int ExpectedValidationIssueCount);

    private static PrivateCapitalFundEventLedgerRecordDto BuildPrivateCapitalFundEventLedgerRecord(
        PrivateCapitalFundEventLedgerReadinessCase testCase)
    {
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var effectiveDate = new DateOnly(2026, 6, 30);
        var journalEntryId = Guid.NewGuid();
        var fundEventId = $"fund-event:fund-alpha:{testCase.Suffix}";
        var validationIssues = testCase.HasCriticalIssue
            ? [new AccountingConfigurationValidationIssueDto("private-capital.critical", AccountingConfigurationValidationSeverityDto.Critical, "Critical event issue.", fundEventId)]
            : Array.Empty<AccountingConfigurationValidationIssueDto>();
        var eventEvidence = testCase.IncludeFundEventEvidence
            ? [$"/api/workstation/evidence/subjects/private-capital/{testCase.Suffix}"]
            : Array.Empty<string>();
        var fundEvent = new PrivateCapitalFundEventDto(
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            testCase.ApprovalState,
            journalEntryId,
            effectiveDate,
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            100m,
            100m,
            "Fund Alpha capital call",
            "payment:fund-alpha:capital-call",
            "settlement:fund-alpha:capital-call",
            eventEvidence,
            validationIssues,
            timestamp,
            ApprovalId: "approval:" + testCase.Suffix);
        var subledgerEntry = new PrivateCapitalCapitalAccountSubledgerEntryDto(
            $"subledger:{testCase.Suffix}",
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            testCase.ApprovalState,
            journalEntryId,
            effectiveDate,
            100m,
            100m,
            100m,
            "Fund Alpha capital call",
            [],
            [],
            timestamp);
        var ledgerImpacts = testCase.IncludeLedgerImpact
            ?
            [
                new PrivateCapitalLedgerImpactDto(
                    $"ledger-impact:{testCase.Suffix}",
                    journalEntryId,
                    fundEventId,
                    "CapitalCall",
                    "capital-account:fund-alpha:lp-1",
                    "investor:lp-1",
                    testCase.ApprovalState,
                    effectiveDate,
                    "USD",
                    100m,
                    testCase.LedgerPostingReady ? 100m : 90m,
                    testCase.LedgerPostingReady ? 0m : 10m,
                    2,
                    testCase.LedgerPostingReady,
                    testCase.LedgerPostingReady,
                    [],
                    [
                        new PrivateCapitalLedgerLineImpactDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "entity-master", null, null, null),
                        new PrivateCapitalLedgerLineImpactDto("credit-capital", "Equity:Capital Contributions", AccountingTemplateLineSideDto.Credit, testCase.LedgerPostingReady ? 100m : 90m, "USD", "entity-master", null, null, null)
                    ],
                    [])
            ]
            : Array.Empty<PrivateCapitalLedgerImpactDto>();
        var reportOutputs = testCase.IncludeReportOutput
            ?
            [
                new PrivateCapitalReportOutputDto(
                    $"report-output:{testCase.Suffix}",
                    "CapitalCallNotice",
                    "Capital call notice",
                    $"/api/fund-structure/report-packs/{testCase.Suffix}",
                    fundEventId,
                    "CapitalCall",
                    "capital-account:fund-alpha:lp-1",
                    "investor:lp-1",
                    testCase.ApprovalState,
                    effectiveDate,
                    "USD",
                    100m,
                    0,
                    [],
                    testCase.ReportReady,
                    [],
                    IsPublished: testCase.ReportPublished,
                    ReportWorkflowState: testCase.ReportPublished ? "Published" : "Submitted")
            ]
            : Array.Empty<PrivateCapitalReportOutputDto>();

        var records = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            "fund-alpha",
            [fundEvent],
            [subledgerEntry],
            ledgerImpacts,
            reportOutputs);

        records.Should().ContainSingle();
        return records[0];
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
