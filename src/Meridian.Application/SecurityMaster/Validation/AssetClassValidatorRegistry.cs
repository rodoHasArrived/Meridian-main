using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

public sealed record SecurityValidationContext(
    SecurityProjectionRecord Record,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Defines validation rules for one supported Security Master asset class.
/// </summary>
public interface ISecurityAssetClassValidator
{
    string AssetClass { get; }
    IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context);
}

/// <summary>
/// Indexes asset-class validators and resolves the validator for a requested asset class.
/// </summary>
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
        => CreateDefault(StaticSecurityAssetProfileCatalog.CreateDefault());

    /// <summary>
    /// The default registry: every class's hand-written semantic rules composed with the rules
    /// generated from its term schema (<see cref="SchemaTermsAssetClassValidator"/>).
    /// </summary>
    public static AssetClassValidatorRegistry CreateDefault(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new(SchemaBackedAssetClassValidator.WrapAll(DefaultAssetClassValidators.Create(assetProfileCatalog)));
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
    public static IReadOnlyList<ISecurityAssetClassValidator> Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => SecurityAssetValidationModules.Compose(SecurityAssetValidationModules.All, assetProfileCatalog);
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

    /// <summary>The hand-written rules, exposed for declared-fact parity tests.</summary>
    internal IReadOnlyList<ISecurityValidationRule> Rules => _rules;

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

internal sealed class CompositeAssetClassValidator : ISecurityAssetClassValidator
{
    private readonly IReadOnlyList<ISecurityAssetClassValidator> _validators;

    public CompositeAssetClassValidator(string assetClass, IReadOnlyList<ISecurityAssetClassValidator> validators)
    {
        AssetClass = assetClass;
        _validators = validators;
    }

    public string AssetClass { get; }

    /// <summary>The composed validators, exposed for declared-fact parity tests.</summary>
    internal IReadOnlyList<ISecurityAssetClassValidator> Validators => _validators;

    public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context)
    {
        var issues = new List<SecurityValidationIssueDto>();
        foreach (var validator in _validators)
        {
            issues.AddRange(validator.Validate(context));
        }

        return issues;
    }
}

