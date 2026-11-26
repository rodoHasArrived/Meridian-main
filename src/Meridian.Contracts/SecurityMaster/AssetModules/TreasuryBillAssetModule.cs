using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for TreasuryBill.</summary>
internal static class TreasuryBillAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.TreasuryBill;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["fixed-income"],
                SupportsCanonicalBulletAmortization: true,
                InstrumentTypes: [InstrumentType.Bond],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.Bbgid,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ],
                AmortizesTowardPar: true,
                RequiresMaturity: true,
                AccountingInstrumentClass: SecurityAccountingInstrumentClasses.Bond,
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity")
                ],
                PaysNoContractualCoupon = true,
                StampFloor = 1
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("auctionDate", SecurityAssetTermFieldType.Date),
                Opt("cusip", SecurityAssetTermFieldType.String),
                Opt("discountRate", SecurityAssetTermFieldType.Decimal)
            ],
            SchemaOrder: 9,
            ElementShapes: [])
        {
            RateFields =
            [
                Coupon(AssetClass, "discountRate", "Discount yield (not a coupon; accretion only)", "")
            ],
            CouponRateFieldKey = "discountRate",
            CodecBinding = Class(AssetClass,
                B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
                B("Auction.AuctionDate", "auctionDate", SecurityTermRead.OptionalDate),
                Fixed("IssuerTerms.IssuerName", "ToOption(\"US Treasury\")"),
                B("IssuerTerms.IssuerProgram", "cusip", SecurityTermRead.OptionalString),
                Fixed("IssuerTerms.IssuerCountry", "ToOption(\"US\")"),
                B("DiscountTerms.DiscountRate", "discountRate", SecurityTermRead.OptionalDecimal)),
            CodecOrder = 9,
            EconomicFlattenings = [
                Map("maturity", MaturityDate),
                Map("auctionDate", ("auction", "auctionDate")),
                Map("cusip", ("issuer", "issuerProgram")),
                Map("discountRate", ("discount", "discountRate"))
            ],
            EconomicFlatteningOrder = 12
        };
}
