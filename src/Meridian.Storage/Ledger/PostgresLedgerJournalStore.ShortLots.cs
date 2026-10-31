using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static void ValidateShortLotJournal(AtomicTaxLotJournalCommand command, LedgerAccount account, decimal basis)
    {
        if (command.Journal.PostingCommand is not { ApprovalState: AccountingPostingApprovalStateDto.Approved } posting
            || string.IsNullOrWhiteSpace(posting.ApprovalId) || string.IsNullOrWhiteSpace(posting.Actor))
            throw new LedgerValidationException("Short opening/cover requires an approved governed posting command and attributed actor.");
        if (command.CorrectsMutationBatchId is not null || command.Journal.SourceJournalEntryId is not null)
            throw new LedgerValidationException("Short corrections require separately certified reversal/rebook treatment.");
        try
        {
            IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> expected;
            if (command.MutationKind == AtomicTaxLotMutationKind.Acquisition)
                expected = ShortLotJournalProjector.Open(account, basis);
            else
            {
                if (command.DisposalSalePrice is not { } coverPrice)
                    throw new LedgerValidationException("Short covers require the explicit functional-currency execution price.");
                var cost = RoundCurrency(command.DisposalSelections!.Sum(selection => selection.Quantity) * coverPrice);
                expected = ShortLotJournalProjector.Cover(account, basis, cost);
            }
            var actual = command.Journal.Entry.Lines;
            if (actual.Count != expected.Count || expected.Any(line =>
                    actual.Count(item => item.Account == line.account && item.Debit == line.debit && item.Credit == line.credit) != 1))
                throw new LedgerValidationException("Short opening/cover journal must exactly retain cash, liability relief and realized book result; extra or inverted lines are refused.");
        }
        catch (ArgumentException exception)
        {
            throw new LedgerValidationException(exception.Message);
        }
    }
}
