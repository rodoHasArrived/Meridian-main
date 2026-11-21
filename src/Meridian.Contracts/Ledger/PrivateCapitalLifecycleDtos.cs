namespace Meridian.Contracts.Ledger;

/// <summary>A stable generation intent; retry the same operation and use a new ID for deliberate regeneration.</summary>
public sealed record GeneratePrivateCapitalNoticeRequest(Guid OperationId, string? CommitmentId = null);

/// <summary>Explicit authority scope shared by commitment, call, and receipt records.</summary>
public sealed record PrivateCapitalScopeDto(
    string TenantId,
    string CompanyId,
    string EntityId,
    string FundProfileId,
    Guid LedgerBookId,
    string Currency);

/// <summary>Review status of one immutable commitment revision.</summary>
public enum PrivateCapitalCommitmentApprovalStatus { Draft, Approved }

/// <summary>Retained commitment terms. Posted amounts are read-model observations, not editable terms.</summary>
public sealed record PrivateCapitalCommitmentDto(
    string CommitmentId,
    PrivateCapitalScopeDto Scope,
    int Revision,
    string InvestorId,
    string CapitalAccountId,
    decimal TotalCommitment,
    DateOnly CommitmentDate,
    DateOnly EffectiveFrom,
    DateOnly? InvestmentPeriodEndDate,
    bool IsActive,
    PrivateCapitalCommitmentApprovalStatus ApprovalStatus,
    IReadOnlyList<string> EvidenceLinks,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedAtUtc = null,
    decimal CalledAmount = 0m,
    decimal FundedAmount = 0m)
{
    public decimal UncalledAmount => TotalCommitment - CalledAmount;
}

/// <summary>A retained per-investor call allocation, with independently observed posting state.</summary>
public sealed record PrivateCapitalCallLineDto(
    string InstallmentId,
    string CommitmentId,
    int CommitmentRevision,
    string InvestorId,
    string CapitalAccountId,
    decimal Amount,
    decimal UncalledBefore,
    decimal UncalledAfter,
    decimal IssuedAmount = 0m,
    decimal FundedAmount = 0m,
    Guid? IssuanceJournalEntryId = null)
{
    public decimal OutstandingAmount => IssuedAmount - FundedAmount;
}

/// <summary>Frozen call plan. Posting, notice delivery, and cash receipt remain separate outcomes.</summary>
public sealed record PrivateCapitalCallDto(
    string CallId,
    PrivateCapitalScopeDto Scope,
    string PeriodId,
    DateOnly NoticeDate,
    DateOnly DueDate,
    decimal AmountToCall,
    string? Purpose,
    string PlanHash,
    IReadOnlyList<PrivateCapitalCallLineDto> Lines,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc);

/// <summary>Evidence bound into the approved journal and checked in its final storage transaction.</summary>
public sealed record PrivateCapitalPostingAuthorityDto(
    PrivateCapitalScopeDto Scope,
    string CallId,
    string CommitmentId,
    int CommitmentRevision,
    string PlanHash,
    string Kind,
    decimal Amount,
    DateOnly EffectiveDate,
    string? FundingReceiptId = null,
    string? BankLineId = null,
    string? BankTransactionHash = null);

/// <summary>Immutable economic receipt written atomically with its journal.</summary>
public sealed record PrivateCapitalPostingReceiptDto(
    string PostingId,
    PrivateCapitalScopeDto Scope,
    string CommitmentId,
    string CallId,
    string InstallmentId,
    string Kind,
    decimal Amount,
    Guid JournalEntryId,
    DateOnly EffectiveDate,
    string? FundingReceiptId = null,
    string? BankLineId = null);

/// <summary>One authority-backed view of commitment, call and posted cash progress.</summary>
public sealed record PrivateCapitalWorkbenchDto(
    PrivateCapitalScopeDto Scope,
    IReadOnlyList<PrivateCapitalCommitmentDto> Commitments,
    IReadOnlyList<PrivateCapitalCallDto> Calls,
    IReadOnlyList<PrivateCapitalPostingReceiptDto> Receipts,
    DateTimeOffset GeneratedAtUtc)
{
    public IReadOnlyList<PrivateCapitalNoticeSummaryDto> Notices { get; init; } = [];
}

/// <summary>Projection of an existing governed notice run and its delivery state.</summary>
public sealed record PrivateCapitalNoticeSummaryDto(
    string RunId,
    string CallId,
    string InvestorId,
    string GovernanceState,
    string? PackageId,
    string DeliveryState,
    string Route,
    string CommitmentId);

/// <summary>Create a new draft revision; expected revision zero creates a new commitment.</summary>
public sealed record CreatePrivateCapitalCommitmentRequest(
    string CommitmentId,
    string InvestorId,
    string CapitalAccountId,
    decimal TotalCommitment,
    DateOnly CommitmentDate,
    DateOnly EffectiveFrom,
    DateOnly? InvestmentPeriodEndDate,
    IReadOnlyList<string> EvidenceLinks,
    int ExpectedRevision = 0,
    bool IsActive = true);

/// <summary>Review the exact retained commitment revision.</summary>
public sealed record ApprovePrivateCapitalCommitmentRequest(int Revision);

/// <summary>Choose approved commitments; monetary source terms always resolve on the server.</summary>
public sealed record PreviewPrivateCapitalCallRequest(
    string CallId,
    decimal AmountToCall,
    DateOnly NoticeDate,
    DateOnly DueDate,
    string PeriodId,
    IReadOnlyList<string> CommitmentIds,
    string? Purpose = null);

/// <summary>Read-only allocation preview; a blocked preview cannot be retained for issuance.</summary>
public sealed record PrivateCapitalCallPreviewDto(
    PrivateCapitalCallDto? Call,
    bool IsReady,
    IReadOnlyList<string> Blockers);

/// <summary>Retain exactly the preview the operator reviewed.</summary>
public sealed record RetainPrivateCapitalCallRequest(
    PreviewPrivateCapitalCallRequest Preview,
    string ExpectedPlanHash);
