using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;

using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// One position an automated producer could not turn into an event, with the reason.
/// </summary>
public sealed record AutomatedJournalEventProductionSkip(string Subject, string Reason);

/// <summary>
/// Events produced for automated journal intake plus the skips that need attention.
/// </summary>
public sealed record AutomatedJournalEventProduction(
    IReadOnlyList<AutomatedJournalEvent> Events,
    IReadOnlyList<AutomatedJournalEventProductionSkip> Skipped);

/// <summary>
/// One held position eligible for dividend accrual production.
/// </summary>
public sealed record DividendAccrualPosition(
    string Symbol,
    decimal Quantity,
    Guid? SecurityId = null,
    string? FinancialAccountId = null);

/// <summary>
/// Request to produce dividend-declared events from Security Master corporate actions
/// whose ex-date falls inside the window. A positive <paramref name="WithholdingTaxRate"/>
/// additionally accrues withholding tax against each declared dividend.
/// </summary>
public sealed record CorporateActionDividendRequest(
    IReadOnlyList<DividendAccrualPosition> Positions,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    DateTimeOffset AsOf,
    decimal WithholdingTaxRate = 0m);

/// <summary>
/// Produces <see cref="AutomatedJournalEventKind.DividendDeclared"/> events from the
/// Security Master's corporate-action store: effective (non-cancelled, amendment-collapsed)
/// dividend actions with an ex-date in the window, multiplied by the held quantity.
/// Unresolvable symbols and failed lookups surface as skips, never silently.
/// </summary>
public sealed class CorporateActionDividendEventProducer
{
    private readonly ISecurityMasterQueryService _securityMaster;

    public CorporateActionDividendEventProducer(ISecurityMasterQueryService securityMaster)
    {
        _securityMaster = securityMaster ?? throw new ArgumentNullException(nameof(securityMaster));
    }

    public async Task<AutomatedJournalEventProduction> ProduceAsync(
        CorporateActionDividendRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Positions.Count == 0)
            throw new ArgumentException("At least one position is required.", nameof(request));
        if (request.WindowEnd < request.WindowStart)
            throw new ArgumentException("Dividend window end must not precede its start.", nameof(request));
        if (request.WithholdingTaxRate is < 0m or >= 1m)
            throw new ArgumentOutOfRangeException(nameof(request), "Withholding tax rate must be at least 0 and below 1.");

        var events = new List<AutomatedJournalEvent>();
        var skipped = new List<AutomatedJournalEventProductionSkip>();

        foreach (var position in request.Positions)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(position.Symbol))
            {
                skipped.Add(new AutomatedJournalEventProductionSkip("(blank)", "Position symbol is required."));
                continue;
            }

            var symbol = position.Symbol.Trim().ToUpperInvariant();
            if (position.Quantity <= 0m)
            {
                skipped.Add(new AutomatedJournalEventProductionSkip(symbol, "Position quantity must be positive."));
                continue;
            }

            try
            {
                var securityId = position.SecurityId
                    ?? (await _securityMaster
                        .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, symbol, provider: null, ct)
                        .ConfigureAwait(false))?.SecurityId;
                if (securityId is null)
                {
                    skipped.Add(new AutomatedJournalEventProductionSkip(
                        symbol, "Ticker is unresolved in the Security Master."));
                    continue;
                }

                var actions = await _securityMaster
                    .GetCorporateActionsAsync(securityId.Value, ct)
                    .ConfigureAwait(false);
                var effectiveDividends = CorporateActionEffectiveStateProjector
                    .ProjectEffectiveActions(actions, request.AsOf)
                    .Where(action =>
                        action.EventType is CorporateActionEventTypes.Dividend or CorporateActionEventTypes.SpecialDividend &&
                        action.DividendPerShare is > 0m &&
                        action.ExDate >= request.WindowStart &&
                        action.ExDate <= request.WindowEnd);

                foreach (var dividend in effectiveDividends)
                {
                    var currencySuffix = dividend.Currency is null ? "" : " " + dividend.Currency;
                    var amount = decimal.Round(
                        position.Quantity * dividend.DividendPerShare!.Value, 2, MidpointRounding.AwayFromZero);
                    if (amount <= 0m)
                    {
                        skipped.Add(new AutomatedJournalEventProductionSkip(
                            symbol,
                            FormattableString.Invariant(
                                $"Corporate action {dividend.CorpActId:N} rounds to a non-positive amount.")));
                        continue;
                    }

                    var evidenceReferences = new[]
                    {
                        new JournalEvidenceReference(
                            EvidenceId: FormattableString.Invariant($"corp-act:{dividend.CorpActId:N}"),
                            Uri: FormattableString.Invariant(
                                $"/api/workstation/security-master/securities/{securityId.Value:D}/corporate-actions/{dividend.CorpActId:D}"),
                            Kind: "corporate-action",
                            SourceSystem: "security-master",
                            RetainedAtUtc: request.AsOf,
                            RetainedBy: "automated-journal",
                            SubjectId: symbol)
                    };

                    events.Add(new AutomatedJournalEvent(
                        AutomatedJournalEventKind.DividendDeclared,
                        symbol,
                        amount,
                        new DateTimeOffset(dividend.ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                        FinancialAccountId: position.FinancialAccountId,
                        Description: FormattableString.Invariant(
                            $"Dividend declared for {symbol}: {position.Quantity} × {dividend.DividendPerShare.Value}{currencySuffix} (ex {dividend.ExDate:yyyy-MM-dd})"),
                        SecurityId: securityId,
                        SourceEventId: dividend.CorpActId.ToString("N"),
                        EffectiveDate: dividend.ExDate,
                        IdempotencyKey: FormattableString.Invariant(
                            $"corp-act-dividend|{dividend.CorpActId:N}|{position.FinancialAccountId ?? "-"}"),
                        EvidenceReferences: evidenceReferences));

                    if (request.WithholdingTaxRate <= 0m)
                        continue;

                    var withholding = decimal.Round(
                        amount * request.WithholdingTaxRate, 2, MidpointRounding.AwayFromZero);
                    if (withholding <= 0m)
                    {
                        skipped.Add(new AutomatedJournalEventProductionSkip(
                            symbol,
                            FormattableString.Invariant(
                                $"Withholding on corporate action {dividend.CorpActId:N} rounds to a non-positive amount.")));
                        continue;
                    }

                    events.Add(new AutomatedJournalEvent(
                        AutomatedJournalEventKind.WithholdingTaxAccrued,
                        symbol,
                        withholding,
                        new DateTimeOffset(dividend.ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                        FinancialAccountId: position.FinancialAccountId,
                        Description: FormattableString.Invariant(
                            $"Withholding tax accrued for {symbol}: {request.WithholdingTaxRate:P2} of dividend {amount}{currencySuffix} (ex {dividend.ExDate:yyyy-MM-dd})"),
                        SecurityId: securityId,
                        SourceEventId: dividend.CorpActId.ToString("N"),
                        EffectiveDate: dividend.ExDate,
                        IdempotencyKey: FormattableString.Invariant(
                            $"corp-act-dividend-wht|{dividend.CorpActId:N}|{position.FinancialAccountId ?? "-"}"),
                        EvidenceReferences: evidenceReferences));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                skipped.Add(new AutomatedJournalEventProductionSkip(
                    symbol, $"Security Master lookup failed: {ex.Message}"));
            }
        }

        return new AutomatedJournalEventProduction(events, skipped);
    }
}

