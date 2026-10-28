using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

public sealed record SecurityValidationContext(
    SecurityProjectionRecord Record,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Defines validation rules for one supported Security Master asset class.
/// </summary>
public interface ISecurityAssetClassValidator
{
    string AssetClass { get; }
    IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context);
}

/// <summary>
/// Indexes asset-class validators and resolves the validator for a requested asset class.
/// </summary>
public sealed class AssetClassValidatorRegistry
{
    private readonly IReadOnlyDictionary<string, ISecurityAssetClassValidator> _validators;

    public AssetClassValidatorRegistry(IEnumerable<ISecurityAssetClassValidator> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);

        _validators = validators.ToDictionary(
            static validator => validator.AssetClass,
            static validator => validator,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> SupportedAssetClasses => _validators.Keys.ToArray();

    public bool TryGetValidator(string assetClass, out ISecurityAssetClassValidator validator)
        => _validators.TryGetValue(assetClass, out validator!);

    public static AssetClassValidatorRegistry CreateDefault()
        => CreateDefault(StaticSecurityAssetProfileCatalog.CreateDefault());

    /// <summary>
    /// The default registry: every class's hand-written semantic rules composed with the rules
    /// generated from its term schema (<see cref="SchemaTermsAssetClassValidator"/>).
    /// </summary>
    public static AssetClassValidatorRegistry CreateDefault(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new(SchemaBackedAssetClassValidator.WrapAll(DefaultAssetClassValidators.Create(assetProfileCatalog)));
}

internal static class SecurityValidationIssueFactory
{
    public static SecurityValidationIssueDto Create(
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        IReadOnlyList<string> affectedFields,
        string suggestedAction,
        IReadOnlyList<SecurityEvidenceLinkDto>? evidenceLinks = null)
        => new(
            severity,
            code,
            title,
            message,
            affectedFields,
            suggestedAction,
            evidenceLinks ?? Array.Empty<SecurityEvidenceLinkDto>());
}

internal static class DefaultAssetClassValidators
{
    public static IReadOnlyList<ISecurityAssetClassValidator> Create(ISecurityAssetProfileCatalog assetProfileCatalog)
    {
        ArgumentNullException.ThrowIfNull(assetProfileCatalog);
        var optionalProfileValidator = new SecurityAssetProfileAssetClassValidator("OtherSecurity", assetProfileCatalog, requireProfileReference: false);
        var requiredProfileValidator = new SecurityAssetProfileAssetClassValidator("CustomAsset", assetProfileCatalog, requireProfileReference: true);

        return
        [
            new JsonAssetClassValidator(
                "Equity",
                [
                    FieldRule.OptionalNonNegative("preferredTerms.dividendRate", "SM_EQUITY_DIVIDEND_RATE_INVALID", "Preferred dividend rate is invalid", "Preferred equity dividend rates must be zero or greater."),
                    FieldRule.OptionalPositive("convertibleTerms.conversionRatio", "SM_EQUITY_CONVERSION_RATIO_INVALID", "Conversion ratio is invalid", "Convertible equity conversion ratios must be greater than zero."),
                    DateOrderRule.Optional("convertibleTerms.conversionStartDate", "convertibleTerms.conversionEndDate", "SM_EQUITY_CONVERSION_WINDOW_INVALID", "Conversion window is invalid", "Convertible equity conversion start date must be on or before conversion end date.")
                ]),
            new JsonAssetClassValidator(
                "Option",
                [
                    FieldRule.OptionalAllowedString("putCall", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.OptionPutCall), "SM_OPTION_PUT_CALL_INVALID", "Option put/call flag is invalid", "Option putCall must be Put or Call."),
                    FieldRule.OptionalPositive("strike", "SM_OPTION_STRIKE_INVALID", "Option strike is invalid", "Option strike must be greater than zero."),
                    FieldRule.OptionalPositive("multiplier", "SM_OPTION_MULTIPLIER_INVALID", "Option multiplier is invalid", "Option multiplier must be greater than zero.")
                ]),
            new JsonAssetClassValidator(
                "Future",
                [
                    FieldRule.OptionalPositive("multiplier", "SM_FUTURE_MULTIPLIER_INVALID", "Future multiplier is invalid", "Future multiplier must be greater than zero."),
                    DateOrderRule.Optional("lastTradingDt", "expiry", "SM_FUTURE_LAST_TRADING_DATE_INVALID", "Future last-trading date is invalid", "Future last-trading date must be on or before expiry."),
                    DateOrderRule.Optional("firstNoticeDt", "expiry", "SM_FUTURE_FIRST_NOTICE_DATE_INVALID", "Future first-notice date is invalid", "Future first-notice date must be on or before expiry.")
                ]),
            new JsonAssetClassValidator(
                "Bond",
                [
                    FieldRule.OptionalNonNegative("couponRate", "SM_BOND_COUPON_RATE_INVALID", "Bond coupon rate is invalid", "Fixed coupon rates must be zero or greater."),
                    FieldRule.OptionalNonNegative("spreadBps", "SM_BOND_FLOATING_SPREAD_INVALID", "Bond floating spread is invalid", "Floating-rate spread must be zero or greater when supplied."),
                    DateOrderRule.Optional("issueDate", "maturity", "SM_BOND_ISSUE_DATE_INVALID", "Bond issue date is invalid", "Bond issue date must be on or before maturity."),
                    DateOrderRule.Optional("callDate", "maturity", "SM_BOND_CALL_DATE_INVALID", "Bond call date is invalid", "Bond call date must be on or before maturity."),
                    // ADR-022 canonical-home enforcement: securitized products (MBS/ABS/CLO/CMBS/CDO
                    // tranches and IO/PO strips) have ONE canonical home — StructuredCredit, which
                    // carries the tranche, pool, factor-schedule, and maturity economics that pool
                    // amortization and cash-flow math require and BondTerms lacks. A Bond classified
                    // into a securitized subclass is a label those pipelines cannot act on, and a
                    // second modeling route reporting/risk/reconciliation cannot partition against.
                    DisallowedStringValuesRule.Create(
                        "subclass",
                        ["AssetBacked", "MortgageBacked", "AgencyMbs", "CommercialMbs", "Cmo", "Clo", "Cdo", "PrincipalOnly", "InterestOnly", "InverseInterestOnly"],
                        SecurityValidationSeverityDto.Error,
                        "SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL",
                        "Securitized products must be modeled as StructuredCredit",
                        "Bond subclass '{0}' models a securitized product; StructuredCredit is the canonical home for MBS/ABS/CLO/CMBS/CDO tranches and IO/PO strips (ADR-022).",
                        "Re-model the instrument as StructuredCredit (tranche, pool, factor schedule) through a governed amendment so downstream cash-flow, amortization, and reconciliation pipelines can act on its economics.")
                ]),
            new JsonAssetClassValidator(
                "FxSpot",
                [
                    DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_FX_PAIR_INVALID", "FX currency pair is invalid", "FX base and quote currencies must differ.")
                ]),
            new JsonAssetClassValidator(
                "Deposit",
                [
                    FieldRule.OptionalNonNegative("interestRate", "SM_DEPOSIT_INTEREST_RATE_INVALID", "Deposit interest rate is invalid", "Deposit interest rates must be zero or greater.")
                ]),
            new JsonAssetClassValidator(
                "MoneyMarketFund",
                [
                    FieldRule.OptionalNonNegative("weightedAverageMaturityDays", "SM_MMF_WAM_INVALID", "Money market WAM is invalid", "Weighted average maturity must be zero or greater when supplied.")
                ]),
            new JsonAssetClassValidator(
                "CertificateOfDeposit",
                [
                    FieldRule.OptionalNonNegative("couponRate", "SM_CD_COUPON_RATE_INVALID", "Certificate of deposit coupon is invalid", "Certificate of deposit coupon rates must be zero or greater."),
                    DateOrderRule.Optional("callableDate", "maturity", "SM_CD_CALLABLE_DATE_INVALID", "Certificate of deposit callable date is invalid", "Callable date must be on or before maturity.")
                ]),
            new JsonAssetClassValidator(
                "CommercialPaper",
                [
                    FieldRule.OptionalNonNegative("discountRate", "SM_CP_DISCOUNT_RATE_INVALID", "Commercial paper discount rate is invalid", "Commercial paper discount rates must be zero or greater.")
                ]),
            new JsonAssetClassValidator(
                "TreasuryBill",
                [
                    DateOrderRule.Optional("auctionDate", "maturity", "SM_TBILL_AUCTION_DATE_INVALID", "Treasury bill auction date is invalid", "Auction date must be on or before maturity."),
                    FieldRule.OptionalNonNegative("discountRate", "SM_TBILL_DISCOUNT_RATE_INVALID", "Treasury bill discount rate is invalid", "Treasury bill discount rates must be zero or greater.")
                ]),
            new JsonAssetClassValidator(
                "Repo",
                [
                    DateOrderRule.Optional("startDate", "endDate", "SM_REPO_DATE_RANGE_INVALID", "Repo date range is invalid", "Repo start date must be on or before end date."),
                    FieldRule.OptionalNonNegative("repoRate", "SM_REPO_RATE_INVALID", "Repo rate is invalid", "Repo rates must be zero or greater."),
                    FieldRule.OptionalNonNegative("haircut", "SM_REPO_HAIRCUT_INVALID", "Repo haircut is invalid", "Repo haircuts must be zero or greater.")
                ]),
            new JsonAssetClassValidator(
                "CashSweep",
                [
                    FieldRule.OptionalNonNegative("yieldRate", "SM_CASH_SWEEP_YIELD_RATE_INVALID", "Cash sweep yield is invalid", "Cash sweep yield rates must be zero or greater.")
                ]),
            new CompositeAssetClassValidator(
                "OtherSecurity",
                [
                    new JsonAssetClassValidator(
                        "OtherSecurity",
                        [
                        ]),
                    optionalProfileValidator
                ]),
            new JsonAssetClassValidator(
                "Swap",
                [
                    DateOrderRule.Optional("effectiveDate", "maturityDate", "SM_SWAP_DATE_RANGE_INVALID", "Swap date range is invalid", "Swap effective date must be on or before maturity date."),
                    FieldRule.OptionalArrayNotEmpty("legs", "SM_SWAP_LEGS_REQUIRED", "Swap legs are missing", "Swaps must include at least one leg.")
                ]),
            new JsonAssetClassValidator(
                "DirectLoan",
                [
                ]),
            new JsonAssetClassValidator(
                "StructuredCredit",
                [
                    FieldRule.OptionalPositive("originalFace", "SM_STRUCTURED_CREDIT_ORIGINAL_FACE_INVALID", "Structured credit original face is invalid", "Structured credit original face must be greater than zero."),
                    FieldRule.OptionalNonNegative("currentFactor", "SM_STRUCTURED_CREDIT_CURRENT_FACTOR_INVALID", "Structured credit current factor is invalid", "Structured credit current factor must be zero or greater when supplied."),
                    // The free-text factorSchedule is a trustee-report pointer, not a schedule: pool
                    // amortization and cash-flow projection read the dated factorScheduleEntries.
                    TypedScheduleBackingRule.Create(
                        "factorSchedule",
                        "factorScheduleEntries",
                        SecurityValidationSeverityDto.Warning,
                        "SM_STRUCTURED_CREDIT_FACTOR_ENTRIES_MISSING",
                        "Structured credit factor schedule has no dated entries",
                        "factorSchedule names a trustee or servicer report but the record carries no dated factorScheduleEntries; calculated paydown and cash-flow projection cannot use a report reference.",
                        "Add dated factorScheduleEntries ({asOfDate, factor}) taken from the referenced report."),
                    FieldRule.OptionalAllowedString("structuredCreditKind", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.StructuredCreditKind), "SM_STRUCTURED_CREDIT_KIND_INVALID", "Structured credit kind is not a governed value", "Structured credit kind must be one of the structured-credit-kind taxonomy values when supplied."),
                    FieldRule.OptionalString("seniority", "SM_STRUCTURED_CREDIT_SENIORITY_INVALID", "Structured credit seniority is not a string", "Structured credit seniority must be a non-blank string when supplied."),
                    FieldRule.OptionalFraction("attachmentPoint", "SM_STRUCTURED_CREDIT_ATTACHMENT_INVALID", "Structured credit attachment point is invalid", "Structured credit attachment point must be a fraction between zero and one when supplied."),
                    FieldRule.OptionalFraction("detachmentPoint", "SM_STRUCTURED_CREDIT_DETACHMENT_INVALID", "Structured credit detachment point is invalid", "Structured credit detachment point must be a fraction between zero and one when supplied."),
                    NumericOrderRule.Optional("attachmentPoint", "detachmentPoint", "SM_STRUCTURED_CREDIT_ATTACHMENT_ABOVE_DETACHMENT", "Structured credit attachment point exceeds detachment point", "Structured credit attachment point must be at or below the detachment point when both are supplied."),
                    FieldRule.OptionalNonNegative("weightedAverageCoupon", "SM_STRUCTURED_CREDIT_WAC_INVALID", "Structured credit weighted-average coupon is invalid", "Structured credit weighted-average coupon must be zero or greater when supplied."),
                    FieldRule.OptionalNonNegative("weightedAverageMaturityMonths", "SM_STRUCTURED_CREDIT_WAM_INVALID", "Structured credit weighted-average maturity is invalid", "Structured credit weighted-average maturity months must be zero or greater when supplied."),
                    FieldRule.OptionalNonNegative("weightedAverageLoanAgeMonths", "SM_STRUCTURED_CREDIT_WALA_INVALID", "Structured credit weighted-average loan age is invalid", "Structured credit weighted-average loan age months must be zero or greater when supplied."),
                    // The ordering rules below read an unparseable date as "not supplied"; these two
                    // rules are what report a supplied period end that is not a date.
                    FieldRule.OptionalDate("reinvestmentPeriodEnd", "SM_STRUCTURED_CREDIT_REINVESTMENT_END_NOT_A_DATE", "Structured credit reinvestment period end is not a date", "Structured credit reinvestment period end must be an ISO-8601 date when supplied."),
                    FieldRule.OptionalDate("nonCallPeriodEnd", "SM_STRUCTURED_CREDIT_NON_CALL_END_NOT_A_DATE", "Structured credit non-call period end is not a date", "Structured credit non-call period end must be an ISO-8601 date when supplied."),
                    DateOrderRule.Optional("reinvestmentPeriodEnd", "maturity", "SM_STRUCTURED_CREDIT_REINVESTMENT_END_INVALID", "Structured credit reinvestment period end is invalid", "Structured credit reinvestment period end must be on or before maturity."),
                    DateOrderRule.Optional("nonCallPeriodEnd", "maturity", "SM_STRUCTURED_CREDIT_NON_CALL_END_INVALID", "Structured credit non-call period end is invalid", "Structured credit non-call period end must be on or before maturity.")
                ]),
            new JsonAssetClassValidator(
                "PrivateFundInterest",
                [
                    FieldRule.OptionalPositive("vintage", "SM_PRIVATE_FUND_VINTAGE_INVALID", "Private fund vintage is invalid", "Private fund interests must include a positive vintage year."),
                    FieldRule.OptionalPositive("commitment", "SM_PRIVATE_FUND_COMMITMENT_INVALID", "Private fund commitment is invalid", "Private fund commitments must be greater than zero."),
                    FieldRule.OptionalNonNegative("fundedAmount", "SM_PRIVATE_FUND_FUNDED_INVALID", "Private fund funded amount is invalid", "Private fund funded amounts must be zero or greater when supplied."),
                    FieldRule.OptionalNonNegative("unfundedAmount", "SM_PRIVATE_FUND_UNFUNDED_INVALID", "Private fund unfunded amount is invalid", "Private fund unfunded amounts must be zero or greater when supplied."),
                ]),
            new JsonAssetClassValidator(
                "PrivateCompanyEquity",
                [
                    FieldRule.OptionalNonNegative("ownershipPercent", "SM_PRIVATE_COMPANY_OWNERSHIP_INVALID", "Private company ownership percent is invalid", "Private company ownership percentages must be zero or greater when supplied."),
                    FieldRule.OptionalPositive("costBasis", "SM_PRIVATE_COMPANY_COST_BASIS_INVALID", "Private company cost basis is invalid", "Private company equity records must include a positive cost basis."),
                    FieldRule.OptionalNonNegative("latestValuation", "SM_PRIVATE_COMPANY_VALUATION_INVALID", "Private company valuation is invalid", "Private company latest valuation must be zero or greater when supplied.")
                ]),
            new JsonAssetClassValidator(
                "RealEstateHolding",
                [
                    FieldRule.OptionalPositive("ownershipPercent", "SM_REAL_ESTATE_OWNERSHIP_INVALID", "Real estate ownership percent is invalid", "Real estate ownership percentages must be greater than zero."),
                    FieldRule.OptionalPositive("appraisalValue", "SM_REAL_ESTATE_APPRAISAL_INVALID", "Real estate appraisal value is invalid", "Real estate holdings must include a positive appraisal value."),
                ]),
            new JsonAssetClassValidator(
                "CommitmentGuarantee",
                [
                    FieldRule.OptionalPositive("committedAmount", "SM_COMMITMENT_GUARANTEE_AMOUNT_INVALID", "Committed or guaranteed amount is invalid", "Commitment and guarantee amounts must be greater than zero."),
                    FieldRule.OptionalNonNegative("unfundedAmount", "SM_COMMITMENT_GUARANTEE_EXPOSURE_INVALID", "Unfunded/exposure amount is invalid", "Unfunded or exposure amounts must be zero or greater when supplied."),
                    DateOrderRule.Optional("effectiveDate", "expiryDate", "SM_COMMITMENT_GUARANTEE_DATE_RANGE_INVALID", "Commitment/guarantee date range is invalid", "Commitment or guarantee effective date must be on or before expiry date."),
                    FieldRule.OptionalNonNegative("feeRate", "SM_COMMITMENT_GUARANTEE_FEE_INVALID", "Commitment/guarantee fee rate is invalid", "Commitment or guarantee fee rates must be zero or greater when supplied.")
                ]),
            new JsonAssetClassValidator(
                "Commodity",
                [
                    FieldRule.OptionalPositive("contractSize", "SM_COMMODITY_CONTRACT_SIZE_INVALID", "Commodity contract size is invalid", "Commodity contract size must be greater than zero when supplied.")
                ]),
            new JsonAssetClassValidator(
                "CryptoCurrency",
                [
                    DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_CRYPTO_PAIR_INVALID", "Crypto pair is invalid", "Crypto base and quote currencies must differ.")
                ]),
            new JsonAssetClassValidator(
                "Cfd",
                [
                    FieldRule.OptionalPositive("leverage", "SM_CFD_LEVERAGE_INVALID", "CFD leverage is invalid", "CFD leverage must be greater than zero when supplied.")
                ]),
            new JsonAssetClassValidator(
                "Warrant",
                [
                    FieldRule.OptionalAllowedString("warrantType", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.WarrantType), "SM_WARRANT_TYPE_INVALID", "Warrant type is invalid", "Warrant type must be Put or Call."),
                    FieldRule.OptionalPositive("strike", "SM_WARRANT_STRIKE_INVALID", "Warrant strike is invalid", "Warrant strike must be greater than zero when supplied."),
                    FieldRule.OptionalPositive("multiplier", "SM_WARRANT_MULTIPLIER_INVALID", "Warrant multiplier is invalid", "Warrant multiplier must be greater than zero when supplied.")
                ]),
            new JsonAssetClassValidator(
                "FxForward",
                [
                    DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_FX_FORWARD_PAIR_INVALID", "FX forward currency pair is invalid", "FX forward base and quote currencies must differ."),
                    DateOrderRule.Optional("tradeDate", "valueDate", "SM_FX_FORWARD_TRADE_DATE_INVALID", "FX forward trade date is after value date", "FX forward trade date must be on or before the value date."),
                    FieldRule.OptionalPositive("notional", "SM_FX_FORWARD_NOTIONAL_INVALID", "FX forward notional is invalid", "FX forward notional must be greater than zero when present."),
                    FieldRule.OptionalPositive("forwardRate", "SM_FX_FORWARD_RATE_INVALID", "FX forward rate is invalid", "FX forward rate must be greater than zero when present.")
                ]),
            new JsonAssetClassValidator(
                "NonDeliverableForward",
                [
                    DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_NDF_PAIR_INVALID", "NDF currency pair is invalid", "NDF base and quote currencies must differ."),
                    DateOrderRule.Optional("fixingDate", "valueDate", "SM_NDF_FIXING_DATE_INVALID", "NDF fixing date is after value date", "NDF fixing date must be on or before the value date."),
                    FieldRule.OptionalPositive("notional", "SM_NDF_NOTIONAL_INVALID", "NDF notional is invalid", "NDF notional must be greater than zero when present."),
                    FieldRule.OptionalPositive("forwardRate", "SM_NDF_RATE_INVALID", "NDF forward rate is invalid", "NDF forward rate must be greater than zero when present.")
                ]),
            new JsonAssetClassValidator(
                "CreditDefaultSwap",
                [
                    FieldRule.OptionalAllowedString("protectionSide", ["Buy", "Sell"], "SM_CDS_PROTECTION_SIDE_INVALID", "CDS protection side is invalid", "CDS protection side must be 'Buy' or 'Sell'."),
                    FieldRule.OptionalPositive("notional", "SM_CDS_NOTIONAL_REQUIRED", "CDS notional is missing or invalid", "Credit default swaps must include a notional greater than zero."),
                    DateOrderRule.Optional("effectiveDate", "maturityDate", "SM_CDS_DATE_RANGE_INVALID", "CDS date range is invalid", "CDS effective date must be on or before the maturity date."),
                    FieldRule.OptionalNonNegative("fixedCouponBps", "SM_CDS_FIXED_COUPON_INVALID", "CDS fixed coupon is missing or invalid", "Credit default swaps must include a running fixed coupon of zero or more basis points."),
                    FieldRule.OptionalFraction("recoveryRateAssumption", "SM_CDS_RECOVERY_RATE_INVALID", "CDS recovery rate is invalid", "CDS recovery rate assumption must be a fraction between 0 and 1.")
                ]),
            new JsonAssetClassValidator(
                "Swaption",
                [
                    FieldRule.OptionalAllowedString("payerReceiver", ["Payer", "Receiver"], "SM_SWAPTION_PAYER_RECEIVER_INVALID", "Swaption payer/receiver is invalid", "Swaptions must be 'Payer' or 'Receiver'."),
                    FieldRule.OptionalAllowedString("exerciseStyle", ["European", "American", "Bermudan"], "SM_SWAPTION_EXERCISE_STYLE_INVALID", "Swaption exercise style is invalid", "Swaption exercise style must be European, American or Bermudan when present."),
                    DateOrderRule.Optional("expiry", "underlyingMaturityDate", "SM_SWAPTION_EXPIRY_AFTER_MATURITY", "Swaption expiry is after the underlying maturity", "Swaption expiry must be on or before the underlying swap's maturity date."),
                    DateOrderRule.OptionalStrict("underlyingEffectiveDate", "underlyingMaturityDate", "SM_SWAPTION_UNDERLYING_DATES_INVALID", "Swaption underlying dates are invalid", "The underlying swap's effective date must fall before its maturity date."),
                    DateOrderRule.Optional("expiry", "underlyingEffectiveDate", "SM_SWAPTION_EFFECTIVE_BEFORE_EXPIRY", "Swaption underlying starts before expiry", "The underlying swap's effective date must be on or after the swaption expiry."),
                    FieldRule.OptionalPositive("notional", "SM_SWAPTION_NOTIONAL_REQUIRED", "Swaption notional is missing or invalid", "Swaptions must include a notional greater than zero.")
                ]),
            new JsonAssetClassValidator(
                "CapFloor",
                [
                    FieldRule.OptionalAllowedString("capFloorType", ["Cap", "Floor", "Collar"], "SM_CAP_FLOOR_TYPE_INVALID", "Cap/floor type is invalid", "Cap/floor type must be Cap, Floor or Collar."),
                    FieldRule.OptionalPositive("notional", "SM_CAP_FLOOR_NOTIONAL_REQUIRED", "Cap/floor notional is missing or invalid", "Caps and floors must include a notional greater than zero."),
                    DateOrderRule.Optional("effectiveDate", "maturityDate", "SM_CAP_FLOOR_DATE_RANGE_INVALID", "Cap/floor date range is invalid", "Cap/floor effective date must be on or before the maturity date."),
                    new RequiredNumberWhenRule("capFloorType", ["Cap", "Collar"], "capStrike", "SM_CAP_FLOOR_CAP_STRIKE_REQUIRED", "Cap strike is missing", "A Cap or Collar must include its cap strike."),
                    new RequiredNumberWhenRule("capFloorType", ["Floor", "Collar"], "floorStrike", "SM_CAP_FLOOR_FLOOR_STRIKE_REQUIRED", "Floor strike is missing", "A Floor or Collar must include its floor strike."),
                    NumericOrderRule.Optional("floorStrike", "capStrike", "SM_CAP_FLOOR_STRIKE_ORDER_INVALID", "Collar floor strike is above the cap strike", "A collar's floor strike must be at or below its cap strike."),
                    FieldRule.OptionalNonNegative("premiumAmount", "SM_CAP_FLOOR_PREMIUM_INVALID", "Cap/floor premium is invalid", "Cap/floor premium must be zero or greater when present.")
                ]),
            new JsonAssetClassValidator(
                "StructuredNote",
                [
                    DateOrderRule.Optional("issueDate", "maturity", "SM_STRUCTURED_NOTE_ISSUE_AFTER_MATURITY", "Structured note issue date is after maturity", "Structured note issue date must be on or before maturity."),
                    FieldRule.OptionalPositive("denomination", "SM_STRUCTURED_NOTE_DENOMINATION_INVALID", "Structured note denomination is invalid", "Structured note denomination must be greater than zero when present."),
                    FieldRule.OptionalFraction("principalProtectionPct", "SM_STRUCTURED_NOTE_PROTECTION_INVALID", "Structured note principal protection is invalid", "Principal protection must be a fraction between 0 and 1 (1 = fully protected)."),
                    FieldRule.OptionalNonNegative("participationRate", "SM_STRUCTURED_NOTE_PARTICIPATION_INVALID", "Structured note participation is invalid", "Participation rate must be zero or greater when present."),
                    FieldRule.OptionalNonNegative("returnCap", "SM_STRUCTURED_NOTE_RETURN_CAP_INVALID", "Structured note return cap is invalid", "Return cap must be zero or greater when present."),
                    FieldRule.OptionalNonNegative("couponRate", "SM_STRUCTURED_NOTE_COUPON_INVALID", "Structured note coupon is invalid", "Structured note coupon must be zero or greater when present."),
                    FieldRule.OptionalPositive("barrierLevel", "SM_STRUCTURED_NOTE_BARRIER_INVALID", "Structured note barrier is invalid", "Structured note barrier must be greater than zero when present.")
                ]),
            // ADR-022 canonical-home guidance: MoneyMarketFund is the canonical home for
            // stable-NAV money-market and government liquidity vehicles; the generic fund wrapper
            // flagged stable-NAV is the overlap the review called out. Warning (not Error): the
            // InvestmentFundTerms contract documents the flag, so existing records are steered,
            // not blocked, toward the canonical class.
            new JsonAssetClassValidator(
                "InvestmentFund",
                [
                    DiscouragedTrueBooleanRule.Create(
                        "isStableNav",
                        SecurityValidationSeverityDto.Warning,
                        "SM_INVESTMENT_FUND_STABLE_NAV_NONCANONICAL",
                        "Stable-NAV vehicles belong in MoneyMarketFund",
                        "This InvestmentFund record is flagged stable-NAV; MoneyMarketFund is the canonical home for stable-NAV money-market and government liquidity vehicles (ADR-022).",
                        "Model the vehicle as MoneyMarketFund (fund family, sweep eligibility, WAM, liquidity-fee eligibility) through a governed amendment.")
                ]),
            new CompositeAssetClassValidator(
                "CustomAsset",
                [
                    requiredProfileValidator,
                    // ADR-022: a CustomAsset envelope whose classification metadata names a
                    // securitized product should resolve to StructuredCredit via a registered
                    // reclassifying profile (e.g. structured-credit-io-po), not stay a generic
                    // custom asset — CustomAsset remains the home for private/other assets with no
                    // first-class kind. Warning: the profile map, not metadata, decides identity.
                    new JsonAssetClassValidator(
                        "CustomAsset",
                        [
                            DisallowedStringValuesRule.Create(
                                "category",
                                ["MBS", "ABS", "CLO", "CMBS", "StructuredCredit"],
                                SecurityValidationSeverityDto.Warning,
                                "SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL",
                                "Securitized products belong in StructuredCredit",
                                "CustomAsset category '{0}' names a securitized product; StructuredCredit is the canonical home for MBS/ABS/CLO/CMBS tranches (ADR-022).",
                                "Pin the record to a profile that resolves to StructuredCredit (e.g. structured-credit-io-po) or re-model it as a first-class StructuredCredit record.")
                        ])
                ])
        ];
    }
}

