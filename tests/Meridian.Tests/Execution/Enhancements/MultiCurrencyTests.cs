using FluentAssertions;
using Meridian.Execution.MultiCurrency;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for Multi-Currency &amp; FX Handling (Phase 3).
/// Validates <see cref="FxRate"/> and <see cref="MultiCurrencyCashBalance"/>.
/// </summary>
public sealed class MultiCurrencyTests
{
    // -------------------------------------------------------------------------
    // FxRate
    // -------------------------------------------------------------------------

    [Fact]
    public void FxRate_Convert_MultipliesAmountByRate()
    {
        var rate = new FxRate("EUR", "USD", 1.085m, DateTimeOffset.UtcNow);
        rate.Convert(1_000m).Should().Be(1_085m);
    }

    [Fact]
    public void FxRate_ConvertInverse_DividesAmountByRate()
    {
        var rate = new FxRate("EUR", "USD", 1.085m, DateTimeOffset.UtcNow);
        rate.ConvertInverse(1_085m).Should().BeApproximately(1_000m, 0.0001m);
    }

    [Fact]
    public void FxRate_ConvertInverse_ThrowsOnZeroRate()
    {
        var rate = new FxRate("EUR", "USD", 0m, DateTimeOffset.UtcNow);
        var act = () => rate.ConvertInverse(100m);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FxRate_Inverse_SwapsBaseAndQuote()
    {
        var rate = new FxRate("EUR", "USD", 1.085m, DateTimeOffset.UtcNow);
        var inverse = rate.Inverse();

        inverse.BaseCurrency.Should().Be("USD");
        inverse.QuoteCurrency.Should().Be("EUR");
        inverse.Rate.Should().BeApproximately(1m / 1.085m, 0.000001m);
    }

    [Fact]
    public void FxRate_ToString_ContainsCurrencyPairAndRate()
    {
        var rate = new FxRate("EUR", "USD", 1.085m, new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero));
        var str = rate.ToString();

        str.Should().Contain("EUR/USD");
        str.Should().Contain("1.085");
    }

    // -------------------------------------------------------------------------
    // MultiCurrencyCashBalance
    // -------------------------------------------------------------------------

    [Fact]
    public void MultiCurrencyBalance_Get_ReturnsZeroForUnknownCurrency()
    {
        var balance = new MultiCurrencyCashBalance();
        balance.Get("EUR").Should().Be(0m);
    }

    [Fact]
    public void MultiCurrencyBalance_Add_AccumulatesBalance()
    {
        var balance = new MultiCurrencyCashBalance();
        balance.Add("USD", 10_000m);
        balance.Add("USD", 5_000m);

        balance.Get("USD").Should().Be(15_000m);
    }

    [Fact]
    public void MultiCurrencyBalance_Subtract_ReducesBalance()
    {
        var balance = new MultiCurrencyCashBalance();
        balance.Add("USD", 10_000m);
        balance.Subtract("USD", 3_000m);

        balance.Get("USD").Should().Be(7_000m);
    }

    [Fact]
    public void MultiCurrencyBalance_IsCaseInsensitive()
    {
        var balance = new MultiCurrencyCashBalance();
        balance.Add("usd", 5_000m);
        balance.Get("USD").Should().Be(5_000m);
    }

    [Fact]
    public void MultiCurrencyBalance_ToBaseCurrency_ConvertsByCurrencyRate()
    {
        var balance = new MultiCurrencyCashBalance();
        balance.Add("USD", 10_000m);
        balance.Add("EUR", 1_000m);

        var rates = new Dictionary<string, decimal>
        {
            ["USD"] = 1.0m,
            ["EUR"] = 1.085m,
        };

        var total = balance.ToBaseCurrency(rates);
        total.Should().Be(10_000m + 1_000m * 1.085m);
    }

    [Fact]
    public void MultiCurrencyBalance_ToBaseCurrency_UsesFallbackRateOfOneForBaseCurrency()
    {
        var balance = new MultiCurrencyCashBalance();
        balance.Add("USD", 10_000m);

        // No rate provided for USD — should assume 1:1
        var total = balance.ToBaseCurrency(new Dictionary<string, decimal>());
        total.Should().Be(10_000m);
    }

    [Fact]
    public void MultiCurrencyBalance_Snapshot_ReturnsCopy()
    {
        var balance = new MultiCurrencyCashBalance();
        balance.Add("USD", 10_000m);

        var snapshot = balance.Snapshot();
        balance.Add("USD", 5_000m);

        // Snapshot should be immutable
        snapshot["USD"].Should().Be(10_000m);
        balance.Get("USD").Should().Be(15_000m);
    }

    [Fact]
    public void MultiCurrencyBalance_InitialiseFromDictionary_ReflectsValues()
    {
        var initial = new Dictionary<string, decimal>
        {
            ["USD"] = 50_000m,
            ["GBP"] = 20_000m,
        };

        var balance = new MultiCurrencyCashBalance(initial);

        balance.Get("USD").Should().Be(50_000m);
        balance.Get("GBP").Should().Be(20_000m);
    }
}