internal sealed class SecurityAssetProfileAssetClassValidator : ISecurityAssetClassValidator
{
    private static readonly HashSet<string> ReservedFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "assetClass",
        "customProfileId",
        "profileVersion",
        "profileFields",
        "profileApproval",
        "schemaVersion",
        SecurityTermsEnvelope.FormatKey,
        SecurityTermsEnvelope.AssetClassTermsVersionKey,
        SecurityTermsEnvelope.RetainedEconomicTermsKey,
        "securityId",
        "status",
        "version",
        "provenance"
    };

    private readonly ISecurityAssetProfileCatalog _assetProfileCatalog;
    private readonly bool _requireProfileReference;
    private readonly bool _enforceWriteTimeGovernance;

    public SecurityAssetProfileAssetClassValidator(
        string assetClass,
        ISecurityAssetProfileCatalog assetProfileCatalog,
        bool requireProfileReference,
        bool enforceWriteTimeGovernance = false)
    {
        AssetClass = assetClass;
        _assetProfileCatalog = assetProfileCatalog;
        _requireProfileReference = requireProfileReference;
        _enforceWriteTimeGovernance = enforceWriteTimeGovernance;
    }

    public string AssetClass { get; }

    public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context)
    {
        var issues = new List<SecurityValidationIssueDto>();
        var assetSpecificTerms = context.Record.AssetSpecificTerms;
        var hasProfileId = JsonValidationReader.TryGetString(assetSpecificTerms, "customProfileId", out var profileId);
        if (!hasProfileId)
        {
            if (_requireProfileReference)
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_ID_REQUIRED",
                    "Custom asset profile is missing",
                    "CustomAsset records must reference the approved custom asset profile used to interpret their typed fields.",
                    ["assetSpecificTerms.customProfileId"],
                    "Select an approved asset profile before creating or amending the custom asset."));
            }

            return issues;
        }

        if (!JsonValidationReader.TryGetInt(assetSpecificTerms, "profileVersion", out var profileVersion) || profileVersion <= 0)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_VERSION_REQUIRED",
                "Custom asset profile version is missing",
                "Profile-backed securities must pin the approved profile version used at creation so historical records remain interpretable after profile changes.",
                ["assetSpecificTerms.profileVersion"],
                "Persist the approved profile version with the Security Master terms."));
            return issues;
        }

        if (!_assetProfileCatalog.TryGetProfile(profileId, profileVersion, out var profile))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_VERSION_UNKNOWN",
                "Custom asset profile version is unknown",
                $"Profile '{profileId}' version '{profileVersion}' is not registered in the approved asset profile catalog.",
                ["assetSpecificTerms.customProfileId", "assetSpecificTerms.profileVersion"],
                "Choose an approved profile version or migrate the record through a governed profile rollback/amendment."));
            return issues;
        }

        issues.AddRange(ValidateProfileDefinition(profile));
        if (profile.Status is not SecurityAssetProfileStatusDto.Approved and not SecurityAssetProfileStatusDto.Superseded)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_NOT_APPROVED",
                "Custom asset profile is not approved",
                $"Profile '{profile.Name}' version '{profile.Version}' is '{profile.Status}' and cannot back governed Security Master records.",
                ["assetSpecificTerms.customProfileId", "assetSpecificTerms.profileVersion"],
                "Route the profile version through approval before using it for Security Master create or amend workflows."));
        }

        // Status alone is not enough at WRITE time: an Approved version may not be effective yet,
        // and a Superseded version carries the window in which it WAS the governing definition. A
        // write evaluated outside [EffectiveFrom, EffectiveTo] pins a version that never governed
        // the record's effective date — a current write to an expired version, or a backdated
        // write to one not yet in force. Read-path validation deliberately skips this: a record
        // legitimately keeps its pinned (now-superseded) version so history stays interpretable.
        if (_enforceWriteTimeGovernance)
        {
            var evaluatedDate = DateOnly.FromDateTime(context.EvaluatedAtUtc.UtcDateTime.Date);
            if (evaluatedDate < profile.EffectiveFrom
                || (profile.EffectiveTo is DateOnly effectiveTo && evaluatedDate > effectiveTo))
            {
                issues.Add(SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_VERSION_NOT_EFFECTIVE",
                    "Custom asset profile version is outside its effective window",
                    $"Profile '{profile.Name}' version '{profile.Version}' is effective " +
                    $"{profile.EffectiveFrom:yyyy-MM-dd}–{profile.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open"}, " +
                    $"which does not cover the evaluated date {evaluatedDate:yyyy-MM-dd}.",
                    ["assetSpecificTerms.customProfileId", "assetSpecificTerms.profileVersion"],
                    "Pin the profile version whose effective window covers the write's effective date."));
            }
        }

        if (!JsonValidationReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields)
            || profileFields.ValueKind != JsonValueKind.Object)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELDS_REQUIRED",
                "Custom asset profile fields are missing",
                "Profile-backed securities must store typed field values under assetSpecificTerms.profileFields.",
                ["assetSpecificTerms.profileFields"],
                "Populate profileFields using the selected approved profile definition."));
            return issues;
        }

        foreach (var field in profile.Fields.OrderBy(static field => field.Key, StringComparer.OrdinalIgnoreCase))
        {
            issues.AddRange(ValidateField(field, profileFields));
        }

        foreach (var rule in profile.DateOrderRules)
        {
            issues.AddRange(ValidateDateOrderRule(rule, profileFields));
        }

        issues.AddRange(ValidateIdentifierCoverage(profile, context.Record, context.EvaluatedAtUtc));
        issues.AddRange(ValidateProfileApprovalMetadata(assetSpecificTerms, profile, _enforceWriteTimeGovernance));

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateProfileDefinition(SecurityAssetProfileDefinitionDto profile)
    {
        var issues = new List<SecurityValidationIssueDto>();
        var duplicateKey = profile.Fields
            .GroupBy(static field => field.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateKey is not null)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_KEY_DUPLICATE",
                "Custom asset profile has duplicate field keys",
                $"Profile '{profile.Name}' repeats field key '{duplicateKey.Key}'.",
                ["assetProfile.fields"],
                "Publish a corrected profile version with unique field keys."));
        }

        var reservedKey = profile.Fields.FirstOrDefault(field => ReservedFieldKeys.Contains(field.Key));
        if (reservedKey is not null)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_KEY_RESERVED",
                "Custom asset profile uses a reserved field key",
                $"Profile '{profile.Name}' uses reserved field key '{reservedKey.Key}'.",
                ["assetProfile.fields"],
                "Rename the field in a new profile version before using the profile for Security Master records."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateField(
        SecurityAssetProfileFieldDefinitionDto field,
        JsonElement profileFields)
    {
        var fieldPath = $"assetSpecificTerms.profileFields.{field.Key}";
        if (!JsonValidationReader.TryGetProperty(profileFields, field.Key, out var value))
        {
            return field.IsRequired
                ?
                [
                    SecurityValidationIssueFactory.Create(
                        SecurityValidationSeverityDto.Error,
                        "SM_CUSTOM_PROFILE_FIELD_REQUIRED",
                        "Custom asset profile field is missing",
                        $"Required profile field '{field.Label}' is missing.",
                        [fieldPath],
                        "Populate the required profile field before the security is used for close, reconciliation, or report-pack evidence.")
                ]
                : [];
        }

        var issues = new List<SecurityValidationIssueDto>();
        var isValidType = field.FieldType switch
        {
            SecurityAssetProfileFieldTypeDto.Text => value.ValueKind == JsonValueKind.String && (!field.IsRequired || !string.IsNullOrWhiteSpace(value.GetString())),
            SecurityAssetProfileFieldTypeDto.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetProfileFieldTypeDto.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SecurityAssetProfileFieldTypeDto.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetProfileFieldTypeDto.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            SecurityAssetProfileFieldTypeDto.Enum => JsonValidationReader.HasAllowedString(value, field.AllowedValues),
            SecurityAssetProfileFieldTypeDto.CurrencyCode => IsCurrencyCode(value),
            SecurityAssetProfileFieldTypeDto.SecurityLink => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid) && guid != Guid.Empty,
            _ => true
        };

        if (!isValidType)
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_TYPE_INVALID",
                "Custom asset profile field type is invalid",
                $"Profile field '{field.Label}' must be a valid {field.FieldType} value.",
                [fieldPath],
                "Correct the typed profile field value before the security is used downstream."));
            return issues;
        }

        if (field.FieldType is SecurityAssetProfileFieldTypeDto.Decimal or SecurityAssetProfileFieldTypeDto.Integer
            && value.TryGetDecimal(out var numericValue)
            && ((field.MinValue.HasValue && numericValue < field.MinValue.Value)
                || (field.MaxValue.HasValue && numericValue > field.MaxValue.Value)))
        {
            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_FIELD_RANGE_INVALID",
                "Custom asset profile field is outside the allowed range",
                $"Profile field '{field.Label}' must be between {field.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"} and {field.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"}.",
                [fieldPath],
                "Correct the profile field value or approve a new profile version with the intended range."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateDateOrderRule(
        SecurityAssetProfileDateOrderRuleDto rule,
        JsonElement profileFields)
    {
        if (!JsonValidationReader.TryGetDate(profileFields, rule.StartFieldKey, out var start)
            || !JsonValidationReader.TryGetDate(profileFields, rule.EndFieldKey, out var end)
            || start <= end)
        {
            return [];
        }

        return
        [
            SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                rule.Code,
                "Custom asset profile date order is invalid",
                rule.Message,
                [$"assetSpecificTerms.profileFields.{rule.StartFieldKey}", $"assetSpecificTerms.profileFields.{rule.EndFieldKey}"],
                "Correct the date ordering or approve a new profile version with the intended lifecycle rule.")
        ];
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateIdentifierCoverage(
        SecurityAssetProfileDefinitionDto profile,
        SecurityProjectionRecord record,
        DateTimeOffset evaluatedAtUtc)
    {
        var activeKinds = record.Identifiers
            .Where(identifier => identifier.ValidFrom <= evaluatedAtUtc && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > evaluatedAtUtc))
            .Select(static identifier => identifier.Kind)
            .ToHashSet();

        var issues = new List<SecurityValidationIssueDto>();
        foreach (var preference in profile.IdentifierPreferences.Where(static preference => preference.IsRequiredForClose))
        {
            if (activeKinds.Contains(preference.Kind))
            {
                continue;
            }

            issues.Add(SecurityValidationIssueFactory.Create(
                SecurityValidationSeverityDto.Error,
                "SM_CUSTOM_PROFILE_IDENTIFIER_COVERAGE_MISSING",
                "Custom asset identifier coverage is incomplete",
                $"Profile '{profile.Name}' requires active {preference.Kind} coverage. {preference.Reason}",
                [$"identifiers.{preference.Kind}"],
                "Attach the required identifier before close, reconciliation, or report-pack readiness consumes this security."));
        }

        return issues;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> ValidateProfileApprovalMetadata(
        JsonElement assetSpecificTerms,
        SecurityAssetProfileDefinitionDto profile,
        bool enforceWriteTimeGovernance)
    {
        if (!(JsonValidationReader.TryGetProperty(assetSpecificTerms, "profileApproval", out var approval)
            && approval.ValueKind == JsonValueKind.Object
            && JsonValidationReader.TryGetString(approval, "approvedBy", out var approvedBy)
            && JsonValidationReader.TryGetDateTimeOffset(approval, "approvedAtUtc", out var approvedAtUtc)
            && JsonValidationReader.TryGetString(approval, "approvalReference", out var approvalReference)))
        {
            return
            [
                SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_APPROVAL_METADATA_REQUIRED",
                    "Custom asset profile approval metadata is missing",
                    "Profile-backed securities must retain immutable profile approval metadata with the pinned profile version.",
                    ["assetSpecificTerms.profileApproval"],
                    "Persist profileApproval.approvedBy, approvedAtUtc, and approvalReference from the governed profile approval event.")
            ];
        }

        // At WRITE time the approval metadata is evidence, not free text: it persists as the
        // record's immutable audit trail, so values contradicting the governed catalog's approval
        // facts would corrupt that trail from the first write. Read-path validation skips the
        // comparison — a historical record's metadata reflects the approval event as it stood
        // when written, which a later catalog re-approval must not retroactively invalidate.
        // The approval reference is verifiable only when the catalog carries one (definitions
        // predating the ApprovalReference field have no fact to compare, and free-text acceptance
        // is exactly what this check exists to prevent — not a reason to fail legacy profiles).
        var referenceContradictsCatalog = profile.ApprovalReference is { } catalogReference
            && !string.IsNullOrWhiteSpace(catalogReference)
            && !string.Equals(approvalReference.Trim(), catalogReference.Trim(), StringComparison.OrdinalIgnoreCase);
        if (enforceWriteTimeGovernance
            && (!string.Equals(approvedBy.Trim(), profile.ApprovedBy.Trim(), StringComparison.OrdinalIgnoreCase)
                || approvedAtUtc != profile.ApprovedAtUtc
                || referenceContradictsCatalog))
        {
            return
            [
                SecurityValidationIssueFactory.Create(
                    SecurityValidationSeverityDto.Error,
                    "SM_CUSTOM_PROFILE_APPROVAL_METADATA_MISMATCH",
                    "Custom asset profile approval metadata contradicts the governed catalog",
                    $"profileApproval names approvedBy '{approvedBy}' at '{approvedAtUtc:O}' (reference '{approvalReference}'), " +
                    $"but profile '{profile.Name}' version '{profile.Version}' was approved by '{profile.ApprovedBy}' at " +
                    $"'{profile.ApprovedAtUtc:O}'{(profile.ApprovalReference is null ? string.Empty : $" (reference '{profile.ApprovalReference}')")}.",
                    ["assetSpecificTerms.profileApproval"],
                    "Copy the approval metadata from the governed profile approval event instead of supplying free text.")
            ];
        }

        return [];
    }

    private static bool IsCurrencyCode(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var currency = value.GetString();
        return currency is { Length: 3 }
               && currency.All(static character => character is >= 'A' and <= 'Z');
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
    /// <summary>An array that must contain an item when supplied; presence comes from the schema.</summary>
    public static FieldRule OptionalArrayNotEmpty(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalArrayNotEmpty, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule OptionalPositive(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalPositiveNumber, SecurityValidationSeverityDto.Error, code, title, message);

    public static FieldRule OptionalNonNegative(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalNonNegativeNumber, SecurityValidationSeverityDto.Error, code, title, message);

    /// <summary>A string that, when supplied, must be one of <paramref name="allowedValues"/>; absent or null passes.</summary>
    public static FieldRule OptionalAllowedString(string fieldPath, IReadOnlyList<string> allowedValues, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalAllowedString, SecurityValidationSeverityDto.Error, code, title, message, allowedValues);

    /// <summary>A number that, when supplied, must lie in [0, 1]; absent or null passes.</summary>
    public static FieldRule OptionalFraction(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalFraction, SecurityValidationSeverityDto.Error, code, title, message);

    /// <summary>A value that, when supplied, must parse as an ISO-8601 date; absent or null passes.</summary>
    public static FieldRule OptionalDate(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalDate, SecurityValidationSeverityDto.Error, code, title, message);

    /// <summary>A value that, when supplied, must be a non-blank JSON string; absent or null passes.</summary>
    public static FieldRule OptionalString(string fieldPath, string code, string title, string message)
        => new(fieldPath, SecurityFieldRuleKind.OptionalString, SecurityValidationSeverityDto.Error, code, title, message);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var field = $"assetSpecificTerms.{FieldPath}";
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var property))
        {
            return null;
        }

        // The canonical serializer writes null where a domain value is None, so for every optional
        // rule kind a JSON null reads as "not supplied"; only a present, non-null value is governed.
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var isValid = RuleKind switch
        {
            SecurityFieldRuleKind.OptionalArrayNotEmpty => property.ValueKind == JsonValueKind.Array && property.GetArrayLength() > 0,
            SecurityFieldRuleKind.OptionalPositiveNumber => JsonValidationReader.TryGetDecimal(property, out var value) && value > 0m,
            SecurityFieldRuleKind.OptionalNonNegativeNumber => JsonValidationReader.TryGetDecimal(property, out var value) && value >= 0m,
            SecurityFieldRuleKind.OptionalAllowedString => JsonValidationReader.HasAllowedString(property, AllowedValues ?? Array.Empty<string>()),
            SecurityFieldRuleKind.OptionalFraction => JsonValidationReader.TryGetDecimal(property, out var value) && value >= 0m && value <= 1m,
            SecurityFieldRuleKind.OptionalDate => JsonValidationReader.HasDate(property),
            SecurityFieldRuleKind.OptionalString => JsonValidationReader.HasNonBlankString(property),
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

}