internal sealed class JsonAssetClassValidator : ISecurityAssetClassValidator
{
    private readonly IReadOnlyList<ISecurityValidationRule> _rules;

    public JsonAssetClassValidator(string assetClass, IReadOnlyList<ISecurityValidationRule> rules)
    {
        AssetClass = assetClass;
        _rules = rules;
    }

    public string AssetClass { get; }

    public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context)
    {
        var issues = new List<SecurityValidationIssueDto>();
        foreach (var rule in _rules)
        {
            var issue = rule.Validate(context);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        return issues;
    }
}

internal sealed class CompositeAssetClassValidator : ISecurityAssetClassValidator
{
    private readonly IReadOnlyList<ISecurityAssetClassValidator> _validators;

    public CompositeAssetClassValidator(string assetClass, IReadOnlyList<ISecurityAssetClassValidator> validators)
    {
        AssetClass = assetClass;
        _validators = validators;
    }

    public string AssetClass { get; }

    public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context)
    {
        var issues = new List<SecurityValidationIssueDto>();
        foreach (var validator in _validators)
        {
            issues.AddRange(validator.Validate(context));
        }

        return issues;
    }
}

internal sealed class SecurityAssetProfileAssetClassValidator : ISecurityAssetClassValidator
{
    private static readonly HashSet<string> ReservedFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "assetClass",
        "customProfileId",
        "profileVersion",
        "profileFields",
        "profileApproval",
        "schemaVersion",
        SecurityTermsEnvelope.FormatKey,
        SecurityTermsEnvelope.AssetClassTermsVersionKey,
        SecurityTermsEnvelope.RetainedEconomicTermsKey,
        "securityId",
        "status",
        "version",
        "provenance"
    };

    private readonly ISecurityAssetProfileCatalog _assetProfileCatalog;
    private readonly bool _requireProfileReference;
    private readonly bool _enforceWriteTimeGovernance;

    public SecurityAssetProfileAssetClassValidator(
        string assetClass,
        ISecurityAssetProfileCatalog assetProfileCatalog,
        bool requireProfileReference,
        bool enforceWriteTimeGovernance = false)
    {
        AssetClass = assetClass;
        _assetProfileCatalog = assetProfileCatalog;
        _requireProfileReference = requireProfileReference;
        _enforceWriteTimeGovernance = enforceWriteTimeGovernance;
    }

    public string AssetClass { get; }

    public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context)
    {
        var issues = new List<SecurityValidationIssueDto>();
        var assetSpecificTerms = context.Record.AssetSpecificTerms;
        var hasProfileId = JsonValidationReader.TryGetString(assetSpecificTerms, "customProfileId", out var profileId);
        if (!hasProfileId)
        {
            if (_requireProfileReference)
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_ID_REQUIRED",
                    "Custom asset profile is missing",
                    "CustomAsset records must reference the approved custom asset profile used to interpret their typed fields.",
                    ["assetSpecificTerms.customProfileId"],
                    "Select an approved asset profile before creating or amending the custom asset."));
            }

            return issues;
        }

        if (!JsonValidationReader.TryGetInt(assetSpecificTerms, "profileVersion", out var profileVersion) || profileVersion <= 0)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_VERSION_REQUIRED",
                "Custom asset profile version is missing",
                "Profile-backed securities must pin the approved profile version used at creation so historical records remain interpretable after profile changes.",
                ["assetSpecificTerms.profileVersion"],
                "Persist the approved profile version with the Security Master terms."));
            return issues;
        }

        if (!_assetProfileCatalog.TryGetProfile(profileId, profileVersion, out var profile))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_VERSION_UNKNOWN",
                "Custom asset profile version is unknown",
                $"Profile '{profileId}' version '{profileVersion}' is not registered in the approved asset profile catalog.",
                ["assetSpecificTerms.customProfileId", "assetSpecificTerms.profileVersion"],
                "Choose an approved profile version or migrate the record through a governed profile rollback/amendment."));
            return issues;
        }

        issues.AddRange(ValidateProfileDefinition(profile));
        if (profile.Status is not SecurityAssetProfileStatusDto.Approved and not SecurityAssetProfileStatusDto.Superseded)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_NOT_APPROVED",
                "Custom asset profile is not approved",
                $"Profile '{profile.Name}' version '{profile.Version}' is '{profile.Status}' and cannot back governed Security Master records.",
                ["assetSpecificTerms.customProfileId", "assetSpecificTerms.profileVersion"],
                "Route the profile version through approval before using it for Security Master create or amend workflows."));
        }

        // Status alone is not enough at WRITE time: an Approved version may not be effective yet,
        // and a Superseded version carries the window in which it WAS the governing definition. A
        // write evaluated outside [EffectiveFrom, EffectiveTo] pins a version that never governed
        // the record's effective date — a current write to an expired version, or a backdated
        // write to one not yet in force. Read-path validation deliberately skips this: a record
        // legitimately keeps its pinned (now-superseded) version so history stays interpretable.
        if (_enforceWriteTimeGovernance)
        {
            var evaluatedDate = DateOnly.FromDateTime(context.EvaluatedAtUtc.UtcDateTime.Date);
            if (evaluatedDate < profile.EffectiveFrom
                || (profile.EffectiveTo is DateOnly effectiveTo && evaluatedDate > effectiveTo))
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_VERSION_NOT_EFFECTIVE",
                    "Custom asset profile version is outside its effective window",
                    $"Profile '{profile.Name}' version '{profile.Version}' is effective " +
                    $"{profile.EffectiveFrom:yyyy-MM-dd}–{profile.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open"}, " +
                    $"which does not cover the evaluated date {evaluatedDate:yyyy-MM-dd}.",
                    ["assetSpecificTerms.customProfileId", "assetSpecificTerms.profileVersion"],
                    "Pin the profile version whose effective window covers the write's effective date."));
            }
        }

        if (!JsonValidationReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields)
            || profileFields.ValueKind != JsonValueKind.Object)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELDS_REQUIRED",
                "Custom asset profile fields are missing",
                "Profile-backed securities must store typed field values under assetSpecificTerms.profileFields.",
                ["assetSpecificTerms.profileFields"],
                "Populate profileFields using the selected approved profile definition."));
            return issues;
        }

        foreach (var field in profile.Fields.OrderBy(static field => field.Key, StringComparer.OrdinalIgnoreCase))
        {
            issues.AddRange(ValidateField(field, profileFields));
        }

        foreach (var rule in profile.DateOrderRules)
        {
            issues.AddRange(ValidateDateOrderRule(rule, profileFields));
        }

        issues.AddRange(ValidateIdentifierCoverage(profile, context.Record, context.EvaluatedAtUtc));
        issues.AddRange(ValidateProfileApprovalMetadata(assetSpecificTerms, profile, _enforceWriteTimeGovernance));

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateProfileDefinition(SecurityAssetProfileDefinitionDto profile)
    {
        var issues = new List<SecurityValidationIssueDto>();
        var duplicateKey = profile.Fields
            .GroupBy(static field => field.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateKey is not null)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_KEY_DUPLICATE",
                "Custom asset profile has duplicate field keys",
                $"Profile '{profile.Name}' repeats field key '{duplicateKey.Key}'.",
                ["assetProfile.fields"],
                "Publish a corrected profile version with unique field keys."));
        }

        var reservedKey = profile.Fields.FirstOrDefault(field => ReservedFieldKeys.Contains(field.Key));
        if (reservedKey is not null)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_KEY_RESERVED",
                "Custom asset profile uses a reserved field key",
                $"Profile '{profile.Name}' uses reserved field key '{reservedKey.Key}'.",
                ["assetProfile.fields"],
                "Rename the field in a new profile version before using the profile for Security Master records."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateField(
        SecurityAssetProfileFieldDefinitionDto field,
        JsonElement profileFields)
    {
        var fieldPath = $"assetSpecificTerms.profileFields.{field.Key}";
        if (!JsonValidationReader.TryGetProperty(profileFields, field.Key, out var value))
        {
            return field.IsRequired
                ?
                [
                    SecurityValidationIssueFactory.Create(
                        SecurityValidationSeverityDto.Error,
                        "SM_CUSTOM_PROFILE_FIELD_REQUIRED",
                        "Custom asset profile field is missing",
                        $"Required profile field '{field.Label}' is missing.",
                        [fieldPath],
                        "Populate the required profile field before the security is used for close, reconciliation, or report-pack evidence.")
                ]
                : [];
        }

        var issues = new List<SecurityValidationIssueDto>();
        var isValidType = field.FieldType switch
        {
            SecurityAssetProfileFieldTypeDto.Text => value.ValueKind == JsonValueKind.String && (!field.IsRequired || !string.IsNullOrWhiteSpace(value.GetString())),
            SecurityAssetProfileFieldTypeDto.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetProfileFieldTypeDto.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SecurityAssetProfileFieldTypeDto.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetProfileFieldTypeDto.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            SecurityAssetProfileFieldTypeDto.Enum => JsonValidationReader.HasAllowedString(value, field.AllowedValues),
            SecurityAssetProfileFieldTypeDto.CurrencyCode => IsCurrencyCode(value),
            SecurityAssetProfileFieldTypeDto.SecurityLink => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid) && guid != Guid.Empty,
            _ => true
        };

        if (!isValidType)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_TYPE_INVALID",
                "Custom asset profile field type is invalid",
                $"Profile field '{field.Label}' must be a valid {field.FieldType} value.",
                [fieldPath],
                "Correct the typed profile field value before the security is used downstream."));
            return issues;
        }

        if (field.FieldType is SecurityAssetProfileFieldTypeDto.Decimal or SecurityAssetProfileFieldTypeDto.Integer
            && value.TryGetDecimal(out var numericValue)
            && ((field.MinValue.HasValue && numericValue < field.MinValue.Value)
                || (field.MaxValue.HasValue && numericValue > field.MaxValue.Value)))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_RANGE_INVALID",
                "Custom asset profile field is outside the allowed range",
                $"Profile field '{field.Label}' must be between {field.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"} and {field.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"}.",
                [fieldPath],
                "Correct the profile field value or approve a new profile version with the intended range."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateDateOrderRule(
        SecurityAssetProfileDateOrderRuleDto rule,
        JsonElement profileFields)
    {
        if (!JsonValidationReader.TryGetDate(profileFields, rule.StartFieldKey, out var start)
            || !JsonValidationReader.TryGetDate(profileFields, rule.EndFieldKey, out var end)
            || start <= end)
        {
            return [];
        }

        return
        [
            SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                rule.Code,
                "Custom asset profile date order is invalid",
                rule.Message,
                [$"assetSpecificTerms.profileFields.{rule.StartFieldKey}", $"assetSpecificTerms.profileFields.{rule.EndFieldKey}"],
                "Correct the date ordering or approve a new profile version with the intended lifecycle rule.")
        ];
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateIdentifierCoverage(
        SecurityAssetProfileDefinitionDto profile,
        SecurityProjectionRecord record,
        DateTimeOffset evaluatedAtUtc)
    {
        var activeKinds = record.Identifiers
            .Where(identifier => identifier.ValidFrom <= evaluatedAtUtc && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > evaluatedAtUtc))
            .Select(static identifier => identifier.Kind)
            .ToHashSet();

        var issues = new List<SecurityValidationIssueDto>();
        foreach (var preference in profile.IdentifierPreferences.Where(static preference => preference.IsRequiredForClose))
        {
            if (activeKinds.Contains(preference.Kind))
            {
                continue;
            }

            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_IDENTIFIER_COVERAGE_MISSING",
                "Custom asset identifier coverage is incomplete",
                $"Profile '{profile.Name}' requires active {preference.Kind} coverage. {preference.Reason}",
                [$"identifiers.{preference.Kind}"],
                "Attach the required identifier before close, reconciliation, or report-pack readiness consumes this security."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateProfileApprovalMetadata(
        JsonElement assetSpecificTerms,
        SecurityAssetProfileDefinitionDto profile,
        bool enforceWriteTimeGovernance)
    {
        if (!(JsonValidationReader.TryGetProperty(assetSpecificTerms, "profileApproval", out var approval)
            && approval.ValueKind == JsonValueKind.Object
            && JsonValidationReader.TryGetString(approval, "approvedBy", out var approvedBy)
            && JsonValidationReader.TryGetDateTimeOffset(approval, "approvedAtUtc", out var approvedAtUtc)
            && JsonValidationReader.TryGetString(approval, "approvalReference", out var approvalReference)))
        {
            return
            [
                SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_APPROVAL_METADATA_REQUIRED",
                    "Custom asset profile approval metadata is missing",
                    "Profile-backed securities must retain immutable profile approval metadata with the pinned profile version.",
                    ["assetSpecificTerms.profileApproval"],
                    "Persist profileApproval.approvedBy, approvedAtUtc, and approvalReference from the governed profile approval event.")
            ];
        }

        // At WRITE time the approval metadata is evidence, not free text: it persists as the
        // record's immutable audit trail, so values contradicting the governed catalog's approval
        // facts would corrupt that trail from the first write. Read-path validation skips the
        // comparison — a historical record's metadata reflects the approval event as it stood
        // when written, which a later catalog re-approval must not retroactively invalidate.
        // The approval reference is verifiable only when the catalog carries one (definitions
        // predating the ApprovalReference field have no fact to compare, and free-text acceptance
        // is exactly what this check exists to prevent — not a reason to fail legacy profiles).
        var referenceContradictsCatalog = profile.ApprovalReference is { } catalogReference
            && !string.IsNullOrWhiteSpace(catalogReference)
            && !string.Equals(approvalReference.Trim(), catalogReference.Trim(), StringComparison.OrdinalIgnoreCase);
        if (enforceWriteTimeGovernance
            && (!string.Equals(approvedBy.Trim(), profile.ApprovedBy.Trim(), StringComparison.OrdinalIgnoreCase)
                || approvedAtUtc != profile.ApprovedAtUtc
                || referenceContradictsCatalog))
        {
            return
            [
                SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_APPROVAL_METADATA_MISMATCH",
                    "Custom asset profile approval metadata contradicts the governed catalog",
                    $"profileApproval names approvedBy '{approvedBy}' at '{approvedAtUtc:O}' (reference '{approvalReference}'), " +
                    $"but profile '{profile.Name}' version '{profile.Version}' was approved by '{profile.ApprovedBy}' at " +
                    $"'{profile.ApprovedAtUtc:O}'{(profile.ApprovalReference is null ? string.Empty : $" (reference '{profile.ApprovalReference}')")}.",
                    ["assetSpecificTerms.profileApproval"],
                    "Copy the approval metadata from the governed profile approval event instead of supplying free text.")
            ];
        }

        return [];
    }

    private static bool IsCurrencyCode(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var currency = value.GetString();
        return currency is { Length: 3 }
               && currency.All(static character => character is >= 'A' and <= 'Z');
    }
}

