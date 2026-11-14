using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.CashFlows;

/// <summary>Exactly the members the engine hashes as a run's input identity, in the retained property order.</summary>
public sealed record ContractualCashFlowInputDigest(
    ContractualCashFlowContract Contract,
    ContractualCashFlowRequest Request,
    IReadOnlyList<CashFlowCalendarSnapshot> CalendarEvidence,
    IReadOnlyList<CashFlowCurveSnapshot> CurveEvidence,
    string SourceVersion,
    string SourceTermsHash);

/// <summary>Exactly the members the engine hashes as a run's output identity, in the retained property order.</summary>
public sealed record ContractualCashFlowOutputDigest(
    IReadOnlyList<ContractualCashFlowEvent> Events,
    IReadOnlyList<CashFlowExerciseEligibility> Eligibility,
    IReadOnlyList<CashFlowExerciseCalculation> Calculations,
    IReadOnlyList<CashFlowCalculationBlocker> Blockers);

/// <summary>
/// The retained Security Master terms a preview was calculated from. The asset-specific terms are
/// hashed without their top-level <see cref="SecurityTermsEnvelope.FormatKey"/> member: the declared
/// payload family is the only envelope key a write or the current-state backfill adds to the same
/// economic terms (ADR-024 amendment 2026-11-13), and it only states the family the shape rule
/// already infers, so declaring it never changes what a preview decodes. Every other member,
/// including <c>schemaVersion</c> and <see cref="SecurityTermsEnvelope.AssetClassTermsVersionKey"/>
/// (which selects the versioned terms the preview decodes), keeps its stored order and bytes and
/// stays in the hash.
/// </summary>
public sealed record SecurityTermsDigest(JsonElement CommonTerms, JsonElement AssetSpecificTerms)
{
    /// <summary>The asset-specific terms as hashed: the supplied terms without a top-level <c>termsFormat</c>.</summary>
    public JsonElement AssetSpecificTerms { get; init; } = WithoutFormatKey(AssetSpecificTerms);

    private static bool IsFormatKey(JsonProperty property)
        => property.NameEquals(SecurityTermsEnvelope.FormatKey);

    private static JsonElement WithoutFormatKey(JsonElement terms)
    {
        if (terms.ValueKind != JsonValueKind.Object || !terms.EnumerateObject().Any(IsFormatKey))
        {
            return terms;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in terms.EnumerateObject())
            {
                if (!IsFormatKey(property))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}

/// <summary>A blocked preview's identity: its code and reason.</summary>
public sealed record CalculationBlockerDigest(string Code, string Reason);

/// <summary>
/// Source-generated serialization for every contractual cash-flow shape that is hashed or retained.
/// Hash bytes are a compatibility contract with retained packets, so the options here reproduce the
/// web defaults the packets were written with (camelCase names, case-insensitive reads).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ContractualCashFlowContract))]
[JsonSerializable(typeof(ContractualCashFlowRequest))]
[JsonSerializable(typeof(ContractualCashFlowResult))]
[JsonSerializable(typeof(ContractualCashFlowEvidence))]
[JsonSerializable(typeof(ContractualCashFlowRun))]
[JsonSerializable(typeof(ContractualCashFlowInputDigest))]
[JsonSerializable(typeof(ContractualCashFlowOutputDigest))]
[JsonSerializable(typeof(SecurityTermsDigest))]
[JsonSerializable(typeof(CalculationBlockerDigest))]
public sealed partial class ContractualCashFlowJsonContext : JsonSerializerContext;
