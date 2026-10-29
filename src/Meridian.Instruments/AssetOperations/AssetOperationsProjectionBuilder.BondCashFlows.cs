using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.AssetOperations;

public static partial class AssetOperationsProjectionBuilder
{
    /// <summary>Stable across JSONB property ordering and decimal formatting on durable reload.</summary>
    internal static string BondTermsContentHash(JsonElement payload)
    {
        using var content = new MemoryStream();
        using (var writer = new Utf8JsonWriter(content))
            Write(payload, writer);
        return Sha256Digest.Compute(content.ToArray());

        static void Write(JsonElement value, Utf8JsonWriter writer)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    Write(item, writer);
                writer.WriteEndArray();
            }
            else if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
            else
                value.WriteTo(writer);
        }
    }

    internal static Guid BondIdentity(Guid securityId, string key)
        => new(Sha256Digest.ComputeBytesUtf8($"meridian/bond-assets/v2/{securityId:N}/{key}").AsSpan(0, 16));

    internal static string BondProjectionInputKey(StructuredCashFlowProjectionDto? projection)
        => projection is null ? "legacy" : Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            projection.SourceKind,
            projection.SourceLastUpdatedUtc,
            projection.ScheduleEvidence,
            projection.InputManifest
        }));

    /// <summary>
    /// Adapts canonical projection amounts without reinterpreting rates, dates or principal. Dates
    /// not retained by the source (such as accrual boundaries) remain absent rather than invented.
    /// </summary>
    internal static IReadOnlyList<AssetProjectedCashFlowDto> MapCanonicalBondCashFlows(
        SecurityDetailDto security, StructuredCashFlowProjectionDto projection, Guid runId)
    {
        if (projection.BlockedReason is not null)
        {
            return [];
        }

        var result = new List<AssetProjectedCashFlowDto>();
        var ordinal = 0;
        foreach (var entry in projection.Schedule)
        {
            var date = DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime.Date);
            ordinal++;
            if (entry.InterestAmount != 0m || entry.RateEvidence is not null)
            {
                Add("Coupon", entry.InterestAmount);
            }
            if (entry.PrincipalAmount != 0m)
            {
                Add(date == projection.TermsUsed?.MaturityDate ? "Maturity" : "PrincipalRepayment", entry.PrincipalAmount);
            }

            void Add(string kind, decimal amount)
                => result.Add(new AssetProjectedCashFlowDto(
                    SchedulePeriodId.Derive(security.SecurityId,
                        entry.UnadjustedStart.HasValue ? $"canonical:{kind}" : $"canonical:{kind}:{ordinal}",
                        entry.UnadjustedStart ?? date, entry.UnadjustedEnd ?? date),
                    runId, security.SecurityId, result.Count + 1, kind, date, amount, security.Currency,
                    "Projected", entry.AccrualStart, entry.AccrualEnd,
                    PrincipalBasis: entry.RateEvidence?.Principal,
                    SourceDomain: "SecurityMaster", SourceEntityId: security.SecurityId.ToString("D"),
                    ExtensionPayload: JsonSerializer.SerializeToElement(new
                    {
                        engineVersion = entry.RateEvidence is null ? "asset-obligation-declared-v2" : "asset-obligation-declared-v3",
                        termsVersion = security.Version,
                        projection.SourceKind,
                        projection.Staleness,
                        projection.ScheduleEvidence,
                        entry.Factor,
                        rateEvidence = entry.RateEvidence,
                        inputManifest = projection.InputManifest
                    })));
        }
        return result;
    }
}
