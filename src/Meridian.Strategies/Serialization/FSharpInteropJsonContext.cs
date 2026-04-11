using System.Text.Json.Serialization;
using Meridian.FSharp.CashFlowInterop;
using Meridian.FSharp.Ledger;

namespace Meridian.Strategies.Serialization;

/// <summary>
/// Source-generated serializer metadata for F# interop DTOs to satisfy ADR-014.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CashFlowProjectionInput))]
[JsonSerializable(typeof(CashLadderBucketInterop))]
[JsonSerializable(typeof(CashLadderInterop))]
[JsonSerializable(typeof(ActualCashEventDto))]
[JsonSerializable(typeof(CashFlowStateDto))]
[JsonSerializable(typeof(ReconciliationOutcomeDto))]
[JsonSerializable(typeof(ReconciliationResultDto))]
internal sealed partial class FSharpInteropJsonContext : JsonSerializerContext;
