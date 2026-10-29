using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Prepares a held-bond settlement; fees are separately expensed, never capitalized implicitly.</summary>
public sealed record BondSettlementConsiderationRequest(
    BondAnalyticsRequest Analytics,
    decimal OriginalFace,
    decimal BookedFactor,
    decimal Fees,
    decimal ObservedDirtyCash,
    string SourceEvidenceId,
    string CashAccountId,
    string AccruedInterestAccountId,
    string? FeesAccountId = null);

/// <summary>
/// Reviewed acquisition settlement economics. Quotes and accrued interest are per 100 face;
/// clean cost, purchased interest, fees and observed cash are absolute transaction-currency amounts.
/// The enclosing lot instruction fingerprint binds this receipt to the retained draft and approval.
/// </summary>
public sealed record BondSettlementConsiderationDto(
    Guid SecurityId,
    string Currency,
    string ModelVersion,
    BondCalculationContext Context,
    BondYieldConvention YieldConvention,
    decimal OriginalFace,
    decimal BookedFactor,
    decimal CleanPricePer100,
    decimal AccruedInterestPer100,
    decimal CleanConsideration,
    decimal PurchasedAccruedInterest,
    decimal Fees,
    decimal DirtyConsideration,
    decimal ObservedDirtyCash,
    string SourceEvidenceId,
    string CashAccountId,
    string AccruedInterestAccountId,
    string? FeesAccountId,
    IReadOnlyList<BondCalculationInput> Inputs);

public sealed record BondSettlementPreparationResult(
    BondSettlementConsiderationDto? Settlement,
    IReadOnlyList<BondAnalyticsBlocker> Blockers)
{
    public bool IsBlocked => Settlement is null || Blockers.Count > 0;
}

public interface IBondSettlementConsiderationService
{
    Task<BondSettlementPreparationResult> PrepareAsync(BondSettlementConsiderationRequest request,
        CancellationToken ct = default);

    /// <summary>Recomputes a retained receipt against its actual pinned/versioned analytics inputs.</summary>
    Task<IReadOnlyList<BondAnalyticsBlocker>> VerifyAsync(BondSettlementConsiderationDto settlement,
        CancellationToken ct = default);
}

/// <summary>Pure receipt validation shared by candidate admission and retained instruction checks.</summary>
public static class BondSettlementConsideration
{
    public static decimal Round(decimal amount, string currency)
        => decimal.Round(amount, CurrencyMinorUnits.GetPrecision(currency), MidpointRounding.AwayFromZero);

    public static IReadOnlyList<string> Validate(BondSettlementConsiderationDto receipt)
    {
        var issues = new List<string>();
        if (receipt.SecurityId == Guid.Empty || string.IsNullOrWhiteSpace(receipt.Currency)
            || receipt.Currency != receipt.Currency.Trim().ToUpperInvariant() || string.IsNullOrWhiteSpace(receipt.ModelVersion)
            || receipt.Context is null || receipt.Context.AsOf == default || receipt.Context.AsOf.Offset != TimeSpan.Zero
            || receipt.Context.SettlementDate is null || !Enum.IsDefined(receipt.YieldConvention))
            issues.Add("Bond settlement requires security, currency, model and explicit UTC calculation/settlement dates.");
        if (receipt.OriginalFace <= 0m || receipt.BookedFactor is <= 0m or > 1m
            || receipt.CleanPricePer100 < 0m || receipt.AccruedInterestPer100 < 0m || receipt.Fees < 0m)
            issues.Add("Bond settlement requires positive face and booked factor in (0,1], and nonnegative prices, interest and fees.");
        if (string.IsNullOrWhiteSpace(receipt.SourceEvidenceId) || string.IsNullOrWhiteSpace(receipt.CashAccountId)
            || string.IsNullOrWhiteSpace(receipt.AccruedInterestAccountId)
            || (receipt.Fees != 0m && string.IsNullOrWhiteSpace(receipt.FeesAccountId))
            || receipt.CashAccountId == receipt.AccruedInterestAccountId
            || (receipt.Fees != 0m && (receipt.FeesAccountId == receipt.CashAccountId || receipt.FeesAccountId == receipt.AccruedInterestAccountId)))
            issues.Add("Bond settlement requires retained cash evidence and distinct cash, accrued-interest and applicable fee accounts.");
        if (receipt.Inputs is null || receipt.Inputs.Any(static input => input is null)
            || receipt.Inputs.Count(input => input.Kind == "SecurityTerms") != 1 || !receipt.Inputs.Any(input => input.Kind == "SecurityTerms"
                && input.Key == receipt.SecurityId.ToString("D") && input.Version is > 0 && IsSha256(input.ContentSha256)))
            issues.Add("Bond settlement requires hash-bound versioned Security Master calculation inputs.");
        if (issues.Count > 0)
            return issues;
        try
        {
            var scaledFace = receipt.OriginalFace * receipt.BookedFactor / 100m;
            var clean = Round(scaledFace * receipt.CleanPricePer100, receipt.Currency);
            var accrued = Round(scaledFace * receipt.AccruedInterestPer100, receipt.Currency);
            if (receipt.CleanConsideration != clean || receipt.PurchasedAccruedInterest != accrued
                || receipt.Fees != Round(receipt.Fees, receipt.Currency)
                || receipt.DirtyConsideration != clean + accrued + receipt.Fees
                || receipt.ObservedDirtyCash != receipt.DirtyConsideration)
                issues.Add("Bond settlement clean cost, purchased interest, fees and observed dirty cash must reconcile exactly in currency minor units.");
        }
        catch (OverflowException)
        {
            issues.Add("Bond settlement amounts exceed supported decimal bounds.");
        }
        return issues;
    }

    public static IReadOnlyList<string> ValidateAcquisition(AssetAcquisitionLotDto acquisition,
        IReadOnlyList<RetainedEvidenceIdentityDto>? evidence = null)
    {
        if (acquisition.SettlementConsideration is not { } receipt)
            return [];
        var issues = Validate(receipt).ToList();
        if (!acquisition.HasFaceValueTerms || acquisition.OriginalFace != receipt.OriginalFace
            || acquisition.BookedFactor != receipt.BookedFactor || acquisition.ParBasis != 100m
            || acquisition.AcquiredDate != receipt.Context?.SettlementDate)
            issues.Add("Bond settlement must match the acquisition's retained face, factor, per-100 quote basis and settlement date.");
        try
        {
            if (acquisition.Quantity * acquisition.UnitCost != receipt.CleanConsideration)
                issues.Add("Purchased accrued interest and fees must remain separate from clean acquisition lot cost.");
        }
        catch (OverflowException)
        {
            issues.Add("Bond acquisition consideration exceeds supported decimal bounds.");
        }
        if (evidence is not null && !evidence.Any(item => item.EvidenceId == receipt.SourceEvidenceId
                && item.EffectiveDate == acquisition.AcquiredDate && RetainedEvidenceIdentityValidator.IsComplete(item)))
            issues.Add("Bond settlement cash evidence must be retained with the acquisition on its settlement date.");
        return issues;
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
