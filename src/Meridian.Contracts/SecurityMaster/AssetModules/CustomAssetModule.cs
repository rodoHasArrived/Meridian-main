using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CustomAsset.</summary>
internal static class CustomAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.CustomAsset;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                RequiresProfileReference: true,
                AssetPackIds: ["controlled-other-asset"],
                RequiresGovernedProfile: true,
                LotUnit: SecurityLotUnitKind.Units,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                // Profile-keyed: one custom_asset_profile_field_projection row per profileFields key
                // (migration 064), written by a specialized store writer because the field set is
                // governed per profile rather than fixed per class.
                ProjectionKind: SecurityTermsProjectionKind.Specialized,
                SupportsBasicCreateWorkflow: true,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Lei
                ],
                SupportsProfileBackedTerms: true)
            { AccountingOutsideSlice = true },
            Fields: [
                // Profile-backed custom assets carry the profile envelope; the class-specific fields
                // are dynamic and live under profileFields, governed by the approved profile version.
                Req("customProfileId", SecurityAssetTermFieldType.String).ReadsAbsentAs(SecurityAssetTermField.EnvelopeLessSalvage),
                Req("profileVersion", SecurityAssetTermFieldType.Integer).ReadsAbsentAs("1"),
                Req("profileFields", SecurityAssetTermFieldType.Object),
                Opt("profileApproval", SecurityAssetTermFieldType.Object),
                // The workstation create path also stamps a display category/sub-type and evidence
                // links onto the document; they are part of the real persisted contract.
                Opt("category", SecurityAssetTermFieldType.String),
                Opt("subType", SecurityAssetTermFieldType.String),
                Opt("evidenceLinks", SecurityAssetTermFieldType.Array)
            ],
            SchemaOrder: 13,
            ElementShapes: [])
        {
            UsesOpaqueProfileCodec = true
        };
}
