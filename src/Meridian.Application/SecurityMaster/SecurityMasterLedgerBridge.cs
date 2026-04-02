using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;

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
        Ledger.Ledger ledger,
        CancellationToken ct = default);
}

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
        Ledger.Ledger ledger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

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
            var meta = new JournalEntryMetadata(
                ActivityType: action.EventType,
                Symbol: normalizedTicker,
                SecurityId: securityId,
                LedgerView: LedgerViewKind.SecurityMaster);

            switch (action.EventType)
            {
                case "Dividend" when action.DividendPerShare.HasValue:
                    PostDividendDeclaration(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                case "StockSplit" when action.SplitRatio.HasValue:
                    PostSplitMemo(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                case "SpinOff" or "MergerAbsorption" or "RightsIssue":
                    PostCorpActionDistribution(ledger, action, normalizedTicker, ts, meta);
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
        Ledger.Ledger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta)
    {
        var amount = action.DividendPerShare!.Value;
        var description = $"Dividend declared {ticker} ex {action.ExDate:yyyy-MM-dd} @ {amount:N4}/sh";

        var entry = new JournalEntry(
            action.CorpActId,
            ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.DividendReceivable(ticker), amount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.DividendIncome, 0m, amount, description),
            ],
            meta);

        ledger.Post(entry);
    }

    private static void PostSplitMemo(
        Ledger.Ledger ledger,
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

    private static void PostCorpActionDistribution(
        Ledger.Ledger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta)
    {
        // Use DistributionRatio as a proxy amount when available; fall back to 1m for memo.
        var amount = action.DistributionRatio ?? action.ExchangeRatio ?? 1m;
        var description = $"{action.EventType} {ticker} ex {action.ExDate:yyyy-MM-dd}";

        var entry = new JournalEntry(
            action.CorpActId,
            ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.Cash, amount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), action.CorpActId, ts,
                    LedgerAccounts.CorpActionDistribution(ticker), 0m, amount, description),
            ],
            meta);

        ledger.Post(entry);
    }
}
