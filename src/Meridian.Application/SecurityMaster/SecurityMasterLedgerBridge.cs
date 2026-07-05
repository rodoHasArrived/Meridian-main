using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Posts Security Master corporate action events into a <see cref="Ledger"/>
/// using <see cref="LedgerViewKind.SecurityMaster"/>, enabling reconciliation between
/// contractual flows (declared in the Security Master) and actual cash movements.
/// </summary>
public interface ISecurityMasterLedgerBridge
{
    /// <summary>
    /// Posts contractual corporate action flows for <paramref name="securityId"/> into
    /// <paramref name="ledger"/> using <see cref="LedgerViewKind.SecurityMaster"/>.
    /// Idempotent: entries whose <see cref="CorporateActionDto.CorpActId"/> already appear
    /// as a <see cref="JournalEntry.JournalEntryId"/> in the ledger are skipped.
    /// </summary>
    Task PostCorporateActionsAsync(
        Guid securityId,
        string ticker,
        DomainLedger ledger,
        CancellationToken ct = default);
}

public sealed record CorporateActionLedgerPostingContext(
    decimal PositionQuantity = 0m,
    decimal WithholdingTaxRate = 0m,
    string? FinancialAccountId = null);

/// <summary>
/// Default implementation of <see cref="ISecurityMasterLedgerBridge"/>.
/// </summary>
public sealed class SecurityMasterLedgerBridge : ISecurityMasterLedgerBridge
{
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ILogger<SecurityMasterLedgerBridge> _logger;

    public SecurityMasterLedgerBridge(
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterLedgerBridge> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PostCorporateActionsAsync(
        Guid securityId,
        string ticker,
        DomainLedger ledger,
        CancellationToken ct = default)
        => await PostCorporateActionsAsync(
            securityId,
            ticker,
            ledger,
            CorporateActionLedgerPostingContextDefaults.Empty,
            ct).ConfigureAwait(false);

    public async Task PostCorporateActionsAsync(
        Guid securityId,
        string ticker,
        DomainLedger ledger,
        CorporateActionLedgerPostingContext? context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        var postingContext = NormalizeContext(context);
        var actions = await _queryService.GetCorporateActionsAsync(securityId, ct).ConfigureAwait(false);
        if (actions.Count == 0)
            return;

        var existingIds = ledger.Journal
            .Select(j => j.JournalEntryId)
            .ToHashSet();

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        int posted = 0;

        foreach (var action in actions)
        {
            if (existingIds.Contains(action.CorpActId))
                continue;

            var ts = new DateTimeOffset(action.ExDate.Year, action.ExDate.Month, action.ExDate.Day,
                                        0, 0, 0, TimeSpan.Zero);
            var eventType = CorporateActionEventTypes.Normalize(action.EventType);
            var meta = new JournalEntryMetadata(
                ActivityType: eventType,
                Symbol: normalizedTicker,
                SecurityId: securityId,
                LedgerView: LedgerViewKind.SecurityMaster);

            switch (eventType)
            {
                case CorporateActionEventTypes.Dividend when action.DividendPerShare.HasValue:
                    if (postingContext.PositionQuantity <= 0m)
                    {
                        _logger.LogWarning(
                            "SecurityMasterLedgerBridge skipped dividend {CorporateActionId} for {Ticker} because no record-date position quantity was supplied.",
                            action.CorpActId,
                            normalizedTicker);
                        break;
                    }

                    var grossDividend = action.DividendPerShare.Value * postingContext.PositionQuantity;
                    var withholding = grossDividend * postingContext.WithholdingTaxRate;
                    PostDividendDeclaration(ledger, action, normalizedTicker, ts, meta, grossDividend, postingContext.PositionQuantity);
                    if (withholding > 0m)
                    {
                        PostWithholdingAccrual(ledger, action, normalizedTicker, postingContext, withholding);
                    }

                    if (action.PayDate.HasValue)
                    {
                        PostDividendReceipt(ledger, action, normalizedTicker, postingContext, grossDividend, withholding);
                    }

                    posted++;
                    break;

                case CorporateActionEventTypes.StockSplit or CorporateActionEventTypes.ReverseStockSplit when action.SplitRatio.HasValue:
                    PostSplitMemo(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                case CorporateActionEventTypes.SpinOff or CorporateActionEventTypes.MergerAbsorption or CorporateActionEventTypes.RightsIssue:
                    PostNonCashLifecycleMemo(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                case CorporateActionEventTypes.PrincipalPaydown when action.DistributionRatio.HasValue:
                    PostFactorPaydown(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                default:
                    _logger.LogDebug(
                        "SecurityMasterLedgerBridge: skipping unhandled event type {EventType} for {Ticker}",
                        action.EventType, normalizedTicker);
                    break;
            }
        }

        if (posted > 0)
            _logger.LogInformation(
                "SecurityMasterLedgerBridge posted {Count} corporate action entries for {Ticker}",
                posted, normalizedTicker);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void PostDividendDeclaration(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta,
        decimal grossAmount,
        decimal positionQuantity)
    {
        var amountPerShare = action.DividendPerShare!.Value;
        var recordDate = action.RecordDate?.ToString("yyyy-MM-dd") ?? "n/a";
        var description = $"Dividend declared {ticker} ex {action.ExDate:yyyy-MM-dd} record {recordDate} @ {amountPerShare:N4}/sh x {positionQuantity:N4}";

        var entry = new JournalEntry(
            action.CorpActId,
            ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.DividendReceivable(ticker), grossAmount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.DividendIncome, 0m, grossAmount, description),
            ],
            meta);

        ledger.Post(entry);
    }

    private static void PostWithholdingAccrual(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        CorporateActionLedgerPostingContext context,
        decimal withholding)
    {
        var ts = ToUtcStartOfDay(action.PayDate ?? action.ExDate);
        var journalId = DeriveJournalId(action.CorpActId, "withholding");
        var scope = context.FinancialAccountId ?? ticker;
        var description = $"Withholding tax accrued {ticker} pay {(action.PayDate ?? action.ExDate):yyyy-MM-dd}";
        var meta = new JournalEntryMetadata(
            ActivityType: AutomatedJournalEventKind.WithholdingTaxAccrued.ToString(),
            Symbol: ticker,
            SecurityId: action.SecurityId,
            LedgerView: LedgerViewKind.SecurityMaster);

        ledger.Post(new JournalEntry(
            journalId,
            ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), journalId, ts,
                    LedgerAccounts.WithholdingTaxExpenseFor(scope), withholding, 0m, description),
                new LedgerEntry(Guid.NewGuid(), journalId, ts,
                    LedgerAccounts.WithholdingTaxPayableFor(scope), 0m, withholding, description),
            ],
            meta));
    }

