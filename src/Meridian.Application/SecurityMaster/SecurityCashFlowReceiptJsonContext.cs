using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

[JsonSourceGenerationOptions(Converters = new[] { typeof(SecurityCashFlowScheduleRowJsonConverter) })]
[JsonSerializable(typeof(SecurityCashFlowCalculationReceipt))]
[JsonSerializable(typeof(SecurityDetailDto))]
[JsonSerializable(typeof(SecurityCashFlowSourceDto))]
[JsonSerializable(typeof(SecurityForwardCurve))]
[JsonSerializable(typeof(StructuredCashFlowProjectionDto))]
[JsonSerializable(typeof(LegacyStructuredCashFlowScheduleEntry))]
[JsonSerializable(typeof(VersionOneStructuredCashFlowScheduleEntry))]
[JsonSerializable(typeof(StructuredCashFlowScheduleEntry))]
internal sealed partial class SecurityCashFlowReceiptJsonContext : JsonSerializerContext;

// Converter-free metadata is required for the current row's complete generated contract. Calling
// the receipt context from its row converter would recurse or freeze current rows to a field list.
[JsonSerializable(typeof(StructuredCashFlowScheduleEntry))]
internal sealed partial class CurrentSecurityCashFlowRowJsonContext : JsonSerializerContext;
