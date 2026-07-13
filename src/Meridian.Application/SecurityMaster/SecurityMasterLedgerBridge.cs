using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
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
    private readonly IFactorPaydownProjectionService _factorPaydownProjector;

    public SecurityMasterLedgerBridge(
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterLedgerBridge> logger,
        IFactorPaydownProjectionService? factorPaydownProjector = null)
    {
        _queryService = queryService;
        _logger = logger;
        _factorPaydownProjector = factorPaydownProjector ?? new FactorPaydownProjectionService();
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

        foreach (var state in CorporateActionEffectiveStateProjector.Project(actions, DateTimeOffset.UtcNow))
        {
            var action = state.Effective;

            // Superseded chains: an amendment or cancellation whose original was already
            // posted needs a correcting entry through the governed restatement path, not a
            // silent repost — the bridge never double-books a corporate action.
            var priorPosted = state.Timeline.Count > 1 || state.IsCancelled
                ? state.Timeline.Any(prior => prior.CorpActId != action.CorpActId && existingIds.Contains(prior.CorpActId))
                : false;
            if (state.IsCancelled)
            {
                if (priorPosted)
                {
                    _logger.LogWarning(
                        "SecurityMasterLedgerBridge: corporate action {CorporateActionId} for {Ticker} was cancelled after a prior posting; reversal requires the governed restatement path.",
                        action.CorpActId, normalizedTicker);
                }

                continue;
            }

            if (existingIds.Contains(action.CorpActId))
                continue;

            if (priorPosted)
            {
                _logger.LogWarning(
                    "SecurityMasterLedgerBridge: corporate action {CorporateActionId} for {Ticker} was amended after a prior posting; correction requires the governed restatement path.",
                    action.CorpActId, normalizedTicker);
                continue;
            }

            if (!CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor))
            {
                _logger.LogDebug(
                    "SecurityMasterLedgerBridge: skipping unhandled event type {EventType} for {Ticker}",
                    action.EventType, normalizedTicker);
                continue;
            }

            var ts = new DateTimeOffset(action.ExDate.Year, action.ExDate.Month, action.ExDate.Day,
                                        0, 0, 0, TimeSpan.Zero);
            var eventType = descriptor.CanonicalName;
            var meta = new JournalEntryMetadata(
                ActivityType: eventType,
                Symbol: normalizedTicker,
                SecurityId: securityId,
                LedgerView: LedgerViewKind.SecurityMaster);

            switch (descriptor.LedgerPostingKind)
            {
                case CorporateActionLedgerPostingKind.DividendDeclaration when action.DividendPerShare.HasValue:
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
                    // Return of capital reduces the security's carrying value instead of
                    // recognizing dividend income; the receivable/receipt legs are shared.
                    var creditAccount = eventType == CorporateActionEventTypes.ReturnOfCapital
                        ? LedgerAccounts.Securities(normalizedTicker)
                        : LedgerAccounts.DividendIncome;
                    PostDividendDeclaration(ledger, action, normalizedTicker, ts, meta, grossDividend, postingContext.PositionQuantity, creditAccount);
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

                case CorporateActionLedgerPostingKind.SplitMemo when action.SplitRatio.HasValue:
                    PostSplitMemo(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                case CorporateActionLedgerPostingKind.Distribution:
                    PostNonCashLifecycleMemo(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                case CorporateActionLedgerPostingKind.FactorPaydown when action.DistributionRatio.HasValue:
                    var projection = ProjectFactorPaydown(action, normalizedTicker, postingContext, ts);
                    if (!projection.ProducesPostingCandidate)
                    {
                        _logger.LogWarning(
                            "SecurityMasterLedgerBridge skipped factor paydown {CorporateActionId} for {Ticker}: {Issues}",
                            action.CorpActId,
                            normalizedTicker,
                            string.Join("; ", projection.Issues.Select(static issue => issue.Message)));
                        break;
                    }

                    PostFactorPaydown(ledger, action, normalizedTicker, ts, meta, projection.PrincipalPaydown!.Value);
                    posted++;
                    break;

                case CorporateActionLedgerPostingKind.RedemptionMemo when action.RedemptionPricePercentOfPar.HasValue:
                    PostRedemptionMemo(ledger, action, normalizedTicker, ts, meta, eventType);
                    posted++;
                    break;

                case CorporateActionLedgerPostingKind.DelistingMemo:
                    PostDelistingMemo(ledger, action, normalizedTicker, ts, meta);
                    posted++;
                    break;

                default:
                    _logger.LogDebug(
                        "SecurityMasterLedgerBridge: skipping unhandled event type {EventType} for {Ticker}",
                        eventType, normalizedTicker);
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
        decimal positionQuantity,
        LedgerAccount creditAccount)
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
                    creditAccount, 0m, grossAmount, description),
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
        JournalEntryMetadata meta,
        decimal amount)
    {
        var description = $"Principal paydown {ticker} ex {action.ExDate:yyyy-MM-dd} factor delta {action.DistributionRatio!.Value:N6}";

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

    private FactorPaydownProjectionResult ProjectFactorPaydown(
        CorporateActionDto action,
        string ticker,
        CorporateActionLedgerPostingContext context,
        DateTimeOffset occurredAtUtc)
    {
        var factorDelta = action.DistributionRatio!.Value;
        var positionId = DeriveJournalId(action.SecurityId, $"factor-position:{ticker}");
        return _factorPaydownProjector.Project(new FactorPaydownProjectionRequest(
            action.SecurityId,
            positionId,
            PositionVersion: 1,
            ExpectedPositionVersion: 1,
            HeldFace: context.PositionQuantity,
            PriorFactor: 1m,
            CurrentFactor: 1m - factorDelta,
            Currency: action.Currency ?? string.Empty,
            EffectiveDate: action.ExDate,
            OccurredAtUtc: occurredAtUtc,
            SourceDomain: "SecurityMaster",
            SourceEntityId: action.CorpActId.ToString("D"),
            SourceContentHash: HashCorporateAction(action),
            EvidenceLinks: [$"security-master://corporate-actions/{action.CorpActId:D}"]));
    }

    private static string HashCorporateAction(CorporateActionDto action)
    {
        var source = string.Join(
            '|',
            action.CorpActId.ToString("N"),
            action.SecurityId.ToString("N"),
            action.EventType,
            action.ExDate.ToString("yyyy-MM-dd"),
            action.DistributionRatio?.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            action.Currency ?? string.Empty,
            action.LifecycleState ?? string.Empty);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()}";
    }

    private static void PostRedemptionMemo(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta,
        string eventType)
    {
        // Contractual-flow view: the redemption price is expressed as percent of par (a
        // per-100-par proxy amount); converting to actual cash against face-value lots is
        // the durable ledger's job, the same as per-share dividend declarations.
        var amount = action.RedemptionPricePercentOfPar!.Value;
        var description = $"{eventType} {ticker} at {amount:N4}% of par, ex {action.ExDate:yyyy-MM-dd}";

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

    private static void PostDelistingMemo(
        DomainLedger ledger,
        CorporateActionDto action,
        string ticker,
        DateTimeOffset ts,
        JournalEntryMetadata meta)
    {
        const decimal memoAmount = 1m;
        var description = $"Delisting {ticker} effective {action.ExDate:yyyy-MM-dd}";

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
