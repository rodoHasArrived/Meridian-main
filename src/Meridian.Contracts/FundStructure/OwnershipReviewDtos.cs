using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<OwnershipReviewValidationStateDto>))]
public enum OwnershipReviewValidationStateDto
{
    Valid,
    Warning,
    Blocking
}

public sealed record OwnershipReviewEffectiveWindowDto(
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActiveAsOf,
    string DisplayLabel);

public sealed record OwnershipLifecycleCommandDto(
    string Label,
    string Endpoint,
    bool IsEnabled,
    string DisabledReason);

public sealed record OwnershipReviewLinkDto(
    Guid OwnershipLinkId,
    string NodeDisplayLabel,
    string ParentLabel,
    string ChildLabel,
    string RelationshipLabel,
    decimal? Percent,
    bool IsPrimary,
    OwnershipReviewEffectiveWindowDto EffectiveWindow,
    OwnershipReviewValidationStateDto ValidationState,
    IReadOnlyList<string> BlockingMessages,
    IReadOnlyList<string> SuggestedRemediationActions,
    IReadOnlyList<OwnershipLifecycleCommandDto> LifecycleCommands);

public sealed record OwnershipReviewSummaryDto(
    int TotalLinkCount,
    int ActiveLinkCount,
    int InvalidLinkCount,
    decimal ExplicitOwnershipPercentTotal,
    string RollupSummary,
    IReadOnlyList<string> BlockingMessages);

public sealed record OwnershipReviewModelDto(
    DateTimeOffset AsOf,
    IReadOnlyList<OwnershipReviewLinkDto> Links,
    OwnershipReviewSummaryDto Summary,
    string EmptyStateTitle,
    string EmptyStateDetail);
