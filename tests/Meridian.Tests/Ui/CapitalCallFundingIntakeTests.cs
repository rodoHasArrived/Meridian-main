using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Coverage for the funding half of the capital-call lifecycle (W9-NAV-006):
/// <see cref="AutomatedJournalIntakeRunner.RunCapitalCallFundingIntakeAsync"/> records LP cash
/// receipts against an issued call as governed Dr Cash / Cr Capital Call Receivable drafts in
/// the manual journal approval queue — never posting, corroborating the fundable ceiling from
/// the call's posted ledger activity (issuance debits minus funding credits), and refusing with
/// reasons whenever the receipt cannot be tied out: an unissued call, over-funding beyond the
/// open receivable, or a receipt with no retained evidence.
/// </summary>
public sealed class CapitalCallFundingIntakeTests
{
    private const string FundProfileId = "fund-alpha";
    private static readonly Guid BookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset AsOf = new(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly CallEffectiveDate = new(2026, 3, 15);
    private static readonly DateOnly ReceivedDate = new(2026, 3, 20);

    [Fact]
    public async Task RunCapitalCallFundingIntakeAsync_FullFunding_LandsGovernedDraftsIntoApprovalQueue_NotPosted()
    {
        var fixture = await CreateFixtureAsync(
            postedFundEvents: [PostedIssuanceEvent("cmt-1", "lp-1", 600_000m), PostedIssuanceEvent("cmt-2", "lp-2", 400_000m)],
            postedLedgerImpacts: [PostedIssuanceImpact("cmt-1", "lp-1", 600_000m), PostedIssuanceImpact("cmt-2", "lp-2", 400_000m)]);

        var result = await fixture.Runner.RunCapitalCallFundingIntakeAsync(BuildRequest(
            Funding("cmt-1", "lp-1", 600_000m),
            Funding("cmt-2", "lp-2", 400_000m)));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Ready);
        result.ReadinessBlockers.Should().BeEmpty();
        result.Intake.Skipped.Should().BeEmpty();
        result.Intake.Created.Should().HaveCount(2);
        result.Intake.Created.Should().OnlyContain(
            draft => draft.Status == ManualJournalEntryStatusDto.Draft,
            "funding drafts must land in the human approval queue, never post directly");
        // Funding is a cash receipt, not a second call: it stays General so the commitment
        // roll-forward never double-counts it as CapitalCall activity.
        result.Intake.Created.Should().OnlyContain(
            draft => draft.EntryType == ManualJournalEntryTypeDto.General);
        result.Intake.Created.Should().OnlyContain(
            draft => draft.TotalDebits == draft.TotalCredits && draft.TotalDebits > 0m);

        var first = result.Intake.Created.Single(draft => draft.TreasuryContext!.InvestorId == "lp-1");
        var second = result.Intake.Created.Single(draft => draft.TreasuryContext!.InvestorId == "lp-2");
        first.TotalDebits.Should().Be(600_000m);
        second.TotalDebits.Should().Be(400_000m);

        first.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Debit &&
            line.AccountPath == "Assets:Cash" &&
            line.Amount == 600_000m);
        first.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Credit &&
            line.AccountPath == "Assets:Capital Call Receivable" &&
            line.Amount == 600_000m);

        // Funding shares the issued call's fund event so the projector groups call and cash
        // receipt into one lifecycle once this draft posts.
        first.TreasuryContext!.FundEventId.Should().Be("fund-event:fund-alpha:capital-call:call-1:cmt-1");
        first.TreasuryContext.FundEventType.Should().Be("CapitalCall");
        first.TreasuryContext.CapitalAccountId.Should().Be("ca-lp-1");
        first.TreasuryContext.IdempotencyKey.Should().Be("capital-call-funding:cmt-1:call-1:cmt-1");
        first.EvidenceLinks.Should().Contain("evidence://funding/cmt-1/remittance");

        var assessment = result.EvidenceAssessments["capital-call-funding:cmt-1:call-1:cmt-1"];
        assessment.RequiresInvestigation.Should().BeFalse();
        assessment.ConfidenceScore.Should().Be(0.90m);
        assessment.Quality.Should().Be(AutomatedJournalEvidenceQualityDto.High);
        assessment.Reasons.Should().ContainSingle(reason => reason.Contains("remittance evidence"));

        var workbench = await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId);
        workbench.Drafts.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunCapitalCallFundingIntakeAsync_SecondRun_SkipsExistingDraftsInsteadOfDuplicating()
    {
        var fixture = await CreateFixtureAsync(
            postedFundEvents: [PostedIssuanceEvent("cmt-1", "lp-1", 600_000m), PostedIssuanceEvent("cmt-2", "lp-2", 400_000m)],
            postedLedgerImpacts: [PostedIssuanceImpact("cmt-1", "lp-1", 600_000m), PostedIssuanceImpact("cmt-2", "lp-2", 400_000m)]);
        var request = BuildRequest(
            Funding("cmt-1", "lp-1", 600_000m),
            Funding("cmt-2", "lp-2", 400_000m));

        var firstRun = await fixture.Runner.RunCapitalCallFundingIntakeAsync(request);
        var secondRun = await fixture.Runner.RunCapitalCallFundingIntakeAsync(request);

        firstRun.Intake.Created.Should().HaveCount(2);
        secondRun.Intake.Created.Should().BeEmpty("a re-run must never duplicate governed drafts");
        secondRun.Intake.Skipped.Should().HaveCount(2);
        secondRun.Intake.Skipped.Should().OnlyContain(skip => skip.IsReadyDuplicate);
    }

    [Fact]
    public async Task RunCapitalCallFundingIntakeAsync_PartialFunding_DraftsFundedPortionAndReportsOpenRemainder()
    {
        var fixture = await CreateFixtureAsync(
            postedFundEvents: [PostedIssuanceEvent("cmt-1", "lp-1", 600_000m)],
            postedLedgerImpacts: [PostedIssuanceImpact("cmt-1", "lp-1", 600_000m)]);

        var result = await fixture.Runner.RunCapitalCallFundingIntakeAsync(BuildRequest(
            Funding("cmt-1", "lp-1", 250_000m)));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Ready);
        var draft = result.Intake.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.TotalDebits.Should().Be(250_000m);

        // Partial funding never closes the receivable: the assessment says the remainder stays
        // open instead of implying the call settled.
        var assessment = result.EvidenceAssessments["capital-call-funding:cmt-1:call-1:cmt-1"];
        assessment.RequiresInvestigation.Should().BeFalse();
        assessment.Reasons.Should().Contain(reason =>
            reason.Contains("Partial funding") && reason.Contains("350000.00"));
        assessment.Summary.Should().Contain("leaving 350000.00 open");
    }

    [Fact]
    public async Task RunCapitalCallFundingIntakeAsync_MissingFundingEvidence_BlocksWithoutDrafting()
    {
        var fixture = await CreateFixtureAsync(
            postedFundEvents: [PostedIssuanceEvent("cmt-1", "lp-1", 600_000m)],
            postedLedgerImpacts: [PostedIssuanceImpact("cmt-1", "lp-1", 600_000m)]);

        var result = await fixture.Runner.RunCapitalCallFundingIntakeAsync(BuildRequest(
            Funding("cmt-1", "lp-1", 100_000m) with { EvidenceLinks = [] }));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        result.ReadinessBlockers.Should().ContainSingle(blocker =>
            blocker.Contains("cmt-1") && blocker.Contains("funding evidence"));
        result.Intake.Created.Should().BeEmpty("unattested cash receipts must refuse, not draft");
        result.EvidenceAssessments.Should().ContainKey("capital-call-funding|fund-alpha|call-1")
            .WhoseValue.RequiresInvestigation.Should().BeTrue();

        var workbench = await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId);
        workbench.Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCapitalCallFundingIntakeAsync_UnissuedCall_BlocksWithoutDrafting()
    {
        // Nothing is posted anywhere: funding call-1 must refuse instead of relieving a
        // receivable the ledger never raised.
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Runner.RunCapitalCallFundingIntakeAsync(BuildRequest(
            Funding("cmt-1", "lp-1", 100_000m)));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        result.ReadinessBlockers.Should().ContainSingle(blocker =>
            blocker.Contains("no posted CapitalCallIssued event"));
        result.Intake.Created.Should().BeEmpty();

        var workbench = await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId);
        workbench.Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCapitalCallFundingIntakeAsync_WithoutPostedActivitySource_BlocksWithoutDrafting()
    {
        var fixture = await CreateFixtureAsync(includeWorkbenchSource: false);

        var result = await fixture.Runner.RunCapitalCallFundingIntakeAsync(BuildRequest(
            Funding("cmt-1", "lp-1", 100_000m)));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        result.ReadinessBlockers.Should().ContainSingle(blocker =>
            blocker.Contains("posted private-capital activity source is unavailable"));
        result.Intake.Created.Should().BeEmpty();
    }

    [Fact]
    public void Produce_OverFunding_FailsClosed()
    {
        // 600k was issued and 500k already funded (both posted): only 100k remains open, so a
        // 200k receipt is refused with the server-computed amounts, never drafted for approval.
        var production = CapitalCallFundingDraftProducer.Produce(
            BuildRequest(Funding("cmt-1", "lp-1", 200_000m)),
            [PostedIssuanceEvent("cmt-1", "lp-1", 600_000m)],
            [PostedIssuanceImpact("cmt-1", "lp-1", 600_000m), PostedFundingImpact("cmt-1", "lp-1", 500_000m)],
            AsOf);

        production.IsReady.Should().BeFalse();
        production.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        production.Blockers.Should().ContainSingle(blocker =>
            blocker.Contains("exceeds the open capital-call receivable") && blocker.Contains("100000"));
        production.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void Produce_PostedFundingReducesOpenReceivable_RemainderStaysFundable()
    {
        // After a posted 250k partial receipt, exactly 350k remains fundable on the 600k call.
        var fundEvents = new[] { PostedIssuanceEvent("cmt-1", "lp-1", 600_000m) };
        var impacts = new[]
        {
            PostedIssuanceImpact("cmt-1", "lp-1", 600_000m),
            PostedFundingImpact("cmt-1", "lp-1", 250_000m)
        };

        var remainder = CapitalCallFundingDraftProducer.Produce(
            BuildRequest(Funding("cmt-1", "lp-1", 350_000m)), fundEvents, impacts, AsOf);
        var overRemainder = CapitalCallFundingDraftProducer.Produce(
            BuildRequest(Funding("cmt-1", "lp-1", 350_000.01m)), fundEvents, impacts, AsOf);

        remainder.IsReady.Should().BeTrue();
        var draft = remainder.Drafts.Should().ContainSingle().Subject;
        draft.Event.Amount.Should().Be(350_000m);
        draft.Lines.Should().Contain(line => line.account.Name == "Cash" && line.debit == 350_000m);
        draft.Lines.Should().Contain(line =>
            line.account.Name == "Capital Call Receivable" && line.credit == 350_000m);
        remainder.EvidenceAssessments[draft.Metadata.IdempotencyKey!].Summary
            .Should().Contain("posted funding 250000.00");

        overRemainder.IsReady.Should().BeFalse();
        overRemainder.Blockers.Should().ContainSingle(blocker =>
            blocker.Contains("exceeds the open capital-call receivable"));
    }

    [Fact]
    public void Produce_IssuanceStillInApprovalQueue_FailsClosed()
    {
        // The issuance draft exists but has not posted (IsPosted == false): funding must wait
        // for the governed lifecycle to raise the receivable first.
        var unpostedIssuance = PostedIssuanceEvent("cmt-1", "lp-1", 600_000m) with
        {
            JournalStatus = ManualJournalEntryStatusDto.Submitted,
            IsPosted = false
        };

        var production = CapitalCallFundingDraftProducer.Produce(
            BuildRequest(Funding("cmt-1", "lp-1", 100_000m)),
            [unpostedIssuance],
            [],
            AsOf);

        production.IsReady.Should().BeFalse();
        production.Blockers.Should().ContainSingle(blocker =>
            blocker.Contains("no posted CapitalCallIssued event"));
        production.Drafts.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Fixture
    // -------------------------------------------------------------------------

    private sealed record Fixture(
        AutomatedJournalIntakeRunner Runner,
        IManualJournalEntryWorkbenchService Workbench);

    private static async Task<Fixture> CreateFixtureAsync(
        bool includeWorkbenchSource = true,
        PrivateCapitalFundEventDto[]? postedFundEvents = null,
        PrivateCapitalLedgerImpactDto[]? postedLedgerImpacts = null)
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(CapitalCallFundingTestData.Workspace(FundProfileId));
        var configurationService = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            new InMemoryAccountingActionAuditStore());
        // The in-memory workbench has no posted ledger, so posted issuance activity is layered
        // onto its projection through the decorator; draft intake still flows through the real
        // workbench underneath.
        IManualJournalEntryWorkbenchService activitySource =
            postedFundEvents is null && postedLedgerImpacts is null
                ? workbench
                : new PostedActivityManualJournalWorkbench(workbench, postedFundEvents ?? [], postedLedgerImpacts ?? []);
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService);
        var runner = new AutomatedJournalIntakeRunner(
            intake,
            new FeeScheduleAccrualEventProducer(),
            manualJournalWorkbench: includeWorkbenchSource ? activitySource : null);
        return new Fixture(runner, workbench);
    }

    private static CapitalCallFundingInput Funding(string commitmentId, string investorId, decimal amount)
        => new(
            commitmentId,
            CapitalAccountId: $"ca-{investorId}",
            InvestorId: investorId,
            FundedAmount: amount,
            EvidenceLinks: [$"evidence://funding/{commitmentId}/remittance"]);

    private static RunCapitalCallFundingDraftIntakeRequest BuildRequest(
        params CapitalCallFundingInput[] fundings)
        => new(
            FundProfileId,
            Currency: "USD",
            Actor: "fund-controller",
            CallId: "call-1",
            ReceivedDate: ReceivedDate,
            Fundings: fundings,
            LedgerBookId: BookId,
            PeriodId: "2026-03",
            EntityId: "entity-alpha",
            AsOf: AsOf);

    private static PrivateCapitalFundEventDto PostedIssuanceEvent(
        string commitmentId,
        string investorId,
        decimal amount)
        => CapitalCallFundingTestData.PostedIssuanceEvent(
            FundProfileId, "call-1", commitmentId, investorId, amount, CallEffectiveDate, AsOf);

    private static PrivateCapitalLedgerImpactDto PostedIssuanceImpact(
        string commitmentId,
        string investorId,
        decimal amount)
        => CapitalCallFundingTestData.PostedIssuanceImpact(
            FundProfileId, "call-1", commitmentId, investorId, amount, CallEffectiveDate);

    private static PrivateCapitalLedgerImpactDto PostedFundingImpact(
        string commitmentId,
        string investorId,
        decimal amount)
        => CapitalCallFundingTestData.PostedFundingImpact(
            FundProfileId, "call-1", commitmentId, investorId, amount, CallEffectiveDate);
}

