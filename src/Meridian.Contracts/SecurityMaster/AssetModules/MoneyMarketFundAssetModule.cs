using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for MoneyMarketFund.</summary>
internal static class MoneyMarketFundAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.MoneyMarketFund;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["cash-bank"],
                InstrumentTypes: [InstrumentType.Equity],
                LotUnit: SecurityLotUnitKind.Units,
                CashFlowBehavior: SecurityCashFlowBehavior.FundDistributions,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ])
            { AccountingOutsideSlice = true },
            Fields: [
                Opt("fundFamily", SecurityAssetTermFieldType.String),
                Req("sweepEligible", SecurityAssetTermFieldType.Boolean).ReadsAbsentAs("false"),
                Opt("weightedAverageMaturityDays", SecurityAssetTermFieldType.Integer),
                Req("liquidityFeeEligible", SecurityAssetTermFieldType.Boolean).ReadsAbsentAs("false")
            ],
            SchemaOrder: 6,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("FundTerms.FundFamily", "fundFamily", SecurityTermRead.OptionalString),
                B("FundTerms.WeightedAverageMaturityDays", "weightedAverageMaturityDays", SecurityTermRead.OptionalInt),
                B("FundTerms.SweepEligible", "sweepEligible", SecurityTermRead.BooleanDefaultFalse),
                B("FundTerms.LiquidityFeeEligible", "liquidityFeeEligible", SecurityTermRead.BooleanDefaultFalse)),
            CodecOrder = 6,
            EconomicFlattenings = [
                Map("fundFamily", ("fund", "fundFamily")),
                Map("sweepEligible", ("fund", "sweepEligible")),
                Map("weightedAverageMaturityDays", ("fund", "weightedAverageMaturityDays")),
                Map("liquidityFeeEligible", ("fund", "liquidityFeeEligible"))
            ],
            EconomicFlatteningOrder = 9
        };
}
