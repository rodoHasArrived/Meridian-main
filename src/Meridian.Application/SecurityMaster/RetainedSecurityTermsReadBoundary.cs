using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Classifies pre-format terms only at explicit retained calculation and report input boundaries.
/// Original verified documents and digests stay unchanged; live Security Master reads never use this seam.
/// </summary>
internal static class RetainedSecurityTermsReadBoundary
{
    internal static JsonElement ReadTerms(string assetClass, JsonElement retainedTerms)
    {
        if (retainedTerms.ValueKind == JsonValueKind.Object)
        {
            var declarations = retainedTerms.EnumerateObject()
                .Where(static member => member.NameEquals(SecurityTermsEnvelope.FormatKey)).ToArray();
            if (declarations.Length > 1 || declarations.Length == 1 && declarations[0].Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Retained calculation terms have an ambiguous or malformed termsFormat declaration.");
        }
        var readable = SecurityTermsLegacyEventClassifier.StampDeclaredFormat(retainedTerms);
        try
        {
            _ = SecurityTermsEnvelope.ResolveFormat(readable);
            SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, readable);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("Retained calculation terms cannot be read: " + exception.Message, exception);
        }
        return readable;
    }
}
