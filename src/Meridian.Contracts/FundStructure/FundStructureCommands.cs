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

public sealed record CreateAccountRequest(
    Guid AccountId,
    AccountTypeDto AccountType,
    string AccountCode,
    string DisplayName,
    string BaseCurrency,
    DateTimeOffset EffectiveFrom,
    string CreatedBy,
    Guid? EntityId = null,
    Guid? FundId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    string? Institution = null,
    CustodianAccountDetailsDto? CustodianDetails = null,
    BankAccountDetailsDto? BankDetails = null,
    string? PortfolioId = null,
    string? LedgerReference = null,
    string? StrategyId = null,
    string? RunId = null);

public sealed record UpdateCustodianAccountDetailsRequest(
    CustodianAccountDetailsDto Details,
    string UpdatedBy);

public sealed record UpdateBankAccountDetailsRequest(
    BankAccountDetailsDto Details,
    string UpdatedBy);

public sealed record IngestCustodianStatementRequest(
    Guid BatchId,
    Guid AccountId,
    DateOnly AsOfDate,
    string CustodianName,
    string SourceFormat,
    string? FileName,
    IReadOnlyList<CustodianPositionLineDto> Lines,
    string LoadedBy);

public sealed record IngestBankStatementRequest(
    Guid BatchId,
    Guid AccountId,
    DateOnly StatementDate,
    string BankName,
    string? FileName,
    IReadOnlyList<BankStatementLineDto> Lines,
    string LoadedBy);

public sealed record RecordAccountBalanceSnapshotRequest(
    Guid AccountId,
    DateOnly AsOfDate,
    string Currency,
    decimal CashBalance,
    string Source,
    string RecordedBy,
    decimal? SecuritiesMarketValue = null,
    decimal? AccruedInterest = null,
    decimal? PendingSettlement = null,
    string? ExternalReference = null);

public sealed record ReconcileAccountRequest(
    Guid AccountId,
    DateOnly AsOfDate,
    string RequestedBy);

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
