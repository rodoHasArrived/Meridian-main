using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Equity.</summary>
internal static class EquityAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.Equity;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                SupportsEquityClassification: true,
                AssetPackIds: ["public-equity-etf"],
                InstrumentTypes: [InstrumentType.Equity],
                LotUnit: SecurityLotUnitKind.Shares,
                CashFlowBehavior: SecurityCashFlowBehavior.Dividends,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: true,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.Sedol,
                    SecurityIdentifierKind.Bbgid,
                    SecurityIdentifierKind.PermTicker,
                    SecurityIdentifierKind.Ric,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ],
                // Every Equity asset-specific term is optional, so a name/currency/identifier row
                // carries the whole contract.
                SupportsIdentifierOnlyImport: true)
            { AccountingOutsideSlice = true },
            Fields: [
                Opt("shareClass", SecurityAssetTermFieldType.String),
                Opt("votingRightsCat", SecurityAssetTermFieldType.String),
                OptOneOf("classification", "Common", "Preferred", "Convertible", "ConvertiblePreferred", "Other")
                    .WithEscape("Other", "otherClassification", "preferredTerms", "convertibleTerms"),
                // Raw label carried alongside classification="Other"; the discriminant stays a
                // closed vocabulary while free-text classifications round-trip losslessly.
                Opt("otherClassification", SecurityAssetTermFieldType.String),
                Opt("preferredTerms", SecurityAssetTermFieldType.Object),
                Opt("convertibleTerms", SecurityAssetTermFieldType.Object)
            ],
            SchemaOrder: 0,
            ElementShapes:
            [
                new("convertibleTerms", 20, [
                    Req("underlyingSecurityId", SecurityAssetTermFieldType.Guid).LinksUnderlying(SecurityUnderlyingRole.Underlying),
                    Req("conversionRatio", SecurityAssetTermFieldType.Decimal),
                    Opt("conversionPrice", SecurityAssetTermFieldType.Decimal),
                    Opt("conversionStartDate", SecurityAssetTermFieldType.Date),
                    Opt("conversionEndDate", SecurityAssetTermFieldType.Date)
                ]),
                new("preferredTerms", 21, [
                    Opt("dividendRate", SecurityAssetTermFieldType.Decimal),
                    ReqOneOf("dividendType", "Fixed", "Floating", "Cumulative"),
                    Opt("redemptionPrice", SecurityAssetTermFieldType.Decimal),
                    Opt("redemptionDate", SecurityAssetTermFieldType.Date),
                    Opt("callableDate", SecurityAssetTermFieldType.Date),
                    Opt("participationTerms", SecurityAssetTermFieldType.Object),
                    Req("liquidationPreference", SecurityAssetTermFieldType.Object)
                ]),
                new("preferredTerms.participationTerms", 22, [
                    Opt("participatesInCommonDividends", SecurityAssetTermFieldType.Boolean),
                    Opt("additionalDividendThreshold", SecurityAssetTermFieldType.Decimal)
                ]),
                new("preferredTerms.liquidationPreference", 23, [
                    ReqOneOf("kind", "Pari", "Senior", "Subordinated"),
                    Opt("multiple", SecurityAssetTermFieldType.Decimal)
                ])
            ])
        {
            RateFields =
            [
                // Equity: preferred dividend (nested preferredTerms block).
                Ratio(AssetClass, "preferredTerms.dividendRate", "Annual preferred dividend rate", AnnualRateMax)
            ],
            CodecBinding = Class(AssetClass,
                B("Capital.ShareClass", "shareClass", SecurityTermRead.OptionalString),
                Computed("Capital.VotingRightsCat", "votingRightsCat",
                    "ToVotingRightsCatOption(bound.OptionalString(\"votingRightsCat\"))",
                    "terms.Capital.VotingRightsCat |> Option.map VotingRightsCat.asString"),
                // The classification union is written as four keys; classification discriminates and
                // the preferred and convertible blocks carry the dependent terms.
                Fixed("Capital.Classification", "ToEquityClassificationOption(bound, mode)"),
                WrittenAs("Capital.Classification", "classification", "EquityCapitalCodec.classification terms.Capital"),
                WrittenAs("Capital.Classification", "otherClassification", "EquityCapitalCodec.otherClassification terms.Capital"),
                WrittenAs("Capital.Classification", "preferredTerms", "EquityCapitalCodec.preferredTerms terms.Capital"),
                WrittenAs("Capital.Classification", "convertibleTerms", "EquityCapitalCodec.convertibleTerms terms.Capital")),
            CodecOrder = 0,
            EconomicFlattenings = [Map("shareClass", ("equityCapital", "shareClass"), ("equityBehavior", "shareClass")), Map("votingRightsCat", ("equityBehavior", "votingRights"))],
            EconomicFlatteningOrder = 0
        };
}
