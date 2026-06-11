using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class CapitalAccountWorkbenchServiceTests
{
    [Fact]
    public async Task GetWorkbenchAsync_WhenReportPackWasRestated_ProjectsStatementLineageAndAuditDrillThrough()
    {
        var workflow = new ReportPackWorkflowService();
        var created = workflow.Create(
            "fund-alpha",
            "capital-account:fund-alpha:lp-1",
            "2026-06",
            new VersionedReportTemplateIdDto("capital-account-statement", 1),
            "controller",
            [
                new ReportPackLineProvenanceDto(
                    "capital-account-ending",
                    "ledger",
                    "ledger-line-1",
                    "evidence-ledger-1",
                    LedgerEntryId: "ledger-line-1",
                    ReconciliationCaseId: "reconciliation-case-1",
                    ReportValue: "100.00",
                    ProviderEventId: "provider-event-1",
                    SecurityMasterId: "security-1",
                    ReconciliationOutcome: "matched",
                    ApprovalId: "approval-lp-1")
            ]);
        var validated = workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Validated, "validator", "validator");
        var pending = workflow.Transition(validated.ReportId, ReportPackWorkflowStateDto.PendingApproval, "validator", "validator");
        var approved = workflow.Transition(pending.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "publisher",
            "hash-restated",
            "manifest-restated",
            "/evidence/report-packs/manifest-restated.json",
            [
                new ReportPackEvidenceLinkDto("evidence-published", "Published support", "/evidence/report-pack/published", "ReportPack"),
                new ReportPackEvidenceLinkDto("evidence-ledger-1", "Line evidence", "/evidence/ledger-line-1", "Ledger"),
                new ReportPackEvidenceLinkDto("ledger-line-1", "Ledger entry", "/ledger/ledger-line-1", "Ledger"),
                new ReportPackEvidenceLinkDto("reconciliation-case-1", "Reconciliation case", "/reconciliation/reconciliation-case-1", "Reconciliation"),
                new ReportPackEvidenceLinkDto("provider-event-1", "Provider event", "/provider-events/provider-event-1", "Provider"),
                new ReportPackEvidenceLinkDto("security-1", "Security Master definition", "/security-master/security-1", "SecurityMaster"),
                new ReportPackEvidenceLinkDto("approval-lp-1", "Approval", "/approvals/approval-lp-1", "Approvals")
            ]);
        var restated = workflow.Restate(
            published.ReportId,
            "approver",
            "approver",
            "capital-account-correction",
            "audit-partner",
            Guid.NewGuid(),
            [new ReportPackChangedLineDto(
                "capital-account-ending",
                "100.00",
                "125.00",
                [new ReportPackEvidenceLinkDto("evidence-restatement", "Restatement support", "/evidence/report-pack/restatement", "ReportPack")])]);
        var reportOutput = new PrivateCapitalReportOutputDto(
            "report-output:fund-alpha:lp-1:statement",
            "CapitalAccountStatement",
            "LP 1 Capital Account Statement",
            "/reporting/report-packs/lp-1",
            "fund-event:fund-alpha:capital-call",
            "CapitalCall",
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            ManualJournalEntryStatusDto.Approved,
            new DateOnly(2026, 6, 30),
            "USD",
            125m,
            1,
            ["/evidence/report-pack/published"],
            true,
            [],
            IsPublished: true,
            ReportPackId: restated.ReportId.ToString("D"),
            ReportWorkflowState: restated.State.ToString(),
            PublicationManifestId: "manifest-from-output",
            RetainedManifestPath: "/evidence/report-packs/manifest-from-output.json",
            PublicationEvidenceHash: "hash-from-output",
            PublishedAtUtc: new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero),
            PublishedBy: "publisher",
            ReportLineProvenanceCount: 1,
            ReportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output%3Afund-alpha%3Alp-1%3Astatement",
            EvidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call/packet",
            CapitalAccountSubledgerRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1");
        var activity = BuildActivity(reportOutput);
        var service = new CapitalAccountWorkbenchService(new StubManualJournalEntryWorkbenchService(activity), workflow);

        var workbench = await service.GetWorkbenchAsync("fund-alpha");

        var allocationRule = workbench.AllocationRules.Should().ContainSingle(item => item.CategoryId == "report-output").Subject;
        allocationRule.RuleVersion.Should().Be("report-output:projection:1.1.1.1");
        allocationRule.EffectiveFrom.Should().Be(new DateOnly(2026, 6, 30));
        allocationRule.EffectiveTo.Should().Be(new DateOnly(2026, 6, 30));
        allocationRule.Formula.Should().Be("all(report_output.is_report_ready) and report_evidence_link_count > 0");
        allocationRule.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Approved.ToString());
        allocationRule.ApprovalReference.Should().Contain("approval-lp-1");
        allocationRule.ReplayTrace.Should().Contain("1 fund event(s)");
        allocationRule.Inputs.Should().ContainSingle(input =>
            input.Kind == "report-output" &&
            input.SourceId == reportOutput.ReportOutputId &&
            input.Amount == 125m &&
            input.Currency == "USD" &&
            input.EffectiveDate == new DateOnly(2026, 6, 30));
        allocationRule.RelatedFundEventIds.Should().ContainSingle().Which.Should().Be("fund-event:fund-alpha:capital-call");
        workbench.StatementLineage.Should().ContainSingle(item =>
            item.ReportOutputId == reportOutput.ReportOutputId &&
            item.HasRestatementLineage &&
            item.RestatementReasonCode == "capital-account-correction" &&
            item.RestatementApprover == "audit-partner" &&
            item.RestatementChangedLineCount == 1 &&
            item.RestatementEvidenceLinkCount == 1 &&
            item.RestatementEvidenceLinks.Contains("/evidence/report-pack/restatement") &&
            item.RestatementChangedLines.Count == 1 &&
            item.RestatementChangedLines[0].LineKey == "capital-account-ending" &&
            item.RestatementChangedLines[0].PreviousValue == "100.00" &&
            item.RestatementChangedLines[0].CurrentValue == "125.00" &&
            item.RestatementChangedLines[0].EvidenceLinks.Contains("/evidence/report-pack/restatement"));
        workbench.AuditDrillThroughs.Should().Contain(item =>
            item.Kind == "restatement" &&
            item.Route == reportOutput.EvidenceRoute &&
            item.EvidenceLinks.Contains("/evidence/report-pack/restatement"));
        workbench.LiveCapabilities.Should().Contain(item => item.Contains("restatement changed-line detail", StringComparison.OrdinalIgnoreCase));
        workbench.PlannedCapabilities.Should().Contain("Broad LP portal self-service");
    }

    [Fact]
    public async Task GetWorkbenchAsync_WhenFiltersDoNotMatchCapitalAccount_FailsClosedAndKeepsRowsEmpty()
    {
        var reportOutput = BuildPublishedReportOutput();
        var activity = BuildActivity(reportOutput);
        var service = new CapitalAccountWorkbenchService(new StubManualJournalEntryWorkbenchService(activity));

        var workbench = await service.GetWorkbenchAsync(
            "fund-alpha",
            capitalAccountId: "capital-account:fund-alpha:missing",
            investorId: "investor:missing",
            currency: "usd");

        workbench.StatusLabel.Should().Be("No capital accounts");
        workbench.StatusReason.Should().Contain("No investor-level capital accounts matched");
        workbench.CapitalAccountId.Should().Be("capital-account:fund-alpha:missing");
        workbench.InvestorId.Should().Be("investor:missing");
        workbench.Currency.Should().Be("USD");
        workbench.InvestorAccountCount.Should().Be(0);
        workbench.FundEventCount.Should().Be(0);
        workbench.StatementCount.Should().Be(0);
        workbench.AuditDrillThroughCount.Should().Be(1);
        workbench.InvestorAccounts.Should().BeEmpty();
        workbench.AllocationRules.Should().BeEmpty();
        workbench.StatementLineage.Should().BeEmpty();
        workbench.WorkbenchRoute.Should().Contain("capitalAccountId=capital-account%3Afund-alpha%3Amissing");
        workbench.WorkbenchRoute.Should().Contain("investorId=investor%3Amissing");
        workbench.WorkbenchRoute.Should().Contain("currency=USD");
    }

    [Fact]
    public async Task GetWorkbenchAsync_WhenCancellationAlreadyRequested_ThrowsBeforeLoadingActivity()
    {
        var reportOutput = BuildPublishedReportOutput();
        var activity = BuildActivity(reportOutput);
        var service = new CapitalAccountWorkbenchService(new StubManualJournalEntryWorkbenchService(activity));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetWorkbenchAsync("fund-alpha", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static PrivateCapitalReportOutputDto BuildPublishedReportOutput()
        => new(
            "report-output:fund-alpha:lp-1:statement",
            "CapitalAccountStatement",
            "LP 1 Capital Account Statement",
            "/reporting/report-packs/lp-1",
            "fund-event:fund-alpha:capital-call",
            "CapitalCall",
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            ManualJournalEntryStatusDto.Approved,
            new DateOnly(2026, 6, 30),
            "USD",
            125m,
            1,
            ["/evidence/report-pack/published"],
            true,
            [],
            IsPublished: true,
            ReportPackId: "33333333-3333-3333-3333-333333333333",
            ReportWorkflowState: ReportPackWorkflowStateDto.Published.ToString(),
            PublicationManifestId: "manifest-from-output",
            RetainedManifestPath: "/evidence/report-packs/manifest-from-output.json",
            PublicationEvidenceHash: "hash-from-output",
            PublishedAtUtc: new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero),
            PublishedBy: "publisher",
            ReportLineProvenanceCount: 1,
            ReportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output%3Afund-alpha%3Alp-1%3Astatement",
            EvidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call/packet",
            CapitalAccountSubledgerRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1");

    private static PrivateCapitalActivityProjectionDto BuildActivity(PrivateCapitalReportOutputDto reportOutput)
    {
        var fundEvent = new PrivateCapitalFundEventDto(
            "fund-event:fund-alpha:capital-call",
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            125m,
            125m,
            "Capital call",
            "payment:fund-alpha:lp-1",
            "settlement:fund-alpha:lp-1",
            ["/evidence/source"],
            [],
            DateTimeOffset.UtcNow,
            IsPosted: true,
            ApprovalId: "approval-lp-1");
        var subledgerEntry = new PrivateCapitalCapitalAccountSubledgerEntryDto(
            "subledger-entry:lp-1",
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.EntryType,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.JournalEntryId,
            fundEvent.EffectiveDate,
            fundEvent.GrossAmount,
            fundEvent.NetCapitalActivity,
            fundEvent.NetCapitalActivity,
            fundEvent.Memo,
            ["/evidence/source"],
            [],
            DateTimeOffset.UtcNow,
            IsPosted: true);
        var ledgerImpact = new PrivateCapitalLedgerImpactDto(
            "ledger-impact:lp-1",
            fundEvent.JournalEntryId,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            125m,
            125m,
            0m,
            2,
            true,
            true,
            ["/evidence/source"],
            [new PrivateCapitalLedgerLineImpactDto("line-1", "Equity:Capital Contributions", AccountingTemplateLineSideDto.Credit, 125m, "USD", null, null, null, "/evidence/source")],
            []);
        var record = new PrivateCapitalFundEventLedgerRecordDto(
            "fund-event-record:lp-1",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.JournalEntryId,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            fundEvent.GrossAmount,
            fundEvent.NetCapitalActivity,
            0m,
            125m,
            fundEvent.Memo,
            fundEvent.PaymentIntentId,
            fundEvent.SettlementReference,
            "/api/ledger/private-capital/activity?fundEventId=fund-event%3Afund-alpha%3Acapital-call",
            "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call/packet",
            fundEvent.ApprovalId,
            "/accounting/approvals?approvalId=approval-lp-1",
            true,
            true,
            true,
            true,
            PrivateCapitalFundEventLedgerReadinessDto.Published,
            "Published",
            "Published report output retained.",
            "Open statement",
            reportOutput.ReportOutputRoute,
            1,
            1,
            1,
            1,
            0,
            reportOutput.ReportOutputId,
            reportOutput.ReportOutputType,
            reportOutput.ReportOutputRoute,
            reportOutput.ReportWorkflowState,
            reportOutput.PublicationManifestId,
            reportOutput.RetainedManifestPath,
            reportOutput.ReportLineProvenanceCount,
            ["/evidence/source"],
            fundEvent,
            [subledgerEntry],
            [ledgerImpact],
            [reportOutput],
            [],
            [new PrivateCapitalEvidenceCategoryDto("source-support", "Source support", true, "Source support retained.", 1, ["/evidence/source"], ["Source support"])]);
        var capitalAccount = new PrivateCapitalCapitalAccountActivityDto(
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            125m,
            0m,
            0m,
            0m,
            0m,
            125m,
            1,
            fundEvent.EffectiveDate,
            fundEvent.FundEventType,
            [fundEvent.FundEventId]);
        var subledger = new PrivateCapitalCapitalAccountSubledgerDto(
            "capital-account-subledger:lp-1",
            "fund-alpha",
            null,
            DateTimeOffset.UtcNow,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
            125m,
            0m,
            0m,
            0m,
            0m,
            0m,
            125m,
            125m,
            1,
            0,
            1,
            1,
            1,
            0,
            fundEvent.EffectiveDate,
            fundEvent.EffectiveDate,
            fundEvent.FundEventType,
            ["/evidence/source"],
            capitalAccount,
            [record],
            [subledgerEntry],
            [ledgerImpact],
            [reportOutput],
            [],
            PrivateCapitalFundEventLedgerReadinessDto.Published,
            "Published",
            "Published statement retained.",
            "Open statement",
            reportOutput.ReportOutputRoute,
            [new PrivateCapitalEvidenceCategoryDto("report-output", "Report output", true, "Governed statement retained.", 1, ["/evidence/report-pack/published"], ["Governed report output"])]);

        return new PrivateCapitalActivityProjectionDto(
            "fund-alpha",
            null,
            DateTimeOffset.UtcNow,
            1,
            1,
            0,
            0,
            1,
            1,
            125m,
            "USD",
            [fundEvent],
            [capitalAccount],
            [subledgerEntry],
            [ledgerImpact],
            [reportOutput],
            [],
            [record],
            [subledger]);
    }

    private sealed class StubManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService
    {
        private readonly PrivateCapitalActivityProjectionDto _activity;

        public StubManualJournalEntryWorkbenchService(PrivateCapitalActivityProjectionDto activity)
        {
            _activity = activity;
        }

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["fund-alpha"]);

        public Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default)
            => Task.FromResult(new ManualJournalEntryWorkbenchDto("fund-alpha", ledgerBookId, DateTimeOffset.UtcNow, [], [], [], [], _activity));

        public Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default)
            => Task.FromResult(_activity);

        public Task<ManualJournalEntryDraftDto> SaveDraftAsync(SaveManualJournalEntryDraftRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(ValidateManualJournalEntryDraftRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(SubmitManualJournalEntryApprovalRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
