using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerAccountingCompletenessTests
{
    private static readonly LedgerAccountingScope Scope = new("book-a", "account-a");
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompoundFill_RequiresTradeAndCommissionJournals_NotOnePerLeg()
    {
        var fillId = Guid.NewGuid();
        var report = LedgerAccountingCompleteness.Reconcile(
            [new(Scope, LedgerAccountingSourceKind.Fill, fillId.ToString("D"), ["trade-fill", "commission"])],
            [LedgerAccountingJournalObservation.FromFill(Journal(fillId), Scope, "trade-fill"),
             LedgerAccountingJournalObservation.FromFill(Journal(fillId), Scope, "commission")], true, true);

        report.IsComplete.Should().BeTrue();
        report.SourceEventCount.Should().Be(1);
        report.ExpectedJournalCount.Should().Be(2);
        report.ObservedJournalCount.Should().Be(2);
        report.MatchedJournalCount.Should().Be(2);
    }

    [Fact]
    public void MissingCommission_IsAVisibleBreakEvenWhenTradePosted()
    {
        var fillId = Guid.NewGuid();
        var report = LedgerAccountingCompleteness.Reconcile(
            [new(Scope, LedgerAccountingSourceKind.Fill, fillId.ToString("D"), ["trade-fill", "commission"])],
            [LedgerAccountingJournalObservation.FromFill(Journal(fillId), Scope, "trade-fill")], true, true);

        report.IsComplete.Should().BeFalse();
        report.Issues.Should().ContainSingle().Which.Kind.Should().Be(LedgerAccountingCompletenessIssueKind.MissingJournal);
        report.Issues[0].PostingKey.Should().Be("commission");
    }

    [Fact]
    public void DuplicateOriginatingJournals_DoNotSatisfyExactlyOnce()
    {
        var fillId = Guid.NewGuid();
        var report = LedgerAccountingCompleteness.Reconcile(
            [new(Scope, LedgerAccountingSourceKind.Fill, fillId.ToString("D"), ["trade-fill"])],
            [LedgerAccountingJournalObservation.FromFill(Journal(fillId), Scope, "trade-fill"),
             LedgerAccountingJournalObservation.FromFill(Journal(fillId), Scope, "trade-fill")], true, true);

        report.IsComplete.Should().BeFalse();
        report.MatchedJournalCount.Should().Be(0);
        report.Issues.Should().ContainSingle().Which.JournalEntryIds.Should().HaveCount(2);
        report.Issues[0].Kind.Should().Be(LedgerAccountingCompletenessIssueKind.DuplicateJournal);
    }

    [Fact]
    public void AnotherAccountsJournal_DoesNotHideMissingPosting()
    {
        var fillId = Guid.NewGuid();
        var report = LedgerAccountingCompleteness.Reconcile(
            [new(Scope, LedgerAccountingSourceKind.Fill, fillId.ToString("D"), ["trade-fill"])],
            [LedgerAccountingJournalObservation.FromFill(Journal(fillId), Scope with { FinancialAccountId = "account-b" }, "trade-fill")], true, true);

        report.IsComplete.Should().BeFalse();
        report.Issues.Select(item => item.Kind).Should().BeEquivalentTo(
            [LedgerAccountingCompletenessIssueKind.MissingJournal, LedgerAccountingCompletenessIssueKind.ScopeMismatch]);
    }

    [Fact]
    public void OrphanAndUnlinkedJournals_AreBothReported()
    {
        var report = LedgerAccountingCompleteness.Reconcile([], [
            new(Journal(null), Scope, LedgerAccountingSourceKind.LoanEvent, "orphan-event", "drawdown"),
            LedgerAccountingJournalObservation.FromFill(Journal(null), Scope, "trade-fill")], true, true);

        report.Issues.Select(item => item.Kind).Should().BeEquivalentTo(
            [LedgerAccountingCompletenessIssueKind.UnexpectedJournal, LedgerAccountingCompletenessIssueKind.UnlinkedJournal]);
        report.IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void UnavailableOrTruncatedInputs_CannotReportComplete(bool sourcesComplete, bool journalsComplete)
    {
        var report = LedgerAccountingCompleteness.Reconcile([], [], sourcesComplete, journalsComplete);
        report.Issues.Should().BeEmpty();
        report.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void NoAccountingEffect_IsAnExplicitZeroPostingExpectation()
    {
        var report = LedgerAccountingCompleteness.Reconcile(
            [new(Scope, LedgerAccountingSourceKind.LoanEvent, "loan-renamed", [])], [], true, true);
        report.SourceEventCount.Should().Be(1);
        report.ExpectedJournalCount.Should().Be(0);
        report.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void SameIdAcrossSourceKinds_DoesNotCrossSatisfy()
    {
        var report = LedgerAccountingCompleteness.Reconcile(
            [new(Scope, LedgerAccountingSourceKind.AssetEvent, "shared-id", ["originating"])],
            [new(Journal(null), Scope, LedgerAccountingSourceKind.LoanEvent, "shared-id", "originating")], true, true);
        report.IsComplete.Should().BeFalse();
        report.Issues.Should().HaveCount(2);
    }

    [Fact]
    public void RepeatedQueryRow_RefusesAmbiguousInputInsteadOfDeduplicating()
    {
        var entry = Journal(null);
        var observation = new LedgerAccountingJournalObservation(entry, Scope, LedgerAccountingSourceKind.AssetEvent, "asset-event", "originating");
        var act = () => LedgerAccountingCompleteness.Reconcile([], [observation, observation], true, true);
        act.Should().Throw<LedgerValidationException>().WithMessage("*repeats one retained journal*");
    }

    [Fact]
    public void DuplicateSourceManifest_RefusesAmbiguousCoverage()
    {
        var expectation = new LedgerAccountingExpectation(Scope, LedgerAccountingSourceKind.AssetEvent, "asset-event", ["originating"]);
        var act = () => LedgerAccountingCompleteness.Reconcile([expectation, expectation], [], true, true);
        act.Should().Throw<LedgerValidationException>().WithMessage("*duplicate source event*");
    }

    [Fact]
    public void SourceEventAdapter_PreservesExistingLoanProvenance()
    {
        var entry = Journal(null, new Dictionary<string, string> { ["sourceEventId"] = "retained-loan-event" });
        var observation = LedgerAccountingJournalObservation.FromSourceEvent(entry, Scope, LedgerAccountingSourceKind.LoanEvent, "drawdown");
        observation.SourceId.Should().Be("retained-loan-event");
    }

    [Fact]
    public void UnbalancedRetainedJournal_CannotSatisfyItsSourceObligation()
    {
        var entry = Journal(null);
        var corrupt = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
            [entry.Lines[0]], entry.Metadata);
        var act = () => LedgerAccountingCompleteness.Reconcile(
            [new(Scope, LedgerAccountingSourceKind.LoanEvent, "event-a", ["drawdown"])],
            [new(corrupt, Scope, LedgerAccountingSourceKind.LoanEvent, "event-a", "drawdown")], true, true);

        act.Should().Throw<LedgerValidationException>().WithMessage("*unbalanced retained journal*");
    }

    private static JournalEntry Journal(Guid? fillId, IReadOnlyDictionary<string, string>? tags = null)
    {
        var id = Guid.NewGuid();
        return new(id, Timestamp, "retained journal", [
            new(Guid.NewGuid(), id, Timestamp, LedgerAccounts.Cash, 10m, 0m, "retained journal"),
            new(Guid.NewGuid(), id, Timestamp, LedgerAccounts.CapitalAccount, 0m, 10m, "retained journal")],
            new(FillId: fillId, Tags: tags));
    }
}
