using System.Text.Json.Serialization;
using Meridian.Infrastructure.Contracts;
using Meridian.Ui.Shared.Contracts;

namespace Meridian.Ui.Shared.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for covered-call endpoint payloads.
/// Per ADR-014, endpoint serialisation must use a source-generated context.
/// </summary>
[ImplementsAdr("ADR-014", "Source-generated JSON context for covered-call backtest endpoint types")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CoveredCallBacktestRequest))]
[JsonSerializable(typeof(CoveredCallRunHandle))]
[JsonSerializable(typeof(CoveredCallRunStatusDto))]
[JsonSerializable(typeof(CoveredCallRunResult))]
[JsonSerializable(typeof(CoveredCallRunSummary))]
[JsonSerializable(typeof(IReadOnlyList<CoveredCallRunSummary>))]
[JsonSerializable(typeof(CoveredCallChainPreviewRequest))]
[JsonSerializable(typeof(CoveredCallChainPreview))]
[JsonSerializable(typeof(CoveredCallProblemDetails))]
internal sealed partial class CoveredCallJsonContext : JsonSerializerContext;
