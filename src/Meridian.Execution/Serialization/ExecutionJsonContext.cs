using System.Text.Json.Serialization;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution.Serialization;

/// <summary>
/// Source-generated JSON serialisation context for paper-session persistence types.
/// Conforms to ADR-014: no-reflection serialisation using <c>System.Text.Json</c> source generators.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PersistedSessionRecord))]
[JsonSerializable(typeof(ExecutionReport))]
[JsonSerializable(typeof(OrderState))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class ExecutionJsonContext : JsonSerializerContext
{
}
