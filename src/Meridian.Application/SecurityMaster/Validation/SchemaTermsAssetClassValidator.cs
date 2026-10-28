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
/// conditions); the presence and shape rules come from here, so a field added to the schema is
/// validated without anyone writing a rule for it.
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
    {
        var terms = context.Record.AssetSpecificTerms;
        if (terms.ValueKind != JsonValueKind.Object)
        {
            // The common record-shape validation reports a non-object payload; without an object
            // there is no field to check and every required field would be reported as missing.
            return [];
        }

        // A field above the class's write version (ADR-024, readable before writable) is checked only
        // where the decoder reads it: a flat payload stamped at or above its version. A stray key of
        // that name on an older or profile-governed payload is not a term of the class.
        var writes = SecurityAssetClassCatalog.GetTermsVersion(AssetClass);
        var decodesThrough = SecurityTermsEnvelope.IsProfileGovernedPayload(AssetClass, terms)
            ? writes
            : Math.Max(writes, SecurityTermsEnvelope.ResolveAssetClassTermsVersion(terms));

        var issues = new List<SecurityValidationIssueDto>();
        foreach (var field in _fields)
        {
            if (field.SinceTermsVersion > decodesThrough)
            {
                continue;
            }

            var issue = ValidateField(field, terms);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    private static SecurityValidationIssueDto? ValidateField(SecurityAssetTermField field, JsonElement terms)
    {
        var path = $"assetSpecificTerms.{field.Key}";
        if (!TryGetUnderKeyOrAlias(terms, field, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return field.Required
                ? SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    RequiredCode,
                    "Required term is missing",
                    $"The term schema declares '{field.Key}' as required and the record does not carry it.",
                    [path],
                    $"Populate assetSpecificTerms.{field.Key} before the security is used downstream.")
                : null;
        }

        if (!MatchesType(field.Type, value))
        {
            return SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                TypeCode,
                "Term has the wrong type",
                $"The term schema declares '{field.Key}' as {DescribeType(field.Type)} and the record carries a {DescribeValue(value)}.",
                [path],
                $"Correct assetSpecificTerms.{field.Key} to a {DescribeType(field.Type)} value.");
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
                $"Set assetSpecificTerms.{field.Key} to one of the declared values.");
        }

        return null;
    }

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
            SecurityAssetTermFieldType.Guid => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
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
/// rules run first and keep their codes; a schema issue on a field the hand-written validator has
/// already reported is dropped, so a field is reported once whichever rule set saw it first.
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
        var issues = new List<SecurityValidationIssueDto>(_handWritten.Validate(context));
        if (!_schema.HasSchema)
        {
            return issues;
        }

        var reported = new HashSet<string>(
            issues.SelectMany(static issue => issue.AffectedFields),
            StringComparer.OrdinalIgnoreCase);
        foreach (var issue in _schema.Validate(context))
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
