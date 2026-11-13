using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private static bool HasAgreementReferences(ManualJournalEntryDraftDto draft)
        => draft.Lines.Any(line => line.Dimensions is { } dimensions &&
            (dimensions.IntercompanyAgreementId.HasValue || dimensions.IntercompanyAgreementVersion.HasValue));

    private async Task ValidateCurrentAgreementDraftAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        if (!HasAgreementReferences(draft))
            return;
        var configuration = await _configurationService.GetWorkspaceAsync(draft.FundProfileId, draft.LedgerBookId,
            ct, draft.TenantId, draft.CompanyId).ConfigureAwait(false);
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        await ValidateAgreementDraftAsync(draft, draft.Lines, BuildChartByPath(configuration.ChartOfAccounts), issues, ct).ConfigureAwait(false);
        if (issues.FirstOrDefault(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical) is { } issue)
            throw new InvalidOperationException(issue.Message);
    }

    private async Task ValidateAgreementMutationAsync(string operation, string fund, Guid journalEntryId,
        string? tenant, string? company, IReadOnlyList<ManualJournalMutationIntent> pending,
        ManualJournalMutationIntent? retained, CancellationToken ct)
    {
        var scope = RecoveryScope(fund, tenant, company);
        var draft = await _draftStore.GetAsync(NormalizeFundProfileId(fund), journalEntryId, ct, tenant, company).ConfigureAwait(false);
        if ((draft is null || !HasAgreementReferences(draft)) && !pending.Any(intent =>
                PendingMatchesScope(intent, scope, journalEntryId) &&
                intent.Before.OfType<ManualJournalEntryDraftDto>().Concat(intent.After).Any(HasAgreementReferences)))
            return;
        var committedReplay = retained is not null &&
            await VerifyCommittedPostingAsync(retained, requirePresent: false, ct).ConfigureAwait(false);
        if (!committedReplay && draft is not null && HasAgreementReferences(draft) &&
            operation is "submit" or "lifecycle-Submit" or "lifecycle-Approve" or "lifecycle-Post")
            await ValidateCurrentAgreementDraftAsync(draft, ct).ConfigureAwait(false);
        foreach (var intent in pending.Where(item => PendingMatchesScope(item, scope, journalEntryId)))
        {
            var source = intent.Before.OfType<ManualJournalEntryDraftDto>().Concat(intent.After).FirstOrDefault(HasAgreementReferences);
            if (source is null || await VerifyCommittedPostingAsync(intent, requirePresent: false, ct).ConfigureAwait(false))
                continue;
            if (intent.Posting is not null ||
                intent.After.Any(item => item.Status is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved))
                await ValidateCurrentAgreementDraftAsync(source, ct).ConfigureAwait(false);
        }
    }

    private async Task ValidateAgreementDraftAsync(ManualJournalEntryDraftDto draft,
        IReadOnlyList<ManualJournalEntryLineDto> lines,
        IReadOnlyDictionary<string, ChartOfAccountsNodeDto> chart,
        List<AccountingConfigurationValidationIssueDto> issues, CancellationToken ct)
    {
        var referenced = lines.Any(line => line.Dimensions is { } dimensions &&
            (dimensions.IntercompanyAgreementId.HasValue || dimensions.IntercompanyAgreementVersion.HasValue));
        foreach (var line in lines)
        {
            var dimensions = line.Dimensions;
            if (dimensions?.IntercompanyAgreementId.HasValue != true && dimensions?.IntercompanyAgreementVersion.HasValue != true &&
                !IsConsolidationDraft(draft) && chart.TryGetValue(line.AccountPath, out var account) &&
                (account.IntercompanyRole is not null ||
                 (line.AccountPath == "Assets:Intercompany Receivable" && account.AccountType == "Asset") ||
                 (line.AccountPath == "Liabilities:Intercompany Payable" && account.AccountType == "Liability")))
                issues.Add(Issue("manual-je.agreement-missing", AccountingConfigurationValidationSeverityDto.Warning,
                    "This intercompany line has no approved agreement reference. The book's retained control mode determines posting eligibility.",
                    line.LineId, "Select the approved agreement/version when available."));
        }
        if (!referenced || draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.Reversed or
            ManualJournalEntryStatusDto.Rebooked or ManualJournalEntryStatusDto.CloseLocked)
            return;
        try
        {
            if (_journalStore is not IIntercompanyAgreementJournalValidation validation ||
                draft.LedgerBookId is not { } bookId || !Guid.TryParse(draft.PeriodId, out var periodId))
                throw new LedgerValidationException("Authoritative agreement review requires a supported ledger writer and retained book/period.");
            var book = await _journalStore.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("The agreement review ledger book is unavailable.");
            // Functional amounts belong to the destination book; explicit retained transaction
            // amounts preserve legal agreement FX, while native drafts retain identity translation.
            if (!string.Equals(draft.Currency?.Trim(), book.BaseCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new LedgerValidationException("The journal functional currency must match the destination book.");
            var timestamp = ToAccountingTimestamp(draft.AccountingDate);
            var ledgerLines = lines.Select(line =>
            {
                if (!chart.TryGetValue(line.AccountPath, out var account) || account.IsArchived ||
                    !TryMapLedgerAccountType(account.AccountType, out var type))
                    throw new LedgerValidationException("Agreement review requires active supported chart accounts.");
                return new LedgerEntry(CreateDeterministicGuid("manual-je-line", draft.JournalEntryId.ToString("D"),
                        line.LineId, line.Side.ToString()), draft.JournalEntryId, timestamp,
                    new(account.AccountName, type, NormalizeOptional(line.LedgerAccountSymbol),
                        NormalizeOptional(line.LedgerAccountFinancialAccountId)),
                    line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                    line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m,
                    "Agreement review", ToLedgerLineDimensions(line.Dimensions),
                    line.TransactionCurrency is { } original
                        ? new LedgerEntryCurrency(original.Currency, draft.Currency,
                            line.Side == AccountingTemplateLineSideDto.Debit ? original.Amount : 0m,
                            line.Side == AccountingTemplateLineSideDto.Credit ? original.Amount : 0m,
                            original.FxRateToFunctional)
                        : string.Equals(line.Currency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(draft.Currency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)
                            ? new LedgerEntryCurrency(book.BaseCurrency, book.BaseCurrency,
                                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m, 1m)
                            : null);
            }).ToArray();
            var reviewLines = await MaterializeAgreementReversalCurrenciesAsync(draft, book, ledgerLines, ct).ConfigureAwait(false);
            var sourceId = draft.ReversalOfJournalEntryId ?? draft.RebookedFromJournalEntryId;
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), bookId, periodId,
                draft.AccountingDate, timestamp, "agreement-review:" + draft.JournalEntryId.ToString("D"),
                Intent: BuildManualPostingIntent(draft), SourceJournalEntryId: sourceId, LedgerBookId: bookId);
            var write = new LedgerJournalEntryWrite(new(draft.JournalEntryId, timestamp, "Agreement review", reviewLines,
                new(EffectiveDate: draft.AccountingDate)), bookId, periodId, AccountingBasis: draft.AccountingBasis,
                SourceJournalEntryId: sourceId, PostingCommand: command, LedgerBookId: bookId);
            await validation.ValidateIntercompanyAgreementReferencesAsync(write, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is LedgerValidationException or InvalidOperationException or
            ArgumentException or UnauthorizedAccessException or Npgsql.NpgsqlException)
        {
            issues.Add(Issue("manual-je.agreement-authority-invalid", AccountingConfigurationValidationSeverityDto.Critical,
                exception.Message, "intercompanyAgreement", "Repair the agreement reference and obtain renewed journal review."));
        }
    }

    private async Task<IReadOnlyList<LedgerEntry>> MaterializeAgreementReversalCurrenciesAsync(
        ManualJournalEntryDraftDto draft, LedgerBookRecord book, IReadOnlyList<LedgerEntry> lines, CancellationToken ct)
    {
        if (!HasAgreementReferences(draft) || draft.ReversalOfJournalEntryId is not { } sourceId)
            return lines;
        if (_journalStore is null || draft.LedgerBookId != book.LedgerBookId
            || draft.RebookedFromJournalEntryId is not null || draft.AccountingBasis != book.AccountingBasis)
            throw new LedgerValidationException("Agreement reversal requires its exact retained source book and accounting basis.");
        var records = await _journalStore.QueryAsync(new LedgerJournalEntryQuery(
            LedgerBookId: book.LedgerBookId, JournalEntryId: sourceId), ct).ConfigureAwait(false);
        if (records.Count != 1 || records[0].AccountingBasis != draft.AccountingBasis
            || records[0].Entry.Lines.Count != lines.Count)
            throw new LedgerValidationException("Agreement reversal requires the complete immutable same-book source journal.");

        var remaining = records[0].Entry.Lines.ToList();
        var materialized = new List<LedgerEntry>(lines.Count);
        foreach (var candidate in lines)
        {
            var matches = remaining.Select((source, index) => (Source: source, Index: index))
                .Where(item => item.Source.Account == candidate.Account
                    && item.Source.Debit == candidate.Credit && item.Source.Credit == candidate.Debit
                    && SamePostingDimensions(LedgerDimensionMapper.ToDto(item.Source.Dimensions),
                        LedgerDimensionMapper.ToDto(candidate.Dimensions))).ToArray();
            if (matches.Length == 0 || matches.Select(item => item.Source.Currency).Distinct().Count() != 1)
                throw new LedgerValidationException("Agreement reversal source legs are missing, changed or ambiguous; accounts, inverse amounts and every dimension must match.");
            var source = matches[0].Source;
            LedgerEntryCurrency? inverseCurrency = null;
            if (source.Currency is { } currency)
            {
                if (!string.Equals(currency.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                    throw new LedgerValidationException("Agreement reversal currency must retain the immutable source's functional book currency.");
                inverseCurrency = new(currency.TransactionCurrency, currency.FunctionalCurrency,
                    currency.TransactionCredit, currency.TransactionDebit, currency.FxRateToFunctional);
                if (!currency.IsFunctionalCurrency && candidate.Currency != inverseCurrency)
                    throw new LedgerValidationException("Agreement reversal requires the exact retained transaction currency, inverse amounts and original FX rate.");
            }
            var hasExplicitCurrency = draft.Lines.Any(line => line.TransactionCurrency is not null &&
                CreateDeterministicGuid("manual-je-line", draft.JournalEntryId.ToString("D"), line.LineId, line.Side.ToString()) == candidate.EntryId);
            if (hasExplicitCurrency && candidate.Currency != inverseCurrency)
                throw new LedgerValidationException("Agreement reversal transaction evidence differs from its immutable source inverse.");
            // Null historical evidence stays null. Native and explicit transaction evidence are
            // inverted exactly; neither a new currency nor a new rate is inferred from today's book.
            materialized.Add(new(candidate.EntryId, candidate.JournalEntryId, candidate.Timestamp, candidate.Account,
                candidate.Debit, candidate.Credit, candidate.Description, candidate.Dimensions, inverseCurrency));
            remaining.RemoveAt(matches[0].Index);
        }
        return materialized;
    }
}
