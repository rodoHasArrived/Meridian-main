namespace Meridian.Contracts.FundStructure;

public sealed record CreateOrganizationRequest(
    Guid OrganizationId,
    string Code,
    string Name,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    string? Description = null);

public sealed record CreateBusinessRequest(
    Guid BusinessId,
    Guid OrganizationId,
    BusinessKindDto BusinessKind,
    string Code,
    string Name,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    string? Description = null);

public sealed record CreateClientRequest(
    Guid ClientId,
    Guid BusinessId,
    string Code,
    string Name,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    string? Description = null);

public sealed record CreateFundRequest(
    Guid FundId,
    string Code,
    string Name,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    string? Description = null,
    Guid? BusinessId = null);

public sealed record CreateSleeveRequest(
    Guid SleeveId,
    Guid FundId,
    string Code,
    string Name,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    string? Mandate = null,
    IReadOnlyList<Guid>? StrategyIds = null);

public sealed record CreateVehicleRequest(
    Guid VehicleId,
    Guid FundId,
    Guid LegalEntityId,
    string Code,
    string Name,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    string? Description = null);

public sealed record CreateLegalEntityRequest(
    Guid EntityId,
    LegalEntityTypeDto EntityType,
    string Code,
    string Name,
    string Jurisdiction,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    string? Description = null);

public sealed record CreateInvestmentPortfolioRequest(
    Guid InvestmentPortfolioId,
    Guid BusinessId,
    string Code,
    string Name,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    Guid? ClientId = null,
    Guid? FundId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? EntityId = null,
    string? Description = null);
public sealed record LinkFundStructureNodesRequest(
    Guid OwnershipLinkId,
    Guid ParentNodeId,
    Guid ChildNodeId,
    OwnershipRelationshipTypeDto RelationshipType,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    decimal? OwnershipPercent = null,
    bool IsPrimary = false,
    string? Notes = null);

public sealed record AssignFundStructureNodeRequest(
    Guid AssignmentId,
    Guid NodeId,
    string AssignmentType,
    string AssignmentReference,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    bool IsPrimary = false,
    DateTimeOffset? EffectiveTo = null);
