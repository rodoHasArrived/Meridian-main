using System.Globalization;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Reconciliation;

/// <summary>
/// Identifies the fund account and statement window an internal ledger-transaction population is
/// projected for. <paramref name="AccountLabel"/> is the label every projected record carries — the
/// run's external (custodian) account key, matching how the retained cash and position populations
/// are labeled. <paramref name="AccountAliases"/> are the identifiers a posted journal may be
/// stamped with for this account (the fund-account GUID, the account's ledger reference, the
/// external custodian key); a journal attributable to none of them is never projected. When
/// <paramref name="LedgerBookId"/> and <paramref name="AccountingPeriodId"/> are both retained, they
/// replace the posting-timestamp window as the journal-store query authority.
/// </summary>
public sealed record InternalLedgerTransactionQuery(
    string AccountLabel,
    IReadOnlyList<string> AccountAliases,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string BaseCurrency,
    Guid? LedgerBookId = null,
    Guid? AccountingPeriodId = null);

/// <summary>
/// Supplies the internal ledger-transaction population a statement run reconciles against. The
/// contract is fail-closed: when no journal source is composed, the account cannot be attributed,
/// or the period window is unusable, implementations return an empty list so the statement-run
/// matcher keeps classifying transaction breaks as informational
/// (<c>internal-transaction-population-unavailable</c>) instead of comparing against a fabricated book.
/// </summary>
public interface IInternalLedgerTransactionSource
{
    Task<IReadOnlyList<InternalLedgerTransaction>> GetTransactionsAsync(
        InternalLedgerTransactionQuery query,
        CancellationToken ct = default);
}

