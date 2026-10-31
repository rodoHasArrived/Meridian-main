using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>
/// Validation generated from the asset-class term schema. For every field
/// <see cref="SecurityAssetTermsSchema"/> declares for the record's class, a required field must be
/// present under its key or one of its aliases, a present field must carry the declared JSON type,
/// and a closed-vocabulary field must carry a declared value. The hand-written class validators
/// keep only the semantic rules the schema cannot express (ranges, date order, cross-field
/// conditions). Economic schedules use <see cref="SecurityEconomicScheduleParser"/> for presence,
/// shape and row diagnostics, sharing the cash-flow and relational projection contract; all other
/// presence and shape rules come from the schema.
/// </summary>
internal sealed class SchemaTermsAssetClassValidator : ISecurityAssetClassValidator
{
    public const string RequiredCode = "SM_TERM_REQUIRED";
    public const string TypeCode = "SM_TERM_TYPE_INVALID";
    public const string VocabularyCode = "SM_TERM_VALUE_NOT_ALLOWED";

    private readonly IReadOnlyList<SecurityAssetTermField> _fields;

    public SchemaTermsAssetClassValidator(string assetClass)
    {
        AssetClass = assetClass;
        _fields = SecurityAssetTermsSchema.Fields(assetClass);
    }

    public string AssetClass { get; }

    /// <summary>True when the schema declares fields for this class, so there is something to generate from.</summary>
    public bool HasSchema => _fields.Count > 0;

