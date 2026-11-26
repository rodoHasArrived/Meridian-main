using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Resolves the Security Master, book-position and functional-currency scope an atomic
/// tax-lot journal posts under.
/// </summary>
public sealed partial class PostgresLedgerJournalStore
{
    private static (Guid SecurityId, Guid BookPositionId) ResolveAtomicAssetScope(
        LedgerJournalEntryWrite journal)
    {
        if (journal.Entry.Metadata.SecurityId is not { } securityId || securityId == Guid.Empty)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot journal metadata requires a non-empty Security Master identity.");
        }

        if (journal.PostingCommand?.LotCorporateAction is { } corporateAction)
        {
            var source = corporateAction.ExpectedLot;
            var allowed = corporateAction.Successors.Select(target => (target.Lot.SecurityId, target.Lot.BookPositionId))
                .Append((source.SecurityId, source.BookPositionId)).ToHashSet();
            if (source.SecurityId != securityId || source.BookPositionId == Guid.Empty
                || journal.Entry.Lines.Any(line => !allowed.Contains((line.Dimensions?.InstrumentId ?? Guid.Empty,
                    line.Dimensions?.PositionId ?? Guid.Empty))))
                throw new LedgerValidationException("Corporate-action journal lines must identify the reviewed predecessor and successors exactly.");
            return (source.SecurityId, source.BookPositionId);
        }

        // A Security Master review F2 re-key moves one legacy account's whole holding, which can sit in
        // several book positions of the same security. Every line still names that security and a
        // position; the batch records the lowest position as its representative scope, and the
        // re-key lock checks each lot against the position its instruction names.
        if (journal.Entry.Metadata.Tags?.ContainsKey(LedgerAccountRekeyRules.JournalTag) == true)
        {
            var positions = journal.Entry.Lines
                .Select(static line => (SecurityId: line.Dimensions?.InstrumentId ?? Guid.Empty,
                    BookPositionId: line.Dimensions?.PositionId ?? Guid.Empty))
                .Distinct()
                .ToArray();
            if (positions.Length == 0 || positions.Any(scope => scope.SecurityId != securityId || scope.BookPositionId == Guid.Empty))
                throw new LedgerValidationException(
                    "Every account re-key journal line must carry the target Security Master identity and a book position.");
            return (securityId, positions.Min(static scope => scope.BookPositionId));
        }

        var lineScopes = journal.Entry.Lines
            .Select(static line => (
                SecurityId: line.Dimensions?.InstrumentId ?? Guid.Empty,
                BookPositionId: line.Dimensions?.PositionId ?? Guid.Empty))
            .Distinct()
            .ToArray();
        if (lineScopes.Length != 1 ||
            lineScopes[0].SecurityId != securityId ||
            lineScopes[0].BookPositionId == Guid.Empty)
        {
            throw new LedgerValidationException(
                "Every atomic tax-lot journal line must carry the same Security Master and book-position identities as the journal metadata.");
        }

        return lineScopes[0];
    }

    private static string ResolveAtomicFunctionalCurrency(LedgerJournalEntryWrite journal)
    {
        var currencies = journal.Entry.Lines
            .Select(static line => line.Currency?.FunctionalCurrency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (currencies.Length != 1 || string.IsNullOrWhiteSpace(currencies[0]))
        {
            throw new LedgerValidationException(
                "Every atomic tax-lot journal line must retain one exact functional currency.");
        }

        return currencies[0]!.Trim().ToUpperInvariant();
    }
}
