using System;
using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using Meridian.Ledger;

using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Coverage for composing a fund-level capital-call plan into the set of governed issuance drafts
/// (W9-NAV-006): the previously-uncalled <see cref="CapitalCallPlanBuilder"/> and
/// <see cref="CapitalCallDraftFactory"/> kernels are wired into balanced governed drafts ready for
/// the automated-journal approval lifecycle.
/// </summary>
public sealed class CapitalCallScheduleDraftBuilderTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly NoticeDate = new(2026, 3, 15);
    private static readonly DateOnly DueDate = new(2026, 3, 29);

    private static InvestorCommitment Commitment(string commitmentId, string investorId, decimal total)
        => new(
            commitmentId,
            fundProfileId: "fund-a",
            ledgerBookId: null,
            capitalAccountId: $"ca-{investorId}",
            investorId: investorId,
            currency: "USD",
            totalCommitment: total,
            commitmentDate: new DateOnly(2025, 1, 1),
            investmentPeriodEndDate: null,
            status: CommitmentStatus.Active);

    private static CommitmentRollForward FreshRollForward(InvestorCommitment commitment)
        => new(commitment, CumulativeCalled: 0m, CumulativeRecallableRestored: 0m, CumulativeExpired: 0m, Steps: [], ValidationIssues: []);

    private static CapitalCallPlanRequest CallRequest(decimal amountToCall, params CommitmentRollForward[] rollForwards)
        => new("call-1", "fund-a", amountToCall, NoticeDate, DueDate, rollForwards);

    [Fact]
    public void BuildsOneBalancedGovernedDraftPerInvestor_ProRataOverUncalled()
    {
        var lp1 = FreshRollForward(Commitment("cmt-1", "lp-1", 6_000_000m));
        var lp2 = FreshRollForward(Commitment("cmt-2", "lp-2", 4_000_000m));

        var drafts = CapitalCallScheduleDraftBuilder.BuildIssuanceDrafts(
            CallRequest(1_000_000m, lp1, lp2), OccurredAt);

        drafts.Should().HaveCount(2);
        drafts.Should().OnlyContain(draft => draft.IsBalanced);
        drafts.Should().OnlyContain(draft => draft.Event.Kind == AutomatedJournalEventKind.CapitalCallIssued);

        // Deterministic per-investor order and pro-rata-by-uncalled split (60% / 40%).
        var first = drafts[0];
        var second = drafts[1];
        first.Metadata.InvestorId.Should().Be("lp-1");
        first.Event.Amount.Should().Be(600_000m);
        second.Metadata.InvestorId.Should().Be("lp-2");
        second.Event.Amount.Should().Be(400_000m);

        // Each draft debits the LP's capital-call receivable and credits its investor capital.
        first.Lines.Should().SatisfyRespectively(
            line => { line.account.Name.Should().Be("Capital Call Receivable"); line.debit.Should().Be(600_000m); },
            line => { line.account.Name.Should().Be("Investor Capital"); line.credit.Should().Be(600_000m); });

        // The call sum ties exactly to the requested amount.
        drafts.Sum(draft => draft.TotalDebits).Should().Be(1_000_000m);
    }

    [Fact]
    public void DraftsCarryDeterministicIdempotencyKeysAndCapitalAccountIdentity()
    {
        var lp1 = FreshRollForward(Commitment("cmt-1", "lp-1", 6_000_000m));
        var lp2 = FreshRollForward(Commitment("cmt-2", "lp-2", 4_000_000m));
        var request = CallRequest(1_000_000m, lp1, lp2);

        var first = CapitalCallScheduleDraftBuilder.BuildIssuanceDrafts(request, OccurredAt);
        var second = CapitalCallScheduleDraftBuilder.BuildIssuanceDrafts(request, OccurredAt);

        var firstKeys = first.Select(d => d.Metadata.IdempotencyKey).ToArray();
        var secondKeys = second.Select(d => d.Metadata.IdempotencyKey).ToArray();

        firstKeys.Should().Equal("capital-call:cmt-1:call-1:cmt-1", "capital-call:cmt-2:call-1:cmt-2");
        secondKeys.Should().Equal(firstKeys); // stable across runs -> idempotent re-issue

        first[0].Metadata.CapitalAccountId.Should().Be("ca-lp-1");
        first[0].Metadata.FundEventId.Should().Be("fund-event:fund-a:capital-call:call-1:cmt-1");
    }

    [Fact]
    public void AttachesRetainedEvidenceToTheMatchingCommitmentDraft()
    {
        var lp1 = FreshRollForward(Commitment("cmt-1", "lp-1", 6_000_000m));
        var evidence = new JournalEvidenceReference(
            EvidenceId: "call-notice:cmt-1",
            Uri: "/api/private-capital/fund-a/calls/call-1/notice.pdf",
            Kind: "capital-call-notice",
            SourceSystem: "private-capital",
            RetainedAtUtc: OccurredAt,
            RetainedBy: "tester",
            SubjectId: "cmt-1");
        var evidenceByCommitment = new Dictionary<string, IReadOnlyList<JournalEvidenceReference>>
        {
            ["cmt-1"] = [evidence],
        };

        var drafts = CapitalCallScheduleDraftBuilder.BuildIssuanceDrafts(
            CallRequest(500_000m, lp1), OccurredAt, evidenceByCommitment);

        drafts.Should().ContainSingle();
        drafts[0].Metadata.EvidenceReferences.Should().ContainSingle(reference => reference.EvidenceId == "call-notice:cmt-1");
    }

    [Fact]
    public void ThrowsWhenPlanIsNotExecutable_CallExceedsUncalledCapacity()
    {
        var lp1 = FreshRollForward(Commitment("cmt-1", "lp-1", 1_000_000m));

        var act = () => CapitalCallScheduleDraftBuilder.BuildIssuanceDrafts(
            CallRequest(5_000_000m, lp1), OccurredAt);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not executable*");
    }
}
