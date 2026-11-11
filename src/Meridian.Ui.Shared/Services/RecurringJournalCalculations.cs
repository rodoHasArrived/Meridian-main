using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Everything one calculated recurring occurrence may read. The accrual window is the inclusive
/// date range this occurrence owns: it starts the day after the previous occurrence (or at the
/// start of the covering accounting period for the first occurrence) and ends on the effective
/// date, so consecutive occurrences never accrue the same day twice.
/// </summary>
public sealed record RecurringJournalCalculationContext(
    RecurringJournalScope Scope,
    string ScheduleId,
    DateOnly EffectiveDate,
    DateOnly? WindowStart,
    DateTimeOffset OccurredAtUtc,
    string PeriodId,
    IReadOnlyDictionary<string, decimal> Parameters,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<JournalEvidenceReference> Evidence)
{
    /// <summary>The first day of the accrual window; fails closed when the period has no start date.</summary>
    public DateOnly RequireWindowStart()
        => WindowStart ?? throw new InvalidOperationException(
            "The covering accounting period has no start date, so the first accrual window cannot be determined.");

    /// <summary>Inclusive number of days in the accrual window.</summary>
    public int WindowDays => EffectiveDate.DayNumber - RequireWindowStart().DayNumber + 1;

    /// <summary>A numeric schedule term. Terms are never defaulted: a missing term blocks the draft.</summary>
    public decimal RequireParameter(string name)
        => Parameters.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"The recurring schedule is missing the required term '{name}'.");

    public string RequireInput(string name)
        => Inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"The calculated template is missing the required input '{name}'.");

    public string? OptionalInput(string name)
        => Inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>An explicit day-count year basis; the accrual refuses to guess one.</summary>
    public int RequireYearBasisDays()
    {
        var basis = RequireParameter("yearBasisDays");
        return basis is 360m or 365m or 366m
            ? (int)basis
            : throw new InvalidOperationException("The schedule term 'yearBasisDays' must be 360, 365 or 366.");
    }
}

/// <summary>
/// The outcome of a posting calculation: a balanced draft for human approval, or a recorded reason
/// why this occurrence has nothing to post.
/// </summary>
public sealed record RecurringJournalCalculationResult(AutomatedJournalDraft? Draft, string Summary)
{
    public static RecurringJournalCalculationResult NothingDue(string reason) => new(null, reason);
}

/// <summary>
/// A posting calculation the recurring-journal runner can run for a calculated template. An
/// implementation reads authoritative inputs, returns one balanced draft (or nothing due), and
/// throws <see cref="InvalidOperationException"/> to block the occurrence when an input is missing.
/// It never submits, approves or posts.
/// </summary>
public interface IRecurringJournalCalculation
{
    string Kind { get; }

    Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct);
}

/// <summary>Authoritative ledger reads for posting calculations.</summary>
public interface IRecurringJournalCalculationInputs
{
    /// <summary>The scope's ledger book including everything recorded through the end of <paramref name="asOf"/>.</summary>
    Task<IReadOnlyLedger> HydrateLedgerAsOfAsync(RecurringJournalScope scope, DateOnly asOf, CancellationToken ct);

    /// <summary>Open tax lots held in <paramref name="account"/> and acquired on or before <paramref name="asOf"/>.</summary>
    Task<IReadOnlyList<LedgerTaxLot>> ListOpenLotsAsync(RecurringJournalScope scope, LedgerAccount account, DateOnly asOf, CancellationToken ct);
}

