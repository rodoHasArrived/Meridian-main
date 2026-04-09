using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<FundStructureNodeKindDto>))]
public enum FundStructureNodeKindDto
{
    Organization,
    Business,
    Client,
    Fund,
    Sleeve,
    Vehicle,
    InvestmentPortfolio,
    Entity,
    Account
}

[JsonConverter(typeof(JsonStringEnumConverter<BusinessKindDto>))]
public enum BusinessKindDto
{
    FinancialAdvisor,
    FundManager,
    Hybrid
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

public sealed record OrganizationSummaryDto(
    Guid OrganizationId,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<Guid> BusinessIds,
    string? Description = null);

public sealed record BusinessSummaryDto(
    Guid BusinessId,
    Guid OrganizationId,
    BusinessKindDto BusinessKind,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<Guid> ClientIds,
    IReadOnlyList<Guid> FundIds,
    IReadOnlyList<Guid> InvestmentPortfolioIds,
    string? Description = null);

public sealed record ClientSummaryDto(
    Guid ClientId,
    Guid BusinessId,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<Guid> InvestmentPortfolioIds,
    string? Description = null);

public sealed record FundSummaryDto(
    Guid FundId,
    Guid? BusinessId,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<Guid> SleeveIds,
    IReadOnlyList<Guid> VehicleIds,
    IReadOnlyList<Guid> EntityIds,
    IReadOnlyList<Guid> InvestmentPortfolioIds,
    IReadOnlyList<Guid> AccountIds,
    string? Description = null);

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
    IReadOnlyList<Guid> InvestmentPortfolioIds,
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
    IReadOnlyList<Guid> InvestmentPortfolioIds,
    IReadOnlyList<Guid> AccountIds,
    string? Description = null);

public sealed record LegalEntitySummaryDto(
    Guid EntityId,
    LegalEntityTypeDto EntityType,
    string Code,
    string Name,
    string Jurisdiction,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Description = null);