internal sealed record DateOrderRule(
    string StartFieldPath,
    string EndFieldPath,
    string Code,
    string Title,
    string Message,
    bool Strict = false) : ISecurityValidationRule
{
    public static DateOrderRule Optional(string startFieldPath, string endFieldPath, string code, string title, string message)
        => new(startFieldPath, endFieldPath, code, title, message);

    /// <summary>Like <see cref="Optional"/>, but the start must fall strictly before the end.</summary>
    public static DateOrderRule OptionalStrict(string startFieldPath, string endFieldPath, string code, string title, string message)
        => new(startFieldPath, endFieldPath, code, title, message, Strict: true);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var hasStart = JsonValidationReader.TryGetAssetTermDate(context.Record.AssetSpecificTerms, StartFieldPath, context.Record.AssetClass, out var start);
        var hasEnd = JsonValidationReader.TryGetAssetTermDate(context.Record.AssetSpecificTerms, EndFieldPath, context.Record.AssetClass, out var end);
        if (!hasStart || !hasEnd)
        {
            return null;
        }

        return (Strict ? start < end : start <= end) ? null : Issue();
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

/// <summary>
/// A number that is required only when a discriminant names one of <c>DiscriminantValues</c>
/// (case-insensitive, matching the F# command validator), e.g. the cap strike of a Cap or Collar.
/// Any other discriminant value, or none, leaves the field to its own optional rules.
/// </summary>
internal sealed record RequiredNumberWhenRule(
    string DiscriminantPath,
    IReadOnlyList<string> DiscriminantValues,
    string FieldPath,
    string Code,
    string Title,
    string Message) : ISecurityValidationRule
{
    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, DiscriminantPath, context.Record.AssetClass, out var discriminant)
            || !JsonValidationReader.HasAllowedString(discriminant, DiscriminantValues))
        {
            return null;
        }

        if (JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var value)
            && JsonValidationReader.TryGetDecimal(value, out _))
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            SecurityValidationSeverityDto.Error,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{DiscriminantPath}", $"assetSpecificTerms.{FieldPath}"],
            "Supply the field the selected type requires and resubmit the Security Master amendment.");
    }
}

