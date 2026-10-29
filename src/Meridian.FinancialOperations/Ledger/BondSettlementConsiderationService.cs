using Meridian.Contracts.FixedIncome;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Prepares explicit settlement economics from the shared bond engine. This is an analytical
/// receipt; the existing event-spine approval and atomic journal/lot boundary retain posting authority.
/// </summary>
public sealed class BondSettlementConsiderationService(IBondAnalyticsService analytics) : IBondSettlementConsiderationService
{
    public async Task<IReadOnlyList<BondAnalyticsBlocker>> VerifyAsync(BondSettlementConsiderationDto settlement,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        var issues = BondSettlementConsideration.Validate(settlement);
        if (issues.Count > 0)
            return issues.Select(issue => new BondAnalyticsBlocker("SettlementNotReconciled", issue)).ToArray();
        var securityInput = settlement.Inputs.SingleOrDefault(input => input.Kind == "SecurityTerms");
        if (securityInput is null)
            return [new("SettlementInputsUnavailable", "Exactly one retained Security Master input is required.")];
        var request = new BondAnalyticsRequest(settlement.SecurityId,
            settlement.Context with { SecurityVersion = securityInput.Version }, settlement.CleanPricePer100,
            YieldConvention: settlement.YieldConvention);
        var result = await analytics.CalculateAsync(request, ct).ConfigureAwait(false);
        if (result is null || result.IsBlocked)
            return result?.Blockers ?? [new("SettlementInputsUnavailable", "Retained analytics inputs cannot be resolved.")];
        if (result.SecurityId != settlement.SecurityId || result.Currency != settlement.Currency
            || result.ModelVersion != settlement.ModelVersion || result.CleanPrice != settlement.CleanPricePer100
            || result.AccruedInterest != settlement.AccruedInterestPer100
            || !result.Inputs.SequenceEqual(settlement.Inputs))
            return [new("SettlementInputsChanged", "Settlement metrics and complete input manifest must match the retained calculation.")];
        return [];
    }

    public async Task<BondSettlementPreparationResult> PrepareAsync(BondSettlementConsiderationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Analytics);
        var result = await analytics.CalculateAsync(request.Analytics, ct).ConfigureAwait(false);
        if (result is null)
            return new(null, [new("SecurityUnknown", "No retained bond security is available.")]);
        if (result.IsBlocked)
            return new(null, result.Blockers);
        if (result.YieldConvention is not { } yieldConvention
            || result.CleanPrice is not { } cleanPrice || result.AccruedInterest is not { } accruedPrice
            || result.DirtyPrice != cleanPrice + accruedPrice)
            return new(null, [new("SettlementMetricsUnavailable", "Bond analytics must provide reconciled clean price, dirty price and accrued interest per 100 face.")]);
        try
        {
            var faceScale = request.OriginalFace * request.BookedFactor / 100m;
            var clean = BondSettlementConsideration.Round(faceScale * cleanPrice, result.Currency);
            var accrued = BondSettlementConsideration.Round(faceScale * accruedPrice, result.Currency);
            var receipt = new BondSettlementConsiderationDto(result.SecurityId, result.Currency, result.ModelVersion,
                result.Context, yieldConvention, request.OriginalFace, request.BookedFactor, cleanPrice, accruedPrice,
                clean, accrued, request.Fees, clean + accrued + request.Fees, request.ObservedDirtyCash,
                request.SourceEvidenceId, request.CashAccountId, request.AccruedInterestAccountId, request.FeesAccountId,
                result.Inputs.ToArray());
            var issues = BondSettlementConsideration.Validate(receipt);
            return issues.Count == 0 ? new(receipt, [])
                : new(null, issues.Select(issue => new BondAnalyticsBlocker("SettlementNotReconciled", issue)).ToArray());
        }
        catch (OverflowException)
        {
            return new(null, [new("SettlementAmountOverflow", "Bond settlement exceeds supported decimal bounds.")]);
        }
    }
}
