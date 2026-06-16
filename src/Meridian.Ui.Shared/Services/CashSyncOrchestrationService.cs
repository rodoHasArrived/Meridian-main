namespace Meridian.Ui.Shared.Services;

public sealed record SourceAvailabilityWindow(string SourceId, TimeOnly StartLocal, TimeOnly EndLocal, bool IsAvailable);

public sealed record RegionalSyncWindow(
    string Region,
    string TimeZoneId,
    TimeOnly SyncStartLocal,
    TimeOnly SyncEndLocal,
    IReadOnlyList<SourceAvailabilityWindow> Sources);

public sealed record SyncAccountSnapshot(
    string AccountId,
    string Counterparty,
    string Currency,
    decimal ClosingBalance,
    decimal NetDelta,
    DateOnly BusinessDate,
    bool IsComplete);

public sealed record SyncValidationIssue(string Code, string Message, string Severity);

public sealed record SyncValidationResult(bool Passed, IReadOnlyList<SyncValidationIssue> Issues);

public sealed record FxConversionReference(string BaseCurrency, string QuoteCurrency, decimal Rate, DateTimeOffset AsOf);

public sealed record PaymentInstruction(
    string InstructionId,
    string AccountId,
    string Counterparty,
    string Currency,
    decimal Amount,
    DateOnly ValueDate,
    string Purpose,
    string Status);

public sealed record SettlementInstruction(
    string InstructionId,
    string AccountId,
    string Currency,
    decimal GrossAmount,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    DateTimeOffset CutoffAt,
    string Status);

public sealed record CouponEvent(
    string EventId,
    string SecurityId,
    string AccountId,
    string Currency,
    decimal Amount,
    DateOnly ExDate,
    DateOnly PayDate,
    string Status);

public sealed record ApprovalStep(
    string StepId,
    string Role,
    string Approver,
    bool IsRequired,
    bool IsApproved,
    DateTimeOffset? DecidedAt);

public sealed record ApprovalPolicy(
    decimal DualControlThreshold,
    IReadOnlyDictionary<string, decimal> CurrencyThresholds,
    IReadOnlyDictionary<string, decimal> AccountThresholds,
    IReadOnlySet<string> HighRiskCounterparties);

public sealed record ApprovalDecision(bool RequiresDualControl, IReadOnlyList<ApprovalStep> Steps);

public sealed record CashForecastPoint(DateOnly Date, string AccountId, string Currency, decimal ExpectedInflow, decimal ExpectedOutflow, decimal Net);

public sealed record LiquidityWarning(string AccountId, string Currency, DateOnly Date, decimal ProjectedNet, string Message);

public sealed record StpTransitionResult(string EntityId, string State, string? ExceptionReason, bool RoutedToBreakQueue);

public interface ICashSyncOrchestrationService
{
    bool IsSyncWindowOpen(DateTimeOffset utcNow, RegionalSyncWindow window);
    SyncValidationResult ValidateCompleteness(IEnumerable<SyncAccountSnapshot> snapshots);
    SyncValidationResult ValidateOutliers(IEnumerable<SyncAccountSnapshot> currentDay, IEnumerable<SyncAccountSnapshot> priorDay, decimal outlierThreshold);
    ApprovalDecision BuildApprovalChain(PaymentInstruction paymentInstruction, ApprovalPolicy policy, string primaryApprover, string secondaryApprover);
    DateOnly ResolveSettlementDate(DateOnly tradeDate, TimeOnly localCutoff, TimeOnly submittedAtLocal, int standardSettlementDays);
    IReadOnlyList<CashForecastPoint> BuildCashLadder(DateOnly startDate, int horizonDays, IEnumerable<PaymentInstruction> payments, IEnumerable<SettlementInstruction> settlements, IEnumerable<CouponEvent> coupons);
    IReadOnlyList<LiquidityWarning> BuildLiquidityWarnings(IEnumerable<CashForecastPoint> ladder, decimal minimumNetThreshold);
    StpTransitionResult AdvanceStpState(string entityId, bool validated, bool approved, string? exceptionReason);
}

