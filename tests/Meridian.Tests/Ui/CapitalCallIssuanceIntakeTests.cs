using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Coverage for the first production consumer of the fund-economics capital-call kernel
/// (W9-NAV-006): <see cref="AutomatedJournalIntakeRunner.RunCapitalCallIssuanceIntakeAsync"/>
/// plans a fund-level call over the attested commitment register, corroborates the uncalled
/// basis against posted private-capital activity, and lands per-LP issuance drafts in the
/// manual journal approval queue — never posting, and refusing with reasons whenever the
/// tie-out cannot be corroborated.
/// </summary>
public sealed class CapitalCallIssuanceIntakeTests
{
    private const string FundProfileId = "fund-alpha";
    private static readonly Guid BookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset AsOf = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly NoticeDate = new(2026, 3, 15);
    private static readonly DateOnly DueDate = new(2026, 3, 29);

    [Fact]
    public async Task RunCapitalCallIssuanceIntakeAsync_PlansProRataDraftsIntoApprovalQueue_NotPosted()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Runner.RunCapitalCallIssuanceIntakeAsync(BuildRequest(
            amountToCall: 1_000_000m,
            Commitment("cmt-1", "lp-1", 6_000_000m),
            Commitment("cmt-2", "lp-2", 4_000_000m)));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Ready);
        result.ReadinessBlockers.Should().BeEmpty();
        result.Intake.Skipped.Should().BeEmpty();
        result.Intake.Created.Should().HaveCount(2);
        result.Intake.Created.Should().OnlyContain(
            draft => draft.Status == ManualJournalEntryStatusDto.Draft,
            "issuance drafts must land in the human approval queue, never post directly");
        result.Intake.Created.Should().OnlyContain(
            draft => draft.EntryType == ManualJournalEntryTypeDto.CapitalCall);
        result.Intake.Created.Should().OnlyContain(
            draft => draft.TotalDebits == draft.TotalCredits && draft.TotalDebits > 0m);

        // Kernel golden expectation: pro-rata by uncalled → 60% / 40% of the requested call.
        var first = result.Intake.Created.Single(draft => draft.TreasuryContext!.InvestorId == "lp-1");
        var second = result.Intake.Created.Single(draft => draft.TreasuryContext!.InvestorId == "lp-2");
        first.TotalDebits.Should().Be(600_000m);
        second.TotalDebits.Should().Be(400_000m);
        result.Intake.Created.Sum(draft => draft.TotalDebits).Should().Be(1_000_000m);

        first.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Debit &&
            line.AccountPath == "Assets:Capital Call Receivable" &&
            line.Amount == 600_000m);
        first.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Credit &&
            line.AccountPath == "Equity:Investor Capital" &&
            line.Amount == 600_000m);

        // Fund-event identity survives intake so the projector can reconstruct the call and the
        // roll-forward can corroborate the next one after posting.
        first.TreasuryContext!.FundEventId.Should().Be("fund-event:fund-alpha:capital-call:call-1:cmt-1");
        first.TreasuryContext.FundEventType.Should().Be("CapitalCall");
        first.TreasuryContext.CapitalAccountId.Should().Be("ca-lp-1");
        first.TreasuryContext.IdempotencyKey.Should().Be("capital-call:cmt-1:call-1:cmt-1");
        first.EvidenceLinks.Should().Contain("evidence://commitments/cmt-1/subscription-agreement");

        // Honesty grading: a first call has no posted history, so the assessment says the
        // attested register is the sole basis instead of overstating corroboration.
        var assessment = result.EvidenceAssessments["capital-call:cmt-1:call-1:cmt-1"];
        assessment.RequiresInvestigation.Should().BeFalse();
        assessment.ConfidenceScore.Should().Be(0.80m);
        assessment.Quality.Should().Be(AutomatedJournalEvidenceQualityDto.Medium);
        assessment.Reasons.Should().ContainSingle(reason => reason.Contains("sole basis"));

        var workbench = await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId);
        workbench.Drafts.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunCapitalCallIssuanceIntakeAsync_SecondRun_SkipsExistingDraftsInsteadOfDuplicating()
    {
        var fixture = await CreateFixtureAsync();
        var request = BuildRequest(
            amountToCall: 1_000_000m,
            Commitment("cmt-1", "lp-1", 6_000_000m),
            Commitment("cmt-2", "lp-2", 4_000_000m));

        var firstRun = await fixture.Runner.RunCapitalCallIssuanceIntakeAsync(request);
        var secondRun = await fixture.Runner.RunCapitalCallIssuanceIntakeAsync(request);

        firstRun.Intake.Created.Should().HaveCount(2);
        secondRun.Intake.Created.Should().BeEmpty("a re-run must never duplicate governed drafts");
        secondRun.Intake.Skipped.Should().HaveCount(2);
        secondRun.Intake.Skipped.Should().OnlyContain(skip => skip.IsReadyDuplicate);
    }

    [Fact]
    public async Task RunCapitalCallIssuanceIntakeAsync_MissingCommitmentEvidence_BlocksWithoutDrafting()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Runner.RunCapitalCallIssuanceIntakeAsync(BuildRequest(
            amountToCall: 500_000m,
            Commitment("cmt-1", "lp-1", 6_000_000m) with { EvidenceLinks = [] }));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        result.ReadinessBlockers.Should().ContainSingle(blocker =>
            blocker.Contains("cmt-1") && blocker.Contains("commitment-register evidence"));
        result.Intake.Created.Should().BeEmpty("uncorroborated register lines must refuse, not draft wrong numbers");
        result.EvidenceAssessments.Should().ContainKey("capital-call|fund-alpha|call-1")
            .WhoseValue.RequiresInvestigation.Should().BeTrue();

        var workbench = await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId);
        workbench.Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCapitalCallIssuanceIntakeAsync_WithoutPostedActivitySource_BlocksWithoutDrafting()
    {
        var fixture = await CreateFixtureAsync(includeWorkbenchSource: false);

        var result = await fixture.Runner.RunCapitalCallIssuanceIntakeAsync(BuildRequest(
            amountToCall: 500_000m,
            Commitment("cmt-1", "lp-1", 6_000_000m)));

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        result.ReadinessBlockers.Should().ContainSingle(blocker =>
            blocker.Contains("posted private-capital activity source is unavailable"));
        result.Intake.Created.Should().BeEmpty();
    }

    [Fact]
    public void Produce_OverCallAgainstPostedActivity_FailsClosed()
    {
        // The ledger already carries a posted 800k call against this 1M commitment, so only 200k
        // remains callable. The caller cannot override that basis: a 500k request is refused with
        // the kernel's own capacity reason.
        var request = BuildRequest(amountToCall: 500_000m, Commitment("cmt-1", "lp-1", 1_000_000m));

        var production = CapitalCallIssuanceDraftProducer.Produce(
            request,
            [PostedCapitalCall("prior-call", "ca-lp-1", "lp-1", 800_000m)],
            AsOf);

        production.IsReady.Should().BeFalse();
        production.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        production.Blockers.Should().ContainSingle(blocker => blocker.Contains("exceeds total uncalled capacity"));
        production.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void Produce_PostedActivityReducesUncalled_AllocatesAgainstRemainingCapacity()
    {
        // lp-1 has 800k of 1M already called (posted), lp-2 is fresh: remaining capacity is
        // 200k / 1,000k, so a 300k call splits 50k / 250k pro-rata by uncalled.
        var request = BuildRequest(
            amountToCall: 300_000m,
            Commitment("cmt-1", "lp-1", 1_000_000m),
            Commitment("cmt-2", "lp-2", 1_000_000m));

        var production = CapitalCallIssuanceDraftProducer.Produce(
            request,
            [PostedCapitalCall("prior-call", "ca-lp-1", "lp-1", 800_000m)],
            AsOf);

        production.IsReady.Should().BeTrue();
        production.Drafts.Should().HaveCount(2);
        var first = production.Drafts.Single(draft => draft.Metadata.InvestorId == "lp-1");
        var second = production.Drafts.Single(draft => draft.Metadata.InvestorId == "lp-2");
        first.Event.Amount.Should().Be(50_000m);
        second.Event.Amount.Should().Be(250_000m);
        production.Drafts.Sum(draft => draft.TotalDebits).Should().Be(300_000m);

        // The ledger-corroborated line grades High; the history-less line grades Medium.
        production.EvidenceAssessments[first.Metadata.IdempotencyKey!].Quality
            .Should().Be(AutomatedJournalEvidenceQualityDto.High);
        production.EvidenceAssessments[second.Metadata.IdempotencyKey!].Quality
            .Should().Be(AutomatedJournalEvidenceQualityDto.Medium);
    }

    // -------------------------------------------------------------------------
    // Fixture
    // -------------------------------------------------------------------------

    private sealed record Fixture(
        AutomatedJournalIntakeRunner Runner,
        IManualJournalEntryWorkbenchService Workbench);

    private static async Task<Fixture> CreateFixtureAsync(bool includeWorkbenchSource = true)
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(
            FundProfileId,
            LedgerBookId: null,
            AccountingConfigurationStatusDto.Draft,
            "test",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts:
            [
                Node("Assets:Capital Call Receivable", "Capital Call Receivable", "Asset"),
                Node("Equity:Investor Capital", "Investor Capital", "Equity")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: []));
        var configurationService = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            new InMemoryAccountingActionAuditStore());
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService);
        var runner = new AutomatedJournalIntakeRunner(
            intake,
            new FeeScheduleAccrualEventProducer(),
            manualJournalWorkbench: includeWorkbenchSource ? workbench : null);
        return new Fixture(runner, workbench);
    }

    private static ChartOfAccountsNodeDto Node(string path, string name, string type)
        => new(NodeId: path, Path: path, AccountName: name, AccountType: type);

    private static CapitalCallCommitmentInput Commitment(string commitmentId, string investorId, decimal total)
        => new(
            commitmentId,
            CapitalAccountId: $"ca-{investorId}",
            InvestorId: investorId,
            TotalCommitment: total,
            CommitmentDate: new DateOnly(2025, 1, 1),
            EvidenceLinks: [$"evidence://commitments/{commitmentId}/subscription-agreement"]);

    private static RunCapitalCallIssuanceDraftIntakeRequest BuildRequest(
        decimal amountToCall,
        params CapitalCallCommitmentInput[] commitments)
        => new(
            FundProfileId,
            Currency: "USD",
            Actor: "fund-controller",
            CallId: "call-1",
            AmountToCall: amountToCall,
            NoticeDate: NoticeDate,
            DueDate: DueDate,
            Commitments: commitments,
            LedgerBookId: BookId,
            PeriodId: "2026-03",
            EntityId: "entity-alpha",
            AsOf: AsOf);

    private static PrivateCapitalFundEventDto PostedCapitalCall(
        string fundEventId,
        string capitalAccountId,
        string investorId,
        decimal amount)
        => new(
            fundEventId,
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Posted,
            Guid.NewGuid(),
            new DateOnly(2025, 6, 1),
            capitalAccountId,
            investorId,
            "USD",
            amount,
            amount,
            "Posted prior capital call",
            PaymentIntentId: null,
            SettlementReference: null,
            EvidenceLinks: [],
            ValidationIssues: [],
            UpdatedAtUtc: AsOf,
            IsPosted: true);
}
