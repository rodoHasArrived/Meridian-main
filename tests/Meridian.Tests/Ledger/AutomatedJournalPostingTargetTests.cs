using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class AutomatedJournalPostingTargetTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 07, 05, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PeriodId = Guid.Parse("f2247cea-21d1-4da9-9dd0-63890e874f54");

    [Fact]
    public async Task InMemoryTarget_PostsApprovedProjectionThroughSharedContract()
    {
        var ledger = new Meridian.Ledger.Ledger();
        IAutomatedJournalPostingTarget target = new InMemoryAutomatedJournalPostingTarget(ledger);
        var approval = ApprovedFeeDraft();

        var posted = await target.PostAsync(
            approval,
            new AutomatedJournalPostingContext(
                PeriodId,
                "controller",
                AsOf,
                "approved for backtest",
                ["/evidence/fees/2026-Q2"],
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                1));

        posted.Status.Should().Be(AutomatedJournalApprovalStatus.Posted);
        ledger.Journal.Should().ContainSingle()
            .Which.JournalEntryId.Should().Be(approval.JournalEntryId);
    }

    [Fact]
    public void PostingContext_RequiresGovernedPostingScopeAndEvidence()
    {
        var act = () => new AutomatedJournalPostingContext(
            Guid.Empty,
            "controller",
            AsOf,
            "missing period",
            ["/evidence/fees/2026-Q2"],
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Period id is required*");
    }

    [Fact]
    public void PostedApproval_CannotBeConvertedBackIntoAnAppendableJournalEntry()
    {
        var approval = ApprovedFeeDraft();
        var posted = approval.PostTo(
            new Meridian.Ledger.Ledger(),
            "controller",
            AsOf,
            "posted",
            ["/evidence/fees/2026-Q2"]);

        Action act = () => posted.ToJournalEntry();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Only approved automated journal drafts can be converted*");
    }

    private static AutomatedJournalApproval ApprovedFeeDraft()
    {
        var draft = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.ManagementFeeAccrued,
            "FUND-ALPHA",
            Amount: 1_200m,
            AsOf,
            IdempotencyKey: "mgmt-fee|fund-alpha|2026-Q2"));

        return AutomatedJournalApproval
            .Submit(draft, "automation", AsOf, "fee accrual")
            .Approve("controller", AsOf, "reviewed", ["/evidence/fees/2026-Q2"]);
    }
}