public sealed class CashSyncOrchestrationService : ICashSyncOrchestrationService
{
    public bool IsSyncWindowOpen(DateTimeOffset utcNow, RegionalSyncWindow window)
    {
        var localNow = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(window.TimeZoneId));
        var inWindow = localNow.TimeOfDay >= window.SyncStartLocal.ToTimeSpan() && localNow.TimeOfDay <= window.SyncEndLocal.ToTimeSpan();
        var sourcesReady = window.Sources.All(source => source.IsAvailable && localNow.TimeOfDay >= source.StartLocal.ToTimeSpan() && localNow.TimeOfDay <= source.EndLocal.ToTimeSpan());
        return inWindow && sourcesReady;
    }

    public SyncValidationResult ValidateCompleteness(IEnumerable<SyncAccountSnapshot> snapshots)
    {
        var issues = snapshots
            .Where(snapshot => !snapshot.IsComplete)
            .Select(snapshot => new SyncValidationIssue("completeness", $"{snapshot.AccountId}/{snapshot.Counterparty} is incomplete for {snapshot.BusinessDate:yyyy-MM-dd}.", "Error"))
            .ToList();

        return new SyncValidationResult(issues.Count == 0, issues);
    }

    public SyncValidationResult ValidateOutliers(IEnumerable<SyncAccountSnapshot> currentDay, IEnumerable<SyncAccountSnapshot> priorDay, decimal outlierThreshold)
    {
        var priorByKey = priorDay.ToDictionary(item => $"{item.AccountId}|{item.Counterparty}|{item.Currency}");
        var issues = new List<SyncValidationIssue>();

        foreach (var current in currentDay)
        {
            var key = $"{current.AccountId}|{current.Counterparty}|{current.Currency}";
            if (!priorByKey.TryGetValue(key, out var prior) || prior.NetDelta == 0m)
            {
                continue;
            }

            var jump = Math.Abs((current.NetDelta - prior.NetDelta) / prior.NetDelta);
            if (jump >= outlierThreshold)
            {
                issues.Add(new SyncValidationIssue("outlier", $"Delta jump for {current.AccountId}/{current.Currency} was {jump:P1} vs prior day.", "Warning"));
            }
        }

        return new SyncValidationResult(issues.Count == 0, issues);
    }

    public ApprovalDecision BuildApprovalChain(PaymentInstruction paymentInstruction, ApprovalPolicy policy, string primaryApprover, string secondaryApprover)
    {
        var currencyThreshold = policy.CurrencyThresholds.TryGetValue(paymentInstruction.Currency, out var cThreshold) ? cThreshold : decimal.MaxValue;
        var accountThreshold = policy.AccountThresholds.TryGetValue(paymentInstruction.AccountId, out var aThreshold) ? aThreshold : decimal.MaxValue;
        var threshold = Math.Min(policy.DualControlThreshold, Math.Min(currencyThreshold, accountThreshold));

        var highRisk = policy.HighRiskCounterparties.Contains(paymentInstruction.Counterparty);
        var requiresDual = highRisk || Math.Abs(paymentInstruction.Amount) >= threshold;

        var steps = new List<ApprovalStep>
        {
            new(Guid.NewGuid().ToString("N"), "Primary", primaryApprover, true, false, null)
        };

        if (requiresDual)
        {
            steps.Add(new ApprovalStep(Guid.NewGuid().ToString("N"), "Secondary", secondaryApprover, true, false, null));
        }

        return new ApprovalDecision(requiresDual, steps);
    }

    public DateOnly ResolveSettlementDate(DateOnly tradeDate, TimeOnly localCutoff, TimeOnly submittedAtLocal, int standardSettlementDays)
    {
        var days = submittedAtLocal > localCutoff ? standardSettlementDays + 1 : standardSettlementDays;
        return tradeDate.AddDays(days);
    }

    public IReadOnlyList<CashForecastPoint> BuildCashLadder(DateOnly startDate, int horizonDays, IEnumerable<PaymentInstruction> payments, IEnumerable<SettlementInstruction> settlements, IEnumerable<CouponEvent> coupons)
    {
        var horizonEnd = startDate.AddDays(horizonDays);
        var points = new Dictionary<string, CashForecastPoint>();

        static string Key(DateOnly d, string acct, string ccy) => $"{d:yyyyMMdd}|{acct}|{ccy}";

        void Add(DateOnly date, string accountId, string currency, decimal inflow, decimal outflow)
        {
            if (date < startDate || date > horizonEnd)
                return;
            var key = Key(date, accountId, currency);
            if (!points.TryGetValue(key, out var existing))
            {
                existing = new CashForecastPoint(date, accountId, currency, 0m, 0m, 0m);
            }

            var next = existing with
            {
                ExpectedInflow = existing.ExpectedInflow + inflow,
                ExpectedOutflow = existing.ExpectedOutflow + outflow,
                Net = existing.Net + inflow - outflow
            };
            points[key] = next;
        }

        foreach (var payment in payments)
        {
            if (payment.Amount >= 0m)
                Add(payment.ValueDate, payment.AccountId, payment.Currency, payment.Amount, 0m);
            else
                Add(payment.ValueDate, payment.AccountId, payment.Currency, 0m, Math.Abs(payment.Amount));
        }

        foreach (var settlement in settlements)
        {
            if (settlement.GrossAmount >= 0m)
                Add(settlement.SettlementDate, settlement.AccountId, settlement.Currency, settlement.GrossAmount, 0m);
            else
                Add(settlement.SettlementDate, settlement.AccountId, settlement.Currency, 0m, Math.Abs(settlement.GrossAmount));
        }

        foreach (var coupon in coupons)
        {
            Add(coupon.PayDate, coupon.AccountId, coupon.Currency, coupon.Amount, 0m);
        }

        return points.Values.OrderBy(point => point.Date).ThenBy(point => point.AccountId).ThenBy(point => point.Currency).ToArray();
    }

    public IReadOnlyList<LiquidityWarning> BuildLiquidityWarnings(IEnumerable<CashForecastPoint> ladder, decimal minimumNetThreshold)
        => ladder
            .Where(point => point.Net < minimumNetThreshold)
            .Select(point => new LiquidityWarning(point.AccountId, point.Currency, point.Date, point.Net, $"Projected net {point.Net} is below threshold {minimumNetThreshold}."))
            .ToArray();

    public StpTransitionResult AdvanceStpState(string entityId, bool validated, bool approved, string? exceptionReason)
    {
        if (!string.IsNullOrWhiteSpace(exceptionReason))
        {
            return new StpTransitionResult(entityId, "Exception", exceptionReason, true);
        }

        if (!validated)
        {
            return new StpTransitionResult(entityId, "ValidationPending", null, false);
        }

        if (!approved)
        {
            return new StpTransitionResult(entityId, "ApprovalPending", null, false);
        }

        return new StpTransitionResult(entityId, "Settled", null, false);
    }
}
