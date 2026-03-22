using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<FundStructureNodeKindDto>))]
public enum FundStructureNodeKindDto
{
    Fund,
    Sleeve,
    Vehicle,
    Entity,
    Account
}

[JsonConverter(typeof(JsonStringEnumConverter<LegalEntityTypeDto>))]
public enum LegalEntityTypeDto
{
    Fund,
    ManagementCompany,
    GeneralPartner,
    LimitedPartner,
    Vehicle,
    Custodian,
    Broker,
    Counterparty,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountTypeDto>))]
public enum AccountTypeDto
{
    Brokerage,
    Custody,
    Bank,
    Margin,
    PrimeBroker,
    LedgerControl,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter<OwnershipRelationshipTypeDto>))]
public enum OwnershipRelationshipTypeDto
{
    Owns,
    Advises,
    Operates,
    ClearsFor,
    CustodiesFor,
    AllocatesTo
}

public sealed record FundStructureNodeDto(
    Guid NodeId,
    FundStructureNodeKindDto Kind,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record FundSummaryDto(
    Guid FundId,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<Guid> SleeveIds,
    IReadOnlyList<Guid> VehicleIds,
    IReadOnlyList<Guid> EntityIds,
    IReadOnlyList<Guid> AccountIds);

public sealed record SleeveSummaryDto(
    Guid SleeveId,
    Guid FundId,
    string Code,
    string Name,
    string? Mandate,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<Guid> StrategyIds,
    IReadOnlyList<Guid> AccountIds);

public sealed record VehicleSummaryDto(
    Guid VehicleId,
    Guid FundId,
    Guid LegalEntityId,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<Guid> AccountIds);

public sealed record LegalEntitySummaryDto(
    Guid EntityId,
    LegalEntityTypeDto EntityType,
    string Code,
    string Name,
    string Jurisdiction,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record AccountSummaryDto(
    Guid AccountId,
    AccountTypeDto AccountType,
    Guid? EntityId,
    Guid? FundId,
    Guid? SleeveId,
    Guid? VehicleId,
    string AccountCode,
    string DisplayName,
    string BaseCurrency,
    string? Institution,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? PortfolioId,
    string? LedgerReference,
    string? StrategyId,
    string? RunId);

public sealed record OwnershipLinkDto(
    Guid OwnershipLinkId,
    Guid ParentNodeId,
    Guid ChildNodeId,
    OwnershipRelationshipTypeDto RelationshipType,
    decimal? OwnershipPercent,
    bool IsPrimary,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Notes);

public sealed record FundStructureAssignmentDto(
    Guid AssignmentId,
    Guid NodeId,
    string AssignmentType,
    string AssignmentReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsPrimary);

public sealed record FundStructureGraphDto(
    IReadOnlyList<FundStructureNodeDto> Nodes,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
    IReadOnlyList<FundStructureAssignmentDto> Assignments);
