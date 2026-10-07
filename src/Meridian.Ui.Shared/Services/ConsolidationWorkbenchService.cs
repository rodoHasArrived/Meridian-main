using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Shared browser/desktop projection and intake into the existing journal approval queue.</summary>
public sealed class ConsolidationWorkbenchService(ConsolidationService consolidation,
    IManualJournalEntryDraftStore drafts, ManualJournalEntryWorkbenchService workbench,
    IAccountingConfigurationService configuration)
{
    public async Task<ConsolidationViewDto> GetAsync(ConsolidationRequestDto request, string? tenant,
        string? company, CancellationToken ct = default)
    {
        var calculation = await consolidation.CalculateAsync(request, ct).ConfigureAwait(false);
        var retained = await drafts.ListAsync(calculation.Book.FundProfileId, request.EliminationBookId,
            ct, tenant, company).ConfigureAwait(false);
        return Project(calculation, retained);
    }

    public async Task<ConsolidationViewDto> CreateDraftAsync(ConsolidationRequestDto request, string actor,
        string? tenant, string? company, CancellationToken ct = default)
    {
        var calculation = await consolidation.CalculateAsync(request, ct).ConfigureAwait(false);
        var draft = await consolidation.BuildDraftAsync(calculation, actor, tenant, company, ct).ConfigureAwait(false);
        if (draft is not null)
        {
            var existing = await drafts.GetAsync(draft.FundProfileId, draft.JournalEntryId, ct, tenant, company).ConfigureAwait(false);
            if (existing is null)
            {
                var workspace = await configuration.GetWorkspaceAsync(draft.FundProfileId, draft.LedgerBookId,
                    ct, tenant, company).ConfigureAwait(false);
                foreach (var line in draft.Lines)
                {
                    var account = workspace.ChartOfAccounts.SingleOrDefault(x => x.Path == line.AccountPath);
                    if (account is null || account.IsArchived || account.AccountName != line.AccountPath ||
                        account.Symbol is not null || account.FinancialAccountId is not null ||
                        !string.Equals(account.AccountType, line.AccountPath == ConsolidationService.ReceivableAccount
                            ? "Asset" : "Liability", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The elimination chart must contain the supported unscoped intercompany account paths with identical account names; Symbol and FinancialAccountId must be absent.");
                }
                await workbench.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(draft, actor,
                    CorrelationId: draft.TreasuryContext!.IdempotencyKey, TenantId: tenant, CompanyId: company), ct).ConfigureAwait(false);
            }
        }
        return await GetAsync(request, tenant, company, ct).ConfigureAwait(false);
    }

    public static ConsolidationViewDto Project(ConsolidationCalculation calculation,
        IReadOnlyList<ManualJournalEntryDraftDto> retained)
    {
        var rows = calculation.SourceRecords.SelectMany(x => x.Entry.Lines).Select(x => x.Account)
            .Concat(calculation.Posted.SelectMany(x => x.Entry.Lines).Select(x => x.Account))
            .GroupBy(x => new { x.Name, x.AccountType }).Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.Ordinal).Select(account =>
            {
                var factor = account.AccountType is LedgerAccountType.Liability or LedgerAccountType.Equity or LedgerAccountType.Revenue ? -1m : 1m;
                var gross = calculation.SourceRecords.SelectMany(x => x.Entry.Lines).Where(x => x.Account.Name == account.Name && x.Account.AccountType == account.AccountType).Sum(x => x.Debit - x.Credit) * factor;
                var posted = calculation.Posted.SelectMany(x => x.Entry.Lines).Where(x => x.Account.Name == account.Name && x.Account.AccountType == account.AccountType).Sum(x => x.Debit - x.Credit) * factor;
                var proposed = calculation.Blockers.Count > 0 ? 0m : calculation.ProposedLines.Where(x => x.AccountPath == account.Name)
                    .Sum(x => x.Side == AccountingTemplateLineSideDto.Debit ? x.Amount : -x.Amount) * factor;
                var sourceIds = calculation.SourceRecords.SelectMany(x => x.Entry.Lines)
                    .Where(x => x.Account.Name == account.Name && x.Account.AccountType == account.AccountType)
                    .Select(x => x.EntryId).ToHashSet();
                var postedSources = calculation.Posted.SelectMany(record => record.Entry.Lines
                    .Where(x => x.Account.Name == account.Name && x.Account.AccountType == account.AccountType)
                    .Select(line => new ConsolidationSourceDto(calculation.Book.LedgerBookId, record.Entry.JournalEntryId,
                        line.EntryId, line.Dimensions?.EntityId ?? string.Empty, line.Dimensions?.CounterpartyId,
                        line.Account.Name, line.Debit, line.Credit,
                        record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime),
                        $"journal:{record.Entry.JournalEntryId:D}/line:{line.EntryId:D}")));
                return new ConsolidationBalanceDto(account.Name, account.AccountType.ToString(), gross, proposed,
                    posted, gross + posted, gross + posted + proposed,
                    calculation.Sources.Where(x => sourceIds.Contains(x.LineId)).Concat(postedSources).ToArray());
            }).ToArray();
        var summaries = retained.Select(x => (Draft: x, Evidence: ConsolidationService.ReadEvidence(x.ConsolidationEvidenceJson)))
            .Where(x => x.Evidence?.ScopeKey == calculation.Evidence.ScopeKey ||
                calculation.Posted.Any(posted => posted.Entry.JournalEntryId == x.Draft.JournalEntryId))
            .Select(x => new ConsolidationDraftSummaryDto(x.Draft.JournalEntryId,
                calculation.Posted.Any(posted => posted.Entry.JournalEntryId == x.Draft.JournalEntryId) ? "Posted" : x.Draft.Status.ToString(),
                !calculation.Posted.Any(posted => posted.Entry.JournalEntryId == x.Draft.JournalEntryId) && x.Draft.Status is not (ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.Rejected or ManualJournalEntryStatusDto.Reversed) &&
                (x.Evidence!.SourceFingerprint != calculation.Evidence.SourceFingerprint ||
                 ConsolidationService.Hash(x.Draft.Lines) != ConsolidationService.Hash(calculation.ProposedLines)),
                x.Draft.RebookedFromJournalEntryId, x.Draft.Lines))
            .Concat(calculation.Posted.Where(posted => retained.All(draft => draft.JournalEntryId != posted.Entry.JournalEntryId))
                .Select(posted => new ConsolidationDraftSummaryDto(posted.Entry.JournalEntryId, "Posted", false,
                    posted.SourceJournalEntryId, ConsolidationService.ReadEvidence(posted.Entry.Metadata.Tags![ConsolidationService.EvidenceTag])!.ExpectedLines)))
            .ToArray();
        return new ConsolidationViewDto(calculation.Request, calculation.Book.FundProfileId,
            calculation.Perimeter.Currency, calculation.Perimeter.ScopeLimitation +
            " Primary basis; one Primary book per entity; dedicated elimination book; receivable/payable rule w10-v1. No FX, minority interests, investment/equity or income/expense elimination.",
            calculation.Perimeter.Entities.Select(x => x.EntityId.ToString("D")).ToArray(),
            calculation.Perimeter.OwnershipEvidence.Select(x => x.OwnershipLinkId).ToArray(), rows,
            calculation.Matches, summaries, calculation.Blockers, calculation.Evidence.SourceFingerprint);
    }
}
