namespace Meridian.Contracts.FundStructure;

/// <summary>Advisory, source-backed coverage; amounts are exact invariant decimal text in each declared currency.</summary>
public sealed record IntercompanyAgreementJournalLineDto(
    Guid JournalEntryId, Guid LineId, Guid PeriodId, long Sequence, DateOnly AccountingDate,
    Guid PostingEntityId, Guid? CounterpartyId, string AccountName, string Description,
    string FunctionalCurrency, string Debit, string Credit,
    string TransactionCurrency, string TransactionDebit, string TransactionCredit,
    Guid? IntercompanyAgreementId, int? IntercompanyAgreementVersion,
    string CoverageState, string? Warning, string SourceReference);

public sealed record IntercompanyAgreementJournalLinesDto(
    Guid AgreementId, Guid LedgerBookId, DateOnly AsOf, string CoverageState,
    IReadOnlyList<IntercompanyAgreementJournalLineDto> Lines, string? NextCursor);
