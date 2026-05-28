using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Application.AccountingClose;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Guards month-end accounting close scenarios where FX rates, posting balance, source-event lineage,
/// period locks, and evidence gates must remain deterministic for operator replay.
/// </summary>
public sealed class AccountingCloseServicesTests
{
    [Fact]
    public void Scenario_MonthEndFxTranslation_ReplayUsesStableAdjustmentIdAndRateLineage()
    {
        var service = new FxTranslationService();
        var rate = new FxRate("EUR", "USD", new DateOnly(2026, 03, 31), 1.10m, "fx:ecb:20260331", "ECB-EURUSD-20260331");

        var first = service.Translate("ledger-a", new DateOnly(2026, 03, 31), "Cash", 100m, rate);
        var replay = service.Translate("ledger-a", new DateOnly(2026, 03, 31), "Cash", 100m, rate);

        first.ReportingAmount.Should().Be(110m);
        first.AdjustmentAmount.Should().Be(10m);
        first.SourceEventId.Should().Be("fx:ecb:20260331");
        first.RateId.Should().Be("ECB-EURUSD-20260331");
        replay.AdjustmentId.Should().Be(first.AdjustmentId);
    }

    [Fact]
    public void Scenario_MonthEndTrialBalance_OutOfBalanceActivityBlocksCloseEvidence()
    {
        var projection = new TrialBalanceProjectionService();
        var entries = ImmutableArray.Create(
            NewEntry("evt-1", "approval-1", new JournalLine("Cash", 100m, "USD", true, "evt-1", "approval-1")),
            NewEntry("evt-2", "approval-2", new JournalLine("Revenue", 90m, "USD", false, "evt-2", "approval-2")));

        var trial = projection.BuildTrialBalance(entries);
        var stateMachine = new MonthEndCloseStateMachine();
        var validating = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(true, true, true), []);

        var next = stateMachine.Transition(validating, new CloseEvidence(true, true, true), projection.IsBalanced(trial));

        projection.IsBalanced(trial).Should().BeFalse();
        next.State.Should().Be(ClosePeriodState.Blocked);
        next.Blockers.Should().Contain("Trial balance is out of balance.");
    }

    [Fact]
    public void Scenario_MonthEndPosting_OutOfBalanceJournalIsRejectedBeforeReplay()
    {
        var posting = new AccountingPostingService();
        var entry = NewEntry("evt-oob", "approval-oob", new JournalLine("Cash", 100m, "USD", true, "evt-oob", "approval-oob"));

        var result = posting.PostWithResult("ledger-a", [entry]);

        result.Accepted.Should().BeFalse();
        result.RejectedReasons.Should().ContainSingle(reason => reason.Contains("out of balance", StringComparison.OrdinalIgnoreCase));
        posting.Replay("ledger-a").Should().BeEmpty();
    }

    [Fact]
    public void Scenario_MonthEndPosting_ClosedPeriodRejectsLateJournal()
    {
        var posting = new AccountingPostingService();
        var lockedPeriod = new ClosePeriod(
            "ledger-a",
            new DateOnly(2026, 03, 01),
            ClosePeriodState.Closed,
            new CloseEvidence(true, true, true),
            [],
            DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
            "controller");

        var result = posting.PostWithResult("ledger-a", [BalancedEntry("evt-late", "approval-late")], lockedPeriod);

        result.Accepted.Should().BeFalse();
        result.RejectedReasons.Should().ContainSingle(reason => reason.Contains("locked", StringComparison.OrdinalIgnoreCase));
        posting.Replay("ledger-a").Should().BeEmpty();
    }

    [Fact]
    public void Scenario_MonthEndClose_EvidenceChecksGateClosedState()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(false, false, false), []);
        var evidence = new CloseEvidence(
            TrialBalanceSignedOff: true,
            ReconciliationSignedOff: true,
            ApprovalsCompleted: true,
            Checks: ImmutableArray.Create(new CloseEvidenceCheck("packet", "Controller packet", true, false, "evt-close", "approval-close", "Controller approval is pending.")));

        var next = stateMachine.Transition(current, evidence, isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Blocked);
        next.Blockers.Should().ContainSingle(reason => reason.Contains("Controller packet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scenario_MonthEndClose_AllGatesPassingLocksThePeriod()
    {
        var stateMachine = new MonthEndCloseStateMachine();
        var current = new ClosePeriod("ledger-a", new DateOnly(2026, 03, 31), ClosePeriodState.Validating, new CloseEvidence(true, true, true), []);

        var next = stateMachine.Transition(current, new CloseEvidence(true, true, true), isTrialBalanceBalanced: true);

        next.State.Should().Be(ClosePeriodState.Closed);
        next.Blockers.Should().BeEmpty();
        next.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Scenario_MonthEndPosting_ReplayOrdersByPeriodDateAndJournalIdWithAuditLineage()
    {
        var posting = new AccountingPostingService();
        var idB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var idA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        posting.Post("ledger-a", [
            BalancedEntry("evt-b", "approval-b", idB),
            BalancedEntry("evt-a", "approval-a", idA)
        ]);

        var replay = posting.Replay("ledger-a");
        var audit = posting.Audit("ledger-a");
        replay[0].JournalEntryId.Should().Be(idA);
        replay[1].JournalEntryId.Should().Be(idB);
        audit[0].SourceEventId.Should().Be("evt-a");
        audit[0].ApprovalId.Should().Be("approval-a");
        audit[0].AccountCodes.Should().BeEquivalentTo("Cash", "Revenue");
    }

    private static JournalEntry BalancedEntry(string sourceEventId, string approvalId, Guid? journalEntryId = null)
        => new(
            journalEntryId ?? Guid.NewGuid(),
            "ledger-a",
            new DateOnly(2026, 03, 02),
            sourceEventId,
            "balanced accrual",
            ImmutableArray.Create(
                new JournalLine("Cash", 100m, "USD", true, sourceEventId, approvalId),
                new JournalLine("Revenue", 100m, "USD", false, sourceEventId, approvalId)));

    private static JournalEntry NewEntry(string sourceEventId, string approvalId, params JournalLine[] lines)
        => new(
            Guid.NewGuid(),
            "ledger-a",
            new DateOnly(2026, 03, 31),
            sourceEventId,
            "month-end source event",
            lines.Select(line => string.IsNullOrWhiteSpace(line.ApprovalId) ? line with { ApprovalId = approvalId } : line).ToImmutableArray());
}
