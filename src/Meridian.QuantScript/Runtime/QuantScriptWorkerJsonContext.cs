using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Runtime;

/// <summary>
/// Closed serialization contract for the worker boundary. Adding a protocol payload requires an
/// explicit source-generated root here; the runtime never falls back to reflection metadata.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    IncludeFields = true,
    PropertyNameCaseInsensitive = false,
    MaxDepth = 64,
    Converters = new[] { typeof(ReadOnlyLedgerJsonConverter), typeof(ReadOnlyStringSetJsonConverter) })]
[JsonSerializable(typeof(WorkerEnvelope))]
[JsonSerializable(typeof(WorkerExecutionRequest))]
[JsonSerializable(typeof(WorkerExecutionResponse))]
[JsonSerializable(typeof(WorkerFatalError))]
[JsonSerializable(typeof(WorkerDataRequest))]
[JsonSerializable(typeof(WorkerDataResponse))]
[JsonSerializable(typeof(WorkerPriceSeries))]
[JsonSerializable(typeof(WorkerOrderBook))]
[JsonSerializable(typeof(WorkerScriptRunResult))]
[JsonSerializable(typeof(IReadOnlyList<ScriptTrade>))]
[JsonSerializable(typeof(List<ScriptTrade>))]
[JsonSerializable(typeof(SecurityDetailDto))]
[JsonSerializable(typeof(IReadOnlyList<CorporateActionDto>))]
[JsonSerializable(typeof(List<CorporateActionDto>))]
[JsonSerializable(typeof(List<JournalEntry>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class QuantScriptWorkerJsonContext : JsonSerializerContext;
