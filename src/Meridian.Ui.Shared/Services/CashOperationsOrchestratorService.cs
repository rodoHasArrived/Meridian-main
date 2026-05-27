using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class CashOperationsOrchestratorService
{
    public IReadOnlyList<CashSyncWindow> BuildDailySyncWindows(
        DateTimeOffset nowUtc,
        IReadOnlyList<CashSyncWindow> configuredWindows)
    {
        ArgumentNullException.ThrowIfNull(configuredWindows);

        return configuredWindows
            .Where(window => window.SourceAvailability.Values.Any(v => v is CashSyncSourceAvailability.Available or CashSyncSourceAvailability.Delayed))
            .OrderBy(window => ResolveWindowStartUtc(nowUtc, window))
            .ToArray();
    }

    public SyncValidationResult ValidateSync(
        IReadOnlyList<SyncCompletenessResult> completeness,
        IReadOnlyList<DeltaOutlierResult> outliers)
    {
        ArgumentNullException.ThrowIfNull(completeness);
        ArgumentNullException.ThrowIfNull(outliers);

        var warnings = new List<string>();
        if (completeness.Any(c => !c.IsComplete)) warnings.Add("Completeness gaps detected.");
        if (outliers.Any(o => o.IsOutlier)) warnings.Add("Outlier deltas exceed policy threshold.");

        return new SyncValidationResult(warnings.Count == 0, completeness, outliers, warnings);
    }

    public ApprovalDecision EvaluateApproval(
        decimal amount,
        string currency,
        Guid accountId,
        string riskBucket,
        ApprovalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(riskBucket);
        ArgumentNullException.ThrowIfNull(policy);

        var reasons = new List<string>();
        var queue = "ops-standard";
        var dualControl = policy.RequireDualControl;

        if (policy.MinimumAmount is { } min && amount < min)
        {
            reasons.Add($"Amount {amount} below minimum {min}.");
            queue = "ops-review";
        }

        if (policy.HighValueThreshold is { } high && amount >= high)
        {
            reasons.Add($"Amount {amount} reached high-value threshold {high}.");
            queue = "risk-approval";
            dualControl = true;
        }

        if (policy.AllowedCurrencies is { Count: > 0 } currencies && !currencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add($"Currency {currency} requires treasury routing.");
            queue = "treasury-approval";
        }

        if (policy.RoutedAccountIds is { Count: > 0 } accounts && accounts.Contains(accountId))
        {
            reasons.Add("Account-specific override route applied.");
            queue = "account-approval";
        }

        if (policy.RoutedRiskBuckets is { Count: > 0 } risks && risks.Contains(riskBucket, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add($"Risk bucket {riskBucket} requires risk routing.");
            queue = "risk-approval";
            dualControl = true;
        }

        return new ApprovalDecision(queue, dualControl, reasons);
    }

    public DateOnly ResolveSettlementDateWithCutoff(DateTimeOffset bookingTimeLocal, TimeOnly cutoffLocal, int normalSettlementLagDays)
    {
        var tradeDate = DateOnly.FromDateTime(bookingTimeLocal.Date);
        var sameDayAllowed = TimeOnly.FromDateTime(bookingTimeLocal.DateTime) <= cutoffLocal;
        var lag = sameDayAllowed ? normalSettlementLagDays : normalSettlementLagDays + 1;
        return tradeDate.AddDays(Math.Max(0, lag));
    }

    public CashForecastResult BuildCashForecast(
        DateOnly asOf,
        IReadOnlyDictionary<(Guid AccountId, string Currency), decimal> openingBalances,
        IReadOnlyList<PaymentInstruction> payments,
        IReadOnlyList<SettlementInstruction> settlements,
        IReadOnlyList<CouponEvent> coupons,
        IReadOnlyList<FxConversionReference> fx,
        decimal liquidityWarningThreshold)
    {
        var timeline = new List<CashFlowProjectionPoint>();
        var warnings = new List<string>();
        var fxMap = fx.ToDictionary(x => (x.BaseCurrency.ToUpperInvariant(), x.QuoteCurrency.ToUpperInvariant()), x => x.Rate);

        foreach (var bucket in openingBalances)
        {
            var accountId = bucket.Key.AccountId;
            var currency = bucket.Key.Currency;
            var running = bucket.Value;
            for (var i = 0; i < 5; i++)
            {
                var day = asOf.AddDays(i);
                var inflow = coupons.Where(c => c.AccountId == accountId && c.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase) && c.PayDate == day).Sum(c => c.CouponAmount);
                var outflow = payments.Where(p => p.AccountId == accountId && p.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase) && p.ValueDate == day).Sum(p => p.Amount)
                    + settlements.Where(s => s.AccountId == accountId && s.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase) && s.SettlementDate == day).Sum(s => s.GrossAmount);

                var net = inflow - outflow;
                running += net;
                var warn = running < liquidityWarningThreshold;
                var detail = warn ? $"Projected balance {running} {currency} is below threshold {liquidityWarningThreshold}." : "";
                if (warn) warnings.Add($"{accountId} {currency} {day:yyyy-MM-dd}: {detail}");

                timeline.Add(new CashFlowProjectionPoint(accountId, currency, day, inflow, outflow, net, running, warn, detail));
            }
        }

        _ = fxMap; // reserved for cross-currency normalization in follow-up integration.
        return new CashForecastResult(asOf, timeline.OrderBy(t => t.Date).ThenBy(t => t.AccountId).ToArray(), warnings);
    }

    public IReadOnlyList<StpStateTransition> BuildStpTrail(Guid instructionId, bool validationPassed, bool approved, bool settled, string? exceptionReason)
    {
        var now = DateTimeOffset.UtcNow;
        var trail = new List<StpStateTransition> { new(instructionId, StpProcessingState.Received, now, "Instruction received.") };
        if (!validationPassed)
        {
            trail.Add(new(instructionId, StpProcessingState.Exception, now, "Validation failed.", exceptionReason ?? "validation-failed"));
            return trail;
        }

        trail.Add(new(instructionId, StpProcessingState.Validated, now, "Validation passed."));
        if (!approved)
        {
            trail.Add(new(instructionId, StpProcessingState.Exception, now, "Approval missing.", exceptionReason ?? "approval-pending"));
            return trail;
        }

        trail.Add(new(instructionId, StpProcessingState.Approved, now, "Approved by policy chain."));
        trail.Add(new(instructionId, StpProcessingState.QueuedForSettlement, now, "Queued for settlement."));
        trail.Add(new(instructionId, settled ? StpProcessingState.Settled : StpProcessingState.Exception, now,
            settled ? "Settled." : "Settlement failed.", settled ? null : exceptionReason ?? "settlement-failed"));
        return trail;
    }

    private static DateTimeOffset ResolveWindowStartUtc(DateTimeOffset nowUtc, CashSyncWindow window)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(window.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var localDateTime = localNow.Date + window.WindowStart.ToTimeSpan();
        var offset = tz.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }
}
