using System.Text.Json.Serialization;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;

namespace Meridian.Ui.Shared.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(OrderPreviewResult))]
[JsonSerializable(typeof(OrderState))]
[JsonSerializable(typeof(JournalOrder))]
[JsonSerializable(typeof(IReadOnlyList<JournalOrder>))]
[JsonSerializable(typeof(JournalSummary))]
[JsonSerializable(typeof(ExecutionAlertSettings))]
[JsonSerializable(typeof(ExecutionNotification))]
[JsonSerializable(typeof(IReadOnlyList<ExecutionNotification>))]
[JsonSerializable(typeof(StrategyRunParityReport))]
internal sealed partial class ExecutionWorkflowJsonContext : JsonSerializerContext;
