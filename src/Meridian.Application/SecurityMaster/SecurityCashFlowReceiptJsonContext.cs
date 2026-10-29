using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

[JsonSerializable(typeof(SecurityCashFlowCalculationReceipt))]
[JsonSerializable(typeof(SecurityDetailDto))]
[JsonSerializable(typeof(SecurityCashFlowSourceDto))]
[JsonSerializable(typeof(SecurityForwardCurve))]
[JsonSerializable(typeof(StructuredCashFlowProjectionDto))]
internal sealed partial class SecurityCashFlowReceiptJsonContext : JsonSerializerContext;
