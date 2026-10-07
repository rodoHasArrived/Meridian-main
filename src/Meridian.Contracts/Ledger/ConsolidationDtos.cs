using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Ledger;

/// <summary>First slice: two directly wholly owned entities, Primary basis, one functional currency.</summary>
public sealed record ConsolidationRequestDto(
    Guid OrganizationId, Guid OwnershipRootId, Guid EliminationBookId, Guid PeriodId, DateOnly AsOf);

public sealed record ConsolidationBookVersionDto(Guid LedgerBookId, long MaxSequence, int JournalCount);

public sealed record ConsolidationEvidenceDto(
    ConsolidationRequestDto Request,
    string ScopeKey,
    string SourceFingerprint,
    IReadOnlyList<ConsolidationBookVersionDto> BookVersions,
    string PerimeterFingerprint,
    string RuleVersion,
    IReadOnlyList<Guid> PriorPostedJournalIds,
    IReadOnlyList<ManualJournalEntryLineDto> ExpectedLines,
    string? Currency = null,
    IReadOnlyList<string>? EntityIds = null,
    IReadOnlyList<OwnershipLinkDto>? OwnershipEvidence = null,
    IReadOnlyList<ConsolidationSourceDto>? Sources = null);

public interface IConsolidationDraftGuard
{
    Task ValidateCurrentAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default);
}

public sealed record ConsolidationSourceDto(
    Guid LedgerBookId, Guid JournalEntryId, Guid LineId, string EntityId, string? CounterpartyId,
    string AccountPath, decimal Debit, decimal Credit, DateOnly EffectiveDate, string DrillThrough);

public sealed record ConsolidationBalanceDto(
    string AccountPath, string AccountType, decimal GrossBalance, decimal ProposedEliminations,
    decimal PostedEliminations, decimal ConsolidatedBalance, decimal PreviewBalance,
    IReadOnlyList<ConsolidationSourceDto> Sources);

public sealed record ConsolidationMatchDto(
    string PostingEntityId, string CounterpartyId, decimal Receivable, decimal Payable,
    decimal MatchedAmount, decimal UnmatchedReceivable, decimal UnmatchedPayable,
    IReadOnlyList<ConsolidationSourceDto> Sources);

public sealed record ConsolidationDraftSummaryDto(
    Guid JournalEntryId, string Status, bool RequiresRenewedReview, Guid? AdjustsJournalEntryId,
    IReadOnlyList<ManualJournalEntryLineDto> Lines);

public sealed record ConsolidationViewDto(
    ConsolidationRequestDto Request, string FundProfileId, string Currency, string ScopeLimitation,
    IReadOnlyList<string> EntityIds, IReadOnlyList<Guid> OwnershipLinkIds,
    IReadOnlyList<ConsolidationBalanceDto> Balances, IReadOnlyList<ConsolidationMatchDto> Matches,
    IReadOnlyList<ConsolidationDraftSummaryDto> Drafts, IReadOnlyList<string> Blockers,
    string SourceFingerprint);