internal interface ISecurityValidationRule
{
    SecurityValidationIssueDto? Validate(SecurityValidationContext context);
}

internal sealed record FieldRule(
    string FieldPath,
    SecurityFieldRuleKind RuleKind,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    IReadOnlyList<string>? AllowedValues = null) : ISecurityValidationRule
{
    /// <summary>An array that must contain an item when supplied; presence comes from the schema.</summary>
    public static FieldRule OptionalArrayNotEmpty(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalArrayNotEmpty, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule OptionalPositive(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalPositiveNumber, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule OptionalNonNegative(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalNonNegativeNumber, SecurityValidationSeverityDto.Error, code, title, message);

    /// <summary>A string that, when supplied, must be one of <paramref name="allowedValues"/>; absent or null passes.</summary>
    public static FieldRule OptionalAllowedString(string fieldPath, IReadOnlyList<string> allowedValues, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalAllowedString, SecurityValidationSeverityDto.Error, code, title, message, allowedValues);

    /// <summary>A number that, when supplied, must lie in [0, 1]; absent or null passes.</summary>
    public static FieldRule OptionalFraction(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalFraction, SecurityValidationSeverityDto.Error, code, title, message);

    /// <summary>A value that, when supplied, must parse as an ISO-8601 date; absent or null passes.</summary>
    public static FieldRule OptionalDate(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalDate, SecurityValidationSeverityDto.Error, code, title, message);

    /// <summary>A value that, when supplied, must be a non-blank JSON string; absent or null passes.</summary>
    public static FieldRule OptionalString(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalString, SecurityValidationSeverityDto.Error, code, title, message);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var field = $"assetSpecificTerms.{FieldPath}";
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var property))
        {
            return null;
        }

        // The canonical serializer writes null where a domain value is None, so for every optional
        // rule kind a JSON null reads as "not supplied"; only a present, non-null value is governed.
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var isValid = RuleKind switch
        {
            SecurityFieldRuleKind.OptionalArrayNotEmpty => property.ValueKind == JsonValueKind.Array && property.GetArrayLength() > 0,
            SecurityFieldRuleKind.OptionalPositiveNumber => JsonValidationReader.TryGetDecimal(property, out var value) && value > 0m,
            SecurityFieldRuleKind.OptionalNonNegativeNumber => JsonValidationReader.TryGetDecimal(property, out var value) && value >= 0m,
            SecurityFieldRuleKind.OptionalAllowedString => JsonValidationReader.HasAllowedString(property, AllowedValues ?? Array.Empty<string>()),
            SecurityFieldRuleKind.OptionalFraction => JsonValidationReader.TryGetDecimal(property, out var value) && value >= 0m && value <= 1m,
            SecurityFieldRuleKind.OptionalDate => JsonValidationReader.HasDate(property),
            SecurityFieldRuleKind.OptionalString => JsonValidationReader.HasNonBlankString(property),
            _ => true
        };

        return isValid ? null : Issue(field);
    }

    private SecurityValidationIssueDto Issue(string field)
        => SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            Message,
            [field],
            "Correct the asset-class term and resubmit the Security Master amendment.");

}

