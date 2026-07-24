using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

public sealed record SecurityValidationContext(
    SecurityProjectionRecord Record,
    DateTimeOffset EvaluatedAtUtc);

public interface ISecurityAssetClassValidator
{
    string AssetClass { get; }
    IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context);
}

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

    public static AssetClassValidatorRegistry CreateDefault(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new(DefaultAssetClassValidators.Create(assetProfileCatalog));
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
                    FieldRule.RequiredGuid("underlyingId", "SM_OPTION_UNDERLYING_REQUIRED", "Option underlying is missing", "Options must reference an underlying SecurityId."),
                    FieldRule.RequiredAllowedString("putCall", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.OptionPutCall), "SM_OPTION_PUT_CALL_INVALID", "Option put/call flag is invalid", "Option putCall must be Put or Call."),
                    FieldRule.RequiredPositive("strike", "SM_OPTION_STRIKE_INVALID", "Option strike is invalid", "Option strike must be greater than zero."),
                    FieldRule.RequiredDate("expiry", "SM_OPTION_EXPIRY_REQUIRED", "Option expiry is missing", "Options must include an expiry date."),
                    FieldRule.RequiredPositive("multiplier", "SM_OPTION_MULTIPLIER_INVALID", "Option multiplier is invalid", "Option multiplier must be greater than zero.")
                ]),
            new JsonAssetClassValidator(
                "Future",
                [
                    FieldRule.RequiredString("rootSymbol", "SM_FUTURE_ROOT_SYMBOL_REQUIRED", "Future root symbol is missing", "Futures must include a root symbol."),
                    FieldRule.RequiredString("contractMonth", "SM_FUTURE_CONTRACT_MONTH_REQUIRED", "Future contract month is missing", "Futures must include a contract month."),
                    FieldRule.RequiredDate("expiry", "SM_FUTURE_EXPIRY_REQUIRED", "Future expiry is missing", "Futures must include an expiry date."),
                    FieldRule.RequiredPositive("multiplier", "SM_FUTURE_MULTIPLIER_INVALID", "Future multiplier is invalid", "Future multiplier must be greater than zero."),
                    DateOrderRule.Optional("lastTradingDt", "expiry", "SM_FUTURE_LAST_TRADING_DATE_INVALID", "Future last-trading date is invalid", "Future last-trading date must be on or before expiry."),
                    DateOrderRule.Optional("firstNoticeDt", "expiry", "SM_FUTURE_FIRST_NOTICE_DATE_INVALID", "Future first-notice date is invalid", "Future first-notice date must be on or before expiry.")
                ]),
            new JsonAssetClassValidator(
                "Bond",
                [
                    FieldRule.RequiredDate("maturity", "SM_BOND_MATURITY_REQUIRED", "Bond maturity is missing", "Bonds must include a maturity date."),
                    FieldRule.OptionalNonNegative("couponRate", "SM_BOND_COUPON_RATE_INVALID", "Bond coupon rate is invalid", "Fixed coupon rates must be zero or greater."),
                    FieldRule.OptionalNonNegative("spreadBps", "SM_BOND_FLOATING_SPREAD_INVALID", "Bond floating spread is invalid", "Floating-rate spread must be zero or greater when supplied."),
                    DateOrderRule.Optional("issueDate", "maturity", "SM_BOND_ISSUE_DATE_INVALID", "Bond issue date is invalid", "Bond issue date must be on or before maturity."),
                    DateOrderRule.Optional("callDate", "maturity", "SM_BOND_CALL_DATE_INVALID", "Bond call date is invalid", "Bond call date must be on or before maturity.")
                ]),
            new JsonAssetClassValidator(
                "FxSpot",
                [
                    FieldRule.RequiredString("baseCurrency", "SM_FX_BASE_CURRENCY_REQUIRED", "FX base currency is missing", "FX spot pairs must include a base currency."),
                    FieldRule.RequiredString("quoteCurrency", "SM_FX_QUOTE_CURRENCY_REQUIRED", "FX quote currency is missing", "FX spot pairs must include a quote currency."),
                    DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_FX_PAIR_INVALID", "FX currency pair is invalid", "FX base and quote currencies must differ.")
                ]),
            new JsonAssetClassValidator(
                "Deposit",
                [
                    FieldRule.RequiredString("depositType", "SM_DEPOSIT_TYPE_REQUIRED", "Deposit type is missing", "Deposits must include a deposit type."),
                    FieldRule.RequiredString("institutionName", "SM_DEPOSIT_INSTITUTION_REQUIRED", "Deposit institution is missing", "Deposits must include an institution name."),
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
                    FieldRule.RequiredString("issuerName", "SM_CD_ISSUER_REQUIRED", "Certificate of deposit issuer is missing", "Certificates of deposit must include an issuer name."),
                    FieldRule.RequiredDate("maturity", "SM_CD_MATURITY_REQUIRED", "Certificate of deposit maturity is missing", "Certificates of deposit must include a maturity date."),
                    FieldRule.OptionalNonNegative("couponRate", "SM_CD_COUPON_RATE_INVALID", "Certificate of deposit coupon is invalid", "Certificate of deposit coupon rates must be zero or greater."),
                    DateOrderRule.Optional("callableDate", "maturity", "SM_CD_CALLABLE_DATE_INVALID", "Certificate of deposit callable date is invalid", "Callable date must be on or before maturity.")
                ]),
            new JsonAssetClassValidator(
                "CommercialPaper",
                [
                    FieldRule.RequiredString("issuerName", "SM_CP_ISSUER_REQUIRED", "Commercial paper issuer is missing", "Commercial paper must include an issuer name."),
                    FieldRule.RequiredDate("maturity", "SM_CP_MATURITY_REQUIRED", "Commercial paper maturity is missing", "Commercial paper must include a maturity date."),
                    FieldRule.OptionalNonNegative("discountRate", "SM_CP_DISCOUNT_RATE_INVALID", "Commercial paper discount rate is invalid", "Commercial paper discount rates must be zero or greater.")
                ]),
            new JsonAssetClassValidator(
                "TreasuryBill",
                [
                    FieldRule.RequiredDate("maturity", "SM_TBILL_MATURITY_REQUIRED", "Treasury bill maturity is missing", "Treasury bills must include a maturity date."),
                    DateOrderRule.Optional("auctionDate", "maturity", "SM_TBILL_AUCTION_DATE_INVALID", "Treasury bill auction date is invalid", "Auction date must be on or before maturity."),
                    FieldRule.OptionalNonNegative("discountRate", "SM_TBILL_DISCOUNT_RATE_INVALID", "Treasury bill discount rate is invalid", "Treasury bill discount rates must be zero or greater.")
                ]),
            new JsonAssetClassValidator(
                "Repo",
                [
                    FieldRule.RequiredString("counterparty", "SM_REPO_COUNTERPARTY_REQUIRED", "Repo counterparty is missing", "Repos must include a counterparty."),
                    FieldRule.RequiredDate("startDate", "SM_REPO_START_DATE_REQUIRED", "Repo start date is missing", "Repos must include a start date."),
                    FieldRule.RequiredDate("endDate", "SM_REPO_END_DATE_REQUIRED", "Repo end date is missing", "Repos must include an end date."),
                    DateOrderRule.Required("startDate", "endDate", "SM_REPO_DATE_RANGE_INVALID", "Repo date range is invalid", "Repo start date must be on or before end date."),
                    FieldRule.OptionalNonNegative("repoRate", "SM_REPO_RATE_INVALID", "Repo rate is invalid", "Repo rates must be zero or greater."),
                    FieldRule.OptionalNonNegative("haircut", "SM_REPO_HAIRCUT_INVALID", "Repo haircut is invalid", "Repo haircuts must be zero or greater.")
                ]),
            new JsonAssetClassValidator(
                "CashSweep",
                [
                    FieldRule.RequiredString("programName", "SM_CASH_SWEEP_PROGRAM_REQUIRED", "Cash sweep program is missing", "Cash sweeps must include a program name."),
                    FieldRule.RequiredString("sweepVehicleType", "SM_CASH_SWEEP_VEHICLE_REQUIRED", "Cash sweep vehicle type is missing", "Cash sweeps must include a sweep vehicle type."),
                    FieldRule.OptionalNonNegative("yieldRate", "SM_CASH_SWEEP_YIELD_RATE_INVALID", "Cash sweep yield is invalid", "Cash sweep yield rates must be zero or greater.")
                ]),
            new CompositeAssetClassValidator(
                "OtherSecurity",
                [
                    new JsonAssetClassValidator(
                        "OtherSecurity",
                        [
                            FieldRule.RequiredString("category", "SM_OTHER_SECURITY_CATEGORY_REQUIRED", "Other security category is missing", "OtherSecurity records must include a category.")
                        ]),
                    optionalProfileValidator
                ]),
            new JsonAssetClassValidator(
                "Swap",
                [
                    FieldRule.RequiredDate("effectiveDate", "SM_SWAP_EFFECTIVE_DATE_REQUIRED", "Swap effective date is missing", "Swaps must include an effective date."),
                    FieldRule.RequiredDate("maturityDate", "SM_SWAP_MATURITY_DATE_REQUIRED", "Swap maturity date is missing", "Swaps must include a maturity date."),
                    DateOrderRule.Required("effectiveDate", "maturityDate", "SM_SWAP_DATE_RANGE_INVALID", "Swap date range is invalid", "Swap effective date must be on or before maturity date."),
                    FieldRule.RequiredArray("legs", "SM_SWAP_LEGS_REQUIRED", "Swap legs are missing", "Swaps must include at least one leg.")
                ]),
            new JsonAssetClassValidator(
                "DirectLoan",
                [
                    FieldRule.RequiredString("borrower", "SM_DIRECT_LOAN_BORROWER_REQUIRED", "Direct loan borrower is missing", "Direct loans must include a borrower.")
                ]),
            new JsonAssetClassValidator(
                "StructuredCredit",
                [
                    FieldRule.RequiredString("tranche", "SM_STRUCTURED_CREDIT_TRANCHE_REQUIRED", "Structured credit tranche is missing", "Structured credit records must include a tranche."),
                    FieldRule.RequiredString("collateralType", "SM_STRUCTURED_CREDIT_COLLATERAL_REQUIRED", "Structured credit collateral type is missing", "Structured credit records must include a pool or collateral type."),
                    FieldRule.RequiredPositive("originalFace", "SM_STRUCTURED_CREDIT_ORIGINAL_FACE_INVALID", "Structured credit original face is invalid", "Structured credit original face must be greater than zero."),
                    FieldRule.OptionalNonNegative("currentFactor", "SM_STRUCTURED_CREDIT_CURRENT_FACTOR_INVALID", "Structured credit current factor is invalid", "Structured credit current factor must be zero or greater when supplied."),
                    FieldRule.RequiredString("couponOrIndex", "SM_STRUCTURED_CREDIT_COUPON_INDEX_REQUIRED", "Structured credit coupon/index is missing", "Structured credit records must include a coupon or index reference.")
                ]),
            new JsonAssetClassValidator(
                "PrivateFundInterest",
                [
                    FieldRule.RequiredString("gpSponsor", "SM_PRIVATE_FUND_GP_REQUIRED", "Private fund GP/sponsor is missing", "Private fund interests must include a GP or sponsor."),
                    FieldRule.RequiredString("strategy", "SM_PRIVATE_FUND_STRATEGY_REQUIRED", "Private fund strategy is missing", "Private fund interests must include a strategy."),
                    FieldRule.RequiredPositive("vintage", "SM_PRIVATE_FUND_VINTAGE_INVALID", "Private fund vintage is invalid", "Private fund interests must include a positive vintage year."),
                    FieldRule.RequiredPositive("commitment", "SM_PRIVATE_FUND_COMMITMENT_INVALID", "Private fund commitment is invalid", "Private fund commitments must be greater than zero."),
                    FieldRule.OptionalNonNegative("fundedAmount", "SM_PRIVATE_FUND_FUNDED_INVALID", "Private fund funded amount is invalid", "Private fund funded amounts must be zero or greater when supplied."),
                    FieldRule.OptionalNonNegative("unfundedAmount", "SM_PRIVATE_FUND_UNFUNDED_INVALID", "Private fund unfunded amount is invalid", "Private fund unfunded amounts must be zero or greater when supplied."),
                    FieldRule.RequiredDate("navDate", "SM_PRIVATE_FUND_NAV_DATE_REQUIRED", "Private fund NAV date is missing", "Private fund interests must include the retained NAV date.")
                ]),
            new JsonAssetClassValidator(
                "PrivateCompanyEquity",
                [
                    FieldRule.RequiredString("issuer", "SM_PRIVATE_COMPANY_ISSUER_REQUIRED", "Private company issuer is missing", "Private company equity records must include the issuer."),
                    FieldRule.RequiredString("shareClass", "SM_PRIVATE_COMPANY_SHARE_CLASS_REQUIRED", "Private company share class is missing", "Private company equity records must include a share class."),
                    FieldRule.RequiredString("round", "SM_PRIVATE_COMPANY_ROUND_REQUIRED", "Private company financing round is missing", "Private company equity records must include the financing round or acquisition round."),
                    FieldRule.OptionalNonNegative("ownershipPercent", "SM_PRIVATE_COMPANY_OWNERSHIP_INVALID", "Private company ownership percent is invalid", "Private company ownership percentages must be zero or greater when supplied."),
                    FieldRule.RequiredPositive("costBasis", "SM_PRIVATE_COMPANY_COST_BASIS_INVALID", "Private company cost basis is invalid", "Private company equity records must include a positive cost basis."),
                    FieldRule.OptionalNonNegative("latestValuation", "SM_PRIVATE_COMPANY_VALUATION_INVALID", "Private company valuation is invalid", "Private company latest valuation must be zero or greater when supplied.")
                ]),
            new JsonAssetClassValidator(
                "RealEstateHolding",
                [
                    FieldRule.RequiredString("propertyType", "SM_REAL_ESTATE_PROPERTY_TYPE_REQUIRED", "Real estate property type is missing", "Real estate holdings must include a property type."),
                    FieldRule.RequiredString("addressOrMarket", "SM_REAL_ESTATE_MARKET_REQUIRED", "Real estate market/address is missing", "Real estate holdings must include a market or address."),
                    FieldRule.RequiredPositive("ownershipPercent", "SM_REAL_ESTATE_OWNERSHIP_INVALID", "Real estate ownership percent is invalid", "Real estate ownership percentages must be greater than zero."),
                    FieldRule.RequiredPositive("appraisalValue", "SM_REAL_ESTATE_APPRAISAL_INVALID", "Real estate appraisal value is invalid", "Real estate holdings must include a positive appraisal value."),
                    FieldRule.RequiredDate("valuationDate", "SM_REAL_ESTATE_VALUATION_DATE_REQUIRED", "Real estate valuation date is missing", "Real estate holdings must include an appraisal or valuation date.")
                ]),
            new JsonAssetClassValidator(
                "CommitmentGuarantee",
                [
                    FieldRule.RequiredString("counterparty", "SM_COMMITMENT_GUARANTEE_COUNTERPARTY_REQUIRED", "Commitment/guarantee counterparty is missing", "Commitment and guarantee records must include the counterparty."),
                    FieldRule.RequiredPositive("committedAmount", "SM_COMMITMENT_GUARANTEE_AMOUNT_INVALID", "Committed or guaranteed amount is invalid", "Commitment and guarantee amounts must be greater than zero."),
                    FieldRule.OptionalNonNegative("unfundedAmount", "SM_COMMITMENT_GUARANTEE_EXPOSURE_INVALID", "Unfunded/exposure amount is invalid", "Unfunded or exposure amounts must be zero or greater when supplied."),
                    FieldRule.RequiredDate("effectiveDate", "SM_COMMITMENT_GUARANTEE_EFFECTIVE_DATE_REQUIRED", "Commitment/guarantee effective date is missing", "Commitments and guarantees must include an effective date."),
                    DateOrderRule.Optional("effectiveDate", "expiryDate", "SM_COMMITMENT_GUARANTEE_DATE_RANGE_INVALID", "Commitment/guarantee date range is invalid", "Commitment or guarantee effective date must be on or before expiry date."),
                    FieldRule.OptionalNonNegative("feeRate", "SM_COMMITMENT_GUARANTEE_FEE_INVALID", "Commitment/guarantee fee rate is invalid", "Commitment or guarantee fee rates must be zero or greater when supplied.")
                ]),
            new JsonAssetClassValidator(
                "Commodity",
                [
                    FieldRule.RequiredString("commodityType", "SM_COMMODITY_TYPE_REQUIRED", "Commodity type is missing", "Commodities must include a commodity type."),
                    FieldRule.OptionalPositive("contractSize", "SM_COMMODITY_CONTRACT_SIZE_INVALID", "Commodity contract size is invalid", "Commodity contract size must be greater than zero when supplied.")
                ]),
            new JsonAssetClassValidator(
                "CryptoCurrency",
                [
                    FieldRule.RequiredString("baseCurrency", "SM_CRYPTO_BASE_CURRENCY_REQUIRED", "Crypto base currency is missing", "Crypto pairs must include a base currency."),
                    FieldRule.RequiredString("quoteCurrency", "SM_CRYPTO_QUOTE_CURRENCY_REQUIRED", "Crypto quote currency is missing", "Crypto pairs must include a quote currency."),
                    DistinctStringRule.Required("baseCurrency", "quoteCurrency", "SM_CRYPTO_PAIR_INVALID", "Crypto pair is invalid", "Crypto base and quote currencies must differ.")
                ]),
            new JsonAssetClassValidator(
                "Cfd",
                [
                    FieldRule.RequiredString("underlyingAssetClass", "SM_CFD_UNDERLYING_ASSET_CLASS_REQUIRED", "CFD underlying asset class is missing", "CFDs must include an underlying asset class."),
                    FieldRule.OptionalPositive("leverage", "SM_CFD_LEVERAGE_INVALID", "CFD leverage is invalid", "CFD leverage must be greater than zero when supplied.")
                ]),
            new JsonAssetClassValidator(
                "Warrant",
                [
                    FieldRule.RequiredGuid("underlyingId", "SM_WARRANT_UNDERLYING_REQUIRED", "Warrant underlying is missing", "Warrants must reference an underlying SecurityId."),
                    FieldRule.RequiredAllowedString("warrantType", SecurityReferenceTaxonomyCatalog.Default.GetValues(SecurityReferenceTaxonomyKeys.WarrantType), "SM_WARRANT_TYPE_INVALID", "Warrant type is invalid", "Warrant type must be Put or Call."),
                    FieldRule.OptionalPositive("strike", "SM_WARRANT_STRIKE_INVALID", "Warrant strike is invalid", "Warrant strike must be greater than zero when supplied."),
                    FieldRule.OptionalPositive("multiplier", "SM_WARRANT_MULTIPLIER_INVALID", "Warrant multiplier is invalid", "Warrant multiplier must be greater than zero when supplied.")
                ]),
            requiredProfileValidator
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
        "securityId",
        "status",
        "version",
        "provenance"
    };

    private readonly ISecurityAssetProfileCatalog _assetProfileCatalog;
    private readonly bool _requireProfileReference;

    public SecurityAssetProfileAssetClassValidator(
        string assetClass,
        ISecurityAssetProfileCatalog assetProfileCatalog,
        bool requireProfileReference)
    {
        AssetClass = assetClass;
        _assetProfileCatalog = assetProfileCatalog;
        _requireProfileReference = requireProfileReference;
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
        issues.AddRange(ValidateProfileApprovalMetadata(assetSpecificTerms));

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

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateProfileApprovalMetadata(JsonElement assetSpecificTerms)
    {
        if (JsonValidationReader.TryGetProperty(assetSpecificTerms, "profileApproval", out var approval)
            && approval.ValueKind == JsonValueKind.Object
            && JsonValidationReader.TryGetString(approval, "approvedBy", out _)
            && JsonValidationReader.TryGetDateTimeOffset(approval, "approvedAtUtc", out _)
            && JsonValidationReader.TryGetString(approval, "approvalReference", out _))
        {
            return [];
        }

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
    public static FieldRule RequiredString(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.RequiredString, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule RequiredAllowedString(string fieldPath, IReadOnlyList<string> allowedValues, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.RequiredAllowedString, SecurityValidationSeverityDto.Error, code, title, message, allowedValues);

    public static FieldRule RequiredGuid(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.RequiredGuid, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule RequiredDate(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.RequiredDate, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule RequiredPositive(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.RequiredPositiveNumber, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule RequiredArray(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.RequiredArrayNotEmpty, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule OptionalPositive(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalPositiveNumber, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule OptionalNonNegative(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalNonNegativeNumber, SecurityValidationSeverityDto.Error, code, title, message);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var field = $"assetSpecificTerms.{FieldPath}";
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var property))
        {
            return RequiresPresence(RuleKind)
                ? Issue(field)
                : null;
        }

        var isValid = RuleKind switch
        {
            SecurityFieldRuleKind.RequiredString => JsonValidationReader.HasNonBlankString(property),
            SecurityFieldRuleKind.RequiredAllowedString => JsonValidationReader.HasAllowedString(property, AllowedValues ?? Array.Empty<string>()),
            SecurityFieldRuleKind.RequiredGuid => JsonValidationReader.HasGuid(property),
            SecurityFieldRuleKind.RequiredDate => JsonValidationReader.HasDate(property),
            SecurityFieldRuleKind.RequiredPositiveNumber => JsonValidationReader.TryGetDecimal(property, out var value) && value > 0m,
            SecurityFieldRuleKind.RequiredArrayNotEmpty => property.ValueKind == JsonValueKind.Array && property.GetArrayLength() > 0,
            SecurityFieldRuleKind.OptionalPositiveNumber => JsonValidationReader.TryGetDecimal(property, out var value) && value > 0m,
            SecurityFieldRuleKind.OptionalNonNegativeNumber => JsonValidationReader.TryGetDecimal(property, out var value) && value >= 0m,
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

    private static bool RequiresPresence(SecurityFieldRuleKind kind)
        => kind is SecurityFieldRuleKind.RequiredString
            or SecurityFieldRuleKind.RequiredAllowedString
            or SecurityFieldRuleKind.RequiredGuid
            or SecurityFieldRuleKind.RequiredDate
            or SecurityFieldRuleKind.RequiredPositiveNumber
            or SecurityFieldRuleKind.RequiredArrayNotEmpty;
}

internal sealed record DateOrderRule(
    string StartFieldPath,
    string EndFieldPath,
    bool RequiresBothFields,
    string Code,
    string Title,
    string Message) : ISecurityValidationRule
{
    public static DateOrderRule Required(string startFieldPath, string endFieldPath, string code, string title, string message)
        => new(startFieldPath, endFieldPath, true, code, title, message);

    public static DateOrderRule Optional(string startFieldPath, string endFieldPath, string code, string title, string message)
        => new(startFieldPath, endFieldPath, false, code, title, message);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var hasStart = JsonValidationReader.TryGetAssetTermDate(context.Record.AssetSpecificTerms, StartFieldPath, context.Record.AssetClass, out var start);
        var hasEnd = JsonValidationReader.TryGetAssetTermDate(context.Record.AssetSpecificTerms, EndFieldPath, context.Record.AssetClass, out var end);
        if (!hasStart || !hasEnd)
        {
            return RequiresBothFields
                ? Issue()
                : null;
        }

        return start <= end ? null : Issue();
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

internal enum SecurityFieldRuleKind
{
    RequiredString,
    RequiredAllowedString,
    RequiredGuid,
    RequiredDate,
    RequiredPositiveNumber,
    RequiredArrayNotEmpty,
    OptionalPositiveNumber,
    OptionalNonNegativeNumber
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
        if (TryGetProperty(root, path, out property))
        {
            return true;
        }

        if (SupportsProfileBackedTerms(assetClass)
            && TryGetProperty(root, "profileFields", out var profileFields)
            && TryGetProperty(profileFields, path, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool SupportsProfileBackedTerms(string assetClass)
        => string.Equals(assetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assetClass, "StructuredCredit", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assetClass, "PrivateFundInterest", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assetClass, "PrivateCompanyEquity", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assetClass, "RealEstateHolding", StringComparison.OrdinalIgnoreCase)
           || string.Equals(assetClass, "CommitmentGuarantee", StringComparison.OrdinalIgnoreCase);

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
