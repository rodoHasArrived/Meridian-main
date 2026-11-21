namespace Meridian.Contracts.Workstation;

/// <summary>A comparable monetary subtotal; native quantities and unknown currencies are never money.</summary>
public sealed record ReconciliationMonetaryTotal(decimal? Amount, string? Currency, string? UnavailableReason)
{
    public static ReconciliationMonetaryTotal Unavailable(string reason) => new(null, null, reason);

    public static ReconciliationMonetaryTotal FromBreaks(IEnumerable<ReconciliationBreakDto> breaks)
    {
        var totals = new List<ReconciliationMonetaryTotal>();
        foreach (var item in breaks)
        {
            var money = (item.Measures ?? []).Where(static measure =>
                measure.Kind is ReconciliationBreakMeasureKindDto.Value or ReconciliationBreakMeasureKindDto.CostBasis).ToArray();
            if (money.Length != 1 || money[0].UnavailableReason is not null
                || !money[0].Expected.HasValue || !money[0].Actual.HasValue || !money[0].Variance.HasValue
                || money[0].Variance != money[0].Actual - money[0].Expected || !IsCurrency(money[0].Unit))
                return Unavailable("A break has no single comparable monetary measure; quantities and unavailable evidence cannot be summed as money.");
            totals.Add(new(Math.Abs(money[0].Variance!.Value), money[0].Unit.ToUpperInvariant(), null));
        }
        return Combine(totals);
    }

    public static ReconciliationMonetaryTotal Combine(IEnumerable<ReconciliationMonetaryTotal> totals)
    {
        decimal amount = 0m;
        string? currency = null;
        foreach (var total in totals)
        {
            if (!total.Amount.HasValue || total.UnavailableReason is not null)
                return Unavailable(total.UnavailableReason ?? "Monetary comparison evidence is unavailable.");
            if (total.Currency is null && total.Amount == 0m)
                continue; // An empty population contributes no currency claim.
            if (!IsCurrency(total.Currency))
                return Unavailable("A monetary subtotal has no retained currency.");
            var next = total.Currency!.ToUpperInvariant();
            if (currency is not null && currency != next)
                return Unavailable("Breaks use different currencies; no FX translation was requested.");
            currency = next;
            amount += total.Amount.Value;
        }
        return new(amount, currency, null);
    }

    private static bool IsCurrency(string? value) => value is { Length: 3 }
        && value.All(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
}
