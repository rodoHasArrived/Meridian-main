using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Application.AccountingClose;
using Xunit;

namespace Meridian.Tests;

public sealed class AccountingCloseServicesTests
{
    [Fact]
    public void FxTranslation_ComputesAdjustmentDeterministically()
    {
        var service = new FxTranslationService();
        var rate = new FxRate("EUR", "USD", new DateOnly(2026, 03, 31), 1.10m, "fx:ecb:20260331");

        var result = service.Translate("ledger-a", new DateOnly(2026, 03, 31), "Cash", 100m, rate);

        result.ReportingAmount.Should().Be(110m);
        result.AdjustmentAmount.Should().Be(10m);
        result.SourceEventId.Should().Be("fx:ecb:20260331");
    }

    [Fact]
    public void TrialBalance_DetectsOutOfBalance()
    {
        var projection = new TrialBalanceProjectionService();
        var entries = ImmutableArray.Create(
            new JournalEntry(Guid.NewGuid(), "ledger-a", new DateOnly(2026, 03, 31), "evt-1", "accrual", ImmutableArray.Create(new JournalLine("Cash", 100m, "USD", true))),
            new JournalEntry(Guid.NewGuid(), "ledger-a", new DateOnly(2026, 03, 31), "evt-2", "offset", ImmutableArray.Create(new JournalLine("Revenue", 90m, "USD", false))));

        var trial = projection.BuildTrialBalance(entries);

        projection.IsBalanced(trial).Should().BeFalse();
    }

    [Fact]
    public void CloseStateMachine_BlocksWhenEvidenceMissing()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Open, new CloseEvidence(false, false, false), []);

        var next = stateMachine.Transition(current, new CloseEvidence(true, false, true), isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Validating);
        next.Blockers.Should().ContainSingle().Which.Should().Contain("Reconciliation");
    }

    [Fact]
    public void CloseStateMachine_ClosesWhenAllGatesPass()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(true, true, true), []);

        var next = stateMachine.Transition(current, new CloseEvidence(true, true, true), isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Closed);
        next.Blockers.Should().BeEmpty();
    }

    [Fact]
    public void PostingReplay_RemainsDeterministicByDateThenId()
    {
        var posting = new AccountingPostingService();
        var idB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var idA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        posting.Post("ledger-a", [
            new JournalEntry(idB, "ledger-a", new DateOnly(2026, 03, 02), "evt-b", "b", ImmutableArray.Create(new JournalLine("Cash", 1m, "USD", true))),
            new JournalEntry(idA, "ledger-a", new DateOnly(2026, 03, 02), "evt-a", "a", ImmutableArray.Create(new JournalLine("Cash", 1m, "USD", true)))
        ]);

        var replay = posting.Replay("ledger-a");
        replay[0].JournalEntryId.Should().Be(idA);
        replay[1].JournalEntryId.Should().Be(idB);
    }
}
