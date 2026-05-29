namespace Meridian.Contracts.FundStructure;

public sealed record FundStructureSetupDraftRequest(
    Guid? DraftId,
    DateTimeOffset EffectiveFrom,
    string RequestedBy,
    IReadOnlyList<FundStructureSetupNodeDraftDto> Nodes,
    IReadOnlyList<FundStructureSetupLinkDraftDto> Links,
    bool ReuseExistingEntities = true);

public sealed record FundStructureSetupNodeDraftDto(
    string ClientKey,
    FundStructureNodeKindDto Kind,
    string Code,
    string Name,
    string BaseCurrency,
    Guid? NodeId = null,
    string? Description = null,
    BusinessKindDto? BusinessKind = null,
    ClientSegmentKind? ClientSegmentKind = null,
    LegalEntityTypeDto? LegalEntityType = null,
    string? Jurisdiction = null,
    string? Mandate = null,
    IReadOnlyList<Guid>? StrategyIds = null);

public sealed record FundStructureSetupLinkDraftDto(
    string ParentClientKey,
    string ChildClientKey,
    OwnershipRelationshipTypeDto RelationshipType,
    Guid? OwnershipLinkId = null,
    decimal? OwnershipPercent = null,
    bool IsPrimary = true,
    string? Notes = null);

public sealed record FundStructureSetupPreviewDto(
    bool IsValid,
    Guid? DraftId,
    IReadOnlyList<FundStructureSetupNodePreviewDto> Nodes,
    IReadOnlyList<FundStructureSetupLinkPreviewDto> Links,
    IReadOnlyList<FundStructureSetupValidationMessageDto> ValidationMessages,
    int CreateNodeCount,
    int ReuseNodeCount,
    int CreateLinkCount);

public sealed record FundStructureSetupNodePreviewDto(
    string ClientKey,
    FundStructureNodeKindDto Kind,
    Guid NodeId,
    string Code,
    string Name,
    bool WillCreate,
    bool ReusesExisting,
    string? Message = null);

public sealed record FundStructureSetupLinkPreviewDto(
    Guid OwnershipLinkId,
    Guid ParentNodeId,
    Guid ChildNodeId,
    OwnershipRelationshipTypeDto RelationshipType,
    bool WillCreate,
    bool ReusesExisting,
    string? Message = null);

public sealed record FundStructureSetupCommitResultDto(
    bool IsCommitted,
    Guid? DraftId,
    IReadOnlyList<Guid> CreatedNodeIds,
    IReadOnlyList<Guid> ReusedNodeIds,
    IReadOnlyList<Guid> CreatedOwnershipLinkIds,
    FundStructureSetupPreviewDto Preview,
    IReadOnlyList<FundStructureSetupValidationMessageDto> ValidationMessages);

public sealed record FundStructureSetupValidationMessageDto(
    string Severity,
    string Code,
    string Message,
    string? ClientKey = null);
