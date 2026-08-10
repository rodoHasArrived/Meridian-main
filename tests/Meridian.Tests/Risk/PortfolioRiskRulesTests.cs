using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
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

    private static OptionContractIdentity OptionIdentity(string? multiplier) => new()
    {
        UnderlyingSymbol = "AAPL",
        ExpirationDate = new DateOnly(2026, 12, 18),
        StrikePrice = 250m,
        Right = "C",
        Multiplier = multiplier
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
    public async Task OrderNotional_AmendmentProbe_MeasuresTheWholeAmendedOrder()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 50_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // Amending 100 → 600 shares at $100 carries a $50k increment for the portfolio
        // rules, but the per-order ceiling governs the order that ends up working at the
        // broker: $60k, over the ceiling. Measuring the increment would let a capped order
        // be walked past the limit one amendment at a time.
        var probe = CreateOrder(quantity: 600m, limitPrice: 100m) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.IncrementalNotionalMetadataKey] = "50000"
            }
        };

        var result = await rule.EvaluateAsync(probe);

        result.IsApproved.Should().BeFalse("the per-order limit governs the full amended order");
        result.RejectReason.Should().Contain("Order notional limit");
    }

    [Fact]
    public async Task GrossExposure_AmendmentProbe_MeasuresOnlyTheIncrement()
    {
        var rule = new GrossExposureRule(
            Provider(grossExposure: 95_000m),
            () => 100_000m,
            NullLogger<GrossExposureRule>.Instance);

        // The original $50k is already inside the reported gross, so only the $4k increase
        // is new exposure: 95k + 4k = 99k, inside the ceiling. Charging the full $60k order
        // would double-count the working order the snapshot already reserves.
        var probe = CreateOrder(quantity: 600m, limitPrice: 100m) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.IncrementalNotionalMetadataKey] = "4000"
            }
        };

        var result = await rule.EvaluateAsync(probe);

        result.IsApproved.Should().BeTrue("a portfolio rule charges the amendment's increment, not the whole order");
    }

    [Fact]
    public async Task OrderNotional_TriggeredBuyStop_IsValuedAtTheMarket()
    {
        var rule = new OrderNotionalRule(
            Provider(symbols: new SymbolExposure("AAPL", 10_000m, 100m, 100m, 10_000m)),
            () => 50_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // A buy stop at $1 with the symbol at $100 has already triggered: it executes at
        // the market, so 1,000 shares route ~$100k, not $1k. A stop price is a trigger,
        // never a cap on what is paid.
        var order = CreateOrder(quantity: 1_000m, type: OrderType.StopMarket) with { StopPrice = 1m };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse("a triggered buy stop routes at the market, not at its stop price");
    }

    [Fact]
    public async Task OrderNotional_BuyStopWithNoCurrentMarket_RejectsAsUnmeasurable()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 50_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var order = CreateOrder(quantity: 1_000m, type: OrderType.StopMarket) with { StopPrice = 1m };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse("a stop trigger does not cap the execution price");
        result.RejectReason.Should().Contain("No current price");
    }

    [Fact]
    public async Task OrderNotional_RestingBuyLimit_KeepsItsOwnCap()
    {
        var rule = new OrderNotionalRule(
            Provider(symbols: new SymbolExposure("AAPL", 10_000m, 100m, 100m, 10_000m)),
            () => 50_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // The same numbers as a buy limit: the limit does cap what is paid, so 1,000
        // shares at $1 is a $1k order however far the market has run above it.
        var result = await rule.EvaluateAsync(CreateOrder(quantity: 1_000m, limitPrice: 1m));

        result.IsApproved.Should().BeTrue("valuing a resting buy limit at the mark would reject a harmless order");
    }

    [Fact]
    public async Task OrderNotional_MarketOrderWithNoPriceReference_Rejects()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 10_000m,
            () => 1_000m,
            NullLogger<OrderNotionalRule>.Instance);

        // Never-held symbol, no limit price, no feed: the rule cannot value the order. It
        // still routes at whatever the market gives it, so approving it unmeasured would
        // hand a configured ceiling an order that consumes none of it.
        var result = await rule.EvaluateAsync(CreateOrder(symbol: "ZZZZ", quantity: 1_000m));

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Contain("No current price");
    }

    [Fact]
    public async Task OrderNotional_MarketOrderWithNoPriceReference_ApprovesWhenNoLimitIsConfigured()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => null,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // Fail-closed applies to a ceiling that exists. With none configured the rule has
        // nothing to enforce and must not start refusing orders it never gated before.
        var result = await rule.EvaluateAsync(CreateOrder(symbol: "ZZZZ", quantity: 1_000m));

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task GrossExposure_OptionOrder_IsMeasuredAtContractNotional()
    {
        var rule = new GrossExposureRule(
            Provider(grossExposure: 1_000m),
            () => 100_000m,
            NullLogger<GrossExposureRule>.Instance);

        // 100 contracts at a $5 premium with the standard 100x multiplier is $50k of
        // notional, not $500. Measuring it as shares would let a contract order consume a
        // hundredth of the ceiling it actually fills.
        var order = CreateOrder(quantity: 100m, limitPrice: 5m) with
        {
            OptionContract = OptionIdentity(multiplier: "100")
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeTrue("$1k book + $50k order is inside the $100k ceiling");

        // The same order against a ceiling the contract notional breaches but the raw
        // share arithmetic would not.
        var tighter = new GrossExposureRule(
            Provider(grossExposure: 1_000m),
            () => 20_000m,
            NullLogger<GrossExposureRule>.Instance);

        (await tighter.EvaluateAsync(order)).IsApproved.Should().BeFalse(
            "the option's $50k contract notional breaches a $20k ceiling that $500 of shares would not");
    }

    [Fact]
    public async Task OrderNotional_OptionOrderWithoutAMultiplier_AssumesTheStandardContractSize()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 20_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // A broker adapter that did not stamp a multiplier must not collapse the contract
        // to 1x; equity options are 100x everywhere Meridian routes.
        var order = CreateOrder(quantity: 100m, limitPrice: 5m) with
        {
            OptionContract = OptionIdentity(multiplier: null)
        };

        (await rule.EvaluateAsync(order)).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task OrderNotional_MultiLegOrder_SumsEveryLegRatherThanNetting()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 90_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // A vertical spread: the net debit is small, but both legs carry real exposure and
        // a gross ceiling does not net one against the other. 10 x (1 + 1) x $5 x 100 = $10k
        // per leg pair... sized here to exceed the ceiling only when both legs count.
        var order = CreateOrder(quantity: 100m, limitPrice: 5m) with
        {
            Legs =
            [
                new OrderLeg { Symbol = "AAPL_C250", Side = OrderSide.Buy, RatioQuantity = 1m, OptionContract = OptionIdentity("100") },
                new OrderLeg { Symbol = "AAPL_C260", Side = OrderSide.Sell, RatioQuantity = 1m, OptionContract = OptionIdentity("100") }
            ]
        };

        (await rule.EvaluateAsync(order)).IsApproved.Should().BeFalse(
            "both legs of a spread carry exposure; a gross ceiling does not net them");
    }

    [Fact]
    public async Task OrderNotional_MarketSell_IsNotValuedBelowTheMark()
    {
        // The short a sell creates is marked at the mid and covered at the ask, so valuing
        // it at the $1 bid would book ~$50.5k of short exposure as a $1k increment.
        var provider = new TouchQuotingProvider(bid: 1m, ask: 100m);
        var rule = new OrderNotionalRule(
            provider,
            () => 40_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(
            CreateOrder(symbol: "WIDE", quantity: 1_000m, side: OrderSide.Sell));

        result.IsApproved.Should().BeFalse("a sell is never valued below the mark");
    }

    [Fact]
    public async Task OrderNotional_MarketBuy_IsValuedAtTheAskNotTheMidpoint()
    {
        // A wide book: bid $1, ask $100. The midpoint would measure a 1,000-share buy at
        // $50,500 — under the ceiling — while it routes near $100,000.
        var provider = new TouchQuotingProvider(bid: 1m, ask: 100m);
        var rule = new OrderNotionalRule(
            provider,
            () => 60_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(CreateOrder(symbol: "WIDE", quantity: 1_000m));

        result.IsApproved.Should().BeFalse("a market buy pays the ask, not the midpoint");
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

    [Theory]
    [InlineData("false")]
    [InlineData("not-a-number")]
    public async Task OrderNotional_UnusableFirstAlias_DoesNotFallThroughToTheSecond(string firstAliasValue)
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // The gateway's ReadMetadataString returns on the FIRST non-blank alias whether or
        // not that value parses, so a "notional" it cannot use means the order routes by
        // quantity. Reading past it to "alpaca:notional" would measure $1 against the
        // ceiling while the gateway sends 100,000 shares.
        var order = CreateOrder(symbol: "AAPL", quantity: 100_000m) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = firstAliasValue,
                ["alpaca:notional"] = "1"
            }
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse(
            "the order is quantity-sized, so it must be measured at quantity x price, not the ignored second alias");
    }

    [Fact]
    public async Task OrderNotional_NumericNotionalMetadata_IsTheDollarAmountNotAFlag()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // Both this parser and the gateway try the decimal form first, so "1" routes one
        // dollar of notional rather than flagging the 20,000 quantity as dollars. The two
        // paths must agree on that, or the rails measure an order the gateway never sends.
        var order = CreateOrder(symbol: "ZZZZ", quantity: 20_000m) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "1"
            }
        };

        (await rule.EvaluateAsync(order)).IsApproved.Should().BeTrue("a $1 notional order is well inside the ceiling");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("y")]
    [InlineData("YES")]
    public async Task OrderNotional_QuantityIsDollarsFlag_MeasuresQuantityAsDollars(string flag)
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 10_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        // The boolean flag form means "quantity is dollars": 20 000 dollars > 10k ceiling.
        // The gateway accepts more spellings than bool.TryParse does; recognizing fewer of
        // them here would measure shares while the gateway routes dollars.
        var order = CreateOrder(symbol: "ZZZZ", quantity: 20_000m) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = flag
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
    public async Task OrderNotional_SellLimitWithNoCurrentMarket_RejectsAsUnmeasurable()
    {
        var rule = new OrderNotionalRule(
            Provider(),
            () => 500_000m,
            () => null,
            NullLogger<OrderNotionalRule>.Instance);

        var result = await rule.EvaluateAsync(
            CreateOrder(quantity: 10_000m, limitPrice: 1m, side: OrderSide.Sell));

        result.IsApproved.Should().BeFalse("a sell limit is a floor, not a cap on execution value");
        result.RejectReason.Should().Contain("No current price");
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
        // Fills carry their owning fund through to the contribution, so the fund's own
        // position is identifiable and a fund-scoped sell closes it rather than adding on
        // top. A shared "default" book is NOT a stand-in for the fund — see the companion
        // test below, where a flat fund's sell must not net against another fund's long.
        var fundAccountId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var exposure = new SymbolExposure(
            "AAPL",
            GrossExposure: 100_000m,
            NetQuantity: 1_000m,
            ReferencePrice: 100m,
            NetNotional: 100_000m,
            AccountNetNotional: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [fundAccountId.ToString("D")] = 100_000m
            });
        var rule = new GrossExposureRule(
            Provider(grossExposure: 100_000m, portfolioValue: 500_000m, symbols: exposure),
            () => 110_000m,
            NullLogger<GrossExposureRule>.Instance);

        var order = CreateOrder(quantity: 500m, limitPrice: 100m, side: OrderSide.Sell) with
        {
            FundAccountId = fundAccountId
        };
        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeTrue("closing a position must not be measured as adding to it");
    }

    [Fact]
    public async Task GrossExposure_FlatFundSellingAgainstAnotherFundsLong_UsesTheAdditiveWorstCase()
    {
        // Fund A is long $100k. Flat Fund B sells $100k. These are two books, not one
        // position being closed: the projection must be the additive $200k, not near zero.
        var fundA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fundB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var exposure = new SymbolExposure(
            "AAPL",
            GrossExposure: 100_000m,
            NetQuantity: 1_000m,
            ReferencePrice: 100m,
            NetNotional: 100_000m,
            AccountNetNotional: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [fundA.ToString("D")] = 100_000m
            });
        var rule = new GrossExposureRule(
            Provider(grossExposure: 100_000m, portfolioValue: 500_000m, symbols: exposure),
            () => 150_000m,
            NullLogger<GrossExposureRule>.Instance);

        var order = CreateOrder(quantity: 1_000m, limitPrice: 100m, side: OrderSide.Sell) with
        {
            FundAccountId = fundB
        };

        var result = await rule.EvaluateAsync(order);

        result.IsApproved.Should().BeFalse(
            "a flat fund's sell does not close another fund's long, so the projection is additive");
    }

    [Fact]
    public async Task GrossExposure_FundOrderAgainstAnUnattributedBook_UsesTheAdditiveWorstCase()
    {
        // A shared, unattributed execution book is not a stand-in for the ordering fund:
        // treating it as theirs is exactly how a flat fund's sell would net away another
        // fund's long.
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
            () => 150_000m,
            NullLogger<GrossExposureRule>.Instance);

        var order = CreateOrder(quantity: 1_000m, limitPrice: 100m, side: OrderSide.Sell) with
        {
            FundAccountId = Guid.Parse("99999999-9999-9999-9999-999999999999")
        };

        (await rule.EvaluateAsync(order)).IsApproved.Should().BeFalse();
    }

    /// <summary>Provider exposing a wide two-sided book, so touch vs midpoint is visible.</summary>
    private sealed class TouchQuotingProvider(decimal bid, decimal ask) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => PortfolioExposureSnapshot.Empty;

        public decimal? TryGetReferencePrice(string symbol) => (bid + ask) / 2m;

        public decimal? TryGetExecutablePrice(string symbol, OrderSide side)
        {
            var mid = (bid + ask) / 2m;
            return Math.Max(mid, side == OrderSide.Buy ? ask : bid);
        }
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
