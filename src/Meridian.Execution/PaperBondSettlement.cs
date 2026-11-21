namespace Meridian.Execution;

/// <summary>
/// Settlement-date convention for paper bond fills: regular-way T+1 on weekdays, the US
/// corporate and Treasury convention. Exchange holidays are not modelled; a fill before a
/// holiday settles on the next weekday. Accrued interest is measured to this date.
/// </summary>
public static class PaperBondSettlement
{
    public const int SettlementLagBusinessDays = 1;

    public static DateOnly SettlementDate(DateOnly tradeDate)
    {
        var date = tradeDate;
        var remaining = SettlementLagBusinessDays;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                remaining--;
        }

        return date;
    }
}
