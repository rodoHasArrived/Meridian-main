using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the InvestmentFund asset module.</summary>
internal static class InvestmentFundAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("InvestmentFund", Create);

    // ADR-022 canonical-home guidance: MoneyMarketFund is the canonical home for
    // stable-NAV money-market and government liquidity vehicles; the generic fund wrapper
    // flagged stable-NAV is the overlap the review called out. Warning (not Error): the
    // InvestmentFundTerms contract documents the flag, so existing records are steered,
    // not blocked, toward the canonical class.
    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "InvestmentFund",
            [
                DiscouragedTrueBooleanRule.Create(
                    "isStableNav",
                    SecurityValidationSeverityDto.Warning,
                    "SM_INVESTMENT_FUND_STABLE_NAV_NONCANONICAL",
                    "Stable-NAV vehicles belong in MoneyMarketFund",
                    "This InvestmentFund record is flagged stable-NAV; MoneyMarketFund is the canonical home for stable-NAV money-market and government liquidity vehicles (ADR-022).",
                    "Model the vehicle as MoneyMarketFund (fund family, sweep eligibility, WAM, liquidity-fee eligibility) through a governed amendment.")
            ]);
}
