namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shadow-mode onboarding (W11-SHADOW-001). A shadow book runs in parallel with the incumbent administrator
/// or GL; it is reconciled every period and cannot publish, release, or close as the book of record until an
/// operator promotes it with a parallel-run certificate attached.
/// </summary>
public static class OnboardingShadowStates
{
    public const string Shadow = "Shadow";
    public const string Promoted = "Promoted";
    public const string Withdrawn = "Withdrawn";
}

/// <summary>Reasons an operator may give for a parallel-run difference. Only <see cref="MeridianError"/> never clears.</summary>
public static class OnboardingDifferenceClassifications
{
    public const string Timing = "Timing";
    public const string Pricing = "Pricing";
    public const string Treatment = "Treatment";
    public const string SourceData = "SourceData";
    public const string MeridianError = "MeridianError";

    public static readonly IReadOnlyList<string> All = [Timing, Pricing, Treatment, SourceData, MeridianError];
}

/// <summary>Incumbent opening-balance sources accepted by shadow onboarding.</summary>
public static class OnboardingOpeningSourceKinds
{
    /// <summary>camt.053 opening booked balance (OPBD) or BAI2 opening ledger (010, falling back to 040).</summary>
    public const string StatementOpening = "StatementOpening";
    /// <summary>Administrator opening pack CSV of cash, position and NAV rows.</summary>
    public const string AdministratorPack = "AdministratorPack";
    /// <summary>Retained external GL trial balance imported through an accounting-system provider.</summary>
    public const string GlTrialBalance = "GlTrialBalance";

    public static readonly IReadOnlyList<string> All = [StatementOpening, AdministratorPack, GlTrialBalance];
}

public sealed record DesignateShadowBookRequestDto(int ExpectedVersion, string Rationale);

public sealed record WithdrawShadowBookRequestDto(int ExpectedVersion, string Reason);

public sealed record OnboardingShadowDesignationDto(
    string State, Guid LedgerBookId, string DesignatedBy, DateTimeOffset DesignatedAtUtc, string Rationale,
    string? ResolvedBy = null, DateTimeOffset? ResolvedAtUtc = null,
    string? PromotionDecisionId = null, string? WithdrawalReason = null);

/// <summary>Maps an incumbent account identifier (IBAN, admin account code, GL account code) to a selected financial account.</summary>
public sealed record OnboardingOpeningAccountMappingDto(string ExternalAccount, string AccountId);

/// <summary>
/// Imports the incumbent's opening state for the workspace start date. File sources carry base64 content;
/// the GL trial balance source carries the exact retained provider import identity instead.
/// </summary>
public sealed record ImportOnboardingOpeningBalancesRequestDto(
    int ExpectedVersion, string SourceKind, string MappingProfileId, string MappingVersion,
    IReadOnlyList<OnboardingOpeningAccountMappingDto> AccountMappings,
    string? FileName = null, string? ContentBase64 = null,
    string? ProviderId = null, string? ImportId = null, string? Notes = null);

public sealed record OnboardingOpeningBalanceLineDto(
    string DifferenceKey, string Kind, string AccountId, string Currency, string? InstrumentId,
    decimal? MeridianAmount, decimal? IncumbentAmount, decimal? Difference, decimal Tolerance,
    string Status, string? Classification, string? MissingReason);

/// <summary>Governed opening-balance reconciliation: incumbent opening state side by side with Meridian's records.</summary>
public sealed record OnboardingOpeningBalanceReportDto(
    string ComparisonId, string SourceKind, DateOnly AsOfDate, string SourceContentHash,
    int MatchedCount, int ExplainedCount, int BreakCount, int MissingCount, bool IsReconciled,
    IReadOnlyList<OnboardingOpeningBalanceLineDto> Lines, IReadOnlyList<string> EvidenceIds);

public sealed record ExplainOnboardingDifferenceRequestDto(
    int ExpectedVersion, string ComparisonId, string Classification, string Notes,
    IReadOnlyList<string> EvidenceIds, DateOnly? ExpectedReversalDate = null);

