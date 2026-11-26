using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Repo.</summary>
internal static class RepoAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.Repo;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                TradesAsBilateralContract: true,
                AssetPackIds: ["fixed-income"],
                InstrumentTypes: [InstrumentType.Repo],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.FinancingInterest,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip
                ],
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "endDate"),
                    new(SecurityCashFlowTermRole.Issue, "startDate"),
                    new(SecurityCashFlowTermRole.CouponRate, "repoRate")
                ],
                StampFloor = 1,
                AccountingOutsideSlice = true,
                // A repo is a financing balance, not a quoted security (slice 8, decided 2026-11-25).
                QuoteConvention = SecurityQuoteConvention.Balance
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                Req("counterparty", SecurityAssetTermFieldType.String),
                Req("startDate", SecurityAssetTermFieldType.Date),
                Req("endDate", SecurityAssetTermFieldType.Date),
                Opt("repoRate", SecurityAssetTermFieldType.Decimal),
                Opt("collateralType", SecurityAssetTermFieldType.String),
                Opt("haircut", SecurityAssetTermFieldType.Decimal)
            ],
            SchemaOrder: 10,
            ElementShapes: [])
        {
            RateFields =
            [
                SignedRate(AssetClass, "repoRate", "Annual repo financing rate", consumedBy: CouponReaders),
                Ratio(AssetClass, "haircut", "Collateral haircut", 1m)
            ],
            CouponRateFieldKey = "repoRate",
            CodecBinding = Class(AssetClass,
                B("FinancingTerms.Counterparty", "counterparty", SecurityTermRead.RequiredString, via: "Counterparty"),
                B("FinancingTerms.CollateralType", "collateralType", SecurityTermRead.OptionalString),
                B("FinancingTerms.Haircut", "haircut", SecurityTermRead.OptionalDecimal),
                B("FinancingTerms.OpenDate", "startDate", SecurityTermRead.RequiredDate, via: "StartDate"),
                B("FinancingTerms.CloseDate", "endDate", SecurityTermRead.RequiredDate, via: "EndDate"),
                B("DiscountTerms.YieldRate", "repoRate", SecurityTermRead.OptionalDecimal)),
            CodecOrder = 10,
            EconomicFlattenings = [
                Map("counterparty", ("financing", "counterparty")),
                Map("startDate", ("financing", "openDate"), ("maturity", "effectiveDate")),
                Map("endDate", ("financing", "closeDate"), MaturityDate),
                Map("repoRate", ("discount", "yieldRate")),
                Map("collateralType", ("financing", "collateralType")),
                Map("haircut", ("financing", "haircut"))
            ],
            EconomicFlatteningOrder = 13
        };
}
