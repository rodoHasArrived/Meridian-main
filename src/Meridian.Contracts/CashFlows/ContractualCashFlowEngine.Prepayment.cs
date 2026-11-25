using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.CashFlows;

public static partial class ContractualCashFlowEngine
{
    /// <summary>Conditional survival over [start,end), split at month and assumption boundaries.</summary>
    public static decimal PrepaymentFraction(CashFlowPrepaymentAssumption assumption, DateOnly start, DateOnly end)
        => PrepaymentFraction(assumption, start, end, ContractualCashFlowVersions.Prepayment);

    /// <summary>
    /// The prepayment fraction under an explicit arithmetic version. Version 2 keeps survival in
    /// decimal; version 1 is the original double arithmetic, kept byte-identical for replay of runs
    /// recorded under it.
    /// </summary>
    internal static decimal PrepaymentFraction(CashFlowPrepaymentAssumption assumption, DateOnly start, DateOnly end, string prepaymentVersion)
    {
        if (assumption.Method == CashFlowPrepaymentMethod.Amount)
            throw new ArgumentException("An amount is not a survival rate.");
        if (!Enum.IsDefined(assumption.Method) || assumption.Value < 0 || assumption.Method != CashFlowPrepaymentMethod.Psa && assumption.Value > 1)
            throw new ArgumentException("Invalid prepayment rate.");
        start = start > assumption.StartDate ? start : assumption.StartDate;
        end = end < assumption.EndDate ? end : assumption.EndDate;
        if (start >= end)
            return 0;
        return prepaymentVersion switch
        {
            ContractualCashFlowVersions.Prepayment => DecimalPrepaymentFraction(assumption, start, end),
            ContractualCashFlowVersions.PrepaymentV1 => DoublePrepaymentFractionV1(assumption, start, end),
            _ => throw new ArgumentException($"Unsupported prepayment version '{prepaymentVersion}'.", nameof(prepaymentVersion))
        };
    }

    private static decimal DecimalPrepaymentFraction(CashFlowPrepaymentAssumption assumption, DateOnly start, DateOnly end)
    {
        var survival = 1m;
        for (var cursor = start; cursor < end;)
        {
            var nextMonth = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1);
            var stop = nextMonth < end ? nextMonth : end;
            var days = (decimal)(stop.DayNumber - cursor.DayNumber);
            var daysInMonth = (decimal)DateTime.DaysInMonth(cursor.Year, cursor.Month);
            var value = MonthlyRate(assumption, cursor);
            var exponent = assumption.Method switch
            {
                CashFlowPrepaymentMethod.Smm => days / daysInMonth,
                CashFlowPrepaymentMethod.Cpr => days / DayCountConventions.DaysPerYear365,
                _ => days / daysInMonth / 12m
            };
            survival *= DecimalMath.Power(1m - value, exponent);
            cursor = stop;
        }
        return 1m - survival;
    }

    private static decimal DoublePrepaymentFractionV1(CashFlowPrepaymentAssumption assumption, DateOnly start, DateOnly end)
    {
        var survival = 1d;
        for (var cursor = start; cursor < end;)
        {
            var nextMonth = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1);
            var stop = nextMonth < end ? nextMonth : end;
            var days = stop.DayNumber - cursor.DayNumber;
            var value = MonthlyRate(assumption, cursor);
            var exponent = assumption.Method == CashFlowPrepaymentMethod.Smm
                ? (double)days / DateTime.DaysInMonth(cursor.Year, cursor.Month)
                : assumption.Method == CashFlowPrepaymentMethod.Cpr ? (double)days / (double)DayCountConventions.DaysPerYear365
                : (double)days / DateTime.DaysInMonth(cursor.Year, cursor.Month) / 12d;
            survival *= Math.Pow(1d - (double)value, exponent);
            cursor = stop;
        }
        return (decimal)(1d - survival);
    }

    /// <summary>The assumption's rate for the month containing <paramref name="cursor"/>; PSA seasons it.</summary>
    private static decimal MonthlyRate(CashFlowPrepaymentAssumption assumption, DateOnly cursor)
    {
        if (assumption.Method != CashFlowPrepaymentMethod.Psa)
            return assumption.Value;
        var origin = assumption.SeasoningOrigin ?? throw new ArgumentException("PSA requires a seasoning origin.");
        var month = (cursor.Year - origin.Year) * 12 + cursor.Month - origin.Month + 1;
        return decimal.Min(1m, 0.002m * Math.Clamp(month, 0, 30) * assumption.Value);
    }
}
