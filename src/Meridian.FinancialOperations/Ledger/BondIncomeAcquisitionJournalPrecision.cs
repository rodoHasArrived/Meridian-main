using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Exact adopted birth facts must survive the durable journal without an implicit round.</summary>
internal static class BondIncomeAcquisitionJournalPrecision
{
    internal static void RequireRepresentable(JournalEntry entry)
    {
        foreach (var line in entry.Lines)
        {
            BondIncomeBookedPrecision.RequireRepresentable(line.Debit);
            BondIncomeBookedPrecision.RequireRepresentable(line.Credit);
            if (line.Currency is not { } currency)
                continue;
            BondIncomeBookedPrecision.RequireRepresentable(currency.TransactionDebit);
            BondIncomeBookedPrecision.RequireRepresentable(currency.TransactionCredit);
        }
    }
}
