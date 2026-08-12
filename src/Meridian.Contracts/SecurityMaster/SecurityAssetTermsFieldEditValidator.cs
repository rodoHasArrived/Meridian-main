using System.Globalization;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Schema validation for operator field edits that target a security's asset-specific terms.
/// The workbench edit surface historically accepted any free-text field path and any string value;
/// this validator anchors <c>assetSpecificTerms.*</c> edits to the declared
/// <see cref="SecurityAssetTermsSchema"/> contract for the record's asset class — the key must be a
/// declared term field (or a profile-governed <c>profileFields</c> path on profile-backed classes)
/// and the value must coerce to the declared type. Paths outside the asset-terms namespace are the
/// annotation surface and pass through unchanged.
/// </summary>
public static class SecurityAssetTermsFieldEditValidator
{
    public const string AssetSpecificTermsPrefix = "assetSpecificTerms.";

    /// <summary>True when the field path addresses the asset-specific-terms namespace.</summary>
    public static bool TargetsAssetSpecificTerms(string? fieldPath)
        => fieldPath is not null
           && fieldPath.TrimStart().StartsWith(AssetSpecificTermsPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates an asset-terms field edit against the declared schema for
    /// <paramref name="assetClass"/>. Returns <see langword="true"/> when the edit is acceptable;
    /// otherwise <paramref name="error"/> carries an actionable rejection reason.
    /// A null or whitespace <paramref name="newValue"/> is always acceptable — it clears the
    /// overlay value rather than asserting a typed one.
    /// </summary>
    public static bool TryValidate(string assetClass, string fieldPath, string? newValue, out string? error)
    {
        error = null;
        if (!TargetsAssetSpecificTerms(fieldPath))
        {
            return true;
        }

        var remainder = fieldPath.Trim()[AssetSpecificTermsPrefix.Length..];
        var separatorIndex = remainder.IndexOf('.', StringComparison.Ordinal);
        var key = separatorIndex < 0 ? remainder : remainder[..separatorIndex];
        var nestedPath = separatorIndex < 0 ? string.Empty : remainder[(separatorIndex + 1)..];

        if (string.IsNullOrWhiteSpace(key))
        {
            error = "Field path 'assetSpecificTerms.' must name a term field.";
            return false;
        }

        if (string.Equals(key, "schemaVersion", StringComparison.OrdinalIgnoreCase))
        {
            error = "assetSpecificTerms.schemaVersion is the payload's codec version and is not operator-editable.";
            return false;
        }

        // Profile-backed classes carry dynamic, profile-governed fields under profileFields; their
        // inner shape is owned by the approved profile version, not this schema.
        if (string.Equals(key, "profileFields", StringComparison.OrdinalIgnoreCase)
            && SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms)
        {
            return true;
        }

        var field = FindDeclaredField(assetClass, key);
        if (field is null)
        {
            var declaredKeys = SecurityAssetTermsSchema.Fields(assetClass).Select(static f => f.Key);
            error =
                $"'{key}' is not a declared asset-specific term for asset class '{assetClass}'. " +
                $"Declared term fields: {string.Join(", ", declaredKeys)}.";
            return false;
        }

        if (nestedPath.Length > 0
            && field.Type is not (SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array))
        {
            error =
                $"'{field.Key}' is declared as {field.Type} for asset class '{assetClass}' and has no " +
                $"nested path '{nestedPath}'.";
            return false;
        }

        if (nestedPath.Length > 0 || string.IsNullOrWhiteSpace(newValue))
        {
            // Nested paths inside Object/Array fields are not enumerated by the schema, and a blank
            // value clears the overlay entry instead of asserting a typed value.
            return true;
        }

        if (!ValueCoercesToType(field.Type, newValue))
        {
            error =
                $"Value '{newValue}' does not parse as the declared type {field.Type} for " +
                $"'{AssetSpecificTermsPrefix}{field.Key}' on asset class '{assetClass}'.";
            return false;
        }

        return true;
    }

    private static SecurityAssetTermField? FindDeclaredField(string assetClass, string key)
    {
        var direct = SecurityAssetTermsSchema.Field(assetClass, key);
        if (direct is not null)
        {
            return direct;
        }

        // Legacy flat-key aliases a tolerant reader accepts are legitimate edit targets too.
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            foreach (var alias in field.Aliases)
            {
                if (string.Equals(alias, key, StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }
        }

        return null;
    }

    private static bool ValueCoercesToType(SecurityAssetTermFieldType type, string newValue)
        => type switch
        {
            SecurityAssetTermFieldType.String => true,
            SecurityAssetTermFieldType.Decimal => decimal.TryParse(newValue, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            SecurityAssetTermFieldType.Integer => int.TryParse(newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            SecurityAssetTermFieldType.Boolean => bool.TryParse(newValue, out _),
            SecurityAssetTermFieldType.Date => DateOnly.TryParse(newValue, CultureInfo.InvariantCulture, out _)
                                               || DateTimeOffset.TryParse(newValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            SecurityAssetTermFieldType.Guid => Guid.TryParse(newValue, out _),
            SecurityAssetTermFieldType.Array => TryParseJsonKind(newValue, JsonValueKind.Array),
            SecurityAssetTermFieldType.Object => TryParseJsonKind(newValue, JsonValueKind.Object),
            _ => true
        };

    private static bool TryParseJsonKind(string newValue, JsonValueKind expected)
    {
        try
        {
            using var document = JsonDocument.Parse(newValue);
            return document.RootElement.ValueKind == expected;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
