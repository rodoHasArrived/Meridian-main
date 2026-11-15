using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CommitmentGuarantee.</summary>
internal static class CommitmentGuaranteeAssetModule
{
    private const string AssetClass = "CommitmentGuarantee";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["commitment-guarantee"],
                LotUnit: SecurityLotUnitKind.Commitment,
                CashFlowBehavior: SecurityCashFlowBehavior.ProviderSupplied,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.Lei,
                    SecurityIdentifierKind.ProviderSymbol
                ],
                AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
                SupportsProfileBackedTerms: true)
            {
                AccountingOutsideSlice = true,
                ClassificationLabels = ["UnfundedCommitment", "Guarantee"],
                ReclassifyingProfileIds = ["commitment-guarantee"]
            },
            Fields: [
                Req("counterparty", SecurityAssetTermFieldType.String),
                Opt("beneficiary", SecurityAssetTermFieldType.String),
                Req("committedAmount", SecurityAssetTermFieldType.Decimal),
                Opt("unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Opt("expiryDate", SecurityAssetTermFieldType.Date),
                Opt("feeRate", SecurityAssetTermFieldType.Decimal),
                Opt("collateral", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Collateral),
                Req("covenants", SecurityAssetTermFieldType.Array)
            ],
            SchemaOrder: 20,
            ElementShapes:
            [
                new("covenants", 7, [
                    Req("covenantType", SecurityAssetTermFieldType.String),
                    Req("threshold", SecurityAssetTermFieldType.String),
                    Opt("notes", SecurityAssetTermFieldType.String)
                ])
            ])
        {
            RateFields =
            [
                // Guarantees and OTC derivatives.
                Ratio(AssetClass, "feeRate", "Commitment or guarantee fee rate", AnnualRateMax)
            ],
            CodecBinding = new(AssetClass, true,
            [
                B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
                Also("Dates.IssueDate", "effectiveDate", SecurityTermRead.RequiredDate),
                B("Dates.MaturityDate", "expiryDate", SecurityTermRead.OptionalDate),
                B("Notional.CommittedAmount", "committedAmount", SecurityTermRead.RequiredDecimal),
                B("Notional.UnfundedAmount", "unfundedAmount", SecurityTermRead.OptionalDecimal),
                B("IssuerTerms.IssuerName", "counterparty", SecurityTermRead.RequiredString, via: "Counterparty"),
                B("IssuerTerms.IssuerProgram", "beneficiary", SecurityTermRead.OptionalString),
                B("ContractualRate.FeeRate", "feeRate", SecurityTermRead.OptionalDecimal),
                B("CollateralTerms.CollateralType", "collateral", SecurityTermRead.OptionalString),
                Listed("LoanCovenants.Entries", "covenants", "ToCovenants(GetOptionalArrayItems(terms, \"covenants\"))",
                    "Covenants", CovenantItems),
            ], Inputs: SecurityCodecInputs.ResolvedTermsDocument),
            CodecOrder = 19,
            EconomicFlattenings = [
                Map("counterparty", ("issuer", "issuerName")),
                Map("beneficiary", ("issuer", "issuerProgram")),
                Map("collateral", ("financing", "collateralType")),
                Map("effectiveDate", ("maturity", "effectiveDate")),
                Map("expiryDate", MaturityDate),
                Map("committedAmount", ("notional", "committedAmount")),
                Map("unfundedAmount", ("notional", "unfundedAmount"))
            ],
            EconomicFlatteningOrder = 22
        };
}
