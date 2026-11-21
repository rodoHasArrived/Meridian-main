using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using static Meridian.Ui.Shared.Services.AmountPassportFacts;

namespace Meridian.Ui.Shared.Services;

internal sealed class AmountPassportScopeMismatchException : Exception;

public sealed partial class PostedLedgerAmountProvenanceService
{
    private async Task<AmountPassportDto> BuildPassportAsync(LedgerJournalEntryRecord record, LedgerEntry line,
        LedgerAmountProofDto proof, LedgerAccountingPeriod period, DateTimeOffset evaluatedAt, bool stale, CancellationToken ct)
    {
        var reference = PostedReference(record, line, proof.Scope, proof.Currency);
        var debit = line.Debit > 0m;
        var calculation = record.RuleId is { } rule
            ? $"Retained {(debit ? "debit" : "credit")} posted under rule {rule} ({record.RuleVersion ?? "version unavailable"})."
            : $"Read the retained functional {(debit ? "debit" : "credit")} from the immutable journal line.";
        var exchangeRate = line.Currency is { } fx
            ? $"{Exact(debit ? fx.TransactionDebit : fx.TransactionCredit)} {fx.TransactionCurrency} × retained rate {Exact(fx.FxRateToFunctional)} to {fx.FunctionalCurrency}."
            : "No separate exchange-rate detail is recorded on this functional posting.";
        var support = EvidenceLinks(proof);
        var supported = proof.Status == EvidenceStatusDto.Ready && support.Length > 0;
        var evidenceAt = proof.Evidence.Where(item => item.Route is not null).Max(item => item.RetainedAt);
        var freshness = new AmountPassportFreshnessDto(stale ? "stale" : supported ? "current" : "review-required",
            evaluatedAt, LedgerEvidencePolicy, stale ? "Retained evidence freshness requires review."
                : supported ? "Scoped source bytes, review and expiry were verified."
                : "Complete supporting evidence could not be verified.", evidenceAt);
        var (history, historyAvailable) = await ReadHistoryAsync(record, line, proof.Scope, ct).ConfigureAwait(false);
        var journal = Journal("journal", record.Entry.JournalEntryId, record.PeriodId, "Posted journal");
        return Build(reference, $"{line.Account.Name} {(debit ? "debit" : "credit")}", proof.Amount,
            Date(record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime)),
            calculation, exchangeRate, freshness,
            new("allowed", "read-only", period.Status == "Open" ? "Posted amounts are immutable. Corrections use a new journal."
                : "This period is closed. The original posting remains available for inspection.", period.Status),
            [
                Component("scope", true, "Exact fund, book, period, basis, date and journal revision."),
                Component("calculation", record.RuleId is null || record.RuleVersion is not null, calculation),
                Component("freshness", supported && !stale, freshness.Detail),
                Component("source", supported, supported ? "Verified retained sources support this posting." : "Verified retained supporting sources are incomplete."),
                Component("evidence", supported, supported ? "Guarded evidence links are available." : "One or more supporting evidence links are unavailable."),
                Component("history", historyAvailable, historyAvailable ? "Retained journal history was checked." : "A related journal could not be resolved in this book and scope."),
                Component("permissions", true, "Inspection is allowed. Posted amounts are read-only.")
            ],
            [journal, new("journal-line", line.EntryId.ToString("D"), "Posting line")], support, history,
            proof.Warnings, proof.Status == EvidenceStatusDto.Blocked)
            with
        { AccountingPreparerAttestations = proof.AccountingPreparerAttestations };
    }

    private async Task<(AmountPassportRecordDto[] History, bool Available)> ReadHistoryAsync(
        LedgerJournalEntryRecord record, LedgerEntry line, LedgerAmountScopeDto scope, CancellationToken ct)
    {
        var journalId = record.Entry.JournalEntryId;
        var selectedScope = LedgerEndpoints.ResolveLineDimensions(record, line);
        var history = new List<AmountPassportRecordDto> { Journal("original", journalId, record.PeriodId, "Retained posting") };
        var available = true;
        if (record.SourceJournalEntryId is { } sourceId)
        {
            var sources = await journals!.QueryAsync(new(LedgerBookId: scope.LedgerBookId, JournalEntryId: sourceId), ct)
                .ConfigureAwait(false);
            var original = sources.Where(candidate => candidate.Entry.JournalEntryId == sourceId && InScope(candidate)).ToArray();
            if (original.Length == 1)
                history.Add(Journal("predecessor", sourceId, original[0].PeriodId, "Predecessor journal"));
            else
                available = false;
        }

        var successors = await journals!.QueryAsync(new(LedgerBookId: scope.LedgerBookId, SourceJournalEntryId: journalId), ct)
            .ConfigureAwait(false);
        history.AddRange(successors.Where(candidate => candidate.SourceJournalEntryId == journalId && InScope(candidate))
            .OrderBy(candidate => candidate.GlobalSequence)
            .Select(candidate => Journal("related-journal", candidate.Entry.JournalEntryId, candidate.PeriodId, "Related journal referencing this posting")));
        return (history.ToArray(), available);

        bool InScope(LedgerJournalEntryRecord candidate) => candidate.AccountingBasis == record.AccountingBasis &&
            candidate.Entry.Lines.Select(item => LedgerEndpoints.ResolveLineDimensions(candidate, item))
                .Any(dimensions => dimensions?.EntityId == selectedScope?.EntityId &&
                    dimensions?.OrganizationId == selectedScope?.OrganizationId &&
                    (dimensions?.FundId is null || dimensions.FundId == scope.FundProfileId));
    }
}
