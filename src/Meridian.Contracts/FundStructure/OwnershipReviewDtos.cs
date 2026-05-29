using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<OwnershipReviewValidationStateDto>))]
public enum OwnershipReviewValidationStateDto
{
    Valid,
    Warning,
    Blocking,
    Inactive
}

public sealed record OwnershipReviewQueryDto(
    bool ActiveOnly = false,
    DateTimeOffset? AsOf = null);

public sealed record OwnershipReviewEffectiveWindowDto(
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActiveAsOf,
    string DisplayLabel);

public sealed record OwnershipReviewLinkDto(
    Guid OwnershipLinkId,
    Guid ParentNodeId,
    Guid ChildNodeId,
    string NodeDisplayLabel,
    string ParentLabel,
    string ChildLabel,
    OwnershipRelationshipTypeDto RelationshipType,
    string RelationshipLabel,
    decimal? Percent,
    string PercentLabel,
    bool IsPrimary,
    OwnershipReviewEffectiveWindowDto EffectiveWindow,
    OwnershipReviewValidationStateDto ValidationState,
    IReadOnlyList<string> BlockingMessages,
    IReadOnlyList<string> SuggestedRemediationActions,
    string? Notes = null);

public sealed record OwnershipReviewSummaryDto(
    int TotalLinkCount,
    int ActiveLinkCount,
    int InvalidLinkCount,
    int BlockingLinkCount,
    int PrimaryLinkCount,
    decimal? PrimaryOwnershipPercentTotal,
    DateTimeOffset AsOf,
    string StatusLabel);

public sealed record OwnershipReviewDto(
    DateTimeOffset AsOf,
    IReadOnlyList<OwnershipReviewLinkDto> Links,
    OwnershipReviewSummaryDto Summary,
    IReadOnlyList<string> EmptyStateMessages);
