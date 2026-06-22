using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalFundEventCommandCenterServiceTests
{
    private const string FundProfileId = "fund-alpha";
    private const string FundEventId = "fund-event:fund-alpha:capital-call";

    [Fact]
    public async Task GetCommandCenterAsync_WhenFundEventIsSourceBacked_ProjectsV018CommandCenterLanes()
    {
        var activity = BuildActivity();
        var service = new PrivateCapitalFundEventCommandCenterService(new StubManualJournalEntryWorkbenchService(activity));

        var commandCenter = await service.GetCommandCenterAsync(FundProfileId, null, FundEventId);

        commandCenter.Should().NotBeNull();
        commandCenter!.FundEventId.Should().Be(FundEventId);
        commandCenter.FundProfileId.Should().Be(FundProfileId);
        commandCenter.CommandCenterRoute.Should().Contain(UiApiRoutes.LedgerPrivateCapitalFundEventCommandCenter);
        commandCenter.CommandCenterRoute.Should().Contain("fundEventId=" + Uri.EscapeDataString(FundEventId));
        commandCenter.ReadyLaneCount.Should().Be(10);
        commandCenter.BlockedLaneCount.Should().Be(0);
        commandCenter.Lanes.Select(static item => item.LaneId).Should().Equal(
            "evidence",
            "workflow",
            "ledger-impact",
            "capital-account-impact",
            "treasury-expectation",
            "reconciliation-status",
            "report-usage",
            "delivery-record",
            "tax-support",
            "audit-history");
        commandCenter.Lanes.Should().ContainSingle(item =>
            item.LaneId == "capital-account-impact" &&
            item.Route != null &&
            item.Route.Contains("fundProfileId=fund-alpha", StringComparison.OrdinalIgnoreCase) &&
            !item.Route.Contains("default-fund", StringComparison.OrdinalIgnoreCase));
        commandCenter.Lanes.Should().ContainSingle(item =>
            item.LaneId == "treasury-expectation" &&
            item.IsReady &&
            item.EvidenceLinks.Contains("/evidence/cash"));
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == $"evidence:{FundEventId}" &&
            item.Status == "Ready");
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == "payment-intent:payment:fund-alpha:lp-1" &&
            item.EvidenceLinks.Contains("/evidence/bank"));
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == "report-output:report-output:fund-alpha:lp-1:statement" &&
            item.Status == "Ready");
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == $"delivery:{FundEventId}" &&
            item.Status == "Ready" &&
            item.EvidenceLinks.Contains("/evidence/report-packs/manifest-from-output.json"));
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == $"tax-support:{FundEventId}" &&
            item.Status == "Ready" &&
            item.EvidenceLinks.Contains("/evidence/source") &&
            item.EvidenceLinks.Contains("/evidence/report-pack/published"));
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == $"audit-support:{FundEventId}" &&
            item.Status == "Ready" &&
            item.Route == "/accounting/approvals?approvalId=approval-lp-1" &&
            item.EvidenceLinks.Contains("/evidence/audit"));
        commandCenter.LiveCapabilities.Should().Contain(item => item.Contains("Event-level evidence", StringComparison.OrdinalIgnoreCase));
        commandCenter.PlannedCapabilities.Should().Contain("Native live treasury payment execution");
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenSupportEvidenceIsIncomplete_FlagsDeliveryTaxAndAuditPackages()
    {
        var activity = BuildActivity();
        var record = activity.FundEventRecords.Should().ContainSingle().Subject;
        var reportOutput = record.ReportOutputs.Should().ContainSingle().Subject with
        {
            IsPublished = false,
            IsReportReady = false,
            EvidenceLinkCount = 0,
            EvidenceLinks = [],
            RetainedManifestPath = null,
            PublicationManifestId = null
        };
        var incompleteRecord = record with
        {
            ApprovalRoute = null,
            EvidenceLinkCount = 0,
            EvidenceLinks = [],
            ReportOutputs = [reportOutput],
            ReportOutputCount = 1,
            PaymentIntentEvidence = null
        };
        var incompleteActivity = activity with
        {
            FundEventRecords = [incompleteRecord],
            ReportOutputs = [reportOutput],
            PaymentIntents = []
        };
        var service = new PrivateCapitalFundEventCommandCenterService(new StubManualJournalEntryWorkbenchService(incompleteActivity));

        var commandCenter = await service.GetCommandCenterAsync(FundProfileId, null, FundEventId);

        commandCenter.Should().NotBeNull();
        commandCenter!.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == $"delivery:{FundEventId}" &&
            item.Status == "ReviewRequired" &&
            item.EvidenceLinkCount == 0 &&
            item.RequiredActions.Contains("Retain delivery package or publication manifest"));
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == $"tax-support:{FundEventId}" &&
            item.Status == "ReviewRequired" &&
            item.EvidenceLinkCount == 0 &&
            item.RequiredActions.Contains("Attach tax support evidence or governed report output"));
        commandCenter.SupportPackages.Should().ContainSingle(item =>
            item.PackageId == $"audit-support:{FundEventId}" &&
            item.Status == "ReviewRequired" &&
            item.EvidenceLinkCount == 0 &&
            item.RequiredActions.Contains("Retain approval or audit evidence"));
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenFundEventIsMissing_FailsClosed()
    {
        var service = new PrivateCapitalFundEventCommandCenterService(new StubManualJournalEntryWorkbenchService(BuildActivity()));

        var commandCenter = await service.GetCommandCenterAsync(FundProfileId, null, "fund-event:fund-alpha:missing");

        commandCenter.Should().BeNull();
    }

    [Fact]
    public async Task GetCommandCenterAsync_WhenCancellationAlreadyRequested_DoesNotLoadActivity()
    {
        var stub = new StubManualJournalEntryWorkbenchService(BuildActivity());
        var service = new PrivateCapitalFundEventCommandCenterService(stub);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.GetCommandCenterAsync(FundProfileId, null, FundEventId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stub.PrivateCapitalActivityLoadCount.Should().Be(0);
    }

    private static PrivateCapitalActivityProjectionDto BuildActivity()
    {
        var now = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var fundEvent = new PrivateCapitalFundEventDto(
            FundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Approved,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
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
            now,
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
            now,
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
            [
                new PrivateCapitalLedgerLineImpactDto(
                    "line-1",
                    "Equity:Capital Contributions",
                    AccountingTemplateLineSideDto.Credit,
                    125m,
                    "USD",
                    null,
                    null,
                    null,
                    "/evidence/source")
            ],
            []);
        var reportOutput = new PrivateCapitalReportOutputDto(
            "report-output:fund-alpha:lp-1:statement",
            "CapitalAccountStatement",
            "LP 1 Capital Account Statement",
            "/reporting/report-packs/lp-1",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Approved,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            125m,
            1,
            ["/evidence/report-pack/published"],
            true,
            [],
            IsPublished: true,
            ReportPackId: "report-pack:fund-alpha:lp-1",
            ReportWorkflowState: "Published",
            PublicationManifestId: "manifest-from-output",
            RetainedManifestPath: "/evidence/report-packs/manifest-from-output.json",
            PublicationEvidenceHash: "hash-from-output",
            PublishedAtUtc: now,
            PublishedBy: "publisher",
            ReportLineProvenanceCount: 1,
            ReportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output%3Afund-alpha%3Alp-1%3Astatement",
            FundEventRecordRoute: "/api/ledger/private-capital/fund-event-record?fundEventId=fund-event%3Afund-alpha%3Acapital-call",
            EvidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call/packet",
            CapitalAccountSubledgerRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1");
        var paymentIntent = new PaymentIntentWorkflowDto(
            "payment:fund-alpha:lp-1",
            fundEvent.SettlementReference,
            FundProfileId,
            null,
            fundEvent.FundEventId,
            fundEvent.JournalEntryId,
            "controller",
            now,
            PaymentIntentWorkflowStatusDto.ExecutionDeferred,
            "Ready, execution deferred",
            "Payment intent evidence is retained; execution remains deferred to treasury.",
            "Execution is deferred to the external treasury system.",
            new PaymentIntentExpectedCashMovementDto(
                "payment:fund-alpha:lp-1",
                PaymentIntentCashDirectionDto.Inflow,
                125m,
                "USD",
                fundEvent.EffectiveDate,
                fundEvent.SettlementReference,
                fundEvent.FundEventId,
                fundEvent.FundEventType,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                "Capital call receipt",
                "fund:fund-alpha",
                "fund:fund-alpha / capital-account:fund-alpha:lp-1 / investor:lp-1",
                "Capital call receipt",
                "Controller approval retained before execution-deferred reliance",
                ["/evidence/source", "/evidence/bank"]),
            "/evidence/payment-intent",
            "/api/ledger/private-capital/activity?paymentIntentId=payment%3Afund-alpha%3Alp-1",
            ApprovalChain: [new PaymentIntentApprovalStepDto(1, "Controller", "controller", "Approved", now, "/approvals/payment-intent")],
            BankEvidence: [new PaymentIntentBankEvidenceDto("bank-evidence:lp-1", "BankStatement", "Matched", "Cash evidence retained.", EvidenceRoute: "/evidence/bank")],
            ReconciliationLinks: [new PaymentIntentReconciliationLinkDto("reconciliation:lp-1", "Matched", "Cash movement reconciled.", "/evidence/reconciliation")],
            AuditHistory: [new PaymentIntentAuditEventDto("audit:lp-1", now, "controller", "payment-intent.defer", "Execution deferred with evidence.", ["/evidence/audit"])]
        );
        var paymentEvidence = new PrivateCapitalPaymentIntentEvidenceDto(
            fundEvent.PaymentIntentId,
            fundEvent.SettlementReference,
            PrivateCapitalPaymentIntentEvidenceStatusDto.SettlementMatched,
            true,
            PaymentIntentCashDirectionDto.Inflow,
            125m,
            "USD",
            fundEvent.EffectiveDate,
            "Payment intent and cash evidence retained.",
            1,
            ["/evidence/cash"],
            EvidenceRoute: "/evidence/payment-intent");
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
            [new PrivateCapitalEvidenceCategoryDto("source-support", "Source support", true, "Source support retained.", 1, ["/evidence/source"], ["Source support"])],
            paymentEvidence);
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
            FundProfileId,
            null,
            now,
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
            FundProfileId,
            null,
            now,
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
            [subledger],
            [paymentIntent]);
    }

    private sealed class StubManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService
    {
        private readonly PrivateCapitalActivityProjectionDto _activity;

        public StubManualJournalEntryWorkbenchService(PrivateCapitalActivityProjectionDto activity)
        {
            _activity = activity;
        }

        public int PrivateCapitalActivityLoadCount { get; private set; }

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([FundProfileId]);

        public Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default, string? tenantId = null, string? companyId = null)
            => Task.FromResult(new ManualJournalEntryWorkbenchDto(FundProfileId, ledgerBookId, DateTimeOffset.UtcNow, [], [], [], [], _activity));

        public Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default, string? tenantId = null, string? companyId = null)
        {
            PrivateCapitalActivityLoadCount++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_activity);
        }

        public Task<ManualJournalEntryDraftDto> SaveDraftAsync(SaveManualJournalEntryDraftRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(ValidateManualJournalEntryDraftRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(SubmitManualJournalEntryApprovalRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
