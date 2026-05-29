namespace Meridian.Ledger;

/// <summary>
/// Base-currency journal line with preserved local-currency posting evidence.
/// </summary>
public sealed record MultiCurrencyJournalLineProjection(
    LedgerAccount Account,
    string LocalCurrency,
    string BaseCurrency,
    decimal LocalDebit,
    decimal LocalCredit,
    decimal FxRateToBase,
    decimal BaseDebit,
    decimal BaseCredit);
