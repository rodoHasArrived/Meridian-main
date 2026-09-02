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
            // A VALUE edit beneath a known contractual schedule cannot be row-validated in
            // isolation — principalSchedule.0.amount = -10 would stage an override the domain
            // rejects wholesale — so those subpaths only accept clears; assert values by
            // replacing the whole array, which runs the schedule invariants. Other nested paths
            // inside Object/Array fields stay dynamic pass-through, and a blank value clears the
            // overlay entry instead of asserting a typed one.
            if (nestedPath.Length > 0
                && !string.IsNullOrWhiteSpace(newValue)
                && (string.Equals(field.Key, "principalSchedule", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field.Key, "factorScheduleEntries", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field.Key, "stepSchedule", StringComparison.OrdinalIgnoreCase)))
            {
                error =
                    $"'{field.Key}' rows carry schedule-wide domain invariants that a nested edit cannot " +
                    "be validated against; replace the whole array instead.";
                return false;
            }

            canonicalFieldPath = BuildCanonicalPath(field.Key, nestedPath);
            return true;
        }

        if (!ValueCoercesToType(field.Type, newValue))
        {
            error =
                $"Value '{newValue}' does not parse as the declared type {field.Type} for " +
                $"'{AssetSpecificTermsPrefix}{field.Key}' on asset class '{assetClass}'.";
            return false;
        }

        // A syntactically valid string is not enough for a field the codec reads through a closed
        // switch with no raw-carrying case: an undeclared value does not fail downstream, it is
        // silently READ AS a different value and re-serialized under that name, so staging it queues
        // a publish that quietly rewrites the record's economics. This validator stages the
        // operator's value verbatim, so the match must be exact-case — accepting "floating" here
        // would persist the very spelling the bond codec collapses to "Fixed".
        if (field.Type == SecurityAssetTermFieldType.String
            && !SecurityAssetTermsSchema.IsAllowedValue(assetClass, field.Key, newValue))
        {
            var declared = string.Join(", ", SecurityAssetTermsSchema.AllowedValues(assetClass, field.Key));
            error =
                $"Value '{newValue}' is not a declared value for '{AssetSpecificTermsPrefix}{field.Key}' on asset " +
                $"class '{assetClass}'. Declared values are {declared}, matched case-sensitively.";
            return false;
        }

        // A syntactically valid array is not enough for the KNOWN contractual schedules: their
        // rows carry the same domain invariants the canonical F# command enforces (positive
        // instalments, factors within [0, 1], unique non-increasing factor dates), and staging a
        // row set the domain would reject puts a draft and provenance row behind a contract that
        // can never persist canonically.
        if (field.Type == SecurityAssetTermFieldType.Array
            && !ScheduleRowsSatisfyDomainInvariants(field.Key, newValue, out var scheduleError))
        {
            error = scheduleError;
            return false;
        }

        canonicalFieldPath = BuildCanonicalPath(field.Key, nestedPath);
        return true;
    }

    private static bool ScheduleRowsSatisfyDomainInvariants(string fieldKey, string newValue, out string? error)
    {
        error = null;
        if (string.Equals(fieldKey, "principalSchedule", StringComparison.OrdinalIgnoreCase))
        {
            return PrincipalScheduleRowsAreValid(newValue, out error);
        }

        if (string.Equals(fieldKey, "factorScheduleEntries", StringComparison.OrdinalIgnoreCase))
        {
            return FactorScheduleRowsAreValid(newValue, out error);
        }

        if (string.Equals(fieldKey, "stepSchedule", StringComparison.OrdinalIgnoreCase))
        {
            return StepScheduleRowsAreValid(newValue, out error);
        }

        return true;
    }

    /// <summary>
    /// Mirrors the canonical F# Bond step-coupon rules a whole-schedule replacement must satisfy
    /// row-locally: each row is an object with a parseable effective date and a non-negative rate,
    /// and effective dates are unique. The date-versus-maturity window stays at the canonical amend
    /// seam, which re-validates the published overlay against the record.
    /// </summary>
    private static bool StepScheduleRowsAreValid(string newValue, out string? error)
    {
        error = null;
        var dates = new List<DateOnly>();
        using var document = JsonDocument.Parse(newValue);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !TryReadRowDate(row, out var effectiveDate, "effectiveDate", "stepDate", "date")
                || !TryReadRowDecimal(row, out var rate, "rate", "couponRate"))
            {
                error = "Each stepSchedule row must be an object with a parseable 'effectiveDate' and numeric 'rate'.";
                return false;
            }

            if (rate < 0m)
            {
                error = "stepSchedule rates must be zero or greater — negative step coupons are rejected by the canonical Bond contract.";
                return false;
            }

            dates.Add(effectiveDate);
        }

        if (dates.Distinct().Count() != dates.Count)
        {
            error = "stepSchedule effective dates must be unique — two rates on one date make the payable coupon depend on input ordering.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Mirrors the canonical F# Bond rules a whole-schedule replacement must satisfy row-locally:
    /// each row is an object with a parseable payment date and a strictly positive amount.
    /// Record-contextual rules (dates within issue/maturity, the schedule-versus-par cap) stay at
    /// the canonical amend seam, which re-validates the published overlay.
    /// </summary>
    private static bool PrincipalScheduleRowsAreValid(string newValue, out string? error)
    {
        error = null;
        using var document = JsonDocument.Parse(newValue);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !TryReadRowDate(row, out _, "paymentDate")
                || !TryReadRowDecimal(row, out var amount, "amount"))
            {
                error = "Each principalSchedule row must be an object with a parseable 'paymentDate' and numeric 'amount'.";
                return false;
            }

            if (amount <= 0m)
            {
                error = "principalSchedule amounts must be greater than zero — non-positive instalments are rejected by the canonical Bond contract.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Mirrors the canonical F# StructuredCredit factor-schedule rules: every factor within
    /// [0, 1], unique as-of dates, and non-increasing factors in date order — the same invariants
    /// that make the effective outstanding principal well defined.
    /// </summary>
    private static bool FactorScheduleRowsAreValid(string newValue, out string? error)
    {
        error = null;
        var entries = new List<(DateOnly AsOfDate, decimal Factor)>();
        using var document = JsonDocument.Parse(newValue);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !TryReadRowDate(row, out var asOfDate, "asOfDate", "factorDate", "effectiveDate", "date")
                || !TryReadRowDecimal(row, out var factor, "factor", "currentFactor"))
            {
                error = "Each factorScheduleEntries row must be an object with a parseable as-of date and numeric factor.";
                return false;
            }

            if (factor < 0m || factor > 1m)
            {
                error = "factorScheduleEntries factors must be between zero and one — factors above one project cash flows exceeding the original principal.";
                return false;
            }

            entries.Add((asOfDate, factor));
        }

        if (entries.Select(static entry => entry.AsOfDate).Distinct().Count() != entries.Count)
        {
            error = "factorScheduleEntries dates must be unique — two factors on the same date make the outstanding principal depend on input ordering.";
            return false;
        }

        var ordered = entries.OrderBy(static entry => entry.AsOfDate).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].Factor > ordered[i - 1].Factor)
            {
                error = "factorScheduleEntries must be non-increasing in date order — a rising factor would grow outstanding principal.";
                return false;
            }
        }

        return true;
    }

    private static bool TryReadRowDate(JsonElement row, out DateOnly value, params string[] propertyNames)
    {
        value = default;
        foreach (var property in row.EnumerateObject())
        {
            foreach (var name in propertyNames)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && DateOnly.TryParse(property.Value.GetString(), CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryReadRowDecimal(JsonElement row, out decimal value, params string[] propertyNames)
    {
        value = default;
        foreach (var property in row.EnumerateObject())
        {
            foreach (var name in propertyNames)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetDecimal(out value))
                {
                    return true;
                }
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
