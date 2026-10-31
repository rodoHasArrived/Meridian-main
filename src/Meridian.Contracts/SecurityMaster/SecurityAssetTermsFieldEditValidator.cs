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
/// annotation surface and pass through unchanged. Discriminant fields carry the additional
/// constraint that the value must be one of the schema's declared vocabulary members.
/// </summary>
public static class SecurityAssetTermsFieldEditValidator
{
    public const string AssetSpecificTermsPrefix = "assetSpecificTerms.";

    /// <summary>
    /// True when the field path addresses the asset-specific-terms namespace — including the EXACT
    /// root <c>assetSpecificTerms</c> (no trailing dot). The bare root never validates (it names
    /// no schema field), but it must still classify as reserved: treating it as a free annotation
    /// would let a root-level asset-terms value stage past the schema, pinned-profile validation,
    /// and the overrides endpoint's reserved-namespace rejection.
    /// </summary>
    public static bool TargetsAssetSpecificTerms(string? fieldPath)
    {
        if (fieldPath is null)
        {
            return false;
        }

        var trimmed = fieldPath.Trim();
        return trimmed.StartsWith(AssetSpecificTermsPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "assetSpecificTerms", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates an asset-terms field edit against the declared schema for
    /// <paramref name="assetClass"/>. Returns <see langword="true"/> when the edit is acceptable;
    /// otherwise <paramref name="error"/> carries an actionable rejection reason.
    /// A null or whitespace <paramref name="newValue"/> is always acceptable — it clears the
    /// overlay value rather than asserting a typed one.
    /// <para><paramref name="canonicalFieldPath"/> is the schema-normalized path the edit must be
    /// PERSISTED under: aliases resolve to the declared field key and casing/whitespace variants
    /// collapse to one spelling, so <c>assetSpecificTerms.dayCount</c> and its alias
    /// <c>assetSpecificTerms.dayCountConvention</c> address the same override key, revision lineage,
    /// and provenance row instead of forking per spelling. Paths outside the asset-terms namespace
    /// (and rejected edits) pass through unchanged.</para>
    /// </summary>
    public static bool TryValidate(
        string assetClass, string fieldPath, string? newValue, out string canonicalFieldPath, out string? error)
    {
        error = null;
        canonicalFieldPath = fieldPath;
        if (!TargetsAssetSpecificTerms(fieldPath))
        {
            return true;
        }

        var trimmedPath = fieldPath.Trim();
        if (!trimmedPath.StartsWith(AssetSpecificTermsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Field path 'assetSpecificTerms' is the reserved asset-terms namespace root, not a schema field; " +
                "edit a specific term such as 'assetSpecificTerms.maturity'.";
            return false;
        }

        var remainder = trimmedPath[AssetSpecificTermsPrefix.Length..];
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

        if (SecurityTermsEnvelope.IsReservedKey(key))
        {
            error = $"assetSpecificTerms.{key} is part of the terms payload envelope (format, contract version, " +
                "or retained source document) and is not operator-editable.";
            return false;
        }

        // The profile GOVERNANCE envelope is not operator-editable field by field: repinning
        // customProfileId or profileVersion through a scalar override would change the record's
        // profile identity while retaining the old profileFields and approval metadata, bypassing
        // the complete-envelope catalog validation and reclassification the canonical amend seam
        // performs — and profileApproval is immutable audit evidence, not an override target.
        if (string.Equals(key, "customProfileId", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "profileVersion", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "profileApproval", StringComparison.OrdinalIgnoreCase))
        {
            error =
                $"assetSpecificTerms.{key} is part of the governed profile envelope and cannot be edited " +
                "field by field; submit a complete profile envelope through a governed amendment instead.";
            return false;
        }

        // Profile-backed classes carry dynamic, profile-governed fields beneath profileFields;
        // their inner shape is owned by the approved profile version, not this schema. A whole-root
        // replacement still has to be a JSON object, including for profile-backed asset classes
        // whose static schema does not declare the synthetic profileFields envelope.
        if (string.Equals(key, "profileFields", StringComparison.OrdinalIgnoreCase)
            && SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms)
        {
            if (nestedPath.Length > 0 || string.IsNullOrWhiteSpace(newValue))
            {
                canonicalFieldPath = BuildCanonicalPath("profileFields", nestedPath);
                return true;
            }

            if (!ValueCoercesToType(SecurityAssetTermFieldType.Object, newValue))
            {
                error =
                    $"Value '{newValue}' does not parse as the declared type Object for " +
                    $"'{AssetSpecificTermsPrefix}profileFields' on asset class '{assetClass}'.";
                return false;
            }

            canonicalFieldPath = BuildCanonicalPath("profileFields", nestedPath);
            return true;
        }

        var field = FindDeclaredField(assetClass, key);
        if (field is null)
        {
            var declaredKeys = SecurityAssetTermsSchema.WritableFields(assetClass).Select(static f => f.Key);
            error =
                $"'{key}' is not a declared asset-specific term for asset class '{assetClass}'. " +
                $"Declared term fields: {string.Join(", ", declaredKeys)}.";
            return false;
        }

        // A term this node only reads (ADR-024, readable before writable) cannot be staged at all —
        // not set, not cleared and not edited beneath — because the merge would map it in Write mode
        // and refuse it after approval. The same holds for a readable-only field inside a declared
        // array element or nested object (a swap leg's scheduleConventions), and for every member of
        // a readable-only container (callSchedule, putSchedule).
        if (TryRefuseReadableOnlyEdit(assetClass, field, nestedPath, newValue, out error))
        {
            return false;
        }

        if (nestedPath.Length > 0 && !string.IsNullOrWhiteSpace(newValue)
            && TargetsVersionedContainer(assetClass, field, nestedPath))
        {
            error = "Declared schedule containers carry window, convention and scalar consistency rules; replace the whole container instead of editing a member.";
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
            // A VALUE edit beneath a known contractual schedule cannot be row-validated in
            // isolation — principalSchedule.0.amount = -10 would stage an override the domain
            // rejects wholesale — so those subpaths only accept clears; assert values by
            // replacing the whole array, which runs the schedule invariants. Other nested paths
            // inside Object/Array fields stay dynamic pass-through, and a blank value clears the
            // overlay entry instead of asserting a typed one.
            if (nestedPath.Length > 0
                && !string.IsNullOrWhiteSpace(newValue)
                && SecurityEconomicScheduleParser.TryGetKind(field.Key, out _))
            {
                error =
                    $"'{field.Key}' rows carry schedule-wide domain invariants that a nested edit cannot " +
                    "be validated against; replace the whole array instead.";
                return false;
            }

            canonicalFieldPath = BuildCanonicalPath(field.Key, nestedPath);
            return true;
        }

        if (SecurityVersionedTerms.IsVersionedContainer(field))
        {
            try
            {
                using var unset = JsonDocument.Parse(newValue);
                if (!SecurityAssetTermsSchema.IsPopulatedTermValue(unset.RootElement))
                {
                    canonicalFieldPath = BuildCanonicalPath(field.Key, nestedPath);
                    return true;
                }
                if (field.Key == SecurityVersionedTerms.ScheduleConventionsKey
                    && (SecurityAssetTermsSchema.Field(assetClass, "issueDate") is null || SecurityAssetTermsSchema.Field(assetClass, "paymentFrequency") is null))
                {
                    error = "Declared schedule conventions require schema-declared issueDate and paymentFrequency anchors.";
                    return false;
                }
            }
            catch (JsonException)
            {
                // The ordinary declared-type diagnostic below owns malformed JSON.
            }
        }

        if (!ValueCoercesToType(field.Type, newValue))
        {
            error =
                $"Value '{newValue}' does not parse as the declared type {field.Type} for " +
                $"'{AssetSpecificTermsPrefix}{field.Key}' on asset class '{assetClass}'.";
            return false;
        }

        // A DISCRIMINANT is not free text: its value selects a domain case, and a value outside the
        // declared vocabulary either vanishes on the next serialize (an unknown couponType collapses
        // to Fixed) or silently re-types the security. Staging one would put a draft and a
        // provenance row behind a value the write-mode codec refuses, so reject it at the edit.
        // The comparison is exact-case for the same reason the codecs are — see
        // SecurityAssetTermField.Allows.
        if (!field.Allows(newValue))
        {
            error = field.DescribeUndeclaredValue(assetClass, newValue);
            return false;
        }

        if (!field.SatisfiesConvention(newValue))
        {
            error = SecurityTermConventions.DescribeUnrecognized(field.Convention, assetClass, field.Key, newValue);
            return false;
        }

        // Whole economic schedules share their parsing and diagnostics with cash-flow resolution,
        // schema validation and relational projection. Do not run another strict JSON-kind check
        // afterward: the shared contract owns supported row aliases and numeric-string coercion.
        if (field.Type == SecurityAssetTermFieldType.Array
            && SecurityEconomicScheduleParser.TryGetKind(field.Key, out var scheduleKind))
        {
            using var replacement = JsonDocument.Parse(newValue);
            var schedules = SecurityEconomicScheduleParser.ParseContainer(
                scheduleKind, replacement.RootElement, BuildCanonicalPath(field.Key, string.Empty));
            if (!schedules.IsValid)
            {
                error = string.Join(" ", schedules.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Path}: {diagnostic.Message}"));
                return false;
            }

            canonicalFieldPath = BuildCanonicalPath(field.Key, nestedPath);
            return true;
        }

        if (field.Type is SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array)
        {
            using var replacement = JsonDocument.Parse(newValue);
            if (!ContainerMembersMatchSchema(assetClass, field.Key, field, replacement.RootElement,
                    SecurityVersionedTerms.IsVersionedContainer(field), out error))
                return false;
        }

        canonicalFieldPath = BuildCanonicalPath(field.Key, nestedPath);
        return true;
    }

    private static bool TargetsVersionedContainer(string assetClass, SecurityAssetTermField field, string nestedPath)
    {
        if (SecurityVersionedTerms.IsVersionedContainer(field))
            return true;
        var firstMember = nestedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static part => !part.All(char.IsAsciiDigit));
        return firstMember is not null && SecurityAssetTermsSchema.ElementFields(assetClass, field.Key)
            .Any(member => SecurityVersionedTerms.IsVersionedContainer(member)
                && member.Key.Equals(firstMember, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainerMembersMatchSchema(string assetClass, string schemaPath, SecurityAssetTermField container,
        JsonElement value, bool exactKeys, out string? error)
    {
        error = null;
        if (value.ValueKind == JsonValueKind.Null)
            return true;
        var fields = SecurityAssetTermsSchema.ElementFields(assetClass, schemaPath);
        if (fields.Count == 0)
            return true;
        JsonElement[] rows = container.Type == SecurityAssetTermFieldType.Array ? value.EnumerateArray().ToArray() : [value];
        foreach (var row in rows)
        {
            if (SecurityVersionedTerms.IsVersionedContainer(container)
                && (container.Key is SecurityVersionedTerms.CallScheduleKey or SecurityVersionedTerms.PutScheduleKey)
                && (row.ValueKind == JsonValueKind.Null
                    || (row.ValueKind == JsonValueKind.Object && !SecurityAssetTermsSchema.IsPopulatedTermValue(row))))
                continue;
            if (row.ValueKind != JsonValueKind.Object)
            {
                error = $"Every item of '{schemaPath}' must be a JSON object.";
                return false;
            }
            foreach (var property in row.EnumerateObject())
            {
                var member = fields.FirstOrDefault(candidate => exactKeys
                    ? candidate.Key.Equals(property.Name, StringComparison.Ordinal)
                    : candidate.Key.Equals(property.Name, StringComparison.OrdinalIgnoreCase)
                      || candidate.Aliases.Any(alias => alias.Equals(property.Name, StringComparison.OrdinalIgnoreCase)));
                if (member is null)
                {
                    if (!exactKeys)
                        continue;
                    error = $"Unknown or case-variant member '{property.Name}' in '{schemaPath}'.";
                    return false;
                }
                if (member.SinceTermsVersion > SecurityAssetClassCatalog.GetTermsVersion(assetClass))
                    continue;
                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;
                var correctKind = member.Type switch
                {
                    SecurityAssetTermFieldType.String or SecurityAssetTermFieldType.Date or SecurityAssetTermFieldType.Guid => property.Value.ValueKind == JsonValueKind.String,
                    SecurityAssetTermFieldType.Decimal or SecurityAssetTermFieldType.Integer => property.Value.ValueKind == JsonValueKind.Number,
                    SecurityAssetTermFieldType.Boolean => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    SecurityAssetTermFieldType.Object => property.Value.ValueKind == JsonValueKind.Object,
                    SecurityAssetTermFieldType.Array => property.Value.ValueKind == JsonValueKind.Array,
                    _ => false
                };
                var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                if (!correctKind || !ValueCoercesToType(member.Type, text)
                    || (member.Type == SecurityAssetTermFieldType.String && (!member.Allows(text) || !member.SatisfiesConvention(text))))
                {
                    error = $"Member '{schemaPath}.{member.Key}' does not match its declared type or vocabulary.";
                    return false;
                }
                if (member.Type is SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array
                    && !ContainerMembersMatchSchema(assetClass, $"{schemaPath}.{member.Key}", member, property.Value,
                        exactKeys || SecurityVersionedTerms.IsVersionedContainer(member), out error))
                    return false;
            }
        }
        return true;
    }

    private static bool TryRefuseReadableOnlyEdit(
        string assetClass,
        SecurityAssetTermField field,
        string nestedPath,
        string? newValue,
        out string? error)
    {
        error = null;
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        if (field.SinceTermsVersion > writes)
        {
            error = SecurityTermsEnvelope.DescribeNotYetWritable(assetClass, field.Key, field.SinceTermsVersion);
            return true;
        }

        if (field.Type is not (SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array))
        {
            return false;
        }

        // An edit beneath the container: its first non-index segment names the element field.
        var segment = nestedPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static part => !part.All(char.IsAsciiDigit));
        if (segment is not null
            && SecurityAssetTermsSchema.ElementFields(assetClass, field.Key).FirstOrDefault(inner =>
                    string.Equals(inner.Key, segment, StringComparison.OrdinalIgnoreCase)
                    || inner.Aliases.Any(alias => string.Equals(alias, segment, StringComparison.OrdinalIgnoreCase)))
                is { } innerField
            && innerField.SinceTermsVersion > writes)
        {
            error = SecurityTermsEnvelope.DescribeNotYetWritable(assetClass, $"{field.Key}.{nestedPath}", innerField.SinceTermsVersion);
            return true;
        }

        // A value for the whole container, or for one of its elements (legs.0), that populates a
        // readable-only element field. An element value is checked as the only element of its array.
        if (segment is null && !string.IsNullOrWhiteSpace(newValue))
        {
            JsonElement value;
            try
            {
                using var document = JsonDocument.Parse(newValue);
                value = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return false;
            }

            var indexDepth = nestedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            for (var depth = 0; depth < indexDepth; depth++)
            {
                value = JsonSerializer.SerializeToElement(new[] { value });
            }

            var container = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [field.Key] = value });
            var populated = SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, container, writes);
            if (populated.Count > 0)
            {
                var path = indexDepth == 0 ? populated[0].Path : $"{field.Key}.{nestedPath}.{populated[0].Field.Key}";
                error = SecurityTermsEnvelope.DescribeNotYetWritable(assetClass, path, populated[0].Field.SinceTermsVersion);
                return true;
            }
        }

        return false;
    }

    private static string BuildCanonicalPath(string fieldKey, string nestedPath)
        => nestedPath.Length > 0
            ? $"{AssetSpecificTermsPrefix}{fieldKey}.{nestedPath}"
            : $"{AssetSpecificTermsPrefix}{fieldKey}";

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
