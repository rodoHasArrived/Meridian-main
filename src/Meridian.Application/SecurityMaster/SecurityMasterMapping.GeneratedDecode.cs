// Generated from SecurityAssetTermsCodecBindings by SecurityFlatCodecGenerator. Do not edit:
// change the binding table and regenerate (MERIDIAN_ACCEPT_GENERATED_CODEC=1, then run
// SecurityFlatCodecGenerationTests).
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Core;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityMasterMapping
{
    /// <summary>The classes whose decode arm is generated from the binding table.</summary>
    internal static IReadOnlyList<string> GeneratedDecodeClasses { get; } =
    [
        "Option",
        "Future",
        "FxSpot",
        "Deposit",
        "MoneyMarketFund",
        "CertificateOfDeposit",
        "CommercialPaper",
        "TreasuryBill",
        "Repo",
        "CashSweep",
        "OtherSecurity",
        "PrivateFundInterest",
        "PrivateCompanyEquity",
        "RealEstateHolding",
        "Commodity",
        "CryptoCurrency",
        "Cfd",
        "Warrant",
        "FxForward",
        "NonDeliverableForward",
        "CreditDefaultSwap",
        "Swaption",
        "CapFloor",
        "StructuredNote",
    ];

    private static SecurityKind DecodeGeneratedOption(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewOption(new OptionTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.RequiredDateOnly("expiry"))),
            new ListingDetailTerms(
                ToOption(bound.OptionalString("optChainId")),
                ToOption(bound.OptionalString("settlementType")),
                ToOption(bound.OptionalBoolean("isAdjusted") ?? false),
                ToOption(bound.OptionalDateOnly("lastTradingDt")),
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            new ExerciseTerms(
                FSharpOption<string>.Some(bound.RequiredString("putCall")),
                ToOption(bound.RequiredDecimal("strike")),
                null,
                CanonicalExerciseStyle(ParseExerciseStyle(bound.OptionalString("exerciseStyle"))),
                FSharpOption<SecurityId>.Some(SecurityId.NewSecurityId(bound.RequiredGuid("underlyingId"))),
                null),
            new TradingParams(
                null,
                null,
                ToOption(bound.RequiredDecimal("multiplier")),
                null,
                null,
                null)));

    private static SecurityKind DecodeGeneratedFuture(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewFuture(new FutureTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.RequiredDateOnly("expiry"))),
            new ListingDetailTerms(
                null,
                ToOption(bound.OptionalString("settlementType")),
                null,
                ToOption(bound.OptionalDateOnly("lastTradingDt")),
                FSharpOption<string>.Some(bound.RequiredString("rootSymbol")),
                FSharpOption<string>.Some(bound.RequiredString("contractMonth")),
                ToOption(bound.OptionalDateOnly("firstNoticeDt")),
                ToOption(bound.OptionalDateOnly("deliveryMonthDt")),
                ToOption(bound.OptionalString("deliveryLocationCode")),
                ToOption(bound.OptionalBoolean("isRollTarget") ?? false),
                ToOption(bound.OptionalInt("rollWindowDays"))),
            new TradingParams(
                null,
                null,
                ToOption(bound.RequiredDecimal("multiplier")),
                null,
                null,
                null)));

    private static SecurityKind DecodeGeneratedFxSpot(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewFxSpot(new FxSpotTerms(
            new CurrencyPairTerms(
                FSharpOption<string>.Some(bound.RequiredString("baseCurrency")),
                FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")),
                null,
                null)));

    private static SecurityKind DecodeGeneratedDeposit(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewDeposit(new DepositTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.OptionalDateOnly("maturity"))),
            new IssuerRef(
                null,
                FSharpOption<string>.Some(bound.RequiredString("institutionName")),
                FSharpOption<string>.Some(bound.RequiredString("depositType")),
                null,
                null,
                null,
                null),
            new CouponTerms(
                FSharpOption<CouponKind>.Some(CouponKind.NewOtherCoupon("SimpleInterest")),
                ToOption(bound.OptionalDecimal("interestRate")),
                null,
                ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
            EconomicCallTermsModule.Scalar(bound.OptionalBoolean("isCallable") ?? false, null),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedMoneyMarketFund(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewMoneyMarketFund(new MoneyMarketFundTerms(
            new FundTerms(
                ToOption(bound.OptionalString("fundFamily")),
                ToOption(bound.OptionalInt("weightedAverageMaturityDays")),
                ToOption(bound.OptionalBoolean("sweepEligible") ?? false),
                ToOption(bound.OptionalBoolean("liquidityFeeEligible") ?? false))));

    private static SecurityKind DecodeGeneratedCertificateOfDeposit(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json, SecurityVersionedTermDecoding versionedTerms)
        => SecurityKind.NewCertificateOfDeposit(new CertificateOfDepositTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.RequiredDateOnly("maturity"))),
            new IssuerRef(
                FSharpOption<string>.Some(bound.RequiredString("issuerName")),
                null,
                null,
                null,
                null,
                null,
                null),
            new CouponTerms(
                FSharpOption<CouponKind>.Some(CouponKind.Fixed),
                ToOption(bound.OptionalDecimal("couponRate")),
                null,
                ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
            EconomicCallTermsModule.Scalar(bound.OptionalDateOnly("callableDate").HasValue, ToOption(bound.OptionalDateOnly("callableDate"))),
            new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),
                ReadCallPutSchedules(json, assetClass, versionedTerms)),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedCommercialPaper(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewCommercialPaper(new CommercialPaperTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.RequiredDateOnly("maturity"))),
            new IssuerRef(
                FSharpOption<string>.Some(bound.RequiredString("issuerName")),
                null,
                ToOption((bound.OptionalBoolean("isAssetBacked") ?? false) ? "AssetBacked" : null),
                null,
                null,
                null,
                null),
            new DiscountTerms(
                ToOption(bound.OptionalDecimal("discountRate")),
                null),
            new CouponTerms(
                FSharpOption<CouponKind>.Some(CouponKind.DiscountNote),
                null,
                null,
                ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedTreasuryBill(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewTreasuryBill(new TreasuryBillTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.RequiredDateOnly("maturity"))),
            new AuctionTerms(
                ToOption(bound.OptionalDateOnly("auctionDate")),
                null),
            new IssuerRef(
                ToOption("US Treasury"),
                null,
                ToOption(bound.OptionalString("cusip")),
                null,
                null,
                null,
                ToOption("US")),
            new DiscountTerms(
                ToOption(bound.OptionalDecimal("discountRate")),
                null),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedRepo(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewRepo(new RepoTerms(
            new FinancingTerms(
                FSharpOption<string>.Some(bound.RequiredString("counterparty")),
                ToOption(bound.OptionalString("collateralType")),
                ToOption(bound.OptionalDecimal("haircut")),
                ToOption(bound.RequiredDateOnly("startDate")),
                ToOption(bound.RequiredDateOnly("endDate"))),
            new DiscountTerms(
                null,
                ToOption(bound.OptionalDecimal("repoRate"))),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedCashSweep(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewCashSweep(new CashSweepTerms(
            new SweepTerms(
                FSharpOption<string>.Some(bound.RequiredString("programName")),
                FSharpOption<SweepVehicle>.Some(ToCodecSweepVehicle(bound.RequiredString("sweepVehicleType"))),
                ToCodecSweepFrequency(ToOption(bound.OptionalString("sweepFrequency"))),
                ToOption(bound.OptionalString("targetAccountType"))),
            new DiscountTerms(
                null,
                ToOption(bound.OptionalDecimal("yieldRate"))),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedOtherSecurity(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.OptionalDateOnly("maturity"))),
            new ClassificationDetailTerms(
                bound.RequiredString("category"),
                ToOption(bound.OptionalString("subType"))),
            new IssuerRef(
                ToOption(bound.OptionalString("issuerName")),
                null,
                ToOption(bound.OptionalString("settlementType")),
                null,
                null,
                null,
                null)));

    private static SecurityKind DecodeGeneratedPrivateFundInterest(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewPrivateFundInterest(new PrivateFundInterestTerms(
            new PrivateAssetValuationTerms(
                ToOption(bound.RequiredInt("vintage")),
                ToOption(bound.RequiredDateOnly("navDate")),
                ToOption(bound.OptionalString("lockup")),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            new NotionalTerms(
                null,
                null,
                ToOption(bound.RequiredDecimal("commitment")),
                ToOption(bound.OptionalDecimal("fundedAmount")),
                ToOption(bound.OptionalDecimal("unfundedAmount"))),
            new IssuerRef(
                FSharpOption<string>.Some(bound.RequiredString("gpSponsor")),
                null,
                FSharpOption<string>.Some(bound.RequiredString("strategy")),
                null,
                null,
                null,
                null)));

    private static SecurityKind DecodeGeneratedPrivateCompanyEquity(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewPrivateCompanyEquity(new PrivateCompanyEquityTerms(
            new EquityCapitalTerms(
                FSharpOption<string>.Some(bound.RequiredString("shareClass")),
                null,
                null),
            new PrivateAssetValuationTerms(
                null,
                null,
                null,
                ToOption(bound.OptionalDecimal("ownershipPercent")),
                ToOption(bound.RequiredDecimal("costBasis")),
                ToOption(bound.OptionalDecimal("latestValuation")),
                ToOption(bound.OptionalString("transferRestrictions")),
                null,
                null,
                null,
                null),
            new IssuerRef(
                FSharpOption<string>.Some(bound.RequiredString("issuer")),
                null,
                FSharpOption<string>.Some(bound.RequiredString("round")),
                null,
                null,
                null,
                null)));

    private static SecurityKind DecodeGeneratedRealEstateHolding(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewRealEstateHolding(new RealEstateHoldingTerms(
            new PrivateAssetValuationTerms(
                null,
                null,
                null,
                ToOption(bound.RequiredDecimal("ownershipPercent")),
                null,
                null,
                null,
                FSharpOption<string>.Some(bound.RequiredString("addressOrMarket")),
                ToOption(bound.RequiredDecimal("appraisalValue")),
                ToOption(bound.RequiredDateOnly("valuationDate")),
                ToOption(bound.OptionalString("debtStack"))),
            new IssuerRef(
                ToOption(bound.OptionalString("sponsor")),
                null,
                FSharpOption<string>.Some(bound.RequiredString("propertyType")),
                null,
                null,
                ToOption("RealEstate"),
                null)));

    private static SecurityKind DecodeGeneratedCommodity(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewCommodity(new CommodityTerms(
            new CommodityContractTerms(
                bound.RequiredString("commodityType"),
                ToOption(bound.OptionalString("denomination")),
                ToOption(bound.OptionalDecimal("contractSize")))));

    private static SecurityKind DecodeGeneratedCryptoCurrency(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewCryptoCurrency(new CryptoTerms(
            new CurrencyPairTerms(
                FSharpOption<string>.Some(bound.RequiredString("baseCurrency")),
                FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")),
                null,
                null),
            new DigitalAssetTerms(
                ToOption(bound.OptionalString("network")))));

    private static SecurityKind DecodeGeneratedCfd(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewCfd(new CfdTerms(
            new UnderlyingExposureTerms(
                bound.RequiredString("underlyingAssetClass"),
                ToOption(bound.OptionalString("underlyingDescription")),
                ToOption(bound.OptionalDecimal("leverage")))));

    private static SecurityKind DecodeGeneratedWarrant(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewWarrant(new WarrantTerms(
            new MaturityTerms(
                null,
                null,
                ToOption(bound.OptionalDateOnly("expiry"))),
            new ExerciseTerms(
                FSharpOption<string>.Some(bound.RequiredString("warrantType")),
                ToOption(bound.OptionalDecimal("strike")),
                null,
                null,
                FSharpOption<SecurityId>.Some(SecurityId.NewSecurityId(bound.RequiredGuid("underlyingId"))),
                null),
            new TradingParams(
                null,
                null,
                ToOption(bound.OptionalDecimal("multiplier")),
                null,
                null,
                null)));

    private static SecurityKind DecodeGeneratedFxForward(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewFxForward(new FxForwardTerms(
            new MaturityTerms(
                ToOption(bound.OptionalStrictDateOnly("tradeDate")),
                null,
                ToOption(bound.RequiredDateOnly("valueDate"))),
            new CurrencyPairTerms(
                FSharpOption<string>.Some(bound.RequiredString("baseCurrency")),
                FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")),
                null,
                ToOption(bound.OptionalStrictDecimal("forwardRate"))),
            new NotionalTerms(
                ToOption(bound.OptionalStrictDecimal("notional")),
                null,
                null,
                null,
                null),
            new SettlementTerms(
                ToOption(bound.OptionalConventionString("settlementType")))));

    private static SecurityKind DecodeGeneratedNonDeliverableForward(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewNonDeliverableForward(new NonDeliverableForwardTerms(
            new MaturityTerms(
                ToOption(bound.RequiredDateOnly("fixingDate")),
                null,
                ToOption(bound.RequiredDateOnly("valueDate"))),
            new CurrencyPairTerms(
                FSharpOption<string>.Some(bound.RequiredString("baseCurrency")),
                FSharpOption<string>.Some(bound.RequiredString("quoteCurrency")),
                FSharpOption<string>.Some(bound.RequiredString("settlementCurrency")),
                ToOption(bound.OptionalStrictDecimal("forwardRate"))),
            new NotionalTerms(
                ToOption(bound.OptionalStrictDecimal("notional")),
                null,
                null,
                null,
                null),
            new ReferenceDataSourceTerms(
                null,
                ToOption(bound.OptionalConventionString("fixingSource")))));

    private static SecurityKind DecodeGeneratedCreditDefaultSwap(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewCreditDefaultSwap(new CreditDefaultSwapTerms(
            new MaturityTerms(
                ToOption(bound.RequiredDateOnly("effectiveDate")),
                ToOption(bound.RequiredDateOnly("effectiveDate")),
                ToOption(bound.RequiredDateOnly("maturityDate"))),
            new CreditProtectionTerms(
                bound.RequiredString("referenceEntity"),
                ToOption(bound.OptionalConventionString("referenceObligation")),
                bound.RequiredString("protectionSide"),
                ToOption(bound.OptionalConventionString("seniority")),
                ToOption(bound.OptionalConventionString("restructuringClause")),
                ToOption(bound.OptionalStrictDecimal("upfrontPoints")),
                ToOption(bound.OptionalStrictDecimal("recoveryRateAssumption")),
                ToOption(bound.OptionalConventionString("indexName"))),
            new NotionalTerms(
                ToOption(bound.RequiredDecimal("notional")),
                null,
                null,
                null,
                null),
            new ContractualRateTerms(
                null,
                ToOption(bound.RequiredDecimal("fixedCouponBps")),
                null,
                ToOption(bound.OptionalConventionString("paymentFrequency")),
                ToDayCountOption(bound.OptionalConventionString("dayCount"), mode))));

    private static SecurityKind DecodeGeneratedSwaption(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewSwaption(new SwaptionTerms(
            new MaturityTerms(
                ToOption(bound.OptionalStrictDateOnly("underlyingEffectiveDate")),
                null,
                ToOption(bound.RequiredDateOnly("expiry"))),
            new ExerciseTerms(
                FSharpOption<string>.Some(bound.RequiredString("payerReceiver")),
                null,
                ToOption(bound.RequiredDecimal("strikeRate")),
                ToOption(bound.OptionalConventionString("exerciseStyle")),
                null,
                null),
            new NotionalTerms(
                ToOption(bound.RequiredDecimal("notional")),
                null,
                null,
                null,
                null),
            new UnderlyingSwapTerms(
                bound.RequiredDateOnly("underlyingMaturityDate"),
                ToOption(bound.OptionalConventionString("floatingIndex")),
                ToOption(bound.OptionalConventionString("fixedLegPaymentFrequency")),
                ToDayCountOption(bound.OptionalConventionString("fixedLegDayCount"), mode, "fixedLegDayCount")),
            new SettlementTerms(
                ToOption(bound.OptionalConventionString("settlementType"))),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedCapFloor(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewCapFloor(new CapFloorTerms(
            new MaturityTerms(
                ToOption(bound.RequiredDateOnly("effectiveDate")),
                null,
                ToOption(bound.RequiredDateOnly("maturityDate"))),
            new ExerciseTerms(
                FSharpOption<string>.Some(bound.RequiredString("capFloorType")),
                null,
                null,
                null,
                null,
                ToOption(bound.OptionalStrictDecimal("premiumAmount"))),
            new FloatingRateTerms(
                FSharpOption<string>.Some(bound.RequiredString("referenceIndex")),
                null,
                ToOption(bound.OptionalConventionString("resetFrequency")),
                ToOption(bound.OptionalStrictDecimal("floorStrike")),
                ToOption(bound.OptionalStrictDecimal("capStrike"))),
            new NotionalTerms(
                ToOption(bound.RequiredDecimal("notional")),
                null,
                null,
                null,
                null),
            new ContractualRateTerms(
                null,
                null,
                null,
                null,
                ToDayCountOption(bound.OptionalConventionString("dayCount"), mode)),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));

    private static SecurityKind DecodeGeneratedStructuredNote(SchemaBoundTerms bound, SecurityKindMappingMode mode, string assetClass, JsonElement json)
        => SecurityKind.NewStructuredNote(new StructuredNoteTerms(
            new MaturityTerms(
                null,
                ToOption(bound.OptionalStrictDateOnly("issueDate")),
                ToOption(bound.RequiredDateOnly("maturity"))),
            new StructuredPayoffTerms(
                bound.RequiredString("underlyingDescription"),
                ToOption(bound.OptionalStrictDecimal("principalProtectionPct")),
                ToOption(bound.OptionalStrictDecimal("participationRate")),
                ToOption(bound.OptionalStrictDecimal("returnCap")),
                ToOption(bound.OptionalStrictDecimal("barrierLevel"))),
            new NotionalTerms(
                null,
                ToOption(bound.OptionalStrictDecimal("denomination")),
                null,
                null,
                null),
            new IssuerRef(
                FSharpOption<string>.Some(bound.RequiredString("issuerName")),
                null,
                null,
                null,
                null,
                null,
                null),
            new ContractualRateTerms(
                ToOption(bound.OptionalStrictDecimal("couponRate")),
                null,
                null,
                ToOption(bound.OptionalConventionString("paymentFrequency")),
                null),
            ToRateConventionOption(SecurityRecordRateUnit.ReadToken(assetClass, json, mode == SecurityKindMappingMode.Write))));
}
