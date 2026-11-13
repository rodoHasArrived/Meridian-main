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
/// hashed without their top-level envelope keys (<see cref="SecurityTermsEnvelope.ReservedKeys"/>):
/// those describe the payload, not the security, so declaring a format on a stored row (ADR-024
/// amendment 2026-11-13) leaves the hash of the same economic terms unchanged. Every other member
/// keeps its stored order and bytes.
/// </summary>
public sealed record SecurityTermsDigest(JsonElement CommonTerms, JsonElement AssetSpecificTerms)
{
    /// <summary>The asset-specific terms as hashed: the supplied terms without top-level envelope keys.</summary>
    public JsonElement AssetSpecificTerms { get; init; } = WithoutEnvelopeKeys(AssetSpecificTerms);

    private static JsonElement WithoutEnvelopeKeys(JsonElement terms)
    {
        if (terms.ValueKind != JsonValueKind.Object
            || !terms.EnumerateObject().Any(property => SecurityTermsEnvelope.IsReservedKey(property.Name)))
        {
            return terms;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in terms.EnumerateObject())
            {
                if (!SecurityTermsEnvelope.IsReservedKey(property.Name))
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