    public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context)
        => Validate(context.Record.AssetSpecificTerms, forWrite: false, commonTerms: context.Record.CommonTerms);

    /// <summary>
    /// Checks new canonical writes against the fields this node writes. Unlike retained-row
    /// diagnostics, admission requires every required member inside an item to be present.
    /// Readable-ahead fields are deliberately left to the versioned-term round-trip guard.
    /// </summary>
    public IReadOnlyList<SecurityValidationIssueDto> ValidateForWrite(SecurityValidationContext context)
        => Validate(context.Record.AssetSpecificTerms, forWrite: true, commonTerms: context.Record.CommonTerms);

    /// <summary>Checks supplied members before tolerant decoding can discard malformed input.</summary>
    public IReadOnlyList<SecurityValidationIssueDto> ValidateSubmittedFieldsForWrite(JsonElement terms)
        => Validate(terms, forWrite: true, requirePresence: false);

    private IReadOnlyList<SecurityValidationIssueDto> Validate(
        JsonElement terms, bool forWrite, bool requirePresence = true, JsonElement commonTerms = default)
    {
        // Submitted terms may omit unrelated top-level fields, but a supplied schedule is a whole
        // assertion: every row must parse. Read diagnostics and write admission therefore use the
        // same schedule pass, including governed ownership and common-term fallback.
        var economicSchedules = SecurityEconomicScheduleParser.Parse(terms, commonTerms);
        var issues = economicSchedules.Diagnostics
            .Select(static diagnostic => SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                diagnostic.Code,
                "Economic schedule is invalid",
                diagnostic.Message,
                [diagnostic.Path],
                $"Correct {diagnostic.Path} before the security is used downstream."))
            .ToList();

        if (terms.ValueKind != JsonValueKind.Object)
        {
            // The record-shape validator owns the non-object payload diagnostic.
            return issues;
        }

        var profileGoverned = SecurityTermsEnvelope.IsProfileGovernedPayload(AssetClass, terms);
        if (forWrite && profileGoverned)
        {
            // Class schemas do not govern approved profile payloads (ADR-024). The shared
            // economic schedule pass above still validates the source the resolver consumes.
            return issues;
        }

        // A field above the class's write version (ADR-024, readable before writable) is checked only
        // where the decoder reads it: a flat payload stamped at or above its version. A stray key of
        // that name on an older or profile-governed payload is not a term of the class. Versioned
        // containers are gated by the predicate the mapping decodes them with
        // (SecurityVersionedTermDecoding); later scalar terms keep the stamp ceiling below.
        var writes = SecurityAssetClassCatalog.GetTermsVersion(AssetClass);
        var decodesThrough = forWrite || profileGoverned
            ? writes
            : Math.Max(writes, SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms));
        var versionedTerms = SecurityVersionedTerms.Decoding(AssetClass, terms, writeMode: forWrite);

        ValidateFields(terms, _fields, "", "assetSpecificTerms", decodesThrough, versionedTerms, economicSchedules,
            requirePresence, forWrite, issues);
        return issues;
    }

    private void ValidateFields(
        JsonElement terms,
        IReadOnlyList<SecurityAssetTermField> fields,
        string schemaPath,
        string valuePath,
        int decodesThrough,
        SecurityVersionedTermDecoding versionedTerms,
        SecurityEconomicScheduleValidation economicSchedules,
        bool requirePresence,
        bool forWrite,
        List<SecurityValidationIssueDto> issues)
    {
        foreach (var field in fields)
        {
            if (SecurityVersionedTerms.IsVersionedContainer(field)
                    ? !versionedTerms.Decodes(field)
                    : field.SinceTermsVersion > decodesThrough)
            {
                continue;
            }

            var path = $"{valuePath}.{field.Key}";
            // These schedules are already validated as selected typed sources. Preserve a class's
            // required top-level assertion (DirectLoan principalSchedule), using shared presence so
            // a governed/common fallback qualifies and an invalid declaration is not called absent.
            // Partial submitted-field validation still permits omitted top-level fields.
            if (schemaPath.Length == 0 && SecurityEconomicScheduleParser.TryGetKind(field.Key, out var scheduleKind))
            {
                var present = scheduleKind switch
                {
                    SecurityEconomicScheduleKind.Factor => economicSchedules.FactorSchedule.IsPresent,
                    SecurityEconomicScheduleKind.Principal => economicSchedules.PrincipalSchedule.IsPresent,
                    _ => economicSchedules.StepCouponSchedule.IsPresent
                };
                if (field.Type == SecurityAssetTermFieldType.Array && !present
                    && ValidateField(field, exists: false, default, path, requirePresence) is { } requiredIssue)
                {
                    issues.Add(requiredIssue);
                }
                // Row aliases, type coercion and shadowed sources belong solely to the parser.
                // Its free-text factorSchedule exemption also retains the evidence-reference field.
                continue;
            }

            var exists = TryGetUnderKeyOrAlias(terms, field, out var value);
            // Versioned containers use ADR-024's unset vocabulary before JSON-kind checks:
            // null, [], {}, and wholly unset members all declare no term.
            if (exists && SecurityVersionedTerms.IsVersionedContainer(field)
                && !SecurityAssetTermsSchema.IsPopulatedTermValue(value))
                continue;
            var issue = ValidateField(field, exists, value, path, requirePresence);
            if (issue is not null)
            {
                issues.Add(issue);
                continue;
            }
            if (!exists || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var childSchemaPath = schemaPath.Length == 0 ? field.Key : $"{schemaPath}.{field.Key}";
            var children = SecurityAssetTermsSchema.ElementFields(AssetClass, childSchemaPath);
            if (children.Count == 0)
            {
                continue;
            }
            if (field.Type == SecurityAssetTermFieldType.Object)
            {
                ValidateFields(value, children, childSchemaPath, path, decodesThrough, versionedTerms, economicSchedules,
                    requirePresence: forWrite && requirePresence, forWrite, issues);
            }
            else if (field.Type == SecurityAssetTermFieldType.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    var itemPath = $"{path}.{index++}";
                    if (SecurityVersionedTerms.IsVersionedContainer(field)
                        && (field.Key is SecurityVersionedTerms.CallScheduleKey or SecurityVersionedTerms.PutScheduleKey)
                        && (item.ValueKind == JsonValueKind.Null
                            || (item.ValueKind == JsonValueKind.Object && !SecurityAssetTermsSchema.IsPopulatedTermValue(item))))
                        continue;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        issues.Add(TypeIssue(field.Key, SecurityAssetTermFieldType.Object, item, itemPath));
                        continue;
                    }
                    ValidateFields(item, children, childSchemaPath, itemPath, decodesThrough, versionedTerms, economicSchedules,
                        requirePresence: forWrite && requirePresence, forWrite, issues);
                }
            }
        }
    }

    private static SecurityValidationIssueDto? ValidateField(
        SecurityAssetTermField field, bool exists, JsonElement value, string path, bool requirePresence)
    {
        if (!exists || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return field.Required && requirePresence
                ? SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    RequiredCode,
                    "Required term is missing",
                    $"The term schema declares '{field.Key}' as required and the record does not carry it.",
                    [path],
                    $"Populate {path} before the security is used downstream.")
                : null;
        }

        if (field.Required && value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
        {
            return SecurityValidationIssueFactory.Create(SecurityValidationSeverityDto.Error, RequiredCode,
                "Required term is blank", $"The required term '{field.Key}' must not be blank.", [path],
                $"Populate {path} before the security is used downstream.");
        }

        if (!MatchesType(field.Type, value))
        {
            return TypeIssue(field.Key, field.Type, value, path);
        }

        if (field.IsDiscriminant
            && !field.CarriesUndeclaredValueVerbatim
            && value.ValueKind == JsonValueKind.String
            && !field.Allows(value.GetString()))
        {
            return SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                VocabularyCode,
                "Term value is outside its declared vocabulary",
                $"The term schema declares '{field.Key}' as one of {string.Join(", ", field.AllowedValues)}; the record carries '{value.GetString()}'.",
                [path],
                $"Set {path} to one of the declared values.");
        }

        return null;
    }

    private static SecurityValidationIssueDto TypeIssue(string key, SecurityAssetTermFieldType type, JsonElement value, string path)
        => SecurityValidationIssueFactory.Create(
            SecurityValidationSeverityDto.Error,
            TypeCode,
            "Term has the wrong type",
            $"The term schema declares '{key}' as {DescribeType(type)} and the record carries a {DescribeValue(value)}.",
            [path],
            $"Correct {path} to a {DescribeType(type)} value.");

    private static bool TryGetUnderKeyOrAlias(JsonElement terms, SecurityAssetTermField field, out JsonElement value)
    {
        if (JsonValidationReader.TryGetProperty(terms, field.Key, out value))
        {
            return true;
        }

        foreach (var alias in field.Aliases)
        {
            if (JsonValidationReader.TryGetProperty(terms, alias, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool MatchesType(SecurityAssetTermFieldType type, JsonElement value)
        => type switch
        {
            SecurityAssetTermFieldType.String => value.ValueKind == JsonValueKind.String,
            SecurityAssetTermFieldType.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetTermFieldType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            SecurityAssetTermFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetTermFieldType.Date => value.ValueKind == JsonValueKind.String && IsDate(value.GetString()),
            SecurityAssetTermFieldType.Guid => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid) && guid != Guid.Empty,
            SecurityAssetTermFieldType.Array => value.ValueKind == JsonValueKind.Array,
            SecurityAssetTermFieldType.Object => value.ValueKind == JsonValueKind.Object,
            _ => true
        };

    private static bool IsDate(string? text)
        => DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
           || DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

    private static string DescribeType(SecurityAssetTermFieldType type)
        => type switch
        {
            SecurityAssetTermFieldType.String => "string",
            SecurityAssetTermFieldType.Decimal => "number",
            SecurityAssetTermFieldType.Integer => "integer",
            SecurityAssetTermFieldType.Boolean => "boolean",
            SecurityAssetTermFieldType.Date => "date",
            SecurityAssetTermFieldType.Guid => "GUID",
            SecurityAssetTermFieldType.Array => "array",
            SecurityAssetTermFieldType.Object => "object",
            _ => type.ToString()
        };

    private static string DescribeValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            _ => value.ValueKind.ToString().ToLowerInvariant()
        };
}

/// <summary>
/// Composes the schema-generated rules with a class's hand-written validator. The hand-written
/// semantic rules run after schema presence and type checks. A semantic issue is suppressed when
/// its field already has a schema issue, so malformed data has one authoritative shape diagnostic.
/// </summary>
internal sealed class SchemaBackedAssetClassValidator : ISecurityAssetClassValidator
{
    private readonly ISecurityAssetClassValidator _handWritten;
    private readonly SchemaTermsAssetClassValidator _schema;

    public SchemaBackedAssetClassValidator(ISecurityAssetClassValidator handWritten)
    {
        ArgumentNullException.ThrowIfNull(handWritten);
        _handWritten = handWritten;
        _schema = new SchemaTermsAssetClassValidator(handWritten.AssetClass);
    }

    public string AssetClass => _handWritten.AssetClass;

    public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context)
    {
        if (!_schema.HasSchema)
        {
            return _handWritten.Validate(context);
        }

        var issues = new List<SecurityValidationIssueDto>(_schema.Validate(context));
        var reported = new HashSet<string>(
            issues.SelectMany(static issue => issue.AffectedFields),
            StringComparer.OrdinalIgnoreCase);
        foreach (var issue in _handWritten.Validate(context))
        {
            if (!issue.AffectedFields.Any(reported.Contains))
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    /// <summary>Every validator wrapped so its class gets the schema-generated rules.</summary>
    public static IReadOnlyList<ISecurityAssetClassValidator> WrapAll(IEnumerable<ISecurityAssetClassValidator> validators)
        => validators.Select(static validator => (ISecurityAssetClassValidator)new SchemaBackedAssetClassValidator(validator)).ToArray();
}
