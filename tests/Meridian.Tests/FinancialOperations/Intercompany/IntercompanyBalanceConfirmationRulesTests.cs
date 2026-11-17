using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;

namespace Meridian.Tests.FinancialOperations.Intercompany;

/// <summary>
/// Month-end reciprocal confirmation of an intercompany loan: the lender's book holds a 1,000,000 receivable and the
/// borrower's book the matching payable. Each side signs off once, independently, on exactly the pinned balances.
/// </summary>
public sealed class IntercompanyBalanceConfirmationRulesTests
{
    private static readonly Guid Arrangement = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid Agreement = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly DateOnly MonthEnd = new(2026, 6, 30);
    private static readonly DateTimeOffset At = new(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BothSidesConfirmingTheSamePinnedBalances_ByDifferentPeople_IsConfirmed()
    {
        var proposed = Propose();
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Proposed, proposed.Status);
        Assert.Equal(0m, proposed.NetTransactionBalance);

        var lender = Act(proposed, IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, "lender-controller");
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne, lender.Status);
        var both = Act(lender, IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyTwo, "borrower-controller");

        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Confirmed, both.Status);
        Assert.Equal(3, both.Version);
        Assert.All(both.Events, item => Assert.Equal(proposed.BalanceDigest, item.BalanceDigest));
        Assert.Null(IntercompanyBalanceConfirmationRules.Finding(Arrangement, both, proposed.BalanceDigest));
    }

    [Fact]
    public void ThePersonWhoConfirmedOneSide_CannotConfirmTheOther()
    {
        var lender = Act(Propose(), IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, "controller");

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            Act(lender, IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyTwo, "CONTROLLER"));
        Assert.Contains("same person cannot confirm both sides", refusal.Message);
    }

    [Fact]
    public void BalancesThatDoNotNetToZero_CannotBeConfirmed_ButCanBeDisputed()
    {
        // The borrower booked 999,000: the pair is out by 1,000.
        var proposed = Propose(borrower: -999_000m);
        Assert.Equal(1_000m, proposed.NetTransactionBalance);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            Act(proposed, IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, "lender-controller"));
        Assert.Contains("do not net to zero", refusal.Message);

        var disputed = Act(proposed, IntercompanyBalanceConfirmationActionDto.Dispute, IntercompanyBalanceConfirmationPartyDto.PartyOne,
            "lender-controller", "Borrower is short 1,000 of accrued interest.");
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Disputed, disputed.Status);
        Assert.Equal("Borrower is short 1,000 of accrued interest.", disputed.Events[^1].Reason);
        Assert.Equal("Disputed", IntercompanyBalanceConfirmationRules.Finding(Arrangement, disputed, disputed.BalanceDigest)!.Code);
    }

    [Fact]
    public void ADispute_NeedsAReason_AndIsResolvedOnlyBySomeoneElseWithAReason()
    {
        var proposed = Propose(borrower: -999_000m);
        Assert.Throws<InvalidOperationException>(() => Act(proposed, IntercompanyBalanceConfirmationActionDto.Dispute,
            IntercompanyBalanceConfirmationPartyDto.PartyTwo, "borrower-controller", "  "));
        var disputed = Act(proposed, IntercompanyBalanceConfirmationActionDto.Dispute, IntercompanyBalanceConfirmationPartyDto.PartyTwo,
            "borrower-controller", "Interest accrual not yet posted.");

        Assert.Contains("cannot also resolve", Assert.Throws<InvalidOperationException>(() =>
            Act(disputed, IntercompanyBalanceConfirmationActionDto.Resolve, null, "borrower-controller", "Posted.")).Message);
        Assert.Contains("needs a reason", Assert.Throws<InvalidOperationException>(() =>
            Act(disputed, IntercompanyBalanceConfirmationActionDto.Resolve, null, "group-controller")).Message);

        var resolved = Act(disputed, IntercompanyBalanceConfirmationActionDto.Resolve, null, "group-controller",
            "Borrower posted the missing accrual on 2 July.");
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Resolved, resolved.Status);
        Assert.Null(resolved.Events[^1].Party);
        Assert.Equal("ResolvedAwaitingProposal", IntercompanyBalanceConfirmationRules.Finding(Arrangement, resolved, "anything")!.Code);
    }

    [Fact]
    public void AnUnresolvedDispute_BlocksAFreshProposal_ButAResolvedOneAllowsIt()
    {
        var disputed = Act(Propose(borrower: -999_000m), IntercompanyBalanceConfirmationActionDto.Dispute,
            IntercompanyBalanceConfirmationPartyDto.PartyOne, "lender-controller", "Short by 1,000.");
        Assert.Contains("disputed", Assert.Throws<InvalidOperationException>(() => Propose(current: disputed)).Message);

        var resolved = Act(disputed, IntercompanyBalanceConfirmationActionDto.Resolve, null, "group-controller", "Correction posted.");
        var corrected = Propose(current: resolved);

        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Proposed, corrected.Status);
        Assert.Equal(disputed.ConfirmationId, corrected.ConfirmationId);
        Assert.Equal(resolved.Version + 1, corrected.Version);
        Assert.Equal(0m, corrected.NetTransactionBalance);
    }

    [Fact]
    public void BalancesThatMovedAfterPinning_ReportChanged_AndAFreshProposalVoidsEarlierSignOffs()
    {
        var lender = Act(Propose(), IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, "lender-controller");
        var moved = Digest(lender: 1_004_166.67m, borrower: -1_004_166.67m);

        Assert.Equal("BalancesChanged", IntercompanyBalanceConfirmationRules.Finding(Arrangement, lender, moved)!.Code);
        var refreshed = Propose(current: lender, lender: 1_004_166.67m, borrower: -1_004_166.67m);
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Proposed, refreshed.Status);
        Assert.Equal(moved, refreshed.BalanceDigest);
        // The earlier lender sign-off does not carry over: the same person may sign the new balances again.
        var again = Act(refreshed, IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, "lender-controller");
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne, again.Status);
    }

    [Fact]
    public void ProposingTheSameUnchangedBalancesAgain_IsRefused()
        => Assert.Contains("already proposed", Assert.Throws<InvalidOperationException>(() => Propose(current: Propose())).Message);

    [Fact]
    public void AnActionAgainstAStaleVersionOrDigest_IsRefused()
    {
        var proposed = Propose();
        Assert.Contains("changed since it was shown", Assert.Throws<InvalidOperationException>(() => IntercompanyBalanceConfirmationRules.Apply(proposed,
            new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne,
                proposed.Version + 1, proposed.BalanceDigest, null), "lender-controller", At)).Message);
        Assert.Contains("differ from the ones shown", Assert.Throws<InvalidOperationException>(() => IntercompanyBalanceConfirmationRules.Apply(proposed,
            new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne,
                proposed.Version, new string('0', 64), null), "lender-controller", At)).Message);
    }

    [Fact]
    public void AFullyConfirmedRecord_CannotBeDisputedOrConfirmedAgain()
    {
        var lender = Act(Propose(), IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, "lender-controller");
        Assert.Contains("already confirmed", Assert.Throws<InvalidOperationException>(() =>
            Act(lender, IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyOne, "lender-deputy")).Message);
        var both = Act(lender, IntercompanyBalanceConfirmationActionDto.Confirm, IntercompanyBalanceConfirmationPartyDto.PartyTwo, "borrower-controller");

        Assert.Throws<InvalidOperationException>(() => Act(both, IntercompanyBalanceConfirmationActionDto.Dispute,
            IntercompanyBalanceConfirmationPartyDto.PartyTwo, "borrower-controller", "Second thoughts."));
    }

    [Fact]
    public void NoProposal_IsReportedAsNotProposed()
        => Assert.Equal("NotProposed", IntercompanyBalanceConfirmationRules.Finding(Arrangement, null, "digest")!.Code);

    [Fact]
    public void TheDigestCoversAmountsAndContributingJournals_ButNotOtherAgreementsInTheSchedule()
    {
        var journal = Guid.NewGuid();
        var baseline = IntercompanyBalanceConfirmationRules.Digest(Arrangement, MonthEnd, Side(1m, [journal]), Side(-1m, [journal]));
        Assert.NotEqual(baseline, IntercompanyBalanceConfirmationRules.Digest(Arrangement, MonthEnd, Side(1m, [journal, Guid.NewGuid()]), Side(-1m, [journal])));
        Assert.NotEqual(baseline, IntercompanyBalanceConfirmationRules.Digest(Arrangement, MonthEnd, Side(2m, [journal]), Side(-1m, [journal])));
        // A schedule hash change caused by another agreement in the same book does not unpin these balances.
        Assert.Equal(baseline, IntercompanyBalanceConfirmationRules.Digest(Arrangement, MonthEnd,
            Side(1m, [journal]) with { ScheduleContentHash = "other-hash" }, Side(-1m, [journal])));

        static IntercompanyBalanceConfirmationSideDto Side(decimal amount, IReadOnlyList<Guid> journals)
            => new(Guid.Empty, Guid.Empty, "USD", amount, "USD", amount, "hash", journals);
    }

    private static IntercompanyBalanceConfirmationDto Propose(IntercompanyBalanceConfirmationDto? current = null,
        decimal lender = 1_000_000m, decimal borrower = -1_000_000m)
        => IntercompanyBalanceConfirmationRules.Propose(current, Guid.NewGuid(), Guid.NewGuid(), "tenant-a", "company-a", Arrangement, Agreement,
            MonthEnd, Lender(lender), Borrower(borrower), "accounting-operations", At);

    private static string Digest(decimal lender, decimal borrower)
        => IntercompanyBalanceConfirmationRules.Digest(Arrangement, MonthEnd, Lender(lender), Borrower(borrower));

    private static IntercompanyBalanceConfirmationSideDto Lender(decimal amount)
        => new(Guid.Parse("a1111111-1111-1111-1111-111111111111"), Guid.Parse("a2222222-2222-2222-2222-222222222222"), "USD", amount, "USD", amount,
            "lender-schedule", [Guid.Parse("a3333333-3333-3333-3333-333333333333")]);

    private static IntercompanyBalanceConfirmationSideDto Borrower(decimal amount)
        => new(Guid.Parse("b1111111-1111-1111-1111-111111111111"), Guid.Parse("b2222222-2222-2222-2222-222222222222"), "USD", amount, "USD", amount,
            "borrower-schedule", [Guid.Parse("b3333333-3333-3333-3333-333333333333")]);

    private static IntercompanyBalanceConfirmationDto Act(IntercompanyBalanceConfirmationDto current, IntercompanyBalanceConfirmationActionDto action,
        IntercompanyBalanceConfirmationPartyDto? party, string actor, string? reason = null)
        => IntercompanyBalanceConfirmationRules.Apply(current, new(Guid.NewGuid(), action, party, current.Version, current.BalanceDigest, reason), actor, At);
}