/// <summary>
/// Request to produce period fee accruals from fund fee terms. Amount conventions match
/// <see cref="PartnershipInvestorAccountingProjector"/>: the management fee is the period
/// rate applied to beginning NAV; the performance fee applies to the high-water-mark
/// excess net of the management fee.
/// </summary>
public sealed record FeeScheduleAccrualRequest(
    string FundId,
    string PeriodId,
    DateTimeOffset AsOf,
    decimal BeginningNav,
    decimal EndingNavBeforeFees,
    decimal HighWaterMark,
    decimal ManagementFeeRate,
    decimal PerformanceFeeRate);

/// <summary>
/// Produces <see cref="AutomatedJournalEventKind.ManagementFeeAccrued"/> and
/// <see cref="AutomatedJournalEventKind.PerformanceFeeAccrued"/> events for a period.
/// Zero-amount fees produce no event; that is a correct outcome, not a gap.
/// </summary>
public sealed class FeeScheduleAccrualEventProducer
{
    public AutomatedJournalEventProduction Produce(FeeScheduleAccrualRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FundId))
            throw new ArgumentException("Fund identifier is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PeriodId))
            throw new ArgumentException("Period identifier is required.", nameof(request));
        if (request.BeginningNav < 0m || request.EndingNavBeforeFees < 0m || request.HighWaterMark < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "NAV figures cannot be negative.");
        if (request.ManagementFeeRate is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(request), "Management fee rate must be between 0 and 1.");
        if (request.PerformanceFeeRate is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(request), "Performance fee rate must be between 0 and 1.");

        var fundId = request.FundId.Trim();
        var periodId = request.PeriodId.Trim();
        var effectiveDate = DateOnly.FromDateTime(request.AsOf.UtcDateTime);
        var managementFee = RoundCurrency(request.BeginningNav * request.ManagementFeeRate);
        var incentiveBase = Math.Max(0m, request.EndingNavBeforeFees - request.HighWaterMark - managementFee);
        var performanceFee = RoundCurrency(incentiveBase * request.PerformanceFeeRate);

        var events = new List<AutomatedJournalEvent>(2);
        if (managementFee > 0m)
        {
            events.Add(new AutomatedJournalEvent(
                AutomatedJournalEventKind.ManagementFeeAccrued,
                fundId,
                managementFee,
                request.AsOf,
                Description: FormattableString.Invariant(
                    $"Management fee accrued for {fundId} period {periodId} ({request.ManagementFeeRate:P2} of beginning NAV {request.BeginningNav})"),
                EffectiveDate: effectiveDate,
                IdempotencyKey: FormattableString.Invariant($"mgmt-fee|{fundId}|{periodId}")));
        }

        if (performanceFee > 0m)
        {
            events.Add(new AutomatedJournalEvent(
                AutomatedJournalEventKind.PerformanceFeeAccrued,
                fundId,
                performanceFee,
                request.AsOf,
                Description: FormattableString.Invariant(
                    $"Performance fee accrued for {fundId} period {periodId} ({request.PerformanceFeeRate:P2} of high-water excess {incentiveBase})"),
                EffectiveDate: effectiveDate,
                IdempotencyKey: FormattableString.Invariant($"perf-fee|{fundId}|{periodId}")));
        }

        return new AutomatedJournalEventProduction(events, []);
    }
}
