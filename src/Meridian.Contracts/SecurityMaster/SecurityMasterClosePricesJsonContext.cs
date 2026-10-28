using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Provides source-generated JSON metadata for retained close price snapshots and their approval evidence.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SecurityClosePriceSnapshotDto))]
[JsonSerializable(typeof(SecurityClosePriceApprovalDto))]
[JsonSerializable(typeof(SecurityPriceGoldenCopyDto))]
[JsonSerializable(typeof(SecurityClosePriceSetDto[]))]
[JsonSerializable(typeof(CreateSecurityClosePriceSetRequest))]
public sealed partial class SecurityMasterClosePricesJsonContext : JsonSerializerContext;