/// <summary>
/// Wraps the real manual journal workbench and layers canned posted private-capital activity
/// (fund events plus their ledger impacts) onto its projection, standing in for the posted
/// ledger the in-memory fixtures do not have. Draft save/validate/submit delegate unchanged.
/// </summary>
internal sealed class PostedActivityManualJournalWorkbench : IManualJournalEntryWorkbenchService
{
    private readonly IManualJournalEntryWorkbenchService _inner;
    private readonly IReadOnlyList<PrivateCapitalFundEventDto> _postedFundEvents;
    private readonly IReadOnlyList<PrivateCapitalLedgerImpactDto> _postedLedgerImpacts;

    public PostedActivityManualJournalWorkbench(
        IManualJournalEntryWorkbenchService inner,
        IReadOnlyList<PrivateCapitalFundEventDto> postedFundEvents,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> postedLedgerImpacts)
    {
        _inner = inner;
        _postedFundEvents = postedFundEvents;
        _postedLedgerImpacts = postedLedgerImpacts;
    }

    public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
        => _inner.ListFundProfileIdsAsync(ct);

    public Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
        => _inner.GetWorkbenchAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);

    public async Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var activity = await _inner
            .GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId)
            .ConfigureAwait(false);
        return activity with
        {
            FundEvents = activity.FundEvents.Concat(_postedFundEvents).ToArray(),
            LedgerImpacts = activity.LedgerImpacts.Concat(_postedLedgerImpacts).ToArray()
        };
    }

    public Task<ManualJournalEntryDraftDto> SaveDraftAsync(
        SaveManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
        => _inner.SaveDraftAsync(request, ct);

    public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
        ValidateManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
        => _inner.ValidateDraftAsync(request, ct);

    public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
        SubmitManualJournalEntryApprovalRequest request,
        CancellationToken ct = default)
        => _inner.SubmitApprovalAsync(request, ct);

    public Task<ManualJournalEntryDraftDto> AttachEvidenceAsync(
        AttachManualJournalEntryEvidenceRequest request,
        CancellationToken ct = default)
        => _inner.AttachEvidenceAsync(request, ct);
}

