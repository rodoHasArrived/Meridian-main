using SharedCurrencyCodeCatalog = Meridian.Core.ReferenceData.CurrencyCodeCatalog;

namespace Meridian.Ledger;

/// <summary>
/// Compatibility surface for the repository-owned currency catalog shared by ledger,
/// statement intake, and provider infrastructure. Catalog data is owned by Core.
/// </summary>
public static class CurrencyCodeCatalog
{
    /// <summary>All recognized codes, including historical ledger currencies.</summary>
    public static IReadOnlyList<string> RecognizedCodes => SharedCurrencyCodeCatalog.RecognizedCodes;

    /// <summary>Codes permitted for new payment transactions.</summary>
    public static IReadOnlyList<string> CurrentTransactionCodes => SharedCurrencyCodeCatalog.CurrentTransactionCodes;

    /// <summary>Returns whether the candidate exactly names a recognized code.</summary>
    public static bool IsRecognized(string? candidate) => SharedCurrencyCodeCatalog.IsRecognized(candidate);

    /// <summary>Returns whether a code is permitted for a new transaction.</summary>
    public static bool IsCurrentForTransactions(string? candidate) => SharedCurrencyCodeCatalog.IsCurrentForTransactions(candidate);

    /// <summary>Trims, uppercases, and validates a recognized currency code.</summary>
    public static bool TryNormalizeRecognized(string? candidate, out string normalized)
        => SharedCurrencyCodeCatalog.TryNormalizeRecognized(candidate, out normalized);

    /// <summary>Trims, uppercases, and validates a currency for a new transaction.</summary>
    public static bool TryNormalizeCurrent(string? candidate, out string normalized)
        => SharedCurrencyCodeCatalog.TryNormalizeCurrent(candidate, out normalized);
}