/// <summary>
/// Cross-field numeric ordering: the start field must be at or below the end field. Mirrors
/// <see cref="DateOrderRule"/> for numbers; a missing or null side is "not supplied" and the
/// schema presence rules govern each value on its own.
/// </summary>
internal sealed record NumericOrderRule(
    string LowerFieldPath,
    string UpperFieldPath,
    string Code,
    string Title,
    string Message) : ISecurityValidationRule
{
    public static NumericOrderRule Optional(string lowerFieldPath, string upperFieldPath, string code, string title, string message)
        => new(lowerFieldPath, upperFieldPath, code, title, message);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var hasLower = JsonValidationReader.TryGetAssetTermDecimal(context.Record.AssetSpecificTerms, LowerFieldPath, context.Record.AssetClass, out var lower);
        var hasUpper = JsonValidationReader.TryGetAssetTermDecimal(context.Record.AssetSpecificTerms, UpperFieldPath, context.Record.AssetClass, out var upper);
        if (!hasLower || !hasUpper)
        {
            return null;
        }

        return lower <= upper ? null : Issue();
    }

    private SecurityValidationIssueDto Issue()
        => SecurityValidationIssueFactory.Create(
            SecurityValidationSeverityDto.Error,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{LowerFieldPath}", $"assetSpecificTerms.{UpperFieldPath}"],
            "Correct the numeric ordering and resubmit the Security Master amendment.");
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
        if (!JsonValidationReader.TryGetAssetTermString(context.Record.AssetSpecificTerms, LeftFieldPath, context.Record.AssetClass, out var left)
            || !JsonValidationReader.TryGetAssetTermString(context.Record.AssetSpecificTerms, RightFieldPath, context.Record.AssetClass, out var right))
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

