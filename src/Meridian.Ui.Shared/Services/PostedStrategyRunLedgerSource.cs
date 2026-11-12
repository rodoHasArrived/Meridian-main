using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>Rebuilds a run view from posted journals carrying its exact retained run identity.
/// Books and accounts are authorized independently; strategy identity alone never selects a book.</summary>
public sealed class PostedStrategyRunLedgerSource(
    ILedgerJournalStore? journals = null,
    IFundProfileTenancyRegistry? tenancy = null) : IStrategyRunLedgerSource
{
    public async Task<IReadOnlyLedger?> ReadAsync(StrategyRunEntry run, StrategyRunReadScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(scope);
        if (journals is null || tenancy is null || run.RunType == RunType.Backtest
            || !TenantReadPredicate.IsResolvedTenant(scope.TenantId)
            || !TenantReadPredicate.IsResolvedTenant(scope.CompanyId))
            return null;

        // These metadata aliases also carry opaque legacy display scopes. Scan all UUID
        // candidates first so a label cannot conceal contradictory authoritative identities.
        var (bookId, hasLegacyBookLabel) = ReadScopeBinding(run, "ledgerBookId", "bookId", "ledgerBookScopeId", "bookScopeId");
        if (run.FundAccountId == Guid.Empty)
            throw new InvalidDataException("The retained run has an empty authoritative fund account identity.");
        var (declaredAccountId, hasLegacyAccountLabel) = ReadScopeBinding(run, "accountScopeId", "accountId");
        var accountId = run.FundAccountId is { } account && account != Guid.Empty
            ? account : declaredAccountId;
        if (declaredAccountId is not null && declaredAccountId != accountId)
            throw new InvalidDataException("The retained run has contradictory accounting account identities.");
        // A typed fund account can coexist with old presentation labels. Do not guess how
        // labels map to posted books/accounts or make an authorized retained run unreadable.
        if (hasLegacyBookLabel || hasLegacyAccountLabel || accountId is null)
            return null;
        IReadOnlyList<LedgerBookRecord> books;
        if (bookId is { } selectedBook)
        {
            var book = await journals.GetLedgerBookAsync(selectedBook, ct).ConfigureAwait(false);
            books = book is null ? [] : [book];
        }
        else
        {
            books = await journals.ListLedgerBooksAsync(fundStructureNodeId: accountId, ct: ct).ConfigureAwait(false);
            // Parallel accounting bases are separate books. A run without an explicit book may
            // use its unique primary book, never add primary, tax and GAAP postings together.
            books = books.Where(book => book.AccountingBasis == AccountingBasisKindDto.Primary).ToArray();
            if (books.Count != 1)
                return null;
        }

        var result = new Meridian.Ledger.Ledger();
        var periods = new Dictionary<Guid, LedgerAccountingPeriod?>();
        foreach (var book in books)
        {
            if (book.FundStructureNodeId != accountId
                || (!string.IsNullOrWhiteSpace(run.FundProfileId)
                    && !string.Equals(run.FundProfileId, book.FundProfileId, StringComparison.Ordinal)))
                continue;
            var owner = await tenancy.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(scope.TenantId)
                || !string.Equals(owner.CompanyId, scope.CompanyId, StringComparison.OrdinalIgnoreCase))
                continue;
            var records = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book.LedgerBookId), ct)
                .ConfigureAwait(false);
            foreach (var record in records)
            {
                ct.ThrowIfCancellationRequested();
                var entry = record.Entry;
                if (entry.Metadata.Tags?.TryGetValue("strategy.runId", out var sourceRunId) != true
                    || !string.Equals(sourceRunId, run.RunId, StringComparison.Ordinal))
                    continue;
                if (!periods.TryGetValue(record.PeriodId, out var period))
                {
                    period = await journals.GetPeriodAsync(record.PeriodId, ct).ConfigureAwait(false);
                    periods.Add(record.PeriodId, period);
                }
                if (period?.LedgerBookId != book.LedgerBookId)
                    throw new InvalidDataException("The posted run journal belongs to a different accounting book.");
                if (!string.Equals(entry.Metadata.StrategyId, run.StrategyId, StringComparison.Ordinal)
                    || !string.Equals(entry.Metadata.FinancialAccountId, accountId.Value.ToString("D"), StringComparison.OrdinalIgnoreCase)
                    || entry.Lines.Any(line => !string.Equals(line.Account.FinancialAccountId,
                        accountId.Value.ToString("D"), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Posted run provenance conflicts with its retained strategy or account.");
                result.Post(entry);
            }
        }
        // Book access proves ownership, not that its journal can reconstruct this run. Historical
        // immutable postings may predate run attribution, and a new run may have no posts yet.
        // Preserve the retained-run fallback (or its missing-ledger warning) until at least one
        // independently validated journal establishes an attributed population for this run.
        return result.Journal.Count > 0 ? result : null;
    }

    private static (Guid? Id, bool HasLegacyLabel) ReadScopeBinding(StrategyRunEntry run, params string[] keys)
    {
        Guid? result = null;
        var hasLegacyLabel = false;
        foreach (var key in keys)
        {
            if (run.ParameterSet is null || !run.ParameterSet.TryGetValue(key, out var value))
                continue;
            if (!Guid.TryParse(value, out var id))
            {
                hasLegacyLabel = true;
                continue;
            }
            if (id == Guid.Empty || (result is not null && result != id))
                throw new InvalidDataException("The retained run has invalid or contradictory accounting scope metadata.");
            result = id;
        }
        return (result, hasLegacyLabel);
    }

}