internal sealed record DateOrderRule(
    string StartFieldPath,
    string EndFieldPath,
    string Code,
    string Title,
    string Message,
    bool Strict = false) : ISecurityValidationRule
{
    public static DateOrderRule Optional(string startFieldPath, string endFieldPath, string code, string title, string message)
        => new(startFieldPath, endFieldPath, code, title, message);

    /// <summary>Like <see cref="Optional"/>, but the start must fall strictly before the end.</summary>
    public static DateOrderRule OptionalStrict(string startFieldPath, string endFieldPath, string code, string title, string message)
        => new(startFieldPath, endFieldPath, code, title, message, Strict: true);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var hasStart = JsonValidationReader.TryGetAssetTermDate(context.Record.AssetSpecificTerms, StartFieldPath, context.Record.AssetClass, out var start);
        var hasEnd = JsonValidationReader.TryGetAssetTermDate(context.Record.AssetSpecificTerms, EndFieldPath, context.Record.AssetClass, out var end);
        if (!hasStart || !hasEnd)
        {
            return null;
        }

        return (Strict ? start < end : start <= end) ? null : Issue();
    }

    private SecurityValidationIssueDto Issue()
        => SecurityValidationIssueFactory.Create(
            SecurityValidationSeverityDto.Error,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{StartFieldPath}", $"assetSpecificTerms.{EndFieldPath}"],
            "Correct the effective-date ordering and resubmit the Security Master amendment.");
}

