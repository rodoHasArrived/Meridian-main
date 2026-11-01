using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>The retained Security Master terms a preview was calculated from.</summary>
public sealed record SecurityTermsDigest(JsonElement CommonTerms, JsonElement AssetSpecificTerms);

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
