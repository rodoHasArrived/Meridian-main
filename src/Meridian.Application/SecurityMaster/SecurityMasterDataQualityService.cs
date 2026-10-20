using System.Collections.Generic;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Implements the eight-layer data quality control model for the Security Master.
/// Covers: MinimumAttribute, TaxonomyCheck, ValueConsistency, StalenessMonitor, and
/// ExternalComparison categories. FileMappingSufficiency, DataTypeRestriction, and
/// RefreshControl violations are surfaced at the ingest/import boundary.
/// </summary>
public sealed class SecurityMasterDataQualityService : ISecurityMasterDataQualityService
{
    private readonly ISecurityMasterStore _securityStore;
    private readonly ISecurityMasterQualityReportStore _reportStore;
    private readonly ILogger<SecurityMasterDataQualityService> _logger;
    private readonly IReadOnlyList<ISecurityCoverageSymbolSource> _coverageSources;
    private readonly ISecurityResolver? _securityResolver;

    private static readonly int MaxTermsAgeDays = 180;

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
        ILogger<SecurityMasterDataQualityService> logger,
        IEnumerable<ISecurityCoverageSymbolSource>? coverageSources = null,
        ISecurityResolver? securityResolver = null)
    {
        _securityStore = securityStore;
        _reportStore = reportStore;
        _logger = logger;
        _coverageSources = coverageSources?.ToArray() ?? [];
        _securityResolver = securityResolver;
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

        violations.AddRange(await RunCoverageRulesAsync(now, ct).ConfigureAwait(false));

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
        // The schema is keyed by canonical class names, so a record carrying a catalog alias is
        // validated against the class the catalog resolves it to.
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        var assetClass = CanonicalAssetClass(security);

        // Only maturity-bearing classes require a maturity date; the shared catalog owns that fact.
        // Other non-equity classes (options, futures, FX, money-market funds, deposits) use different
        // required fields, so MA001 must not flag them for a missing maturity.
        var requiresMaturity = descriptor.RequiresMaturity;
        if (requiresMaturity)
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

        // A coupon rate is conditional on the coupon structure, which the schema cannot mark as
        // Required: a fixed coupon needs a rate, floating and zero-coupon structures do not (the
        // canonical mapper defaults couponType to "Fixed" when absent). The rule is scoped to the
        // classes whose schema declares a coupon structure, not to one class name.
        if (SecurityAssetTermsSchema.Field(assetClass, "couponType") is not null)
        {
            var couponType = TryReadStringField(security.AssetSpecificTerms, "couponType") ?? "Fixed";
            if (string.Equals(couponType, "Fixed", StringComparison.OrdinalIgnoreCase))
            {
                var coupon = TryReadDecimalField(security.AssetSpecificTerms, "couponRate")
                    ?? TryReadDecimalField(security.AssetSpecificTerms, "coupon");

                if (coupon is null)
                {
                    yield return Violation(
                        "MA002", "Missing Fixed Coupon Rate", DataQualityRuleCategory.MinimumAttribute,
                        security.SecurityId, "assetSpecificTerms.couponRate",
                        $"Fixed-coupon {assetClass} securities require a coupon rate.",
                        DataQualityRuleSeverity.Error, now);
                }
            }
        }

        // Every other minimum attribute is what the terms schema marks Required for the class: the
        // canonical serializer always emits those fields, so a governed row missing one was not
        // written through it. Reading the schema replaces the per-class strike/expiry rules, which
        // also probed spellings ("strikePrice", "expirationDate") the schema never declared, and
        // covers the classes those rules skipped (futures, warrants, swaptions, ...). Maturity is
        // left to MA001 so a class that requires it is not reported twice.
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (!field.Required || (requiresMaturity && IsMaturityField(field)))
            {
                continue;
            }

            if (HasDeclaredValue(security.AssetSpecificTerms, field, descriptor.SupportsProfileBackedTerms))
            {
                continue;
            }

            yield return Violation(
                "MA003", "Missing Required Term", DataQualityRuleCategory.MinimumAttribute,
                security.SecurityId, $"assetSpecificTerms.{field.Key}",
                $"{assetClass} securities require '{field.Key}'.",
                DataQualityRuleSeverity.Error, now);
        }
    }

    // ── TaxonomyCheck rules ─────────────────────────────────────────────────

    private static IEnumerable<DataQualityRuleViolationDto> RunTaxonomyRules(
        SecurityProjectionRecord security, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(security.Currency)
            && !CurrencyCodeCatalog.IsRecognized(security.Currency))
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

        // Maturity must be after the issue date, for every class whose schema dates issuance.
        var assetClass = CanonicalAssetClass(security);
        if (SecurityAssetTermsSchema.Field(assetClass, "issueDate") is not null)
        {
            var issueDate = TryReadDateField(security.AssetSpecificTerms, "issueDate")
                ?? TryReadDateField(security.CommonTerms, "issueDate");
            var maturity = TryReadDateField(security.AssetSpecificTerms, "maturity")
                ?? TryReadDateField(security.AssetSpecificTerms, "maturityDate");

            if (issueDate.HasValue && maturity.HasValue && maturity.Value <= issueDate.Value)
            {
                yield return Violation(
                    "VC002", "Maturity On or Before Issue Date", DataQualityRuleCategory.ValueConsistency,
                    security.SecurityId, "assetSpecificTerms.maturity",
                    $"{security.AssetClass} maturity date must be after the issue date.",
                    DataQualityRuleSeverity.Error, now);
            }
        }

        // Expiry must be in the future for an active security, for every class whose schema
        // declares an expiry (options, futures, warrants, swaptions, ...), read under the key and
        // aliases the schema declares rather than a spelling of this rule's own. The schema's Date
        // fields carry no intraday time (a date-only value reads as midnight UTC), so the comparison
        // is by calendar date: an instrument expiring today is not yet expired.
        if (SecurityAssetTermsSchema.Field(assetClass, "expiry") is { Type: SecurityAssetTermFieldType.Date } expiryField)
        {
            var expiry = TryReadDateField(security.AssetSpecificTerms, expiryField);
            if (expiry.HasValue
                && DateOnly.FromDateTime(expiry.Value.UtcDateTime) < DateOnly.FromDateTime(now.UtcDateTime))
            {
                yield return Violation(
                    "VC003", "Already Expired", DataQualityRuleCategory.ValueConsistency,
                    security.SecurityId, $"assetSpecificTerms.{expiryField.Key}",
                    $"Active {security.AssetClass} security has expiry {expiry.Value:yyyy-MM-dd} in the past.",
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

    // ── RefreshControl rules (coverage sweep) ───────────────────────────────

    /// <summary>
    /// Sweeps registered coverage sources for symbols that are active somewhere in the
    /// platform but have no resolvable security-master record (rule RC001). Violations use
    /// a deterministic pseudo security id derived from the symbol so downstream casework
    /// can key on (rule, security, field) across runs.
    /// </summary>
    private async Task<IReadOnlyList<DataQualityRuleViolationDto>> RunCoverageRulesAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        if (_coverageSources.Count == 0 || _securityResolver is null)
        {
            return [];
        }

        var sourcesBySymbol = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var source in _coverageSources)
        {
            foreach (var symbol in await source.GetActiveSymbolsAsync(ct).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                var normalized = symbol.Trim().ToUpperInvariant();
                if (!sourcesBySymbol.TryGetValue(normalized, out var sources))
                {
                    sourcesBySymbol[normalized] = sources = [];
                }

                if (!sources.Contains(source.SourceName, StringComparer.Ordinal))
                {
                    sources.Add(source.SourceName);
                }
            }
        }

        var violations = new List<DataQualityRuleViolationDto>();
        foreach (var (symbol, sources) in sourcesBySymbol.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var resolved = await _securityResolver.ResolveAsync(
                    new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, symbol, null, now),
                    ct)
                .ConfigureAwait(false);
            if (resolved is not null)
            {
                continue;
            }

            violations.Add(Violation(
                "RC001", "Unmastered Active Symbol", DataQualityRuleCategory.RefreshControl,
                DeterministicSymbolId(symbol), $"symbol.{symbol}",
                $"Symbol '{symbol}' is active in {string.Join(", ", sources)} but has no resolvable security-master record.",
                DataQualityRuleSeverity.Error, now));
        }

        return violations;
    }

    private static Guid DeterministicSymbolId(string normalizedSymbol)
    {
        var hash = Sha256Digest.ComputeBytesUtf8("security-master:coverage:" + normalizedSymbol);
        return new Guid(hash.AsSpan(0, 16));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The catalog's canonical name for the record's class when the record carries a registered
    /// alias; the raw spelling otherwise, so an unknown class keeps reading as itself.
    /// </summary>
    private static string CanonicalAssetClass(SecurityProjectionRecord security)
        => SecurityAssetClassCatalog.TryGet(security.AssetClass, out var descriptor)
            ? descriptor.AssetClass
            : security.AssetClass;

    private static bool IsMaturityField(SecurityAssetTermField field)
        => field.Type == SecurityAssetTermFieldType.Date
           && (IsMaturityKey(field.Key) || field.Aliases.Any(IsMaturityKey));

    private static bool IsMaturityKey(string key)
        => string.Equals(key, "maturity", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "maturityDate", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the terms carry a value of the declared kind under the field's key or one of its
    /// declared aliases. Null, the wrong JSON kind, or an unparseable date/GUID count as missing,
    /// matching how the canonical mapper would fail to read them.
    /// </summary>
    private static bool HasDeclaredValue(JsonElement terms, SecurityAssetTermField field, bool supportsProfileBackedTerms)
        => EnumerateTermSources(terms, supportsProfileBackedTerms && !ProfileEnvelopeKeys.Contains(field.Key))
            .Any(source => HasDeclaredValueIn(source, field));

    /// <summary>
    /// The profile governance envelope the canonical mapper and the write validator read at the
    /// document root; a copy nested under <c>profileFields</c> is not where they look, so it does
    /// not satisfy the requirement.
    /// </summary>
    private static readonly HashSet<string> ProfileEnvelopeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "customProfileId",
        "profileVersion",
        "profileFields",
        "profileApproval"
    };

    /// <summary>
    /// Where a class's schema terms live: a profile-backed record (PrivateFundInterest,
    /// RealEstateHolding, ...) persists its governed scalar terms beneath <c>profileFields</c>, so
    /// for a class the catalog marks as supporting profile-backed terms that object is probed before
    /// the envelope root, matching the other governed-term readers. Any other class is read from
    /// the root alone, which is the only shape its canonical mapper consumes.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateTermSources(JsonElement terms, bool supportsProfileBackedTerms)
    {
        if (terms.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (supportsProfileBackedTerms
            && terms.TryGetProperty("profileFields", out var profileFields)
            && profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return terms;
    }

    private static bool HasDeclaredValueIn(JsonElement terms, SecurityAssetTermField field)
    {
        if (!TryGetDeclaredProperty(terms, field, out var value))
        {
            return false;
        }

        return field.Type switch
        {
            SecurityAssetTermFieldType.String => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
            // The same conversions the canonical mapper applies: a fractional or out-of-range number
            // is as unreadable to it as a missing one.
            SecurityAssetTermFieldType.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetTermFieldType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SecurityAssetTermFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetTermFieldType.Date => TryReadDate(value) is not null,
            SecurityAssetTermFieldType.Guid => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
            SecurityAssetTermFieldType.Array => value.ValueKind == JsonValueKind.Array,
            SecurityAssetTermFieldType.Object => value.ValueKind == JsonValueKind.Object,
            _ => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
        };
    }

    private static DateTimeOffset? TryReadDateField(JsonElement element, SecurityAssetTermField field)
        => TryGetDeclaredProperty(element, field, out var value) ? TryReadDate(value) : null;

    private static bool TryGetDeclaredProperty(JsonElement element, SecurityAssetTermField field, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, field.Key, StringComparison.OrdinalIgnoreCase)
                || field.Aliases.Any(alias => string.Equals(property.Name, alias, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            }
        }

        return false;
    }

    private static DateTimeOffset? TryReadDate(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (value.TryGetDateTimeOffset(out var timestamp))
        {
            return timestamp;
        }

        return DateOnly.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var date)
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
    }

    private static DateTimeOffset? TryReadDateField(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(fieldName, out var prop)
            && prop.ValueKind == JsonValueKind.String
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
