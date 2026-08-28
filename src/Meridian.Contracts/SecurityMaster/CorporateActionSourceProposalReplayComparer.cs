using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Defines equality for replaying one provider event/version into the durable source inbox.
/// Observation time, locally assigned proposal identity, and the locally owned workflow disposition
/// are not source payload. Evidence, state-relevant consensus metadata, and economic/source-chain
/// content are authoritative and must not be silently replaced.
/// </summary>
public static class CorporateActionSourceProposalReplayComparer
{
    public static bool HasSameSourcePayload(
        CorporateActionSourceProposalDto existing,
        CorporateActionSourceProposalDto candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        return existing.SecurityId == candidate.SecurityId
               && existing.PayloadSchemaVersion == candidate.PayloadSchemaVersion
               && Sha256Digest.FixedEquals(existing.EconomicFingerprint, candidate.EconomicFingerprint)
               && string.Equals(
                   NormalizeLifecycle(existing.ProposedAction.LifecycleState),
                   NormalizeLifecycle(candidate.ProposedAction.LifecycleState),
                   StringComparison.Ordinal)
               && existing.ProposedAction.SupersedesCorpActId == candidate.ProposedAction.SupersedesCorpActId
               && existing.SupersedesProposalId == candidate.SupersedesProposalId
               && existing.ProviderIdentity.ReleaseStatus == candidate.ProviderIdentity.ReleaseStatus
               && string.Equals(
                   existing.ProviderIdentity.EvidenceHash,
                   candidate.ProviderIdentity.EvidenceHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   existing.ProviderIdentity.EvidenceReference,
                   candidate.ProviderIdentity.EvidenceReference,
                   StringComparison.Ordinal)
               && HasSameDisplayMetadata(existing.DisplayMetadata, candidate.DisplayMetadata);
    }

    private static string NormalizeLifecycle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? CorporateActionLifecycleStates.Confirmed : value.Trim();

    private static bool HasSameDisplayMetadata(
        CorporateActionSourceDisplayMetadataDto? existing,
        CorporateActionSourceDisplayMetadataDto? candidate) =>
        string.Equals(
            CanonicalizeDisplayMetadata(existing),
            CanonicalizeDisplayMetadata(candidate),
            StringComparison.Ordinal);

    private static string? CanonicalizeDisplayMetadata(
        CorporateActionSourceDisplayMetadataDto? metadata)
    {
        var normalized = NormalizeDisplayMetadata(metadata);
        return normalized is null
            ? null
            : CanonicalizeJson(JsonSerializer.SerializeToElement(normalized));
    }

    private static CorporateActionSourceDisplayMetadataDto? NormalizeDisplayMetadata(
        CorporateActionSourceDisplayMetadataDto? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return metadata with
        {
            Ticker = metadata.Ticker.Trim(),
            WinningSource = metadata.WinningSource.Trim(),
            AgreeingSources = NormalizeSources(metadata.AgreeingSources),
            DissentingSources = NormalizeSources(metadata.DissentingSources),
            DissentingFields = (metadata.DissentingFields ?? [])
                .Where(static field => !string.IsNullOrWhiteSpace(field.Field))
                .Select(static field => field with
                {
                    Field = field.Field.Trim(),
                    Candidates = field.Candidates
                        .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Source))
                        .Select(static candidate => candidate with
                        {
                            Source = candidate.Source.Trim(),
                            EvidenceReference = NormalizeOptional(candidate.EvidenceReference),
                        })
                        .OrderBy(static candidate => candidate.Source, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static candidate => candidate.EvidenceReference, StringComparer.Ordinal)
                        .ThenBy(static candidate => CanonicalizeJson(candidate.Value), StringComparer.Ordinal)
                        .ToArray(),
                })
                .OrderBy(static field => field.Field, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static IReadOnlyList<string> NormalizeSources(IReadOnlyList<string> sources) =>
        sources
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Select(static source => source.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string CanonicalizeJson(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonicalJson(writer, element);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(CanonicalizeNumber(element.GetRawText()), skipInputValidation: true);
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException(
                    $"Corporate-action display metadata contains unsupported JSON value kind '{element.ValueKind}'.");
        }
    }

    private static string CanonicalizeNumber(string raw)
    {
        // JSON permits one decimal value to have several spellings (for example 0.2600 and
        // 2.6e-1). Compare its exact coefficient/exponent rather than its lexical representation.
        var exponentIndex = raw.AsSpan().IndexOfAny('e', 'E');
        var mantissa = exponentIndex >= 0 ? raw[..exponentIndex] : raw;
        var exponent = exponentIndex >= 0
            ? BigInteger.Parse(raw[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            : BigInteger.Zero;
        var isNegative = mantissa.StartsWith("-", StringComparison.Ordinal);
        if (isNegative)
        {
            mantissa = mantissa[1..];
        }

        var decimalPointIndex = mantissa.IndexOf('.', StringComparison.Ordinal);
        if (decimalPointIndex >= 0)
        {
            exponent -= mantissa.Length - decimalPointIndex - 1;
            mantissa = mantissa.Remove(decimalPointIndex, 1);
        }

        var coefficient = mantissa.TrimStart('0');
        if (coefficient.Length == 0)
        {
            return "0";
        }

        var significantLength = coefficient.Length;
        while (significantLength > 1 && coefficient[significantLength - 1] == '0')
        {
            significantLength--;
            exponent++;
        }

        coefficient = coefficient[..significantLength];
        var sign = isNegative ? "-" : string.Empty;
        return $"{sign}{coefficient}e{exponent.ToString(CultureInfo.InvariantCulture)}";
    }
}
