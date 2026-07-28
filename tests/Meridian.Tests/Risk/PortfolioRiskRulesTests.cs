using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Covers the portfolio-aware pre-trade rules: gross exposure, symbol concentration, and
/// per-order notional, all fed from an <see cref="IPortfolioExposureProvider"/> snapshot.
/// </summary>
public sealed class PortfolioRiskRulesTests
{
    private static OrderRequest CreateOrder(
        string symbol = "AAPL",
        decimal quantity = 10m,
        decimal? limitPrice = null,
        OrderSide side = OrderSide.Buy,
        OrderType? type = null) => new()
        {
            Symbol = symbol,
            Side = side,
            Type = type ?? (limitPrice.HasValue ? OrderType.Limit : OrderType.Market),
            Quantity = quantity,
            LimitPrice = limitPrice,
        };

    private static StubExposureProvider Provider(
        decimal grossExposure = 0m,
        decimal portfolioValue = 100_000m,
        params SymbolExposure[] symbols) => new(new PortfolioExposureSnapshot(
            GrossExposure: grossExposure,
            NetExposure: grossExposure,
            PortfolioValue: portfolioValue,
            SymbolExposures: symbols.ToDictionary(static s => s.Symbol, StringComparer.OrdinalIgnoreCase),
            AsOf: DateTimeOffset.UtcNow));

    // --- GrossExposureRule ---

    [Fact]
    public async Task GrossExposure_WithoutConfiguredCeiling_Approves()
    {
        var rule = new GrossExposureRule(
            Provider(grossExposure: 1_000_000m),
            () => null,
            NullLogger<GrossExposureRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(limitPrice: 100m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task GrossExposure_ProjectedBreach_Rejects()
    {
        var rule = new GrossExposureRule(
            Provider(grossExposure: 95_000m),
            () => 100_000m,
            NullLogger<GrossExposureRule>.Instance);

        // 95k existing + 10 × 1000 = 105k projected > 100k ceiling.
        var result = await rule.EvaluateAsync(CreateOrder(quantity: 10m, limitPrice: 1_000m));

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("Gross exposure limit");
    }

    [Fact]
    public async Task GrossExposure_WithinCeiling_Approves()
    {
        var rule = new GrossExposureRule(
            Provider(grossExposure: 50_000m),
            () => 100_000m,
            NullLogger<GrossExposureRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 10m, limitPrice: 100m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task GrossExposure_DeRiskingSellNearCeiling_Approves()
    {
        // Entire 95k book is this symbol long; selling 500 x 100 = 50k reduces the
        // projection to 45k, well under the 100k ceiling.
        var rule = new GrossExposureRule(
            Provider(grossExposure: 95_000m, symbols: new SymbolExposure("AAPL", 95_000m, 950m, 100m, 95_000m)),
            () => 100_000m,
            NullLogger<GrossExposureRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 500m, limitPrice: 100m, side: OrderSide.Sell));

        result.IsApproved.Should().BeTrue("a de-risking order reduces projected gross exposure");
    }

    [Fact]
    public async Task Concentration_ReducingSellAboveCap_Approves()
    {
        // 30% concentration already; selling 5% projects 25%, inside the 28% cap.
        var rule = new SymbolConcentrationRule(
            Provider(grossExposure: 30_000m, portfolioValue: 100_000m, symbols: new SymbolExposure("AAPL", 30_000m, 300m, 100m, 30_000m)),
            () => 28m,
            NullLogger<SymbolConcentrationRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 50m, limitPrice: 100m, side: OrderSide.Sell));

        result.IsApproved.Should().BeTrue("an order that reduces concentration must not be rejected by the cap");
    }

    [Fact]
    public void GrossExposure_DeclaresCriticalSeverity()
    {
        var rule = new GrossExposureRule(
            Provider(),
            () => 100_000m,
            NullLogger<GrossExposureRule>.Instance);

        rule.Severity.Should().Be(RiskRuleSeverity.Critical, "a book-level breach must trip the circuit breaker");
    }

    // --- SymbolConcentrationRule ---

    [Fact]
    public async Task Concentration_WithoutConfiguredCap_Approves()
    {
        var rule = new SymbolConcentrationRule(
            Provider(grossExposure: 90_000m, symbols: new SymbolExposure("AAPL", 90_000m, 900m, 100m, 90_000m)),
            () => null,
            NullLogger<SymbolConcentrationRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(limitPrice: 100m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Concentration_ProjectedBreach_Rejects()
    {
        // AAPL already 20% of a 100k portfolio; order adds 10k more → 30% > 25% cap.
        var rule = new SymbolConcentrationRule(
            Provider(grossExposure: 20_000m, portfolioValue: 100_000m, symbols: new SymbolExposure("AAPL", 20_000m, 200m, 100m, 20_000m)),
            () => 25m,
            NullLogger<SymbolConcentrationRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 100m, limitPrice: 100m));

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("Concentration limit");
    }

    [Fact]
    public async Task Concentration_InsideObserveBand_ApprovesWithWarning()
    {
        // Projected 21% of portfolio value ≥ 80% of the 25% cap → approved + warning flag.
        var rule = new SymbolConcentrationRule(
            Provider(grossExposure: 20_000m, portfolioValue: 100_000m, symbols: new SymbolExposure("AAPL", 20_000m, 200m, 100m, 20_000m)),
            () => 25m,
            NullLogger<SymbolConcentrationRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 10m, limitPrice: 100m));

        result.IsApproved.Should().BeTrue();
        result.Warnings.Should().ContainSingle(warning => warning.Contains("approaching"));
    }

    [Fact]
    public async Task Concentration_WellUnderCap_ApprovesCleanly()
    {
        var rule = new SymbolConcentrationRule(
            Provider(grossExposure: 5_000m, portfolioValue: 100_000m, symbols: new SymbolExposure("AAPL", 5_000m, 50m, 100m, 5_000m)),
            () => 25m,
            NullLogger<SymbolConcentrationRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 10m, limitPrice: 100m));

        result.IsApproved.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Concentration_WithZeroPortfolioValue_Approves()
    {
        var rule = new SymbolConcentrationRule(
            Provider(grossExposure: 0m, portfolioValue: 0m),
            () => 25m,
            NullLogger<SymbolConcentrationRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(limitPrice: 100m));

        result.IsApproved.Should().BeTrue();
    }

    // --- OrderNotionalRule ---

    [Fact]
    public async Task OrderNotional_WithoutConfiguredLimits_Approves()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => null,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 1_000_000m, limitPrice: 500m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task OrderNotional_OverHardCeiling_Rejects()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 50_000m,
            () => 10_000m,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 600m, limitPrice: 100m));

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeFalse();
        result.RejectReason.Should().Contain("Order notional limit");
    }

    [Fact]
    public async Task OrderNotional_InsideEscalationBand_EscalatesForApproval()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 50_000m,
            () => 10_000m,
            NullLogger<OrderNotionalRule>.Instance);