/// <summary>Reads calculation inputs from the durable ledger journal store.</summary>
public sealed class LedgerRecurringJournalCalculationInputs(ILedgerJournalStore? journalStore = null) : IRecurringJournalCalculationInputs
{
    public async Task<IReadOnlyLedger> HydrateLedgerAsOfAsync(RecurringJournalScope scope, DateOnly asOf, CancellationToken ct)
        => await Store().HydrateLedgerAsOfAsync(scope.LedgerBookId, RecurringJournalCalculationMath.EndOfDay(asOf), ct: ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<LedgerTaxLot>> ListOpenLotsAsync(RecurringJournalScope scope, LedgerAccount account, DateOnly asOf, CancellationToken ct)
    {
        var records = await Store().ListOpenTaxLotsAsync(scope.LedgerBookId, account, ct).ConfigureAwait(false);
        return records
            .Where(record => record.OpenQuantity > 0m && record.AcquiredDate <= asOf)
            // Only face-value lots (quantity in units of 100 par, unit cost as a price per 100)
            // carry a premium or discount; other lots are ignored rather than misread as bonds.
            .Where(record => record.HasFaceValueTerms)
            .Select(record => new LedgerTaxLot(record.LotId, record.AcquiredDate, record.OpenQuantity, record.UnitCost,
                record.SecurityId == Guid.Empty ? null : record.SecurityId)
            { UnitKind = SecurityLotUnitKind.FaceValue, Multiplier = LedgerTaxLotFaceValueTerms.LedgerLotParBasis })
            .ToArray();
    }

    private ILedgerJournalStore Store() => journalStore ?? throw new InvalidOperationException(
        "The durable ledger is unavailable; calculated recurring journals are blocked.");
}

internal static class RecurringJournalCalculationMath
{
    public static DateTimeOffset EndOfDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    public static decimal NetAssets(IReadOnlyLedger ledger, DateOnly asOf)
    {
        var statements = LedgerFinancialStatementBuilder.BuildAsOf(ledger, EndOfDay(asOf));
        return statements.TotalAssets - statements.TotalLiabilities;
    }

    /// <summary>
    /// A fund-level accrual has no investor capital account. The approval flow treats a fund-event
    /// identity as private-capital context that must name one, so the draft is identified by its
    /// recurring occurrence, activity type and fund/period tags instead.
    /// </summary>
    public static AutomatedJournalDraft FundLevel(AutomatedJournalDraft draft, string description)
        => draft with { Description = description, Metadata = draft.Metadata with { FundEventId = null, FundEventType = null } };

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Rate(decimal rate) => rate.ToString("0.########", CultureInfo.InvariantCulture);
}

/// <summary>
/// Day-weighted management fee on the net assets the ledger held at the close of the day before the
/// accrual window. Terms: <c>annualRate</c>, <c>yearBasisDays</c>.
/// </summary>
public sealed class ManagementFeeAccrualCalculation(IRecurringJournalCalculationInputs inputs) : IRecurringJournalCalculation
{
    public const string CalculationKind = "management-fee-accrual";

    public string Kind => CalculationKind;

    public async Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct)
    {
        var annualRate = context.RequireParameter("annualRate");
        if (annualRate is <= 0m or > 1m)
            throw new InvalidOperationException("The schedule term 'annualRate' must be greater than 0 and at most 1.");
        var yearBasisDays = context.RequireYearBasisDays();
        var navDate = context.RequireWindowStart().AddDays(-1);
        var ledger = await inputs.HydrateLedgerAsOfAsync(context.Scope, navDate, ct).ConfigureAwait(false);
        var netAssets = RecurringJournalCalculationMath.NetAssets(ledger, navDate);
        if (netAssets <= 0m)
            return RecurringJournalCalculationResult.NothingDue(
                $"Net assets were {RecurringJournalCalculationMath.Money(netAssets)} at {navDate:yyyy-MM-dd}; no management fee accrues.");

        var draft = FundEconomicsJournalFactory.BuildManagementFeeAccrualDraft(context.Scope.FundProfileId, netAssets,
            annualRate, context.WindowDays, yearBasisDays, context.EffectiveDate, context.OccurredAtUtc, context.PeriodId, context.Evidence);
        var description = FormattableString.Invariant(
            $"{draft.Description}; rate {RecurringJournalCalculationMath.Rate(annualRate)} on net assets {RecurringJournalCalculationMath.Money(netAssets)} at {navDate:yyyy-MM-dd}, accrual {context.RequireWindowStart():yyyy-MM-dd} to {context.EffectiveDate:yyyy-MM-dd}");
        return new(RecurringJournalCalculationMath.FundLevel(draft, description), description);
    }
}

