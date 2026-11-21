using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// Canonical wire representation used by certification, persistence validation, release
/// revalidation, and audit hashing. A new reporting parameter snapshot has one byte representation
/// regardless of the persistence adapter.
/// </summary>
public static class ReportingCanonicalParameterSerializer
{
    public static string Serialize(
        ReportingRunParametersDto parameters,
        bool requiresCertifiedLedgerPresentation = false) =>
        SerializeCore(parameters, requiresCertifiedLedgerPresentation, canonicalizeExternalGl: true);

    /// <summary>
    /// Matches the current canonical bytes or the exact former raw-dictionary serialization of
    /// the same DTO. Callers must still verify the stored hash against the original frozen bytes.
    /// </summary>
    public static bool MatchesRetained(
        string? retainedCanonicalJson,
        ReportingRunParametersDto parameters,
        bool requiresCertifiedLedgerPresentation = false)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return retainedCanonicalJson is not null
            && (string.Equals(retainedCanonicalJson,
                    Serialize(parameters, requiresCertifiedLedgerPresentation), StringComparison.Ordinal)
                || string.Equals(retainedCanonicalJson,
                    SerializeCore(parameters, requiresCertifiedLedgerPresentation, canonicalizeExternalGl: false),
                    StringComparison.Ordinal));
    }

    private static string SerializeCore(
        ReportingRunParametersDto parameters,
        bool requiresCertifiedLedgerPresentation,
        bool canonicalizeExternalGl)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("scope");
            writer.WriteString("fundProfileId", parameters.Scope.FundProfileId);
            writer.WriteString("entityScopeKind", parameters.Scope.EntityScopeKind.ToString());
            WriteOptional(writer, "entityId", parameters.Scope.EntityId);
            WriteOptional(writer, "portfolioId", parameters.Scope.PortfolioId);
            WriteOptional(writer, "investorId", parameters.Scope.InvestorId);
            writer.WritePropertyName("dimensions");
            var dimensions = parameters.Scope.Dimensions;
            if (canonicalizeExternalGl && dimensions?.ExternalGlDimensions is { Count: > 1 } externalGlDimensions)
            {
                // Certification already orders this map. Reapply that same byte order after
                // persistence adapters such as jsonb reorder object properties on read-back.
                dimensions = dimensions with
                {
                    ExternalGlDimensions = externalGlDimensions
                        .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
                };
            }
            JsonSerializer.Serialize(writer, dimensions);
            writer.WriteEndObject();
            writer.WriteString("periodId", parameters.PeriodId);
            writer.WriteString(
                "asOfDate",
                parameters.AsOfDate.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString(
                "ledgerBookId",
                parameters.LedgerBook.LedgerBookId?.ToString("D"));
            WriteOptional(writer, "ledgerBookCode", parameters.LedgerBook.LedgerBookCode);
            writer.WriteString("accountingBasis", parameters.AccountingBasis.ToString());
            writer.WriteString("presentationCurrency", parameters.PresentationCurrency);
            writer.WriteString("consolidationLevel", parameters.ConsolidationLevel.ToString());
            writer.WriteString("outputFormat", parameters.OutputFormat.ToString());
            writer.WriteString("finality", parameters.Finality.ToString());
            if (requiresCertifiedLedgerPresentation)
            {
                writer.WriteBoolean("requiresCertifiedLedgerPresentation", true);
            }

            writer.WriteBoolean(
                "includeSupportingSchedules",
                parameters.IncludeSupportingSchedules);
            writer.WriteBoolean(
                "includeEvidenceAppendix",
                parameters.IncludeEvidenceAppendix);
            writer.WriteStartObject("templateParameters");
            foreach (var pair in parameters.TemplateParameters
                         .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string ComputeHash(
        ReportingRunParametersDto parameters,
        bool requiresCertifiedLedgerPresentation = false) =>
        Sha256Digest.ComputeUtf8(Serialize(parameters, requiresCertifiedLedgerPresentation));

    private static void WriteOptional(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }
}
