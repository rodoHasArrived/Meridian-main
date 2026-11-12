using Meridian.Contracts.Ledger;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.Accounting.Lots;

public sealed record BondIncomeCouponLotAllocationDto(Guid TaxLotRecordId, long ExpectedLotVersion,
    decimal HeldParFace, decimal RawAmount, decimal CashAmount)
{
    public DateOnly AcquisitionSettlementDate { get; init; }
    public IReadOnlyList<BondCalculationInput> EntitlementInputs { get; init; } = [];
}

public sealed record BondIncomeCouponAllocationDto(string ModelVersion, DateOnly PaymentDate,
    DateOnly AccrualStart, DateOnly AccrualEnd, string Currency,
    IReadOnlyList<BondIncomeCouponLotAllocationDto> Lots);

/// <summary>One native cash boundary for a holder obligation, deterministically allocated to its lots.</summary>
public static class BondIncomeCouponRounding
{
    public const string ModelVersion = "bond-income-coupon-allocation-v1";

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
        var computed = Allocate(row.PaymentDate, row.AccrualStart, row.AccrualEnd, row.Currency, row.Lots);
        if (!computed.Lots.SequenceEqual(row.Lots))
            throw new ArgumentException("Coupon cash allocation differs from the deterministic native boundary.");
        return selected.CashAmount;
    }

    public static decimal CompletedCash(Guid lotId, BondIncomeInstructionDto income)
    {
        var flows = income.CurrentProjection.CashFlows.Where(f => f.PaymentDate > income.AcquisitionDate
            && f.PaymentDate <= income.ThroughDate && f.Interest != 0m)
            .GroupBy(f => (f.PaymentDate, f.AccrualStart)).ToArray();
        return flows.Sum(group => AllocatedCash(lotId, income, group.Key.PaymentDate, group.Key.AccrualStart));
    }
}
