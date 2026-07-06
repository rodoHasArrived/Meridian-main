using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests the ledger spine unification: approved automated journal drafts post durably
/// through ILedgerJournalStore first, with the in-memory projection kept in step.
/// </summary>
public sealed class DurableAutomatedJournalPosterTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 07, 05, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PeriodId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

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

    [Fact]
    public async Task PostAsync_AppendsDurablyThenPostsProjectionAndTransitions()
    {
        var store = new Mock<ILedgerJournalStore>();
        LedgerJournalEntryWrite? captured = null;
        store.Setup(s => s.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
            .Callback<LedgerJournalEntryWrite, CancellationToken>((write, _) => captured = write)
            .Returns(Task.CompletedTask);
        var poster = new DurableAutomatedJournalPoster(store.Object);
        var projection = new Meridian.Ledger.Ledger();
        var approval = ApprovedFeeDraft();

        var posted = await poster.PostAsync(
            approval, PeriodId, "controller", AsOf, "posted", ["/evidence/fees/2026-Q2"], projection);

        posted.Status.Should().Be(AutomatedJournalApprovalStatus.Posted);
        captured.Should().NotBeNull();
        captured!.PeriodId.Should().Be(PeriodId);
        captured.AggregateId.Should().Be(approval.ApprovalId);
        captured.Entry.JournalEntryId.Should().Be(approval.JournalEntryId);
        captured.Entry.Lines.Sum(line => line.Debit).Should().Be(captured.Entry.Lines.Sum(line => line.Credit));

        projection.Journal.Should().ContainSingle(
            "the in-memory projection must stay in step with the durable spine");
        projection.Journal[0].JournalEntryId.Should().Be(captured.Entry.JournalEntryId);
    }

    [Fact]
    public async Task PostAsync_DurableFailure_LeavesApprovalUnposted()
    {
        var store = new Mock<ILedgerJournalStore>();
        store.Setup(s => s.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("wal full"));
        var poster = new DurableAutomatedJournalPoster(store.Object);
        var projection = new Meridian.Ledger.Ledger();
        var approval = ApprovedFeeDraft();

        var act = () => poster.PostAsync(
            approval, PeriodId, "controller", AsOf, "posted", ["/evidence/fees/2026-Q2"], projection);

        await act.Should().ThrowAsync<IOException>();
        approval.Status.Should().Be(AutomatedJournalApprovalStatus.Approved,
            "a storage failure must not mark the approval posted");
        projection.Journal.Should().BeEmpty("the projection must not diverge from the durable spine");
    }

    [Fact]
    public async Task PostAsync_UnapprovedDraft_ThrowsBeforeAppending()
    {
        var store = new Mock<ILedgerJournalStore>();
        var poster = new DurableAutomatedJournalPoster(store.Object);
        var draft = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.ManagementFeeAccrued, "FUND-ALPHA", 100m, AsOf));
        var submittedOnly = AutomatedJournalApproval.Submit(draft, "automation", AsOf, "fee accrual");

        var act = () => poster.PostAsync(
            submittedOnly, PeriodId, "controller", AsOf, "posted", ["/evidence"], projectionLedger: null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        store.Verify(s => s.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