/// <summary>
/// Projects posted ledger journals into the custodian-visible internal transactions the
/// <see cref="StatementMatchingEngine"/> matches statement movements against. This is the
/// period-scoped journal→custodian-transaction projection the retained population provider's
/// remarks specify.
/// </summary>
/// <remarks>
/// Projection rules (conservative by design — a wrongly projected transaction would fabricate a
/// false match or a false internal-only break, both worse than the honest informational break):
/// <list type="bullet">
///   <item><b>Period scope:</b> journals are read via the tenant-scoped
///     <see cref="ILedgerJournalStore.QueryAsync"/>. A retained ledger-book and accounting-period
///     identity scopes the store query directly, so a journal posted later but effective in the
///     statement period remains visible. Legacy unscoped runs retain the posting-timestamp query
///     window. In both cases each entry's effective date
///     (<see cref="JournalEntryMetadata.EffectiveDate"/>, falling back to the posting timestamp) is
///     re-checked against the statement window.</item>
///   <item><b>Account attribution:</b> an entry is projected only when its metadata
///     <c>FinancialAccountId</c> or any line's account-scoped <c>FinancialAccountId</c> equals one
///     of the query's account aliases (ordinal-ignore-case). Only matching account-scoped cash lines
///     are netted; an unscoped cash line is accepted only when the entry metadata itself matches.
///     Unattributed journals — including an entire currency-blind single-account book — fail closed
///     to the empty population.</item>
///   <item><b>Custodian visibility:</b> only entries that move cash project — at least one line on
///     a well-known cash asset account (<c>Cash</c> or a per-currency <c>Cash (XXX)</c>). Pure
///     internal postings (accrual declarations, fair-value marks, revaluations, period close,
///     depreciation) never move cash and are excluded structurally; known internal activity
///     classifications and period-close closing entries are excluded explicitly as well. Reversal
///     entries (<c>reversal.of</c> tag) and the entries they reverse are both excluded: the pair
///     nets to nothing internally, and if the custodian really executed the movement its statement
///     row still surfaces as an honest break.</item>
///   <item><b>Amount and currency:</b> the projected net amount is the entry's net cash movement
///     (debits positive, credits negative), grouped per resolved cash currency — the line's
///     transaction-currency detail when present, else the <c>Cash (XXX)</c> denomination, else the
///     run's base currency (currency-blind legacy legs are assumed base-denominated). A
///     multi-currency entry (an FX conversion) projects one record per currency, mirroring the two
///     movements a statement shows; a zero net movement in a currency is skipped.</item>
///   <item><b>Type:</b> the canonical statement vocabulary (<c>trade</c>/<c>fee</c>/<c>dividend</c>/
///     <c>transaction</c>) is derived from the journal's activity classification when stamped, else
///     from its account shape (dividend accounts before instrument accounts, so a receivable
///     relief classifies as a dividend receipt).</item>
///   <item><b>Identity fields:</b> the external (FITID) id comes from
///     <see cref="JournalEntryMetadata.SettlementReference"/> or an <c>externalTransactionId</c>/
///     <c>fitid</c> tag; quantity and settlement date come from opt-in <c>quantity</c>/
///     <c>settlementDate</c> tags (invariant decimal / ISO date) and default to 0 and the trade
///     date — posting paths that carry them match exactly, others degrade to the engine's
///     candidate stage for operator review rather than fabricating identity.</item>
/// </list>
/// Every query failure — no composed store, a store without scoped-query support, an unavailable
/// database — degrades to the empty population (logged), never to a partial or fabricated one.
/// </remarks>
public sealed class LedgerJournalInternalTransactionSource(
    ILedgerJournalStore? journalStore = null,
    ILogger<LedgerJournalInternalTransactionSource>? logger = null)
    : IInternalLedgerTransactionSource
{
    private const string DefaultBaseCurrency = "USD";
    private const string ExternalTransactionIdTag = "externalTransactionId";
    private const string FitIdTag = "fitid";
    private const string QuantityTag = "quantity";
    private const string SettlementDateTag = "settlementDate";

    /// <summary>
    /// Activity classifications stamped by internal-only posting paths (period close, valuation
    /// marks, depreciation schedules, accrual/revaluation engines). These never have a custodian
    /// counterpart even if a future posting path routed cash through them.
    /// </summary>
    private static readonly HashSet<string> InternalOnlyActivityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "period-close",
        "period-reopen",
        "fair-value-mark",
        "depreciation",
        "amortization",
        "accrual",
        "revaluation",
    };

    private static readonly HashSet<string> TradeActivityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "trade",
        "buy",
        "sell",
        "short_sell",
        "cover_short",
    };

    private static readonly HashSet<string> FeeActivityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fee",
        "commission",
    };

    /// <summary>Per-symbol instrument accounts whose movement marks the entry as a trade.</summary>
    private static readonly HashSet<string> TradeAccountNames = new(StringComparer.Ordinal)
    {
        "Securities",
        "Short Securities Payable",
        "Option Premium Asset",
        "Option Premium Liability",
        "Futures MTM Settlement",
    };

    private static readonly HashSet<string> DividendAccountNames = new(StringComparer.Ordinal)
    {
        "Dividend Income",
        "Dividend Expense",
        "Dividend Receivable",
    };

    public async Task<IReadOnlyList<InternalLedgerTransaction>> GetTransactionsAsync(
        InternalLedgerTransactionQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A projection needs a journal source, a usable statement window, and at least one account
        // identity to attribute journals by; otherwise fail closed to the empty population so the
        // informational break classification keeps operating.
        if (journalStore is null || query.PeriodEnd == default)
        {
            return [];
        }

        var periodStart = query.PeriodStart == default ? query.PeriodEnd : query.PeriodStart;
        if (periodStart > query.PeriodEnd)
        {
            return [];
        }

        var aliases = new HashSet<string>(
            query.AccountAliases.Where(static alias => !string.IsNullOrWhiteSpace(alias)).Select(static alias => alias.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (aliases.Count == 0)
        {
            return [];
        }

        IReadOnlyList<LedgerJournalEntryRecord> records;
        var ledgerBookId = query.LedgerBookId is { } retainedLedgerBookId && retainedLedgerBookId != Guid.Empty
            ? retainedLedgerBookId
            : (Guid?)null;
        var accountingPeriodId = query.AccountingPeriodId is { } retainedAccountingPeriodId && retainedAccountingPeriodId != Guid.Empty
            ? retainedAccountingPeriodId
            : (Guid?)null;
        if (ledgerBookId.HasValue != accountingPeriodId.HasValue)
        {
            // Exact accounting authority is atomic. Falling back to posting timestamps when only
            // half of the retained scope is present could silently read another book or period.
            return [];
        }

        var hasExactAccountingScope = ledgerBookId.HasValue && accountingPeriodId.HasValue;
        try
        {
            var journalQuery = hasExactAccountingScope
                ? new LedgerJournalEntryQuery(
                    LedgerBookId: ledgerBookId,
                    PeriodId: accountingPeriodId)
                : new LedgerJournalEntryQuery(
                    OccurredFrom: new DateTimeOffset(periodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    OccurredTo: new DateTimeOffset(query.PeriodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
            records = await journalStore.QueryAsync(
                    journalQuery,
                    ct)
                .ConfigureAwait(false);
            if (hasExactAccountingScope)
            {
                // A store that violates its period predicate must not bleed another accounting
                // period into this population. Ledger-book ownership is enforced by the store's
                // period join; the record itself retains the exact period id for this second check.
                records = (records ?? [])
                    .Where(record => record.PeriodId == accountingPeriodId.Value)
                    .ToArray();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed to the empty transaction population only: the retained cash/position
            // populations must keep reconciling even when the journal store is unavailable or does
            // not support scoped queries.
            logger?.LogWarning(
                ex,
                "Failed to query posted journals for retained ledger book {LedgerBookId} and accounting period {AccountingPeriodId}; projecting an empty internal transaction population.",
                ledgerBookId,
                accountingPeriodId);
            return [];
        }

        return Project(records ?? [], query, aliases, periodStart);
    }

    private static IReadOnlyList<InternalLedgerTransaction> Project(
        IReadOnlyList<LedgerJournalEntryRecord> records,
        InternalLedgerTransactionQuery query,
        HashSet<string> aliases,
        DateOnly periodStart)
    {
        var reversalExclusions = CollectReversalExclusions(records);
        var baseCurrency = string.IsNullOrWhiteSpace(query.BaseCurrency)
            ? DefaultBaseCurrency
            : query.BaseCurrency.Trim().ToUpperInvariant();

        var transactions = new List<InternalLedgerTransaction>();
        foreach (var record in records)
        {
            var entry = record.Entry;
            if (reversalExclusions.Contains(entry.JournalEntryId)
                || record.PostingKind == LedgerPostingKindDto.ClosingEntry
                || (entry.Metadata.ActivityType is { } activity && InternalOnlyActivityTypes.Contains(activity))
                || !BelongsToAccount(entry, aliases))
            {
                continue;
            }

            // Conservative custodian-visibility rule: only postings that move cash have a statement
            // counterpart. Accrual declarations, valuation marks, and pure reclasses have no cash
            // line and are excluded structurally.
            var metadataAccountMatches = MatchesAccountAlias(entry.Metadata.FinancialAccountId, aliases);
            var cashLines = entry.Lines
                .Where(line =>
                    IsCashLine(line) &&
                    CashLineBelongsToAccount(line, metadataAccountMatches, aliases))
                .ToArray();
            if (cashLines.Length == 0)
            {
                continue;
            }

            var tradeDate = entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(entry.Timestamp.UtcDateTime);
            if (tradeDate < periodStart || tradeDate > query.PeriodEnd)
            {
                // Defensive re-check: never project an out-of-window record even if a store streams
                // one outside the requested bound (mirrors the position-snapshot ceiling check).
                continue;
            }

            var currencyGroups = cashLines
                .GroupBy(line => ResolveCashCurrency(line, baseCurrency), StringComparer.OrdinalIgnoreCase)
                .Select(group => (Currency: group.Key, NetAmount: group.Sum(SignedCashAmount)))
                .Where(static group => group.NetAmount != 0m)
                .ToArray();
            if (currencyGroups.Length == 0)
            {
                continue;
            }

            var transactionType = ClassifyTransactionType(entry);
            var externalTransactionId = ResolveExternalTransactionId(entry.Metadata);
            var settlementDate = ResolveSettlementDate(entry.Metadata) ?? tradeDate;
            // Quantity only applies to a single-currency movement; an FX conversion's per-currency
            // legs carry no share count.
            var quantity = currencyGroups.Length == 1 ? ResolveQuantity(entry.Metadata) : 0m;
            var journalId = entry.JournalEntryId.ToString("D");

            foreach (var (currency, netAmount) in currencyGroups)
            {
                var transactionId = currencyGroups.Length == 1
                    ? $"internal-txn:{query.AccountLabel}:{journalId}"
                    : $"internal-txn:{query.AccountLabel}:{journalId}:{currency}";
                transactions.Add(new InternalLedgerTransaction(
                    transactionId,
                    externalTransactionId,
                    query.AccountLabel,
                    entry.Metadata.Symbol,
                    currency,
                    tradeDate,
                    settlementDate,
                    transactionType,
                    quantity,
                    netAmount,
                    $"internal:journal:{journalId}"));
            }
        }

        return transactions;
    }

    /// <summary>
    /// A reversal entry and the entry it reverses are both excluded: internally the pair nets to
    /// nothing, so neither side is a custodian-comparable movement. If the custodian actually
    /// executed the reversed movement, its statement row still surfaces as an honest break.
    /// </summary>
    private static HashSet<Guid> CollectReversalExclusions(IReadOnlyList<LedgerJournalEntryRecord> records)
    {
        var excluded = new HashSet<Guid>();
        foreach (var record in records)
        {
            if (record.Entry.Metadata.Tags is { } tags
                && tags.TryGetValue(LedgerJournalReversal.ReversalOfTag, out var reversedId)
                && Guid.TryParse(reversedId, out var originalJournalEntryId))
            {
                excluded.Add(originalJournalEntryId);
                excluded.Add(record.Entry.JournalEntryId);
            }
        }

        return excluded;
    }

    private static bool BelongsToAccount(JournalEntry entry, HashSet<string> aliases)
    {
        if (MatchesAccountAlias(entry.Metadata.FinancialAccountId, aliases))
        {
            return true;
        }

        return entry.Lines.Any(line => MatchesAccountAlias(line.Account.FinancialAccountId, aliases));
    }

    private static bool CashLineBelongsToAccount(
        LedgerEntry line,
        bool metadataAccountMatches,
        HashSet<string> aliases)
    {
        var lineAccountId = line.Account.FinancialAccountId;
        return string.IsNullOrWhiteSpace(lineAccountId)
            ? metadataAccountMatches
            : aliases.Contains(lineAccountId.Trim());
    }

    private static bool MatchesAccountAlias(string? financialAccountId, HashSet<string> aliases) =>
        !string.IsNullOrWhiteSpace(financialAccountId) && aliases.Contains(financialAccountId.Trim());

    private static bool IsCashLine(LedgerEntry line) =>
        line.Account.AccountType == LedgerAccountType.Asset
        && (string.Equals(line.Account.Name, "Cash", StringComparison.Ordinal)
            || (line.Account.Name.StartsWith("Cash (", StringComparison.Ordinal)
                && line.Account.Name.EndsWith(")", StringComparison.Ordinal)));

    private static string ResolveCashCurrency(LedgerEntry line, string baseCurrency)
    {
        if (line.Currency is { } currency)
        {
            return currency.TransactionCurrency;
        }

        // Per-currency cash accounts carry their denomination as the account symbol ("Cash (EUR)").
        if (line.Account.Symbol is { } symbol && !string.IsNullOrWhiteSpace(symbol))
        {
            return symbol.Trim().ToUpperInvariant();
        }

        // Currency-blind legacy legs post functional amounts; assume the run's base currency.
        return baseCurrency;
    }

    /// <summary>Net cash movement of one line: debits (cash in) positive, credits (cash out) negative.</summary>
    private static decimal SignedCashAmount(LedgerEntry line)
    {
        if (line.Currency is { } currency)
        {
            return currency.IsDebit ? currency.TransactionDebit : -currency.TransactionCredit;
        }

        return line.Debit - line.Credit;
    }

    /// <summary>
    /// Maps the journal onto the canonical statement activity vocabulary ("trade"/"fee"/"dividend"/
    /// "transaction") the matching engine compares transaction types with. Dividend accounts are
    /// checked before instrument accounts so a receivable relief (Dr Cash / Cr Dividend Receivable)
    /// classifies as a dividend receipt rather than a trade.
    /// </summary>
    private static string ClassifyTransactionType(JournalEntry entry)
    {
        if (entry.Metadata.ActivityType is { } activity)
        {
            if (TradeActivityTypes.Contains(activity))
            {
                return "trade";
            }

            if (FeeActivityTypes.Contains(activity))
            {
                return "fee";
            }

            if (activity.Equals("dividend", StringComparison.OrdinalIgnoreCase))
            {
                return "dividend";
            }
        }

        if (entry.Lines.Any(static line => DividendAccountNames.Contains(line.Account.Name)))
        {
            return "dividend";
        }

        if (entry.Lines.Any(static line => TradeAccountNames.Contains(line.Account.Name)))
        {
            return "trade";
        }

        if (entry.Lines.Any(static line => line.Account.AccountType == LedgerAccountType.Expense))
        {
            return "fee";
        }

        return "transaction";
    }

    private static string? ResolveExternalTransactionId(JournalEntryMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.SettlementReference))
        {
            return metadata.SettlementReference;
        }

        if (metadata.Tags is { } tags
            && (tags.TryGetValue(ExternalTransactionIdTag, out var externalId)
                || tags.TryGetValue(FitIdTag, out externalId))
            && !string.IsNullOrWhiteSpace(externalId))
        {
            return externalId.Trim();
        }

        return null;
    }

    private static decimal ResolveQuantity(JournalEntryMetadata metadata) =>
        metadata.Tags is { } tags
            && tags.TryGetValue(QuantityTag, out var quantityText)
            && decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
        ? quantity
        : 0m;

    private static DateOnly? ResolveSettlementDate(JournalEntryMetadata metadata) =>
        metadata.Tags is { } tags
            && tags.TryGetValue(SettlementDateTag, out var settlementText)
            && DateOnly.TryParse(settlementText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var settlementDate)
        ? settlementDate
        : null;
}
