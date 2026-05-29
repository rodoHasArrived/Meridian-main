using System.Text.Json.Serialization;

namespace Meridian.Contracts.PrivateAssets;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PrivateAssetDto))]
[JsonSerializable(typeof(IReadOnlyList<PrivateAssetDto>))]
[JsonSerializable(typeof(PrivateAssetReadModelDto))]
[JsonSerializable(typeof(IReadOnlyList<PrivateAssetReadModelDto>))]
[JsonSerializable(typeof(PrivateAssetEvidenceLinkDto))]
[JsonSerializable(typeof(IReadOnlyList<PrivateAssetEvidenceLinkDto>))]
[JsonSerializable(typeof(PrivateAssetCommitmentScheduleEntryDto))]
[JsonSerializable(typeof(IReadOnlyList<PrivateAssetCommitmentScheduleEntryDto>))]
[JsonSerializable(typeof(PrivateAssetValidationWarningDto))]
[JsonSerializable(typeof(IReadOnlyList<PrivateAssetValidationWarningDto>))]
public sealed partial class PrivateAssetContractsJsonContext : JsonSerializerContext;
