namespace Meridian.Contracts.SecurityMaster;

/// <summary>Observed reference-record usage of one exact profile version; never holdings or exposure.</summary>
public sealed record SecurityAssetProfileUsageDto(
    string? ProfileId, int? Version, long ActiveRecordCount, long InactiveRecordCount,
    long UnknownStatusRecordCount, string? UnresolvedReason = null)
{
    /// <summary>Required-field presence among active reference records; not type or economic validation.</summary>
    public long? CompleteActiveRecordCount { get; init; }
    public long? IncompleteActiveRecordCount { get; init; }
    public long? UnknownActiveRecordCount { get; init; }
    public IReadOnlyDictionary<string, long>? MissingRequiredFieldCounts { get; init; }
}

public sealed record SecurityAssetProfilePromotionAssessmentDto(
    string ProfileId, int Version, string Name, string ProfileFingerprint,
    SecurityAssetProfileUsageDto Usage, SecurityAssetProfilePromotionCandidateDto? DefinitionAssessment);

public sealed record SecurityAssetProfilePromotionAssessmentsDto(
    string Scope, DateTimeOffset ObservedAtUtc,
    IReadOnlyList<SecurityAssetProfilePromotionAssessmentDto> Profiles,
    IReadOnlyList<SecurityAssetProfileUsageDto> UnresolvedUsage)
{
    public bool CanExport { get; init; }
}

public sealed record SecurityAssetProfileCandidateExportRequestDto(string ExpectedProfileFingerprint);

/// <summary>A developer-review artifact. Nothing here registers a class, grants approval or supplies defaults.</summary>
public sealed record SecurityAssetProfileCandidateExportDto(
    string ArtifactKind, string Scope, DateTimeOffset ExportedAtUtc, string ExportedBy,
    string ProfileFingerprint, SecurityAssetProfileDefinitionDto SourceProfile,
    SecurityAssetProfileUsageDto Usage, SecurityAssetPackDescriptor Candidate,
    AssetPackRegistryValidationResult DescriptorValidation,
    AssetPackRegistryValidationResult CandidateSetValidation,
    IReadOnlyList<string> DeveloperReviewTasks);
