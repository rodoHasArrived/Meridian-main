namespace Meridian.Contracts.CashFlows;

public static partial class ContractualCashFlowEngine
{
    /// <summary>Conditional survival over [start,end), split at month and assumption boundaries.</summary>
    public static decimal PrepaymentFraction(CashFlowPrepaymentAssumption assumption, DateOnly start, DateOnly end)
    {
        if (assumption.Method == CashFlowPrepaymentMethod.Amount)
            throw new ArgumentException("An amount is not a survival rate.");
        if (!Enum.IsDefined(assumption.Method) || assumption.Value < 0 || assumption.Method != CashFlowPrepaymentMethod.Psa && assumption.Value > 1)
            throw new ArgumentException("Invalid prepayment rate.");
        start = start > assumption.StartDate ? start : assumption.StartDate;
        end = end < assumption.EndDate ? end : assumption.EndDate;
        if (start >= end)
            return 0;
        var survival = 1d;
        for (var cursor = start; cursor < end;)
        {
            var nextMonth = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1);
            var stop = nextMonth < end ? nextMonth : end;
            var days = stop.DayNumber - cursor.DayNumber;
            var value = assumption.Value;
            if (assumption.Method == CashFlowPrepaymentMethod.Psa)
            {
                var origin = assumption.SeasoningOrigin ?? throw new ArgumentException("PSA requires a seasoning origin.");
                var month = (cursor.Year - origin.Year) * 12 + cursor.Month - origin.Month + 1;
                value = decimal.Min(1m, 0.002m * Math.Clamp(month, 0, 30) * value);
            }
            var exponent = assumption.Method == CashFlowPrepaymentMethod.Smm
                ? (double)days / DateTime.DaysInMonth(cursor.Year, cursor.Month)
                : assumption.Method == CashFlowPrepaymentMethod.Cpr ? (double)days / 365d
                : (double)days / DateTime.DaysInMonth(cursor.Year, cursor.Month) / 12d;
            survival *= Math.Pow(1d - (double)value, exponent);
            cursor = stop;
        }
        return (decimal)(1d - survival);
    }
}