/// <summary>
/// Performance fee accrued to its cumulative amount above the high-water mark and hurdle. Net asset
/// value before the fee is the ledger's net assets plus the performance fee already accrued and
/// payable, and only the change from that accrued amount is drafted. Terms:
/// <c>performanceFeeRate</c>, <c>highWaterMark</c>, <c>hurdleAmount</c>, <c>crystallize</c> (0 or 1).
/// After a fee is crystallized and settled, register a new schedule version with the new mark.
/// </summary>
public sealed class PerformanceFeeAccrualCalculation(IRecurringJournalCalculationInputs inputs) : IRecurringJournalCalculation
{
    public const string CalculationKind = "performance-fee-accrual";

    public string Kind => CalculationKind;

    public async Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct)
    {
        var rate = context.RequireParameter("performanceFeeRate");
        var highWaterMark = context.RequireParameter("highWaterMark");
        var hurdle = context.RequireParameter("hurdleAmount");
        var crystallize = context.RequireParameter("crystallize") switch
        {
            0m => false,
            1m => true,
            _ => throw new InvalidOperationException("The schedule term 'crystallize' must be 0 or 1.")
        };
        if (rate is <= 0m or > 1m)
            throw new InvalidOperationException("The schedule term 'performanceFeeRate' must be greater than 0 and at most 1.");
        if (highWaterMark < 0m || hurdle < 0m)
            throw new InvalidOperationException("The schedule terms 'highWaterMark' and 'hurdleAmount' cannot be negative.");

        var fund = context.Scope.FundProfileId;
        var ledger = await inputs.HydrateLedgerAsOfAsync(context.Scope, context.EffectiveDate, ct).ConfigureAwait(false);
        var asOf = RecurringJournalCalculationMath.EndOfDay(context.EffectiveDate);
        var alreadyAccrued = Math.Max(0m, ledger.GetBalanceAsOf(LedgerAccounts.PerformanceFeePayableFor(fund), asOf));
        var navBeforeFee = RecurringJournalCalculationMath.NetAssets(ledger, context.EffectiveDate) + alreadyAccrued;

        var result = FundEconomicsJournalFactory.BuildPerformanceFeeAccrualDraft(fund, navBeforeFee, highWaterMark, hurdle,
            rate, crystallize, context.EffectiveDate, context.OccurredAtUtc, context.PeriodId, context.Evidence, alreadyAccrued);
        var basis = FormattableString.Invariant(
            $"net asset value before fee {RecurringJournalCalculationMath.Money(navBeforeFee)} at {context.EffectiveDate:yyyy-MM-dd}, high-water mark {RecurringJournalCalculationMath.Money(highWaterMark)}, hurdle {RecurringJournalCalculationMath.Money(hurdle)}, rate {RecurringJournalCalculationMath.Rate(rate)}, already accrued {RecurringJournalCalculationMath.Money(alreadyAccrued)}");
        if (result.Draft is null)
            return RecurringJournalCalculationResult.NothingDue(
                $"Cumulative performance fee {RecurringJournalCalculationMath.Money(result.PerformanceFee)} needs no further accrual ({basis}).");

        var description = crystallize
            ? FormattableString.Invariant($"{result.Draft.Description}; {basis}; crystallized, next high-water mark {RecurringJournalCalculationMath.Money(result.NewHighWaterMark)}")
            : FormattableString.Invariant($"{result.Draft.Description}; {basis}");
        return new(RecurringJournalCalculationMath.FundLevel(result.Draft, description), description);
    }
}

/// <summary>
/// Straight-line accrual of an annual fund expense over the accrual window. Terms:
/// <c>annualAmount</c>, <c>yearBasisDays</c>. Input: <c>expenseId</c>.
/// </summary>
public sealed class FundExpenseAccrualCalculation : IRecurringJournalCalculation
{
    public const string CalculationKind = "fund-expense-accrual";

    public string Kind => CalculationKind;