/// <summary>
/// A number that is required only when a discriminant names one of <c>DiscriminantValues</c>
/// (case-insensitive, matching the F# command validator), e.g. the cap strike of a Cap or Collar.
/// Any other discriminant value, or none, leaves the field to its own optional rules.
/// </summary>
internal sealed record RequiredNumberWhenRule(
    string DiscriminantPath,
    IReadOnlyList<string> DiscriminantValues,
    string FieldPath,
    string Code,
    string Title,
    string Message) : ISecurityValidationRule
{
    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, DiscriminantPath, context.Record.AssetClass, out var discriminant)
            || !JsonValidationReader.HasAllowedString(discriminant, DiscriminantValues))
        {
            return null;
        }

        if (JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var value)
            && JsonValidationReader.TryGetDecimal(value, out _))
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            SecurityValidationSeverityDto.Error,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{DiscriminantPath}", $"assetSpecificTerms.{FieldPath}"],
            "Supply the field the selected type requires and resubmit the Security Master amendment.");
    }
}

/// <summary>
/// Cross-field numeric ordering: the start field must be at or below the end field. Mirrors
/// <see cref="DateOrderRule"/> for numbers; a missing or null side is "not supplied" and the
/// schema presence rules govern each value on its own.
/// </summary>
internal sealed record NumericOrderRule(
    string LowerFieldPath,
    string UpperFieldPath,
    string Code,
    string Title,
    string Message) : ISecurityValidationRule
{
    public static NumericOrderRule Optional(string lowerFieldPath, string upperFieldPath, string code, string title, string message)
        => new(lowerFieldPath, upperFieldPath, code, title, message);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var hasLower = JsonValidationReader.TryGetAssetTermDecimal(context.Record.AssetSpecificTerms, LowerFieldPath, context.Record.AssetClass, out var lower);
        var hasUpper = JsonValidationReader.TryGetAssetTermDecimal(context.Record.AssetSpecificTerms, UpperFieldPath, context.Record.AssetClass, out var upper);
        if (!hasLower || !hasUpper)
        {
            return null;
        }

        return lower <= upper ? null : Issue();
    }

    private SecurityValidationIssueDto Issue()
        => SecurityValidationIssueFactory.Create(
            SecurityValidationSeverityDto.Error,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{LowerFieldPath}", $"assetSpecificTerms.{UpperFieldPath}"],
            "Correct the numeric ordering and resubmit the Security Master amendment.");
}