/// <summary>
/// An operator explanation of one difference. It applies only while the compared amounts are unchanged, never
/// to a <see cref="OnboardingDifferenceClassifications.MeridianError"/>, and for timing only through the
/// expected reversal date.
/// </summary>
public sealed record OnboardingDifferenceExplanationDto(
    string ExplanationId, string ComparisonId, string DifferenceKey, string Classification, string Notes,
    IReadOnlyList<string> EvidenceIds, DateOnly? ExpectedReversalDate,
    decimal? MeridianAmount, decimal? ExternalAmount,
    string ActorId, DateTimeOffset RecordedAtUtc, int DataRevision);

public sealed record OnboardingClassificationCountDto(string Classification, int Count);

/// <summary>Per-period agreement between Meridian and the incumbent for one required date.</summary>
public sealed record OnboardingPeriodSummaryDto(
    DateOnly AsOfDate, string ComparisonId, string? NavCurrency,
    decimal? MeridianNav, decimal? ExternalNav, decimal? NavVariance,
    int ObservationCount, int MatchedCount, int ExplainedCount, int UnexplainedCount, int MissingSourceCount,
    IReadOnlyList<OnboardingClassificationCountDto> ExplainedByClassification, bool IsClean);

public sealed record IssueParallelRunCertificateRequestDto(int ExpectedVersion);

public sealed record OnboardingParallelRunCertificateContentDto(
    string WorkspaceId, string Name, OnboardingScopeDto Scope, int DataRevision,
    OnboardingOpeningBalanceReportDto Opening,
    IReadOnlyList<OnboardingPeriodSummaryDto> Periods, IReadOnlyList<DateOnly> CleanPeriods,
    IReadOnlyList<OnboardingDifferenceExplanationDto> Explanations,
    IReadOnlyList<OnboardingReviewDto> ApprovingReviews,
    string PacketId, string PacketContentHash, string Statement);

/// <summary>
/// The parallel-run certificate: every period's agreement with the incumbent, every difference explained,
/// hashed and frozen. It is the evidence a promotion decision must attach; it is not itself a promotion.
/// </summary>
public sealed record OnboardingParallelRunCertificateDto(
    string CertificateId, DateTimeOffset IssuedAtUtc, string IssuedBy,
    OnboardingParallelRunCertificateContentDto Content, string ContentHash, string HashAlgorithm);

public sealed record PromoteShadowBookRequestDto(
    int ExpectedVersion, string CertificateId, string Rationale, IReadOnlyList<string> EvidenceIds);

public sealed record ShadowBookPromotionDecisionDto(
    string DecisionId, string CertificateId, string CertificateContentHash, string DecidedBy,
    DateTimeOffset DecidedAtUtc, string Rationale, IReadOnlyList<string> EvidenceIds, int DataRevision);

/// <summary>Append-only, hash-chained record of every shadow-mode authority event on a workspace.</summary>
public sealed record OnboardingShadowAuditEntryDto(
    int Sequence, string Action, string ActorId, DateTimeOffset OccurredAtUtc, string SubjectId,
    string Detail, string? PreviousHash, string EntryHash);

/// <summary>Whether a ledger book may act as the book of record (publish, release, hard-close).</summary>
public sealed record BookOfRecordStatusDto(
    Guid LedgerBookId, bool IsBookOfRecord, string? ShadowState, string? ShadowWorkspaceId, string Reason,
    int? CleanPeriodCount = null, int? RequiredPeriodCount = null, int? UnexplainedDifferenceCount = null);

/// <summary>
/// Answers whether a ledger book is authoritative. A book in shadow mode (or a withdrawn shadow trial) is not.
/// </summary>
public interface IBookOfRecordGuard
{
    Task<BookOfRecordStatusDto> GetStatusAsync(
        string tenantId, string? companyId, Guid ledgerBookId, CancellationToken ct = default);
}

/// <summary>Raised when a shadow book is asked to act as the book of record.</summary>
public sealed class ShadowBookNotBookOfRecordException(string message) : InvalidOperationException(message);
