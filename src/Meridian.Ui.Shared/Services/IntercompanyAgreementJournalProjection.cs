using System.Globalization;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Projects posted line provenance without calculating agreement matches or changing consolidation.</summary>
public static class IntercompanyAgreementJournalProjection
{
    public static IntercompanyAgreementJournalLinesDto Project(IntercompanyAgreementDetailDto agreement,
        LedgerBookRecord book, IReadOnlyList<LedgerJournalEntryRecord> records,
        DateOnly asOf, string? cursor = null, int pageSize = 100)
    {
        if (pageSize is < 1 or > 500)
            throw new IntercompanyAgreementValidationException("Page size must be between 1 and 500.");
        long cursorSequence = -1;
        var cursorLine = Guid.Empty;
        if (cursor is not null)
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out cursorSequence)
                || !Guid.TryParseExact(parts[1], "N", out cursorLine))
                throw new IntercompanyAgreementValidationException("The source-line cursor is invalid.");
        }
        var result = new List<IntercompanyAgreementJournalLineDto>();
        foreach (var record in records)
        {
            var date = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
            if (date > asOf) continue;
            foreach (var line in record.Entry.Lines)
            {
                var dimensions = line.Dimensions;
                var exactReference = dimensions?.IntercompanyAgreementId == agreement.AgreementId;
                var isParty = Guid.TryParse(dimensions?.EntityId, out var entity) && entity == book.FundStructureNodeId;
                var hasCounterparty = Guid.TryParse(dimensions?.CounterpartyId, out var counterparty);
                var reciprocal = isParty && hasCounterparty && entity != counterparty
                    && ((entity == agreement.PartyOneId && counterparty == agreement.PartyTwoId)
                        || (entity == agreement.PartyTwoId && counterparty == agreement.PartyOneId));
                var supportedAccount = line.Account.Name == ConsolidationService.ReceivableAccount
                    && line.Account.AccountType == LedgerAccountType.Asset
                    || line.Account.Name == ConsolidationService.PayableAccount
                    && line.Account.AccountType == LedgerAccountType.Liability;
                supportedAccount &= line.Account.Symbol is null && line.Account.FinancialAccountId is null;
                var missingReference = dimensions?.IntercompanyAgreementId is null
                    && dimensions?.IntercompanyAgreementVersion is null;
                if (!exactReference && !(missingReference && reciprocal && supportedAccount)) continue;
                if (record.GlobalSequence < cursorSequence
                    || record.GlobalSequence == cursorSequence && line.EntryId.CompareTo(cursorLine) <= 0) continue;
                var state = "MissingAgreement";
                string? warning = "This posted intercompany line has no agreement reference.";
                if (exactReference)
                {
                    var pinned = agreement.Revisions.SingleOrDefault(revision => revision.AgreementVersion == dimensions!.IntercompanyAgreementVersion);
                    var currency = line.Currency?.TransactionCurrency ?? book.BaseCurrency;
                    if (!reciprocal || pinned is null || pinned.Action == IntercompanyAgreementActionDto.Terminate
                        || !string.Equals(currency, pinned.Terms.Currency, StringComparison.OrdinalIgnoreCase))
                    {
                        state = "Unknown";
                        warning = "The retained reference or party/currency attribution cannot be verified.";
                    }
                    else
                    {
                        var current = IntercompanyAgreementTimeline.Resolve(agreement.Revisions, date);
                        var knownThen = IntercompanyAgreementTimeline.Resolve(agreement.Revisions, date, record.CreatedAt);
                        if (pinned.ApprovedAtUtc > record.CreatedAt)
                        {
                            state = "Unknown";
                            warning = "The cited revision was approved after this journal was recorded.";
                        }
                        else if (current.Status == IntercompanyAgreementStatusDto.Active
                            && current.Revision?.AgreementVersion == pinned.AgreementVersion
                            && knownThen.Status == IntercompanyAgreementStatusDto.Active
                            && knownThen.Revision?.AgreementVersion == pinned.AgreementVersion)
                        {
                            state = "Covered";
                            warning = null;
                        }
                        else
                        {
                            state = "HistoricalRevision";
                            warning = "The journal retains its original approved revision; inspect effective-date history and correction lineage.";
                        }
                    }
                }
                result.Add(new(record.Entry.JournalEntryId, line.EntryId, record.PeriodId, record.GlobalSequence, date,
                    book.FundStructureNodeId, reciprocal ? counterparty : null, line.Account.Name, line.Description,
                    line.Currency?.FunctionalCurrency ?? book.BaseCurrency, Amount(line.Debit), Amount(line.Credit),
                    line.Currency?.TransactionCurrency ?? book.BaseCurrency,
                    Amount(line.Currency?.TransactionDebit ?? line.Debit), Amount(line.Currency?.TransactionCredit ?? line.Credit),
                    dimensions?.IntercompanyAgreementId, dimensions?.IntercompanyAgreementVersion,
                    state, warning, $"journal:{record.Entry.JournalEntryId:D}/line:{line.EntryId:D}"));
            }
        }
        var ordered = result.OrderBy(line => line.Sequence).ThenBy(line => line.LineId).Take(pageSize + 1).ToArray();
        var page = ordered.Take(pageSize).ToArray();
        var next = ordered.Length > pageSize
            ? $"{page[^1].Sequence.ToString(CultureInfo.InvariantCulture)}:{page[^1].LineId:N}" : null;
        return new(agreement.AgreementId, book.LedgerBookId, asOf,
            page.Any(line => line.CoverageState == "Unknown") ? "Unknown" : "Available", page, next);
    }

    private static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