internal sealed record DistinctStringRule(
    string LeftFieldPath,
    string RightFieldPath,
    string Code,
    string Title,
    string Message) : ISecurityValidationRule
{
    public static DistinctStringRule Required(string leftFieldPath, string rightFieldPath, string code, string title, string message)
        => new(leftFieldPath, rightFieldPath, code, title, message);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermString(context.Record.AssetSpecificTerms, LeftFieldPath, context.Record.AssetClass, out var left)
            || !JsonValidationReader.TryGetAssetTermString(context.Record.AssetSpecificTerms, RightFieldPath, context.Record.AssetClass, out var right))
        {
            return null;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                Code,
                Title,
                Message,
                [$"assetSpecificTerms.{LeftFieldPath}", $"assetSpecificTerms.{RightFieldPath}"],
                "Correct the currency pair definition and resubmit the Security Master amendment.")
            : null;
    }
}

/// <summary>
/// ADR-022 canonical-home rule: flags a string term whose value routes the instrument into a
/// non-canonical modeling home (e.g. a Bond subclass naming a securitized product whose canonical
/// home is StructuredCredit). Absent or unlisted values pass — this rule partitions homes, it does
/// not require the field.
/// </summary>
internal sealed record DisallowedStringValuesRule(
    string FieldPath,
    IReadOnlyList<string> DisallowedValues,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string MessageFormat,
    string SuggestedAction) : ISecurityValidationRule
{
    public static DisallowedStringValuesRule Create(
        string fieldPath,
        IReadOnlyList<string> disallowedValues,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string messageFormat,
        string suggestedAction)
        => new(fieldPath, disallowedValues, severity, code, title, messageFormat, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermString(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var value))
        {
            return null;
        }

        var disallowed = DisallowedValues.FirstOrDefault(
            candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        if (disallowed is null)
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            string.Format(CultureInfo.InvariantCulture, MessageFormat, value),
            [$"assetSpecificTerms.{FieldPath}"],
            SuggestedAction);
    }
}

