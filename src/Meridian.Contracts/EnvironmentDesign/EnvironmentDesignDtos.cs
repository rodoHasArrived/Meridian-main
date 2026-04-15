using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.EnvironmentDesign;

[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentNodeKind>))]
public enum EnvironmentNodeKind
{
    Organization,
    Business,
    Client,
    Fund,
    Sleeve,
    Vehicle,
    InvestmentPortfolio,
    Entity,
    Account,
    LedgerGroup
}

[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentLaneArchetype>))]
public enum EnvironmentLaneArchetype
{
    IndividualInvestor,
    AdvisoryPractice,
    FamilyOffice,
    FundPlatform,
    Custom
}

[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentManagedScopeKind>))]
public enum EnvironmentManagedScopeKind
{
    IndividualInvestor,
    FamilyOffice,
    Fund
}

[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentValidationSeverity>))]
public enum EnvironmentValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record EnvironmentNodeDefinitionDto(
    string NodeDefinitionId,
    EnvironmentNodeKind NodeKind,
    string Code,
    string Name,
    string BaseCurrency,
    string? Description = null,
    string? ParentNodeDefinitionId = null,
    OwnershipRelationshipTypeDto? ParentRelationshipType = null,
    string? LaneId = null,
    BusinessKindDto? BusinessKind = null,
    LegalEntityTypeDto? LegalEntityType = null,
    AccountTypeDto? AccountType = null,
    ClientSegmentKind ClientSegmentKind = ClientSegmentKind.Unspecified,
    string? Jurisdiction = null,
    string? LedgerReference = null,
    string? Institution = null,
    bool IsActive = true);

public sealed record EnvironmentRelationshipDefinitionDto(
    string RelationshipDefinitionId,
    string ParentNodeDefinitionId,
    string ChildNodeDefinitionId,
    OwnershipRelationshipTypeDto RelationshipType,
    bool IsPrimary = false,
    decimal? OwnershipPercent = null,
    string? Notes = null);

public sealed record EnvironmentLaneDefinitionDto(
    string LaneId,
    string Name,
    EnvironmentLaneArchetype Archetype,
    string DefaultWorkspaceId,
    string DefaultLandingPageTag,
    string RootNodeDefinitionId,
    string DefaultContextNodeDefinitionId,
    IReadOnlyList<EnvironmentManagedScopeKind> AllowedManagedScopeKinds,
    string? Description = null,
    IReadOnlyDictionary<string, string>? LabelOverrides = null,
    bool ShowInNavigation = true,
    bool UseForEmptyStates = true);

public sealed record OrganizationEnvironmentDefinitionDto(
    Guid OrganizationId,
    string OrganizationNodeDefinitionId,
    string OrganizationCode,
    string OrganizationName,
    string BaseCurrency,
    IReadOnlyList<EnvironmentLaneDefinitionDto> Lanes,
    IReadOnlyList<EnvironmentNodeDefinitionDto> Nodes,
    IReadOnlyList<EnvironmentRelationshipDefinitionDto> Relationships);

public sealed record CreateEnvironmentDraftRequest(
    Guid DraftId,
    string Name,
    string CreatedBy,
    OrganizationEnvironmentDefinitionDto Definition,
    string? Notes = null);

public sealed record EnvironmentDraftDto(
    Guid DraftId,
    string Name,
    OrganizationEnvironmentDefinitionDto Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedBy,
    string UpdatedBy,
    Guid? BasedOnVersionId = null,
    string? Notes = null);

public sealed record EnvironmentNodeRemapDto(
    string RemovedNodeDefinitionId,
    string ReplacementNodeDefinitionId);

public sealed record EnvironmentPublishPlanDto(
    Guid DraftId,
    string PublishedBy,
    bool AllowDestructiveDeletes = false,
    string? VersionLabel = null,
    string? Notes = null,
    IReadOnlyList<EnvironmentNodeRemapDto>? NodeRemaps = null);

public sealed record RollbackEnvironmentVersionRequest(
    Guid VersionId,
    string RolledBackBy,
    string? Reason = null);

public sealed record EnvironmentValidationIssueDto(
    EnvironmentValidationSeverity Severity,
    string Code,
    string Message,
    string? LaneId = null,
    string? NodeDefinitionId = null,
    string? RelationshipDefinitionId = null);

public sealed record EnvironmentValidationResultDto(
    bool IsValid,
    IReadOnlyList<EnvironmentValidationIssueDto> Issues);

public sealed record EnvironmentPublishChangeDto(
    string ChangeType,
    EnvironmentNodeKind? NodeKind,
    string TargetId,
    string Summary,
    bool IsBreaking = false);

public sealed record EnvironmentPublishPreviewDto(
    Guid DraftId,
    EnvironmentValidationResultDto Validation,
    IReadOnlyList<EnvironmentPublishChangeDto> Changes,
    bool HasDestructiveChanges);

public sealed record EnvironmentLaneRuntimeDto(
    string LaneId,
    string Name,
    EnvironmentLaneArchetype Archetype,
    string RootNodeDefinitionId,
    string DefaultWorkspaceId,
    string DefaultLandingPageTag,
    string DefaultContextNodeDefinitionId,
    IReadOnlyList<EnvironmentManagedScopeKind> AllowedManagedScopeKinds,
    IReadOnlyDictionary<string, string>? LabelOverrides = null,
    bool ShowInNavigation = true,
    bool UseForEmptyStates = true);

public sealed record PublishedEnvironmentNodeRuntimeDto(
    string NodeDefinitionId,
    Guid RuntimeNodeId,
    EnvironmentNodeKind NodeKind,
    string Name,
    string? ContextKey = null,
    string? LaneId = null);

public sealed record EnvironmentContextMappingDto(
    string NodeDefinitionId,
    Guid RuntimeNodeId,
    string ContextKey,
    string LaneId,
    string LaneName,
    EnvironmentLaneArchetype Archetype);

public sealed record EnvironmentLedgerGroupRuntimeDto(
    Guid LedgerGroupId,
    string NodeDefinitionId,
    string DisplayName,
    string BaseCurrency,
    Guid? OrganizationId,
    Guid? BusinessId,
    Guid? ClientId,
    Guid? FundId,
    Guid? SleeveId,
    Guid? VehicleId,
    IReadOnlyList<Guid> AccountIds,
    IReadOnlyList<Guid> InvestmentPortfolioIds);

public sealed record PublishedEnvironmentRuntimeDto(
    Guid OrganizationId,
    string OrganizationName,
    string BaseCurrency,
    OrganizationStructureGraphDto OrganizationGraph,
    IReadOnlyList<EnvironmentLaneRuntimeDto> Lanes,
    IReadOnlyList<PublishedEnvironmentNodeRuntimeDto> Nodes,
    IReadOnlyList<EnvironmentContextMappingDto> ContextMappings,
    IReadOnlyList<EnvironmentLedgerGroupRuntimeDto> LedgerGroups);

public sealed record PublishedEnvironmentVersionDto(
    Guid VersionId,
    Guid DraftId,
    Guid OrganizationId,
    string OrganizationName,
    string VersionLabel,
    DateTimeOffset PublishedAt,
    string PublishedBy,
    EnvironmentValidationResultDto Validation,
    PublishedEnvironmentRuntimeDto Runtime,
    IReadOnlyDictionary<string, Guid> RuntimeNodeMappings,
    string? Notes = null,
    bool IsCurrent = false);