        // 200 × 100 = 20k: at/above the 10k band, under the 50k ceiling.
        var result = await rule.EvaluateAsync(CreateOrder(quantity: 200m, limitPrice: 100m));

        result.IsApproved.Should().BeFalse();
        result.RequiresApproval.Should().BeTrue();
        result.RejectReason.Should().Contain("governed-approval band");
    }

    [Fact]
    public async Task OrderNotional_BelowEscalationBand_Approves()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 50_000m,
            () => 10_000m,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 50m, limitPrice: 100m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task OrderNotional_MarketOrderWithKnownSymbol_UsesReferencePrice()
    {
        var rule = new OrderNotionalRule(
            Provider(symbols: new SymbolExposure("AAPL", 10_000m, 100m, 200m, 10_000m)),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // Market order: 100 × reference 200 = 20k > 10k ceiling.
        var result = await rule.EvaluateAsync(CreateOrder(quantity: 100m));

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task OrderNotional_MarketOrderWithNoPriceReference_Approves()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 10_000m,
            () => 1_000m,
            NullLogger<OrderNotionalRule>.Instance);

        // Never-held symbol, no limit price: the rule never guesses a price.
        var result = await rule.EvaluateAsync(CreateOrder(symbol: "ZZZZ", quantity: 1_000m));

        result.IsApproved.Should().BeTrue();
    }

    [Theory]
    [InlineData("notional")]
    [InlineData("alpaca:notional")]
    public async Task OrderNotional_BrokerNotionalMetadata_MeasuresTheRoutedDollars(string metadataKey)
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // Broker-native notional sizing: the gateway routes the metadata dollars, so the
        // rule must measure those dollars — a never-held symbol with no limit price would
        // otherwise resolve to no notional at all and bypass the ceiling.
        var order = CreateOrder(symbol: "ZZZZ", quantity: 1m) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [metadataKey] = "20000"
            }
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse("the routed notional is the metadata dollars, not quantity × price");
    }

    [Fact]
    public async Task OrderNotional_QuantityIsDollarsFlag_MeasuresQuantityAsDollars()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // The boolean flag form means "quantity is dollars": 20 000 dollars > 10k ceiling.
        var order = CreateOrder(symbol: "ZZZZ", quantity: 20_000m) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "true"
            }
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task OrderNotional_MarketOrderInNeverHeldSymbol_MeasuresAtTheLiveMark()
    {
        // The book is flat in ZZZZ, but the feed prices it and the gateway will execute
        // it — measuring at the live mark beats approving it unmeasured.
        var rule = new OrderNotionalRule(
            new StubExposureProvider(
                Provider().GetSnapshot(),
                referencePrices: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["ZZZZ"] = 250m }),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(symbol: "ZZZZ", quantity: 100m));

        result.IsApproved.Should().BeFalse("100 x the 250 live mark is 25k, over the 10k ceiling");
    }

    [Fact]
    public async Task OrderNotional_MarketableSellLimit_ValuesAtTheLiveMark()
    {
        // A sell limit far below the market is immediately executable AT the market: a
        // 10,000-share sale limited at $1 while the symbol trades at $100 routes ~$1m,
        // not $10k, so the limit price is not a bound on execution value.
        var rule = new OrderNotionalRule(
            new StubExposureProvider(
                Provider().GetSnapshot(),
                referencePrices: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["AAPL"] = 100m }),
            () => 500_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(
            CreateOrder(quantity: 10_000m, limitPrice: 1m, side: OrderSide.Sell));

        result.IsApproved.Should().BeFalse("the executable value is 10,000 x the 100 market, not the 1 limit");
    }

    [Fact]
    public async Task OrderNotional_NonMarketableBuyLimit_ValuesAtTheHigherLimit()
    {
        // The conservative side works both ways: a buy limit above the market can fill up
        // to the limit, so the larger of the two prices is the honest measurement.
        var rule = new OrderNotionalRule(
            new StubExposureProvider(
                Provider().GetSnapshot(),
                referencePrices: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["AAPL"] = 100m }),
            () => 50_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 400m, limitPrice: 150m));

        result.IsApproved.Should().BeFalse("400 x the 150 limit is 60k, over the 50k ceiling");
    }

    [Fact]
    public async Task OrderNotional_RestingBuyLimitBelowTheMark_ValuesAtTheLimit()
    {
        // A buy limit caps the price paid, so a 1,000-share buy limited at $1 can never
        // cost more than $1k even with the symbol marked at $100. Valuing it at the mark
        // would reject a harmless resting order — and at Critical severity, halt on it.
        var rule = new OrderNotionalRule(
            new StubExposureProvider(
                Provider().GetSnapshot(),
                referencePrices: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["AAPL"] = 100m }),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(quantity: 1_000m, limitPrice: 1m));

        result.IsApproved.Should().BeTrue("a buy limit is an upper bound on the execution price");
    }

    [Fact]
    public async Task GrossExposure_OrderFromAnEmptyAccount_UsesTheAdditiveWorstCase()
    {
        // One account is long $100k; the order sells $20k from a different, flat account.
        // Treating the other account's long as the order's own position would project
        // $80k gross instead of the real $120k across both accounts.
        var exposure = new SymbolExposure(
            "AAPL",
            GrossExposure: 100_000m,
            NetQuantity: 1_000m,
            ReferencePrice: 100m,
            NetNotional: 100_000m,
            AccountNetNotional: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["11111111-1111-1111-1111-111111111111"] = 100_000m
            });
        var rule = new GrossExposureRule(
            Provider(grossExposure: 100_000m, portfolioValue: 500_000m, symbols: exposure),
            () => 110_000m,
            NullLogger<GrossExposureRule>.Instance);

        var order = CreateOrder(quantity: 200m, limitPrice: 100m, side: OrderSide.Sell) with
        {
            FundAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse("the order adds exposure in its own account rather than reducing another's");
    }

    [Fact]
    public async Task GrossExposure_FundScopedCloseAgainstTheDefaultAccount_ReducesExposure()
    {
        // FundAccountId is an accounting scope; the paper portfolio records positions under
        // the "default" execution account. With a single, non-fund-keyed contributing
        // account the mapping is unambiguous, so a fund-scoped sell must be seen as closing
        // that position rather than adding on top of it.
        var exposure = new SymbolExposure(
            "AAPL",
            GrossExposure: 100_000m,
            NetQuantity: 1_000m,
            ReferencePrice: 100m,
            NetNotional: 100_000m,
            AccountNetNotional: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = 100_000m
            });
        var rule = new GrossExposureRule(
            Provider(grossExposure: 100_000m, portfolioValue: 500_000m, symbols: exposure),
            () => 110_000m,
            NullLogger<GrossExposureRule>.Instance);

        var order = CreateOrder(quantity: 500m, limitPrice: 100m, side: OrderSide.Sell) with
        {
            FundAccountId = Guid.Parse("99999999-9999-9999-9999-999999999999")
        };
        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeTrue("closing a position must not be measured as adding to it");
    }

    private sealed class StubExposureProvider(
        PortfolioExposureSnapshot snapshot,
        IReadOnlyDictionary<string, decimal>? referencePrices = null) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => snapshot;

        public decimal? TryGetReferencePrice(string symbol) =>
            referencePrices is not null && referencePrices.TryGetValue(symbol, out var price) ? price : null;
    }
}
