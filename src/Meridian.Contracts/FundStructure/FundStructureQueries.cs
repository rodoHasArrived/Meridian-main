namespace Meridian.Contracts.FundStructure;

public sealed record FundStructureQuery(
    Guid? FundId = null,
    Guid? NodeId = null,
    FundStructureNodeKindDto? NodeKind = null,
    bool ActiveOnly = true,
    DateTimeOffset? AsOf = null);

public sealed record OrganizationStructureQuery(
    Guid? OrganizationId = null,
    Guid? BusinessId = null,
    Guid? NodeId = null,
    FundStructureNodeKindDto? NodeKind = null,
    bool ActiveOnly = true,
    DateTimeOffset? AsOf = null);

public sealed record AdvisoryStructureQuery(
    Guid BusinessId,
    Guid? OrganizationId = null,
    Guid? ClientId = null,
    Guid? InvestmentPortfolioId = null,
    bool ActiveOnly = true,
    DateTimeOffset? AsOf = null);

public sealed record FundOperatingStructureQuery(
    Guid BusinessId,
    Guid? OrganizationId = null,
    Guid? FundId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? InvestmentPortfolioId = null,
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

public sealed record AccountingStructureQuery(
    Guid? OrganizationId = null,
    Guid? BusinessId = null,
    Guid? ClientId = null,
    Guid? FundId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? InvestmentPortfolioId = null,
    string? LedgerReference = null,
    bool ActiveOnly = true,
    DateTimeOffset? AsOf = null);

public sealed record GovernanceCashFlowQuery(
    GovernanceCashFlowScopeKindDto ScopeKind,
    Guid? OrganizationId = null,
    Guid? BusinessId = null,
    Guid? ClientId = null,
    Guid? FundId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? InvestmentPortfolioId = null,
    Guid? AccountId = null,
    LedgerGroupId? LedgerGroupId = null,
    bool ActiveOnly = true,
    DateTimeOffset? AsOf = null,
    string? Currency = null,
    int HistoricalDays = 7,
    int ForecastDays = 7,
    int BucketDays = 7);
