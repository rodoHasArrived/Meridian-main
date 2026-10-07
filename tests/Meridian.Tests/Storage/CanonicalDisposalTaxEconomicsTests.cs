using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

[Trait("Category", "Unit")]
public sealed class CanonicalDisposalTaxEconomicsTests
{
    [Fact]
    public void DeferredDisposal_MultipleExactCashLegsReconcileToOneRetainedResult()
    {
        var (lot, history, journal) = CreateDeferredDisposal();

        var result = CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        result.Proceeds.Should().Be(80m);
        result.RealizedGainOrLoss.Should().Be(-20m);
        result.DisallowedWashSaleLoss.Should().Be(20m);
        result.RecognizedGainOrLoss.Should().Be(0m);
    }

    [Fact]
    public void DeferredDisposal_AdditionalSourceCreditCannotBeAttributedToThisDisposal()
    {
        var (lot, history, journal) = CreateDeferredDisposal();
        var dimensions = journal.Lines[0].Dimensions;
        journal = new(journal.JournalEntryId, journal.Timestamp, journal.Description,
            journal.Lines.Concat([
                new LedgerEntry(Guid.NewGuid(), journal.JournalEntryId, journal.Timestamp, LedgerAccounts.Cash,
                    10m, 0m, journal.Description, dimensions),
                new LedgerEntry(Guid.NewGuid(), journal.JournalEntryId, journal.Timestamp,
                    new LedgerAccount("Other disposal", LedgerAccountType.Asset),
                    0m, 10m, journal.Description, dimensions)
            ]).ToArray(), journal.Metadata);

        var act = () => CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        act.Should().Throw<LedgerValidationException>().WithMessage("*supported cash, asset-basis, and realized-result*");
    }

    [Theory]
    [InlineData("account")]
    [InlineData("position")]
    [InlineData("security")]
    [InlineData("missing-recipient")]
    [InlineData("recipient-amount")]
    [InlineData("recipient-lot")]
    [InlineData("recipient-source")]
    public void DeferredDisposal_RequiresExactRecipientEvidenceAndJournalBasis(string mismatch)
    {
        var (lot, history, journal) = CreateDeferredDisposal();
        var recipient = history.DeferralRecipients!.Single();
        if (mismatch is "account" or "position" or "security")
        {
            journal = new(journal.JournalEntryId, journal.Timestamp, journal.Description,
                journal.Lines.Select(line => line.Account == recipient.Account
                    ? new LedgerEntry(line.EntryId, journal.JournalEntryId, journal.Timestamp,
                        mismatch == "account" ? new LedgerAccount("Unrelated investment", LedgerAccountType.Asset) : line.Account,
                        line.Debit, line.Credit, journal.Description,
                        line.Dimensions! with
                        {
                            InstrumentId = mismatch == "security" ? Guid.NewGuid() : recipient.SecurityId,
                            PositionId = mismatch == "position" ? Guid.NewGuid() : recipient.BookPositionId
                        }) : line).ToArray(), journal.Metadata);
        }
        else
        {
            history = history with
            {
                DeferralRecipients = mismatch == "missing-recipient" ? null :
                [recipient with
                {
                    DeferredLoss = mismatch == "recipient-amount" ? 19m : recipient.DeferredLoss,
                    ReplacementLotId = mismatch == "recipient-lot" ? "foreign-lot" : recipient.ReplacementLotId,
                    ReplacementTaxLotRecordId = mismatch == "recipient-source" ? lot.TaxLotRecordId : recipient.ReplacementTaxLotRecordId
                }]
            };
        }

        var project = () => CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");
        var result = LedgerDisposalTaxReadService.Project(history, journal, lot.LedgerBookId, "USD", new(2026, 5, 12));

        project.Should().Throw<LedgerValidationException>();
        result.State.Should().Be("MissingEvidence");
        result.EconomicGainOrLoss.Should().BeNull();
        result.RecognizedGainOrLoss.Should().BeNull();
        result.DeferredLoss.Should().BeNull();
    }