/// <summary>
/// ADR-022 canonical-home rule: flags a boolean term whose <c>true</c> value indicates the record
/// belongs in a different canonical asset class (e.g. an InvestmentFund flagged stable-NAV, whose
/// canonical home is MoneyMarketFund). Absent or <c>false</c> values pass.
/// </summary>
/// <summary>
/// A free-text pointer field that, when supplied, must be backed by a non-empty typed array: the
/// pointer is evidence lineage, the array is what computation reads. Absent or null pointer passes.
/// </summary>
internal sealed record TypedScheduleBackingRule(
    string PointerFieldPath,
    string ScheduleFieldPath,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    string SuggestedAction) : ISecurityValidationRule
{
    public static TypedScheduleBackingRule Create(
        string pointerFieldPath,
        string scheduleFieldPath,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        string suggestedAction)
        => new(pointerFieldPath, scheduleFieldPath, severity, code, title, message, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, PointerFieldPath, context.Record.AssetClass, out var pointer)
            || !JsonValidationReader.HasNonBlankString(pointer))
        {
            return null;
        }

        if (JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, ScheduleFieldPath, context.Record.AssetClass, out var schedule)
            && schedule.ValueKind == JsonValueKind.Array
            && schedule.GetArrayLength() > 0)
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{PointerFieldPath}", $"assetSpecificTerms.{ScheduleFieldPath}"],
            SuggestedAction);
    }
}

