namespace Meridian.Contracts.FundStructure;

public sealed record FundStructureQuery(
    Guid? FundId = null,
    Guid? NodeId = null,
    FundStructureNodeKindDto? NodeKind = null,
    bool ActiveOnly = true,
    DateTimeOffset? AsOf = null);

public sealed record FundStructureAssignmentQuery(
    Guid? NodeId = null,
    string? AssignmentType = null,
    string? AssignmentReference = null,
    DateTimeOffset? AsOf = null,
    bool ActiveOnly = true);

public sealed record AccountStructureQuery(
    Guid? AccountId = null,
    Guid? EntityId = null,
    Guid? FundId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    string? PortfolioId = null,
    string? LedgerReference = null,
    string? StrategyId = null,
    string? RunId = null,
    bool ActiveOnly = true,
    DateTimeOffset? AsOf = null);
