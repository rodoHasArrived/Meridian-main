using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>The owner's bounded W14 treatment. It never grants ordinary independent review.</summary>
public static class CorporateActionCashDividendAttestationPolicy
{
    public const string Version = "single-principal-cash-dividend-v1";
    public const string DecisionKind = "PreparerAttestation";
    public const string CaseState = "PreparerAttested";
    public const string Currency = "USD";
    public const decimal RelatedEventGrossLimitUsd = 10_000m;
    public const decimal BookCalendarMonthGrossLimitUsd = 100_000m;
    public const decimal EventGrossLimitUsd = RelatedEventGrossLimitUsd;
    public const decimal MonthlyGrossLimitUsd = BookCalendarMonthGrossLimitUsd;
    public const string Disclosure = SinglePrincipalAttestationPolicy.Disclosure;
    public const string Confirmation = "I inspected this exact regular mandatory cash-dividend projection and the complete related-event entitlement. I attest as its preparer, not as an independent reviewer. Existing posting controls remain required.";
    public static string PolicyHash { get; } = Sha256Digest.ComputeUtf8(
        Version + "\nregular-mandatory-cash-dividend-declaration\nUSD\n10000\n100000\nsource-ex-date-calendar-month;recognition=source-ex-date\n" +
        "debit=governed-dividend-receivable-role;credit=governed-dividend-income-role;" +
        "gross-no-netting;complete-book-entitlement;source-alias-stable;no-corrections;no-elections;no-lot-mutation;" +
        "no-estimates;no-FX;no-tax-special-treatment;no-independent-review-credit\n" + Confirmation);
}

public sealed record CorporateActionCashDividendDecisionAuthorityDto(
    bool CanAttestAccounting,
    bool CanPostAccounting,
    OperationsActionOriginDto Origin = OperationsActionOriginDto.HumanOperator);

public sealed record AttestCorporateActionCashDividendRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    Guid ProjectionId,
    long ExpectedDeclarationRevision,
    string ExpectedEligibilityHash,
    string Reason,
    string EvidenceReference,
    string EvidenceHash,
    string Confirmation,
    string Actor,
    CorporateActionCaseScopeDto? ScopeAssertion = null,
    string? CorrelationId = null,
    CorporateActionCashDividendDecisionAuthorityDto? Authority = null);

public sealed record PostAttestedCorporateActionCashDividendRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    Guid ProjectionId,
    Guid AttestationId,
    string Reason,
    string Actor,
    CorporateActionCaseScopeDto? ScopeAssertion = null,
    string? CorrelationId = null,
    CorporateActionCashDividendDecisionAuthorityDto? Authority = null);

/// <summary>
/// Server-resolved complete-book entitlement, retained independently of a client amount or a
/// per-account projection. No netting, missing population or unverified valuation may qualify.
/// </summary>
public sealed record CorporateActionCashDividendEligibilityDto(
    Guid SecurityId,
    DateOnly ExDate,
    string RelatedEventKey,
    string ProviderId,
    string SourceEventId,
    string SourceEventVersion,
    string SourceEvidenceHash,
    decimal DividendPerShare,
    decimal GrossImpactUsd,
    decimal WholeEventGrossUsd,
    DateOnly BudgetMonth,
    string EntitlementSnapshotHash,
    string EntitlementSnapshotJson,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CashDividendAccountRoleBindingDto? AccountRoleBinding = null);

/// <summary>Retained preparer decision and durable cap reservation, separate from approval.</summary>
public sealed record CorporateActionCashDividendAttestationDto(
    Guid AttestationId,
    Guid CaseId,
    Guid ProjectionId,
    long BoundCaseVersion,
    Guid DeclarationId,
    long DeclarationRevision,
    string PolicyVersion,
    string PolicyHash,
    Guid BudgetReservationId,
    string RelatedEventKey,
    DateOnly ExDate,
    string ProviderId,
    string SourceEventId,
    string SourceEventVersion,
    string SourceEvidenceHash,
    string EntitlementSnapshotHash,
    string EntitlementSnapshotJson,
    decimal GrossImpactUsd,
    decimal WholeEventGrossUsd,
    DateOnly BudgetMonth,
    string AttestedBy,
    DateTimeOffset AttestedAtUtc,
    string Reason,
    string EvidenceReference,
    string EvidenceHash,
    string Confirmation,
    string Disclosure,
    DateTimeOffset? VoidedAtUtc = null,
    string? VoidedBy = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CashDividendAccountRoleBindingDto? AccountRoleBinding = null)
{
    public bool IsActive => VoidedAtUtc is null;
    public string DecisionKind => CorporateActionCashDividendAttestationPolicy.DecisionKind;
}

/// <summary>A posting bound to a preparer decision; deliberately contains no ordinary ApprovalId.</summary>
public sealed record CorporateActionCashDividendPostingDto(
    Guid PostingId,
    Guid CaseId,
    Guid ProjectionId,
    Guid AttestationId,
    Guid JournalEntryId,
    Guid LedgerBookId,
    Guid PeriodId,
    string AccountingBasis,
    string Currency,
    decimal TotalDebits,
    decimal TotalCredits,
    string PostingStatus,
    Guid? TaxLotMutationBatchId,
    string PostedBy,
    DateTimeOffset PostedAtUtc);

public sealed record CorporateActionCashDividendAttestationResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionCashDividendAttestationDto Attestation,
    CorporateActionCaseTransitionDto Transition,
    bool Replayed);

public sealed record CorporateActionCashDividendPostingResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionCashDividendPostingDto Posting,
    CorporateActionCaseTransitionDto Transition,
    bool Replayed);

/// <summary>Original posting proof; later declaration withdrawal does not rewrite its decision.</summary>
public sealed record CorporateActionCashDividendPostedProofDto(
    CorporateActionCashDividendPostingDto Posting,
    CorporateActionCashDividendAttestationDto Attestation,
    CashDividendAttestationDeclarationDto Declaration,
    CorporateActionCaseAccountingProjectionDto Projection);

public sealed record CorporateActionCashDividendPreviewDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionCaseAccountingProjectionDto Projection,
    CorporateActionCashDividendEligibilityDto Eligibility,
    string Disclosure,
    string Confirmation);

public interface ICorporateActionCashDividendEligibilityResolver
{
    Task<CorporateActionCashDividendEligibilityDto> ResolveAsync(
        CorporateActionProcessingCaseDto processingCase,
        CorporateActionCaseAccountingProjectionDto projection,
        AssetAccountingEventSpineDto spine,
        CancellationToken ct = default);
}

public interface ICorporateActionCashDividendAccountingService
{
    Task<CorporateActionCashDividendPreviewDto> PreviewCashDividendAsync(
        Guid caseId, string tenantId, string companyId, CancellationToken ct = default);

    Task<CorporateActionCashDividendAttestationResultDto> AttestCashDividendAsync(
        AttestCorporateActionCashDividendRequestDto request, CancellationToken ct = default);

    Task<CorporateActionCashDividendPostingResultDto> PostAttestedCashDividendAsync(
        PostAttestedCorporateActionCashDividendRequestDto request, CancellationToken ct = default);
}
