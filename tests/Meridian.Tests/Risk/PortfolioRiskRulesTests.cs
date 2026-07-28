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
            Provider(grossExposure: 90_000m, symbols: new SymbolExposure("AAPL", 90_000m, 900m, 100m)),
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
            Provider(grossExposure: 20_000m, portfolioValue: 100_000m, symbols: new SymbolExposure("AAPL", 20_000m, 200m, 100m)),
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
            Provider(grossExposure: 20_000m, portfolioValue: 100_000m, symbols: new SymbolExposure("AAPL", 20_000m, 200m, 100m)),
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
            Provider(grossExposure: 5_000m, portfolioValue: 100_000m, symbols: new SymbolExposure("AAPL", 5_000m, 50m, 100m)),
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
            Provider(symbols: new SymbolExposure("AAPL", 10_000m, 100m, 200m)),
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

    private sealed class StubExposureProvider(PortfolioExposureSnapshot snapshot) : IPortfolioExposureProvider
    {
        public PortfolioExposureSnapshot GetSnapshot() => snapshot;
    }
}
