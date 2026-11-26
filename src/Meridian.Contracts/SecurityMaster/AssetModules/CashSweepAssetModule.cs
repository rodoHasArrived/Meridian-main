using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CashSweep.</summary>
internal static class CashSweepAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.CashSweep;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["cash-bank"],
                InstrumentTypes: [InstrumentType.Deposit],
                LotUnit: SecurityLotUnitKind.CurrencyAmount,
                CashFlowBehavior: SecurityCashFlowBehavior.ProviderSupplied,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Ticker
                ],
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            { StampFloor = 1, AccountingOutsideSlice = true },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                Req("programName", SecurityAssetTermFieldType.String),
                Req("sweepVehicleType", SecurityAssetTermFieldType.String),
                Opt("sweepFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.PaymentFrequency),
                Opt("targetAccountType", SecurityAssetTermFieldType.String),
                Opt("yieldRate", SecurityAssetTermFieldType.Decimal)
            ],
            SchemaOrder: 11,
            ElementShapes: [])
        {
            RateFields =
            [
                SignedRate(AssetClass, "yieldRate", "Sweep vehicle yield", RateUnit.Fraction)
            ],
            CodecBinding = Class(AssetClass,
                B("SweepTerms.ProgramName", "programName", SecurityTermRead.RequiredString, via: "ProgramName"),
                Converted("SweepTerms.SweepVehicleType", "sweepVehicleType",
                    "FSharpOption<SweepVehicle>.Some(ToCodecSweepVehicle(bound.RequiredString(\"sweepVehicleType\")))", "SweepVehicleType"),
                Converted("SweepTerms.SweepFrequency", "sweepFrequency",
                    "ToCodecSweepFrequency(ToOption(bound.OptionalString(\"sweepFrequency\")))", "SweepFrequency"),
                B("SweepTerms.TargetAccountType", "targetAccountType", SecurityTermRead.OptionalString),
                B("DiscountTerms.YieldRate", "yieldRate", SecurityTermRead.OptionalDecimal)),
            CodecOrder = 11,
            EconomicFlattenings = [
                Map("programName", ("sweep", "programName")),
                Map("sweepVehicleType", ("sweep", "sweepVehicleType")),
                Map("sweepFrequency", ("sweep", "sweepFrequency")),
                Map("targetAccountType", ("sweep", "targetAccountType")),
                Map("yieldRate", ("discount", "yieldRate"))
            ],
            EconomicFlatteningOrder = 14
        };
}