    public Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct)
    {
        var annualAmount = context.RequireParameter("annualAmount");
        if (annualAmount <= 0m)
            throw new InvalidOperationException("The schedule term 'annualAmount' must be positive.");
        var draft = FundEconomicsJournalFactory.BuildExpenseAccrualDraft(context.Scope.FundProfileId,
            context.RequireInput("expenseId"), annualAmount, context.WindowDays, context.RequireYearBasisDays(),
            context.EffectiveDate, context.OccurredAtUtc, context.PeriodId, context.Evidence);
        var description = FormattableString.Invariant(
            $"{draft.Description}; annual amount {RecurringJournalCalculationMath.Money(annualAmount)}, accrual {context.RequireWindowStart():yyyy-MM-dd} to {context.EffectiveDate:yyyy-MM-dd}");
        return Task.FromResult(new RecurringJournalCalculationResult(RecurringJournalCalculationMath.FundLevel(draft, description), description));
    }
}

/// <summary>
/// Coupon accrual and premium/discount amortization for one bond position, from the Security Master
/// cash-flow projection and the position's open lots. Each occurrence drafts the day-count share
/// of every coupon period that falls inside its accrual window, and the premium or discount
/// amortized over the same days. Projected principal paydowns are not drafted here: they are cash movements and
/// arrive through cash activity. Inputs: <c>securityId</c>, <c>ticker</c>, optional
/// <c>financialAccountId</c>.
/// </summary>
public sealed class FixedIncomeAccrualCalculation(
    IRecurringJournalCalculationInputs inputs,
    ISecurityMasterAmortizationLedgerBridge? bridge = null,
    ISecurityMasterCashFlowService? cashFlows = null) : IRecurringJournalCalculation
{
    public const string CalculationKind = "fixed-income-accrual";
    private const string AmortizationActivity = "FixedIncomeAmortization";

    public string Kind => CalculationKind;

    public async Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct)
    {
        if (bridge is null || cashFlows is null)
            throw new InvalidOperationException("The Security Master cash-flow service is unavailable; fixed-income accruals are blocked.");
        if (!Guid.TryParse(context.RequireInput("securityId"), out var securityId) || securityId == Guid.Empty)
            throw new InvalidOperationException("The calculated template input 'securityId' must be a security identifier.");
        var ticker = context.RequireInput("ticker").ToUpperInvariant();
        var financialAccountId = context.OptionalInput("financialAccountId");
        var windowStart = context.RequireWindowStart();
        var projectionAsOf = new DateTimeOffset(windowStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var projection = await cashFlows.GetProjectionAsOfAsync(securityId, StructuredCashFlowScenario.Base, projectionAsOf, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No cash-flow projection is available for {ticker}.");
        if (StructuredCashFlowLedgerGate.EvaluateBlockReason(projection) is { } blocked)
            throw new InvalidOperationException($"The cash-flow projection for {ticker} cannot be posted: {blocked}");

        var amortizedThrough = windowStart.AddDays(-1);
        var lots = (await inputs.ListOpenLotsAsync(context.Scope, LedgerAccounts.Securities(ticker, financialAccountId),
                context.EffectiveDate, ct).ConfigureAwait(false))
            .Where(lot => lot.SecurityId == securityId)
            .ToArray();

        // The bridge posts to a scratch ledger that is never persisted; its journals are the
        // calculation result and reach the real ledger only through the approved draft.
        var scratch = new DomainLedger();
        await bridge.PostProjectedCashFlowsAsync(securityId, ticker, scratch, new AmortizationLedgerPostingContext(
            lots, financialAccountId, StructuredCashFlowScenario.Base, ProjectionAsOf: projectionAsOf,
            AmortizedThrough: amortizedThrough,
            AccrualWindow: new AmortizationAccrualWindow(windowStart, context.EffectiveDate)), ct).ConfigureAwait(false);
        var journals = scratch.Journal
            .Where(journal => journal.Metadata.ActivityType == AmortizationActivity &&
                journal.Metadata.EffectiveDate is { } date && InWindow(date, windowStart, context.EffectiveDate))
            .ToArray();
        var lines = journals.SelectMany(journal => journal.Lines)
            .GroupBy(line => line.Account)
            .SelectMany(group => new[]
            {
                (account: group.Key, debit: group.Sum(line => line.Debit), credit: 0m, dimensions: (LedgerLineDimensionSet?)null),
                (account: group.Key, debit: 0m, credit: group.Sum(line => line.Credit), dimensions: (LedgerLineDimensionSet?)null)
            })
            .Where(line => line.debit != 0m || line.credit != 0m)
            .ToArray();
        if (lines.Length == 0)
            return RecurringJournalCalculationResult.NothingDue(
                $"{ticker} accrues no interest or amortization between {windowStart:yyyy-MM-dd} and {context.EffectiveDate:yyyy-MM-dd}.");

        var total = lines.Sum(line => line.debit);
        var description = FormattableString.Invariant(
            $"Fixed-income accrual and amortization {ticker} for {windowStart:yyyy-MM-dd} to {context.EffectiveDate:yyyy-MM-dd} ({RecurringJournalCalculationMath.Money(total)} from {lots.Length} open lot(s))");
        var journalEvent = new AutomatedJournalEvent(AutomatedJournalEventKind.RecurringTemplate, ticker, total,
            context.OccurredAtUtc, financialAccountId, description, securityId, EffectiveDate: context.EffectiveDate);
        var metadata = new JournalEntryMetadata(ActivityType: AmortizationActivity, Symbol: ticker, SecurityId: securityId,
            FinancialAccountId: financialAccountId, EffectiveDate: context.EffectiveDate);
        return new(new AutomatedJournalDraft(journalEvent, description, lines, metadata), description);
    }

    private static bool InWindow(DateOnly date, DateOnly start, DateOnly end) => date >= start && date <= end;
}

/// <summary>
/// Unrealized FX revaluation of foreign-currency asset and liability accounts at the effective
/// date. An account's foreign balance is the sum of its journal legs recorded in that currency; its
/// carrying value is its book-currency balance, which already includes earlier revaluations, so each
/// occurrence drafts only the change since the last one. The rate must come with its evidence
/// (quote date, derivation and table revision) and be no older than the schedule term
/// <c>maxRateAgeDays</c>; a missing, undated or stale rate blocks the draft. The memo records the
/// evidence of every rate used.
/// </summary>
public sealed class FxRevaluationCalculation(
    IRecurringJournalCalculationInputs inputs,
    IReconciliationFxRateProvider? rates = null) : IRecurringJournalCalculation
{
    public const string CalculationKind = "fx-revaluation";

    public string Kind => CalculationKind;

    public async Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct)
    {
        if (rates is null)
            throw new InvalidOperationException("No FX rate source is available; FX revaluation is blocked.");
        var maxRateAgeDays = context.RequireParameter("maxRateAgeDays");
        if (maxRateAgeDays < 0m || maxRateAgeDays != decimal.Truncate(maxRateAgeDays))
            throw new InvalidOperationException("The schedule term 'maxRateAgeDays' must be a whole number of days, zero or more.");
        var bookCurrency = context.Scope.Currency.Trim().ToUpperInvariant();
        var asOf = RecurringJournalCalculationMath.EndOfDay(context.EffectiveDate);
        var ledger = await inputs.HydrateLedgerAsOfAsync(context.Scope, context.EffectiveDate, ct).ConfigureAwait(false);

        var foreignLegs = ledger.Journal
            .Where(journal => journal.Timestamp <= asOf)
            .SelectMany(journal => journal.Lines)
            .Where(line => line.Currency is { IsFunctionalCurrency: false } &&
                line.Account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Liability)
            .GroupBy(line => line.Account)
            .ToArray();
        if (foreignLegs.Length == 0)
            return RecurringJournalCalculationResult.NothingDue(
                $"No asset or liability account carries a foreign-currency balance at {context.EffectiveDate:yyyy-MM-dd}.");

        var localBalances = new Dictionary<LedgerAccount, decimal>();
        var accountCurrencies = new Dictionary<LedgerAccount, string>();
        var carrying = new Dictionary<LedgerAccount, decimal>();
        foreach (var legs in foreignLegs)
        {
            var currencies = legs.Select(line => line.Currency!.TransactionCurrency).Distinct(StringComparer.Ordinal).ToArray();
            if (currencies.Length != 1)
                throw new InvalidOperationException(
                    $"Account '{legs.Key.Name}' holds legs in {string.Join(", ", currencies)}; revaluation requires one foreign currency per account.");
            if (legs.Any(line => !string.Equals(line.Currency!.FunctionalCurrency, bookCurrency, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Account '{legs.Key.Name}' has legs whose functional currency is not the book currency {bookCurrency}.");
            localBalances[legs.Key] = DomainLedger.CalculateNetBalance(legs.Key,
                legs.Sum(line => line.Currency!.TransactionDebit), legs.Sum(line => line.Currency!.TransactionCredit));
            accountCurrencies[legs.Key] = currencies[0];
            carrying[legs.Key] = ledger.GetBalanceAsOf(legs.Key, asOf);
        }

        var ratesToBook = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var rateEvidence = new List<string>();
        foreach (var currency in accountCurrencies.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!rates.TryResolveRate(currency, bookCurrency, context.EffectiveDate, out var resolved) || resolved.Rate <= 0m)
                throw new InvalidOperationException(
                    $"No {currency}/{bookCurrency} FX rate with a quote date and source is available for {context.EffectiveDate:yyyy-MM-dd}; FX revaluation is blocked.");
            var ageDays = context.EffectiveDate.DayNumber - resolved.RateDate.DayNumber;
            if (ageDays < 0 || ageDays > maxRateAgeDays)
                throw new InvalidOperationException(
                    $"The {currency}/{bookCurrency} FX rate is dated {resolved.RateDate:yyyy-MM-dd}, {ageDays} day(s) from {context.EffectiveDate:yyyy-MM-dd}; the schedule allows {maxRateAgeDays:0}. FX revaluation is blocked.");
            ratesToBook[currency] = resolved.Rate;
            rateEvidence.Add(FormattableString.Invariant(
                $"{currency}/{bookCurrency} {RecurringJournalCalculationMath.Rate(resolved.Rate)} dated {resolved.RateDate:yyyy-MM-dd} ({resolved.Method}, {resolved.Source})"));
        }

        var translation = MultiCurrencyLedgerTranslator.Translate(localBalances, bookCurrency, ratesToBook, accountCurrencies, carrying);
        // Each exposure yields a debit/credit pair for the same amount, so rounding every line to
        // the book currency keeps the draft balanced.
        var lines = MultiCurrencyLedgerTranslator.BuildUnrealizedFxRevaluationLines(translation)
            .Select(line => (line.account, debit: RoundCurrency(line.debit, bookCurrency), credit: RoundCurrency(line.credit, bookCurrency),
                dimensions: (LedgerLineDimensionSet?)null))
            .Where(line => line.debit != 0m || line.credit != 0m)
            .ToArray();
        var rateText = string.Join("; ", rateEvidence);
        if (lines.Length == 0)
            return RecurringJournalCalculationResult.NothingDue(
                $"Foreign-currency balances already carry their {context.EffectiveDate:yyyy-MM-dd} value ({rateText}).");

        var total = lines.Sum(line => line.debit);
        var description = FormattableString.Invariant(
            $"Unrealized FX revaluation at {context.EffectiveDate:yyyy-MM-dd} for {foreignLegs.Length} account(s), {RecurringJournalCalculationMath.Money(total)}; {rateText}");
        var journalEvent = new AutomatedJournalEvent(AutomatedJournalEventKind.RecurringTemplate, bookCurrency, total,
            context.OccurredAtUtc, Description: description, EffectiveDate: context.EffectiveDate);
        var metadata = new JournalEntryMetadata(ActivityType: "fx-revaluation", EffectiveDate: context.EffectiveDate);
        return new(new AutomatedJournalDraft(journalEvent, description, lines, metadata), description);
    }
}