/// <summary>
/// ADR-022 canonical-home rule: flags a string term whose value routes the instrument into a
/// non-canonical modeling home (e.g. a Bond subclass naming a securitized product whose canonical
/// home is StructuredCredit). Absent or unlisted values pass — this rule partitions homes, it does
/// not require the field.
/// </summary>
internal sealed record DisallowedStringValuesRule(
    string FieldPath,
    IReadOnlyList<string> DisallowedValues,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string MessageFormat,
    string SuggestedAction) : ISecurityValidationRule
{
    public static DisallowedStringValuesRule Create(
        string fieldPath,
        IReadOnlyList<string> disallowedValues,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string messageFormat,
        string suggestedAction)
        => new(fieldPath, disallowedValues, severity, code, title, messageFormat, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermString(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var value))
        {
            return null;
        }

        var disallowed = DisallowedValues.FirstOrDefault(
            candidate => SecurityCanonicalAssetHomes.NormalizeLabel(candidate) == SecurityCanonicalAssetHomes.NormalizeLabel(value));
        if (disallowed is null)
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            string.Format(CultureInfo.InvariantCulture, MessageFormat, value),
            [$"assetSpecificTerms.{FieldPath}"],
            SuggestedAction);
    }
}

/// <summary>
/// ADR-022 canonical-home rule: flags a boolean term whose <c>true</c> value indicates the record
/// belongs in a different canonical asset class (e.g. an InvestmentFund flagged stable-NAV, whose
/// canonical home is MoneyMarketFund). Absent or <c>false</c> values pass.
/// </summary>
/// <summary>
/// A free-text pointer field that, when supplied, must be backed by a non-empty typed array: the
/// pointer is evidence lineage, the array is what computation reads. Absent or null pointer passes.
/// </summary>
internal sealed record TypedScheduleBackingRule(
    string PointerFieldPath,
    string ScheduleFieldPath,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    string SuggestedAction) : ISecurityValidationRule
{
    public static TypedScheduleBackingRule Create(
        string pointerFieldPath,
        string scheduleFieldPath,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        string suggestedAction)
        => new(pointerFieldPath, scheduleFieldPath, severity, code, title, message, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, PointerFieldPath, context.Record.AssetClass, out var pointer)
            || !JsonValidationReader.HasNonBlankString(pointer))
        {
            return null;
        }

        if (JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, ScheduleFieldPath, context.Record.AssetClass, out var schedule)
            && schedule.ValueKind == JsonValueKind.Array
            && schedule.GetArrayLength() > 0)
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{PointerFieldPath}", $"assetSpecificTerms.{ScheduleFieldPath}"],
            SuggestedAction);
    }
}

