using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SecurityClosePriceSnapshotDto))]
[JsonSerializable(typeof(SecurityClosePriceApprovalDto))]
[JsonSerializable(typeof(SecurityPriceGoldenCopyDto))]
public sealed partial class SecurityMasterClosePricesJsonContext : JsonSerializerContext;
