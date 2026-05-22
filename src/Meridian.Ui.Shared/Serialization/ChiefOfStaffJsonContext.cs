using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(ChiefOfStaffStartRequestDto))]
[JsonSerializable(typeof(ChiefOfStaffSessionQueryDto))]
[JsonSerializable(typeof(ChiefOfStaffSessionSummaryDto))]
[JsonSerializable(typeof(IReadOnlyList<ChiefOfStaffSessionSummaryDto>))]
[JsonSerializable(typeof(ChiefOfStaffDecisionRequestDto))]
[JsonSerializable(typeof(ChiefOfStaffTraceExportRequestDto))]
[JsonSerializable(typeof(ChiefOfStaffActionCandidateDto))]
[JsonSerializable(typeof(IReadOnlyList<ChiefOfStaffActionCandidateDto>))]
[JsonSerializable(typeof(ChiefOfStaffRecommendationDto))]
[JsonSerializable(typeof(IReadOnlyList<ChiefOfStaffRecommendationDto>))]
[JsonSerializable(typeof(ChiefOfStaffRuntimeRequestDto))]
[JsonSerializable(typeof(ChiefOfStaffRuntimeResponseDto))]
[JsonSerializable(typeof(ChiefOfStaffRuntimeHealthDto))]
[JsonSerializable(typeof(ChiefOfStaffTraceSummaryDto))]
[JsonSerializable(typeof(ChiefOfStaffTraceEnvelopeDto))]
[JsonSerializable(typeof(ChiefOfStaffApprovalCommandDto))]
[JsonSerializable(typeof(ChiefOfStaffApprovalResultDto))]
[JsonSerializable(typeof(ChiefOfStaffEvidenceBundleDto))]
[JsonSerializable(typeof(ChiefOfStaffSessionDto))]
[JsonSerializable(typeof(IReadOnlyList<ChiefOfStaffSessionDto>))]
[JsonSerializable(typeof(ChiefOfStaffEvidenceExportDto))]
internal sealed partial class ChiefOfStaffJsonContext : JsonSerializerContext;
