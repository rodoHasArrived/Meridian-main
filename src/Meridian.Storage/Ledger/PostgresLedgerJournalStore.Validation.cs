using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static void ValidateAuthoritativeBookContext(
        LedgerJournalEntryWrite entry,
        LedgerBookRecord book)
    {
        if (!LegacyCompatibleAuthoritativeTextEquals(book.AccountingPolicyId, entry.AccountingPolicyId) ||
            !LegacyCompatibleAuthoritativeTextEquals(book.AccountingPolicyVersion, entry.AccountingPolicyVersion))
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' accounting policy " +
                $"'{entry.AccountingPolicyId}/{entry.AccountingPolicyVersion}' does not match retained ledger book " +
                $"'{book.DisplayName}' policy '{book.AccountingPolicyId}/{book.AccountingPolicyVersion}'.");
        }

        if (entry.PostingCommand?.BookContext is not { } context)
        {
            return;
        }

        if (context.LedgerBookId != book.LedgerBookId)
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' book context does not match retained ledger book '{book.DisplayName}'.");
        }

        if (!RequiredAuthoritativeTextEquals(context.FundProfileId, book.FundProfileId) ||
            context.FundStructureNodeId != book.FundStructureNodeId ||
            context.FundStructureNodeKind != book.FundStructureNodeKind)
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' book context owner does not match retained ledger book '{book.DisplayName}'.");
        }

        if (!RequiredAuthoritativeTextEquals(context.BaseCurrency, book.BaseCurrency))
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' book context base currency '{context.BaseCurrency}' " +
                $"does not match retained ledger book '{book.DisplayName}' currency '{book.BaseCurrency}'.");
        }

        if (context.AccountingBasis != book.AccountingBasis ||
            !RequiredAuthoritativeTextEquals(context.AccountingPolicyId, book.AccountingPolicyId) ||
            !RequiredAuthoritativeTextEquals(context.AccountingPolicyVersion, book.AccountingPolicyVersion))
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' book context accounting lineage does not match retained ledger book '{book.DisplayName}'.");
        }
    }

    private static bool LegacyCompatibleAuthoritativeTextEquals(string? left, string? right)
    {
        var leftMissing = string.IsNullOrWhiteSpace(left);
        var rightMissing = string.IsNullOrWhiteSpace(right);
        return leftMissing || rightMissing
            ? leftMissing && rightMissing
            : string.Equals(left!.Trim(), right!.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiredAuthoritativeTextEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

}
