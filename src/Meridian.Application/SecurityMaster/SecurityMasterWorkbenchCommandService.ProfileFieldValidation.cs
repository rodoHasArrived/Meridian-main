using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Static profile-field validation and effective-overlay layering helpers for
/// <see cref="SecurityMasterWorkbenchCommandService"/>: resolving EFFECTIVE profile dates across
/// staged override layers, reconstructing the post-approval profileFields object, coercing
/// string-staged overrides to declared field types, and enforcing pinned-profile type/range rules.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService
{
    /// <summary>
    /// A profile field's EFFECTIVE date: the staged per-field operator override when one exists,
    /// then a field inside a staged WHOLE-OBJECT profileFields replacement (a replacement is what
    /// the record reads after approval, so falling through it to the superseded canonical value
    /// would validate against dates the overlay has already replaced), and only then the canonical
    /// projection value.
    /// </summary>
    private static bool TryResolveEffectiveProfileDate(
        string fieldKey,
        JsonElement? currentProfileFields,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        out DateOnly value)
    {
        value = default;
        if (stagedOverrides is not null)
        {
            var overridePath = ProfileFieldsNestedPrefix + fieldKey;
            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (string.Equals(path, overridePath, StringComparison.OrdinalIgnoreCase)
                    && DateOnly.TryParse(overrideValue.Trim(), System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }

            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (!string.Equals(path, ProfileFieldsRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var replacement = JsonDocument.Parse(overrideValue);
                    if (replacement.RootElement.ValueKind == JsonValueKind.Object
                        && TryReadProfileDate(replacement.RootElement, fieldKey, out value))
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // A malformed staged replacement cannot supply the counterpart; fall through
                    // to the canonical value.
                }
            }
        }

        return currentProfileFields is JsonElement retainedFields
            && TryReadProfileDate(retainedFields, fieldKey, out value);
    }

    private static bool TryReadProfileDate(JsonElement profileFields, string key, out DateOnly value)
    {
        value = default;
        foreach (var property in profileFields.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(property.Value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The EFFECTIVE date a whole-object profileFields replacement stages for
    /// <paramref name="fieldKey"/>: a staged per-field override outranks the replacement (the
    /// override layer is applied on top of the object layer when the record is read), so date-order
    /// rules must bind against it — only in its absence does the replacement's own value apply. A
    /// previously staged whole-object replacement is NOT consulted: the replacement being validated
    /// supersedes it.
    /// </summary>
    private static bool TryResolveEffectiveReplacementDate(
        string fieldKey,
        JsonElement replacementRoot,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        out DateOnly value)
    {
        value = default;
        if (stagedOverrides is not null)
        {
            var overridePath = ProfileFieldsNestedPrefix + fieldKey;
            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (string.Equals(path, overridePath, StringComparison.OrdinalIgnoreCase)
                    && DateOnly.TryParse(overrideValue.Trim(), System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }
        }

        return TryReadProfileDate(replacementRoot, fieldKey, out value);
    }

    /// <summary>
    /// A profile-governed field edit on a record whose asset class RESOLVED past CustomAsset
    /// (e.g. a private-fund-interest envelope reclassified to PrivateFundInterest) must satisfy
    /// the resolved class's domain invariants, not just the pinned profile's field rules: the
    /// seeded profile permits <c>commitment = 0</c> while the PrivateFundInterest kind requires a
    /// strictly positive commitment, so a profile-valid edit could stage — and approve — an
    /// overlay the canonical amend seam rejects. Reconstructs the record's EFFECTIVE profileFields
    /// (base object layer, staged per-field overrides, then the proposed edit), parses the
    /// resolved kind from the effective envelope, and runs the canonical kind invariants. Fails
    /// CLOSED when the effective terms cannot be reconstructed or parsed — a reserved-namespace
    /// edit whose effective outcome cannot be validated must not stage.
    /// </summary>
    private static void EnsureEffectiveOverlaySatisfiesResolvedKindInvariants(
        SecurityProjectionRecord projection,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto profile,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        string? editedFieldKey,
        string? proposedValue,
        JsonElement? proposedReplacementRoot)
    {
        if (string.Equals(projection.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(projection.AssetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase))
        {
            // A CustomAsset overlay is governed by the pinned profile alone; there is no resolved
            // kind whose invariants could tighten the profile's declared field rules.
            return;
        }

        try
        {
            var effectiveFields = BuildEffectiveProfileFields(
                projection, profile, stagedOverrides, editedFieldKey, proposedValue, proposedReplacementRoot);
            var envelope = System.Text.Json.Nodes.JsonNode.Parse(projection.AssetSpecificTerms.GetRawText())?.AsObject()
                ?? throw new InvalidOperationException("The record's assetSpecificTerms envelope is not a JSON object.");
            foreach (var variantKey in envelope
                .Where(static property => string.Equals(property.Key, "profileFields", StringComparison.OrdinalIgnoreCase))
                .Select(static property => property.Key)
                .ToArray())
            {
                envelope.Remove(variantKey);
            }

            envelope["profileFields"] = effectiveFields;
            var effectiveTerms = JsonSerializer.SerializeToElement(envelope);
            var kind = SecurityMasterMapping.ToRecord(projection with { AssetSpecificTerms = effectiveTerms }).Kind;
            var invariantErrors = Meridian.FSharp.SecurityMasterInterop.SecurityMasterCommandFacade.ValidateKindInvariants(kind);
            if (invariantErrors.Length > 0)
            {
                var summary = string.Join("; ", invariantErrors.Select(static e => $"[{e.Code}] {e.Message}"));
                throw new ArgumentException(
                    $"The effective profileFields overlay violates the resolved asset class " +
                    $"'{projection.AssetClass}' domain invariants: {summary}");
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ArgumentException(
                $"The effective profileFields overlay for security '{projection.SecurityId:D}' could not be " +
                $"reconstructed and validated against the resolved asset class '{projection.AssetClass}' " +
                $"({ex.Message}); the namespace only accepts validated writes.", ex);
        }
    }

    /// <summary>
    /// Layers the record's effective profileFields the way the post-approval read applies them:
    /// the base object layer (the proposed whole-object replacement when this edit IS one, else a
    /// previously staged whole-object replacement, else the canonical profileFields), then staged
    /// per-field overrides on top, then the proposed scalar edit (which supersedes any staged
    /// override of the same field).
    /// </summary>
    private static System.Text.Json.Nodes.JsonObject BuildEffectiveProfileFields(
        SecurityProjectionRecord projection,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto profile,
        IReadOnlyDictionary<string, string>? stagedOverrides,
        string? editedFieldKey,
        string? proposedValue,
        JsonElement? proposedReplacementRoot)
    {
        System.Text.Json.Nodes.JsonObject baseFields;
        if (proposedReplacementRoot is JsonElement replacementRoot)
        {
            baseFields = System.Text.Json.Nodes.JsonNode.Parse(replacementRoot.GetRawText())!.AsObject();
        }
        else
        {
            string? stagedRootReplacement = null;
            if (stagedOverrides is not null)
            {
                foreach (var (path, overrideValue) in stagedOverrides)
                {
                    if (string.Equals(path, ProfileFieldsRootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        stagedRootReplacement = overrideValue;
                        break;
                    }
                }
            }

            if (stagedRootReplacement is not null)
            {
                baseFields = System.Text.Json.Nodes.JsonNode.Parse(stagedRootReplacement)!.AsObject();
            }
            else if (projection.AssetSpecificTerms is { ValueKind: JsonValueKind.Object } terms
                && terms.TryGetProperty("profileFields", out var persisted)
                && persisted.ValueKind == JsonValueKind.Object)
            {
                baseFields = System.Text.Json.Nodes.JsonNode.Parse(persisted.GetRawText())!.AsObject();
            }
            else
            {
                baseFields = new System.Text.Json.Nodes.JsonObject();
            }
        }

        if (stagedOverrides is not null)
        {
            foreach (var (path, overrideValue) in stagedOverrides)
            {
                if (!path.StartsWith(ProfileFieldsNestedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var overriddenKey = path[ProfileFieldsNestedPrefix.Length..];
                if (overriddenKey.Length == 0
                    || overriddenKey.Contains('.', StringComparison.Ordinal)
                    || (editedFieldKey is not null
                        && string.Equals(overriddenKey, editedFieldKey, StringComparison.OrdinalIgnoreCase)))
                {
                    // Deeper subpaths are rejected as edits for declared fields and stay dynamic
                    // junk for undeclared ones; the edited field's old override is superseded by
                    // the proposed value below.
                    continue;
                }

                SetEffectiveProfileField(baseFields, profile, overriddenKey, overrideValue);
            }
        }

        if (editedFieldKey is not null && proposedValue is not null)
        {
            SetEffectiveProfileField(baseFields, profile, editedFieldKey, proposedValue);
        }

        return baseFields;
    }

    /// <summary>
    /// Writes a string-staged override into the effective profileFields object coerced to the
    /// declared field type (overrides are stored as strings while the kind parser reads typed
    /// JSON), replacing any case-variant spelling of the key so the layered value cannot fork
    /// from the canonical one.
    /// </summary>
    private static void SetEffectiveProfileField(
        System.Text.Json.Nodes.JsonObject fields,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileDefinitionDto profile,
        string key,
        string value)
    {
        var declared = profile.Fields.FirstOrDefault(
            field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
        var canonicalKey = declared?.Key ?? key;
        foreach (var variantKey in fields
            .Where(property => string.Equals(property.Key, canonicalKey, StringComparison.OrdinalIgnoreCase))
            .Select(static property => property.Key)
            .ToArray())
        {
            fields.Remove(variantKey);
        }

        var trimmed = value.Trim();
        System.Text.Json.Nodes.JsonNode? node = declared?.FieldType switch
        {
            SecurityAssetProfileFieldTypeDto.Decimal when decimal.TryParse(
                trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var decimalValue)
                => System.Text.Json.Nodes.JsonValue.Create(decimalValue),
            SecurityAssetProfileFieldTypeDto.Integer when int.TryParse(
                trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integerValue)
                => System.Text.Json.Nodes.JsonValue.Create(integerValue),
            SecurityAssetProfileFieldTypeDto.Boolean when bool.TryParse(trimmed, out var booleanValue)
                => System.Text.Json.Nodes.JsonValue.Create(booleanValue),
            // An UNDECLARED key has no profile type to coerce through, but the resolved kind's
            // parser reads typed JSON: a structured value edited as its JSON text (an array of
            // factor rows, a number) must land as that structure, not as a quoted string the
            // parser cannot read. Values that do not parse as JSON stay plain strings.
            null => TryParseJsonNode(trimmed) ?? System.Text.Json.Nodes.JsonValue.Create(value),
            _ => System.Text.Json.Nodes.JsonValue.Create(value)
        };
        fields[canonicalKey] = node;
    }

    private static System.Text.Json.Nodes.JsonNode? TryParseJsonNode(string value)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ProfileFieldStringIsValid(
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldDefinitionDto field,
        string value,
        out string? error)
    {
        error = null;
        var trimmed = value.Trim();
        var typeIsValid = field.FieldType switch
        {
            SecurityAssetProfileFieldTypeDto.Text => true,
            SecurityAssetProfileFieldTypeDto.Decimal =>
                decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Integer =>
                int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Boolean => bool.TryParse(trimmed, out _),
            SecurityAssetProfileFieldTypeDto.Date =>
                DateOnly.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Enum =>
                field.AllowedValues.Any(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)),
            SecurityAssetProfileFieldTypeDto.CurrencyCode =>
                trimmed.Length == 3 && trimmed.All(static character => character is >= 'A' and <= 'Z'),
            SecurityAssetProfileFieldTypeDto.SecurityLink =>
                Guid.TryParse(trimmed, out var link) && link != Guid.Empty,
            _ => true
        };
        if (!typeIsValid)
        {
            error =
                $"Value '{value}' does not satisfy the pinned profile's declared type {field.FieldType} " +
                $"for profile field '{field.Key}'.";
            return false;
        }

        if (field.FieldType is SecurityAssetProfileFieldTypeDto.Decimal or SecurityAssetProfileFieldTypeDto.Integer
            && decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var numeric)
            && ((field.MinValue.HasValue && numeric < field.MinValue.Value)
                || (field.MaxValue.HasValue && numeric > field.MaxValue.Value)))
        {
            error =
                $"Value '{value}' is outside the pinned profile's allowed range for field '{field.Key}' " +
                $"({field.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}" +
                $"–{field.MaxValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}).";
            return false;
        }

        return true;
    }

    private static bool ProfileFieldElementIsValid(
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldDefinitionDto field,
        JsonElement value,
        out string? error)
    {
        error = null;
        var typeIsValid = field.FieldType switch
        {
            // A required Text field must also be nonblank, mirroring the read-side profile
            // validator: an empty required string strips the value while passing the kind check.
            SecurityAssetProfileFieldTypeDto.Text =>
                value.ValueKind == JsonValueKind.String
                && (!field.IsRequired || !string.IsNullOrWhiteSpace(value.GetString())),
            SecurityAssetProfileFieldTypeDto.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetProfileFieldTypeDto.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SecurityAssetProfileFieldTypeDto.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetProfileFieldTypeDto.Date =>
                value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out _),
            SecurityAssetProfileFieldTypeDto.Enum =>
                value.ValueKind == JsonValueKind.String
                && field.AllowedValues.Any(allowed => string.Equals(allowed, value.GetString(), StringComparison.OrdinalIgnoreCase)),
            SecurityAssetProfileFieldTypeDto.CurrencyCode =>
                value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: 3 } currency
                && currency.All(static character => character is >= 'A' and <= 'Z'),
            SecurityAssetProfileFieldTypeDto.SecurityLink =>
                value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var link)
                && link != Guid.Empty,
            _ => true
        };
        if (!typeIsValid)
        {
            error =
                $"profileFields.{field.Key} does not satisfy the pinned profile's declared type {field.FieldType}.";
            return false;
        }

        if (field.FieldType is SecurityAssetProfileFieldTypeDto.Decimal or SecurityAssetProfileFieldTypeDto.Integer
            && value.TryGetDecimal(out var numeric)
            && ((field.MinValue.HasValue && numeric < field.MinValue.Value)
                || (field.MaxValue.HasValue && numeric > field.MaxValue.Value)))
        {
            error =
                $"profileFields.{field.Key} is outside the pinned profile's allowed range " +
                $"({field.MinValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}" +
                $"–{field.MaxValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unbounded"}).";
            return false;
        }

        return true;
    }
}
