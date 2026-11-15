using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

internal static class RecurringJournalEconomicObligationGuard
{
    internal static void RequireMatch(RecurringOccurrenceRecord claim, RecurringJournalEvidence evidence)
    {
        var expected = claim.EconomicObligationKeys is { } keys
            ? RecurringJournalIdentity.EconomicObligationDigest(keys) : null;
        if (expected is null ? evidence.EconomicObligationDigest is not null
            : !Sha256Digest.FixedEquals(expected, evidence.EconomicObligationDigest))
            throw new InvalidOperationException("Recurring economic obligations differ from their independently retained occurrence claim.");
    }
}
