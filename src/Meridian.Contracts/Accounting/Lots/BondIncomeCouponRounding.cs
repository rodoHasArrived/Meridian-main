using Meridian.Contracts.Ledger;
using Meridian.Contracts.FixedIncome;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Accounting.Lots;

public sealed record BondIncomeCouponLotAllocationDto(Guid TaxLotRecordId, long ExpectedLotVersion,
    decimal HeldParFace, decimal RawAmount, decimal CashAmount)
{
    public DateOnly AcquisitionSettlementDate { get; init; }
    public IReadOnlyList<BondCalculationInput> EntitlementInputs { get; init; } = [];
}

public sealed record BondIncomeCouponAllocationDto(string ModelVersion, DateOnly PaymentDate,
    DateOnly AccrualStart, DateOnly AccrualEnd, string Currency,
    IReadOnlyList<BondIncomeCouponLotAllocationDto> Lots)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? HolderSnapshotDate { get; init; }
}

/// <summary>One native cash boundary for a holder obligation, deterministically allocated to its lots.</summary>
public static class BondIncomeCouponRounding
{
    public const string ModelVersion = "bond-income-coupon-allocation-v1";
    public const string TerminalEntitlementModelVersion = "bond-terminal-entitlement-v1";

    public static IReadOnlyList<BondWorkflowCashFlow> EarnedFlows(BondIncomeInstructionDto income)
    {
        var flows = income.CurrentProjection.CashFlows.Where(f => f.PaymentDate > income.AcquisitionDate && f.Interest != 0m).ToArray();
        if (income.TerminalEntitlement is not { } terminal)
            return flows.Where(f => f.PaymentDate <= income.ThroughDate).ToArray();
        var lastPrincipal = income.CurrentProjection.CashFlows.Where(f => f.Principal > 0m).OrderBy(f => f.PaymentDate).LastOrDefault();
        if (terminal.ModelVersion != TerminalEntitlementModelVersion || terminal.PrincipalDate > income.ThroughDate
            || lastPrincipal is null || lastPrincipal.PaymentDate != terminal.PrincipalDate || lastPrincipal.OutstandingPrincipal != 0m
            || flows.Any(f => f.PaymentDate > terminal.PrincipalDate && (f.IsForecast || f.AccrualStart >= terminal.PrincipalDate)))
            throw new ArgumentException("Terminal coupon entitlement requires observed fully earned rights after all contractual principal is due; future unearned or unfixed rights remain unsupported.");
        return flows;
    }

    public static (decimal Receivable, decimal Payable) EarnedSides(Guid lotId, BondIncomeInstructionDto income, decimal openShare = 1m)
    {
        var cash = EarnedFlows(income).GroupBy(f => (f.PaymentDate, f.AccrualStart))
            .Select(g => AllocatedCash(lotId, income, g.Key.PaymentDate, g.Key.AccrualStart)).ToArray();
        var accrued = income.TerminalEntitlement is null ? income.CurrentAccruedInterest * openShare : 0m;
        return (decimal.Round(cash.Sum(c => Math.Max(c, 0m)) + Math.Max(accrued, 0m)
                - Math.Max(income.AcquisitionAccruedInterest, 0m) * openShare, 12, MidpointRounding.ToEven),
            decimal.Round(cash.Sum(c => Math.Max(-c, 0m)) + Math.Max(-accrued, 0m)
                - Math.Max(-income.AcquisitionAccruedInterest, 0m) * openShare, 12, MidpointRounding.ToEven));
    }

    public static BondIncomeCouponAllocationDto Allocate(DateOnly paymentDate, DateOnly accrualStart,
        DateOnly accrualEnd, string currency, IReadOnlyList<BondIncomeCouponLotAllocationDto> inputs)
    {
        if (inputs.Count == 0 || inputs.Any(l => l.TaxLotRecordId == Guid.Empty || l.ExpectedLotVersion <= 0 || l.HeldParFace <= 0m)
            || inputs.Select(l => l.TaxLotRecordId).Distinct().Count() != inputs.Count
            || inputs.Select(l => Math.Sign(l.RawAmount)).Where(s => s != 0).Distinct().Count() > 1)
            throw new ArgumentException("Coupon cash allocation requires unique authoritative holder lots and one signed obligation.");
        var precision = CurrencyMinorUnits.GetPrecision(currency);
        var quantum = new decimal(1, 0, 0, false, (byte)precision);
        var sign = Math.Sign(inputs.Sum(l => l.RawAmount));
        var target = Math.Abs(LedgerCurrencyRounding.RoundCurrency(inputs.Sum(l => l.RawAmount), currency));
        var floors = inputs.Select(l => decimal.Round(Math.Abs(l.RawAmount), precision, MidpointRounding.ToZero)).ToArray();
        var residual = (target - floors.Sum()) / quantum;
        if (residual < 0m || residual != decimal.Truncate(residual) || residual > inputs.Count)
            throw new ArgumentException("Coupon cash allocation exceeds native rounding bounds.");
        var extras = Enumerable.Range(0, inputs.Count)
            .OrderByDescending(i => Math.Abs(inputs[i].RawAmount) - floors[i])
            .ThenBy(i => inputs[i].TaxLotRecordId.ToString("D"), StringComparer.Ordinal).Take((int)residual).ToHashSet();
        var lots = inputs.Select((lot, i) => lot with { CashAmount = sign * (floors[i] + (extras.Contains(i) ? quantum : 0m)) })
            .OrderBy(l => l.TaxLotRecordId.ToString("D"), StringComparer.Ordinal).ToArray();
        return new(ModelVersion, paymentDate, accrualStart, accrualEnd, currency, lots);
    }

    public static decimal AllocatedCash(Guid lotId, BondIncomeInstructionDto income,
        DateOnly paymentDate, DateOnly accrualStart)
    {
        var row = income.CouponCashAllocations?.SingleOrDefault(a => a.PaymentDate == paymentDate && a.AccrualStart == accrualStart)
            ?? throw new ArgumentException("Completed coupon cash requires the retained holder-level obligation allocation.");
        if (row.ModelVersion != ModelVersion || row.Currency != income.CurrentProjection.Currency
            || row.Lots.SingleOrDefault(l => l.TaxLotRecordId == lotId) is not { } selected)
            throw new ArgumentException("Coupon cash allocation does not bind this authoritative lot and currency.");
        if (income.TerminalEntitlement is { } terminal && row.PaymentDate > terminal.PrincipalDate
            && row.HolderSnapshotDate != terminal.PrincipalDate)
            throw new ArgumentException("Deferred terminal coupon cash must retain its actual terminal-date holder pool.");
        var computed = Allocate(row.PaymentDate, row.AccrualStart, row.AccrualEnd, row.Currency, row.Lots);
        if (!computed.Lots.SequenceEqual(row.Lots))
            throw new ArgumentException("Coupon cash allocation differs from the deterministic native boundary.");
        return selected.CashAmount;
    }

    public static decimal CompletedCash(Guid lotId, BondIncomeInstructionDto income)
    {
        var flows = EarnedFlows(income)
            .GroupBy(f => (f.PaymentDate, f.AccrualStart)).ToArray();
        return flows.Sum(group => AllocatedCash(lotId, income, group.Key.PaymentDate, group.Key.AccrualStart));
    }
}
