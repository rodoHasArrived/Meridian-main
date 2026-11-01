using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>
/// Reviewed execution terms for covering part of an open short lot in its transaction currency. The
/// journal is derived from these retained inputs; displayed amounts are never resubmitted. Fees are the
/// execution commission in the transaction currency. The acquisition FX is the lot's retained rate; the
/// cover FX is the settlement-date rate observed with the execution.
/// </summary>
public sealed record ShortCoverTerms(
    decimal CoveredQuantity,
    decimal CoverPrice,
    decimal Fees,
    string TransactionCurrency,
    string FunctionalCurrency,
    decimal AcquisitionFxRateToFunctional,
    decimal CoverFxRateToFunctional,
    decimal RelievedProceedsFunctional,
    string ModelVersion = ShortCoverTerms.CurrentModelVersion)
{
    public const string CurrentModelVersion = "short-cover-currency-v1";

    public bool IsFunctionalCurrency => string.Equals(TransactionCurrency, FunctionalCurrency, StringComparison.Ordinal);

    /// <summary>Rounds to the currency's minor units, away from zero at the midpoint, like settlement consideration.</summary>
    public static decimal RoundMinor(decimal amount, string currency)
        => decimal.Round(amount, CurrencyMinorUnits.GetPrecision(currency), MidpointRounding.AwayFromZero);

    /// <summary>The reason the terms cannot produce a journal, or null.</summary>
    public string? Refusal()
    {
        if (CoveredQuantity <= 0m)
            return "Covered quantity must be positive.";
        if (CoverPrice < 0m)
            return "Cover price cannot be negative.";
        if (Fees < 0m)
            return "Fees cannot be negative.";
        if (string.IsNullOrWhiteSpace(TransactionCurrency) || string.IsNullOrWhiteSpace(FunctionalCurrency))
            return "Transaction and functional currencies are required.";
        if (TransactionCurrency != TransactionCurrency.Trim().ToUpperInvariant() || FunctionalCurrency != FunctionalCurrency.Trim().ToUpperInvariant())
            return "Currency codes must be upper-case ISO codes without surrounding whitespace.";
        if (AcquisitionFxRateToFunctional <= 0m || CoverFxRateToFunctional <= 0m)
            return "Acquisition and cover FX rates must be positive.";
        if (IsFunctionalCurrency && (AcquisitionFxRateToFunctional != 1m || CoverFxRateToFunctional != 1m))
            return "Same-currency covers must carry FX rates of exactly one.";
        if (RelievedProceedsFunctional <= 0m)
            return "Relieved opening proceeds must be positive.";
        if (ModelVersion != CurrentModelVersion)
            return $"Unsupported short cover model version '{ModelVersion}'.";
        return null;
    }
}
