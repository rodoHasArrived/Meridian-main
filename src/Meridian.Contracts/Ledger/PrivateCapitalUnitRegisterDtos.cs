namespace Meridian.Contracts.Ledger;

/// <summary>Exact reviewed source bytes and a retained hard-close period for one entity-owned book.</summary>
public sealed record PreviewPrivateCapitalUnitRegisterRequest(
    Guid PeriodId,
    string EvidenceLink,
    string DocumentId);

/// <summary>The inception-to-cutoff register supplied by an accepted Evidence Vault document.</summary>
public sealed record PrivateCapitalUnitRegisterSourceDto(
    Guid LedgerBookId,
    Guid PeriodId,
    Guid EntityId,
    string FundProfileId,
    string ShareClassId,
    string Currency,
    string PolicyVersion,
    DateOnly InceptionDate,
    DateOnly AsOf,
    decimal InceptionNavPerUnit,
    decimal ClassNav,
    IReadOnlyList<PrivateCapitalUnitRegisterSourceMovementDto> Movements);

public sealed record PrivateCapitalUnitRegisterSourceMovementDto(
    Guid JournalEntryId,
    string InvestorId,
    string CapitalAccountId,
    DateOnly DealingDate,
    string Type,
    decimal NavPerUnit,
    decimal Amount,
    decimal Units);

public sealed record PrivateCapitalUnitRegisterHoldingDto(
    string InvestorId,
    decimal Units,
    decimal SubscribedAmount,
    decimal RedeemedAmount,
    decimal EqualisationCredit,
    decimal ContingentRedemption,
    decimal Value);

public sealed record PrivateCapitalUnitRegisterMovementDto(
    Guid TransactionId,
    string InvestorId,
    string CapitalAccountId,
    DateOnly DealingDate,
    string Type,
    decimal NavPerUnit,
    decimal Amount,
    decimal Units,
    decimal EqualisationCredit,
    decimal ContingentRedemption);

/// <summary>Read-only unit and NAV reconciliation with independently retained ledger and register evidence.</summary>
public sealed record PrivateCapitalUnitRegisterPreviewDto(
    PrivateCapitalScopeDto Scope,
    Guid PeriodId,
    DateOnly AsOf,
    string ShareClassId,
    string Currency,
    string PolicyVersion,
    decimal UnitsOutstanding,
    decimal ClassNav,
    decimal NavPerUnit,
    decimal HighWaterNavPerUnit,
    decimal ValuationRoundingResidual,
    IReadOnlyList<PrivateCapitalUnitRegisterHoldingDto> Holdings,
    IReadOnlyList<PrivateCapitalUnitRegisterMovementDto> Movements,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> Issues,
    string EvidenceLink,
    string DocumentId,
    string SourceHashSha256,
    string CloseSnapshotHashSha256,
    bool IsTied);
