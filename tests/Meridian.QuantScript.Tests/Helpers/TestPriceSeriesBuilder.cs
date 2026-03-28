namespace Meridian.QuantScript.Tests.Helpers;

/// <summary>Builds synthetic <see cref="PriceSeries"/> for tests.</summary>
public static class TestPriceSeriesBuilder
{
    /// <summary>Creates a deterministic ascending price series.</summary>
    public static PriceSeries Build(
        string symbol = "TEST",
        int barCount = 30,
        decimal startPrice = 100.0m,
        DateOnly? startDate = null)
    {
        var date = startDate ?? new DateOnly(2024, 1, 2);
        var bars = new List<PriceBar>(barCount);
        var price = startPrice;
        for (var i = 0; i < barCount; i++)
        {
            price *= 1.001m; // 0.1% up each day
            bars.Add(new PriceBar(date, price * 0.99m, price * 1.01m, price * 0.98m, price, 1_000_000L));
            date = date.AddDays(1);
        }
        return new PriceSeries(symbol, bars);
    }

    /// <summary>Creates a flat price series (all closes == startPrice).</summary>
    public static PriceSeries BuildFlat(
        string symbol = "FLAT",
        int barCount = 10,
        decimal price = 100.0m,
        DateOnly? startDate = null)
    {
        var date = startDate ?? new DateOnly(2024, 1, 2);
        var bars = Enumerable.Range(0, barCount)
            .Select(_ =>
            {
                var bar = new PriceBar(date, price, price, price, price, 100_000L);
                date = date.AddDays(1);
                return bar;
            })
            .ToList();
        return new PriceSeries(symbol, bars);
    }
}
