using System.Text.Json.Serialization;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.Ledger;

/// <summary>Operator assertions for a paydown; scope authority, lots and amounts are resolved by the server.</summary>
public sealed record PrepareFactorPaydownRequestDto(
    Guid LedgerBookId,
    Guid SecurityId,
    Guid BookPositionId,
    Guid PeriodId,
    long ExpectedPeriodVersion,
    AccountingBasisKindDto AccountingBasis,
    string FundProfileId,
    DateOnly EffectiveDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal PriorFactor,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal NewFactor,
    string SourceDomain,
    string SourceEntityId,
    string SourceContentHash,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<RetainedEvidenceIdentityDto> FactorEvidence,
    string CashAccountPath,
    string CarryingAccountPath,
    string RealizedGainAccountPath,
    string RealizedLossAccountPath,
    Guid? CorrelationId = null);

/// <summary>Read-only preview. Decimal component strings preserve exact workstation display.</summary>
public sealed record FactorPaydownPreparationDto(
    OpenLotFactorPaydownInstructionDto Instruction,
    AssetLotMutationInstructionDto LotMutation,
    ProjectAssetAccountingEventRequestDto ProjectRequest,
    IReadOnlyDictionary<string, string> Components,
    string PreparationFingerprint);

/// <summary>Rechecks the reviewed preview before retaining a governed draft; never approves or posts.</summary>
public sealed record CreateFactorPaydownDraftRequestDto(
    PrepareFactorPaydownRequestDto Request,
    string ExpectedPreparationFingerprint,
    string Description);

/// <summary>Bytes retained by an actual authenticated independent approval command.</summary>
public sealed record AssetAccountingApprovalReceiptDto(
    string ApprovalId,
    string Approver,
    DateTimeOffset ApprovedAtUtc,
    string CandidateFingerprint,
    string SubjectId,
    string CanonicalContentBase64,
    string ContentHashSha256);

/// <summary>An exact original posting command retained before any canonical financial side effect.</summary>
public sealed record AssetAccountingPostIntentDto(
    string OriginalCommandHash,
    JournalEntryLifecycleActionRequestDto OriginalCommand,
    PostPostingRuleJournalCandidateRequestDto CanonicalRequest,
    string VaultId,
    string DocumentId);

/// <summary>Caller-specific capability. Missing or unknown protocol remains read-only.</summary>
public sealed record AssetAccountingDraftCapabilitiesDto(
    int ProtocolVersion,
    Guid JournalEntryId,
    int DraftVersion,
    string FundProfileId,
    Guid LedgerBookId,
    Guid PeriodId,
    string CandidateFingerprint,
    bool CanApprove,
    bool CanPost,
    string? ApproveBlockedReason,
    string? PostBlockedReason,
    AssetAccountingApprovalReceiptDto? Approval,
    bool HasPendingPost,
    JournalEntryLifecycleActionRequestDto? PendingPostCommand,
    PostedLedgerJournalEntryResultDto? PostedJournal,
    string? AuthenticatedActor = null);