/// <summary>
/// Shared builders for posted capital-call activity used by the funding intake and endpoint
/// suites: the accounting workspace chart, a posted CapitalCallIssued fund event, and the
/// posted-ledger-shaped impacts (issuance raises the receivable, funding relieves it).
/// </summary>
internal static class CapitalCallFundingTestData
{
    public static AccountingConfigurationWorkspaceDto Workspace(
        string fundProfileId,
        string? tenantId = null,
        string? companyId = null)
        => new(
            fundProfileId,
            LedgerBookId: null,
            AccountingConfigurationStatusDto.Draft,
            "test",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts:
            [
                Node("Assets:Cash", "Cash", "Asset"),
                Node("Assets:Capital Call Receivable", "Capital Call Receivable", "Asset"),
                Node("Equity:Investor Capital", "Investor Capital", "Equity")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [],
            TenantId: tenantId,
            CompanyId: companyId);

    public static PrivateCapitalFundEventDto PostedIssuanceEvent(
        string fundProfileId,
        string callId,
        string commitmentId,
        string investorId,
        decimal amount,
        DateOnly effectiveDate,
        DateTimeOffset updatedAtUtc)
        => new(
            FundEventId(fundProfileId, callId, commitmentId),
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Posted,
            Guid.NewGuid(),
            effectiveDate,
            $"ca-{investorId}",
            investorId,
            "USD",
            amount,
            amount,
            "Posted capital call issuance",
            PaymentIntentId: null,
            SettlementReference: null,
            EvidenceLinks: [],
            ValidationIssues: [],
            UpdatedAtUtc: updatedAtUtc,
            IsPosted: true);

    public static PrivateCapitalLedgerImpactDto PostedIssuanceImpact(
        string fundProfileId,
        string callId,
        string commitmentId,
        string investorId,
        decimal amount,
        DateOnly effectiveDate)
        => PostedImpact(
            fundProfileId, callId, commitmentId, investorId, effectiveDate,
            [
                ("Capital Call Receivable", AccountingTemplateLineSideDto.Debit, amount, investorId),
                ("Investor Capital", AccountingTemplateLineSideDto.Credit, amount, investorId)
            ]);

    public static PrivateCapitalLedgerImpactDto PostedFundingImpact(
        string fundProfileId,
        string callId,
        string commitmentId,
        string investorId,
        decimal amount,
        DateOnly effectiveDate)
        => PostedImpact(
            fundProfileId, callId, commitmentId, investorId, effectiveDate,
            [
                ("Cash", AccountingTemplateLineSideDto.Debit, amount, fundProfileId),
                ("Capital Call Receivable", AccountingTemplateLineSideDto.Credit, amount, investorId)
            ]);

    private static PrivateCapitalLedgerImpactDto PostedImpact(
        string fundProfileId,
        string callId,
        string commitmentId,
        string investorId,
        DateOnly effectiveDate,
        (string AccountName, AccountingTemplateLineSideDto Side, decimal Amount, string EntityId)[] lines)
    {
        var journalEntryId = Guid.NewGuid();
        var totalDebits = lines
            .Where(static line => line.Side == AccountingTemplateLineSideDto.Debit)
            .Sum(static line => line.Amount);
        var totalCredits = lines
            .Where(static line => line.Side == AccountingTemplateLineSideDto.Credit)
            .Sum(static line => line.Amount);
        return new PrivateCapitalLedgerImpactDto(
            $"journal-entry:{journalEntryId:D}",
            journalEntryId,
            FundEventId(fundProfileId, callId, commitmentId),
            "CapitalCall",
            $"ca-{investorId}",
            investorId,
            ManualJournalEntryStatusDto.Approved,
            effectiveDate,
            "USD",
            totalDebits,
            totalCredits,
            totalDebits - totalCredits,
            lines.Length,
            IsBalanced: true,
            IsPostingReady: true,
            EvidenceLinks: [],
            Lines: lines
                .Select((line, index) => new PrivateCapitalLedgerLineImpactDto(
                    $"line-{index + 1}",
                    line.AccountName,
                    line.Side,
                    line.Amount,
                    "USD",
                    line.EntityId,
                    null,
                    null,
                    null))
                .ToArray(),
            ValidationIssues: []);
    }

    private static string FundEventId(string fundProfileId, string callId, string commitmentId)
        => $"fund-event:{fundProfileId}:capital-call:{callId}:{commitmentId}";

    private static ChartOfAccountsNodeDto Node(string path, string name, string type)
        => new(NodeId: path, Path: path, AccountName: name, AccountType: type);
}
