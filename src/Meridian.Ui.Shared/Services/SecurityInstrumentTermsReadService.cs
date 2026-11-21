using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Projects retained Security Master terms for inspection without running a codec, upgrading a
/// payload, choosing between aliases or calculating economics. All such decisions stay with their
/// canonical owners. Even unsupported payloads remain available as exact JSON text.
/// </summary>
public sealed class SecurityInstrumentTermsReadService(
    ISecurityMasterQueryService query,
    ISecurityAssetProfileCatalog? profiles = null)
{
    public async Task<SecurityInstrumentTermsDto?> ReadAsync(Guid securityId, CancellationToken ct = default)
    {
        var detail = await query.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (detail is null) return null;
        if (detail.SecurityId != securityId)
            throw new InvalidOperationException("Canonical terms returned a different security identity.");

        // Economic metadata is a separate read. It is exposed only when its complete version and
        // economic identity match, never attached to a newer or older set of terms by display name.
        var economic = await query.GetEconomicDefinitionByIdAsync(securityId, ct).ConfigureAwait(false);
        return Project(detail, economic, profiles, DateTimeOffset.UtcNow);
    }

    public static SecurityInstrumentTermsDto Project(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        ISecurityAssetProfileCatalog? profiles,
        DateTimeOffset retrievedAtUtc)
    {
        var diagnostics = new List<string>();
        var supported = SecurityAssetClassCatalog.TryGet(detail.AssetClass, out var descriptor);
        var assetClass = descriptor?.AssetClass ?? detail.AssetClass;
        // Current-state reads require an explicit family. The frozen legacy classifier belongs to
        // event replay; this inspection must neither guess a family nor stamp the retained bytes.
        var declaredFormat = SecurityTermsEnvelope.GetDeclaredFormat(detail.AssetSpecificTerms);
        var format = declaredFormat ?? "Undeclared";
        var termsVersion = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(detail.AssetSpecificTerms);
        if (!supported) diagnostics.Add($"Asset class '{detail.AssetClass}' has no registered term schema; supplied values remain unvalidated.");
        if (declaredFormat is null)
        {
            supported = false;
            diagnostics.Add("The retained current-state payload does not declare a termsFormat. Its family was not inferred; raw payload inspection remains available.");
        }
        if (supported && SecurityTermsEnvelope.TryGetReadRefusal(assetClass, detail.AssetSpecificTerms, out var refusal))
        {
            supported = false;
            diagnostics.Add(refusal);
        }
        if (HasInvalidEnvelope(detail.AssetSpecificTerms))
        {
            supported = false;
            diagnostics.Add("The retained terms envelope has an invalid or ambiguous format/version declaration.");
        }
        var profileGoverned = SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, detail.AssetSpecificTerms);
        var codecVersions = Properties(detail.AssetSpecificTerms, SecurityTermsEnvelope.SchemaVersionKey);
        if (codecVersions.Count == 1 && codecVersions[0].Value.ValueKind == JsonValueKind.Number
            && codecVersions[0].Value.TryGetInt32(out var codecVersion)
            && !AssetSpecificTermsSchema.IsAccepted(codecVersion, profileGoverned))
        {
            supported = false;
            diagnostics.Add($"Retained schemaVersion {codecVersion} is not supported by the asset-specific-terms family; raw payload inspection remains available.");
        }

        string? profileId = null;
        int? profileVersion = null;
        var profile = profileGoverned
            ? ResolveProfile(detail.AssetSpecificTerms, profiles, diagnostics, out profileId, out profileVersion) : null;
        if ((format == SecurityTermsFormats.CustomProfile || profileGoverned) && profile is null)
        {
            supported = false;
            if (!profileGoverned)
                diagnostics.Add("The retained profile payload does not belong to a profile-supported asset class/envelope; no profile schema was substituted.");
        }

        var source = Source(detail, economic);
        if (source.Status != SecurityInstrumentTermsSourceStatus.Matched) diagnostics.Add(source.Summary);
        var context = new ProjectionContext(assetClass, termsVersion, detail.AssetSpecificTerms, profile, diagnostics);
        var fields = new List<SecurityInstrumentTermDto>();
        fields.AddRange(context.Block(detail.CommonTerms, "/commonTerms", "Common terms", []));
        IReadOnlyList<SecurityAssetTermField> declarations = supported && format == SecurityTermsFormats.Flat && !profileGoverned
            ? SecurityAssetTermsSchema.Fields(assetClass) : [];
        fields.AddRange(context.Block(detail.AssetSpecificTerms, "/assetSpecificTerms", "Asset-specific terms", declarations));

        // Dated schedule grammar comes from the shared parser. No normalized or rounded schedule
        // values are serialized back; the retained rows above remain the only displayed operands.
        if (supported)
        {
            foreach (var issue in SecurityEconomicScheduleParser.Parse(detail.AssetSpecificTerms, detail.CommonTerms).Diagnostics)
                diagnostics.Add($"{issue.Code} · {issue.Path}: {issue.Message}");
        }
        var status = !supported ? SecurityInstrumentTermsStatus.Unsupported
            : diagnostics.Count > 0 ? SecurityInstrumentTermsStatus.ReviewRequired : SecurityInstrumentTermsStatus.Ready;
        var common = Raw(detail.CommonTerms);
        var terms = Raw(detail.AssetSpecificTerms);
        var fingerprintInput = JsonSerializer.Serialize(new
        {
            detail.SecurityId, detail.AssetClass, detail.Currency, detail.Version,
            detail.EffectiveFrom, detail.EffectiveTo, CommonTerms = common, AssetSpecificTerms = terms
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))).ToLowerInvariant();
        return new(detail.SecurityId, detail.AssetClass, detail.Currency, detail.Version,
            detail.EffectiveFrom, detail.EffectiveTo, retrievedAtUtc, status, fingerprint, format, termsVersion,
            new(assetClass, descriptor?.TermsVersion ?? 0, descriptor?.ReadsTermsThrough ?? 0,
                descriptor?.LotUnit.ToString(), descriptor?.CashFlowBehavior.ToString(), profileId, profileVersion, profile?.Status.ToString()),
            source, common, terms, fields, diagnostics);
    }

    private static SecurityInstrumentTermsSourceDto Source(SecurityDetailDto detail, SecurityEconomicDefinitionRecord? economic)
    {
        if (economic is null)
            return new(SecurityInstrumentTermsSourceStatus.Unavailable, "Canonical terms are available; source provenance is unavailable for this version.", null);
        if (economic.SecurityId != detail.SecurityId || economic.Version != detail.Version
            || !string.Equals(economic.AssetClass, detail.AssetClass, StringComparison.Ordinal)
            || !string.Equals(economic.Currency, detail.Currency, StringComparison.Ordinal)
            || economic.EffectiveFrom != detail.EffectiveFrom || economic.EffectiveTo != detail.EffectiveTo)
            return new(SecurityInstrumentTermsSourceStatus.VersionMismatch, "Source metadata does not match the canonical terms version and has been withheld. Refresh before relying on provenance.", null);
        if (economic.Provenance.ValueKind is not JsonValueKind.Object || !economic.Provenance.EnumerateObject().Any())
            return new(SecurityInstrumentTermsSourceStatus.Unavailable, "No source provenance was retained for this canonical terms version.", null);
        return new(SecurityInstrumentTermsSourceStatus.Matched, "Retained economic provenance matches the canonical security identity, version and effective dates.", Raw(economic.Provenance));
    }

    private static SecurityAssetProfileDefinitionDto? ResolveProfile(JsonElement terms, ISecurityAssetProfileCatalog? profiles,
        List<string> diagnostics, out string? profileId, out int? profileVersion)
    {
        profileId = null;
        profileVersion = null;
        if (terms.ValueKind != JsonValueKind.Object) return null;
        var ids = Properties(terms, "customProfileId");
        var versions = Properties(terms, "profileVersion");
        if (ids.Count == 0 && versions.Count == 0) return null;
        if (ids.Count != 1 || versions.Count != 1 || ids[0].Value.ValueKind != JsonValueKind.String
            || versions[0].Value.ValueKind != JsonValueKind.Number || !versions[0].Value.TryGetInt32(out var version) || version < 1)
        {
            diagnostics.Add("The retained profile identity/version is missing, malformed or ambiguous; profile fields remain unvalidated.");
            return null;
        }
        profileId = ids[0].Value.GetString();
        profileVersion = version;
        if (string.IsNullOrWhiteSpace(profileId) || profiles is null || !profiles.TryGetProfile(profileId, version, out var profile))
        {
            diagnostics.Add("The exact retained profile schema version is unavailable; no latest-profile substitution was made.");
            return null;
        }
        if (profile.Status == SecurityAssetProfileStatusDto.Draft)
            diagnostics.Add("The retained profile schema is a draft; read visibility does not establish governed approval.");
        return profile;
    }

    private static bool HasInvalidEnvelope(JsonElement terms)
    {
        if (terms.ValueKind is not JsonValueKind.Object) return false;
        var formats = Properties(terms, SecurityTermsEnvelope.FormatKey);
        var versions = Properties(terms, SecurityTermsEnvelope.AssetClassTermsVersionKey);
        var codecVersions = Properties(terms, SecurityTermsEnvelope.SchemaVersionKey);
        return formats.Count > 1 || versions.Count > 1 || codecVersions.Count > 1
            || formats.Count == 1 && (formats[0].Name != SecurityTermsEnvelope.FormatKey || formats[0].Value.ValueKind != JsonValueKind.String)
            || versions.Count == 1 && (versions[0].Name != SecurityTermsEnvelope.AssetClassTermsVersionKey || versions[0].Value.ValueKind != JsonValueKind.Number || !versions[0].Value.TryGetInt32(out _))
            || codecVersions.Count == 1 && (codecVersions[0].Name != SecurityTermsEnvelope.SchemaVersionKey || codecVersions[0].Value.ValueKind != JsonValueKind.Number || !codecVersions[0].Value.TryGetInt32(out _));
    }

    private static string? Raw(JsonElement value) => value.ValueKind == JsonValueKind.Undefined ? null : value.GetRawText();
    private static List<JsonProperty> Properties(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToList() : [];
    private static string Pointer(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed class ProjectionContext(string assetClass, int termsVersion, JsonElement assetTerms, SecurityAssetProfileDefinitionDto? profile, List<string> diagnostics)
    {
        public IReadOnlyList<SecurityInstrumentTermDto> Block(JsonElement value, string path, string group, IReadOnlyList<SecurityAssetTermField> declarations)
            => value.ValueKind == JsonValueKind.Object
                ? Object(value, path, group, declarations, "", 0)
                : [Node(value, path, path[1..], group, null, false, false, "The retained term block is not an object.", "", 0)];

        private IReadOnlyList<SecurityInstrumentTermDto> Object(JsonElement value, string path, string group,
            IReadOnlyList<SecurityAssetTermField> declarations, string schemaPath, int depth)
        {
            var result = new List<SecurityInstrumentTermDto>();
            var properties = value.EnumerateObject().ToArray();
            foreach (var property in properties)
            {
                var declared = declarations.Where(field => Matches(field, property.Name)).ToArray();
                var field = declared.Length == 1 ? declared[0] : null;
                var matches = field is null ? properties.Count(candidate => string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase))
                    : properties.Count(candidate => Matches(field, candidate.Name));
                var reserved = path == "/assetSpecificTerms" && (SecurityTermsEnvelope.IsReservedKey(property.Name)
                    || property.Name is "customProfileId" or "profileVersion" or "profileFields" or "profileApproval");
                var fieldPath = path + "/" + Pointer(property.Name);
                var nestedSchema = string.IsNullOrEmpty(schemaPath) ? field?.Key ?? property.Name : schemaPath + "." + (field?.Key ?? property.Name);
                var message = matches > 1 || declared.Length > 1 ? "Multiple retained property spellings or aliases claim this field; no winner was selected." : null;
                var node = Node(property.Value, fieldPath, property.Name, reserved ? "Envelope" : group, field,
                    reserved || field is not null, matches > 1 || declared.Length > 1, message, nestedSchema, depth, value);
                result.Add(node);
                if (!node.IsDeclared && (path is "/assetSpecificTerms" or "/assetSpecificTerms/profileFields" || declarations.Count > 0))
                    diagnostics.Add($"{fieldPath}: unregistered field preserved without an economic interpretation.");
            }
            foreach (var field in declarations.Where(field => field.SinceTermsVersion <= termsVersion && !properties.Any(property => Matches(field, property.Name))))
                result.Add(Node(default, path + "/" + Pointer(field.Key), field.Key, group, field, true, false, null, schemaPath, depth));
            return result;
        }

        private SecurityInstrumentTermDto Node(JsonElement value, string path, string key, string group, SecurityAssetTermField? field,
            bool declared, bool ambiguous, string? message, string schemaPath, int depth, JsonElement siblings = default)
        {
            var state = ambiguous ? SecurityInstrumentTermState.Ambiguous
                : value.ValueKind == JsonValueKind.Undefined ? SecurityInstrumentTermState.Missing
                : value.ValueKind == JsonValueKind.Null ? SecurityInstrumentTermState.Null
                : SecurityInstrumentTermState.Present;
            if (state == SecurityInstrumentTermState.Present && field is not null)
            {
                message = Validate(value, field);
                if (field.SinceTermsVersion > termsVersion)
                    message = "This field was introduced after the payload's declared terms version.";
                if (message is not null) state = SecurityInstrumentTermState.Invalid;
            }
            var toleratedReadAbsence = state == SecurityInstrumentTermState.Missing && field?.AbsentReadDefault is not null;
            if (state == SecurityInstrumentTermState.Missing && field?.Required == true)
                message = toleratedReadAbsence
                    ? $"No retained source value. The canonical reader declares absence as '{field!.AbsentReadDefault}'; this inspection does not insert it."
                    : "Required by the retained asset-class schema; no source value was supplied.";
            if (state == SecurityInstrumentTermState.Null && field?.Required == true)
                message = "Required by the retained asset-class schema; an explicit null was supplied.";
            if (field is null && message is not null && !ambiguous && value.ValueKind != JsonValueKind.Undefined)
                state = SecurityInstrumentTermState.Invalid;
            if (message is not null && !toleratedReadAbsence) diagnostics.Add($"{path}: {message}");

            IReadOnlyList<SecurityInstrumentTermDto> children = [];
            if (depth < 32)
            {
                var nested = SecurityAssetTermsSchema.ElementFields(assetClass, schemaPath);
                if (path == "/assetSpecificTerms/profileFields" && profile is not null)
                    nested = profile.Fields.Select(ProfileField).ToArray();
                if (value.ValueKind == JsonValueKind.Object)
                    children = Object(value, path, key == "profileFields" ? "Profile fields" : group, nested, schemaPath, depth + 1);
                else if (value.ValueKind == JsonValueKind.Array)
                    children = value.EnumerateArray().Select((item, index) =>
                        Node(item, path + "/" + index, $"[{index}]", group, null, false, false, null, schemaPath, depth + 1)).ToArray();
            }
            else if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                diagnostics.Add($"{path}: nested inspection depth exceeded; full retained JSON remains available.");

            var unit = Unit(key, siblings, path, value, schemaPath);
            var hint = SecurityAssetClassRegistry.TryGet(assetClass, out var definition)
                ? definition.UiHints.FirstOrDefault(candidate => candidate.Key == field?.Key) : null;
            var label = path.StartsWith("/assetSpecificTerms/profileFields/", StringComparison.Ordinal)
                ? profile?.Fields.FirstOrDefault(candidate => candidate.Key == field?.Key)?.Label ?? key : hint?.Label ?? key;
            return new(path, key, label, hint?.Group ?? group, field?.Type.ToString(), value.ValueKind.ToString(), state,
                declared, field?.Required ?? false,
                path.StartsWith("/assetSpecificTerms/profileFields/", StringComparison.Ordinal) ? null : field?.Role.ToString(),
                value.ValueKind == JsonValueKind.String ? value.GetString()
                    : value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? value.GetRawText() : null,
                Raw(value), unit.Value, unit.Status, unit.Path, message, children);
        }

        private static bool Matches(SecurityAssetTermField field, string key)
            => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase) || field.Aliases.Contains(key, StringComparer.OrdinalIgnoreCase);

        private static SecurityAssetTermField ProfileField(SecurityAssetProfileFieldDefinitionDto field)
            => new(field.Key, field.FieldType switch
            {
                SecurityAssetProfileFieldTypeDto.Decimal => SecurityAssetTermFieldType.Decimal,
                SecurityAssetProfileFieldTypeDto.Integer => SecurityAssetTermFieldType.Integer,
                SecurityAssetProfileFieldTypeDto.Boolean => SecurityAssetTermFieldType.Boolean,
                SecurityAssetProfileFieldTypeDto.Date => SecurityAssetTermFieldType.Date,
                SecurityAssetProfileFieldTypeDto.SecurityLink => SecurityAssetTermFieldType.Guid,
                _ => SecurityAssetTermFieldType.String
            }, field.IsRequired, [], field.AllowedValues);

        private static string? Validate(JsonElement value, SecurityAssetTermField field)
        {
            var valid = field.Type switch
            {
                SecurityAssetTermFieldType.String => value.ValueKind == JsonValueKind.String,
                SecurityAssetTermFieldType.Decimal => value.ValueKind == JsonValueKind.Number && SecurityScheduleDecimal.TryRead(value, out _),
                SecurityAssetTermFieldType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
                SecurityAssetTermFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                SecurityAssetTermFieldType.Date => value.ValueKind == JsonValueKind.String &&
                    (DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                     || DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)),
                SecurityAssetTermFieldType.Guid => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
                SecurityAssetTermFieldType.Array => value.ValueKind == JsonValueKind.Array,
                SecurityAssetTermFieldType.Object => value.ValueKind == JsonValueKind.Object,
                _ => false
            };
            if (!valid) return $"Retained JSON does not match declared type {field.Type}; its original value is preserved.";
            if (value.ValueKind == JsonValueKind.String && (!field.Allows(value.GetString()) || !field.SatisfiesConvention(value.GetString())))
                return "Retained string does not match the declared vocabulary or convention; no substitute was selected.";
            return null;
        }

        private (string? Value, SecurityInstrumentTermUnitStatus Status, string? Path) Unit(string key, JsonElement siblings, string path, JsonElement value, string schemaPath)
        {
            if (value.ValueKind is not JsonValueKind.Number)
                return (null, SecurityInstrumentTermUnitStatus.NotApplicable, null);
            var rateField = SecurityRateFieldUnits.Find(assetClass, schemaPath);
            if (rateField is not null)
            {
                // The current canonical rate-field inventory owns which values honor record units.
                // Field defaults and unit-invariant zero are deliberately not presented as an
                // explicit source declaration, and no amount is normalized for this readout.
                if (rateField.HonorsRecordUnit && !schemaPath.Contains('.')
                    && !SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, assetTerms))
                {
                    var conventionField = SecurityAssetTermsSchema.Field(assetClass, "scheduleConventions");
                    var conventions = Properties(assetTerms, "scheduleConventions");
                    if (conventionField is not null && conventionField.SinceTermsVersion <= termsVersion
                        && termsVersion <= SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass) && conventions.Count == 1)
                    {
                        var couponUnits = Properties(conventions[0].Value, "couponRateUnit");
                        if (couponUnits.Count == 1 && couponUnits[0].Value.ValueKind == JsonValueKind.String
                            && RateUnitNormalizer.ParseCouponRateUnit(couponUnits[0].Value.GetString()) is not null)
                            return (couponUnits[0].Value.GetString(), SecurityInstrumentTermUnitStatus.Explicit,
                                "/assetSpecificTerms/" + Pointer(conventions[0].Name) + "/" + Pointer(couponUnits[0].Name));
                    }
                    var recordUnits = Properties(assetTerms, SecurityRecordRateUnit.FieldKey);
                    if (recordUnits.Count == 1
                        && SecurityRecordRateUnit.Read(assetClass, assetTerms) is RateUnit.Fraction or RateUnit.Percent or RateUnit.BasisPoints)
                        return (recordUnits[0].Value.GetString(), SecurityInstrumentTermUnitStatus.Explicit,
                            "/assetSpecificTerms/" + Pointer(recordUnits[0].Name));
                }
                return (null, SecurityInstrumentTermUnitStatus.Unspecified, null);
            }
            // Only a retained, unambiguous adjacent declaration establishes a unit. In particular,
            // an instrument's reporting currency does not prove strike currency or a quote basis.
            var companion = key switch
            {
                "couponRate" => "couponRateUnit", "yield" or "yieldRate" => "yieldUnit",
                "strike" or "strikePrice" => "strikeCurrency", "price" or "cleanPrice" => "quoteUnit",
                "callPrice" or "putPrice" => "priceBasis",
                _ => null
            };
            List<JsonProperty> units = companion is null ? [] : Properties(siblings, companion);
            if (units.Count == 1 && units[0].Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(units[0].Value.GetString()))
                return (units[0].Value.GetString(), SecurityInstrumentTermUnitStatus.Explicit,
                    path[..path.LastIndexOf('/')] + "/" + Pointer(units[0].Name));
            return (null, SecurityInstrumentTermUnitStatus.Unspecified, null);
        }
    }
}