internal sealed record DiscouragedTrueBooleanRule(
    string FieldPath,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    string SuggestedAction) : ISecurityValidationRule
{
    public static DiscouragedTrueBooleanRule Create(
        string fieldPath,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        string suggestedAction)
        => new(fieldPath, severity, code, title, message, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, FieldPath, context.Record.AssetClass, out var property)
            || property.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{FieldPath}"],
            SuggestedAction);
    }
}

/// <summary>
/// Reports a supplied term whose meaning depends on another term that is missing, such as a spread
/// with no index to be quoted over.
/// </summary>
internal sealed record DependentTermRule(
    string TriggerFieldPath,
    string RequiredFieldPath,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    string SuggestedAction) : ISecurityValidationRule
{
    public static DependentTermRule Create(
        string triggerFieldPath,
        string requiredFieldPath,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        string suggestedAction)
        => new(triggerFieldPath, requiredFieldPath, severity, code, title, message, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var terms = context.Record.AssetSpecificTerms;
        var assetClass = context.Record.AssetClass;
        if (!JsonValidationReader.TryGetAssetTermProperty(terms, TriggerFieldPath, assetClass, out var trigger)
            || trigger.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (JsonValidationReader.TryGetAssetTermProperty(terms, RequiredFieldPath, assetClass, out var required)
            && JsonValidationReader.HasNonBlankString(required))
        {
            return null;
        }

        return SecurityValidationIssueFactory.Create(
            Severity,
            Code,
            Title,
            Message,
            [$"assetSpecificTerms.{TriggerFieldPath}", $"assetSpecificTerms.{RequiredFieldPath}"],
            SuggestedAction);
    }
}

/// <summary>
/// One element of a term array, read through the schema so element aliases resolve the same way the
/// schema validator resolves them.
/// </summary>
internal readonly record struct TermArrayElement(JsonElement Value, JsonElement Terms, JsonElement CommonTerms, string AssetClass, string SchemaPath)
{
    public bool TryGet(string key, out JsonElement property)
        => JsonValidationReader.TryGetElementTermProperty(Value, AssetClass, SchemaPath, key, out property)
           && property.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    public bool HasNonBlankString(string key)
        => TryGet(key, out var property) && JsonValidationReader.HasNonBlankString(property);

    public bool HasString(string key, string expected)
        => TryGet(key, out var property)
           && property.ValueKind == JsonValueKind.String
           && string.Equals(property.GetString()?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    public bool HasNumber(string key)
        => TryGet(key, out var property) && property.ValueKind == JsonValueKind.Number;

    public bool TryGetDecimal(string key, out decimal value)
    {
        value = default;
        return TryGet(key, out var property) && JsonValidationReader.TryGetDecimal(property, out value);
    }

    public bool TryGetDate(string key, out DateOnly value)
    {
        value = default;
        return TryGet(key, out var property) && JsonValidationReader.TryGetDate(property, out value);
    }

    /// <summary>
    /// Whether the record itself (asset terms or common terms) declares a value for a cash-flow term
    /// role, read with the same spellings the cash-flow resolver uses for its record-level fallback.
    /// </summary>
    public bool RecordDeclaresString(SecurityCashFlowTermRole role)
        => SecurityTermReader.ReadString([Terms, CommonTerms], SecurityCashFlowTermRoles.Spellings(AssetClass, role)) is { Length: > 0 } value
           && !string.IsNullOrWhiteSpace(value);

    /// <summary>A date on the record itself (not the element), such as the instrument's maturity.</summary>
    public bool TryGetRecordDate(string path, out DateOnly value)
        => JsonValidationReader.TryGetAssetTermDate(Terms, path, AssetClass, out value);

    /// <summary>Whether a nested schedule's dates are strictly increasing; an absent schedule passes.</summary>
    public bool ScheduleDatesIncrease(string scheduleKey, string dateKey)
        => !TryGet(scheduleKey, out var schedule)
           || JsonValidationReader.FirstOutOfOrderDateIndex(schedule, AssetClass, $"{SchemaPath}.{scheduleKey}", dateKey) < 0;
}

/// <summary>
/// Applies a check to each element of a term array and reports every failing element in one issue.
/// The check returns the element-relative field at fault, or <see langword="null"/> when the element
/// passes. Element shape and types are the schema validator's job; checks skip what they cannot read.
/// </summary>
internal sealed record TermArrayElementRule(
    string ArrayFieldPath,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    string SuggestedAction,
    Func<TermArrayElement, string?> FindViolation) : ISecurityValidationRule
{
    public static TermArrayElementRule Create(
        string arrayFieldPath,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        string suggestedAction,
        Func<TermArrayElement, string?> findViolation)
        => new(arrayFieldPath, severity, code, title, message, suggestedAction, findViolation);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        var terms = context.Record.AssetSpecificTerms;
        var assetClass = context.Record.AssetClass;
        if (!JsonValidationReader.TryGetAssetTermProperty(terms, ArrayFieldPath, assetClass, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var affected = new List<string>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && FindViolation(new TermArrayElement(item, terms, context.Record.CommonTerms, assetClass, ArrayFieldPath)) is { } field)
            {
                affected.Add($"assetSpecificTerms.{ArrayFieldPath}.{index}.{field}");
            }

            index++;
        }

        return affected.Count == 0
            ? null
            : SecurityValidationIssueFactory.Create(Severity, Code, Title, Message, affected, SuggestedAction);
    }
}

/// <summary>
/// Requires the dates of a top-level term schedule to be strictly increasing. Unparseable dates are
/// skipped here; the schema validator reports them.
/// </summary>
internal sealed record TermScheduleDateOrderRule(
    string ScheduleFieldPath,
    string DateKey,
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    string SuggestedAction) : ISecurityValidationRule
{
    public static TermScheduleDateOrderRule Create(
        string scheduleFieldPath,
        string dateKey,
        SecurityValidationSeverityDto severity,
        string code,
        string title,
        string message,
        string suggestedAction)
        => new(scheduleFieldPath, dateKey, severity, code, title, message, suggestedAction);

    public SecurityValidationIssueDto? Validate(SecurityValidationContext context)
    {
        if (!JsonValidationReader.TryGetAssetTermProperty(context.Record.AssetSpecificTerms, ScheduleFieldPath, context.Record.AssetClass, out var schedule))
        {
            return null;
        }

        var index = JsonValidationReader.FirstOutOfOrderDateIndex(schedule, context.Record.AssetClass, ScheduleFieldPath, DateKey);
        return index < 0
            ? null
            : SecurityValidationIssueFactory.Create(
                Severity,
                Code,
                Title,
                Message,
                [$"assetSpecificTerms.{ScheduleFieldPath}.{index}.{DateKey}"],
                SuggestedAction);
    }
}

internal enum SecurityFieldRuleKind
{
    OptionalArrayNotEmpty,
    OptionalPositiveNumber,
    OptionalNonNegativeNumber,
    OptionalAllowedString,
    OptionalFraction,
    OptionalDate,
    OptionalString
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

    public static bool TryGetAssetTermProperty(JsonElement root, string path, string assetClass, out JsonElement property)
    {
        if (TryGetSchemaProperty(root, path, assetClass, out property))
        {
            return true;
        }

        if (SupportsProfileBackedTerms(assetClass)
            && TryGetProperty(root, "profileFields", out var profileFields)
            && TryGetSchemaProperty(profileFields, path, assetClass, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool TryGetSchemaProperty(JsonElement root, string path, string assetClass, out JsonElement property)
    {
        property = default;
        var current = root;
        var parentPath = "";
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var field = parentPath.Length == 0
                ? SecurityAssetTermsSchema.Field(assetClass, segment)
                : SecurityAssetTermsSchema.ElementFields(assetClass, parentPath)
                    .FirstOrDefault(candidate => candidate.Key.Equals(segment, StringComparison.OrdinalIgnoreCase));
            var found = current.TryGetProperty(segment, out var value);
            if (!found && field is not null)
            {
                foreach (var alias in field.Aliases)
                {
                    if (current.TryGetProperty(alias, out value))
                    {
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
            {
                return false;
            }
            current = value;
            parentPath = parentPath.Length == 0 ? segment : $"{parentPath}.{segment}";
        }
        property = current;
        return true;
    }

    private static bool SupportsProfileBackedTerms(string assetClass)
        => Meridian.Contracts.SecurityMaster.SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms;

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

    public static bool TryGetAssetTermString(JsonElement root, string path, string assetClass, out string value)
    {
        value = string.Empty;
        if (!TryGetAssetTermProperty(root, path, assetClass, out var property) || property.ValueKind != JsonValueKind.String)
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

    /// <summary>
    /// Reads a field of one array element by its schema key or any declared alias of it.
    /// </summary>
    public static bool TryGetElementTermProperty(JsonElement element, string assetClass, string elementSchemaPath, string key, out JsonElement property)
    {
        property = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(key, out property))
        {
            return true;
        }

        var field = SecurityAssetTermsSchema.ElementFields(assetClass, elementSchemaPath)
            .FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (field is not null)
        {
            foreach (var alias in field.Aliases)
            {
                if (element.TryGetProperty(alias, out property))
                {
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    public static bool TryGetDate(JsonElement property, out DateOnly value)
    {
        value = default;
        return property.ValueKind == JsonValueKind.String
               && DateOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>
    /// The index of the first schedule element whose date is not after the previous readable date,
    /// or -1 when the dates are strictly increasing (or the value is not an array).
    /// </summary>
    public static int FirstOutOfOrderDateIndex(JsonElement schedule, string assetClass, string elementSchemaPath, string dateKey)
    {
        if (schedule.ValueKind != JsonValueKind.Array)
        {
            return -1;
        }

        DateOnly? previous = null;
        var index = 0;
        foreach (var item in schedule.EnumerateArray())
        {
            if (TryGetElementTermProperty(item, assetClass, elementSchemaPath, dateKey, out var property)
                && TryGetDate(property, out var date))
            {
                if (previous is { } prior && date <= prior)
                {
                    return index;
                }

                previous = date;
            }

            index++;
        }

        return -1;
    }

    public static bool TryGetDecimal(JsonElement property, out decimal value)
    {
        value = default;
        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value);
    }

    public static bool TryGetInt(JsonElement root, string path, out int value)
    {
        value = default;
        if (!TryGetProperty(root, path, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
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

    public static bool TryGetAssetTermDecimal(JsonElement root, string path, string assetClass, out decimal value)
    {
        if (TryGetAssetTermProperty(root, path, assetClass, out var property) && TryGetDecimal(property, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGetAssetTermDate(JsonElement root, string path, string assetClass, out DateOnly value)
    {
        value = default;
        if (!TryGetAssetTermProperty(root, path, assetClass, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return DateOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    public static bool TryGetDateTimeOffset(JsonElement root, string path, out DateTimeOffset value)
    {
        value = default;
        if (!TryGetProperty(root, path, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }
}
