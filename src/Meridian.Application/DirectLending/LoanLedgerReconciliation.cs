using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;

namespace Meridian.Application.DirectLending;

/// <summary>Reads an exact canonical journal population using the loan posting account-role registry.</summary>
public static class LoanLedgerReconciliation
{
    public static async Task<IReadOnlyList<ReconciliationResultDto>> ReadAsync(
        Guid runId, Guid loanId, PersistedDirectLendingState state, ILedgerJournalStore? store,
        DirectLendingOptions options, IConfiguredLedgerAccountResolver? resolver, IFundProfileTenancyRegistry? tenancy,
        DateOnly asOf, DateTimeOffset observedAt, ReconcileLoanRequest request, CancellationToken ct)
    {
        IReadOnlyList<Guid> capturedJournalIds = [];
        Guid? capturedBookId = null;
        if (store is not ILedgerReportingSnapshotSource source)
            return [Unavailable("The journal store cannot capture an authoritative accounting population.")];
        if (state.Servicing.RevisionHistory.Any(revision => revision.EffectiveAsOfDate > asOf))
            return [Unavailable("The servicing state contains future-effective revisions beyond the reconciliation cutoff.")];
        Guid bookId;
        if (options.LedgerBookId is { } configured)
            bookId = configured;
        else
        {
            var candidates = await store.QueryAsync(new(AggregateId: loanId, EffectiveTo: asOf), ct).ConfigureAwait(false);
            var books = new HashSet<Guid>();
            foreach (var periodId in candidates.Select(static item => item.PeriodId).Distinct())
            {
                var period = await store.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
                if (period?.LedgerBookId is not { } candidate)
                    return [Unavailable("A retained loan journal has no exact ledger-book authority.")];
                books.Add(candidate);
            }
            if (books.Count != 1)
                return [Unavailable("The loan has no unambiguous canonical accounting book.")];
            bookId = books.Single();
        }
        var book = await store.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
        var currency = state.Contract.CurrentTerms.BaseCurrency.ToString();
        if (book is null || !string.Equals(book.BaseCurrency, currency, StringComparison.OrdinalIgnoreCase))
            return [Unavailable("The selected book cannot establish the loan's functional currency without conversion.")];
        var snapshot = await source.CaptureReportingSnapshotAsync(new(LedgerBookId: bookId, AggregateId: loanId,
            EffectiveTo: asOf), ct: ct).ConfigureAwait(false);
        var records = snapshot.Journals;
        if (records.Count == 0 || records.Any(record => record.AggregateId != loanId || record.AccountingBasis != book.AccountingBasis
                || (record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime)) > asOf))
            return [Unavailable("The captured loan journal population is absent or disagrees with its accounting scope.")];
        capturedBookId = bookId;
        capturedJournalIds = records.Select(static record => record.Entry.JournalEntryId).Distinct().Order().ToArray();
        var securityReference = state.Contract.CurrentTerms.SecurityMasterReference;
        if (securityReference is null || securityReference.SecurityId == Guid.Empty || string.IsNullOrWhiteSpace(securityReference.Symbol))
            return [Unavailable("The servicing state has no exact canonical security identity for the loan journal population.")];
        // Do not silently select only the current ticker from a loan's earlier security history.
        // A terms amendment can change that reference without disposing or transferring every old
        // carrying leg; omitting the old legs could falsely match a smaller current position.
        if (records.Any(record => record.Entry.Metadata.SecurityId != securityReference.SecurityId
                || !string.Equals(record.Entry.Metadata.Symbol, securityReference.Symbol, StringComparison.OrdinalIgnoreCase)
                || record.Entry.Lines.Any(line =>
                    line.Account.FinancialAccountId is { } accountId && string.Equals(accountId, loanId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                    && (line.Account.Symbol is { } lineSymbol && !string.Equals(lineSymbol, securityReference.Symbol, StringComparison.OrdinalIgnoreCase)
                        || line.Dimensions?.InstrumentId is { } instrumentId && instrumentId != securityReference.SecurityId))))
            return [Unavailable("The captured loan journals contain a different or missing security identity/symbol; the full retained history cannot be compared under the current reference.")];
        if (options.LedgerBookId.HasValue && records.SelectMany(static record => record.Entry.Lines)
                .Any(line => !string.Equals(line.ChartAccount?.Revision, options.LedgerChartMappingRevision, StringComparison.Ordinal)))
            return [Unavailable("The captured loan journal history does not use one exact retained configured chart revision.")];
        var chart = StandardChartOfAccounts.Instance;
        var balances = state.Servicing.Balances;
        (LedgerAccountRole Role, decimal Expected)[] measures =
        [
            (LedgerAccountRole.LoanPrincipal, balances.PrincipalOutstanding),
            (LedgerAccountRole.LoanAccruedInterestReceivable, balances.InterestAccruedUnpaid),
            (LedgerAccountRole.LoanPikInterestReceivable, state.Servicing.PikInterestAccruedUnpaid),
            (LedgerAccountRole.LoanCommitmentFeeReceivable, balances.CommitmentFeeAccruedUnpaid),
            (LedgerAccountRole.LoanFeeReceivable, balances.FeesAccruedUnpaid),
            (LedgerAccountRole.LoanPenaltyReceivable, balances.PenaltyAccruedUnpaid),
            (LedgerAccountRole.LoanPrepaidInterestLiability, balances.PrepaidInterest),
            (LedgerAccountRole.LoanUnappliedCashLiability, balances.UnappliedCash)
        ];
        var results = new List<ReconciliationResultDto>();
        var symbol = state.Contract.CurrentTerms.SecurityMasterReference?.Symbol;
        var ownership = options.LedgerBookId.HasValue && tenancy is not null
            ? await tenancy.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false) : null;
        foreach (var measure in measures)
        {
            var roleAccount = chart.Resolve(measure.Role, loanId.ToString("D"), symbol);
            ChartOfAccountsNodeDto? node = null;
            if (options.LedgerBookId.HasValue)
            {
                if (resolver is null || ownership is null || string.IsNullOrWhiteSpace(options.LedgerChartMappingRevision)
                    || !options.LedgerAccountPaths.TryGetValue(roleAccount.Name, out var path))
                {
                    results.Add(Unavailable("The pinned configured account-role mapping is unavailable.", measure.Role, measure.Expected));
                    continue;
                }
                node = await resolver.ResolveAsync(new(ownership.TenantId, ownership.CompanyId, book.FundProfileId,
                    bookId, path, roleAccount.AccountType.ToString(), options.LedgerChartMappingRevision,
                    symbol, loanId.ToString("D")), ct).ConfigureAwait(false);
            }
            var selected = records.SelectMany(record => record.Entry.Lines
                .Where(line => line.Account.FinancialAccountId == roleAccount.FinancialAccountId
                    && line.Account.Symbol == roleAccount.Symbol
                    && (node is null ? chart.TryGetRegistered(line.Account, out var role) && role == measure.Role
                        : line.Account.Name == node.AccountName && line.Account.AccountType == roleAccount.AccountType
                            && line.ChartAccount is { } reference && reference.NodeId == node.NodeId
                            && reference.Revision == options.LedgerChartMappingRevision))
                .Select(line => (Record: record, Line: line))).ToArray();
            if (selected.Length == 0)
            {
                results.Add(Unavailable("No retained opening or movement evidence exists for this economic account role.", measure.Role, measure.Expected));
                continue;
            }
            if (selected.Any(item => !HasCurrency(item.Record, item.Line, currency)))
            {
                results.Add(Unavailable("The role's functional currency is absent or conflicts with the exact book.", measure.Role, measure.Expected));
                continue;
            }
            var actual = selected.Sum(item => roleAccount.AccountType == LedgerAccountType.Liability
                ? item.Line.Credit - item.Line.Debit : item.Line.Debit - item.Line.Credit);
            results.Add(new(Guid.NewGuid(), runId, loanId, null, null,
                Math.Abs(actual - measure.Expected) <= request.AmountTolerance ? "Matched" : "LedgerBalanceMismatch",
                measure.Expected, actual, actual - measure.Expected, asOf, asOf, $"CanonicalRole:{measure.Role}",
                System.Text.Json.JsonSerializer.Serialize(new { amountTolerance = request.AmountTolerance }),
                [$"Compared servicing revision {state.Servicing.ServicingRevision} with the captured {book.AccountingBasis} carrying balance."], observedAt)
            {
                MeasurementKind = $"ledger-balance:{measure.Role}",
                Currency = currency,
                AsOfDate = asOf,
                LedgerBookId = bookId,
                ServicingRevision = state.Servicing.ServicingRevision,
                JournalEntryIds = selected.Select(static item => item.Record.Entry.JournalEntryId).Distinct().Order().ToArray()
            });
        }
        return results;

        ReconciliationResultDto Unavailable(string reason, LedgerAccountRole? role = null, decimal? expected = null) =>
            new(Guid.NewGuid(), runId, loanId, null, null, "LedgerCoverageUnavailable", expected, null, null,
                asOf, null, role is null ? "CanonicalRole:population" : $"CanonicalRole:{role}", null, [reason], observedAt)
            {
                MeasurementKind = role is null ? "ledger-balance:population" : $"ledger-balance:{role}",
                AsOfDate = asOf,
                ServicingRevision = state.Servicing.ServicingRevision,
                LedgerBookId = capturedBookId,
                JournalEntryIds = capturedJournalIds
            };
    }

    private static bool HasCurrency(LedgerJournalEntryRecord record, LedgerEntry line, string expected) =>
        line.Currency is { } detail ? string.Equals(detail.FunctionalCurrency, expected, StringComparison.OrdinalIgnoreCase)
            : record.Entry.Metadata.Tags?.TryGetValue("currency", out var currency) == true
                && string.Equals(currency, expected, StringComparison.OrdinalIgnoreCase);
}
