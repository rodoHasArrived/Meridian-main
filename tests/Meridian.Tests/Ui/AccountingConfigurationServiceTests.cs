using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class AccountingConfigurationServiceTests
{
    private static readonly Guid ManualJournalLedgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ManualJournalPeriodId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DailyValuationAaplSecurityId = Guid.Parse("A1111111-1111-4111-8111-111111111111");
    private static readonly Guid DailyValuationMsftSecurityId = Guid.Parse("B2222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task AccountingConfigurationService_IsolatesWorkspacesByTenantAndCompanyScope()
    {
        var store = new InMemoryAccountingConfigurationStore();
        var auditStore = new InMemoryAccountingActionAuditStore();
        var service = new AccountingConfigurationService(store, auditStore);

        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto("cash-alpha", "Assets:Cash", "Alpha Cash", "Asset"),
            "controller-alpha",
            CompanyId: "company-alpha",
            TenantId: "tenant-alpha"));

        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto("cash-beta", "Assets:Cash", "Beta Cash", "Asset"),
            "controller-beta",
            CompanyId: "company-beta",
            TenantId: "tenant-beta"));

        var alpha = await service.GetWorkspaceAsync("fund-alpha", tenantId: "tenant-alpha", companyId: "company-alpha");
        var beta = await service.GetWorkspaceAsync("fund-alpha", tenantId: "tenant-beta", companyId: "company-beta");
        var unscoped = await service.GetWorkspaceAsync("fund-alpha");
        var alphaAudit = await service.ListAuditAsync("fund-alpha", companyId: "company-alpha");

        alpha.TenantId.Should().Be("tenant-alpha");
        alpha.CompanyId.Should().Be("company-alpha");
        alpha.ChartOfAccounts.Should().ContainSingle(node => node.NodeId == "cash-alpha");
        alpha.ChartOfAccounts.Should().NotContain(node => node.NodeId == "cash-beta");
        beta.TenantId.Should().Be("tenant-beta");
        beta.CompanyId.Should().Be("company-beta");
        beta.ChartOfAccounts.Should().ContainSingle(node => node.NodeId == "cash-beta");
        beta.ChartOfAccounts.Should().NotContain(node => node.NodeId == "cash-alpha");
        unscoped.ChartOfAccounts.Should().BeEmpty();
        alphaAudit.Should().ContainSingle(item =>
            item.Action == "chart.upsert" &&
            item.CompanyId == "company-alpha");
    }

    [Fact]
    public async Task AccountingConfigurationService_IsolatesAuditByTenantAndCompanyScope()
    {
        var store = new InMemoryAccountingConfigurationStore();
        var auditStore = new InMemoryAccountingActionAuditStore();
        var service = new AccountingConfigurationService(store, auditStore);

        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto("cash-tenant-alpha", "Assets:Cash", "Tenant Alpha Cash", "Asset"),
            "controller-alpha",
            CompanyId: "company-shared",
            TenantId: "tenant-alpha"));

        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto("cash-tenant-beta", "Assets:Cash", "Tenant Beta Cash", "Asset"),
            "controller-beta",
            CompanyId: "company-shared",
            TenantId: "tenant-beta"));

        var alphaAudit = await service.ListAuditAsync("fund-alpha", tenantId: "tenant-alpha", companyId: "company-shared");
        var betaAudit = await service.ListAuditAsync("fund-alpha", tenantId: "tenant-beta", companyId: "company-shared");
        var companyAudit = await service.ListAuditAsync("fund-alpha", companyId: "company-shared");

        alphaAudit.Should().ContainSingle(item =>
            item.Actor == "controller-alpha" &&
            item.TenantId == "tenant-alpha" &&
            item.CompanyId == "company-shared");
        alphaAudit.Should().NotContain(item => item.TenantId == "tenant-beta");
        betaAudit.Should().ContainSingle(item =>
            item.Actor == "controller-beta" &&
            item.TenantId == "tenant-beta" &&
            item.CompanyId == "company-shared");
        betaAudit.Should().NotContain(item => item.TenantId == "tenant-alpha");
        companyAudit.Should().HaveCount(2);
    }

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

        var projection = PrivateCapitalActivityProjectionBuilder.Build(
            new PrivateCapitalActivityProjectionInput(
                "fund-alpha",
                null,
                Drafts: [],
                Audit: [],
                BankTransactions: [],
                PostedProjection: new PostedPrivateCapitalActivityProjection(
                    new PrivateCapitalFundEventLedgerProjection([postedEvent]),
                    new Dictionary<Guid, string> { [journalEntryId] = "USD" }),
                ReportPackWorkflowRecords: [workflowRecord]));

        projection.PublishedReportOutputCount.Should().Be(1);
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

        var missingActivationEvidence = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            CorrelationId: "config-golden-activate-missing-evidence"));

        await missingActivationEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");

        var assistantActivation = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            CorrelationId: "config-golden-activate-assistant",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"],
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));

        await assistantActivation.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reviewed automation cannot activate accounting configurations*human operator*");

        var activated = await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            CorrelationId: "config-golden-activate",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        var audit = await service.ListAuditAsync("fund-alpha");

        activated.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activated.ValidationIssues.Should().NotContain(issue => issue.Code == "configuration.activation-evidence-required");
        activated.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        audit.Select(item => item.Action).Should().Contain(new[]
        {
            "chart.upsert",
            "template.upsert",
            "posting-rule.upsert",
            "configuration.activate"
        });
        audit.Should().NotContain(item => item.CorrelationId == "config-golden-activate-missing-evidence");
        audit.Should().OnlyContain(item => item.Actor.Length > 0);
        audit.Should().OnlyContain(item => item.BeforeHash.Length > 0 && item.AfterHash.Length > 0);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunUsesRequestedLedgerBookConfiguration()
    {
        var service = CreateService();
        var primaryBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var gaapBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await SeedBookScopedRuleAsync(service, primaryBookId, "primary", "Primary interest rule");
        await SeedBookScopedRuleAsync(service, gaapBookId, "gaap", "GAAP interest rule");

        var primaryDryRun = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            "fund-alpha",
            "InterestAccrual",
            100m,
            "USD",
            new DateOnly(2026, 6, 30),
            "controller",
            LedgerBookId: primaryBookId));
        var gaapDryRun = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            "fund-alpha",
            "InterestAccrual",
            100m,
            "USD",
            new DateOnly(2026, 6, 30),
            "controller",
            LedgerBookId: gaapBookId));

        primaryDryRun.LedgerBookId.Should().Be(primaryBookId);
        primaryDryRun.SelectedRuleId.Should().Be("rule-primary");
        primaryDryRun.RuleMatches.Should().ContainSingle(match => match.RuleId == "rule-primary");
        primaryDryRun.RuleMatches.Should().NotContain(match => match.RuleId == "rule-gaap");

        gaapDryRun.LedgerBookId.Should().Be(gaapBookId);
        gaapDryRun.SelectedRuleId.Should().Be("rule-gaap");
        gaapDryRun.RuleMatches.Should().ContainSingle(match => match.RuleId == "rule-gaap");
        gaapDryRun.RuleMatches.Should().NotContain(match => match.RuleId == "rule-primary");

        var primaryAudit = await service.ListAuditAsync("fund-alpha", primaryBookId);
        var gaapAudit = await service.ListAuditAsync("fund-alpha", gaapBookId);
        primaryAudit.Should().OnlyContain(item => item.LedgerBookId == primaryBookId);
        gaapAudit.Should().OnlyContain(item => item.LedgerBookId == gaapBookId);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_BookScopedWorkspaceRequiresConfiguredLedgerBook()
    {
        var missingBookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var configuredBookId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var service = CreateService(new StaticLedgerBookService(LedgerBook(configuredBookId, "fund-alpha")));

        await SeedBookScopedRuleAsync(service, missingBookId, "missing-book", "Missing book interest rule");

        var workspace = await service.GetWorkspaceAsync("fund-alpha", missingBookId);

        workspace.LedgerBooks.Should().ContainSingle(book => book.LedgerBookId == configuredBookId);
        workspace.LedgerBookSetupCandidate.Should().NotBeNull();
        workspace.LedgerBookSetupCandidate!.RequestedLedgerBookId.Should().Be(missingBookId);
        workspace.LedgerBookSetupCandidate.SourceLedgerBookId.Should().Be(configuredBookId);
        workspace.LedgerBookSetupCandidate.FundStructureNodeId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        workspace.LedgerBookSetupCandidate.FundStructureNodeKind.Should().Be(FundStructureNodeKindDto.Fund);
        workspace.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "configuration.ledger-book-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == missingBookId.ToString("D"));
        workspace.RulesStudio!.Summary.CriticalIssueCount.Should().BeGreaterThan(0);

        var activate = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            "fund-alpha",
            "controller",
            LedgerBookId: missingBookId,
            CorrelationId: "activate-missing-ledger-book",
            EvidenceLinks: [$"evidence://accounting/configuration/activation-approval/{missingBookId:D}"]));

        await activate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Accounting configuration cannot be activated while critical validation issues remain.");

        var audit = await service.ListAuditAsync("fund-alpha", missingBookId);
        audit.Should().NotContain(item => item.Action == "configuration.activate");
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
            saved.Version,
            LedgerBookId: ManualJournalLedgerBookId));

        saved.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        saved.TotalDebits.Should().Be(100m);
        saved.TotalCredits.Should().Be(100m);
        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        submitted.ApprovalId.Should().Be("manual-je-approval-" + submitted.JournalEntryId.ToString("N"));
        submitted.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        submitted.LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Submit &&
            transition.FromStatus == ManualJournalEntryStatusDto.Draft &&
            transition.ToStatus == ManualJournalEntryStatusDto.Submitted &&
            transition.Actor == "controller");
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.Drafts.Should().ContainSingle(item => item.JournalEntryId == submitted.JournalEntryId && item.Status == ManualJournalEntryStatusDto.Submitted);
        workbench.Drafts.Single(item => item.JournalEntryId == submitted.JournalEntryId).LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Submit &&
            transition.ToStatus == ManualJournalEntryStatusDto.Submitted);
        workbench.AuditTrail.Select(item => item.Action).Should().Contain(new[] { "manual-je.save-draft", "manual-je.submit-approval" });
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryWorkbench_IsolatesDraftsAndAuditByTenantAndCompanyScope()
    {
        var configuration = CreateService();
        await SeedManualJournalTenantConfigurationAsync(configuration, "tenant-alpha", "company-shared");
        await SeedManualJournalTenantConfigurationAsync(configuration, "tenant-beta", "company-shared");
        var service = CreateManualJournalEntryWorkbenchService(configuration);

        var alpha = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry() with
            {
                Memo = "Tenant alpha adjustment"
            },
            "alpha-controller",
            CorrelationId: "manual-je-alpha",
            TenantId: "tenant-alpha",
            CompanyId: "company-shared",
            ReportGroupPrincipalIds: ["accounting-alpha"]));
        var beta = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry() with
            {
                Memo = "Tenant beta adjustment"
            },
            "beta-controller",
            CorrelationId: "manual-je-beta",
            TenantId: "tenant-beta",
            CompanyId: "company-shared",
            ReportGroupPrincipalIds: ["accounting-beta"]));

        var alphaWorkbench = await service.GetWorkbenchAsync("fund-alpha", ManualJournalLedgerBookId, tenantId: "tenant-alpha", companyId: "company-shared");
        var betaWorkbench = await service.GetWorkbenchAsync("fund-alpha", ManualJournalLedgerBookId, tenantId: "tenant-beta", companyId: "company-shared");
        var companyWorkbench = await service.GetWorkbenchAsync("fund-alpha", ManualJournalLedgerBookId, companyId: "company-shared");

        alpha.TenantId.Should().Be("tenant-alpha");
        alpha.CompanyId.Should().Be("company-shared");
        beta.TenantId.Should().Be("tenant-beta");
        beta.CompanyId.Should().Be("company-shared");
        alphaWorkbench.Drafts.Should().ContainSingle(item => item.JournalEntryId == alpha.JournalEntryId && item.TenantId == "tenant-alpha");
        alphaWorkbench.Drafts.Should().NotContain(item => item.TenantId == "tenant-beta");
        betaWorkbench.Drafts.Should().ContainSingle(item => item.JournalEntryId == beta.JournalEntryId && item.TenantId == "tenant-beta");
        betaWorkbench.Drafts.Should().NotContain(item => item.TenantId == "tenant-alpha");
        companyWorkbench.Drafts.Where(item => item.CompanyId == "company-shared").Should().HaveCount(2);
        alphaWorkbench.AuditTrail.Should().ContainSingle(item =>
            item.Action == "manual-je.save-draft" &&
            item.TenantId == "tenant-alpha" &&
            item.CompanyId == "company-shared" &&
            item.ReportGroupPrincipalIds != null &&
            item.ReportGroupPrincipalIds.Contains("accounting-alpha"));
        alphaWorkbench.AuditTrail.Should().NotContain(item => item.TenantId == "tenant-beta");
        betaWorkbench.AuditTrail.Should().ContainSingle(item =>
            item.Action == "manual-je.save-draft" &&
            item.TenantId == "tenant-beta" &&
            item.CompanyId == "company-shared" &&
            item.ReportGroupPrincipalIds != null &&
            item.ReportGroupPrincipalIds.Contains("accounting-beta"));
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_RequiresTenantCompanyScopedEvidenceForTenantDrafts()
    {
        var configuration = CreateService();
        await SeedManualJournalTenantConfigurationAsync(configuration, "tenant-alpha", "company-shared");
        var service = CreateManualJournalEntryWorkbenchService(configuration);

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry(),
            "ops-user",
            TenantId: "tenant-alpha",
            CompanyId: "company-shared"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            LedgerBookId: saved.LedgerBookId,
            TenantId: "tenant-alpha",
            CompanyId: "company-shared"));

        var bookScopedOnly = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approved tenant-scoped manual journal.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted)],
            LedgerBookId: submitted.LedgerBookId,
            TenantId: "tenant-alpha",
            CompanyId: "company-shared"));
        await bookScopedOnly.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal approval and rejection evidence must reference reviewer intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var tenantScopedEvidence =
            $"/api/workstation/evidence/subjects/accounting-record/approval/tenant/tenant-alpha/company/company-shared/ledger-book/{submitted.LedgerBookId:D}/{submitted.PeriodId}";
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approved tenant-scoped manual journal.",
            EvidenceLinks: [tenantScopedEvidence],
            LedgerBookId: submitted.LedgerBookId,
            TenantId: "tenant-alpha",
            CompanyId: "company-shared"));

        approved.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        approved.JournalEntry.TenantId.Should().Be("tenant-alpha");
        approved.JournalEntry.CompanyId.Should().Be("company-shared");
        approved.Transition.EvidenceLinks.Should().Contain(tenantScopedEvidence);
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryWorkbench_IsolatesPostedPrivateCapitalActivityByTenantAndCompanyScope()
    {
        var configuration = CreateService();
        await SeedManualJournalTenantConfigurationAsync(configuration, "tenant-alpha", "company-shared");
        await SeedManualJournalTenantConfigurationAsync(configuration, "tenant-beta", "company-shared");
        var timestamp = DateTimeOffset.UtcNow;
        var alphaJournalEntryId = Guid.NewGuid();
        var betaJournalEntryId = Guid.NewGuid();
        var journalStore = new PostedPrivateCapitalLedgerJournalStore(
            new LedgerBookRecord(
                ManualJournalLedgerBookId,
                "fund-alpha",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "Fund Alpha GAAP book",
                "USD",
                timestamp,
                timestamp),
            new LedgerAccountingPeriod(
                ManualJournalPeriodId,
                ManualJournalLedgerBookId,
                2026,
                6,
                "2026-06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Closed",
                timestamp,
                timestamp,
                1),
            CreatePostedPrivateCapitalJournalRecord(alphaJournalEntryId, "tenant-alpha", "company-shared", "fund-event:alpha", "capital-account:alpha", 1, timestamp),
            CreatePostedPrivateCapitalJournalRecord(betaJournalEntryId, "tenant-beta", "company-shared", "fund-event:beta", "capital-account:beta", 2, timestamp));
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore);

        var alphaActivity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ManualJournalLedgerBookId, tenantId: "tenant-alpha", companyId: "company-shared");
        var betaActivity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ManualJournalLedgerBookId, tenantId: "tenant-beta", companyId: "company-shared");
        var companyActivity = await service.GetPrivateCapitalActivityAsync("fund-alpha", ManualJournalLedgerBookId, companyId: "company-shared");

        alphaActivity.FundEvents.Should().ContainSingle(item => item.FundEventId == "fund-event:alpha" && item.IsPosted);
        alphaActivity.FundEvents.Should().NotContain(item => item.FundEventId == "fund-event:beta");
        alphaActivity.LedgerImpacts.Should().ContainSingle(item => item.FundEventId == "fund-event:alpha");
        alphaActivity.CapitalAccountSubledgerEntries.Should().ContainSingle(item => item.CapitalAccountId == "capital-account:alpha");
        betaActivity.FundEvents.Should().ContainSingle(item => item.FundEventId == "fund-event:beta" && item.IsPosted);
        betaActivity.FundEvents.Should().NotContain(item => item.FundEventId == "fund-event:alpha");
        companyActivity.FundEvents.Select(item => item.FundEventId).Should().BeEquivalentTo("fund-event:alpha", "fund-event:beta");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_SubmitActionRetainsTransition()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));

        var result = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            saved.JournalEntryId,
            saved.FundProfileId,
            JournalEntryLifecycleActionDto.Submit,
            "controller",
            saved.Version,
            Notes: "Submit through lifecycle action.",
            CorrelationId: "manual-je-lifecycle-submit",
            EvidenceLinks: ["evidence://accounting/manual-je/submit"]));

        result.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        result.Transition.Action.Should().Be(JournalEntryLifecycleActionDto.Submit);
        result.Transition.FromStatus.Should().Be(ManualJournalEntryStatusDto.Draft);
        result.Transition.ToStatus.Should().Be(ManualJournalEntryStatusDto.Submitted);
        result.Transition.CorrelationId.Should().Be("manual-je-lifecycle-submit");
        result.Transition.EvidenceLinks.Should().Contain("evidence://accounting/manual-je/submit");
        result.JournalEntry.LifecycleTransitions.Should().ContainSingle(transition =>
            transition.TransitionId == result.Transition.TransitionId &&
            transition.Action == JournalEntryLifecycleActionDto.Submit);
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.AuditTrail.Should().ContainSingle(item =>
            item.Action == "manual-je.submit-approval" &&
            item.CorrelationId == "manual-je-lifecycle-submit");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_ReplayedCorrelationReturnsExistingTransition()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));

        var submitted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            saved.JournalEntryId,
            saved.FundProfileId,
            JournalEntryLifecycleActionDto.Submit,
            "controller",
            saved.Version,
            Notes: "Submit through lifecycle action.",
            CorrelationId: "manual-je-submit-idempotent",
            LedgerBookId: saved.LedgerBookId));
        var replayedSubmit = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            saved.JournalEntryId,
            saved.FundProfileId,
            JournalEntryLifecycleActionDto.Submit,
            "controller",
            saved.Version,
            Notes: "Submit through lifecycle action.",
            CorrelationId: "manual-je-submit-idempotent",
            LedgerBookId: saved.LedgerBookId));

        replayedSubmit.Transition.TransitionId.Should().Be(submitted.Transition.TransitionId);
        replayedSubmit.JournalEntry.Version.Should().Be(submitted.JournalEntry.Version);
        replayedSubmit.JournalEntry.LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Submit &&
            transition.CorrelationId == "manual-je-submit-idempotent");

        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntry.JournalEntryId,
            submitted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.JournalEntry.Version,
            Notes: "Controller approved with retained evidence.",
            CorrelationId: "manual-je-approve-idempotent",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted.JournalEntry)],
            LedgerBookId: submitted.JournalEntry.LedgerBookId));
        var replayedApprove = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntry.JournalEntryId,
            submitted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.JournalEntry.Version,
            Notes: "Controller approved with retained evidence.",
            CorrelationId: "manual-je-approve-idempotent",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted.JournalEntry)],
            LedgerBookId: submitted.JournalEntry.LedgerBookId));

        replayedApprove.Transition.TransitionId.Should().Be(approved.Transition.TransitionId);
        replayedApprove.JournalEntry.Version.Should().Be(approved.JournalEntry.Version);
        replayedApprove.JournalEntry.LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Approve &&
            transition.CorrelationId == "manual-je-approve-idempotent");

        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Post after approval evidence.",
            CorrelationId: "manual-je-post-idempotent",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)],
            LedgerBookId: approved.JournalEntry.LedgerBookId));
        var replayedPost = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Post after approval evidence.",
            CorrelationId: "manual-je-post-idempotent",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)],
            LedgerBookId: approved.JournalEntry.LedgerBookId));

        replayedPost.Transition.TransitionId.Should().Be(posted.Transition.TransitionId);
        replayedPost.JournalEntry.Version.Should().Be(posted.JournalEntry.Version);
        replayedPost.JournalEntry.LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.Post &&
            transition.CorrelationId == "manual-je-post-idempotent");

        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.AuditTrail.Should().ContainSingle(item =>
            item.Action == "manual-je.submit-approval" &&
            item.CorrelationId == "manual-je-submit-idempotent");
        workbench.AuditTrail.Should().ContainSingle(item =>
            item.Action == "manual-je.approve" &&
            item.CorrelationId == "manual-je-approve-idempotent");
        workbench.AuditTrail.Should().ContainSingle(item =>
            item.Action == "manual-je.post" &&
            item.CorrelationId == "manual-je-post-idempotent");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_PostAppendsDurableLedgerWrite()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var journalStore = WritableManualJournalLedgerJournalStore.Default();
        using var postingTarget = new RecordingGovernedLedgerPostingTarget(journalStore);
        var service = CreateManualJournalEntryWorkbenchService(
            configuration,
            journalStore,
            postingTarget: postingTarget);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var submitted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            saved.JournalEntryId,
            saved.FundProfileId,
            JournalEntryLifecycleActionDto.Submit,
            "controller",
            saved.Version,
            Notes: "Submit manual journal for review.",
            EvidenceLinks: ["evidence://accounting/manual-je/submit"],
            LedgerBookId: saved.LedgerBookId));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntry.JournalEntryId,
            submitted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.JournalEntry.Version,
            Notes: "Controller approved with retained evidence.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted.JournalEntry)],
            LedgerBookId: submitted.JournalEntry.LedgerBookId));
        var correlationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Controller posted after approval evidence.",
            CorrelationId: correlationId.ToString("D"),
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)],
            LedgerBookId: approved.JournalEntry.LedgerBookId));

        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        posted.PostedJournal.Should().NotBeNull();
        posted.PostedJournal!.JournalEntryId.Should().Be(saved.JournalEntryId);
        posted.PostedJournal.LedgerBookId.Should().Be(ManualJournalLedgerBookId);
        posted.PostedJournal.PeriodId.Should().Be(ManualJournalPeriodId);
        posted.PostedJournal.AggregateId.Should().Be(ManualJournalLedgerBookId);
        posted.PostedJournal.SourceEventId.Should().Be(saved.JournalEntryId);
        posted.PostedJournal.CorrelationId.Should().Be(correlationId);

        var write = journalStore.Appended.Should().ContainSingle().Subject;
        postingTarget.PostCount.Should().Be(1);
        // The governed target captures the write as received; the store persists the normalized
        // clone (normalize-on-append rebuilds the record), so compare by stable journal-entry identity.
        postingTarget.LastWrite.Should().NotBeNull();
        postingTarget.LastWrite!.Entry.JournalEntryId.Should().Be(write.Entry.JournalEntryId);
        write.AggregateId.Should().Be(ManualJournalLedgerBookId);
        write.LedgerBookId.Should().Be(ManualJournalLedgerBookId);
        write.PeriodId.Should().Be(ManualJournalPeriodId);
        write.SourceEventId.Should().Be(saved.JournalEntryId);
        write.AccountingBasis.Should().Be(AccountingBasisKindDto.Gaap);
        write.AccountingPolicyId.Should().Be("gaap-close-v1");
        write.PostingCommand.Should().NotBeNull();
        write.PostingCommand!.AggregateId.Should().Be(ManualJournalLedgerBookId);
        write.PostingCommand.PeriodId.Should().Be(ManualJournalPeriodId);
        write.PostingCommand.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Approved);
        write.PostingCommand.ActionOrigin.Should().Be(OperationsActionOriginDto.HumanOperator);
        write.PostingCommand.Evidence.Should().Contain(item => item.Kind == AccountingPostingEvidenceKindDto.Approval);
        write.PostingCommand.Evidence.Should().Contain(item => item.Kind == AccountingPostingEvidenceKindDto.AuditSupport);
        write.Entry.IsBalanced.Should().BeTrue();
        write.Entry.Lines.Sum(line => line.Debit).Should().Be(100m);
        write.Entry.Lines.Sum(line => line.Credit).Should().Be(100m);
        write.Entry.Metadata.IdempotencyKey.Should().Be($"manual-je:{ManualJournalLedgerBookId:N}:{saved.JournalEntryId:N}");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_InstrumentPostingWithoutAuthoritativeSecurityMasterFailsClosed()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(
            configuration,
            includeAuthoritativeSecurityMaster: false);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry(),
            "ops-user"));
        var submitted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            saved.JournalEntryId,
            saved.FundProfileId,
            JournalEntryLifecycleActionDto.Submit,
            "controller",
            saved.Version,
            Notes: "Submit instrument-bearing manual journal for review.",
            EvidenceLinks: ["evidence://accounting/manual-je/submit"],
            LedgerBookId: saved.LedgerBookId));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntry.JournalEntryId,
            submitted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.JournalEntry.Version,
            Notes: "Controller approved with retained evidence.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted.JournalEntry)],
            LedgerBookId: submitted.JournalEntry.LedgerBookId));

        var post = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Attempt posting without an authoritative Security Master.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)],
            LedgerBookId: approved.JournalEntry.LedgerBookId));

        await post.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Instrument-bearing manual journal posting requires an authoritative Security Master query service.");
    }

    /// <summary>
    /// Scenario: two listed securities receive a corrected same-day close. Both the original and
    /// corrected batches traverse intake and the all-entry approval/post lifecycle, while restart
    /// proof confirms the correction posts only the incremental carrying-value delta.
    /// </summary>
    [Fact]
    public async Task Scenario_SameDayMultiSecurityCorrection_PostsIncrementalBatchAndRestartsWithoutCompounding()
    {
        var asOf = new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero);
        var correctionAsOf = asOf.AddHours(2);
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "securities-aapl",
                "Assets:Securities:AAPL",
                "Securities",
                "Asset",
                Symbol: "AAPL"),
            "valuation-ops"));
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "unrealized-gain-aapl",
                "Income:Unrealized Gain:AAPL",
                "Unrealized Gain",
                "Revenue",
                FinancialAccountId: "AAPL"),
            "valuation-ops"));
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "securities-msft",
                "Assets:Securities:MSFT",
                "Securities",
                "Asset",
                Symbol: "MSFT"),
            "valuation-ops"));
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "unrealized-gain-msft",
                "Income:Unrealized Gain:MSFT",
                "Unrealized Gain",
                "Revenue",
                FinancialAccountId: "MSFT"),
            "valuation-ops"));
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "unrealized-loss",
                "Expenses:Unrealized Loss",
                "Unrealized Loss",
                "Expense"),
            "valuation-ops"));

        var journalStore = WritableManualJournalLedgerJournalStore.Default(AccountingBasisKindDto.Primary);
        journalStore.Seed(BuildJournal(
            asOf.AddDays(-10),
            "capital contribution",
            (LedgerAccounts.Cash, 25_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 25_000m)));
        journalStore.Seed(BuildJournal(
            asOf.AddDays(-9),
            "buy AAPL at cost",
            (LedgerAccounts.Securities("AAPL"), 15_000m, 0m),
            (LedgerAccounts.Cash, 0m, 15_000m)));
        journalStore.Seed(BuildJournal(
            asOf.AddDays(-8),
            "buy MSFT at cost",
            (LedgerAccounts.Securities("MSFT"), 10_000m, 0m),
            (LedgerAccounts.Cash, 0m, 10_000m)));

        var draftStore = new InMemoryManualJournalEntryDraftStore();
        using var postingTarget = new DurableLedgerPostingTarget(journalStore);
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configuration,
            new InMemoryAccountingActionAuditStore(),
            securityMasterQueryService: new DailyValuationSecurityMasterQueryService(),
            journalStore: journalStore,
            postingTarget: postingTarget);
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configuration);
        var positionService = CreateDailyValuationPositionService();
        var priceSource = new MutableMarkPriceSource();
        priceSource.Set("AAPL", new MarkPriceQuote(
            160m,
            "trusted-close",
            "evidence://prices/AAPL/2026-06-30/initial",
            new DateOnly(2026, 6, 30),
            DailyPortfolioPriceConfidence.High));
        priceSource.Set("MSFT", new MarkPriceQuote(
            210m,
            "trusted-close",
            "evidence://prices/MSFT/2026-06-30/initial",
            new DateOnly(2026, 6, 30),
            DailyPortfolioPriceConfidence.High));
        var runner = new AutomatedJournalIntakeRunner(
            intake,
            new FeeScheduleAccrualEventProducer(),
            dailyMarkToMarketService: new DailyMarkToMarketService(
                priceSource,
                new LedgerMarkToMarketCarryingValueSource(journalStore)),
            dailyValuationPositionService: positionService);
        var scheduleSource = new InMemoryDailyValuationPortfolioSource();
        var configured = await scheduleSource.SaveAsync(new DailyValuationScheduleWorkItem(
            "daily-valuation-fund-alpha",
            "fund-alpha",
            "USD",
            "valuation-ops",
            ManualJournalLedgerBookId,
            ManualJournalPeriodId,
            asOf,
            [
                new MarkToMarketPosition("AAPL", 100m, 150m, SecurityId: DailyValuationAaplSecurityId),
                new MarkToMarketPosition("MSFT", 50m, 200m, SecurityId: DailyValuationMsftSecurityId)
            ],
            "valuation-policy-1",
            "Daily close",
            "market-close",
            "cfo",
            asOf.AddMonths(-1),
            "End-of-day valuation",
            MaximumMarkAgeDays: 3,
            MinimumConfidence: DailyPortfolioPriceConfidence.Medium,
            RequireCompleteCoverage: true,
            ClosePeriodId: "2026-06",
            EntityId: "entity-master",
            UseStaticPositionOverride: true,
            StaticPositionsAsOfUtc: asOf));
        var scheduler = new DailyValuationScheduledWorker(
            scheduleSource,
            runner,
            NullLogger<DailyValuationScheduledWorker>.Instance,
            positionService);

        var beforeDue = await scheduler.RunDueAsync(asOf.AddTicks(-1));
        var firstBatch = await scheduler.RunDueAsync(asOf);
        var duplicateBatch = await scheduler.RunDueAsync(asOf);

        beforeDue.Runs.Should().BeEmpty();
        var scheduledRun = firstBatch.Runs.Should().ContainSingle().Subject;
        scheduledRun.State.Should().Be(DailyValuationScheduleStateDto.DraftReady);
        duplicateBatch.Runs.Should().BeEmpty("the scheduled timestamp is claimed exactly once");
        var initialDrafts = (await draftStore.ListAsync("fund-alpha", ManualJournalLedgerBookId))
            .OrderBy(static draft => draft.JournalEntryId)
            .ToArray();
        initialDrafts.Should().HaveCount(2).And.OnlyContain(draft => draft.Status == ManualJournalEntryStatusDto.Draft);
        scheduledRun.JournalEntryIds.Should().BeEquivalentTo(initialDrafts.Select(static draft => draft.JournalEntryId));
        initialDrafts.Should().OnlyContain(draft =>
            draft.TreasuryContext!.IdempotencyKey!.StartsWith(
                $"fair-value|fund=fund-alpha|period={ManualJournalPeriodId:D}|date=2026-06-30|",
                StringComparison.Ordinal));
        initialDrafts.Select(static draft => draft.TreasuryContext!.BatchCorrelationId)
            .Should().OnlyContain(correlationId => correlationId == scheduledRun.BatchCorrelationId);
        var scheduleStatus = await scheduleSource.GetStatusAsync(
            "fund-alpha",
            ManualJournalLedgerBookId,
            "2026-06",
            entityId: "entity-master");
        scheduleStatus.State.Should().Be(DailyValuationScheduleStateDto.DraftReady);
        scheduleStatus.NextRunAtUtc.Should().Be(asOf.AddDays(1));
        scheduleStatus.EvidenceLinks.Should().Contain(link => link.Route == "evidence://prices/AAPL/2026-06-30/initial");
        scheduleStatus.EvidenceLinks.Should().Contain(link => link.Route == "evidence://prices/MSFT/2026-06-30/initial");

        var batchLifecycle = new DailyValuationBatchLifecycleService(scheduleSource, draftStore, workbench);
        var initialPosting = await batchLifecycle.ApproveAndPostAsync(new DailyValuationBatchLifecycleRequestDto(
            configured.ScheduleId,
            configured.FundProfileId,
            "controller",
            "Approve and post the complete trusted closing-mark batch.",
            ["evidence://accounting/valuation/initial-batch"]));

        initialPosting.IsComplete.Should().BeTrue();
        initialPosting.JournalEntryIds.Should().HaveCount(2);
        initialPosting.PostedJournalEntryIds.Should().BeEquivalentTo(initialPosting.JournalEntryIds);
        journalStore.Appended.Should().HaveCount(2);

        priceSource.Set("AAPL", new MarkPriceQuote(
            162m,
            "trusted-corrected-close",
            "evidence://prices/AAPL/2026-06-30/correction",
            new DateOnly(2026, 6, 30),
            DailyPortfolioPriceConfidence.High));
        priceSource.Set("MSFT", new MarkPriceQuote(
            208m,
            "trusted-corrected-close",
            "evidence://prices/MSFT/2026-06-30/correction",
            new DateOnly(2026, 6, 30),
            DailyPortfolioPriceConfidence.High));
        var postedSchedule = await scheduleSource.GetAsync(configured.ScheduleId);
        postedSchedule.Should().NotBeNull();
        await scheduleSource.SaveAsync(postedSchedule! with
        {
            State = DailyValuationScheduleStateDto.Scheduled,
            NextRunAtUtc = correctionAsOf,
            StaticPositionsAsOfUtc = correctionAsOf,
            LastSummary = "Corrected same-day provider marks scheduled for review.",
            Blockers = []
        });

        var correctionBatch = await scheduler.RunDueAsync(correctionAsOf);
        var correctionRun = correctionBatch.Runs.Should().ContainSingle().Subject;
        correctionRun.State.Should().Be(
            DailyValuationScheduleStateDto.DraftReady,
            "the corrected marks should produce an incremental draft batch; summary: {0}; blockers: {1}",
            correctionRun.Summary,
            string.Join(" | ", correctionRun.Blockers));
        correctionRun.JournalEntryIds.Should().HaveCount(2)
            .And.NotBeEquivalentTo(initialPosting.JournalEntryIds);
        var correctionPosting = await batchLifecycle.ApproveAndPostAsync(new DailyValuationBatchLifecycleRequestDto(
            configured.ScheduleId,
            configured.FundProfileId,
            "controller",
            "Approve and post both corrected same-day marks.",
            ["evidence://accounting/valuation/correction-batch"]));

        correctionPosting.IsComplete.Should().BeTrue();
        correctionPosting.JournalEntryIds.Should().HaveCount(2);
        correctionPosting.PostedJournalEntryIds.Should().BeEquivalentTo(correctionPosting.JournalEntryIds);
        (await draftStore.ListAsync("fund-alpha", ManualJournalLedgerBookId))
            .Should().HaveCount(4)
            .And.OnlyContain(draft => draft.Status == ManualJournalEntryStatusDto.Posted);
        var finalSchedule = await scheduleSource.GetAsync(configured.ScheduleId);
        finalSchedule.Should().NotBeNull();
        var retainedFinalSchedule = finalSchedule!;
        retainedFinalSchedule.State.Should().Be(DailyValuationScheduleStateDto.Posted);
        retainedFinalSchedule.JournalEntryIds.Should().BeEquivalentTo(correctionPosting.JournalEntryIds);
        journalStore.Appended.Should().HaveCount(4);
        journalStore.Appended.GroupBy(static write => write.Entry.Metadata.Symbol)
            .Should().OnlyContain(group => group.Count() == 2);
        journalStore.Appended
            .Where(static write => write.Entry.Metadata.Symbol == "AAPL")
            .Select(static write => write.Entry.Lines.Max(line => Math.Max(line.Debit, line.Credit)))
            .Should().BeEquivalentTo([1_000m, 200m]);
        journalStore.Appended
            .Where(static write => write.Entry.Metadata.Symbol == "MSFT")
            .Select(static write => write.Entry.Lines.Max(line => Math.Max(line.Debit, line.Credit)))
            .Should().BeEquivalentTo([500m, 100m]);
        journalStore.Appended.Select(static write => write.Entry.Metadata.SecurityId)
            .Should().BeEquivalentTo(
                [
                    DailyValuationAaplSecurityId,
                    DailyValuationAaplSecurityId,
                    DailyValuationMsftSecurityId,
                    DailyValuationMsftSecurityId
                ]);

        // Recreate the journal-store process boundary from retained records, then rebuild every
        // downstream read without carrying any in-memory ledger projection across the restart.
        var restartedJournalStore = journalStore.RestartFromRetainedRecords();
        var restartedLedger = await restartedJournalStore.HydrateFundLedgerAsOfAsync(
            "fund-alpha",
            correctionAsOf,
            AccountingBasisKindDto.Primary);
        restartedLedger.Journal.Should().HaveCount(7);
        restartedLedger.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(16_200m);
        restartedLedger.GetBalance(LedgerAccounts.Securities("MSFT")).Should().Be(10_400m);

        var statements = LedgerFinancialStatementBuilder.BuildAsOf(restartedLedger, correctionAsOf);
        statements.TotalAssets.Should().Be(26_600m);
        statements.NetIncome.Should().Be(1_600m);

        var restartedFundBook = new FundLedgerBook("fund-alpha");
        foreach (var journal in restartedLedger.Journal)
        {
            restartedFundBook.FundLedger.Post(journal);
        }

        var nav = await new NavAttributionService(new NullSecurityMasterQueryService()).AttributeAsync(
            new NavAttributionRequest("fund-alpha", correctionAsOf, restartedFundBook));
        nav.Consolidated.TotalNav.Should().Be(26_600m);
    }

    [Fact]
    public async Task DailyValuationScheduler_EmptyConfiguredPortfolio_RecordsVisibleBlockedEvidence()
    {
        var dueAt = new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero);
        var configuration = CreateService();
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configuration,
            new InMemoryAccountingActionAuditStore());
        var positionService = CreateDailyValuationPositionService();
        var runner = new AutomatedJournalIntakeRunner(
            new AutomatedJournalDraftIntakeService(workbench, draftStore, configuration),
            new FeeScheduleAccrualEventProducer(),
            dailyMarkToMarketService: new DailyMarkToMarketService(
                Substitute.For<IMarkPriceSource>()),
            dailyValuationPositionService: positionService);
        var source = new InMemoryDailyValuationPortfolioSource();
        await source.SaveAsync(new DailyValuationScheduleWorkItem(
            "daily-valuation-empty-scope",
            "fund-alpha",
            "USD",
            "valuation-ops",
            ManualJournalLedgerBookId,
            ManualJournalPeriodId,
            dueAt,
            Positions: [],
            "valuation-policy-1",
            "Daily close",
            "market-close",
            "cfo",
            dueAt.AddMonths(-1),
            "End-of-day valuation",
            ClosePeriodId: "2026-06",
            UseStaticPositionOverride: true,
            StaticPositionsAsOfUtc: dueAt));
        var scheduler = new DailyValuationScheduledWorker(
            source,
            runner,
            NullLogger<DailyValuationScheduledWorker>.Instance,
            positionService);

        var batch = await scheduler.RunDueAsync(dueAt);
        var rerun = await scheduler.RunDueAsync(dueAt);
        var status = await source.GetStatusAsync("fund-alpha", ManualJournalLedgerBookId, "2026-06");

        batch.Runs.Should().ContainSingle(run =>
            run.State == DailyValuationScheduleStateDto.Blocked &&
            run.Blockers.Any(blocker => blocker.Contains("no open positions", StringComparison.OrdinalIgnoreCase)));
        rerun.Runs.Should().BeEmpty();
        status.State.Should().Be(DailyValuationScheduleStateDto.Blocked);
        status.Summary.Should().ContainEquivalentOf("no open positions");
        status.Blockers.Should().ContainSingle();
        status.NextRunAtUtc.Should().Be(dueAt.AddDays(1));
        (await draftStore.ListAsync("fund-alpha", ManualJournalLedgerBookId)).Should().BeEmpty();
    }

    [Fact]
    public async Task DailyValuationScheduler_AuthoritativeFlatSnapshot_RecordsNoAdjustment()
    {
        var dueAt = new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero);
        var accountId = Guid.NewGuid();
        var owner = new PositionSnapshotOwnerScope(
            "tenant-a",
            "company-a",
            "fund-alpha",
            ManualJournalLedgerBookId,
            "entity-master");
        var flatSnapshot = new AccountSnapshotRecord(
            "daily-flat-run",
            accountId.ToString("D"),
            "Primary brokerage",
            "Brokerage",
            Cash: 125_000m,
            MarginBalance: 0m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m,
            Positions: [],
            AsOf: dueAt.AddMinutes(-5),
            TenantId: owner.TenantId,
            CompanyId: owner.CompanyId,
            FundProfileId: owner.FundProfileId,
            LedgerBookId: owner.LedgerBookId,
            EntityId: owner.EntityId);
        var snapshotStore = Substitute.For<IPositionSnapshotStore>();
        snapshotStore.GetSnapshotHistoryAsync(
                flatSnapshot.RunId,
                flatSnapshot.AccountId,
                owner,
                Arg.Any<DateTimeOffset>(),
                dueAt,
                Arg.Any<CancellationToken>())
            .Returns(_ => SnapshotHistory(flatSnapshot));
        var configuration = CreateService();
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configuration,
            new InMemoryAccountingActionAuditStore());
        var positionService = CreateDailyValuationPositionService(snapshotStore);
        var markPriceSource = Substitute.For<IMarkPriceSource>();
        var runner = new AutomatedJournalIntakeRunner(
            new AutomatedJournalDraftIntakeService(workbench, draftStore, configuration),
            new FeeScheduleAccrualEventProducer(),
            dailyMarkToMarketService: new DailyMarkToMarketService(markPriceSource),
            dailyValuationPositionService: positionService);
        var source = new InMemoryDailyValuationPortfolioSource();
        await source.SaveAsync(new DailyValuationScheduleWorkItem(
            "daily-valuation-flat-scope",
            "fund-alpha",
            "USD",
            "valuation-ops",
            ManualJournalLedgerBookId,
            ManualJournalPeriodId,
            dueAt,
            Positions: [],
            "valuation-policy-1",
            "Daily close",
            "market-close",
            "cfo",
            dueAt.AddMonths(-1),
            "End-of-day valuation",
            ClosePeriodId: "2026-06",
            EntityId: owner.EntityId,
            TenantId: owner.TenantId,
            CompanyId: owner.CompanyId,
            PositionSnapshotScopes: [new DailyValuationPositionSnapshotScope(flatSnapshot.RunId, flatSnapshot.AccountId)]));
        var scheduler = new DailyValuationScheduledWorker(
            source,
            runner,
            NullLogger<DailyValuationScheduledWorker>.Instance,
            positionService);

        var batch = await scheduler.RunDueAsync(dueAt);
        var rerun = await scheduler.RunDueAsync(dueAt);
        var status = await source.GetStatusAsync(
            "fund-alpha",
            ManualJournalLedgerBookId,
            "2026-06",
            entityId: owner.EntityId,
            tenantId: owner.TenantId,
            companyId: owner.CompanyId);

        batch.Runs.Should().ContainSingle(run =>
            run.State == DailyValuationScheduleStateDto.NoAdjustment &&
            run.Summary.Contains("authoritative flat-position snapshot", StringComparison.OrdinalIgnoreCase));
        rerun.Runs.Should().BeEmpty();
        status.State.Should().Be(DailyValuationScheduleStateDto.NoAdjustment);
        status.Blockers.Should().BeEmpty();
        status.EvidenceLinks.Should().ContainSingle();
        (await draftStore.ListAsync("fund-alpha", ManualJournalLedgerBookId)).Should().BeEmpty();
        await markPriceSource.DidNotReceive().GetMarkPriceAsync(
            Arg.Any<string>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FileDailyValuationPortfolioSource_PersistsConfiguredScopeAcrossRestart()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "meridian-daily-valuation-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "daily-valuation-schedules.json");
        try
        {
            var dueAt = new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero);
            var source = new FileDailyValuationPortfolioSource(path);
            await source.SaveAsync(new DailyValuationScheduleWorkItem(
                "daily-valuation-persisted",
                "fund-alpha",
                "USD",
                "valuation-ops",
                ManualJournalLedgerBookId,
                ManualJournalPeriodId,
                dueAt,
                [new MarkToMarketPosition("AAPL", 100m, 150m)],
                "valuation-policy-1",
                "Daily close",
                "market-close",
                "cfo",
                dueAt.AddMonths(-1),
                "End-of-day valuation",
                ClosePeriodId: "2026-06"));

            var restarted = new FileDailyValuationPortfolioSource(path);
            var persisted = await restarted.GetAsync("daily-valuation-persisted");
            var status = await restarted.GetStatusAsync("fund-alpha", ManualJournalLedgerBookId, "2026-06");

            persisted.Should().NotBeNull();
            persisted!.Positions.Should().ContainSingle(position =>
                position.Symbol == "AAPL" && position.Quantity == 100m && position.CostPrice == 150m);
            status.IsConfigured.Should().BeTrue();
            status.State.Should().Be(DailyValuationScheduleStateDto.Scheduled);
            status.NextRunAtUtc.Should().Be(dueAt);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_PostWithoutLedgerStoreFailsClosed()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration, includeDefaultJournalStore: false);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var submitted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            saved.JournalEntryId,
            saved.FundProfileId,
            JournalEntryLifecycleActionDto.Submit,
            "controller",
            saved.Version,
            Notes: "Submit manual journal for review.",
            EvidenceLinks: ["evidence://accounting/manual-je/submit"],
            LedgerBookId: saved.LedgerBookId));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntry.JournalEntryId,
            submitted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.JournalEntry.Version,
            Notes: "Controller approved with retained evidence.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted.JournalEntry)],
            LedgerBookId: submitted.JournalEntry.LedgerBookId));

        var post = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Controller posted after approval evidence.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)],
            LedgerBookId: approved.JournalEntry.LedgerBookId));

        await post.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no ledger journal store is configured*");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_RequiresIndependentLifecycleActor()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);

        var selfPrepared = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "controller"));
        var selfSubmitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            selfPrepared.JournalEntryId,
            selfPrepared.FundProfileId,
            "controller",
            selfPrepared.Version,
            Notes: "Submit own prepared draft for review."));
        var selfApprove = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            selfSubmitted.JournalEntryId,
            selfSubmitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            selfSubmitted.Version,
            Notes: "Attempt to approve own prepared journal.",
            EvidenceLinks: [ManualJournalApprovalEvidence(selfSubmitted)]));

        await selfApprove.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires an independent actor*prepared the journal entry*");

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            Notes: "Submit for independent review."));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Controller approved with retained evidence.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted)]));
        var selfPost = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "ops-user",
            approved.JournalEntry.Version,
            Notes: "Attempt to post own prepared journal.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)]));

        await selfPost.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires an independent actor*prepared the journal entry*");

        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Controller posted after approval evidence.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)]));
        var selfReverse = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "ops-user",
            posted.JournalEntry.Version,
            Notes: "Attempt to reverse own prepared journal.",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{posted.JournalEntry.LedgerBookId:D}/{posted.JournalEntry.PeriodId}"]));

        await selfReverse.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires an independent actor*prepared the journal entry*");

        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.AuditTrail.Should().NotContain(item =>
            (item.Action == "manual-je.approve" ||
                item.Action == "manual-je.post" ||
                item.Action == "manual-je.reverse") &&
            item.Actor == "ops-user");
        workbench.Drafts.Should().ContainSingle(item =>
            item.JournalEntryId == posted.JournalEntry.JournalEntryId &&
            item.Status == ManualJournalEntryStatusDto.Posted);
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_DraftSaveHonorsLifecycleMutability()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            Notes: "Submit for controller review."));

        var editSubmitted = async () => await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            submitted with { Memo = "Attempted edit while submitted." },
            "ops-user"));

        await editSubmitted.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*is Submitted and cannot be edited through draft save*");

        var resubmitSubmitted = async () => await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            "controller",
            submitted.Version,
            Notes: "Duplicate submission should fail."));
        await resubmitSubmitted.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*is Submitted and cannot be submitted for approval*");
        submitted.ApprovalId.Should().NotBeNullOrWhiteSpace();
        submitted.SubmittedAtUtc.Should().NotBeNull();
        submitted.SubmittedBy.Should().Be("controller");

        var rejected = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Reject,
            "controller",
            submitted.Version,
            Notes: "Controller rejected for correction.",
            EvidenceLinks: [ManualJournalReviewEvidence(submitted)]));
        var repaired = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            rejected.JournalEntry with { Memo = "Corrected after rejection." },
            "ops-user"));

        repaired.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        repaired.Version.Should().Be(rejected.JournalEntry.Version + 1);
        repaired.Memo.Should().Be("Corrected after rejection.");
        repaired.ApprovalId.Should().BeNull();
        repaired.SubmittedAtUtc.Should().BeNull();
        repaired.SubmittedBy.Should().BeNull();
        repaired.ApprovedAtUtc.Should().BeNull();
        repaired.ApprovedBy.Should().BeNull();
        repaired.LifecycleTransitions.Should().Contain(item =>
            item.Action == JournalEntryLifecycleActionDto.Reject &&
            item.ToStatus == ManualJournalEntryStatusDto.Rejected);

        var resubmitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            repaired.JournalEntryId,
            repaired.FundProfileId,
            "controller",
            repaired.Version,
            Notes: "Resubmit corrected journal."));
        resubmitted.ApprovalId.Should().NotBeNullOrWhiteSpace();
        resubmitted.SubmittedAtUtc.Should().NotBeNull();
        resubmitted.SubmittedBy.Should().Be("controller");
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            resubmitted.JournalEntryId,
            resubmitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            resubmitted.Version,
            Notes: "Controller approved corrected journal.",
            EvidenceLinks: [ManualJournalApprovalEvidence(resubmitted)]));
        var editApproved = async () => await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            approved.JournalEntry with { Memo = "Attempted edit while approved." },
            "ops-user"));

        await editApproved.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*is Approved and cannot be edited through draft save*");

        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Post corrected journal.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)]));
        var editPosted = async () => await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            posted.JournalEntry with { Memo = "Attempted edit after posting." },
            "ops-user"));

        await editPosted.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*is Posted and cannot be edited through draft save*");
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.AuditTrail.Select(item => item.Action).Should().Contain(new[]
        {
            "manual-je.reject",
            "manual-je.save-draft",
            "manual-je.submit-approval",
            "manual-je.approve",
            "manual-je.post"
        });
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_AttachesEvidenceWithVersionGuardAndImmutablePostedProtection()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry() with { EvidenceAttachments = [], EvidenceLinks = [] },
            "ops-user"));

        var attached = await service.AttachEvidenceAsync(new AttachManualJournalEntryEvidenceRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            new ManualJournalEntryEvidenceAttachmentDto(
                "controller-close-support",
                "Controller close support",
                "SourceDocument",
                "/api/workstation/evidence/subjects/accounting-record/controller-close-support",
                "EvidenceVault",
                DateTimeOffset.UtcNow,
                "controller",
                LineId: "debit-cash"),
            CorrelationId: "manual-je-attach-evidence",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/close-checklist"]));

        attached.Version.Should().Be(saved.Version + 1);
        attached.EvidenceAttachments.Should().ContainSingle(item =>
            item.AttachmentId == "controller-close-support" &&
            item.LineId == "debit-cash");
        attached.EvidenceLinks.Should().Contain(new[]
        {
            "/api/workstation/evidence/subjects/accounting-record/close-checklist",
            "/api/workstation/evidence/subjects/accounting-record/controller-close-support"
        });
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.AuditTrail.Should().Contain(item =>
            item.Action == "manual-je.attach-evidence" &&
            item.CorrelationId == "manual-je-attach-evidence");

        var stale = async () => await service.AttachEvidenceAsync(new AttachManualJournalEntryEvidenceRequest(
            attached.JournalEntryId,
            attached.FundProfileId,
            "controller",
            saved.Version,
            new ManualJournalEntryEvidenceAttachmentDto(
                "stale-evidence",
                "Stale evidence",
                "SourceDocument",
                "/api/workstation/evidence/subjects/accounting-record/stale",
                "EvidenceVault",
                DateTimeOffset.UtcNow,
                "controller")));
        await stale.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal entry draft version is stale.");

        var missingLine = async () => await service.AttachEvidenceAsync(new AttachManualJournalEntryEvidenceRequest(
            attached.JournalEntryId,
            attached.FundProfileId,
            "controller",
            attached.Version,
            new ManualJournalEntryEvidenceAttachmentDto(
                "missing-line-evidence",
                "Missing line evidence",
                "SourceDocument",
                "/api/workstation/evidence/subjects/accounting-record/missing-line",
                "EvidenceVault",
                DateTimeOffset.UtcNow,
                "controller",
                LineId: "missing-line")));
        await missingLine.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal evidence attachment references missing line 'missing-line'.");

        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            attached.JournalEntryId,
            attached.FundProfileId,
            "controller",
            attached.Version));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Controller approved attached source evidence.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted)]));
        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Posted after evidence attachment.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)]));
        var immutable = async () => await service.AttachEvidenceAsync(new AttachManualJournalEntryEvidenceRequest(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            "controller",
            posted.JournalEntry.Version,
            new ManualJournalEntryEvidenceAttachmentDto(
                "posted-evidence",
                "Posted evidence",
                "SourceDocument",
                "/api/workstation/evidence/subjects/accounting-record/posted",
                "EvidenceVault",
                DateTimeOffset.UtcNow,
                "controller")));
        await immutable.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Posted, reversed, rebooked, and close-locked journal entries are immutable; attach evidence before posting or create a correction draft.");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_PeriodLockBlocksMutationsAndValidatesAsCritical()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry();

        var lockedSave = async () => await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            draft,
            "ops-user",
            PeriodIsLocked: true));
        await lockedSave.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot save manual journal entry drafts because the accounting period is locked after close.");

        var validated = await service.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(
            draft,
            "controller",
            PeriodIsLocked: true));
        validated.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "manual-je.period-locked" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        var lockedSubmit = async () => await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            PeriodIsLocked: true));
        await lockedSubmit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot submit manual journal entries for approval because the accounting period is locked after close.");

        var lockedEvidence = async () => await service.AttachEvidenceAsync(new AttachManualJournalEntryEvidenceRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            new ManualJournalEntryEvidenceAttachmentDto(
                "locked-period-evidence",
                "Locked period evidence",
                "SourceDocument",
                "/api/workstation/evidence/subjects/accounting-record/locked-period",
                "EvidenceVault",
                DateTimeOffset.UtcNow,
                "controller"),
            PeriodIsLocked: true));
        await lockedEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot attach evidence to manual journal entries because the accounting period is locked after close.");

        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            LedgerBookId: ManualJournalLedgerBookId));
        var lockedApprove = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            LedgerBookId: submitted.LedgerBookId,
            PeriodIsLocked: true));
        await lockedApprove.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal entry lifecycle action is blocked because the accounting period is locked after close.");

        var lifecycleValidate = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Validate,
            "controller",
            submitted.Version,
            LedgerBookId: submitted.LedgerBookId,
            PeriodIsLocked: true));
        lifecycleValidate.JournalEntry.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "manual-je.period-locked" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_LedgerBookNativePeriodValidationBlocksMismatchedBook()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var selectedBookId = Guid.NewGuid();
        var periodBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var book = new LedgerBookRecord(
            selectedBookId,
            "fund-alpha",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "GAAP close book",
            "USD",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-close-v1",
            AccountingPolicyVersion: "v1");
        var period = new LedgerAccountingPeriod(
            periodId,
            periodBookId,
            2026,
            6,
            "2026-06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "Open",
            DateTimeOffset.UtcNow,
            null,
            1);
        var service = CreateManualJournalEntryWorkbenchService(
            configuration,
            journalStore: new PostedPrivateCapitalLedgerJournalStore(book, period));
        var draft = BalancedManualJournalEntry() with
        {
            LedgerBookId = selectedBookId,
            PeriodId = periodId.ToString("D")
        };

        var validated = await service.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(
            draft,
            "controller"));

        validated.ValidationIssues.Should().ContainSingle(issue =>
            issue.Code == "manual-je.period-book-mismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "periodId");

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(draft, "ops-user"));
        var submit = async () => await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version));
        await submit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal entry cannot be submitted while critical validation issues remain.");
    }

    [Fact]
    public async Task ManualJournalEntryCloseLock_WithoutJournalStoreRejectsCallerReportedLock()
    {
        var fixture = await CreateManualJournalCloseLockAuthorityFixtureAsync(authoritativePeriod: null);

        var act = async () => await fixture.Service.ApplyLifecycleActionAsync(
            ManualJournalCloseLockRequest(fixture.Posted, periodIsLocked: true));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exact server-owned HardClosed ledger period*");
        var retained = await fixture.DraftStore.GetAsync(
            fixture.Posted.FundProfileId,
            fixture.Posted.JournalEntryId);
        retained.Should().NotBeNull();
        retained!.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        retained.Version.Should().Be(fixture.Posted.Version);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("SoftClosed")]
    public async Task ManualJournalEntryCloseLock_NonHardClosedServerPeriodRejectsCallerReportedLock(
        string periodStatus)
    {
        var fixture = await CreateManualJournalCloseLockAuthorityFixtureAsync(
            ManualJournalAccountingPeriod(periodStatus));

        var act = async () => await fixture.Service.ApplyLifecycleActionAsync(
            ManualJournalCloseLockRequest(fixture.Posted, periodIsLocked: true));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exact server-owned HardClosed ledger period*");
        var retained = await fixture.DraftStore.GetAsync(
            fixture.Posted.FundProfileId,
            fixture.Posted.JournalEntryId);
        retained.Should().NotBeNull();
        retained!.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        retained.Version.Should().Be(fixture.Posted.Version);
    }

    [Fact]
    public async Task ManualJournalEntryCloseLock_ExactHardClosedServerPeriodGrantsWhenCallerReportsUnlocked()
    {
        var fixture = await CreateManualJournalCloseLockAuthorityFixtureAsync(
            ManualJournalAccountingPeriod("HardClosed"));

        var result = await fixture.Service.ApplyLifecycleActionAsync(
            ManualJournalCloseLockRequest(fixture.Posted, periodIsLocked: false));

        result.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.CloseLocked);
        result.Transition.Action.Should().Be(JournalEntryLifecycleActionDto.LockAfterClose);
        result.Transition.FromStatus.Should().Be(ManualJournalEntryStatusDto.Posted);
        result.Transition.ToStatus.Should().Be(ManualJournalEntryStatusDto.CloseLocked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualJournalEntryCloseLock_HardClosedLookupWithMismatchedScopeRejectsCallerReportedLock(
        bool mismatchPeriodId)
    {
        var authoritativePeriod = ManualJournalAccountingPeriod(
            "HardClosed",
            periodId: mismatchPeriodId ? Guid.NewGuid() : ManualJournalPeriodId,
            ledgerBookId: mismatchPeriodId ? ManualJournalLedgerBookId : Guid.NewGuid());
        var fixture = await CreateManualJournalCloseLockAuthorityFixtureAsync(authoritativePeriod);

        var act = async () => await fixture.Service.ApplyLifecycleActionAsync(
            ManualJournalCloseLockRequest(fixture.Posted, periodIsLocked: true));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exact server-owned HardClosed ledger period*");
        var retained = await fixture.DraftStore.GetAsync(
            fixture.Posted.FundProfileId,
            fixture.Posted.JournalEntryId);
        retained.Should().NotBeNull();
        retained!.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        retained.Version.Should().Be(fixture.Posted.Version);
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_LifecycleMutationsRejectMismatchedLedgerBookScope()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var draft = BalancedManualJournalEntry();
        var savedLedgerBookId = draft.LedgerBookId!.Value;
        var wrongLedgerBookId = Guid.NewGuid();

        var save = async () => await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            draft,
            "ops-user",
            LedgerBookId: wrongLedgerBookId));
        await save.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*belongs to ledger book '{savedLedgerBookId:D}', not requested ledger book '{wrongLedgerBookId:D}'*");

        var validate = async () => await service.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(
            draft,
            "controller",
            LedgerBookId: wrongLedgerBookId));
        await validate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*belongs to ledger book '{savedLedgerBookId:D}', not requested ledger book '{wrongLedgerBookId:D}'*");

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            draft,
            "ops-user",
            LedgerBookId: savedLedgerBookId));

        var unscopedEdit = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            saved with { Memo = "Unscoped retained draft edit" },
            "ops-user"));
        unscopedEdit.LedgerBookId.Should().Be(savedLedgerBookId);
        unscopedEdit.Memo.Should().Be("Unscoped retained draft edit");

        var submit = async () => await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            unscopedEdit.JournalEntryId,
            unscopedEdit.FundProfileId,
            "controller",
            unscopedEdit.Version,
            LedgerBookId: wrongLedgerBookId));
        await submit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*belongs to ledger book '{savedLedgerBookId:D}', not requested ledger book '{wrongLedgerBookId:D}'*");

        var attach = async () => await service.AttachEvidenceAsync(new AttachManualJournalEntryEvidenceRequest(
            unscopedEdit.JournalEntryId,
            unscopedEdit.FundProfileId,
            "controller",
            unscopedEdit.Version,
            new ManualJournalEntryEvidenceAttachmentDto(
                "wrong-book-evidence",
                "Wrong book evidence",
                "SourceDocument",
                "/api/workstation/evidence/subjects/accounting-record/wrong-book",
                "EvidenceVault",
                DateTimeOffset.UtcNow,
                "controller"),
            LedgerBookId: wrongLedgerBookId));
        await attach.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*belongs to ledger book '{savedLedgerBookId:D}', not requested ledger book '{wrongLedgerBookId:D}'*");

        var lifecycle = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            unscopedEdit.JournalEntryId,
            unscopedEdit.FundProfileId,
            JournalEntryLifecycleActionDto.Validate,
            "controller",
            unscopedEdit.Version,
            LedgerBookId: wrongLedgerBookId));
        await lifecycle.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*belongs to ledger book '{savedLedgerBookId:D}', not requested ledger book '{wrongLedgerBookId:D}'*");

        var workbench = await service.GetWorkbenchAsync("fund-alpha", savedLedgerBookId);
        workbench.Drafts.Should().ContainSingle(item =>
            item.JournalEntryId == unscopedEdit.JournalEntryId &&
            item.Version == unscopedEdit.Version &&
            item.Status == ManualJournalEntryStatusDto.Draft);
    }

    [Fact]
    public async Task Scenario_ManualJournalEntry_DimensionsNormalizeAndPropagateToLines()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var source = BalancedManualJournalEntry() with
        {
            Dimensions = new LedgerDimensionSetDto(
                FundId: " fund-alpha ",
                EntityId: " entity-master ",
                SleeveId: " sleeve-core ",
                StrategyId: " strategy-carry ",
                InvestorId: " investor:lp-1 ",
                CapitalAccountId: " capital-account:fund-alpha:lp-1 ",
                CounterpartyId: " counterparty-bank ",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [" Department "] = " Fund Ops ",
                    ["Blank"] = " "
                }),
            Lines =
            [
                BalancedManualJournalEntry().Lines[0] with { TaxLotId = " tax-lot-001 " },
                BalancedManualJournalEntry().Lines[1] with
                {
                    EntityId = " entity-special-purpose ",
                    Dimensions = new LedgerDimensionSetDto(
                        CostCenterId: " cost-center-42 ",
                        ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Book"] = " GAAP "
                        })
                }
            ]
        };

        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(source, "ops-user"));
        var validated = await service.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(saved, "controller"));

        saved.Dimensions.Should().NotBeNull();
        saved.Dimensions!.FundId.Should().Be("fund-alpha");
        saved.Dimensions.EntityId.Should().Be("entity-master");
        saved.Dimensions.ExternalGlDimensions.Should().ContainKey("Department")
            .WhoseValue.Should().Be("Fund Ops");
        saved.Dimensions.ExternalGlDimensions.Should().NotContainKey("Blank");
        saved.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "manual-je.dimension-fund-missing" ||
            issue.Code == "manual-je.dimension-entity-missing");
        var instrumentLine = saved.Lines.Single(line => line.LineId == "debit-cash");
        instrumentLine.Dimensions.Should().NotBeNull();
        instrumentLine.Dimensions!.FundId.Should().Be("fund-alpha");
        instrumentLine.Dimensions.EntityId.Should().Be("entity-master");
        instrumentLine.Dimensions.InstrumentId.Should().Be(instrumentLine.SecurityId);
        instrumentLine.Dimensions.TaxLotId.Should().Be("tax-lot-001");
        instrumentLine.Dimensions.ExternalGlDimensions.Should().ContainKey("Department")
            .WhoseValue.Should().Be("Fund Ops");
        var costCenterLine = saved.Lines.Single(line => line.LineId == "credit-income");
        costCenterLine.Dimensions.Should().NotBeNull();
        costCenterLine.Dimensions!.FundId.Should().Be("fund-alpha");
        costCenterLine.Dimensions.EntityId.Should().Be("entity-special-purpose");
        costCenterLine.Dimensions.CostCenterId.Should().Be("cost-center-42");
        costCenterLine.Dimensions.ExternalGlDimensions.Should().ContainKey("Department");
        costCenterLine.Dimensions.ExternalGlDimensions.Should().ContainKey("Book")
            .WhoseValue.Should().Be("GAAP");
        validated.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "manual-je.dimension-fund-missing" ||
            issue.Code == "manual-je.dimension-entity-missing");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunSelectsEffectiveScopedPriorityRuleAndGeneratesBalancedLines()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-generated",
                DisplayName: "Generated interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                Description: "Generated multi-line rule for interest accruals.",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                EffectiveTo: new DateOnly(2026, 12, 31),
                Priority: 100,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master", CounterpartyId: "counterparty-bank"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "threshold",
                        "amount",
                        AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                        "100"),
                    new AccountingRuleConditionDto(
                        "counterparty",
                        "counterpartyId",
                        AccountingRuleConditionOperatorDto.Equals,
                        "counterparty-bank")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "counterparty-bank"));

        result.SelectedRuleId.Should().Be("rule-interest-generated");
        result.IsPostingBalanced.Should().BeTrue();
        result.GeneratedPostingLines.Should().HaveCount(2);
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Amount == 250m);
        result.RuleMatches.Should().ContainSingle(item => item.RuleId == "rule-interest-generated" && item.IsMatched);
        result.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunEnforcesExternalGlDimensionScope()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-external-gl-department",
                DisplayName: "Department-scoped interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 125,
                Scope: new LedgerDimensionSetDto(
                    FundId: "fund-alpha",
                    EntityId: "entity-master",
                    ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Department"] = "Fund Ops"
                    }),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "department-predicate",
                        "externalGl.Department",
                        AccountingRuleConditionOperatorDto.Equals,
                        "Fund Ops"),
                    new AccountingRuleConditionDto(
                        "department-alias-predicate",
                        "gl.Department",
                        AccountingRuleConditionOperatorDto.IsPresent)
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var wrongDepartment = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "Investor Relations"
                })));
        var rightDepartment = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "Fund Ops"
                })));

        wrongDepartment.SelectedRuleId.Should().BeNull();
        wrongDepartment.IsPostingBalanced.Should().BeFalse();
        wrongDepartment.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-external-gl-department" &&
            !item.IsMatched &&
            item.Explanations.Contains("Rule scope does not match the dry-run dimensions."));
        rightDepartment.SelectedRuleId.Should().Be("rule-interest-external-gl-department");
        rightDepartment.IsPostingBalanced.Should().BeTrue();
        rightDepartment.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-external-gl-department" &&
            item.Explanations.Contains("Condition 'department-predicate' matched.") &&
            item.Explanations.Contains("Condition 'department-alias-predicate' matched."));
        foreach (var line in rightDepartment.GeneratedPostingLines)
        {
            line.Dimensions.Should().NotBeNull();
            line.Dimensions!.ExternalGlDimensions.Should().ContainKey("Department")
                .WhoseValue.Should().Be("Fund Ops");
        }
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsMalformedAmountCondition()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var workspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-invalid-threshold",
                DisplayName: "Invalid-threshold interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 140,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "malformed-threshold",
                        "amount",
                        AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                        "not-a-number")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.condition-amount-invalid" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "malformed-threshold");
        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-invalid-threshold" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "rule.condition-amount-invalid" &&
                issue.TargetId == "malformed-threshold"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.condition-amount-invalid" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "malformed-threshold");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsTextConditionWithoutComparisonValue()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var workspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-missing-text-value",
                DisplayName: "Missing text predicate value interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 141,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "counterparty-empty",
                        "counterpartyId",
                        AccountingRuleConditionOperatorDto.Equals)
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.condition-value-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "counterparty-empty");
        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.GeneratedPostingLines.Should().BeEmpty();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-missing-text-value" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "rule.condition-value-missing" &&
                issue.TargetId == "counterparty-empty"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.condition-value-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "counterparty-empty");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsInvertedAmountBetweenCondition()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var workspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-inverted-range",
                DisplayName: "Inverted-range interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 145,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "inverted-range",
                        "amount",
                        AccountingRuleConditionOperatorDto.AmountBetween,
                        "500",
                        "100")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.condition-amount-range-invalid" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "inverted-range");
        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-inverted-range" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "rule.condition-amount-range-invalid" &&
                issue.TargetId == "inverted-range"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.condition-amount-range-invalid" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "inverted-range");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsConditionWithoutStableId()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var workspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-missing-condition-id",
                DisplayName: "Missing condition id interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 146,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "",
                        "amount",
                        AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                        "100")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.condition-id-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "rule-interest-missing-condition-id");
        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-missing-condition-id" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "rule.condition-id-missing" &&
                issue.TargetId == "rule-interest-missing-condition-id"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.condition-id-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "rule-interest-missing-condition-id");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsDuplicateConditionIds()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var workspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-duplicate-condition-id",
                DisplayName: "Duplicate condition id interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 147,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "amount-threshold",
                        "amount",
                        AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                        "100"),
                    new AccountingRuleConditionDto(
                        "amount-threshold",
                        "currency",
                        AccountingRuleConditionOperatorDto.Equals,
                        "USD")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.condition-id-duplicate" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "amount-threshold");
        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-duplicate-condition-id" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "rule.condition-id-duplicate" &&
                issue.TargetId == "amount-threshold"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.condition-id-duplicate" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "amount-threshold");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunEvaluatesAnyConditionGroups()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-grouped",
                DisplayName: "Grouped interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 150,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                ConditionGroups:
                [
                    new AccountingRuleConditionGroupDto(
                        "large-or-bank",
                        AccountingRuleConditionGroupOperatorDto.Any,
                        [
                            new AccountingRuleConditionDto(
                                "large-amount",
                                "amount",
                                AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                                "500"),
                            new AccountingRuleConditionDto(
                                "bank-counterparty",
                                "counterpartyId",
                                AccountingRuleConditionOperatorDto.Equals,
                                "counterparty-bank")
                        ])
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var matchedByAmount = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 750m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "counterparty-other"));
        var notMatched = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 50m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "counterparty-other"));

        matchedByAmount.SelectedRuleId.Should().Be("rule-interest-grouped");
        matchedByAmount.IsPostingBalanced.Should().BeTrue();
        matchedByAmount.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-grouped" &&
            item.IsMatched &&
            item.Explanations.Contains("Condition group 'large-or-bank' matched."));
        notMatched.SelectedRuleId.Should().BeNull();
        notMatched.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-grouped" &&
            !item.IsMatched &&
            item.Explanations.Contains("Required condition group 'large-or-bank' did not match."));
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunSurfacesInvalidEffectiveDateWindow()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var upserted = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-invalid-window",
                DisplayName: "Invalid-window interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 12, 31),
                EffectiveTo: new DateOnly(2026, 1, 1),
                Priority: 250,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        upserted.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.effective-date-range" &&
            issue.TargetId == "rule-interest-invalid-window" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-invalid-window" &&
            !item.IsMatched &&
            item.Explanations.Contains("Rule effective-date window is invalid.") &&
            item.ValidationIssues.Any(issue => issue.Code == "posting-rule.effective-date-range"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.effective-date-range" &&
            issue.TargetId == "rule-interest-invalid-window" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_UpsertRetainsRuleVersionHistory()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-versioned",
                DisplayName: "Versioned interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 100,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-versioned/v1"]));

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-versioned",
                DisplayName: "Versioned interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 200,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ],
                PromotionApproval: new RulePromotionApprovalDto(
                    "approval-rule-interest-v2",
                    "controller",
                    DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                    ManualJournalEntryStatusDto.Approved,
                    ApprovedBy: "cfo",
                    ApprovedAtUtc: DateTimeOffset.Parse("2026-06-15T11:00:00Z"),
                    EvidenceLinks: ["evidence://accounting/rules/rule-interest-versioned/v2/approval-rule-interest-v2/approval-v2"])),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-versioned/v2"]));

        var workspace = await service.GetWorkspaceAsync("fund-alpha");
        var rule = workspace.PostingRules.Should().ContainSingle(item => item.RuleId == "rule-interest-versioned").Subject;

        rule.RuleVersion.Should().Be("v2");
        rule.Versions.Should().HaveCount(2);
        rule.Versions.Should().Contain(version =>
            version.Version == "v1" &&
            version.CreatedBy == "controller" &&
            version.ChangeSummary.Contains("Created posting rule", StringComparison.OrdinalIgnoreCase) &&
            version.EvidenceLinks.Contains("evidence://accounting/rules/rule-interest-versioned/v1"));
        rule.Versions.Should().Contain(version =>
            version.Version == "v2" &&
            version.PromotionApproval != null &&
            version.PromotionApproval.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            version.EvidenceLinks.Contains("evidence://accounting/rules/rule-interest-versioned/v2"));
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_MaterialRuleEditRequiresFreshPromotionApproval()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var approvedRule = new PostingRuleDto(
            RuleId: "rule-interest-approved",
            DisplayName: "Approved interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "",
            RuleVersion: "v1",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            Priority: 100,
            Scope: new LedgerDimensionSetDto(FundId: "fund-alpha"),
            Formulas:
            [
                new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
            ],
            GeneratedPostings:
            [
                new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
            ],
            PromotionApproval: new RulePromotionApprovalDto(
                "approval-rule-interest-v1",
                "controller",
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                ManualJournalEntryStatusDto.Approved,
                ApprovedBy: "cfo",
                ApprovedAtUtc: DateTimeOffset.Parse("2026-06-15T11:00:00Z"),
                EvidenceLinks: ["evidence://accounting/rules/rule-interest-approved/v1/approval-rule-interest-v1/approval-v1"]),
            RequiresPromotionApproval: true);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: approvedRule,
            Actor: "controller"));

        var staleApprovalWorkspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: approvedRule with
            {
                RuleVersion = "v2",
                Priority = 200
            },
            Actor: "controller"));
        var staleRule = staleApprovalWorkspace.PostingRules.Should().ContainSingle(item => item.RuleId == "rule-interest-approved").Subject;

        staleRule.PromotionApproval.Should().BeNull();
        staleApprovalWorkspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.promotion-approval-required" &&
            issue.TargetId == "rule-interest-approved" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        var freshApprovalWorkspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: staleRule with
            {
                PromotionApproval = new RulePromotionApprovalDto(
                    "approval-rule-interest-v2",
                    "controller",
                    DateTimeOffset.Parse("2026-06-16T10:00:00Z"),
                    ManualJournalEntryStatusDto.Approved,
                    ApprovedBy: "cfo",
                    ApprovedAtUtc: DateTimeOffset.Parse("2026-06-16T11:00:00Z"),
                    EvidenceLinks: ["evidence://accounting/rules/rule-interest-approved/v2/approval-rule-interest-v2/approval-v2"])
            },
            Actor: "controller"));
        var freshRule = freshApprovalWorkspace.PostingRules.Should().ContainSingle(item => item.RuleId == "rule-interest-approved").Subject;

        freshRule.PromotionApproval.Should().NotBeNull();
        freshRule.PromotionApproval!.ApprovalId.Should().Be("approval-rule-interest-v2");
        freshApprovalWorkspace.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "posting-rule.promotion-approval-required" &&
            issue.TargetId == "rule-interest-approved");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_PromotionApprovalIsAuditedVersionedOperation()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-promotion-operation",
                DisplayName: "Promotion operation interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v7",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 500,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ],
                RequiresPromotionApproval: true),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/v7"]));

        var weakEvidence = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve v7 after regression review.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/draft"]));
        await weakEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires retained approval, certification, sign-off, or review evidence*");

        var staleVersion = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v6",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve stale version.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v6/approval-rule-interest-v7"]));
        await staleVersion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*currently at version 'v7'*");

        var wrongRuleEvidence = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve with unrelated approval evidence.",
            EvidenceLinks: ["evidence://accounting/rules/other-rule/review-v6"]));
        await wrongRuleEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must reference the retained rule, rule version, and approval id in the same artifact*");

        var splitApprovalEvidence = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve with split approval evidence.",
            EvidenceLinks:
            [
                "evidence://accounting/rules/rule-interest-promotion-operation/draft",
                "evidence://accounting/rules/other-rule/review-v6"
            ]));
        await splitApprovalEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must reference the retained rule, rule version, and approval id in the same artifact*");

        var missingApprovalIdEvidence = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve with rule and version evidence that omits the approval id.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7"]));
        await missingApprovalIdEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must reference the retained rule, rule version, and approval id in the same artifact*");

        var missingRegression = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve without regression coverage.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"]));
        await missingRegression.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires at least one saved regression test case*");

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-promotion-operation-regression",
                "Interest promotion operation regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 250m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")),
                ExpectedRuleId: "rule-interest-promotion-operation",
                ExpectedRuleVersion: "v7"),
            Actor: "controller",
            EvidenceLinks:
            [
                "evidence://accounting/rule-tests/unrelated-regression",
                "evidence://accounting/rules/rule-interest-promotion-operation/draft-v7"
            ]));

        var weakRegressionEvidence = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve with weak regression evidence.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"]));
        await weakRegressionEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires every current-version saved test case evidence*");

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-promotion-operation-regression",
                "Interest promotion operation regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 250m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")),
                ExpectedRuleId: "rule-interest-promotion-operation",
                ExpectedRuleVersion: "v7",
                ExpectedGeneratedPostingLines:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 249m, Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 250m, Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"))
                ]),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/interest-promotion-operation-regression/rule-interest-promotion-operation/v7"]));

        var failingRegression = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve with failing regression coverage.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"]));
        await failingRegression.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires all saved regression tests*to pass*");

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-promotion-operation-regression",
                "Interest promotion operation regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 250m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")),
                ExpectedRuleId: "rule-interest-promotion-operation",
                ExpectedRuleVersion: "v7",
                ExpectedGeneratedPostingLines:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 250m, Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 250m, Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"))
                ]),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/interest-promotion-operation-regression/rule-interest-promotion-operation/v7"]));

        var assistantApproval = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve v7 from assistant draft.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"],
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));
        await assistantApproval.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reviewed automation cannot approve posting rule promotions*human operator*");

        var approved = await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Approve v7 after regression review.",
            RequestedBy: "controller",
            RequestedAtUtc: DateTimeOffset.Parse("2026-06-18T10:00:00Z"),
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"]));
        var rule = approved.PostingRules.Should().ContainSingle(item => item.RuleId == "rule-interest-promotion-operation").Subject;

        rule.RequiresPromotionApproval.Should().BeTrue();
        rule.PromotionApproval.Should().NotBeNull();
        rule.PromotionApproval!.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Approved);
        rule.PromotionApproval.ApprovedBy.Should().Be("cfo");
        rule.PromotionApproval.RequestedBy.Should().Be("controller");
        rule.PromotionApproval.Notes.Should().Be("Approve v7 after regression review.");
        rule.PromotionApproval.EvidenceLinks.Should().Contain("evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7");
        rule.Versions.Should().Contain(version =>
            version.Version == "v7" &&
            version.PromotionApproval != null &&
            version.PromotionApproval.ApprovalId == "approval-rule-interest-v7" &&
            version.EvidenceLinks.Contains("evidence://accounting/rules/rule-interest-promotion-operation/v7") &&
            version.EvidenceLinks.Contains("evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"));
        approved.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "posting-rule.promotion-approval-required" &&
            issue.TargetId == "rule-interest-promotion-operation");

        var idempotentRetry = await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7",
            Notes: "Retry same approval after transport timeout.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"]));
        var retriedRule = idempotentRetry.PostingRules.Should().ContainSingle(item => item.RuleId == "rule-interest-promotion-operation").Subject;
        retriedRule.PromotionApproval.Should().NotBeNull();
        retriedRule.PromotionApproval!.ApprovalId.Should().Be("approval-rule-interest-v7");
        retriedRule.PromotionApproval.Notes.Should().Be("Approve v7 after regression review.");
        retriedRule.Versions.Should().ContainSingle(version =>
            version.Version == "v7" &&
            version.PromotionApproval != null &&
            version.PromotionApproval.ApprovalId == "approval-rule-interest-v7");

        var conflictingApproval = async () => await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-interest-promotion-operation",
            RuleVersion: "v7",
            Actor: "cfo",
            ApprovalId: "approval-rule-interest-v7-second",
            Notes: "Attempt to replace an approved promotion.",
            EvidenceLinks: ["evidence://accounting/rules/rule-interest-promotion-operation/review-v7-second/approval-rule-interest-v7-second"]));
        await conflictingApproval.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already approved by promotion 'approval-rule-interest-v7'*");

        var audit = await service.ListAuditAsync("fund-alpha");
        audit.Should().ContainSingle(item =>
            item.Action == "posting-rule.promotion-approve" &&
            item.Actor == "cfo" &&
            item.EvidenceLinks.Contains("evidence://accounting/rules/rule-interest-promotion-operation/review-v7/approval-rule-interest-v7"));
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_FileStoreRetainsRuleVersionsApprovalsAndSavedTestsAcrossRestart()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"meridian-accounting-rules-{Guid.NewGuid():N}.json");
        var service = CreateFileBackedService(snapshotPath);
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-file-durable",
                DisplayName: "Durable file-backed interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 625,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ],
                RequiresPromotionApproval: true),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rules/rule-file-durable/v1"]));
        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "file-durable-regression",
                "File durable regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 450m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")),
                ExpectedRuleId: "rule-file-durable",
                ExpectedRuleVersion: "v1"),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/file-durable-regression/rule-file-durable/v1"]));
        await service.ApprovePostingRulePromotionAsync(new ApprovePostingRulePromotionRequest(
            FundProfileId: "fund-alpha",
            RuleId: "rule-file-durable",
            RuleVersion: "v1",
            Actor: "cfo",
            ApprovalId: "approval-rule-file-durable-v1",
            Notes: "Approve durable file-backed rule after regression evidence.",
            EvidenceLinks: ["evidence://accounting/rules/rule-file-durable/review-v1/approval-rule-file-durable-v1"]));

        var reloaded = CreateFileBackedService(snapshotPath);
        var workspace = await reloaded.GetWorkspaceAsync("fund-alpha");
        var rule = workspace.PostingRules.Should().ContainSingle(item => item.RuleId == "rule-file-durable").Subject;
        var suite = await reloaded.ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
            FundProfileId: "fund-alpha",
            Actor: "controller"));
        var audit = await reloaded.ListAuditAsync("fund-alpha");

        rule.PromotionApproval.Should().NotBeNull();
        rule.PromotionApproval!.ApprovalId.Should().Be("approval-rule-file-durable-v1");
        rule.Versions.Should().ContainSingle(version =>
            version.Version == "v1" &&
            version.PromotionApproval != null &&
            version.PromotionApproval.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            version.EvidenceLinks.Contains("evidence://accounting/rules/rule-file-durable/v1") &&
            version.EvidenceLinks.Contains("evidence://accounting/rules/rule-file-durable/review-v1/approval-rule-file-durable-v1"));
        workspace.RuleTestCases.Should().ContainSingle(testCase =>
            testCase.TestCaseId == "file-durable-regression" &&
            testCase.EvidenceLinks.Contains("evidence://accounting/rule-tests/file-durable-regression/rule-file-durable/v1"));
        suite.TotalCount.Should().Be(1);
        suite.PassedCount.Should().Be(1);
        suite.Results.Should().ContainSingle(result =>
            result.TestCaseId == "file-durable-regression" &&
            result.Passed &&
            result.DryRunResult.SelectedRuleId == "rule-file-durable");
        audit.Should().Contain(item => item.Action == "posting-rule.promotion-approve" && item.Actor == "cfo");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunAppliesAllocationsToGeneratedPostingLines()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-allocated",
                DisplayName: "Allocated interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v3",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 250,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                Allocations:
                [
                    new AllocationRuleDto(
                        "allocation-lp-1",
                        AllocationRuleBasisDto.FixedPercent,
                        1m,
                        new LedgerDimensionSetDto(InvestorId: "investor-lp-1", CapitalAccountId: "capital-account-lp-1"),
                        Description: "LP 1 allocation"),
                    new AllocationRuleDto(
                        "allocation-lp-2",
                        AllocationRuleBasisDto.FixedPercent,
                        2m,
                        new LedgerDimensionSetDto(InvestorId: "investor-lp-2", CapitalAccountId: "capital-account-lp-2"),
                        Description: "LP 2 allocation")
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 100.01m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        result.SelectedRuleId.Should().Be("rule-interest-allocated");
        result.IsPostingBalanced.Should().BeTrue();
        result.GeneratedPostingLines.Should().HaveCount(4);
        result.GeneratedPostingLines.Should().Contain(line =>
            line.LineId == "debit-cash:allocation-lp-1" &&
            line.Amount == 33.34m &&
            line.Dimensions != null &&
            line.Dimensions.InvestorId == "investor-lp-1" &&
            line.Dimensions.CapitalAccountId == "capital-account-lp-1" &&
            line.Dimensions.FundId == "fund-alpha" &&
            line.Dimensions.EntityId == "entity-master");
        result.GeneratedPostingLines.Should().Contain(line =>
            line.LineId == "credit-income:allocation-lp-2" &&
            line.Amount == 66.67m &&
            line.Dimensions != null &&
            line.Dimensions.InvestorId == "investor-lp-2" &&
            line.Dimensions.CapitalAccountId == "capital-account-lp-2");
        result.GeneratedPostingLines
            .Where(line => line.Side == AccountingTemplateLineSideDto.Debit)
            .Sum(line => line.Amount)
            .Should().Be(100.01m);
        result.GeneratedPostingLines
            .Where(line => line.Side == AccountingTemplateLineSideDto.Credit)
            .Sum(line => line.Amount)
            .Should().Be(100.01m);
        result.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_GeneratedPostingsInheritRuleAndEventDimensions()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        var instrumentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-dimensional",
                DisplayName: "Dimensional interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v6",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 260,
                Scope: new LedgerDimensionSetDto(
                    FundId: "fund-alpha",
                    EntityId: "entity-master",
                    ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Department"] = "Treasury"
                    }),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "counterparty-bank",
                        "counterparty_id",
                        AccountingRuleConditionOperatorDto.Equals,
                        "counterparty-bank")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto(
                        "debit-cash",
                        "Assets:Cash",
                        AccountingTemplateLineSideDto.Debit,
                        "source-amount",
                        0m,
                        Dimensions: new LedgerDimensionSetDto(
                            CostCenterId: "cost-center-interest",
                            AccountId: "account-cash-operating",
                            VendorId: "vendor-bank",
                            ProjectId: "project-accrual",
                            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Book"] = "Accrual"
                            })),
                    new GeneratedPostingLineDto(
                        "credit-income",
                        "Income:Interest",
                        AccountingTemplateLineSideDto.Credit,
                        "source-amount",
                        0m,
                        Dimensions: new LedgerDimensionSetDto(
                            CostCenterId: "cost-center-interest",
                            AccountId: "account-interest-income",
                            VendorId: "vendor-bank",
                            ProjectId: "project-accrual",
                            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Book"] = "Accrual"
                            }))
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 125m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                SleeveId: "sleeve-income",
                InstrumentId: instrumentId,
                TaxLotId: "tax-lot-2026-06",
                OrganizationId: "organization-alpha",
                PortfolioId: "portfolio-credit",
                BookId: "book-gaap",
                CustomerId: "customer-borrower",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "Treasury",
                    ["Region"] = "US"
                }),
            CounterpartyId: "counterparty-bank"));

        result.SelectedRuleId.Should().Be("rule-interest-dimensional");
        result.IsPostingBalanced.Should().BeTrue();
        result.GeneratedPostingLines.Should().HaveCount(2);
        result.GeneratedPostingLines.Should().OnlyContain(line =>
            line.Dimensions != null &&
            line.Dimensions.FundId == "fund-alpha" &&
            line.Dimensions.EntityId == "entity-master" &&
            line.Dimensions.SleeveId == "sleeve-income" &&
            line.Dimensions.InstrumentId == instrumentId &&
            line.Dimensions.TaxLotId == "tax-lot-2026-06" &&
            line.Dimensions.CostCenterId == "cost-center-interest" &&
            line.Dimensions.CounterpartyId == "counterparty-bank" &&
            line.Dimensions.OrganizationId == "organization-alpha" &&
            line.Dimensions.PortfolioId == "portfolio-credit" &&
            line.Dimensions.BookId == "book-gaap" &&
            line.Dimensions.CustomerId == "customer-borrower" &&
            line.Dimensions.VendorId == "vendor-bank" &&
            line.Dimensions.ProjectId == "project-accrual" &&
            !string.IsNullOrWhiteSpace(line.Dimensions.AccountId) &&
            line.Dimensions.ExternalGlDimensions["Department"] == "Treasury" &&
            line.Dimensions.ExternalGlDimensions["Region"] == "US" &&
            line.Dimensions.ExternalGlDimensions["Book"] == "Accrual");
        result.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunAppliesFormulaBackedAllocationWeights()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-formula-allocated",
                DisplayName: "Formula allocated interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v5",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 275,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m),
                    new AccountingRuleFormulaDto("lp-1-weight", AccountingRuleFormulaKindDto.FixedAmount, 75m),
                    new AccountingRuleFormulaDto("lp-2-weight", AccountingRuleFormulaKindDto.FixedAmount, 25m)
                ],
                Allocations:
                [
                    new AllocationRuleDto(
                        "allocation-lp-1",
                        AllocationRuleBasisDto.CustomFormula,
                        0m,
                        new LedgerDimensionSetDto(InvestorId: "investor-lp-1", CapitalAccountId: "capital-account-lp-1"),
                        FormulaId: "lp-1-weight",
                        Description: "LP 1 formula allocation"),
                    new AllocationRuleDto(
                        "allocation-lp-2",
                        AllocationRuleBasisDto.CustomFormula,
                        0m,
                        new LedgerDimensionSetDto(InvestorId: "investor-lp-2", CapitalAccountId: "capital-account-lp-2"),
                        FormulaId: "lp-2-weight",
                        Description: "LP 2 formula allocation")
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 200m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        result.SelectedRuleId.Should().Be("rule-interest-formula-allocated");
        result.IsPostingBalanced.Should().BeTrue();
        result.GeneratedPostingLines.Should().HaveCount(4);
        result.GeneratedPostingLines.Should().Contain(line =>
            line.LineId == "debit-cash:allocation-lp-1" &&
            line.Amount == 150m &&
            line.Dimensions != null &&
            line.Dimensions.InvestorId == "investor-lp-1" &&
            line.Dimensions.CapitalAccountId == "capital-account-lp-1");
        result.GeneratedPostingLines.Should().Contain(line =>
            line.LineId == "credit-income:allocation-lp-2" &&
            line.Amount == 50m &&
            line.Dimensions != null &&
            line.Dimensions.InvestorId == "investor-lp-2" &&
            line.Dimensions.CapitalAccountId == "capital-account-lp-2");
        result.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsEventSpecificNonPositiveAllocationWeight()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-event-zero-allocation",
                DisplayName: "Event amount allocated interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v5",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 276,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                Allocations:
                [
                    new AllocationRuleDto(
                        "allocation-event-weight",
                        AllocationRuleBasisDto.CustomFormula,
                        0m,
                        new LedgerDimensionSetDto(InvestorId: "investor-lp-1", CapitalAccountId: "capital-account-lp-1"),
                        FormulaId: "source-amount",
                        Description: "Event amount allocation")
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 0m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));

        result.SelectedRuleId.Should().BeNull();
        result.GeneratedPostingLines.Should().BeEmpty();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-event-zero-allocation" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "rule.allocation-weight" &&
                issue.TargetId == "allocation-event-weight"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.allocation-weight" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "allocation-event-weight");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_ActivationRejectsNonPositiveFormulaBackedAllocationWeight()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: BalancedInterestAccrualTemplate(),
            Actor: "controller"));
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-zero-allocation",
                DisplayName: "Zero allocation formula",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v5",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 275,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m),
                    new AccountingRuleFormulaDto("zero-weight", AccountingRuleFormulaKindDto.FixedAmount, 0m)
                ],
                Allocations:
                [
                    new AllocationRuleDto(
                        "allocation-lp-1",
                        AllocationRuleBasisDto.CustomFormula,
                        0m,
                        new LedgerDimensionSetDto(InvestorId: "investor-lp-1", CapitalAccountId: "capital-account-lp-1"),
                        FormulaId: "zero-weight")
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var workspace = await service.GetWorkspaceAsync("fund-alpha");
        var activate = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));

        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.allocation-formula-nonpositive" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "allocation-lp-1");
        await activate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsMissingGeneratedFormulaReference()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-missing-formula",
                DisplayName: "Interest accrual with missing formula",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v4",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 300,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "missing-formula", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 100m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));
        var workspace = await service.GetWorkspaceAsync("fund-alpha");

        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-missing-formula" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "posting-rule.generated-formula-missing" &&
                issue.TargetId == "debit-cash"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.generated-formula-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "debit-cash");
        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.generated-formula-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "debit-cash");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsDuplicateGeneratedPostingLineIds()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-duplicate-generated-line",
                DisplayName: "Interest accrual with duplicate generated line ids",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v4",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 301,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("cash-line", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("cash-line", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 100m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));
        var workspace = await service.GetWorkspaceAsync("fund-alpha");

        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-duplicate-generated-line" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "posting-rule.generated-line-id-duplicate" &&
                issue.TargetId == "cash-line"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.generated-line-id-duplicate" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "cash-line");
        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.generated-line-id-duplicate" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "cash-line");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunFailsClosedWhenPredicatesRejectAllCandidates()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-high-threshold",
                DisplayName: "High threshold interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v4",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 304,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "minimum-interest-threshold",
                        "amount",
                        AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                        "500")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 100m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
            CounterpartyId: "counterparty-bank"));

        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.GeneratedPostingLines.Should().BeEmpty();
        result.RuleMatches.Should().ContainSingle(match =>
            match.RuleId == "rule-interest-high-threshold" &&
            !match.IsMatched &&
            match.Explanations.Contains("Required condition 'minimum-interest-threshold' did not match."));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.no-candidate-match" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "InterestAccrual");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsDuplicateAllocationIds()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-duplicate-allocation",
                DisplayName: "Interest accrual with duplicate allocation ids",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v4",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 302,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                Allocations:
                [
                    new AllocationRuleDto(
                        "allocation-lp",
                        AllocationRuleBasisDto.FixedPercent,
                        60m,
                        new LedgerDimensionSetDto(InvestorId: "investor-a")),
                    new AllocationRuleDto(
                        "allocation-lp",
                        AllocationRuleBasisDto.FixedPercent,
                        40m,
                        new LedgerDimensionSetDto(InvestorId: "investor-b"))
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 100m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));
        var workspace = await service.GetWorkspaceAsync("fund-alpha");

        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-duplicate-allocation" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "posting-rule.allocation-id-duplicate" &&
                issue.TargetId == "allocation-lp"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.allocation-id-duplicate" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "allocation-lp");
        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.allocation-id-duplicate" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "allocation-lp");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_DryRunRejectsMissingGeneratedAccountReference()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-missing-generated-account",
                DisplayName: "Interest accrual with missing generated account",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v4",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 303,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-missing-cash", "Assets:Missing Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 100m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master")));
        var workspace = await service.GetWorkspaceAsync("fund-alpha");

        result.SelectedRuleId.Should().BeNull();
        result.IsPostingBalanced.Should().BeFalse();
        result.RuleMatches.Should().ContainSingle(item =>
            item.RuleId == "rule-interest-missing-generated-account" &&
            !item.IsMatched &&
            item.ValidationIssues.Any(issue =>
                issue.Code == "rule.generated-account-missing" &&
                issue.TargetId == "debit-missing-cash"));
        result.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule.generated-account-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "debit-missing-cash");
        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.generated-account-missing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "debit-missing-cash");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_ValidationReportsDuplicateChartPathsWithoutThrowing()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto("cash-duplicate", "Assets:Cash", "Duplicate Cash", "Asset"),
            Actor: "controller"));

        var workspace = await service.GetWorkspaceAsync("fund-alpha");
        var activate = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));

        workspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "chart.path-duplicate" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "Assets:Cash");
        await activate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_ExecutesRegressionTestCasesAgainstDryRunEngine()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-generated",
                DisplayName: "Generated interest accrual",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v2",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 100,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "threshold",
                        "amount",
                        AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                        "100")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var result = await service.ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            TestCases:
            [
                new AccountingRuleTestCaseDto(
                    "interest-accrual-happy-path",
                    "Interest accrual happy path",
                    new RuleDryRunRequestDto(
                        FundProfileId: "fund-alpha",
                        SourceEventType: "InterestAccrual",
                        EventAmount: 250m,
                        Currency: "USD",
                        EffectiveDate: new DateOnly(2026, 6, 30),
                        Actor: "controller",
                        Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                        CounterpartyId: "counterparty-bank"),
                    ExpectedRuleId: "rule-interest-generated",
                    ExpectedRuleVersion: "v2"),
                new AccountingRuleTestCaseDto(
                    "interest-accrual-wrong-rule",
                    "Interest accrual wrong expected rule",
                    new RuleDryRunRequestDto(
                        FundProfileId: "fund-alpha",
                        SourceEventType: "InterestAccrual",
                        EventAmount: 250m,
                        Currency: "USD",
                        EffectiveDate: new DateOnly(2026, 6, 30),
                        Actor: "controller",
                        Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                        CounterpartyId: "counterparty-bank"),
                    ExpectedRuleId: "rule-other",
                    ExpectedRuleVersion: "v2"),
                new AccountingRuleTestCaseDto(
                    "interest-accrual-stale-version",
                    "Interest accrual stale expected version",
                    new RuleDryRunRequestDto(
                        FundProfileId: "fund-alpha",
                        SourceEventType: "InterestAccrual",
                        EventAmount: 250m,
                        Currency: "USD",
                        EffectiveDate: new DateOnly(2026, 6, 30),
                        Actor: "controller",
                        Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                        CounterpartyId: "counterparty-bank"),
                    ExpectedRuleId: "rule-interest-generated",
                    ExpectedRuleVersion: "v1")
            ]));

        result.TotalCount.Should().Be(3);
        result.PassedCount.Should().Be(1);
        result.FailedCount.Should().Be(2);
        result.Results.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-happy-path" &&
            item.Passed &&
            item.DryRunResult.SelectedRuleId == "rule-interest-generated");
        result.Results.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-wrong-rule" &&
            !item.Passed &&
            item.AssertionIssues.Any(issue => issue.Code == "rule-test.expected-rule-mismatch"));
        result.Results.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-stale-version" &&
            !item.Passed &&
            item.AssertionIssues.Any(issue => issue.Code == "rule-test.expected-version-mismatch"));

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-accrual-saved-happy-path",
                "Saved interest accrual happy path",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 300m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                    CounterpartyId: "counterparty-bank"),
                ExpectedRuleId: "rule-interest-generated",
                ExpectedRuleVersion: "v2"),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/interest-accrual-saved-happy-path/rule-interest-generated/v2"]));

        var persistedResult = await service.ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
            FundProfileId: "fund-alpha",
            Actor: "controller"));

        persistedResult.TotalCount.Should().Be(1);
        persistedResult.PassedCount.Should().Be(1);
        persistedResult.Results.Should().ContainSingle(item =>
            item.TestCaseId == "interest-accrual-saved-happy-path" &&
            item.Passed &&
            item.DryRunResult.SelectedRuleId == "rule-interest-generated");
        var workspace = await service.GetWorkspaceAsync("fund-alpha");
        workspace.RuleTestCases.Should().ContainSingle(item => item.TestCaseId == "interest-accrual-saved-happy-path");
        workspace.RuleTestCases.Single(item => item.TestCaseId == "interest-accrual-saved-happy-path").EvidenceLinks
            .Should().Contain("evidence://accounting/rule-tests/interest-accrual-saved-happy-path/rule-interest-generated/v2");
        workspace.AuditTrail.Should().Contain(item => item.Action == "rule-test-case.upsert");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_ActivationRequiresApprovedPromotionAndPassingSavedTests()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: BalancedInterestAccrualTemplate(),
            Actor: "controller"));

        var gatedRule = new PostingRuleDto(
            RuleId: "rule-interest-promotion-gated",
            DisplayName: "Promotion-gated interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            Priority: 200,
            Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
            RequiresPromotionApproval: true);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: gatedRule,
            Actor: "controller"));
        var missingApprovalWorkspace = await service.GetWorkspaceAsync("fund-alpha");
        missingApprovalWorkspace.RulesStudio.Should().NotBeNull();
        missingApprovalWorkspace.RulesStudio!.Summary.RulesBlockedByPromotionApproval.Should().Be(1);
        missingApprovalWorkspace.RulesStudio.Summary.RulesBlockedByRegressionTests.Should().Be(1);
        missingApprovalWorkspace.RulesStudio.Summary.RulesReadyForActivation.Should().Be(0);
        missingApprovalWorkspace.RulesStudio.Summary.RequiredActions.Should().Contain(action =>
            action.Contains("current-version saved regression test", StringComparison.OrdinalIgnoreCase));
        missingApprovalWorkspace.RulesStudio.Summary.RequiredActions.Should().Contain(action =>
            action.Contains("human review", StringComparison.OrdinalIgnoreCase));

        var missingApprovalAct = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        await missingApprovalAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: gatedRule with
            {
                PromotionApproval = new RulePromotionApprovalDto(
                    "approval-rule-interest-weak",
                    "controller",
                    DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                    ManualJournalEntryStatusDto.Approved,
                    ApprovedBy: "cfo",
                    ApprovedAtUtc: DateTimeOffset.Parse("2026-06-15T11:00:00Z"),
                    EvidenceLinks: ["evidence://accounting/rule-support/rule-interest"])
            },
            Actor: "controller"));
        var weakApprovalWorkspace = await service.GetWorkspaceAsync("fund-alpha");
        weakApprovalWorkspace.RulesStudio.Should().NotBeNull();
        weakApprovalWorkspace.RulesStudio!.Summary.RulesBlockedByCriticalIssues.Should().BeGreaterThan(0);
        weakApprovalWorkspace.RulesStudio.Summary.RequiredActions.Should().Contain(action =>
            action.Contains("critical validation issue", StringComparison.OrdinalIgnoreCase));
        weakApprovalWorkspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.promotion-approval-required" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "rule-interest-promotion-gated");

        var weakApprovalAct = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        await weakApprovalAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: gatedRule with
            {
                PromotionApproval = new RulePromotionApprovalDto(
                    "approval-rule-interest",
                    "controller",
                    DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                    ManualJournalEntryStatusDto.Approved,
                    ApprovedBy: "cfo",
                    ApprovedAtUtc: DateTimeOffset.Parse("2026-06-15T11:00:00Z"),
                    EvidenceLinks: ["evidence://accounting/rule-approval/rule-interest-promotion-gated/v1/approval-rule-interest"])
            },
            Actor: "controller"));

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-promotion-failing-regression",
                "Interest promotion unevidenced regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 150m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                    CounterpartyId: "counterparty-bank"),
                ExpectedRuleId: "rule-interest-promotion-gated",
                ExpectedRuleVersion: "v1"),
            Actor: "controller"));
        var unevidencedRegressionWorkspace = await service.GetWorkspaceAsync("fund-alpha");
        unevidencedRegressionWorkspace.RulesStudio.Should().NotBeNull();
        unevidencedRegressionWorkspace.RulesStudio!.Summary.CriticalIssueCount.Should().BeGreaterThan(0);
        unevidencedRegressionWorkspace.RulesStudio.Summary.RequiredActions.Should().Contain(action =>
            action.Contains("critical validation issue", StringComparison.OrdinalIgnoreCase));
        unevidencedRegressionWorkspace.ValidationIssues.Should().Contain(issue =>
            issue.Code == "rule-test-case.evidence-required" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "interest-promotion-failing-regression");

        var unevidencedRegressionAct = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        await unevidencedRegressionAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-promotion-failing-regression",
                "Interest promotion failing regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 150m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                    CounterpartyId: "counterparty-bank"),
                ExpectedRuleId: "rule-other",
                ExpectedRuleVersion: "v1"),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/interest-promotion-failing-regression/rule-other/v1"]));

        var failingRegressionAct = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        await failingRegressionAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-promotion-failing-regression",
                "Interest promotion passing regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 150m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                    CounterpartyId: "counterparty-bank"),
                ExpectedRuleId: "rule-interest-promotion-gated",
                ExpectedRuleVersion: "v1"),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/interest-promotion-failing-regression/rule-interest-promotion-gated/v1"]));

        var activated = await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation"]));
        var audit = await service.ListAuditAsync("fund-alpha");
        var activatedStudio = activated.RulesStudio;

        activated.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activatedStudio.Should().NotBeNull();
        activatedStudio!.Summary.RulesBlockedByPromotionApproval.Should().Be(0);
        activatedStudio.Summary.RulesBlockedByRegressionTests.Should().Be(0);
        activatedStudio.Summary.RulesBlockedByCriticalIssues.Should().Be(0);
        activatedStudio.Summary.RulesReadyForActivation.Should().Be(activatedStudio.Summary.ActiveRules);
        activatedStudio.Summary.RequiredActions.Should().Contain("Rules Studio is ready for activation review.");
        activated.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        audit.Should().Contain(item => item.Action == "configuration.activate");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_ActivationRequiresSavedTestForCurrentRuleVersion()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: BalancedInterestAccrualTemplate(),
            Actor: "controller"));

        var gatedRule = new PostingRuleDto(
            RuleId: "rule-interest-versioned",
            DisplayName: "Versioned promotion-gated interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            RuleVersion: "v1",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            Priority: 210,
            Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
            PromotionApproval: new RulePromotionApprovalDto(
                "approval-rule-interest-v1",
                "controller",
                DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
                ManualJournalEntryStatusDto.Approved,
                ApprovedBy: "cfo",
                ApprovedAtUtc: DateTimeOffset.Parse("2026-06-15T11:00:00Z"),
                EvidenceLinks: ["evidence://accounting/rule-approval/rule-interest-versioned/v1/approval-rule-interest-v1"]),
            RequiresPromotionApproval: true);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: gatedRule,
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-version/rule-interest-v1"]));

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-versioned-regression",
                "Versioned interest accrual regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 150m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                    CounterpartyId: "counterparty-bank"),
                ExpectedRuleId: "rule-interest-versioned",
                ExpectedRuleVersion: "v1"),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/interest-versioned-regression/rule-interest-versioned/v1"]));

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: gatedRule with
            {
                RuleVersion = "v2",
                PromotionApproval = new RulePromotionApprovalDto(
                    "approval-rule-interest-v2",
                    "controller",
                    DateTimeOffset.Parse("2026-06-16T10:00:00Z"),
                    ManualJournalEntryStatusDto.Approved,
                    ApprovedBy: "cfo",
                    ApprovedAtUtc: DateTimeOffset.Parse("2026-06-16T11:00:00Z"),
                    EvidenceLinks: ["evidence://accounting/rule-approval/rule-interest-versioned/v2/approval-rule-interest-v2"])
            },
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-version/rule-interest-v2"]));

        var staleVersionSuite = await service.ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
            FundProfileId: "fund-alpha",
            Actor: "controller"));
        staleVersionSuite.Results.Should().ContainSingle(result =>
            result.TestCaseId == "interest-versioned-regression" &&
            !result.Passed &&
            result.AssertionIssues.Any(issue => issue.Code == "rule-test.expected-version-mismatch"));

        var staleVersionAct = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        await staleVersionAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");

        await service.UpsertRuleTestCaseAsync(new UpsertAccountingRuleTestCaseRequest(
            FundProfileId: "fund-alpha",
            TestCase: new AccountingRuleTestCaseDto(
                "interest-versioned-regression",
                "Versioned interest accrual regression",
                new RuleDryRunRequestDto(
                    FundProfileId: "fund-alpha",
                    SourceEventType: "InterestAccrual",
                    EventAmount: 150m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    Actor: "controller",
                    Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha"),
                    CounterpartyId: "counterparty-bank"),
                ExpectedRuleId: "rule-interest-versioned",
                ExpectedRuleVersion: "v2"),
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/rule-tests/interest-versioned-regression/rule-interest-versioned/v2"]));

        var activated = await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        activated.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activated.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_TestCasesAssertGeneratedPostingLines()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                RuleId: "rule-interest-generated-regression",
                DisplayName: "Generated interest regression",
                SourceEventType: "InterestAccrual",
                TemplateId: "",
                RuleVersion: "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 300,
                Scope: new LedgerDimensionSetDto(
                    FundId: "fund-alpha",
                    EntityId: "entity-master",
                    CounterpartyId: "counterparty-bank",
                    ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Department"] = "Fund Ops"
                    }),
                Formulas:
                [
                    new AccountingRuleFormulaDto("source-amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 0m),
                    new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 0m)
                ]),
            Actor: "controller"));

        var expectedDimensions = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-master",
            CounterpartyId: "counterparty-bank",
            OrganizationId: "organization-alpha",
            PortfolioId: "portfolio-credit",
            BookId: "book-gaap",
            AccountId: "account-interest",
            CustomerId: "customer-borrower",
            VendorId: "vendor-bank",
            ProjectId: "project-accrual",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Fund Ops"
            });
        var passingSuite = await service.ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            TestCases:
            [
                new AccountingRuleTestCaseDto(
                    "interest-generated-lines-pass",
                    "Generated interest lines pass",
                    new RuleDryRunRequestDto(
                        FundProfileId: "fund-alpha",
                        SourceEventType: "InterestAccrual",
                        EventAmount: 150m,
                        Currency: "USD",
                        EffectiveDate: new DateOnly(2026, 6, 30),
                        Actor: "controller",
                        Dimensions: expectedDimensions,
                        CounterpartyId: "counterparty-bank"),
                    ExpectedRuleId: "rule-interest-generated-regression",
                    ExpectedRuleVersion: "v1",
                    ExpectedGeneratedPostingLines:
                    [
                        new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 150m, Dimensions: expectedDimensions),
                        new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 150m, Dimensions: expectedDimensions)
                    ])
            ]));

        passingSuite.PassedCount.Should().Be(1);
        passingSuite.Results.Should().ContainSingle(result => result.Passed);

        var failingSuite = await service.ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            TestCases:
            [
                new AccountingRuleTestCaseDto(
                    "interest-generated-lines-fail",
                    "Generated interest lines fail",
                    new RuleDryRunRequestDto(
                        FundProfileId: "fund-alpha",
                        SourceEventType: "InterestAccrual",
                        EventAmount: 150m,
                        Currency: "USD",
                        EffectiveDate: new DateOnly(2026, 6, 30),
                        Actor: "controller",
                        Dimensions: expectedDimensions,
                        CounterpartyId: "counterparty-bank"),
                    ExpectedRuleId: "rule-interest-generated-regression",
                    ExpectedRuleVersion: "v1",
                    ExpectedGeneratedPostingLines:
                    [
                        new GeneratedPostingLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, "source-amount", 149m, Dimensions: expectedDimensions),
                        new GeneratedPostingLineDto("credit-income", "Income:Interest", AccountingTemplateLineSideDto.Credit, "source-amount", 150m, Dimensions: expectedDimensions)
                    ])
            ]));

        failingSuite.FailedCount.Should().Be(1);
        failingSuite.Results.Should().ContainSingle(result =>
            !result.Passed &&
            result.AssertionIssues.Any(issue =>
                issue.Code == "rule-test.generated-line-mismatch" &&
                issue.TargetId == "debit-cash" &&
                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical));
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_ActivationBlocksOverlappingSamePriorityRules()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: BalancedInterestAccrualTemplate(),
            Actor: "controller"));
        var firstRule = new PostingRuleDto(
            RuleId: "rule-interest-bank-master",
            DisplayName: "Bank master interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            EffectiveTo: new DateOnly(2026, 12, 31),
            Priority: 500,
            Scope: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                CounterpartyId: "counterparty-bank",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "Fund Ops"
                }));
        var overlappingRule = new PostingRuleDto(
            RuleId: "rule-interest-bank-overlap",
            DisplayName: "Overlapping bank interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            EffectiveFrom: new DateOnly(2026, 6, 1),
            EffectiveTo: new DateOnly(2026, 12, 31),
            Priority: 500,
            Scope: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                CounterpartyId: "counterparty-bank",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "Fund Ops"
                }));

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: firstRule,
            Actor: "controller"));
        var conflicted = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: overlappingRule,
            Actor: "controller"));

        conflicted.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.priority-conflict" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "rule-interest-bank-master");
        var dryRunConflict = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                CounterpartyId: "counterparty-bank",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "Fund Ops"
                }),
            CounterpartyId: "counterparty-bank"));
        dryRunConflict.SelectedRuleId.Should().BeNull();
        dryRunConflict.IsPostingBalanced.Should().BeFalse();
        dryRunConflict.GeneratedPostingLines.Should().BeEmpty();
        dryRunConflict.RuleMatches.Count(item => item.IsMatched).Should().Be(2);
        dryRunConflict.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.priority-conflict" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == "rule-interest-bank-master" &&
            issue.Message.Contains("Dry run matched 2 posting rules at priority 500"));
        var activateConflict = async () => await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));
        await activateConflict.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*critical validation issues*");

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: overlappingRule with { Priority = 501 },
            Actor: "controller"));
        var activated = await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));

        activated.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activated.ValidationIssues.Should().NotContain(issue => issue.Code == "posting-rule.priority-conflict");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_ActivationAllowsSamePriorityRulesWithDisjointAmountPredicates()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: BalancedInterestAccrualTemplate(),
            Actor: "controller"));
        var lowValueRule = new PostingRuleDto(
            RuleId: "rule-interest-low-value",
            DisplayName: "Low-value interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            EffectiveTo: new DateOnly(2026, 12, 31),
            Priority: 500,
            Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
            Conditions:
            [
                new AccountingRuleConditionDto(
                    "low-value-threshold",
                    "amount",
                    AccountingRuleConditionOperatorDto.AmountLessThanOrEqual,
                    "100")
            ]);
        var highValueRule = new PostingRuleDto(
            RuleId: "rule-interest-high-value",
            DisplayName: "High-value interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            EffectiveTo: new DateOnly(2026, 12, 31),
            Priority: 500,
            Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
            Conditions:
            [
                new AccountingRuleConditionDto(
                    "high-value-threshold",
                    "amount",
                    AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                    "101")
            ]);

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: lowValueRule,
            Actor: "controller"));
        var workspace = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: highValueRule,
            Actor: "controller"));

        workspace.ValidationIssues.Should().NotContain(issue => issue.Code == "posting-rule.priority-conflict");
        var lowValueDryRun = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 75m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
            CounterpartyId: "counterparty-bank"));
        var highValueDryRun = await service.DryRunPostingRuleAsync(new RuleDryRunRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "InterestAccrual",
            EventAmount: 250m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller",
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", CounterpartyId: "counterparty-bank"),
            CounterpartyId: "counterparty-bank"));

        lowValueDryRun.SelectedRuleId.Should().Be("rule-interest-low-value");
        lowValueDryRun.RuleMatches.Count(item => item.IsMatched).Should().Be(1);
        lowValueDryRun.ValidationIssues.Should().NotContain(issue => issue.Code == "posting-rule.priority-conflict");
        highValueDryRun.SelectedRuleId.Should().Be("rule-interest-high-value");
        highValueDryRun.RuleMatches.Count(item => item.IsMatched).Should().Be(1);
        highValueDryRun.ValidationIssues.Should().NotContain(issue => issue.Code == "posting-rule.priority-conflict");

        var activated = await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));

        activated.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activated.ValidationIssues.Should().NotContain(issue => issue.Code == "posting-rule.priority-conflict");
    }

    [Fact]
    public async Task Scenario_AccountingRulesStudio_PositionScopesDistinguishSamePriorityRules()
    {
        var service = CreateService();
        await SeedBalancedConfigurationAsync(service);
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: BalancedInterestAccrualTemplate(),
            Actor: "controller"));
        var firstPositionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondPositionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var firstRule = new PostingRuleDto(
            RuleId: "rule-interest-position-one",
            DisplayName: "Position one interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            EffectiveTo: new DateOnly(2026, 12, 31),
            Priority: 500,
            Scope: new LedgerDimensionSetDto(FundId: "fund-alpha")
            {
                PositionId = firstPositionId
            });
        var secondRule = new PostingRuleDto(
            RuleId: "rule-interest-position-two",
            DisplayName: "Position two interest accrual",
            SourceEventType: "InterestAccrual",
            TemplateId: "template-interest-accrual",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            EffectiveTo: new DateOnly(2026, 12, 31),
            Priority: 500,
            Scope: new LedgerDimensionSetDto(FundId: "fund-alpha")
            {
                PositionId = secondPositionId
            });

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: firstRule,
            Actor: "controller"));
        var disjoint = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: secondRule,
            Actor: "controller"));

        disjoint.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "posting-rule.priority-conflict");

        var overlapping = await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: secondRule with
            {
                Scope = secondRule.Scope! with { PositionId = firstPositionId }
            },
            Actor: "controller"));

        overlapping.ValidationIssues.Should().Contain(issue =>
            issue.Code == "posting-rule.priority-conflict" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == firstRule.RuleId);

        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: secondRule,
            Actor: "controller"));
        var activated = await service.ActivateAsync(new ActivateAccountingConfigurationRequest(
            FundProfileId: "fund-alpha",
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));

        activated.Status.Should().Be(AccountingConfigurationStatusDto.Active);
        activated.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "posting-rule.priority-conflict");
    }

    [Fact]
    public async Task Scenario_ManualJournalEntryLifecycle_ApprovesPostsAndCreatesImmutableReversalDraft()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var journalStore = WritableManualJournalLedgerJournalStore.Default();
        var service = CreateManualJournalEntryWorkbenchService(configuration, journalStore: journalStore);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            LedgerBookId: ManualJournalLedgerBookId));
        var missingApprovalNotes = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            LedgerBookId: submitted.LedgerBookId));
        await missingApprovalNotes.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal approval and rejection actions require reviewer notes.");

        var missingApprovalEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approved by fund controller",
            LedgerBookId: submitted.LedgerBookId));
        await missingApprovalEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal approval and rejection actions require retained reviewer evidence.");

        var weakApprovalEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approved by fund controller",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/support-packet"],
            LedgerBookId: submitted.LedgerBookId));
        await weakApprovalEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal approval and rejection actions require retained approval, rejection, sign-off, or review evidence.");

        var splitApprovalEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approved by fund controller",
            EvidenceLinks:
            [
                $"/api/workstation/evidence/subjects/accounting-record/support-packet/{submitted.PeriodId}",
                "/api/workstation/evidence/subjects/accounting-record/approval/generic-review"
            ],
            LedgerBookId: submitted.LedgerBookId));
        await splitApprovalEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal approval and rejection evidence must reference reviewer intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var wrongBookApprovalEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approved by fund controller",
            EvidenceLinks:
            [
                $"/api/workstation/evidence/subjects/accounting-record/approval/ledger-book/{Guid.NewGuid():D}/{submitted.PeriodId}"
            ],
            LedgerBookId: submitted.LedgerBookId));
        await wrongBookApprovalEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal approval and rejection evidence must reference reviewer intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var approvalEvidence = ManualJournalApprovalEvidence(submitted);
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approved by fund controller",
            EvidenceLinks: [approvalEvidence],
            LedgerBookId: submitted.LedgerBookId));
        var missingPostNotes = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            LedgerBookId: approved.JournalEntry.LedgerBookId));
        await missingPostNotes.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal posting and close-lock actions require operator notes.");

        var missingPostEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Posted after approval",
            LedgerBookId: approved.JournalEntry.LedgerBookId));
        await missingPostEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal posting actions require retained posting evidence.");

        var weakPostEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Posted after approval",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/support-packet"],
            LedgerBookId: approved.JournalEntry.LedgerBookId));
        await weakPostEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal posting actions require retained posting, approval, certification, sign-off, or review evidence.");

        var splitPostEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Posted after approval",
            EvidenceLinks:
            [
                $"/api/workstation/evidence/subjects/accounting-record/support-packet/{approved.JournalEntry.PeriodId}",
                "/api/workstation/evidence/subjects/accounting-record/posting/generic-review"
            ],
            LedgerBookId: approved.JournalEntry.LedgerBookId));
        await splitPostEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal posting evidence must reference posting intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var postingEvidence = ManualJournalPostingEvidence(approved.JournalEntry);
        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Posted after approval",
            EvidenceLinks: [postingEvidence],
            LedgerBookId: approved.JournalEntry.LedgerBookId));
        var missingCloseLockNotes = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.LockAfterClose,
            "controller",
            posted.JournalEntry.Version,
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await missingCloseLockNotes.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal posting and close-lock actions require operator notes.");

        var missingCloseLockEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.LockAfterClose,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Lock after close package review",
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await missingCloseLockEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal close-lock actions require retained close evidence.");

        var weakCloseLockEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.LockAfterClose,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Lock after close package review",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/support-packet"],
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await weakCloseLockEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal close-lock actions require retained close, period-lock, sign-off, certification, approval, or review evidence.");

        var wrongPeriodCloseLockEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.LockAfterClose,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Lock after close package review",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-close/close-package/fund-alpha-2026-07"],
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await wrongPeriodCloseLockEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal close-lock evidence must reference close-lock intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var splitCloseLockEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.LockAfterClose,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Lock after close package review",
            EvidenceLinks:
            [
                $"/api/workstation/evidence/subjects/accounting-record/support-packet/{posted.JournalEntry.PeriodId}",
                "/api/workstation/evidence/subjects/accounting-close/close-package/generic-review"
            ],
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await splitCloseLockEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal close-lock evidence must reference close-lock intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var missingReason = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "controller",
            posted.JournalEntry.Version,
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/reversal"],
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await missingReason.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal reversal and rebook actions require a correction reason.");

        var missingEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Rebook,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Rebook after close review",
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await missingEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal reversal and rebook actions require retained correction evidence.");

        var genericCorrectionEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Reverse after close review",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/support-packet"],
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await genericCorrectionEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal reversal and rebook actions require retained reversal, rebook, correction, approval, or review evidence.");

        var wrongPeriodCorrectionEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Reverse after close review",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/reversal/2026-07"],
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await wrongPeriodCorrectionEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal reversal and rebook evidence must reference correction intent, the posted journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var splitCorrectionEvidence = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Reverse after close review",
            EvidenceLinks:
            [
                $"/api/workstation/evidence/subjects/accounting-record/support-packet/{posted.JournalEntry.PeriodId}",
                "/api/workstation/evidence/subjects/accounting-record/reversal/generic-review"
            ],
            LedgerBookId: posted.JournalEntry.LedgerBookId));
        await splitCorrectionEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal reversal and rebook evidence must reference correction intent, the posted journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");

        var reversalEvidence = $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{posted.JournalEntry.LedgerBookId:D}/{posted.JournalEntry.PeriodId}";
        var reversal = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Reverse after close review",
            EvidenceLinks: [reversalEvidence],
            LedgerBookId: posted.JournalEntry.LedgerBookId));

        approved.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        approved.Transition.EvidenceLinks.Should().Contain(approvalEvidence);
        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        posted.Transition.EvidenceLinks.Should().Contain(postingEvidence);
        reversal.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Reversed);
        reversal.Transition.FromStatus.Should().Be(ManualJournalEntryStatusDto.Posted);
        reversal.Transition.ToStatus.Should().Be(ManualJournalEntryStatusDto.Reversed);
        reversal.Transition.EvidenceLinks.Should().Contain(reversalEvidence);
        reversal.JournalEntry.Reversal.Should().NotBeNull();
        reversal.JournalEntry.Reversal!.OriginalJournalEntryId.Should().Be(posted.JournalEntry.JournalEntryId);
        reversal.JournalEntry.Reversal.ReversalJournalEntryId.Should().Be(reversal.GeneratedJournalEntries.Single().JournalEntryId);
        reversal.JournalEntry.Reversal.Reason.Should().Be("Reverse after close review");
        reversal.JournalEntry.Reversal.CreatedBy.Should().Be("controller");
        reversal.GeneratedJournalEntries.Should().ContainSingle();
        var reversalDraft = reversal.GeneratedJournalEntries.Single();
        reversalDraft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        reversalDraft.EntryType.Should().Be(ManualJournalEntryTypeDto.Reversal);
        reversalDraft.ReversalOfJournalEntryId.Should().Be(posted.JournalEntry.JournalEntryId);
        reversalDraft.Reversal.Should().BeEquivalentTo(reversal.JournalEntry.Reversal);
        reversalDraft.LifecycleTransitions.Should().ContainSingle(item =>
            item.Action == JournalEntryLifecycleActionDto.Reverse &&
            item.FromStatus == ManualJournalEntryStatusDto.Posted &&
            item.ToStatus == ManualJournalEntryStatusDto.Draft &&
            item.EvidenceLinks.Contains(reversalEvidence));
        reversalDraft.EvidenceAttachments.Should().ContainSingle(item => item.AttachmentId == "source-doc-1");
        reversalDraft.EvidenceLinks.Should().Contain(reversalEvidence);
        reversalDraft.Lines.Should().Contain(line => line.LineId == "reversal-debit-cash" && line.Side == AccountingTemplateLineSideDto.Credit);
        reversalDraft.Lines.Should().Contain(line => line.LineId == "reversal-credit-income" && line.Side == AccountingTemplateLineSideDto.Debit);
        var duplicateCorrection = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            reversal.JournalEntry.JournalEntryId,
            reversal.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Rebook,
            "controller",
            reversal.JournalEntry.Version,
            Notes: "Duplicate correction attempt",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/rebook/{reversal.JournalEntry.PeriodId}"],
            LedgerBookId: reversal.JournalEntry.LedgerBookId));
        await duplicateCorrection.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal entry lifecycle action 'Rebook' requires a posted journal entry.");

        var rebookSaved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var rebookSubmitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            rebookSaved.JournalEntryId,
            rebookSaved.FundProfileId,
            "controller",
            rebookSaved.Version,
            LedgerBookId: rebookSaved.LedgerBookId));
        var rebookApproved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            rebookSubmitted.JournalEntryId,
            rebookSubmitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            rebookSubmitted.Version,
            Notes: "Controller approved the rebook source entry.",
            EvidenceLinks: [ManualJournalApprovalEvidence(rebookSubmitted)],
            LedgerBookId: rebookSubmitted.LedgerBookId));
        var rebookPosted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            rebookApproved.JournalEntry.JournalEntryId,
            rebookApproved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            rebookApproved.JournalEntry.Version,
            Notes: "Posted before rebook",
            EvidenceLinks: [ManualJournalPostingEvidence(rebookApproved.JournalEntry)],
            LedgerBookId: rebookApproved.JournalEntry.LedgerBookId));
        var invalidRebook = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            rebookPosted.JournalEntry.JournalEntryId,
            rebookPosted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Rebook,
            "controller",
            rebookPosted.JournalEntry.Version,
            Notes: "Invalid rebook with unbalanced correction lines",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/rebook-review/ledger-book/{rebookPosted.JournalEntry.LedgerBookId:D}/{rebookPosted.JournalEntry.PeriodId}"],
            RebookLines:
            [
                new ManualJournalEntryLineDto("rebook-debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash"),
                new ManualJournalEntryLineDto("rebook-credit-interest", AccountingTemplateLineSideDto.Credit, 90m, "USD", "Income:Interest")
            ],
            LedgerBookId: rebookPosted.JournalEntry.LedgerBookId));
        await invalidRebook.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Manual journal reversal and rebook actions cannot transition the posted entry while the generated correction draft has critical validation issues.");
        var afterInvalidRebook = await service.GetWorkbenchAsync("fund-alpha");
        afterInvalidRebook.Drafts.Should().ContainSingle(item =>
            item.JournalEntryId == rebookPosted.JournalEntry.JournalEntryId &&
            item.Status == ManualJournalEntryStatusDto.Posted &&
            item.Version == rebookPosted.JournalEntry.Version);
        afterInvalidRebook.Drafts.Should().NotContain(item =>
            item.RebookedFromJournalEntryId == rebookPosted.JournalEntry.JournalEntryId);

        var rebookEvidence = $"/api/workstation/evidence/subjects/accounting-record/rebook/ledger-book/{rebookPosted.JournalEntry.LedgerBookId:D}/{rebookPosted.JournalEntry.PeriodId}";
        var rebook = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            rebookPosted.JournalEntry.JournalEntryId,
            rebookPosted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Rebook,
            "controller",
            rebookPosted.JournalEntry.Version,
            Notes: "Rebook to alternate income account",
            EvidenceLinks: [rebookEvidence],
            RebookLines:
            [
                new ManualJournalEntryLineDto("rebook-debit-cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Assets:Cash"),
                new ManualJournalEntryLineDto("rebook-credit-interest", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Income:Interest")
            ],
            LedgerBookId: rebookPosted.JournalEntry.LedgerBookId));
        var rebookDraft = rebook.GeneratedJournalEntries.Should().ContainSingle().Subject;
        rebook.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Rebooked);
        rebook.Transition.FromStatus.Should().Be(ManualJournalEntryStatusDto.Posted);
        rebook.Transition.ToStatus.Should().Be(ManualJournalEntryStatusDto.Rebooked);
        rebook.JournalEntry.Rebook.Should().NotBeNull();
        rebook.JournalEntry.Rebook!.OriginalJournalEntryId.Should().Be(rebookPosted.JournalEntry.JournalEntryId);
        rebook.JournalEntry.Rebook.RebookJournalEntryId.Should().Be(rebookDraft.JournalEntryId);
        rebook.JournalEntry.Rebook.Reason.Should().Be("Rebook to alternate income account");
        rebook.JournalEntry.Rebook.CreatedBy.Should().Be("controller");
        rebookDraft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        rebookDraft.RebookedFromJournalEntryId.Should().Be(rebookPosted.JournalEntry.JournalEntryId);
        rebookDraft.Rebook.Should().BeEquivalentTo(rebook.JournalEntry.Rebook);
        rebookDraft.LifecycleTransitions.Should().ContainSingle(item =>
            item.Action == JournalEntryLifecycleActionDto.Rebook &&
            item.FromStatus == ManualJournalEntryStatusDto.Posted &&
            item.ToStatus == ManualJournalEntryStatusDto.Draft &&
            item.EvidenceLinks.Contains(rebookEvidence));
        rebookDraft.EvidenceAttachments.Should().ContainSingle(item => item.AttachmentId == "source-doc-1");
        rebookDraft.EvidenceLinks.Should().Contain(rebookEvidence);
        var lockSaved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(BalancedManualJournalEntry(), "ops-user"));
        var lockSubmitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            lockSaved.JournalEntryId,
            lockSaved.FundProfileId,
            "controller",
            lockSaved.Version,
            LedgerBookId: lockSaved.LedgerBookId));
        var lockApproved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            lockSubmitted.JournalEntryId,
            lockSubmitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            lockSubmitted.Version,
            Notes: "Controller approved the close-lock source entry.",
            EvidenceLinks: [ManualJournalApprovalEvidence(lockSubmitted)],
            LedgerBookId: lockSubmitted.LedgerBookId));
        var lockPosted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            lockApproved.JournalEntry.JournalEntryId,
            lockApproved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            lockApproved.JournalEntry.Version,
            Notes: "Posted before close lock.",
            EvidenceLinks: [ManualJournalPostingEvidence(lockApproved.JournalEntry)],
            LedgerBookId: lockApproved.JournalEntry.LedgerBookId));
        var closeLockEvidence =
            $"/api/workstation/evidence/subjects/accounting-close/close-package/ledger-book/{lockPosted.JournalEntry.LedgerBookId:D}/{lockPosted.JournalEntry.PeriodId}";
        journalStore.SetPeriodStatus("HardClosed");
        var closeLocked = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            lockPosted.JournalEntry.JournalEntryId,
            lockPosted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.LockAfterClose,
            "controller",
            lockPosted.JournalEntry.Version,
            Notes: "Lock after close package approval.",
            EvidenceLinks: [closeLockEvidence],
            LedgerBookId: lockPosted.JournalEntry.LedgerBookId));
        closeLocked.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.CloseLocked);
        closeLocked.Transition.FromStatus.Should().Be(ManualJournalEntryStatusDto.Posted);
        closeLocked.Transition.ToStatus.Should().Be(ManualJournalEntryStatusDto.CloseLocked);
        closeLocked.JournalEntry.EvidenceLinks.Should().Contain(closeLockEvidence);
        var reverseCloseLocked = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            closeLocked.JournalEntry.JournalEntryId,
            closeLocked.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "controller",
            closeLocked.JournalEntry.Version,
            Notes: "Attempt to reverse after close lock.",
            EvidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/reversal"],
            LedgerBookId: closeLocked.JournalEntry.LedgerBookId));
        await reverseCloseLocked.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*accounting period is locked after close*");
        var afterCloseLockedReverseAttempt = await service.GetWorkbenchAsync("fund-alpha");
        afterCloseLockedReverseAttempt.Drafts.Should().ContainSingle(item =>
            item.JournalEntryId == lockPosted.JournalEntry.JournalEntryId &&
            item.Status == ManualJournalEntryStatusDto.CloseLocked &&
            item.Version == closeLocked.JournalEntry.Version);
        afterCloseLockedReverseAttempt.Drafts.Should().NotContain(item =>
            item.ReversalOfJournalEntryId == closeLocked.JournalEntry.JournalEntryId);
        var workbench = await service.GetWorkbenchAsync("fund-alpha");
        workbench.Drafts.Should().Contain(item => item.JournalEntryId == posted.JournalEntry.JournalEntryId && item.Status == ManualJournalEntryStatusDto.Reversed);
        workbench.Drafts.Should().Contain(item => item.JournalEntryId == rebookPosted.JournalEntry.JournalEntryId && item.Status == ManualJournalEntryStatusDto.Rebooked);
        workbench.Drafts.Should().Contain(item => item.JournalEntryId == lockPosted.JournalEntry.JournalEntryId && item.Status == ManualJournalEntryStatusDto.CloseLocked);
        workbench.Drafts.Should().Contain(item => item.ReversalOfJournalEntryId == posted.JournalEntry.JournalEntryId);
        workbench.Drafts.Should().Contain(item => item.RebookedFromJournalEntryId == rebookPosted.JournalEntry.JournalEntryId);
        workbench.AuditTrail.Select(item => item.Action).Should().Contain(new[] { "manual-je.approve", "manual-je.post", "manual-je.lock-after-close", "manual-je.reverse", "manual-je.reverse-draft", "manual-je.rebook", "manual-je.rebook-draft" });
    }

    [Fact]
    public async Task ManualJournalEntryCorrection_AtomicBatchFailure_LeavesPostedSourceUnchanged()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var draftStore = new FailOnceManualJournalEntryDraftStore();
        var service = CreateManualJournalEntryWorkbenchService(
            configuration,
            draftStore: draftStore);
        var saved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
            BalancedManualJournalEntry(),
            "ops-user"));
        var submitted = await service.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId,
            saved.FundProfileId,
            "controller",
            saved.Version,
            LedgerBookId: saved.LedgerBookId));
        var approved = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            submitted.JournalEntryId,
            submitted.FundProfileId,
            JournalEntryLifecycleActionDto.Approve,
            "controller",
            submitted.Version,
            Notes: "Approve source before atomic correction test.",
            EvidenceLinks: [ManualJournalApprovalEvidence(submitted)],
            LedgerBookId: submitted.LedgerBookId));
        var posted = await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            approved.JournalEntry.JournalEntryId,
            approved.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Post,
            "controller",
            approved.JournalEntry.Version,
            Notes: "Post source before atomic correction test.",
            EvidenceLinks: [ManualJournalPostingEvidence(approved.JournalEntry)],
            LedgerBookId: approved.JournalEntry.LedgerBookId));
        draftStore.FailNextBatch = true;
        var reversalEvidence =
            $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{posted.JournalEntry.LedgerBookId:D}/{posted.JournalEntry.PeriodId}";

        var act = async () => await service.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
            posted.JournalEntry.JournalEntryId,
            posted.JournalEntry.FundProfileId,
            JournalEntryLifecycleActionDto.Reverse,
            "controller",
            posted.JournalEntry.Version,
            Notes: "Reverse with an injected atomic persistence failure.",
            EvidenceLinks: [reversalEvidence],
            LedgerBookId: posted.JournalEntry.LedgerBookId));

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("Injected atomic manual-journal batch failure.");
        var retained = await draftStore.ListAsync(posted.JournalEntry.FundProfileId, posted.JournalEntry.LedgerBookId);
        retained.Should().ContainSingle(draft =>
            draft.JournalEntryId == posted.JournalEntry.JournalEntryId &&
            draft.Status == ManualJournalEntryStatusDto.Posted &&
            draft.Version == posted.JournalEntry.Version);
        retained.Should().NotContain(draft =>
            draft.ReversalOfJournalEntryId == posted.JournalEntry.JournalEntryId);
        draftStore.BatchSaveAttempts.Should().Be(1);
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
            Actor: "controller",
            EvidenceLinks: ["evidence://accounting/configuration/activation-approval"]));

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

    private static AccountingConfigurationService CreateService(ILedgerBookService? ledgerBookService = null)
    {
        return new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore(),
            ledgerBookService);
    }

    private static AccountingConfigurationService CreateFileBackedService(string snapshotPath)
    {
        var store = new FileAccountingConfigurationStore(snapshotPath);
        return new AccountingConfigurationService(store, store);
    }

    private static LedgerBookDto LedgerBook(Guid ledgerBookId, string fundProfileId)
        => new(
            ledgerBookId,
            fundProfileId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            FundStructureNodeKindDto.Fund,
            "GAAP close book",
            "USD",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-close-v1",
            AccountingPolicyVersion: "v1");

    private static ManualJournalEntryWorkbenchService CreateManualJournalEntryWorkbenchService(
        IAccountingConfigurationService configurationService,
        ILedgerJournalStore? journalStore = null,
        ReportPackWorkflowService? reportPackWorkflowService = null,
        IBankTransactionSource? bankTransactionSource = null,
        bool includeDefaultJournalStore = true,
        IGovernedLedgerPostingTarget? postingTarget = null,
        IManualJournalEntryDraftStore? draftStore = null,
        bool includeAuthoritativeSecurityMaster = true)
    {
        journalStore ??= includeDefaultJournalStore
            ? WritableManualJournalLedgerJournalStore.Default()
            : null;

        return new ManualJournalEntryWorkbenchService(
            draftStore ?? new InMemoryManualJournalEntryDraftStore(),
            configurationService,
            new InMemoryAccountingActionAuditStore(),
            securityMasterQueryService: includeAuthoritativeSecurityMaster
                ? new DailyValuationSecurityMasterQueryService()
                : null,
            journalStore: journalStore,
            reportPackWorkflowService: reportPackWorkflowService,
            bankTransactionSource: bankTransactionSource,
            postingTarget: postingTarget);
    }

    private static async Task<ManualJournalCloseLockAuthorityFixture> CreateManualJournalCloseLockAuthorityFixtureAsync(
        LedgerAccountingPeriod? authoritativePeriod)
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var now = DateTimeOffset.UtcNow;
        var posted = BalancedManualJournalEntry() with
        {
            Status = ManualJournalEntryStatusDto.Posted,
            Version = 4,
            ApprovalId = $"approval:{Guid.NewGuid():N}",
            SubmittedAtUtc = now.AddMinutes(-3),
            SubmittedBy = "ops-user",
            ApprovedAtUtc = now.AddMinutes(-2),
            ApprovedBy = "controller",
            PostedAtUtc = now.AddMinutes(-1),
            PostedBy = "controller",
            UpdatedAtUtc = now.AddMinutes(-1)
        };
        await draftStore.SaveAsync(posted);

        ILedgerJournalStore? journalStore = null;
        if (authoritativePeriod is not null)
        {
            journalStore = Substitute.For<ILedgerJournalStore>();
            journalStore.GetPeriodAsync(ManualJournalPeriodId, Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<LedgerAccountingPeriod?>(authoritativePeriod));
        }

        var service = CreateManualJournalEntryWorkbenchService(
            configuration,
            journalStore: journalStore,
            includeDefaultJournalStore: false,
            draftStore: draftStore);
        return new ManualJournalCloseLockAuthorityFixture(service, draftStore, posted);
    }

    private static LedgerAccountingPeriod ManualJournalAccountingPeriod(
        string status,
        Guid? periodId = null,
        Guid? ledgerBookId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new LedgerAccountingPeriod(
            periodId ?? ManualJournalPeriodId,
            ledgerBookId ?? ManualJournalLedgerBookId,
            2026,
            6,
            "2026-06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            status,
            now,
            string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase) ? null : now,
            1);
    }

    private static JournalEntryLifecycleActionRequestDto ManualJournalCloseLockRequest(
        ManualJournalEntryDraftDto posted,
        bool periodIsLocked)
        => new(
            posted.JournalEntryId,
            posted.FundProfileId,
            JournalEntryLifecycleActionDto.LockAfterClose,
            "controller",
            posted.Version,
            Notes: "Lock after retained hard-close approval.",
            EvidenceLinks:
            [
                $"/api/workstation/evidence/subjects/accounting-close/period-lock/ledger-book/{posted.LedgerBookId:D}/period/{posted.PeriodId}/journal-entry/{posted.JournalEntryId:D}"
            ],
            LedgerBookId: posted.LedgerBookId,
            PeriodIsLocked: periodIsLocked);

    private sealed record ManualJournalCloseLockAuthorityFixture(
        ManualJournalEntryWorkbenchService Service,
        InMemoryManualJournalEntryDraftStore DraftStore,
        ManualJournalEntryDraftDto Posted);

    private sealed class FailOnceManualJournalEntryDraftStore : IManualJournalEntryDraftStore
    {
        private readonly InMemoryManualJournalEntryDraftStore _inner = new();

        public bool FailNextBatch { get; set; }

        public int BatchSaveAttempts { get; private set; }

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
            => _inner.ListFundProfileIdsAsync(ct);

        public Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
            string fundProfileId,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => _inner.ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);

        public Task<ManualJournalEntryDraftDto?> GetAsync(
            string fundProfileId,
            Guid journalEntryId,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => _inner.GetAsync(fundProfileId, journalEntryId, ct, tenantId, companyId);

        public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
            => _inner.SaveAsync(draft, ct);

        public Task SaveBatchAsync(
            IReadOnlyList<ManualJournalEntryDraftDto> drafts,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            BatchSaveAttempts++;
            if (FailNextBatch)
            {
                FailNextBatch = false;
                throw new IOException("Injected atomic manual-journal batch failure.");
            }

            return _inner.SaveBatchAsync(drafts, ct);
        }
    }

    private static string ManualJournalApprovalEvidence(ManualJournalEntryDraftDto journalEntry)
        => $"/api/workstation/evidence/subjects/accounting-record/approval/ledger-book/{journalEntry.LedgerBookId:D}/{journalEntry.PeriodId}";

    private static string ManualJournalReviewEvidence(ManualJournalEntryDraftDto journalEntry)
        => $"/api/workstation/evidence/subjects/accounting-record/review/ledger-book/{journalEntry.LedgerBookId:D}/{journalEntry.PeriodId}";

    private static string ManualJournalPostingEvidence(ManualJournalEntryDraftDto journalEntry)
        => $"/api/workstation/evidence/subjects/accounting-record/posting/ledger-book/{journalEntry.LedgerBookId:D}/{journalEntry.PeriodId}";

    private static JournalEntry BuildJournal(
        DateTimeOffset timestamp,
        string description,
        params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var journalEntryId = Guid.NewGuid();
        return new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            lines.Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description)).ToArray());
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


    private static LedgerJournalEntryRecord CreatePostedPrivateCapitalJournalRecord(
        Guid journalEntryId,
        string tenantId,
        string companyId,
        string fundEventId,
        string capitalAccountId,
        long sequence,
        DateTimeOffset timestamp)
    {
        var cashLedgerEntryId = Guid.NewGuid();
        var capitalLedgerEntryId = Guid.NewGuid();
        var journal = new JournalEntry(
            journalEntryId,
            timestamp,
            $"Posted private-capital event {fundEventId}",
            [
                new LedgerEntry(
                    cashLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "entity-master"),
                    100m,
                    0m,
                    $"Posted private-capital event {fundEventId}"),
                new LedgerEntry(
                    capitalLedgerEntryId,
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Capital Contributions", LedgerAccountType.Equity, FinancialAccountId: capitalAccountId),
                    0m,
                    100m,
                    $"Posted private-capital event {fundEventId}")
            ],
            new JournalEntryMetadata(
                ActivityType: "CapitalCall",
                EffectiveDate: new DateOnly(2026, 6, 30),
                FundEventId: fundEventId,
                FundEventType: "CapitalCall",
                CapitalAccountId: capitalAccountId,
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenantId"] = tenantId,
                    ["companyId"] = companyId,
                    ["automatedJournalStatus"] = "Posted",
                    ["automatedJournalApprovalId"] = $"approval:{fundEventId}",
                    ["evidenceLinks"] = $"/api/workstation/evidence/subjects/private-capital/{fundEventId}"
                }));

        return new LedgerJournalEntryRecord(
            journal,
            Guid.NewGuid(),
            ManualJournalPeriodId,
            CommandId: null,
            CorrelationId: null,
            GlobalSequence: sequence,
            CreatedAt: timestamp);
    }

    private sealed class RecordingGovernedLedgerPostingTarget(ILedgerJournalStore store)
        : IGovernedLedgerPostingTarget, IDisposable
    {
        private readonly DurableLedgerPostingTarget _inner = new(store);

        public int PostCount { get; private set; }

        public LedgerJournalEntryWrite? LastWrite { get; private set; }

        public async Task<GovernedLedgerPostingResult> PostAsync(
            LedgerJournalEntryWrite write,
            CancellationToken ct = default)
        {
            PostCount++;
            LastWrite = write;
            return await _inner.PostAsync(write, ct);
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class StaticMarkPriceSource(MarkPriceQuote quote) : IMarkPriceSource
    {
        public Task<MarkPriceQuote?> GetMarkPriceAsync(
            string symbol,
            DateOnly asOf,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<MarkPriceQuote?>(
                string.Equals(symbol, "AAPL", StringComparison.OrdinalIgnoreCase) ? quote : null);
        }
    }

    private sealed class MutableMarkPriceSource : IMarkPriceSource
    {
        private readonly Dictionary<string, MarkPriceQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string symbol, MarkPriceQuote quote) => _quotes[symbol] = quote;

        public Task<MarkPriceQuote?> GetMarkPriceAsync(
            string symbol,
            DateOnly asOf,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_quotes.TryGetValue(symbol, out var quote) ? quote : null);
        }
    }

    private static DailyValuationPositionService CreateDailyValuationPositionService(
        IPositionSnapshotStore? snapshotStore = null)
    {
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.GetDefinition(Arg.Is<string>(symbol =>
                string.Equals(symbol, "AAPL", StringComparison.OrdinalIgnoreCase)))
            .Returns(new CanonicalSymbolDefinition
            {
                Canonical = "AAPL",
                SecurityId = DailyValuationAaplSecurityId,
                DisplayName = "Apple Inc.",
                AssetClass = "equity",
                Exchange = "NASDAQ",
                Currency = "USD",
                Aliases = ["AAPL"]
            });
        registry.GetDefinition(Arg.Is<string>(symbol =>
                string.Equals(symbol, "MSFT", StringComparison.OrdinalIgnoreCase)))
            .Returns(new CanonicalSymbolDefinition
            {
                Canonical = "MSFT",
                SecurityId = DailyValuationMsftSecurityId,
                DisplayName = "Microsoft Corp.",
                AssetClass = "equity",
                Exchange = "NASDAQ",
                Currency = "USD",
                Aliases = ["MSFT"]
            });
        return new DailyValuationPositionService(
            snapshotStore,
            registry,
            new DailyValuationSecurityMasterQueryService());
    }

    private static async IAsyncEnumerable<AccountSnapshotRecord> SnapshotHistory(
        params AccountSnapshotRecord[] snapshots)
    {
        await Task.Yield();
        foreach (var snapshot in snapshots)
        {
            yield return snapshot;
        }
    }

    private sealed class DailyValuationSecurityMasterQueryService
        : Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService
    {
        private static readonly JsonElement EmptyTerms = JsonDocument.Parse("{}").RootElement.Clone();

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(
                securityId == DailyValuationAaplSecurityId
                    ? CreateDetail(DailyValuationAaplSecurityId, "AAPL", "Apple Inc.", new DateTimeOffset(1980, 12, 12, 0, 0, 0, TimeSpan.Zero))
                    : securityId == DailyValuationMsftSecurityId
                        ? CreateDetail(DailyValuationMsftSecurityId, "MSFT", "Microsoft Corp.", new DateTimeOffset(1986, 3, 13, 0, 0, 0, TimeSpan.Zero))
                        : null);

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(
            Guid securityId,
            DateTimeOffset asOfUtc,
            CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetRecordedByIdAsOfAsync(
            Guid securityId,
            DateTimeOffset asOfUtc,
            CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
            => Task.FromResult<SecurityDetailDto?>(
                identifierKind != SecurityIdentifierKind.Ticker
                    ? null
                    : string.Equals(identifierValue, "AAPL", StringComparison.OrdinalIgnoreCase)
                        ? CreateDetail(DailyValuationAaplSecurityId, "AAPL", "Apple Inc.", new DateTimeOffset(1980, 12, 12, 0, 0, 0, TimeSpan.Zero))
                        : string.Equals(identifierValue, "MSFT", StringComparison.OrdinalIgnoreCase)
                            ? CreateDetail(DailyValuationMsftSecurityId, "MSFT", "Microsoft Corp.", new DateTimeOffset(1986, 3, 13, 0, 0, 0, TimeSpan.Zero))
                            : null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(
            SecuritySearchRequest request,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(
            SecurityHistoryRequest request,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(
            Guid securityId,
            CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(
            Guid securityId,
            DateTimeOffset asOf,
            CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(
            Guid securityId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(
            Guid securityId,
            CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(
            Guid securityId,
            CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);

        private static SecurityDetailDto CreateDetail(
            Guid securityId,
            string symbol,
            string displayName,
            DateTimeOffset validFrom)
            => new(
                securityId,
                "Equity",
                SecurityStatusDto.Active,
                displayName,
                "USD",
                EmptyTerms,
                EmptyTerms,
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Ticker,
                        symbol,
                        IsPrimary: true,
                        ValidFrom: validFrom,
                        NormalizedValue: symbol)
                ],
                Aliases: [],
                Version: 7,
                EffectiveFrom: validFrom,
                EffectiveTo: null);
    }

    private sealed class WritableManualJournalLedgerJournalStore(
        LedgerBookRecord book,
        LedgerAccountingPeriod period) : ILedgerJournalStore
    {
        private readonly List<LedgerJournalEntryWrite> _writes = [];
        private readonly List<LedgerJournalEntryRecord> _records = [];
        private LedgerAccountingPeriod CurrentPeriod { get; set; } = period;

        public IReadOnlyList<LedgerJournalEntryWrite> Appended => _writes;

        public void SetPeriodStatus(string status)
        {
            CurrentPeriod = CurrentPeriod with
            {
                Status = status,
                ClosedAt = string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : DateTimeOffset.UtcNow,
                Version = CurrentPeriod.Version + 1
            };
        }

        public static WritableManualJournalLedgerJournalStore Default(
            AccountingBasisKindDto accountingBasis = AccountingBasisKindDto.Gaap)
        {
            var now = DateTimeOffset.UtcNow;
            var book = new LedgerBookRecord(
                ManualJournalLedgerBookId,
                "fund-alpha",
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                FundStructureNodeKindDto.Fund,
                $"{accountingBasis} close book",
                "USD",
                now,
                now,
                AccountingBasis: accountingBasis,
                AccountingPolicyId: accountingBasis == AccountingBasisKindDto.Gaap ? "gaap-close-v1" : "primary-v1",
                AccountingPolicyVersion: "v1");
            var period = new LedgerAccountingPeriod(
                ManualJournalPeriodId,
                ManualJournalLedgerBookId,
                2026,
                6,
                "2026-06",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Open",
                now,
                null,
                1);
            return new WritableManualJournalLedgerJournalStore(book, period);
        }

        public void Seed(JournalEntry entry)
        {
            _records.Add(new LedgerJournalEntryRecord(
                entry,
                book.LedgerBookId,
                CurrentPeriod.PeriodId,
                CommandId: null,
                CorrelationId: null,
                GlobalSequence: _records.Count + 1,
                CreatedAt: entry.Timestamp,
                AccountingBasis: book.AccountingBasis,
                AccountingPolicyId: book.AccountingPolicyId,
                AccountingPolicyVersion: book.AccountingPolicyVersion));
        }

        public WritableManualJournalLedgerJournalStore RestartFromRetainedRecords()
        {
            var restarted = new WritableManualJournalLedgerJournalStore(book, CurrentPeriod);
            restarted._records.AddRange(_records);
            return restarted;
        }

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            entry = AccountingPostingCommandValidator.NormalizeAndValidate(entry);
            LedgerPeriodPostingGuard.Validate(entry, CurrentPeriod);
            _writes.Add(entry);
            _records.Add(new LedgerJournalEntryRecord(
                entry.Entry,
                entry.AggregateId,
                entry.PeriodId,
                entry.CommandId,
                entry.CorrelationId,
                _records.Count + 1,
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

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<LedgerJournalEntryRecord> filtered = _records;
            if (query.LedgerBookId.HasValue)
                filtered = filtered.Where(record => record.AggregateId == query.LedgerBookId.Value);
            if (query.PeriodId.HasValue)
                filtered = filtered.Where(record => record.PeriodId == query.PeriodId.Value);
            if (query.AggregateId.HasValue)
                filtered = filtered.Where(record => record.AggregateId == query.AggregateId.Value);
            if (query.OccurredFrom.HasValue)
                filtered = filtered.Where(record => record.Entry.Timestamp >= query.OccurredFrom.Value);
            if (query.OccurredTo.HasValue)
                filtered = filtered.Where(record => record.Entry.Timestamp <= query.OccurredTo.Value);
            if (query.SourceEventId.HasValue)
                filtered = filtered.Where(record => record.SourceEventId == query.SourceEventId.Value);

            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(filtered.ToArray());
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _records.Where(record => record.PeriodId == periodId).ToArray());
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                _records.Where(record => record.AggregateId == aggregateId).ToArray());
        }

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<LedgerAccountingPeriod?>(periodId == CurrentPeriod.PeriodId ? CurrentPeriod : null);
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
                (!ledgerBookId.HasValue || CurrentPeriod.LedgerBookId == ledgerBookId.Value) &&
                (string.IsNullOrWhiteSpace(status) || string.Equals(CurrentPeriod.Status, status, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)) &&
                (!fundStructureNodeId.HasValue || book.FundStructureNodeId == fundStructureNodeId.Value);
            return Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(matches ? [CurrentPeriod] : []);
        }

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CurrentPeriod = period;
            return Task.FromResult(period);
        }

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
            Task.FromResult(book);
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

    private sealed class StaticLedgerBookService(params LedgerBookDto[] books) : ILedgerBookService
    {
        public Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("Test service is read-only.");

        public Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(books.FirstOrDefault(book => book.LedgerBookId == ledgerBookId));
        }

        public Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var rows = books
                .Where(book => string.IsNullOrWhiteSpace(query.FundProfileId) ||
                    string.Equals(book.FundProfileId, query.FundProfileId, StringComparison.OrdinalIgnoreCase))
                .Where(book => !query.FundStructureNodeId.HasValue || book.FundStructureNodeId == query.FundStructureNodeId.Value)
                .Where(book => !query.FundStructureNodeKind.HasValue || book.FundStructureNodeKind == query.FundStructureNodeKind.Value)
                .Where(book => !query.AccountingBasis.HasValue || book.AccountingBasis == query.AccountingBasis.Value)
                .ToArray();
            return Task.FromResult<IReadOnlyList<LedgerBookDto>>(rows);
        }

        public Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("Test service is read-only.");

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

        public Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(
            Guid periodId,
            CloseLedgerPeriodRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Test service is read-only.");
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

    private static async Task SeedManualJournalTenantConfigurationAsync(
        AccountingConfigurationService service,
        string tenantId,
        string companyId)
    {
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto($"cash-{tenantId}", "Assets:Cash", "Cash", "Asset"),
            Actor: "ops-user",
            LedgerBookId: ManualJournalLedgerBookId,
            CompanyId: companyId,
            TenantId: tenantId));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto($"interest-income-{tenantId}", "Income:Interest", "Interest Income", "Revenue"),
            Actor: "ops-user",
            LedgerBookId: ManualJournalLedgerBookId,
            CompanyId: companyId,
            TenantId: tenantId));
    }

    private static async Task SeedBookScopedRuleAsync(
        AccountingConfigurationService service,
        Guid ledgerBookId,
        string suffix,
        string displayName)
    {
        var cashPath = $"Assets:Cash:{suffix}";
        var incomePath = $"Income:Interest:{suffix}";
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto($"cash-{suffix}", cashPath, $"Cash {suffix}", "Asset"),
            Actor: "ops-user",
            LedgerBookId: ledgerBookId));
        await service.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            FundProfileId: "fund-alpha",
            Node: new ChartOfAccountsNodeDto($"income-{suffix}", incomePath, $"Interest {suffix}", "Revenue"),
            Actor: "ops-user",
            LedgerBookId: ledgerBookId));
        await service.UpsertTemplateAsync(new UpsertJournalEntryTemplateRequest(
            FundProfileId: "fund-alpha",
            Template: new JournalEntryTemplateDto(
                $"template-{suffix}",
                displayName,
                "Book-scoped interest accrual.",
                [
                    new JournalEntryTemplateLineDto($"debit-{suffix}", cashPath, AccountingTemplateLineSideDto.Debit, 100m),
                    new JournalEntryTemplateLineDto($"credit-{suffix}", incomePath, AccountingTemplateLineSideDto.Credit, 100m)
                ]),
            Actor: "ops-user",
            LedgerBookId: ledgerBookId));
        await service.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            FundProfileId: "fund-alpha",
            Rule: new PostingRuleDto(
                $"rule-{suffix}",
                displayName,
                "InterestAccrual",
                $"template-{suffix}"),
            Actor: "ops-user",
            LedgerBookId: ledgerBookId));
    }

    private static ManualJournalEntryDraftDto BalancedManualJournalEntry()
    {
        var now = DateTimeOffset.UtcNow;
        return new ManualJournalEntryDraftDto(
            JournalEntryId: Guid.NewGuid(),
            Status: ManualJournalEntryStatusDto.Draft,
            FundProfileId: "fund-alpha",
            LedgerBookId: ManualJournalLedgerBookId,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingDate: new DateOnly(2026, 6, 30),
            PeriodId: ManualJournalPeriodId.ToString("D"),
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
                new ManualJournalEntryLineDto(
                    "debit-cash",
                    AccountingTemplateLineSideDto.Debit,
                    100m,
                    "USD",
                    "Assets:Cash",
                    SecurityId: DailyValuationAaplSecurityId,
                    LedgerAccountSymbol: "AAPL"),
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
