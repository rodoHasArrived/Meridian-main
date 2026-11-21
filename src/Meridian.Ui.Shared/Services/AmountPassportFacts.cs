using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Ui.Shared.Services;

internal static class AmountPassportFacts
{
    internal const string LedgerKind = "ledger-amount";
    internal const string PositionKind = "position-valuation";
    internal const string CurrencyUnit = "currency";
    internal const string LedgerEvidencePolicy = "Retained posting evidence review, integrity and expiry";
    private static readonly string[] RequiredComponents = ["scope", "source", "calculation", "freshness", "evidence", "history", "permissions"];

    internal static string Exact(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    internal static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    internal static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static AmountPassportReferenceDto PostedReference(LedgerJournalEntryRecord record,
        LedgerEntry line, LedgerAmountScopeDto scope, string baseCurrency)
    {
        var debit = line.Debit > 0m;
        var dimensions = LedgerEndpoints.ResolveLineDimensions(record, line);
        var subjectId = $"{record.Entry.JournalEntryId:D}:{line.EntryId:D}:{(debit ? "debit" : "credit")}";
        return new(new AmountPassportIdentityDto(LedgerKind, subjectId,
                new(scope.TenantId, scope.CompanyId, record.AccountingBasis.ToString(), scope.FundProfileId,
                    scope.LedgerBookId, scope.PeriodId, dimensions?.EntityId, dimensions?.AccountId ?? line.Account.FinancialAccountId,
                    dimensions?.OrganizationId),
                Timestamp(line.Timestamp), record.GlobalSequence.ToString(CultureInfo.InvariantCulture)),
            line.Currency?.FunctionalCurrency ?? baseCurrency, CurrencyUnit,
            line.Currency is { IsFunctionalCurrency: false } ? "calculated" : "sourced", false,
            ExactValue: Exact(debit ? line.Debit : line.Credit));
    }

    internal static AmountPassportComponentDto Component(string component, bool available, string detail)
        => new(component, available ? "available" : "unavailable", detail);

    internal static AmountPassportDto Build(AmountPassportReferenceDto reference, string label, decimal? amount,
        string? effectiveDate, string? calculation, string? exchangeRateRule, AmountPassportFreshnessDto freshness,
        AmountPassportPermissionsDto permissions, IReadOnlyList<AmountPassportComponentDto> components,
        IReadOnlyList<AmountPassportRecordDto> sources, IReadOnlyList<AmountPassportRecordDto> evidence,
        IReadOnlyList<AmountPassportRecordDto> history, IEnumerable<string> warnings, bool blocked = false)
    {
        var proofGaps = RequiredComponents.Where(key => components.Count(item => item.Component == key) != 1)
            .Select(key => $"The {key} component is missing or ambiguous.");
        var gaps = warnings.Concat(proofGaps)
            .Concat(components.Where(item => item.Status is not ("available" or "not-applicable")).Select(item => item.Detail))
            .Distinct(StringComparer.Ordinal).ToArray();
        var complete = gaps.Length == 0 && amount is not null && reference.Origin != "unavailable" &&
            sources.Count > 0 && !string.IsNullOrWhiteSpace(calculation) && !string.IsNullOrWhiteSpace(exchangeRateRule) &&
            permissions.View == "allowed" && freshness.Status == "current";
        var status = blocked ? "blocked" : freshness.Status == "stale" ? "stale"
            : history.Any(item => item.Kind == "successor") ? "superseded"
            : amount is null ? "unavailable" : complete ? "complete" : "review-required";
        return new(reference with { Freshness = freshness.Status }, label, amount is { } value ? Exact(value) : null, status, effectiveDate,
            calculation, exchangeRateRule, freshness, permissions, components, sources, evidence, history, gaps);
    }

    internal static AmountPassportRecordDto Journal(string kind, Guid journalId, Guid periodId, string label)
        => new(kind, journalId.ToString("D"), label,
            $"/workstation/accounting/journal-entries/detail?journalEntryId={journalId:D}&periodId={periodId:D}");

    internal static AmountPassportRecordDto[] EvidenceLinks(LedgerAmountProofDto proof)
        => proof.Status == EvidenceStatusDto.Blocked ? [] : proof.Evidence
            .Where(item => item.Status == EvidenceStatusDto.Ready && item.Route is not null)
            .Select(item => new AmountPassportRecordDto(item.Kind, item.EvidenceId, item.Label, item.Route,
                ContentHash: item.ContentHash)).ToArray();
}
