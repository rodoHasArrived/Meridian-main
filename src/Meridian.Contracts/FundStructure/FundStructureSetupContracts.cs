namespace Meridian.Contracts.FundStructure;

/// <summary>Request envelope for previewing or committing an operator entity setup draft.</summary>
public sealed record FundStructureSetupDraftRequest(
    FundStructureSetupDraftDto Draft,
    bool ReuseExistingEntities = true);

public sealed record FundStructureSetupEntityResolutionDto(
    FundStructureSetupNodeAlias Alias,
    FundStructureNodeKindDto Kind,
    Guid NodeId,
    string Code,
    string Name,
    bool WasCreated,
    string Resolution);

public sealed record FundStructureSetupCommitResultDto(
    bool Succeeded,
    IReadOnlyList<FundStructureSetupEntityResolutionDto> Entities,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
    FundStructureAssignmentDto? AccountHandoffAssignment,
    FundStructureGraphDto Graph,
    FundStructureSetupValidationSummaryDto ValidationSummary,
    IReadOnlyList<string> Messages);