public sealed record InvestmentPortfolioSummaryDto(
    Guid InvestmentPortfolioId,
    Guid BusinessId,
    string Code,
    string Name,
    string BaseCurrency,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    Guid? ClientId,
    Guid? FundId,
    Guid? SleeveId,
    Guid? VehicleId,
    Guid? EntityId,
    IReadOnlyList<Guid> AccountIds,
    string? Description = null,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

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
    BankAccountDetailsDto? BankDetails = null,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

public sealed record FundStructureSharedDataAccessDto(
    SecurityMasterAccessSummaryDto SecurityMaster,
    HistoricalPriceAccessSummaryDto HistoricalPrices,
    BackfillAccessSummaryDto Backfill);

public sealed record SecurityMasterAccessSummaryDto(
    bool IsAvailable,
    string AvailabilityDescription,
    bool InstrumentDefinitionsAccessible,
    bool EconomicDefinitionsAccessible,
    bool TradingParametersAccessible);

public sealed record HistoricalPriceAccessSummaryDto(
    bool IsAvailable,
    bool HasStoredData,
    int AvailableSymbolCount,
    IReadOnlyList<string> SampleSymbols,
    string AvailabilityDescription);

public sealed record BackfillAccessSummaryDto(
    bool IsAvailable,
    bool IsActive,
    int ProviderCount,
    string? LastProvider,
    DateOnly? LastFrom,
    DateOnly? LastTo,
    DateTimeOffset? LastCompletedUtc,
    bool? LastRunSucceeded,
    int SymbolCheckpointCount,
    int SymbolBarCountCount,
    string AvailabilityDescription);

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

public sealed record OrganizationStructureGraphDto(
    IReadOnlyList<OrganizationSummaryDto> Organizations,
    IReadOnlyList<BusinessSummaryDto> Businesses,
    IReadOnlyList<ClientSummaryDto> Clients,
    IReadOnlyList<FundSummaryDto> Funds,
    IReadOnlyList<SleeveSummaryDto> Sleeves,
    IReadOnlyList<VehicleSummaryDto> Vehicles,
    IReadOnlyList<LegalEntitySummaryDto> Entities,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> Accounts,
    IReadOnlyList<FundStructureNodeDto> Nodes,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
    IReadOnlyList<FundStructureAssignmentDto> Assignments,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

public sealed record AdvisoryClientViewDto(
    ClientSummaryDto Client,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> Accounts);

public sealed record AdvisoryStructureViewDto(
    OrganizationSummaryDto Organization,
    BusinessSummaryDto Business,
    IReadOnlyList<AdvisoryClientViewDto> Clients,
    IReadOnlyList<InvestmentPortfolioSummaryDto> UnassignedInvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> UnassignedAccounts,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

public sealed record FundSleeveOperatingViewDto(
    SleeveSummaryDto Sleeve,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> Accounts);

public sealed record VehicleOperatingViewDto(
    VehicleSummaryDto Vehicle,
    LegalEntitySummaryDto? LegalEntity,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> Accounts);

public sealed record FundOperatingSliceDto(
    FundSummaryDto Fund,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> Accounts,
    IReadOnlyList<FundSleeveOperatingViewDto> Sleeves,
    IReadOnlyList<VehicleOperatingViewDto> Vehicles);

public sealed record FundOperatingViewDto(
    OrganizationSummaryDto Organization,
    BusinessSummaryDto Business,
    IReadOnlyList<FundOperatingSliceDto> Funds,
    IReadOnlyList<InvestmentPortfolioSummaryDto> UnassignedInvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> UnassignedAccounts,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

public readonly record struct LedgerGroupId(string Value);

public sealed record LedgerGroupSummaryDto(
    LedgerGroupId LedgerGroupId,
    string DisplayName,
    IReadOnlyList<Guid> AccountIds,
    IReadOnlyList<Guid> InvestmentPortfolioIds,
    IReadOnlyList<Guid> ClientIds,
    IReadOnlyList<Guid> FundIds,
    IReadOnlyList<Guid> SleeveIds,
    IReadOnlyList<Guid> VehicleIds,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

public sealed record AccountingStructureViewDto(
    OrganizationSummaryDto? Organization,
    BusinessSummaryDto? Business,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<AccountSummaryDto> Accounts,
    IReadOnlyList<LedgerGroupSummaryDto> LedgerGroups,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

public sealed record FundStructureGraphDto(
    IReadOnlyList<FundStructureNodeDto> Nodes,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
    IReadOnlyList<FundStructureAssignmentDto> Assignments);

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceCashFlowScopeKindDto>))]
public enum GovernanceCashFlowScopeKindDto
{
    Organization,
    Business,
    Client,
    Fund,
    Sleeve,
    Vehicle,
    InvestmentPortfolio,
    Account,
    LedgerGroup
}

public sealed record GovernanceCashFlowScopeDto(
    GovernanceCashFlowScopeKindDto ScopeKind,
    string DisplayName,
    Guid? OrganizationId,
    Guid? BusinessId,
    Guid? ClientId,
    Guid? FundId,
    Guid? SleeveId,
    Guid? VehicleId,
    Guid? InvestmentPortfolioId,
    Guid? AccountId,
    LedgerGroupId? LedgerGroupId,
    IReadOnlyList<Guid> AccountIds,
    IReadOnlyList<Guid> InvestmentPortfolioIds);

public sealed record GovernanceCashFlowAccountViewDto(
    Guid AccountId,
    string AccountCode,
    string DisplayName,
    string BaseCurrency,
    string? LedgerReference,
    decimal CurrentCashBalance,
    decimal RealizedNetFlow,
    decimal ProjectedNetFlow,
    int RealizedEntryCount,
    int ProjectedEntryCount,
    DateOnly? LatestSnapshotDate,
    bool UsedTrendFallback,
    int SecurityProjectedEntryCount = 0,
    bool UsedSecurityMasterRules = false,
    FundStructureSharedDataAccessDto? SharedDataAccess = null);

public sealed record GovernanceCashFlowEntryDto(
    DateTimeOffset EventDate,
    decimal Amount,
    string Currency,
    string EventKind,
    string SourceKind,
    Guid AccountId,
    string AccountDisplayName,
    string? LedgerReference,
    string? Description,
    bool IsProjected,
    Guid? SecurityId = null,
    string? SecurityDisplayName = null,
    string? SecurityTypeName = null);

public sealed record GovernanceCashFlowBucketDto(
    int BucketIndex,
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    decimal ProjectedInflows,
    decimal ProjectedOutflows,
    decimal NetFlow,
    string Currency,
    int EventCount);

public sealed record GovernanceCashFlowLadderDto(
    DateTimeOffset AsOf,
    DateTimeOffset WindowEnd,
    string Currency,
    int BucketDays,
    decimal TotalProjectedInflows,
    decimal TotalProjectedOutflows,
    decimal NetPosition,
    IReadOnlyList<GovernanceCashFlowBucketDto> Buckets);

public sealed record GovernanceCashFlowVarianceBucketDto(
    int BucketIndex,
    DateTimeOffset RealizedBucketStart,
    DateTimeOffset RealizedBucketEnd,
    DateTimeOffset ProjectedBucketStart,
    DateTimeOffset ProjectedBucketEnd,
    decimal RealizedInflows,
    decimal RealizedOutflows,
    decimal RealizedNetFlow,
    decimal ProjectedInflows,
    decimal ProjectedOutflows,
    decimal ProjectedNetFlow,
    decimal VarianceAmount,
    int RealizedEventCount,
    int ProjectedEventCount,
    string Currency);

public sealed record GovernanceCashFlowVarianceSummaryDto(
    decimal RealizedInflows,
    decimal RealizedOutflows,
    decimal RealizedNetFlow,
    decimal ProjectedInflows,
    decimal ProjectedOutflows,
    decimal ProjectedNetFlow,
    decimal VarianceAmount,
    string ComparisonBasis);

public sealed record GovernanceCashFlowViewDto(
    GovernanceCashFlowScopeDto Scope,
    DateTimeOffset AsOf,
    DateTimeOffset HistoricalWindowStart,
    DateTimeOffset ProjectionWindowEnd,
    string Currency,
    int HistoricalDays,
    int ForecastDays,
    int BucketDays,
    int AccountCount,
    decimal CurrentCashBalance,
    decimal ProjectedClosingCashBalance,
    IReadOnlyList<GovernanceCashFlowAccountViewDto> Accounts,
    IReadOnlyList<GovernanceCashFlowEntryDto> RealizedEntries,
    IReadOnlyList<GovernanceCashFlowEntryDto> ProjectedEntries,
    GovernanceCashFlowLadderDto RealizedLadder,
    GovernanceCashFlowLadderDto ProjectedLadder,
    GovernanceCashFlowVarianceSummaryDto VarianceSummary,
    IReadOnlyList<GovernanceCashFlowVarianceBucketDto> VarianceBuckets,
    FundStructureSharedDataAccessDto? SharedDataAccess = null,
    int SecurityProjectedEntryCount = 0);
