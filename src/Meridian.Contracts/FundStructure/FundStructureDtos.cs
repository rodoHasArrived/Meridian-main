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
    AllocatesTo,
    BanksFor
}

/// Custody-specific settlement details attached to a Custody-type account.
public sealed record CustodianAccountDetailsDto(
    string? SubAccountNumber,
    string? DtcParticipantCode,
    string? CrestMemberCode,
    string? EuroclearAccountNumber,
    string? ClearstreamAccountNumber,
    string? PrimebrokerGiveupCode,
    string? SafekeepingLocation,
    string? ServiceAgreementReference);

/// Banking-specific settlement details attached to a Bank-type account.
public sealed record BankAccountDetailsDto(
    string AccountNumber,
    string BankName,
    string? BranchName,
    string? Iban,
    string? BicSwift,
    string? RoutingNumber,
    string? SortCode,
    string? IntermediaryBankBic,
    string? IntermediaryBankName,
    string? BeneficiaryName,
    string? BeneficiaryAddress);

/// Balance snapshot captured from an external statement for reconciliation.
public sealed record AccountBalanceSnapshotDto(
    Guid SnapshotId,
    Guid AccountId,
    Guid? FundId,
    DateOnly AsOfDate,
    string Currency,
    decimal CashBalance,
    decimal? SecuritiesMarketValue,
    decimal? AccruedInterest,
    decimal? PendingSettlement,
    string Source,
    DateTimeOffset RecordedAt,
    string? ExternalReference);

/// Fund-level multi-account view grouping accounts by type.
public sealed record FundAccountsDto(
    Guid FundId,
    IReadOnlyList<AccountSummaryDto> CustodianAccounts,
    IReadOnlyList<AccountSummaryDto> BankAccounts,
    IReadOnlyList<AccountSummaryDto> BrokerageAccounts,
    IReadOnlyList<AccountSummaryDto> OtherAccounts);

/// One position line from a custodian statement batch.
public sealed record CustodianPositionLineDto(
    Guid PositionLineId,
    Guid BatchId,
    Guid AccountId,
    DateOnly AsOfDate,
    string SecurityIdentifier,
    string IdentifierType,
    decimal Quantity,
    decimal MarketValue,
    string MarketValueCurrency,
    decimal? CostBasis,
    decimal? AccruedIncome,
    bool SettlementPending);

/// One transaction line from a bank account statement batch.
public sealed record BankStatementLineDto(
    Guid StatementLineId,
    Guid BatchId,
    Guid AccountId,
    DateOnly StatementDate,
    DateOnly ValueDate,
    decimal Amount,
    string Currency,
    string TransactionType,
    string Description,
    string? ExternalReference,
    decimal? RunningBalance);

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
    string? RunId,
    CustodianAccountDetailsDto? CustodianDetails = null,
    BankAccountDetailsDto? BankDetails = null);

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
