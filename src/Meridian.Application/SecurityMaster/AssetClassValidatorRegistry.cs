using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

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
        => new(DefaultAssetClassValidators.Create());
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
    public static IReadOnlyList<ISecurityAssetClassValidator> Create()
        =>
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
                    FieldRule.RequiredAllowedString("putCall", ["Put", "Call"], "SM_OPTION_PUT_CALL_INVALID", "Option put/call flag is invalid", "Option putCall must be Put or Call."),
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
            new JsonAssetClassValidator(
                "OtherSecurity",
                [
                    FieldRule.RequiredString("category", "SM_OTHER_SECURITY_CATEGORY_REQUIRED", "Other security category is missing", "OtherSecurity records must include a category.")
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
                    FieldRule.RequiredAllowedString("warrantType", ["Put", "Call"], "SM_WARRANT_TYPE_INVALID", "Warrant type is invalid", "Warrant type must be Put or Call."),
                    FieldRule.OptionalPositive("strike", "SM_WARRANT_STRIKE_INVALID", "Warrant strike is invalid", "Warrant strike must be greater than zero when supplied."),
                    FieldRule.OptionalPositive("multiplier", "SM_WARRANT_MULTIPLIER_INVALID", "Warrant multiplier is invalid", "Warrant multiplier must be greater than zero when supplied.")
                ])
        ];
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
        if (!JsonValidationReader.TryGetProperty(context.Record.AssetSpecificTerms, FieldPath, out var property))
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
        var hasStart = JsonValidationReader.TryGetDate(context.Record.AssetSpecificTerms, StartFieldPath, out var start);
        var hasEnd = JsonValidationReader.TryGetDate(context.Record.AssetSpecificTerms, EndFieldPath, out var end);
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
        if (!JsonValidationReader.TryGetString(context.Record.AssetSpecificTerms, LeftFieldPath, out var left)
            || !JsonValidationReader.TryGetString(context.Record.AssetSpecificTerms, RightFieldPath, out var right))
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

    public static bool TryGetDecimal(JsonElement property, out decimal value)
    {
        value = default;
        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value);
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
}
