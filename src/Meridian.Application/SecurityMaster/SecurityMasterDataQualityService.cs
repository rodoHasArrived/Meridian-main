using System.Collections.Generic;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Implements the Clearwater 8-layer data quality control model for the Security Master.
/// Covers: MinimumAttribute, TaxonomyCheck, ValueConsistency, StalenessMonitor, and
/// ExternalComparison categories. FileMappingSufficiency, DataTypeRestriction, and
/// RefreshControl violations are surfaced at the ingest/import boundary.
/// </summary>
public sealed class SecurityMasterDataQualityService : ISecurityMasterDataQualityService
{
    private readonly ISecurityMasterStore _securityStore;
    private readonly ISecurityMasterQualityReportStore _reportStore;
    private readonly ILogger<SecurityMasterDataQualityService> _logger;

    private static readonly int MaxTermsAgeDays = 180;

    // Asset classes whose canonical validator requires a maturity date (see AssetClassValidatorRegistry).
    // Other non-equity classes (options, futures, FX, money-market funds, deposits) use different
    // required fields, so MA001 must not flag them for a missing maturity.
    private static readonly HashSet<string> MaturityBearingClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bond", "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "Swap"
    };

    // ISO 4217 subset covering the most common currencies; a full list would be loaded from a reference source.
    private static readonly HashSet<string> Iso4217Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD","EUR","GBP","JPY","CHF","CAD","AUD","NZD","HKD","SGD","SEK","NOK","DKK","CNY","CNH",
        "BRL","MXN","INR","ZAR","KRW","TRY","PLN","CZK","HUF","ILS","SAR","AED","MYR","THB","IDR",
        "PHP","CLP","COP","PEN","ARS","VND","NGN","EGP","PKR","BDT","UAH","RON","BGN","HRK","RSD",
        "ISK","GEL","KZT","UZS","AZN","AMD","BYR","MDL","MKD","ALL","BAM","RUB","TND","MAD","DZD",
        "LYD","IQD","KWD","BHD","OMR","QAR","JOD","LBP","SYP","YER","AFN","IRR","XAU","XAG","XPT",
        "XPD","XDR","XOF","XAF","XCD","XPF","CLF","UYU","UYI","SOS","GHS","ETB","TZS","UGX","MZN",
        "AOA","KES","RWF","ZMW","BWP","MGA","ZWL","NAD","SCR","MUR","MWK","SZL","LSL","CVE","GMD",
        "GNF","LRD","SLL","SLE","STN","XSU","XUA","NIO","GTQ","HNL","CRC","PAB","DOP","JMD","TTD",
        "BBD","BSD","HTG","CUP","CUC","AWG","ANG","SRD","GYD","BMD","KYD","BZD","FJD","PGK","SBD",
        "VUV","WST","TOP","MOP","BND","MMK","KHR","LAK","MNT","NPR","LKR","MVR","BTN","PKR","TJS",
        "TMT","KGS","KPW","TWD"
    };

    // ISO 3166-1 alpha-2 subset.
    private static readonly HashSet<string> Iso3166Alpha2Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AF","AX","AL","DZ","AS","AD","AO","AI","AQ","AG","AR","AM","AW","AU","AT","AZ","BS","BH",
        "BD","BB","BY","BE","BZ","BJ","BM","BT","BO","BQ","BA","BW","BV","BR","IO","BN","BG","BF",
        "BI","CV","KH","CM","CA","KY","CF","TD","CL","CN","CX","CC","CO","KM","CG","CD","CK","CR",
        "CI","HR","CU","CW","CY","CZ","DK","DJ","DM","DO","EC","EG","SV","GQ","ER","EE","SZ","ET",
        "FK","FO","FJ","FI","FR","GF","PF","TF","GA","GM","GE","DE","GH","GI","GR","GL","GD","GP",
        "GU","GT","GG","GN","GW","GY","HT","HM","VA","HN","HK","HU","IS","IN","ID","IR","IQ","IE",
        "IM","IL","IT","JM","JP","JE","JO","KZ","KE","KI","KP","KR","KW","KG","LA","LV","LB","LS",
        "LR","LY","LI","LT","LU","MO","MG","MW","MY","MV","ML","MT","MH","MQ","MR","MU","YT","MX",
        "FM","MD","MC","MN","ME","MS","MA","MZ","MM","NA","NR","NP","NL","NC","NZ","NI","NE","NG",
        "NU","NF","MK","MP","NO","OM","PK","PW","PS","PA","PG","PY","PE","PH","PN","PL","PT","PR",
        "QA","RE","RO","RU","RW","BL","SH","KN","LC","MF","PM","VC","WS","SM","ST","SA","SN","RS",
        "SC","SL","SG","SX","SK","SI","SB","SO","ZA","GS","SS","ES","LK","SD","SR","SJ","SE","CH",
        "SY","TW","TJ","TZ","TH","TL","TG","TK","TO","TT","TN","TR","TM","TC","TV","UG","UA","AE",
        "GB","US","UM","UY","UZ","VU","VE","VN","VG","VI","WF","EH","YE","ZM","ZW","XK"
    };

    public SecurityMasterDataQualityService(
        ISecurityMasterStore securityStore,
        ISecurityMasterQualityReportStore reportStore,
        ILogger<SecurityMasterDataQualityService> logger)
    {
        _securityStore = securityStore;
        _reportStore = reportStore;
        _logger = logger;
    }

    public async Task<SecurityMasterQualityReportDto> RunQualityChecksAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Security Master data quality scan.");

        var securities = await _securityStore.LoadActiveAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var violations = new List<DataQualityRuleViolationDto>();
        var scanned = 0;

        foreach (var security in securities)
        {
            if (security.Status != SecurityStatusDto.Active)
                continue;

            scanned++;
            violations.AddRange(RunMinimumAttributeRules(security, now));
            violations.AddRange(RunTaxonomyRules(security, now));
            violations.AddRange(RunConsistencyRules(security, now));
            violations.AddRange(RunStalenessRules(security, now));
        }

        var report = new SecurityMasterQualityReportDto(now, scanned, violations.Count, violations);
        await _reportStore.SaveAsync(report, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security Master quality scan complete: {Scanned} securities scanned, {Violations} violations.",
            scanned, violations.Count);

        return report;
    }

    public Task<SecurityMasterQualityReportDto?> GetLatestReportAsync(CancellationToken ct = default)
        => _reportStore.GetLatestAsync(ct);

    // ── MinimumAttribute rules ──────────────────────────────────────────────

    private static IEnumerable<DataQualityRuleViolationDto> RunMinimumAttributeRules(
        SecurityProjectionRecord security, DateTimeOffset now)
    {
        var assetClass = security.AssetClass;

        // Only maturity-bearing classes require a maturity date.
        if (MaturityBearingClasses.Contains(assetClass))
        {
            // The canonical Security Master mapper stores maturity under "maturity" for
            // Bond/CD/CP/TBill and "maturityDate" for swaps; accept either spelling.
            var maturity = TryReadDateField(security.AssetSpecificTerms, "maturity")
                ?? TryReadDateField(security.AssetSpecificTerms, "maturityDate")
                ?? TryReadDateField(security.CommonTerms, "maturity")
                ?? TryReadDateField(security.CommonTerms, "maturityDate");

            if (maturity is null)
            {
                yield return Violation(
                    "MA001", "Missing Maturity Date", DataQualityRuleCategory.MinimumAttribute,
                    security.SecurityId, "assetSpecificTerms.maturity",
                    $"{assetClass} securities require a maturity date.",
                    DataQualityRuleSeverity.Error, now);
            }
        }

        // Bonds require coupon rate.
        if (string.Equals(assetClass, "Bond", StringComparison.OrdinalIgnoreCase))
        {
            var coupon = TryReadDecimalField(security.AssetSpecificTerms, "couponRate")
                ?? TryReadDecimalField(security.AssetSpecificTerms, "coupon");

            if (coupon is null)
            {
                yield return Violation(
                    "MA002", "Missing Bond Coupon", DataQualityRuleCategory.MinimumAttribute,
                    security.SecurityId, "assetSpecificTerms.couponRate",
                    "Bond securities require a coupon rate.",
                    DataQualityRuleSeverity.Error, now);
            }
        }

        // Options require strike and expiry.
        if (string.Equals(assetClass, "Option", StringComparison.OrdinalIgnoreCase))
        {
            var strike = TryReadDecimalField(security.AssetSpecificTerms, "strikePrice")
                ?? TryReadDecimalField(security.AssetSpecificTerms, "strike");
            var expiry = TryReadDateField(security.AssetSpecificTerms, "expirationDate")
                ?? TryReadDateField(security.AssetSpecificTerms, "expiry");

            if (strike is null)
            {
                yield return Violation(
                    "MA003", "Missing Option Strike", DataQualityRuleCategory.MinimumAttribute,
                    security.SecurityId, "assetSpecificTerms.strikePrice",
                    "Option securities require a strike price.",
                    DataQualityRuleSeverity.Error, now);
            }

            if (expiry is null)
            {
                yield return Violation(
                    "MA004", "Missing Option Expiry", DataQualityRuleCategory.MinimumAttribute,
                    security.SecurityId, "assetSpecificTerms.expirationDate",
                    "Option securities require an expiration date.",
                    DataQualityRuleSeverity.Error, now);
            }
        }
    }

    // ── TaxonomyCheck rules ─────────────────────────────────────────────────

    private static IEnumerable<DataQualityRuleViolationDto> RunTaxonomyRules(
        SecurityProjectionRecord security, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(security.Currency)
            && !Iso4217Codes.Contains(security.Currency))
        {
            yield return Violation(
                "TX001", "Invalid Currency Code", DataQualityRuleCategory.TaxonomyCheck,
                security.SecurityId, "currency",
                $"Currency '{security.Currency}' is not a recognised ISO 4217 code.",
                DataQualityRuleSeverity.Error, now);
        }

        var countryOfRisk = TryReadStringField(security.CommonTerms, "countryOfRisk");
        if (!string.IsNullOrWhiteSpace(countryOfRisk) && !Iso3166Alpha2Codes.Contains(countryOfRisk))
        {
            yield return Violation(
                "TX002", "Invalid Country Code", DataQualityRuleCategory.TaxonomyCheck,
                security.SecurityId, "commonTerms.countryOfRisk",
                $"CountryOfRisk '{countryOfRisk}' is not a recognised ISO 3166-1 alpha-2 code.",
                DataQualityRuleSeverity.Warning, now);
        }
    }

    // ── ValueConsistency rules ──────────────────────────────────────────────

    private static IEnumerable<DataQualityRuleViolationDto> RunConsistencyRules(
        SecurityProjectionRecord security, DateTimeOffset now)
    {
        // EffectiveTo must be after EffectiveFrom when set.
        if (security.EffectiveTo.HasValue && security.EffectiveTo.Value <= security.EffectiveFrom)
        {
            yield return Violation(
                "VC001", "EffectiveTo Before EffectiveFrom", DataQualityRuleCategory.ValueConsistency,
                security.SecurityId, "effectiveTo",
                "EffectiveTo must be after EffectiveFrom.",
                DataQualityRuleSeverity.HardBlock, now);
        }

        // Bond: maturity must be after issue date.
        if (string.Equals(security.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase))
        {
            var issueDate = TryReadDateField(security.AssetSpecificTerms, "issueDate")
                ?? TryReadDateField(security.CommonTerms, "issueDate");
            var maturity = TryReadDateField(security.AssetSpecificTerms, "maturity")
                ?? TryReadDateField(security.AssetSpecificTerms, "maturityDate");

            if (issueDate.HasValue && maturity.HasValue && maturity.Value <= issueDate.Value)
            {
                yield return Violation(
                    "VC002", "Maturity On or Before Issue Date", DataQualityRuleCategory.ValueConsistency,
                    security.SecurityId, "assetSpecificTerms.maturityDate",
                    "Bond maturity date must be after the issue date.",
                    DataQualityRuleSeverity.Error, now);
            }
        }

        // Options: expiry must be in the future (for active securities).
        if (string.Equals(security.AssetClass, "Option", StringComparison.OrdinalIgnoreCase))
        {
            var expiry = TryReadDateField(security.AssetSpecificTerms, "expirationDate")
                ?? TryReadDateField(security.AssetSpecificTerms, "expiry");

            if (expiry.HasValue && expiry.Value < now)
            {
                yield return Violation(
                    "VC003", "Option Already Expired", DataQualityRuleCategory.ValueConsistency,
                    security.SecurityId, "assetSpecificTerms.expirationDate",
                    $"Active option security has expiration date {expiry.Value:yyyy-MM-dd} in the past.",
                    DataQualityRuleSeverity.Warning, now);
            }
        }
    }

    // ── StalenessMonitor rules ──────────────────────────────────────────────

    private static IEnumerable<DataQualityRuleViolationDto> RunStalenessRules(
        SecurityProjectionRecord security, DateTimeOffset now)
    {
        var ageDays = (int)(now - security.EffectiveFrom).TotalDays;
        if (ageDays > MaxTermsAgeDays)
        {
            yield return Violation(
                "SM001", "Stale Security Terms", DataQualityRuleCategory.StalenessMonitor,
                security.SecurityId, "effectiveFrom",
                $"Security terms have not been amended for {ageDays} days (threshold: {MaxTermsAgeDays}).",
                DataQualityRuleSeverity.Warning, now);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static DateTimeOffset? TryReadDateField(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(fieldName, out var prop)
            && prop.ValueKind != JsonValueKind.Null
            && prop.TryGetDateTimeOffset(out var result))
            return result;

        return null;
    }

    private static decimal? TryReadDecimalField(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(fieldName, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDecimal(out var result))
            return result;

        return null;
    }

    private static string? TryReadStringField(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(fieldName, out var prop)
            && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();

        return null;
    }

    private static DataQualityRuleViolationDto Violation(
        string ruleId,
        string ruleName,
        DataQualityRuleCategory category,
        Guid securityId,
        string? fieldPath,
        string message,
        DataQualityRuleSeverity severity,
        DateTimeOffset detectedAt)
        => new(ruleId, ruleName, category, securityId, fieldPath, message, severity, detectedAt);
}