    private static void PostDividendReceipt(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        CorporateActionLedgerPostingContext context,
        decimal grossAmount,
        decimal withholding)
    {
        var payDate = action.PayDate!.Value;
        var ts = ToUtcStartOfDay(payDate);
        var journalId = DeriveJournalId(action.CorpActId, "receipt");
        var scope = context.FinancialAccountId ?? ticker;
        var netCash = grossAmount - withholding;
        var description = $"Dividend received {ticker} pay {payDate:yyyy-MM-dd}";
        var meta = new JournalEntryMetadata(
            ActivityType: AutomatedJournalEventKind.DividendReceived.ToString(),
            Symbol: ticker,
            SecurityId: action.SecurityId,
            LedgerView: LedgerViewKind.SecurityMaster);

        var lines = new List<LedgerEntry>
        {
            new(Guid.NewGuid(), journalId, ts, LedgerAccounts.Cash, netCash, 0m, description)
        };

        if (withholding > 0m)
        {
            lines.Add(new LedgerEntry(Guid.NewGuid(), journalId, ts,
                LedgerAccounts.WithholdingTaxPayableFor(scope), withholding, 0m, description));
        }

        lines.Add(new LedgerEntry(Guid.NewGuid(), journalId, ts,
            LedgerAccounts.DividendReceivable(ticker), 0m, grossAmount, description));

        ledger.Post(new JournalEntry(journalId, ts, description, lines, meta));
    }

    private static void PostSplitMemo(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta)
    {
        // Stock splits are non-monetary; post a symbolic 1-unit memo entry to
        // record the event in the Security Master ledger view for audit purposes.
        const decimal memoAmount = 1m;
        var description = $"Stock split {ticker} {action.SplitRatio:N4}:1 ex {action.ExDate:yyyy-MM-dd}";

        var entry = new JournalEntry(
            action.CorpActId,
            ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.Securities(ticker), memoAmount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.Securities(ticker), 0m, memoAmount, description),
            ],
            meta);

        ledger.Post(entry);
    }

    private static void PostNonCashLifecycleMemo(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta)
    {
        const decimal memoAmount = 1m;
        var description = $"{action.EventType} {ticker} ex {action.ExDate:yyyy-MM-dd}";

        var entry = new JournalEntry(
            action.CorpActId,
            ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.Securities(ticker), memoAmount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.Securities(ticker), 0m, memoAmount, description),
            ],
            meta);

        ledger.Post(entry);
    }

    private static void PostFactorPaydown(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta)
    {
        var amount = action.DistributionRatio!.Value;
        var description = $"Principal paydown {ticker} ex {action.ExDate:yyyy-MM-dd} factor delta {amount:N6}";

        var entry = new JournalEntry(
            action.CorpActId,
            ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.Cash, amount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.Securities(ticker), 0m, amount, description),
            ],
            meta);

        ledger.Post(entry);
    }

    private static CorporateActionLedgerPostingContext NormalizeContext(CorporateActionLedgerPostingContext? context)
    {
        var value = context ?? CorporateActionLedgerPostingContextDefaults.Empty;
        if (value.WithholdingTaxRate < 0m || value.WithholdingTaxRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(context), "WithholdingTaxRate must be between 0 and 1.");

        return value;
    }

    private static DateTimeOffset ToUtcStartOfDay(DateOnly date)
        => new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    private static Guid DeriveJournalId(Guid corporateActionId, string suffix)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"{corporateActionId:D}:{suffix}")));

    private static class CorporateActionLedgerPostingContextDefaults
    {
        public static CorporateActionLedgerPostingContext Empty { get; } = new();
    }
}
