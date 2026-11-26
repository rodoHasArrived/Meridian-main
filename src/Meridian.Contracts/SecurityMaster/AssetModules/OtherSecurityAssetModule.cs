using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for OtherSecurity.</summary>
internal static class OtherSecurityAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.OtherSecurity;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["controlled-other-asset"],
                RequiresGovernedProfile: true,
                LotUnit: SecurityLotUnitKind.Units,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Figi
                ],
                SupportsProfileBackedTerms: true)
            { AccountingOutsideSlice = true },
            Fields: [
                Req("category", SecurityAssetTermFieldType.String),
                Opt("subType", SecurityAssetTermFieldType.String),
                Opt("maturity", SecurityAssetTermFieldType.Date),
                Opt("issuerName", SecurityAssetTermFieldType.String),
                Opt("settlementType", SecurityAssetTermFieldType.String)
            ],
            SchemaOrder: 12,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
                B("ClassificationDetail.Category", "category", SecurityTermRead.RequiredString),
                B("ClassificationDetail.SubType", "subType", SecurityTermRead.OptionalString),
                B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.OptionalString),
                B("IssuerTerms.IssuerProgram", "settlementType", SecurityTermRead.OptionalString)),
            CodecOrder = 12,
            EconomicFlattenings = [
                Map("category", ("classificationDetail", "category")),
                Map("subType", ("classificationDetail", "subType")),
                Map("maturity", MaturityDate),
                Map("issuerName", ("issuer", "issuerName")),
                Map("settlementType", ("issuer", "issuerProgram"))
            ],
            EconomicFlatteningOrder = 15
        };
}
