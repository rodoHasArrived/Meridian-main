using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.Banking;
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

    [Fact]
    public void PrivateCapitalActivityProjection_CountsPublishedLedgerAndWorkflowReportOutputOnce()
    {
        var reportPackId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var journalEntryId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var ledgerEntryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        const string fundEventId = "fund-event:fund-alpha:capital-call:published";
        var postedEvent = new PrivateCapitalFundEventLedgerEvent(
            fundEventId,
            "CapitalCall",
            new DateOnly(2026, 6, 30),
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "payment:fund-alpha:published",
            "settlement:fund-alpha:published",
            "posted:fund-alpha:published",
            timestamp,
            timestamp,
            PrivateCapitalFundEventApprovalState.Posted,
            "approval:fund-alpha:published",
            "controller",
            [journalEntryId],
            [ledgerEntryId],
            [],
            [],
            ["/api/workstation/evidence/subjects/private-capital/published"],
            [
                new PrivateCapitalFundEventReportOutput(
                    $"{reportPackId:D}:private-capital:{fundEventId}",
                    reportPackId.ToString("D"),
                    LedgerReportPackLifecycleStatus.Published,
                    timestamp,
                    "sha256:published",
                    ["capital-account-statement.pdf"],
                    ["/api/workstation/evidence/report-packs/published"],
                    IsPublished: true)
            ],
            []);
        var workflowRecord = new ReportPackWorkflowRecordDto(
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
                    "ledger-evidence-published",
                    LedgerEntryId: ledgerEntryId.ToString("D"),
                    ReportValue: "100000",
                    ApprovalId: "approval:fund-alpha:published")
            ],
            Publication: new ReportPackPublicationManifestDto(
                "manifest-published",
                "/retained/report-packs/published.json",
                "sha256:published",
                "controller",
                timestamp,
                [new ReportPackEvidenceLinkDto("publication-evidence-published", "Publication manifest", "/api/workstation/evidence/report-packs/published", "EvidenceVault", timestamp)]));

        var count = InvokePublishedReportOutputCount("fund-alpha", [postedEvent], [workflowRecord]);

        count.Should().Be(1);
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

    [Theory]
    [InlineData(
        "blocked-critical",
        PrivateCapitalFundEventLedgerReadinessDto.Blocked,
        "Blocked",
        "A critical validation issue or blocked fund event prevents capital-account subledger reliance.",
        "Repair capital-account subledger",
        "approvalId=approval%3Ablocked-critical")]
    [InlineData(
        "evidence-missing",
        PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing,
        "Evidence missing",
        "Retained source evidence is missing for one or more fund events in this capital-account subledger.",
        "Attach retained evidence",
        "/api/workstation/evidence/subjects/private-capital-fund-event/")]
    [InlineData(
        "approval-pending",
        PrivateCapitalFundEventLedgerReadinessDto.ApprovalPending,
        "Approval pending",
        "One or more fund events require approval before the capital-account subledger can be treated as posting ready.",
        "Submit or review approval",
        "/api/ledger/private-capital/activity")]
    [InlineData(
        "posting-review",
        PrivateCapitalFundEventLedgerReadinessDto.PostingReview,
        "Posting review",
        "One or more fund events have approval and evidence but are not posting ready in the ledger.",
        "Review ledger impact",
        "/api/ledger/private-capital/activity")]
    [InlineData(
        "report-output-missing",
        PrivateCapitalFundEventLedgerReadinessDto.ReportReview,
        "Report review",
        "Ledger impact is ready, but one or more retained report outputs are not ready for stakeholder use.",
        "Prepare report output",
        "/api/ledger/private-capital/activity")]
    [InlineData(
        "ready-report-output",
        PrivateCapitalFundEventLedgerReadinessDto.Ready,
        "Ready",
        "The capital-account subledger has retained evidence, posting-ready ledger impact, capital-account movement, and report output ready for publication.",
        "Review report output",
        "/api/fund-structure/report-packs/ready-report-output")]
    [InlineData(
        "published-report-output",
        PrivateCapitalFundEventLedgerReadinessDto.Published,
        "Published",
        "All fund events in this capital-account subledger have retained evidence, posting-ready ledger impact, capital-account movement, and published report output.",
        "Open published report",
        "/api/fund-structure/report-packs/published-report-output")]
    public void PrivateCapitalCapitalAccountSubledgerBuilder_DerivesReadinessAndNextAction(
        string caseSuffix,
        PrivateCapitalFundEventLedgerReadinessDto expectedReadiness,
        string expectedLabel,
        string expectedReason,
        string expectedNextAction,
        string expectedRouteFragment)
    {
        var record = BuildPrivateCapitalFundEventLedgerRecord(FindPrivateCapitalFundEventLedgerReadinessCase(caseSuffix));
        var reportOutputs = caseSuffix is "ready-report-output" or "published-report-output"
            ? AddRetainedReportEvidence(record.ReportOutputs)
            : record.ReportOutputs;
        var capitalAccount = new PrivateCapitalCapitalAccountActivityDto(
            record.CapitalAccountId,
            record.InvestorId,
            record.Currency,
            Contributions: 100m,
            Distributions: 0m,
            Subscriptions: 0m,
            Redemptions: 0m,
            ManagementFees: 0m,
            NetActivity: 100m,
            FundEventCount: 1,
            LastEffectiveDate: record.EffectiveDate,
            LastFundEventType: record.FundEventType,
            FundEventIds: [record.FundEventId]);

        var subledger = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            "fund-alpha",
            ledgerBookId: null,
            new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero),
            [capitalAccount],
            [record],
            record.CapitalAccountSubledgerEntries,
            record.LedgerImpacts,
            reportOutputs,
            []).Should().ContainSingle().Subject;

        subledger.Readiness.Should().Be(expectedReadiness);
        subledger.ReadinessLabel.Should().Be(expectedLabel);
        subledger.ReadinessReason.Should().Be(expectedReason);
        subledger.NextAction.Should().Be(expectedNextAction);
        subledger.NextActionRoute.Should().Contain(expectedRouteFragment);
        subledger.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == record.FundEventId &&
            item.Readiness == record.Readiness);
    }

    [Fact]
    public void PrivateCapitalEvidenceCategories_RequireAllLinkedReportOutputsReady()
    {
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var effectiveDate = new DateOnly(2026, 6, 30);
        var journalEntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        const string fundEventId = "fund-event:fund-alpha:capital-call:mixed-report-output";
        var fundEvent = new PrivateCapitalFundEventDto(
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Submitted,
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
            ["/evidence/source.pdf"],
            [],
            timestamp,
            ApprovalId: "approval:mixed-report-output");
        var subledgerEntry = new PrivateCapitalCapitalAccountSubledgerEntryDto(
            "subledger:mixed-report-output",
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            "USD",
            fundEventId,
            fundEvent.FundEventType,
            fundEvent.EntryType,
            fundEvent.JournalStatus,
            journalEntryId,
            effectiveDate,
            100m,
            100m,
            100m,
            fundEvent.Memo,
            ["/evidence/subledger.pdf"],
            [],
            timestamp);
        var ledgerImpact = new PrivateCapitalLedgerImpactDto(
            "ledger-impact:mixed-report-output",
            journalEntryId,
            fundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.JournalStatus,
            effectiveDate,
            "USD",
            100m,
            100m,
            0m,
            2,
            IsBalanced: true,
            IsPostingReady: true,
            ["/evidence/ledger.pdf"],
            [
                new PrivateCapitalLedgerLineImpactDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", null, null, null, "/evidence/ledger-line-debit.pdf"),
                new PrivateCapitalLedgerLineImpactDto("credit-capital", "Equity:Capital Contributions", AccountingTemplateLineSideDto.Credit, 100m, "USD", null, null, null, "/evidence/ledger-line-credit.pdf")
            ],
            []);
        var reportOutputs = new[]
        {
            new PrivateCapitalReportOutputDto(
                "report-output:mixed-report-output:ready",
                "CapitalCallNotice",
                "Ready capital call notice",
                "/api/fund-structure/report-packs/ready",
                fundEventId,
                fundEvent.FundEventType,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                fundEvent.JournalStatus,
                effectiveDate,
                "USD",
                100m,
                1,
                ["/evidence/report-ready.pdf"],
                IsReportReady: true,
                []),
            new PrivateCapitalReportOutputDto(
                "report-output:mixed-report-output:review",
                "DistributionNotice",
                "Report output under review",
                "/api/fund-structure/report-packs/review",
                fundEventId,
                fundEvent.FundEventType,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                fundEvent.JournalStatus,
                effectiveDate,
                "USD",
                100m,
                1,
                ["/evidence/report-review.pdf"],
                IsReportReady: false,
                [])
        };

        var records = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            "fund-alpha",
            [fundEvent],
            [subledgerEntry],
            [ledgerImpact],
            reportOutputs);
        var record = records.Should().ContainSingle().Subject;
        var capitalAccount = new PrivateCapitalCapitalAccountActivityDto(
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            "USD",
            Contributions: 100m,
            Distributions: 0m,
            Subscriptions: 0m,
            Redemptions: 0m,
            ManagementFees: 0m,
            NetActivity: 100m,
            FundEventCount: 1,
            LastEffectiveDate: effectiveDate,
            LastFundEventType: fundEvent.FundEventType,
            FundEventIds: [fundEventId]);
        var subledger = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            "fund-alpha",
            ledgerBookId: null,
            timestamp,
            [capitalAccount],
            records,
            [subledgerEntry],
            [ledgerImpact],
            reportOutputs,
            []);

        record.IsReportReady.Should().BeFalse();
        record.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            !item.IsReady &&
            item.EvidenceLinkCount == 2);
        subledger.Should().ContainSingle().Subject.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            !item.IsReady &&
            item.EvidenceLinkCount == 2);
    }

    [Fact]
    public void PrivateCapitalEvidenceCategories_RequireEachReportOutputToRetainEvidence()
    {
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var effectiveDate = new DateOnly(2026, 6, 30);
        var journalEntryId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        const string fundEventId = "fund-event:fund-alpha:capital-call:partial-report-evidence";
        var fundEvent = new PrivateCapitalFundEventDto(
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Submitted,
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
            ["/evidence/source.pdf"],
            [],
            timestamp,
            ApprovalId: "approval:partial-report-evidence");
        var reportOutputs = new[]
        {
            new PrivateCapitalReportOutputDto(
                "report-output:partial-report-evidence:ready-linked",
                "CapitalCallNotice",
                "Ready linked capital call notice",
                "/api/fund-structure/report-packs/ready-linked",
                fundEventId,
                fundEvent.FundEventType,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                fundEvent.JournalStatus,
                effectiveDate,
                "USD",
                100m,
                1,
                ["/evidence/report-ready.pdf"],
                IsReportReady: true,
                []),
            new PrivateCapitalReportOutputDto(
                "report-output:partial-report-evidence:ready-unlinked",
                "DistributionNotice",
                "Ready distribution notice without retained evidence",
                "/api/fund-structure/report-packs/ready-unlinked",
                fundEventId,
                fundEvent.FundEventType,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                fundEvent.JournalStatus,
                effectiveDate,
                "USD",
                100m,
                1,
                [],
                IsReportReady: true,
                [])
        };

        var fundEventCategories = PrivateCapitalEvidenceCategoryBuilder.BuildForFundEvent(
            fundEvent,
            [],
            [],
            reportOutputs,
            "/api/approvals/partial-report-evidence");
        var subledgerCategories = PrivateCapitalEvidenceCategoryBuilder.BuildForCapitalAccountSubledger(
            [],
            [],
            [],
            reportOutputs);

        fundEventCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            !item.IsReady &&
            item.EvidenceLinkCount == 1);
        subledgerCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            !item.IsReady &&
            item.EvidenceLinkCount == 1);
    }

    [Fact]
    public void PrivateCapitalPaymentIntentEvidence_FailsClosedWhenCashEvidenceIsMissing()
    {
        var record = BuildPrivateCapitalFundEventLedgerRecord(FindPrivateCapitalFundEventLedgerReadinessCase("approval-pending"));

        record.PaymentIntentEvidence.Should().NotBeNull();
        record.PaymentIntentEvidence!.Status.Should().Be(PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing);
        record.PaymentIntentEvidence.IsReady.Should().BeFalse();
        record.PaymentIntentEvidence.RequiredEvidence.Should().Contain("Retained bank, cash, or settlement evidence");
        record.EvidenceCategories.Should().Contain(item =>
            item.CategoryId == "payment-intent" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("payment:fund-alpha:capital-call"));
        record.EvidenceCategories.Should().Contain(item =>
            item.CategoryId == "cash-evidence" &&
            !item.IsReady &&
            item.EvidenceLinkCount == 0);
    }

    [Fact]
    public void PrivateCapitalPaymentIntentEvidence_RetainsCashEvidenceWithoutEnablingExecution()
    {
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var effectiveDate = new DateOnly(2026, 6, 30);
        var journalEntryId = Guid.Parse("23232323-2323-2323-2323-232323232323");
        const string fundEventId = "fund-event:fund-alpha:capital-call:cash-evidence";
        const string cashEvidenceRoute = "/api/workstation/evidence/subjects/cash-evidence/payment%3Afund-alpha%3Acapital-call%3Acash-evidence/packet";
        var fundEvent = new PrivateCapitalFundEventDto(
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            journalEntryId,
            effectiveDate,
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            100m,
            100m,
            "Fund Alpha capital call with bank evidence",
            "payment:fund-alpha:capital-call:cash-evidence",
            "settlement:fund-alpha:capital-call:cash-evidence",
            [cashEvidenceRoute],
            [],
            timestamp,
            ApprovalId: "approval:cash-evidence");

        var record = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            "fund-alpha",
            [fundEvent],
            [],
            [],
            []).Should().ContainSingle().Subject;

        record.PaymentIntentEvidence.Should().NotBeNull();
        record.PaymentIntentEvidence!.Status.Should().Be(PrivateCapitalPaymentIntentEvidenceStatusDto.SettlementMatched);
        record.PaymentIntentEvidence.IsReady.Should().BeTrue();
        record.PaymentIntentEvidence.Direction.Should().Be(PaymentIntentCashDirectionDto.Inflow);
        record.PaymentIntentEvidence.CashEvidenceLinks.Should().ContainSingle(cashEvidenceRoute);
        record.PaymentIntentEvidence.Summary.Should().Contain("live execution remains deferred");
        record.EvidenceCategories.Should().Contain(item =>
            item.CategoryId == "cash-evidence" &&
            item.IsReady &&
            item.EvidenceLinks.Contains(cashEvidenceRoute));
    }

    [Fact]
    public void PrivateCapitalPaymentIntentEvidence_RejectsExplicitUnrelatedCashEvidenceLink()
    {
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var effectiveDate = new DateOnly(2026, 6, 30);
        var journalEntryId = Guid.Parse("24242424-2424-2424-2424-242424242424");
        const string unrelatedCashEvidenceRoute = "/api/workstation/evidence/subjects/cash-evidence/payment%3Afund-alpha%3Adistribution%3Aunrelated/packet";
        var fundEvent = new PrivateCapitalFundEventDto(
            "fund-event:fund-alpha:capital-call:unrelated-cash-evidence",
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            journalEntryId,
            effectiveDate,
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            100m,
            100m,
            "Fund Alpha capital call with unrelated cash evidence",
            "payment:fund-alpha:capital-call:unrelated-cash-evidence",
            "settlement:fund-alpha:capital-call:unrelated-cash-evidence",
            [unrelatedCashEvidenceRoute],
            [],
            timestamp,
            ApprovalId: "approval:unrelated-cash-evidence");

        var record = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            "fund-alpha",
            [fundEvent],
            [],
            [],
            []).Should().ContainSingle().Subject;

        record.PaymentIntentEvidence.Should().NotBeNull();
        record.PaymentIntentEvidence!.Status.Should().Be(PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing);
        record.PaymentIntentEvidence.IsReady.Should().BeFalse();
        record.PaymentIntentEvidence.CashEvidenceLinks.Should().BeEmpty();
        record.PaymentIntentEvidence.RequiredEvidence.Should().Contain("Retained bank, cash, or settlement evidence");
        record.EvidenceCategories.Should().Contain(item =>
            item.CategoryId == "cash-evidence" &&
            !item.IsReady &&
            item.EvidenceLinkCount == 0);
    }

    [Fact]
    public void PrivateCapitalPaymentIntentEvidence_RetainsReturnEvidenceWithoutEnablingExecution()
    {
        var timestamp = new DateTimeOffset(2026, 7, 3, 17, 0, 0, TimeSpan.Zero);
        var effectiveDate = new DateOnly(2026, 7, 3);
        var journalEntryId = Guid.Parse("25252525-2525-2525-2525-252525252525");
        const string returnEvidenceRoute = "/api/workstation/evidence/subjects/payment-return/payment%3Afund-alpha%3Acapital-call%3Areturned/packet";
        var fundEvent = new PrivateCapitalFundEventDto(
            "fund-event:fund-alpha:capital-call:return-evidence",
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            journalEntryId,
            effectiveDate,
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            100m,
            100m,
            "Fund Alpha capital call with retained bank return evidence",
            "payment:fund-alpha:capital-call:returned",
            "settlement:fund-alpha:capital-call:returned",
            [returnEvidenceRoute],
            [],
            timestamp,
            ApprovalId: "approval:return-evidence");

        var record = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            "fund-alpha",
            [fundEvent],
            [],
            [],
            []).Should().ContainSingle().Subject;

        record.PaymentIntentEvidence.Should().NotBeNull();
        record.PaymentIntentEvidence!.Status.Should().Be(PrivateCapitalPaymentIntentEvidenceStatusDto.SettlementMatched);
        record.PaymentIntentEvidence.IsReady.Should().BeTrue();
        record.PaymentIntentEvidence.CashEvidenceLinks.Should().ContainSingle(returnEvidenceRoute);
        record.PaymentIntentEvidence.Summary.Should().Contain("live execution remains deferred");
    }

    [Fact]
    public void PrivateCapitalCapitalAccountSubledger_DerivesReadinessFromReportOutputEvidenceLane()
    {
        var record = BuildPrivateCapitalFundEventLedgerRecord(new PrivateCapitalFundEventLedgerReadinessCase(
            Suffix: "subledger-report-evidence-missing",
            ApprovalState: ManualJournalEntryStatusDto.Submitted,
            IncludeFundEventEvidence: true,
            IncludeLedgerImpact: true,
            LedgerPostingReady: true,
            IncludeReportOutput: true,
            ReportReady: true,
            ReportPublished: false,
            HasCriticalIssue: false,
            ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.Ready,
            ExpectedLabel: "Ready",
            ExpectedNextAction: "Review report output",
            ExpectedNextActionRouteFragment: "/api/fund-structure/report-packs/subledger-report-evidence-missing",
            ExpectedEvidenceCount: 1,
            ExpectedValidationIssueCount: 0));
        var capitalAccount = new PrivateCapitalCapitalAccountActivityDto(
            record.CapitalAccountId,
            record.InvestorId,
            record.Currency,
            Contributions: 100m,
            Distributions: 0m,
            Subscriptions: 0m,
            Redemptions: 0m,
            ManagementFees: 0m,
            NetActivity: 100m,
            FundEventCount: 1,
            LastEffectiveDate: record.EffectiveDate,
            LastFundEventType: record.FundEventType,
            FundEventIds: [record.FundEventId]);

        var subledger = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            "fund-alpha",
            ledgerBookId: null,
            new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero),
            [capitalAccount],
            [record],
            record.CapitalAccountSubledgerEntries,
            record.LedgerImpacts,
            record.ReportOutputs,
            []).Should().ContainSingle().Subject;

        subledger.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.ReportReview);
        subledger.ReadinessLabel.Should().Be("Report review");
        subledger.ReadinessReason.Should().Contain("report-output evidence is not complete");
        subledger.NextAction.Should().Be("Prepare report output");
        subledger.NextActionRoute.Should().Contain("/api/fund-structure/report-packs/subledger-report-evidence-missing");
        subledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            !item.IsReady &&
            item.EvidenceLinkCount == 0);
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
    public async Task Scenario_ManualJournalEntry_ReviewedAutomationCannotSubmitApproval()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry();

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        var act = async () => await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "assistant",
            saved.Version,
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reviewed automation cannot submit manual journal entries for approval*");
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.Drafts.Should().ContainSingle(item =>
            item.JournalEntryId == saved.JournalEntryId &&
            item.Status == ManualJournalEntryStatusDto.Draft);
        workbench.AuditTrail.Select(item => item.Action).Should().NotContain("manual-je.submit-approval");
    }

    [Fact]
    public async Task ManualJournalEntryWorkbenchService_ListFundProfileIds_ReturnsRetainedDraftScopes()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);

        await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry() with { FundProfileId = "fund-beta" },
            "ops-user"));

        var fundProfileIds = await service.ListFundProfileIdsAsync();

        fundProfileIds.Should().ContainInOrder("fund-alpha", "fund-beta");
    }

    [Fact]
    public async Task ManualJournalEntryWorkbenchService_ListFundProfileIds_IncludesLedgerBookScopesWithoutDrafts()
    {
        var configuration = CreateService();
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var periodId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var journalStore = new PostedPrivateCapitalLedgerJournalStore(
            new LedgerBookRecord(
                ledgerBookId,
                "fund-beta",
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                FundStructureNodeKindDto.Fund,
                "Fund Beta GAAP",
                "USD",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                AccountingBasis: AccountingBasisKindDto.Gaap),
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                FiscalYear: 2026,
                PeriodNo: 6,
                Label: "2026-06",
                StartDate: new DateOnly(2026, 6, 1),
                EndDate: new DateOnly(2026, 6, 30),
                Status: "Open",
                OpenedAt: DateTimeOffset.UtcNow,
                ClosedAt: null,
                Version: 1));
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore);

        var fundProfileIds = await service.ListFundProfileIdsAsync();

        fundProfileIds.Should().ContainSingle("fund-beta");
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
            item.IsReportReady &&
            item.ReadinessLabel == "Ready" &&
            item.ReadinessReason == "The report output has retained evidence and linked posting-ready fund-event impact." &&
            item.NextAction == "Review report output" &&
            item.NextActionRoute == item.ReportOutputRoute);
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
            item.PrimaryReportRoute.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase) &&
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
        var paymentIntent = workbench.PrivateCapitalActivity.PaymentIntents.Should().ContainSingle().Subject;
        paymentIntent.PaymentIntentId.Should().Be("payment:fund-alpha:capital-call:20260630");
        paymentIntent.SettlementReference.Should().Be("settlement:fund-alpha:capital-call:20260630");
        paymentIntent.Requester.Should().Be("ops-user");
        paymentIntent.Status.Should().Be(PaymentIntentWorkflowStatusDto.ApprovalPending);
        paymentIntent.StatusLabel.Should().Be("Approval pending");
        paymentIntent.ExpectedCashMovement.Direction.Should().Be(PaymentIntentCashDirectionDto.Inflow);
        paymentIntent.ExpectedCashMovement.Amount.Should().Be(100m);
        paymentIntent.ExpectedCashMovement.Currency.Should().Be("USD");
        paymentIntent.ExpectedCashMovement.Payee.Should().Be("fund:fund-alpha");
        paymentIntent.ExpectedCashMovement.AccountScope.Should().Contain("capital-account:fund-alpha:lp-1");
        paymentIntent.ExpectedCashMovement.BusinessPurpose.Should().Be("Capital call for Fund Alpha LP");
        paymentIntent.ExpectedCashMovement.ApprovalPolicy.Should().Be("Controller approval pending before execution-deferred reliance");
        paymentIntent.ExpectedCashMovement.SourceEvidenceLinks.Should().Contain("/api/workstation/evidence/subjects/accounting-record/manual-je");
        paymentIntent.ApprovalChain.Should().Contain(step =>
            step.Role == "Controller approval" &&
            step.Status == ManualJournalEntryStatusDto.Submitted.ToString());
        paymentIntent.BankEvidence.Should().ContainSingle(item =>
            item.EvidenceKind == "BankConfirmation" &&
            item.Status == "Missing");
        paymentIntent.ReconciliationLinks.Should().ContainSingle(item => item.Status == "Pending");
        paymentIntent.AuditHistory.Should().Contain(item => item.Action == "payment-intent.execution-deferred");
        paymentIntent.ExecutionDeferredReason.Should().Contain("Full payment execution is explicitly deferred");
        paymentIntent.EvidenceRoute.Contains(
            "/api/workstation/evidence/subjects/payment-intent/payment%3Afund-alpha%3Acapital-call%3A20260630/packet",
            StringComparison.OrdinalIgnoreCase).Should().BeTrue();
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
        directActivity.PaymentIntents.Should().ContainSingle(item =>
            item.PaymentIntentId == "payment:fund-alpha:capital-call:20260630" &&
            item.Status == PaymentIntentWorkflowStatusDto.ApprovalPending);
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
    public async Task Scenario_ManualJournalEntry_PaymentIntentWorkflowCapturesBankConfirmationAndReconciliationWithoutExecution()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var journalEntryId = Guid.Parse("19191919-1919-1919-1919-191919191919");
        const string paymentIntentId = "payment:fund-alpha:capital-call:bank-confirmed";
        const string settlementReference = "settlement:fund-alpha:capital-call:bank-confirmed";
        var bankSource = new StaticBankTransactionSource(
            new BankTransactionDto(
                Guid.Parse("29292929-2929-2929-2929-292929292929"),
                journalEntryId,
                "ApprovedPayment",
                new DateOnly(2026, 6, 30),
                new DateOnly(2026, 6, 30),
                new DateOnly(2026, 7, 2),
                100m,
                "USD",
                paymentIntentId,
                new DateTimeOffset(2026, 7, 2, 13, 0, 0, TimeSpan.Zero),
                IsVoided: false,
                RecordedBy: "cash-ops@example.com"));
        var service = CreateManualJournalEntryWorkbenchService(configuration, bankTransactionSource: bankSource);
        var draft = BalancedManualJournalEntry() with
        {
            JournalEntryId = journalEntryId,
            EntryType = ManualJournalEntryTypeDto.CapitalCall,
            Memo = "Capital call with bank confirmation",
            Lines =
            [
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash"),
                new ManualJournalEntryLineDto("credit-capital", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Equity:Capital Contributions")
            ],
            EvidenceLinks =
            [
                "/api/reconciliation/runs/run:capital-call-bank-confirmed",
                "/api/workstation/evidence/subjects/cash-evidence/payment%3Afund-alpha%3Acapital-call%3Abank-confirmed/packet"
            ],
            TreasuryContext = new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "manual-je:fund-alpha:capital-call:bank-confirmed",
                FundEventId: "fund-event:fund-alpha:capital-call:bank-confirmed",
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                PaymentIntentId: paymentIntentId,
                SettlementReference: settlementReference)
        };

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version));
        var workbench = await service.GetWorkbenchAsync("fund-alpha");

        var paymentIntent = workbench.PrivateCapitalActivity!.PaymentIntents.Should().ContainSingle().Subject;
        paymentIntent.PaymentIntentId.Should().Be(paymentIntentId);
        paymentIntent.Status.Should().Be(PaymentIntentWorkflowStatusDto.ApprovalPending);
        paymentIntent.BankEvidence.Should().Contain(item =>
            item.EvidenceKind == "BankConfirmation" &&
            item.Status == "Confirmed" &&
            item.BankTransactionId == Guid.Parse("29292929-2929-2929-2929-292929292929") &&
            item.ExternalRef == paymentIntentId &&
            item.RecordedBy == "cash-ops@example.com");
        paymentIntent.ExpectedCashMovement.SourceEvidenceLinks.Should().Contain(link =>
            link.Contains("cash-evidence", StringComparison.OrdinalIgnoreCase));
        paymentIntent.ExpectedCashMovement.Payee.Should().Be("fund:fund-alpha");
        paymentIntent.ExpectedCashMovement.ApprovalPolicy.Should().Be("Controller approval pending before execution-deferred reliance");
        paymentIntent.ReconciliationLinks.Should().Contain(item =>
            item.Status == "Ready" &&
            item.EvidenceRoute!.Contains("reconciliation", StringComparison.OrdinalIgnoreCase));
        paymentIntent.AuditHistory.Select(item => item.Action).Should().Contain("payment-intent.execution-deferred");
        paymentIntent.ExecutionDeferredReason.Should().Contain("before any bank-side instruction");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_PaymentIntentWorkflowRejectsUnrelatedExplicitCashEvidence()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var journalEntryId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        const string paymentIntentId = "payment:fund-alpha:capital-call:scoped-evidence";
        const string settlementReference = "settlement:fund-alpha:capital-call:scoped-evidence";
        const string unrelatedCashEvidenceRoute = "/api/workstation/evidence/subjects/cash-evidence/payment%3Afund-alpha%3Adistribution%3Aunrelated/packet";
        const string unrelatedReconciliationRoute = "/api/reconciliation/runs/payment%3Afund-alpha%3Adistribution%3Aunrelated/run:unrelated";
        var draft = BalancedManualJournalEntry() with
        {
            JournalEntryId = journalEntryId,
            EntryType = ManualJournalEntryTypeDto.CapitalCall,
            Memo = "Capital call with unrelated payment evidence",
            Lines =
            [
                new ManualJournalEntryLineDto("debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash"),
                new ManualJournalEntryLineDto("credit-capital", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Equity:Capital Contributions")
            ],
            EvidenceLinks =
            [
                "/api/source-documents/capital-call/scoped-evidence",
                unrelatedCashEvidenceRoute,
                unrelatedReconciliationRoute
            ],
            TreasuryContext = new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "manual-je:fund-alpha:capital-call:scoped-evidence",
                FundEventId: "fund-event:fund-alpha:capital-call:scoped-evidence",
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                PaymentIntentId: paymentIntentId,
                SettlementReference: settlementReference)
        };

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version));
        var workbench = await service.GetWorkbenchAsync("fund-alpha");

        var paymentIntent = workbench.PrivateCapitalActivity!.PaymentIntents.Should().ContainSingle().Subject;
        paymentIntent.PaymentIntentId.Should().Be(paymentIntentId);
        paymentIntent.BankEvidence.Should().ContainSingle(item =>
            item.EvidenceKind == "BankConfirmation" &&
            item.Status == "Missing");
        paymentIntent.BankEvidence.Should().NotContain(item =>
            string.Equals(item.EvidenceRoute, unrelatedCashEvidenceRoute, StringComparison.OrdinalIgnoreCase));
        paymentIntent.ReconciliationLinks.Should().ContainSingle(item => item.Status == "Pending");
        paymentIntent.ReconciliationLinks.Should().NotContain(item =>
            string.Equals(item.EvidenceRoute, unrelatedReconciliationRoute, StringComparison.OrdinalIgnoreCase));
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
        var reportOutput = activity.ReportOutputs.Single();
        reportOutput.ReportOutputRoute.Should().NotBeNull();
        reportOutput.ReportOutputRoute!.Should().Contain("/api/ledger/private-capital/report-output");
        reportOutput.ReportOutputRoute.Should().Contain($"ledgerBookId={ledgerBookId:D}");
        reportOutput.ReportOutputRoute.Should().Contain("reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3Aposted");
        reportOutput.FundEventRecordRoute.Should().NotBeNull();
        reportOutput.FundEventRecordRoute!.Should().Contain("/api/ledger/private-capital/fund-event-record");
        reportOutput.FundEventRecordRoute.Should().Contain($"ledgerBookId={ledgerBookId:D}");
        reportOutput.FundEventRecordRoute.Should().Contain("fundEventId=fund-event%3Afund-alpha%3Acapital-call%3Aposted");
        reportOutput.CapitalAccountSubledgerRoute.Should().NotBeNull();
        reportOutput.CapitalAccountSubledgerRoute!.Should().Contain("/api/ledger/private-capital/capital-account-subledger");
        reportOutput.CapitalAccountSubledgerRoute.Should().Contain($"ledgerBookId={ledgerBookId:D}");
        reportOutput.CapitalAccountSubledgerRoute.Should().Contain("capitalAccountId=capital-account%3Afund-alpha%3Alp-1");
        reportOutput.CapitalAccountSubledgerRoute.Should().Contain("currency=EUR");
        reportOutput.EvidenceRoute.Should().NotBeNull();
        reportOutput.EvidenceRoute!.Should().Match(
            "*/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3Aposted/packet*");
        reportOutput.ApprovalRoute.Should().NotBeNull();
        reportOutput.ApprovalRoute!.Should().Match("*approvalId=approval%3Acapital-call-controller*");
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
            item.PrimaryReportRoute != null &&
            item.PrimaryReportRoute.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase) &&
            item.PrimaryReportRoute.Contains($"reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3Aposted%3A{reportPackId:D}", StringComparison.OrdinalIgnoreCase) &&
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
        var fundEventRecord = activity.FundEventRecords.Single();
        fundEventRecord.EvidenceCategories.Should().HaveCount(7);
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "source-support" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "capital-account-subledger" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "ledger-impact" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "approval-state" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("approval:capital-call-controller") &&
            item.EvidenceLinks.Any(link => link.Contains("approvalId=approval%3Acapital-call-controller", StringComparison.OrdinalIgnoreCase)));
        fundEventRecord.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/report-packs/capital-call-1"));
        activity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.Currency == "EUR");
        var capitalAccountSubledger = activity.CapitalAccountSubledgers.Single();
        capitalAccountSubledger.SubledgerId.Should().Be("capital-account-subledger:capital-account:fund-alpha:lp-1:investor:lp-1:eur");
        capitalAccountSubledger.FundProfileId.Should().Be("fund-alpha");
        capitalAccountSubledger.LedgerBookId.Should().Be(ledgerBookId);
        capitalAccountSubledger.ActivityRoute.Should().Contain("/api/ledger/private-capital/capital-account-subledger");
        capitalAccountSubledger.ActivityRoute.Should().Contain($"ledgerBookId={ledgerBookId:D}");
        capitalAccountSubledger.ActivityRoute.Should().Contain("capitalAccountId=capital-account%3Afund-alpha%3Alp-1");
        capitalAccountSubledger.ActivityRoute.Should().Contain("investorId=investor%3Alp-1");
        capitalAccountSubledger.ActivityRoute.Should().Contain("currency=EUR");
        capitalAccountSubledger.Contributions.Should().Be(250000m);
        capitalAccountSubledger.Distributions.Should().Be(0m);
        capitalAccountSubledger.OpeningNetActivity.Should().Be(0m);
        capitalAccountSubledger.EndingNetActivity.Should().Be(250000m);
        capitalAccountSubledger.NetCapitalActivity.Should().Be(250000m);
        capitalAccountSubledger.FundEventCount.Should().Be(1);
        capitalAccountSubledger.ApprovalQueueCount.Should().Be(0);
        capitalAccountSubledger.PostedFundEventCount.Should().Be(1);
        capitalAccountSubledger.PublishedReportOutputCount.Should().Be(1);
        capitalAccountSubledger.EvidenceLinkCount.Should().Be(3);
        capitalAccountSubledger.ValidationIssueCount.Should().Be(0);
        capitalAccountSubledger.FirstEffectiveDate.Should().Be(new DateOnly(2026, 6, 30));
        capitalAccountSubledger.LastEffectiveDate.Should().Be(new DateOnly(2026, 6, 30));
        capitalAccountSubledger.LastFundEventType.Should().Be("CapitalCall");
        capitalAccountSubledger.EvidenceLinks.Should().Contain("/api/workstation/evidence/subjects/private-capital/capital-call-source");
        capitalAccountSubledger.EvidenceLinks.Should().Contain("/api/workstation/evidence/report-packs/capital-call-1");
        capitalAccountSubledger.EvidenceLinks.Should().Contain("ledger-evidence-1");
        capitalAccountSubledger.CapitalAccount.Should().NotBeNull();
        capitalAccountSubledger.CapitalAccount!.Contributions.Should().Be(250000m);
        capitalAccountSubledger.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.Currency == "EUR" &&
            item.IsPosted &&
            item.IsPublished &&
            item.ApprovalId == "approval:capital-call-controller");
        capitalAccountSubledger.SubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.Currency == "EUR" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            item.IsPosted &&
            item.NetCapitalActivity == 250000m &&
            item.RunningNetActivity == 250000m);
        capitalAccountSubledger.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.Currency == "EUR" &&
            item.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            item.IsPostingReady &&
            item.LineCount == 2);
        capitalAccountSubledger.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.ReportOutputType == "GovernedReportPack" &&
            item.Currency == "EUR" &&
            item.IsPublished &&
            item.ReportPackId == reportPackId.ToString("D") &&
            item.ReportWorkflowState == ReportPackWorkflowStateDto.Published.ToString());
        capitalAccountSubledger.ValidationIssues.Should().BeEmpty();
        capitalAccountSubledger.EvidenceCategories.Should().HaveCount(7);
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "source-support" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "capital-account-subledger" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "ledger-impact" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/subjects/private-capital/capital-call-source"));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "approval-state" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("approval:capital-call-controller") &&
            item.EvidenceLinks.Any(link => link.Contains("approvalId=approval%3Acapital-call-controller", StringComparison.OrdinalIgnoreCase)));
        capitalAccountSubledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/api/workstation/evidence/report-packs/capital-call-1"));
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
    public async Task Scenario_PrivateCapitalActivityProjection_DoesNotMarkPublishedReportReadyWhenReportEvidenceMissing()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.NewGuid();
        var cashLedgerEntryId = Guid.NewGuid();
        var capitalLedgerEntryId = Guid.NewGuid();
        var reportPackId = Guid.NewGuid();
        const string fundEventId = "fund-event:fund-alpha:capital-call:missing-report-evidence";
        var encodedFundEventId = Uri.EscapeDataString(fundEventId);
        const string sourceEvidence = "source:missing-report-evidence-capital-call";
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            "Report-evidence-missing Fund Alpha capital call",
            [
                new LedgerEntry(
                    cashLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    175000m,
                    0m,
                    "Report-evidence-missing Fund Alpha capital call"),
                new LedgerEntry(
                    capitalLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Capital Contributions", LedgerAccountType.Equity, FinancialAccountId: "capital-account:fund-alpha:lp-1"),
                    0m,
                    175000m,
                    "Report-evidence-missing Fund Alpha capital call")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "posted:fund-alpha:capital-call:missing-report-evidence",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = sourceEvidence,
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:missing-report-evidence-capital-call",
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
                LineProvenance: [],
                Publication: new ReportPackPublicationManifestDto(
                    $"manifest:{fundEventId}",
                    $"/retained/report-packs/{encodedFundEventId}.json",
                    "sha256:missing-report-evidence",
                    "controller",
                    timestamp,
                    []))));
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore, workflowService);

        var activity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ledgerBookId);

        activity.PublishedReportOutputCount.Should().Be(1);
        var reportOutput = activity.ReportOutputs.Should().ContainSingle(item => item.FundEventId == fundEventId).Subject;
        reportOutput.IsPublished.Should().BeTrue();
        reportOutput.IsReportReady.Should().BeFalse();
        reportOutput.ReadinessLabel.Should().Be("Evidence missing");
        reportOutput.ReadinessReason.Should().Be("Posted private-capital report output is missing retained report evidence links.");
        reportOutput.NextAction.Should().Be("Attach retained evidence");
        reportOutput.NextActionRoute.Should().Be(reportOutput.EvidenceRoute);
        reportOutput.ReportLineProvenanceCount.Should().Be(0);
        reportOutput.EvidenceLinks.Should().Contain(sourceEvidence);
        reportOutput.ValidationIssues.Should().Contain(issue =>
            issue.Code == "private-capital.report-output-evidence-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Warning);

        var record = activity.FundEventRecords.Should().ContainSingle(item => item.FundEventId == fundEventId).Subject;
        record.IsPostingReady.Should().BeTrue();
        record.IsPublished.Should().BeTrue();
        record.IsReportReady.Should().BeFalse();
        record.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.ReportReview);
        record.ReadinessLabel.Should().Be("Report review");
        record.ReportOutputs.Single().ValidationIssues.Should().Contain(issue =>
            issue.Code == "private-capital.report-output-evidence-missing");
        record.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            !item.IsReady &&
            item.EvidenceLinks.Contains(sourceEvidence));

        var subledger = activity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1").Subject;
        subledger.PublishedReportOutputCount.Should().Be(1);
        subledger.EvidenceCategories.Should().ContainSingle(item =>
            item.CategoryId == "report-output" &&
            !item.IsReady &&
            item.EvidenceLinks.Contains(sourceEvidence));
    }

    [Fact]
    public async Task Scenario_PrivateCapitalActivityProjection_MatchesPublishedReportOutputByRetainedFundEventEvidence()
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
        const string fundEventId = "fund-event:fund-alpha:capital-call:evidence-linked";
        var encodedFundEventId = Uri.EscapeDataString(fundEventId);
        var eventEvidenceRoute = $"/api/workstation/evidence/subjects/private-capital-fund-event/{encodedFundEventId}/packet";
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            "Evidence-linked Fund Alpha capital call",
            [
                new LedgerEntry(
                    cashLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    125000m,
                    0m,
                    "Evidence-linked Fund Alpha capital call"),
                new LedgerEntry(
                    capitalLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Capital Contributions", LedgerAccountType.Equity, FinancialAccountId: "capital-account:fund-alpha:lp-1"),
                    0m,
                    125000m,
                    "Evidence-linked Fund Alpha capital call")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "posted:fund-alpha:capital-call:evidence-linked",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "source:evidence-linked-capital-call",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:evidence-linked-capital-call",
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
                LineProvenance: [],
                Publication: new ReportPackPublicationManifestDto(
                    $"manifest:{fundEventId}",
                    $"/retained/report-packs/{encodedFundEventId}.json",
                    "sha256:evidence-linked-capital-call",
                    "controller",
                    timestamp,
                    [new ReportPackEvidenceLinkDto("publication-evidence-linked", "Private-capital fund-event packet", eventEvidenceRoute, "EvidenceVault", timestamp)]))));
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore, workflowService);

        var activity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ledgerBookId);

        activity.PublishedReportOutputCount.Should().Be(1);
        activity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.IsPublished &&
            item.IsReportReady &&
            item.ReportPackId == reportPackId.ToString("D") &&
            item.ReportLineProvenanceCount == 0 &&
            item.EvidenceLinks.Contains(eventEvidenceRoute) &&
            !item.ValidationIssues.Any(issue => issue.Code == "private-capital.report-output-missing"));
        activity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.IsPostingReady &&
            item.IsReportReady &&
            item.IsPublished &&
            item.ReportOutputCount == 1 &&
            item.PrimaryReportOutputId == $"report-output:{fundEventId}:{reportPackId:D}".ToLowerInvariant() &&
            item.ReportLineProvenanceCount == 0 &&
            item.EvidenceLinks.Contains(eventEvidenceRoute) &&
            item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Published);
    }

    [Fact]
    public async Task Scenario_PrivateCapitalActivityProjection_PreservesPostedCapitalAccountImpactIdentities()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        const string fundEventId = "fund-event:fund-alpha:capital-call:shared-posted";
        var lp1JournalEntryId = Guid.NewGuid();
        var lp1CashLedgerEntryId = Guid.NewGuid();
        var lp1CapitalLedgerEntryId = Guid.NewGuid();
        var lp2JournalEntryId = Guid.NewGuid();
        var lp2CashLedgerEntryId = Guid.NewGuid();
        var lp2CapitalLedgerEntryId = Guid.NewGuid();
        var reportPackId = Guid.NewGuid();
        var lp1Journal = new JournalEntry(
            lp1JournalEntryId,
            timestamp,
            "Posted Fund Alpha shared capital call - LP 1",
            [
                new LedgerEntry(
                    lp1CashLedgerEntryId,
                    lp1JournalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    100000m,
                    0m,
                    "Posted Fund Alpha shared capital call - LP 1"),
                new LedgerEntry(
                    lp1CapitalLedgerEntryId,
                    lp1JournalEntryId,
                    timestamp,
                    new LedgerAccount("Capital Contributions", LedgerAccountType.Equity, FinancialAccountId: "capital-account:fund-alpha:lp-1"),
                    0m,
                    100000m,
                    "Posted Fund Alpha shared capital call - LP 1")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "posted:fund-alpha:shared-capital-call:lp-1",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-1",
                InvestorId: "investor:lp-1",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "source:shared-capital-call-lp-1",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:shared-capital-call",
                    ["approvedBy"] = "controller"
                }));
        var lp2Journal = new JournalEntry(
            lp2JournalEntryId,
            timestamp.AddMinutes(5),
            "Posted Fund Alpha shared capital call - LP 2",
            [
                new LedgerEntry(
                    lp2CashLedgerEntryId,
                    lp2JournalEntryId,
                    timestamp.AddMinutes(5),
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    200000m,
                    0m,
                    "Posted Fund Alpha shared capital call - LP 2"),
                new LedgerEntry(
                    lp2CapitalLedgerEntryId,
                    lp2JournalEntryId,
                    timestamp.AddMinutes(5),
                    new LedgerAccount("Capital Contributions", LedgerAccountType.Equity, FinancialAccountId: "capital-account:fund-alpha:lp-2"),
                    0m,
                    200000m,
                    "Posted Fund Alpha shared capital call - LP 2")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                IdempotencyKey: "posted:fund-alpha:shared-capital-call:lp-2",
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: "capital-account:fund-alpha:lp-2",
                InvestorId: "investor:lp-2",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidenceLinks"] = "source:shared-capital-call-lp-2",
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = "approval:shared-capital-call",
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
                lp1Journal,
                Guid.NewGuid(),
                periodId,
                CommandId: null,
                CorrelationId: null,
                GlobalSequence: 1,
                CreatedAt: timestamp),
            new LedgerJournalEntryRecord(
                lp2Journal,
                Guid.NewGuid(),
                periodId,
                CommandId: null,
                CorrelationId: null,
                GlobalSequence: 2,
                CreatedAt: timestamp.AddMinutes(5)));
        var workflowService = new ReportPackWorkflowService(new StaticReportPackWorkflowRecordStore(
            new ReportPackWorkflowRecordDto(
                reportPackId,
                "fund-alpha",
                "capital-account:fund-alpha:lp-2",
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
                        "capital-account.contribution.lp-2",
                        "ledger",
                        fundEventId,
                        "ledger-evidence-lp-2",
                        LedgerEntryId: lp2CapitalLedgerEntryId.ToString("D"),
                        ReportValue: "200000",
                        ApprovalId: "approval:shared-capital-call")
                ],
                Publication: new ReportPackPublicationManifestDto(
                    "manifest-shared-capital-call-lp-2",
                    "/retained/report-packs/shared-capital-call-lp-2.json",
                    "sha256:shared-capital-call-lp-2",
                    "controller",
                    timestamp,
                    [new ReportPackEvidenceLinkDto("publication-evidence-lp-2", "Publication manifest", "/api/workstation/evidence/report-packs/shared-capital-call-lp-2", "EvidenceVault", timestamp)]))));
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore, workflowService);

        var activity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ledgerBookId);

        activity.PublishedReportOutputCount.Should().Be(1);
        activity.FundEvents.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.NetCapitalActivity == 300000m &&
            item.ValidationIssues.Any(issue => issue.Code == "private-capital.capital-account-conflict") &&
            item.ValidationIssues.Any(issue => issue.Code == "private-capital.investor-conflict"));
        activity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.NetActivity == 100000m);
        activity.CapitalAccounts.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.NetActivity == 200000m);
        activity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.NetCapitalActivity == 100000m &&
            item.RunningNetActivity == 100000m &&
            item.IsPosted);
        activity.CapitalAccountSubledgerEntries.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.NetCapitalActivity == 200000m &&
            item.RunningNetActivity == 200000m &&
            item.IsPosted);
        activity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.JournalEntryId == lp1JournalEntryId);
        activity.LedgerImpacts.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.JournalEntryId == lp2JournalEntryId);
        activity.ReportOutputs.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            item.ReportOutputType == "GovernedReportPack" &&
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.NetCapitalActivity == 200000m &&
            item.IsPublished &&
            !item.IsReportReady &&
            item.ReportLineProvenanceCount == 1 &&
            item.ValidationIssues.Any(issue => issue.Code == "private-capital.report-output-posting-not-ready"));
        activity.FundEventRecords.Should().ContainSingle(item =>
            item.FundEventId == fundEventId &&
            !item.IsPostingReady &&
            item.NetCapitalActivity == 300000m &&
            item.CapitalAccountOpeningNetActivity == 0m &&
            item.CapitalAccountEndingNetActivity == 300000m &&
            item.CapitalAccountSubledgerEntryCount == 2 &&
            item.ReportOutputCount == 1 &&
            item.CapitalAccountSubledgerEntries.Any(entry => entry.CapitalAccountId == "capital-account:fund-alpha:lp-1") &&
            item.CapitalAccountSubledgerEntries.Any(entry => entry.CapitalAccountId == "capital-account:fund-alpha:lp-2"));
        activity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-1" &&
            item.InvestorId == "investor:lp-1" &&
            item.NetCapitalActivity == 100000m &&
            item.SubledgerEntries.Count == 1 &&
            item.FundEventRecords.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 0);
        activity.CapitalAccountSubledgers.Should().ContainSingle(item =>
            item.CapitalAccountId == "capital-account:fund-alpha:lp-2" &&
            item.InvestorId == "investor:lp-2" &&
            item.NetCapitalActivity == 200000m &&
            item.SubledgerEntries.Count == 1 &&
            item.FundEventRecords.Count == 1 &&
            item.LedgerImpacts.Count == 1 &&
            item.ReportOutputs.Count == 1);
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
                ExpectedValidationIssueCount: 0),
            new(
                Suffix: "ready-report-output",
                ApprovalState: ManualJournalEntryStatusDto.Submitted,
                IncludeFundEventEvidence: true,
                IncludeLedgerImpact: true,
                LedgerPostingReady: true,
                IncludeReportOutput: true,
                ReportReady: true,
                ReportPublished: false,
                HasCriticalIssue: false,
                ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.Ready,
                ExpectedLabel: "Ready",
                ExpectedNextAction: "Review report output",
                ExpectedNextActionRouteFragment: "/api/fund-structure/report-packs/ready-report-output",
                ExpectedEvidenceCount: 1,
                ExpectedValidationIssueCount: 0),
            new(
                Suffix: "published-report-output",
                ApprovalState: ManualJournalEntryStatusDto.Submitted,
                IncludeFundEventEvidence: true,
                IncludeLedgerImpact: true,
                LedgerPostingReady: true,
                IncludeReportOutput: true,
                ReportReady: true,
                ReportPublished: true,
                HasCriticalIssue: false,
                ExpectedReadiness: PrivateCapitalFundEventLedgerReadinessDto.Published,
                ExpectedLabel: "Published",
                ExpectedNextAction: "Open published report",
                ExpectedNextActionRouteFragment: "/api/fund-structure/report-packs/published-report-output",
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

    private static PrivateCapitalFundEventLedgerReadinessCase FindPrivateCapitalFundEventLedgerReadinessCase(string suffix)
    {
        foreach (var row in PrivateCapitalFundEventLedgerReadinessCases())
        {
            if (string.Equals(row.Suffix, suffix, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        throw new InvalidOperationException($"Unknown private-capital readiness case '{suffix}'.");
    }

    private static IReadOnlyList<PrivateCapitalReportOutputDto> AddRetainedReportEvidence(
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
        => reportOutputs
            .Select(output => output with
            {
                EvidenceLinkCount = 1,
                EvidenceLinks = [$"/api/workstation/evidence/report-packs/{output.ReportOutputId}"]
            })
            .ToArray();

    private static AccountingConfigurationService CreateService()
    {
        return new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
    }

    private static ManualJournalEntryWorkbenchService CreateManualJournalEntryWorkbenchService(
        IAccountingConfigurationService configurationService,
        ILedgerJournalStore? journalStore = null,
        ReportPackWorkflowService? reportPackWorkflowService = null,
        IBankTransactionSource? bankTransactionSource = null)
    {
        return new ManualJournalEntryWorkbenchService(
            new InMemoryManualJournalEntryDraftStore(),
            configurationService,
            new InMemoryAccountingActionAuditStore(),
            journalStore: journalStore,
            reportPackWorkflowService: reportPackWorkflowService,
            bankTransactionSource: bankTransactionSource);
    }

    private sealed class StaticBankTransactionSource(params BankTransactionDto[] transactions) : IBankTransactionSource
    {
        public Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(
            Guid? entityId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<BankTransactionDto>>(entityId.HasValue
                ? transactions.Where(transaction => transaction.EntityId == entityId.Value).ToArray()
                : transactions);
        }
    }

    private sealed class PostedPrivateCapitalLedgerJournalStore(
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        params LedgerJournalEntryRecord[] records) : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            throw new NotSupportedException("Test store is read-only.");

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                periodId == period.PeriodId ? records : []);
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                records.Where(record => record.AggregateId == aggregateId).ToArray());
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

    private static int InvokePublishedReportOutputCount(
        string fundProfileId,
        IReadOnlyList<PrivateCapitalFundEventLedgerEvent> postedEvents,
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords)
    {
        var method = typeof(ManualJournalEntryWorkbenchService).GetMethod(
            "CountPublishedReportOutputs",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        return (int)method!.Invoke(null, [fundProfileId, postedEvents, workflowRecords])!;
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
