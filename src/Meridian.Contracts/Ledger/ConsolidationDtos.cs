using Meridian.Contracts.FundStructure;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

/// <summary>First slice: two directly wholly owned entities, Primary basis, one functional currency.</summary>
public sealed record ConsolidationRequestDto(
    Guid OrganizationId, Guid OwnershipRootId, Guid EliminationBookId, Guid PeriodId, DateOnly AsOf);

public sealed record ConsolidationRuleEvidenceDto(string RuleId, string RuleVersion);

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
    IReadOnlyList<ConsolidationSourceDto>? Sources = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IntercompanyAccountBindingDto>? AccountBindings { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TranslationFingerprint { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TranslationEvidenceJson { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ConsolidationRuleEvidenceDto>? ParticipatingRules { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<LegacyIntercompanyAttributionDto>? LegacyAttributions { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AccountingRulePackReferenceDto? RulePackReference { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ConsolidationTransitionLineageDto>? TransitionLineage { get; init; }
}

/// <summary>
/// Checks that a consolidation draft's source authority is still current before it proceeds.
/// </summary>
public interface IConsolidationDraftGuard
{
    Task ValidateCurrentAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default);
}

public sealed record ConsolidationSourceDto(
    Guid LedgerBookId, Guid JournalEntryId, Guid LineId, string EntityId, string? CounterpartyId,
    string AccountPath, decimal Debit, decimal Credit, DateOnly EffectiveDate, string DrillThrough)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalAccountPath { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CounterpartyDisposition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Currency { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChartAccountNodeId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountBindingRevisionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountingArrangementId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountingArrangementRevisionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? IntercompanyAgreementId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IntercompanyAgreementVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntercompanyAgreementPayloadHash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntercompanyTransactionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? TransactionDebit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? TransactionCredit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TransactionCurrency { get; init; }
}

public sealed record ConsolidationBalanceDto(
    string AccountPath, string AccountType, decimal GrossBalance, decimal ProposedEliminations,
    decimal PostedEliminations, decimal ConsolidatedBalance, decimal PreviewBalance,
    IReadOnlyList<ConsolidationSourceDto> Sources);

public sealed record ConsolidationMatchDto(
    string PostingEntityId, string CounterpartyId, decimal Receivable, decimal Payable,
    decimal MatchedAmount, decimal UnmatchedReceivable, decimal UnmatchedPayable,
    IReadOnlyList<ConsolidationSourceDto> Sources)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountingArrangementId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountingArrangementRevisionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Treatment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MatchingCurrency { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? MatchedTransactionAmount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? UnmatchedFirstTransactionAmount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? UnmatchedSecondTransactionAmount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? TranslationDifference { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntercompanyTransactionId { get; init; }
}

public sealed record ConsolidationDraftSummaryDto(
    Guid JournalEntryId, string Status, bool RequiresRenewedReview, Guid? AdjustsJournalEntryId,
    IReadOnlyList<ManualJournalEntryLineDto> Lines);

public sealed record ConsolidationViewDto(
    ConsolidationRequestDto Request, string FundProfileId, string Currency, string ScopeLimitation,
    IReadOnlyList<string> EntityIds, IReadOnlyList<Guid> OwnershipLinkIds,
    IReadOnlyList<ConsolidationBalanceDto> Balances, IReadOnlyList<ConsolidationMatchDto> Matches,
    IReadOnlyList<ConsolidationDraftSummaryDto> Drafts, IReadOnlyList<string> Blockers,
    string SourceFingerprint, bool CanCreateDrafts = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TranslationFingerprint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, decimal>? TranslationReserve { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, decimal>? TranslationDifferences { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, decimal>? RoundingDifferences { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? EntityNames { get; init; }
}
