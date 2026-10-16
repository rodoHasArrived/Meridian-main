using System.Globalization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Client-side form rules for schema-driven term editors, shared by the desktop workstation and
/// mirrored by the browser's passport editor. The governed field-edit validator on the server stays
/// authoritative; these rules only stop obviously invalid input before it is staged.
/// </summary>
public static class SecurityAssetTermFormModel
{
    /// <summary>The scalar-editable fields an operator can pick, in registry order.</summary>
    public static IReadOnlyList<SecurityAssetTermFieldDefinitionDto> PickableFields(SecurityAssetClassDefinitionDto? definition)
        => definition is null
            ? []
            : definition.Fields.Where(static field => field.ScalarEditable).OrderBy(static field => field.Order).ToArray();

    /// <summary>The closed set of values a field offers as a choice list, or empty for free input.</summary>
    public static IReadOnlyList<string> ValueChoices(SecurityAssetTermFieldDefinitionDto? field)
        => field?.Editor switch
        {
            SecurityAssetTermEditorKind.Choice or SecurityAssetTermEditorKind.Convention => field.Choices,
            SecurityAssetTermEditorKind.Toggle => ["true", "false"],
            _ => []
        };

    /// <summary>A validation message for <paramref name="value"/>, or null when it may be staged.</summary>
    public static string? Validate(SecurityAssetTermFieldDefinitionDto? field, string? value)
    {
        if (field is null)
        {
            return null;
        }

        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return field.Required ? $"{field.Label} is required." : null;
        }

        return field.Editor switch
        {
            SecurityAssetTermEditorKind.Decimal when !decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                => $"{field.Label} must be a number.",
            SecurityAssetTermEditorKind.Integer when !long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                => $"{field.Label} must be a whole number.",
            SecurityAssetTermEditorKind.Date when !DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                => $"{field.Label} must be a date (YYYY-MM-DD).",
            SecurityAssetTermEditorKind.Toggle when trimmed is not ("true" or "false")
                => $"{field.Label} must be true or false.",
            SecurityAssetTermEditorKind.Choice when !field.Choices.Contains(trimmed, StringComparer.Ordinal)
                => $"{field.Label} must be one of: {string.Join(", ", field.Choices)}.",
            _ => null
        };
    }
}