    [Theory]
    [InlineData(8, 12, true)]
    [InlineData(9, 11, false)]
    public void DeferredDisposal_BindsEachRecipientPositionAmountDespiteUnchangedTotal(
        int firstDebit, int secondDebit, bool certifies)
    {
        var (lot, history, journal) = CreateDeferredDisposal();
        var recipient = history.DeferralRecipients!.Single();
        var first = recipient with { BookPositionId = Guid.NewGuid(), DeferredLoss = 8m };
        var second = recipient with
        {
            ReplacementTaxLotRecordId = Guid.NewGuid(),
            ReplacementLotId = "replacement-2",
            BookPositionId = Guid.NewGuid(),
            DeferredLoss = 12m
        };
        history = history with
        {
            WashSaleBasisIncreases = [new(first.ReplacementLotId, 8m, lot.AcquiredDate), new(second.ReplacementLotId, 12m, lot.AcquiredDate)],
            DeferralRecipients = [first, second]
        };
        journal = new(journal.JournalEntryId, journal.Timestamp, journal.Description,
            journal.Lines.Where(line => line.Account != recipient.Account).Concat(new[] { (first, firstDebit), (second, secondDebit) }
                .Select(item => new LedgerEntry(Guid.NewGuid(), journal.JournalEntryId, journal.Timestamp,
                    item.Item1.Account, item.Item2, 0m, journal.Description,
                    new LedgerLineDimensionSet(InstrumentId: item.Item1.SecurityId) { PositionId = item.Item1.BookPositionId }))).ToArray(),
            journal.Metadata);

        var project = () => CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");
        if (certifies)
            project().DisallowedWashSaleLoss.Should().Be(20m);
        else
            project.Should().Throw<LedgerValidationException>().WithMessage("*exact replacement recipient journal basis*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredDisposal_MissingDeferralRowsCannotTurnACashLossIntoASettledGain(bool foreignCashAccount)
    {
        var (lot, history, journal) = CreateDeferredDisposal();
        history = history with { WashSaleBasisIncreases = [], DeferralRecipients = null, MatchedReplacementQuantity = 0m, PolicyRevision = "retained-policy" };
        if (foreignCashAccount)
            journal = new(journal.JournalEntryId, journal.Timestamp, journal.Description,
                journal.Lines.Select(line => line.Account.Name.StartsWith("Cash", StringComparison.Ordinal)
                    ? new LedgerEntry(line.EntryId, journal.JournalEntryId, journal.Timestamp,
                        LedgerAccounts.CashAccount("different-account"), line.Debit, line.Credit,
                        journal.Description, line.Dimensions, line.Currency)
                    : line).ToArray(), journal.Metadata);

        var result = LedgerDisposalTaxReadService.Project(history, journal, lot.LedgerBookId, "USD", new(2026, 5, 12));

        result.State.Should().Be("MissingEvidence");
        result.CanChange.Should().BeTrue();
        result.EconomicGainOrLoss.Should().BeNull();
        result.RecognizedGainOrLoss.Should().BeNull();
        result.DeferredLoss.Should().BeNull();
    }

    [Fact]
    public void UnquotedDisposalWithoutDeferrals_PreservesExistingNonCashReporting()
    {
        var (lot, history, journal) = CreateDeferredDisposal();
        history = history with { WashSaleBasisIncreases = [], DeferralRecipients = null, MatchedReplacementQuantity = 0m };
        journal = new(journal.JournalEntryId, journal.Timestamp, journal.Description,
        [
            journal.Lines.Single(line => line.Account == lot.Account),
            new LedgerEntry(Guid.NewGuid(), journal.JournalEntryId, journal.Timestamp,
                new LedgerAccount("Retained non-cash consideration", LedgerAccountType.Asset),
                100m, 0m, journal.Description, journal.Lines[0].Dimensions)
        ], journal.Metadata);

        var result = CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        result.Proceeds.Should().Be(100m);
        result.RealizedGainOrLoss.Should().Be(0m);
    }

    private static (LedgerTaxLotRecord Lot, LedgerTaxLotDisposalHistoryRecord History, JournalEntry Journal) CreateDeferredDisposal()
    {
        var lot = CanonicalOpenLotConsumerTests.DurableLot(1, 100m, 1m);
        var id = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lot.SecurityId) { PositionId = lot.BookPositionId };
        const string description = "Retained deferred disposal";
        var journal = new JournalEntry(id, timestamp, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, timestamp, LedgerAccounts.Cash, 30m, 0m, description, dimensions),
            new LedgerEntry(Guid.NewGuid(), id, timestamp, LedgerAccounts.CashInCurrency("USD"), 50m, 0m, description, dimensions),
            new LedgerEntry(Guid.NewGuid(), id, timestamp, lot.Account, 0m, 100m, description, dimensions),
            new LedgerEntry(Guid.NewGuid(), id, timestamp, new LedgerAccount("Replacement investment", LedgerAccountType.Asset),
                20m, 0m, description, dimensions)
        ], new(EffectiveDate: new(2026, 5, 12)));
        var history = new LedgerTaxLotDisposalHistoryRecord(Guid.NewGuid(), id, lot.Account,
            LedgerTaxLotReliefMethod.Fifo, [new(lot.LotId, lot.AcquiredDate, lot.AcquiredDate, 1m, 100m, 100m)],
            [new("replacement", 20m, lot.AcquiredDate)], 1m, [lot.ToOpenLot()],
            ProceedsAllocationVersion: LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion,
            PolicyRevision: "retained-policy",
            DeferralRecipients: [new(Guid.NewGuid(), "replacement", journal.Lines[^1].Account,
                lot.SecurityId, lot.BookPositionId, 20m)]);
        return (lot, history, journal);
    }
}
