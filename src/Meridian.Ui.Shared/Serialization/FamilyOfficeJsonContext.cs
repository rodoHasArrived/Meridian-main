using System.Text.Json.Serialization;
using Meridian.Infrastructure.Contracts;
using Meridian.Ui.Shared.Contracts;

namespace Meridian.Ui.Shared.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for family-office workstation contracts.
/// Per ADR-014, endpoint serialization must use generated metadata for these payloads.
/// </summary>
[ImplementsAdr("ADR-014", "Source-generated JSON context for family-office workstation contracts")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FamilyOfficeOverviewDto))]
[JsonSerializable(typeof(FamilyOfficeEndpointStateDto))]
[JsonSerializable(typeof(FamilyOfficeBalanceSheetResponseDto))]
[JsonSerializable(typeof(FamilyOfficeEntitiesResponseDto))]
[JsonSerializable(typeof(FamilyOfficeOwnershipGraphResponseDto))]
[JsonSerializable(typeof(FamilyBalanceSheetDto))]
[JsonSerializable(typeof(FamilyEntityDto))]
[JsonSerializable(typeof(IReadOnlyList<FamilyEntityDto>))]
[JsonSerializable(typeof(FamilyOwnershipGraphDto))]
[JsonSerializable(typeof(FamilyOwnershipNodeDto))]
[JsonSerializable(typeof(IReadOnlyList<FamilyOwnershipNodeDto>))]
[JsonSerializable(typeof(FamilyOwnershipEdgeDto))]
[JsonSerializable(typeof(IReadOnlyList<FamilyOwnershipEdgeDto>))]
[JsonSerializable(typeof(FamilyAccountSummaryDto))]
[JsonSerializable(typeof(IReadOnlyList<FamilyAccountSummaryDto>))]
[JsonSerializable(typeof(FamilyAssetSummaryDto))]
[JsonSerializable(typeof(IReadOnlyList<FamilyAssetSummaryDto>))]
[JsonSerializable(typeof(PrivateAssetSummaryDto))]
[JsonSerializable(typeof(IReadOnlyList<PrivateAssetSummaryDto>))]
[JsonSerializable(typeof(CapitalCommitmentDto))]
[JsonSerializable(typeof(IReadOnlyList<CapitalCommitmentDto>))]
[JsonSerializable(typeof(CapitalActivityDto))]
[JsonSerializable(typeof(IReadOnlyList<CapitalActivityDto>))]
[JsonSerializable(typeof(FamilyOfficeReadinessDto))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(FamilyOfficeEvidenceLinkDto))]
[JsonSerializable(typeof(IReadOnlyList<FamilyOfficeEvidenceLinkDto>))]
internal sealed partial class FamilyOfficeJsonContext : JsonSerializerContext;
