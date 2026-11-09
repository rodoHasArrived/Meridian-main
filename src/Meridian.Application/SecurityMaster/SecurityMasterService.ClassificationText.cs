using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Keeps newly written classification text unambiguous for <see cref="ClassificationLabelReader"/>.
/// </summary>
/// <remarks>
/// Before 2026-11-01 stored labels were the F# union rendering (<c>Some(...)</c>, <c>None</c>,
/// <c>OtherSubType "x"</c>); since then the writer stores free text verbatim. The reader decodes
/// stored labels by their text, so a new free-text value that itself looks like an old rendering
/// (for example <c>None</c> or <c>Some(Equity)</c>) would be misread. Such values are refused on
/// create and amend. Only <c>OtherSecurity</c> takes caller-supplied classification text
/// (<c>subType</c>, falling back to <c>category</c>); every other free-text label is a catalog
/// constant. Replaying retained events is unaffected: the check runs only on new writes.
/// </remarks>
public sealed partial class SecurityMasterService
{
    private static readonly string[] FreeTextClassificationTerms = ["subType", "category"];

    private static void EnsureClassificationTextIsUnambiguous(string? assetClass, JsonElement? assetSpecificTerms)
    {
        if (!string.Equals(SecurityAssetClassCatalog.Canonicalize(assetClass ?? string.Empty), "OtherSecurity", StringComparison.OrdinalIgnoreCase)
            || assetSpecificTerms is not { ValueKind: JsonValueKind.Object } terms)
        {
            return;
        }

        foreach (var property in terms.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String
                || !FreeTextClassificationTerms.Any(key => string.Equals(key, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var value = property.Value.GetString();
            if (ClassificationLabelReader.IsAmbiguousFreeText(value))
            {
                throw new SecurityMasterWriteRefusedException(
                    $"OtherSecurity {property.Name} '{value}' looks like a retired F# classification rendering "
                    + $"and would be read back as '{ClassificationLabelReader.Normalize(value) ?? "(no label)"}'. "
                    + "Choose a classification label that is not 'None', 'Some(...)', a quoted string or "
                    + "'OtherFamily \"...\"' / 'OtherSubType \"...\"'.");
            }
        }
    }
}