internal sealed record DiscouragedTrueBooleanRule(
    string FieldPath,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    string SuggestedAction) : ISecurityValidationRule
{
    public static DiscouragedTrueBooleanRule Create(
        string fieldPath,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        string suggestedAction)
        => new(fieldPath, severity, code, title, message, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var property)
            || property.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{FieldPath}"],
            SuggestedAction);
    }
}

internal enum SecurityFieldRuleKind
{
    OptionalArrayNotEmpty,
    OptionalPositiveNumber,
    OptionalNonNegativeNumber,
    OptionalAllowedString,
    OptionalFraction,
    OptionalDate,
    OptionalString
}

internal static class JsonValidationReader
{
    public static bool TryGetProperty(JsonElement root, string path, out JsonElement property)
    {
        property = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        property = current;
        return true;
    }

    public static bool TryGetAssetTermProperty(JsonElement root, string path, string assetClass, out JsonElement property)
    {
        if (TryGetSchemaProperty(root, path, assetClass, out property))
        {
            return true;
        }

        if (SupportsProfileBackedTerms(assetClass)
            && TryGetProperty(root, "profileFields", out var profileFields)
            && TryGetSchemaProperty(profileFields, path, assetClass, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool TryGetSchemaProperty(JsonElement root, string path, string assetClass, out JsonElement property)
    {
        property = default;
        var current = root;
        var parentPath = "";
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var field = parentPath.Length == 0
                ? SecurityAssetTermsSchema.Field(assetClass, segment)
                : SecurityAssetTermsSchema.ElementFields(assetClass, parentPath)
                    .FirstOrDefault(candidate => candidate.Key.Equals(segment, StringComparison.OrdinalIgnoreCase));
            var found = current.TryGetProperty(segment, out var value);
            if (!found && field is not null)
            {
                foreach (var alias in field.Aliases)
                {
                    if (current.TryGetProperty(alias, out value))
                    {
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
            {
                return false;
            }
            current = value;
            parentPath = parentPath.Length == 0 ? segment : $"{parentPath}.{segment}";
        }
        property = current;
        return true;
    }

    private static bool SupportsProfileBackedTerms(string assetClass)
        => Meridian.Contracts.SecurityMaster.SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms;

    public static bool HasNonBlankString(JsonElement property)
        => property.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(property.GetString());

    public static bool HasAllowedString(JsonElement property, IReadOnlyList<string> allowedValues)
        => HasNonBlankString(property)
           && allowedValues.Any(value => string.Equals(value, property.GetString(), StringComparison.OrdinalIgnoreCase));

    public static bool HasGuid(JsonElement property)
        => property.ValueKind == JsonValueKind.String
           && Guid.TryParse(property.GetString(), out var guid)
           && guid != Guid.Empty;

    public static bool HasDate(JsonElement property)
        => property.ValueKind == JsonValueKind.String
           && DateOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool TryGetString(JsonElement root, string path, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(root, path, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    public static bool TryGetAssetTermString(JsonElement root, string path, string assetClass, out string value)
    {
        value = string.Empty;
        if (!TryGetAssetTermProperty(root, path, assetClass, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    public static bool TryGetDecimal(JsonElement property, out decimal value)
    {
        value = default;
        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value);
    }

    public static bool TryGetInt(JsonElement root, string path, out int value)
    {
        value = default;
        if (!TryGetProperty(root, path, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    public static bool TryGetDate(JsonElement root, string path, out DateOnly value)
    {
        value = default;
        if (!TryGetProperty(root, path, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return DateOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    public static bool TryGetAssetTermDecimal(JsonElement root, string path, string assetClass, out decimal value)
    {
        if (TryGetAssetTermProperty(root, path, assetClass, out var property) && TryGetDecimal(property, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGetAssetTermDate(JsonElement root, string path, string assetClass, out DateOnly value)
    {
        value = default;
        if (!TryGetAssetTermProperty(root, path, assetClass, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return DateOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    public static bool TryGetDateTimeOffset(JsonElement root, string path, out DateTimeOffset value)
    {
        value = default;
        if (!TryGetProperty(root, path, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }
}
