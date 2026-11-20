using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class LedgerReportingAuthoritativeSource
{
    public const string FiscalYearCloseReceiptParameter = "fiscalYearCloseReceiptId";
    public const string BookFxSettlementSetParameter = "bookFxSettlementSetId";
    public const string ComparisonPeriodParameter = "comparisonPeriodId";

    private async Task<StatementControlScope> ResolveStatementControlScopeAsync(
        ReportingRunParametersDto parameters,
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        DateTimeOffset cutoff,
        IReadOnlyList<LedgerJournalEntryRecord> journals,
        string tenantId,
        string companyId,
        CancellationToken ct)
    {
        var periodStart = period.StartDate;
        var fxJournals = ResolveCashRevaluationSources(journals, periodStart, cutoff);
        var definition = new LedgerStatementControlDefinition(LedgerStatementControlDefinition.CurrentVersion,
            book.AccountingBasis)
        { CashFxRevaluationJournalIds = fxJournals };
        if (parameters.TemplateParameters.TryGetValue(BookFxSettlementSetParameter, out var settlementValue))
        {
            var settlement = await RequireSettlementSetAsync(settlementValue, parameters.AsOfDate, book, journals,
                tenantId, companyId, ct).ConfigureAwait(false);
            definition = definition with
            {
                BookFxSettlementSetId = settlement.Plan.Input.SetId,
                BookFxSettlementPlanHashSha256 = settlement.Plan.PlanHashSha256
            };
        }
        var annual = parameters.TemplateParameters.TryGetValue(FiscalYearCloseReceiptParameter, out var receiptValue);
        var comparative = parameters.TemplateParameters.TryGetValue(ComparisonPeriodParameter, out var comparisonValue);
        if (annual && comparative)
            throw Unavailable("A comparative period is available for individual period control packs. Annual comparisons require two retained fiscal closes.");
        if (parameters.Finality != ReportingFinalityDto.Final)
        {
            if (annual)
                throw Unavailable("Fiscal-year statement controls require a final reporting run and a retained fiscal close.");
            if (comparative)
                throw Unavailable("Comparative statement controls require a final reporting run and two retained period closes.");
            return new(periodStart, definition);
        }

        if (parameters.AsOfDate != period.EndDate || _journalStore is not IAtomicLedgerPeriodCloseStore snapshots)
            throw Unavailable("Final statement controls require the selected period's exact cutoff and retained hard-close snapshot.");

        try
        {
            var snapshot = await snapshots.GetCloseSnapshotAsync(period.PeriodId, period.Version, ct).ConfigureAwait(false)
                ?? throw Unavailable("The selected hard-close version has no retained journal snapshot.");
            var payload = snapshot.VerifyIntegrity();
            if (snapshot.LedgerBookId != book.LedgerBookId || payload.Period.Version != period.Version
                || payload.Period.EndDate != period.EndDate || payload.Period.StartDate != period.StartDate
                || !Sha256Digest.FixedEquals(snapshot.ContentHashSha256,
                    LedgerPeriodCloseSnapshot.Capture(payload.Period, payload.CloseEvent, journals).ContentHashSha256))
                throw Unavailable("The current scoped journal differs from the retained hard-close population. Review the latest close before certification.");

            definition = definition with
            {
                CloseSnapshotVersion = snapshot.PeriodVersion,
                CloseSnapshotHashSha256 = snapshot.ContentHashSha256
            };
            if (comparative)
            {
                var (priorDefinition, priorSnapshot) = await RequireComparativeCloseAsync(snapshots, book, period,
                    comparisonValue!, ct).ConfigureAwait(false);
                return new(periodStart, definition with { Comparison = priorDefinition }, priorSnapshot);
            }
            if (!annual)
                return new(periodStart, definition);

            if (!Guid.TryParse(receiptValue, out var receiptId) || receiptId == Guid.Empty
                || _journalStore is not IFiscalYearCloseReceiptStore fiscalStore)
                throw Unavailable("Select an exact retained fiscal-year close receipt in the same ledger book.");
            var receipt = await fiscalStore.GetFiscalYearCloseReceiptAsync(book.LedgerBookId, period.FiscalYear,
                receiptId, ct).ConfigureAwait(false)
                ?? throw Unavailable("The selected fiscal-year close receipt is unavailable.");
            var fiscal = receipt.Plan;
            var finalPeriod = fiscal.Periods.OrderByDescending(static item => item.EndDate).FirstOrDefault();
            if (!fiscal.IsReady || receipt.ReceiptId != receiptId || fiscal.LedgerBookId != book.LedgerBookId
                || fiscal.FiscalYear != period.FiscalYear || fiscal.FiscalYearStart is not { } fiscalYearStart
                || fiscal.FiscalYearEnd != period.EndDate || finalPeriod?.PeriodId != period.PeriodId
                || finalPeriod.SnapshotVersion != snapshot.PeriodVersion
                || !Sha256Digest.FixedEquals(finalPeriod.SnapshotHashSha256, snapshot.ContentHashSha256)
                || receipt.AccountingBasis != book.AccountingBasis
                || !string.Equals(receipt.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)
                || !Sha256Digest.IsCanonical(fiscal.PlanHashSha256))
                throw Unavailable("The retained fiscal-year receipt does not match the current book, basis, currency and final hard-close snapshot.");

            return new(fiscalYearStart, definition with
            {
                FiscalYearCloseReceiptId = receipt.ReceiptId,
                FiscalYearClosePlanHashSha256 = fiscal.PlanHashSha256,
                CashFxRevaluationJournalIds = ResolveCashRevaluationSources(journals, fiscalYearStart, cutoff)
            });
        }
        catch (Exception exception) when (exception is InvalidDataException or LedgerValidationException or JsonException or NotSupportedException)
        {
            throw Unavailable($"The retained statement control source could not be verified: {exception.Message}");
        }
    }

    private async Task<(LedgerStatementComparisonDefinition Definition, LedgerPeriodCloseSnapshot Snapshot)> RequireComparativeCloseAsync(
        IAtomicLedgerPeriodCloseStore snapshots, LedgerBookRecord book, LedgerAccountingPeriod current,
        string comparisonValue, CancellationToken ct)
    {
        if (!Guid.TryParse(comparisonValue, out var priorPeriodId) || priorPeriodId == Guid.Empty)
            throw Unavailable("Select an exact earlier hard-closed accounting period for comparison.");
        var prior = await _journalStore.GetPeriodAsync(priorPeriodId, ct).ConfigureAwait(false);
        if (prior is null || prior.LedgerBookId != book.LedgerBookId || prior.Status != "HardClosed" || prior.EndDate >= current.StartDate)
            throw Unavailable("The comparative period must be hard-closed, earlier, and in the same ledger book.");
        var snapshot = await snapshots.GetCloseSnapshotAsync(prior.PeriodId, prior.Version, ct).ConfigureAwait(false)
            ?? throw Unavailable("The comparative hard-close version has no retained journal snapshot.");
        var payload = snapshot.VerifyIntegrity();
        if (snapshot.PeriodId != prior.PeriodId || snapshot.LedgerBookId != book.LedgerBookId || snapshot.PeriodVersion != prior.Version
            || payload.Period.StartDate != prior.StartDate || payload.Period.EndDate != prior.EndDate || prior.StartDate > prior.EndDate)
            throw Unavailable("The retained comparative snapshot does not match its exact period and book.");
        var fxJournals = ResolveCashRevaluationSources(payload.Entries, prior.StartDate, EndOfUtcDate(prior.EndDate));
        return (new(prior.PeriodId, prior.StartDate, prior.EndDate, prior.Version, snapshot.ContentHashSha256, fxJournals), snapshot);
    }

    private async Task<BookFxSettlementSetDto> RequireSettlementSetAsync(
        string value, DateOnly asOf, LedgerBookRecord book, IReadOnlyList<LedgerJournalEntryRecord> journals,
        string tenantId, string companyId, CancellationToken ct)
    {
        if (!Guid.TryParse(value, out var setId) || setId == Guid.Empty || _journalStore is not ILedgerBookFxSettlementStore store)
            throw Unavailable("Select an exact retained book FX settlement set.");
        var settlement = await store.GetBookFxSettlementSetAsync(setId, tenantId, companyId, ct).ConfigureAwait(false)
            ?? throw Unavailable("The selected book FX settlement set is unavailable.");
        var plan = settlement.Plan;
        BookFxSettlementLegDto[] expectedLegs = book.AccountingBasis switch
        {
            AccountingBasisKindDto.Gaap when plan.Input.GaapBookId == book.LedgerBookId
                => [BookFxSettlementLegDto.GaapTrade, BookFxSettlementLegDto.GaapSettlement],
            AccountingBasisKindDto.Cash when plan.Input.CashBookId == book.LedgerBookId
                => [BookFxSettlementLegDto.CashSettlement],
            _ => []
        };
        var receipts = settlement.Receipts.Where(receipt => receipt.LedgerBookId == book.LedgerBookId).ToArray();
        if (!settlement.IsComplete || plan.Input.SetId != setId || plan.Input.SettlementDate > asOf
            || plan.EntityId != book.FundStructureNodeId || !SameRequired(plan.FundProfileId, book.FundProfileId)
            || !SameRequired(plan.FunctionalCurrency, book.BaseCurrency) || !Sha256Digest.IsCanonical(plan.PlanHashSha256)
            || expectedLegs.Length == 0 || !receipts.Select(receipt => receipt.Leg).Order().SequenceEqual(expectedLegs.Order())
            || receipts.Any(receipt => !Sha256Digest.FixedEquals(receipt.PlanHashSha256, plan.PlanHashSha256)
                || !journals.Any(journal => journal.Entry.JournalEntryId == receipt.JournalEntryId
                    && journal.PeriodId == receipt.PeriodId && journal.GlobalSequence == receipt.JournalSequence)))
            throw Unavailable("The selected book FX settlement set is incomplete or outside the retained report population and cutoff.");
        return settlement;
    }

    private static IReadOnlyList<Guid> ResolveCashRevaluationSources(
        IReadOnlyList<LedgerJournalEntryRecord> journals, DateOnly start, DateTimeOffset cutoff)
    {
        var after = StartOfUtcDate(start);
        var currencyEffects = new List<Guid>();
        foreach (var record in journals)
        {
            var journal = record.Entry;
            if (journal.Timestamp < after || journal.Timestamp > cutoff
                || !journal.Lines.Any(line => line.Account.AccountType == LedgerAccountType.Asset
                    && line.Account.Name.StartsWith("Cash", StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                if (!FxRevaluationCalculation.IsPriorRevaluation(journal.Metadata))
                    continue;
                var proof = journal.Metadata.Tags?.GetValueOrDefault(RecurringJournalEvidenceGuard.EvidenceTag);
                var evidence = proof is null ? journal.Metadata.EvidenceReferences
                    : RecurringJournalEvidenceGuard.Deserialize(proof)?.SourceEvidence;
                if (RecurringJournalEvidenceGuard.ValidateSources(evidence) is not null)
                    throw new InvalidDataException("The cash carrying-value effect lacks retained rate evidence.");
                currencyEffects.Add(journal.JournalEntryId);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or JsonException)
            {
                throw Unavailable($"FX revaluation provenance for journal '{journal.JournalEntryId:D}' is unavailable: {exception.Message}");
            }
        }

        return currencyEffects.Order().ToArray();
    }

    private sealed record StatementControlScope(DateOnly PeriodStart, LedgerStatementControlDefinition Definition,
        LedgerPeriodCloseSnapshot? ComparativeClose = null);
}
