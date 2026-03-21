using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

namespace Meridian.Tests.Monitoring;

public sealed class TickSizeValidatorTests : IDisposable
{
    private readonly TickSizeValidator _validator;

    public TickSizeValidatorTests()
    {
        _validator = new TickSizeValidator(new TickSizeValidatorConfig
        {
            DefaultTickSize = 0.01m,
            SubDollarTickSize = 0.0001m,
            UseSubDollarTicks = true,
            AlertCooldownMs = 0 // Disable cooldown for tests
        });
    }

    #region ValidateTrade Tests

    [Theory]
    [InlineData(100.00)]
    [InlineData(100.01)]
    [InlineData(100.99)]
    [InlineData(1.00)]
    [InlineData(50.50)]
    [InlineData(999.99)]
    public void ValidateTrade_WithValidPrice_ShouldReturnFalse(double price)
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateTrade("AAPL", (decimal)price);

        // Assert
        hasViolation.Should().BeFalse($"price {price} should be on valid $0.01 tick");
    }

    [Theory]
    [InlineData(100.001)]
    [InlineData(100.005)]
    [InlineData(100.015)]
    [InlineData(100.123)]
    [InlineData(50.555)]
    public void ValidateTrade_WithInvalidPrice_ShouldReturnTrue(double price)
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateTrade("AAPL", (decimal)price);

        // Assert
        hasViolation.Should().BeTrue($"price {price} should violate $0.01 tick size");
    }

    [Fact]
    public void ValidateTrade_WithZeroPrice_ShouldReturnFalse()
    {
        // Arrange & Act - zero prices are ignored (not validated)
        var hasViolation = _validator.ValidateTrade("AAPL", 0m);

        // Assert
        hasViolation.Should().BeFalse();
    }

    [Fact]
    public void ValidateTrade_WithNegativePrice_ShouldReturnFalse()
    {
        // Arrange & Act - negative prices are ignored (not validated)
        var hasViolation = _validator.ValidateTrade("AAPL", -10.00m);

        // Assert
        hasViolation.Should().BeFalse();
    }

    #endregion

    #region Sub-Dollar Tick Size Tests

    [Theory]
    [InlineData(0.5000)]
    [InlineData(0.5001)]
    [InlineData(0.9999)]
    [InlineData(0.0001)]
    [InlineData(0.1234)]
    public void ValidateTrade_WithValidSubDollarPrice_ShouldReturnFalse(double price)
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateTrade("PENNY", (decimal)price);

        // Assert
        hasViolation.Should().BeFalse($"sub-dollar price {price} should be on valid $0.0001 tick");
    }

    [Theory]
    [InlineData(0.50001)]
    [InlineData(0.12345)]
    [InlineData(0.00005)]
    public void ValidateTrade_WithInvalidSubDollarPrice_ShouldReturnTrue(double price)
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateTrade("PENNY", (decimal)price);

        // Assert
        hasViolation.Should().BeTrue($"sub-dollar price {price} should violate $0.0001 tick size");
    }

    [Fact]
    public void ValidateTrade_WithSubDollarTicksDisabled_ShouldUseDefaultTick()
    {
        // Arrange
        using var validator = new TickSizeValidator(new TickSizeValidatorConfig
        {
            DefaultTickSize = 0.01m,
            UseSubDollarTicks = false,
            AlertCooldownMs = 0
        });

        // Act - this would be valid with sub-dollar ticks but invalid without
        var hasViolation = validator.ValidateTrade("PENNY", 0.5001m);

        // Assert
        hasViolation.Should().BeTrue("sub-dollar ticks are disabled, so $0.01 tick applies");
    }

    #endregion

    #region ValidateQuote Tests

    [Fact]
    public void ValidateQuote_WithValidPrices_ShouldReturnFalse()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateQuote("AAPL", 149.99m, 150.01m);

        // Assert
        hasViolation.Should().BeFalse();
    }

    [Fact]
    public void ValidateQuote_WithInvalidBidPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateQuote("AAPL", 149.995m, 150.01m);

        // Assert
        hasViolation.Should().BeTrue("bid price 149.995 violates tick size");
    }

    [Fact]
    public void ValidateQuote_WithInvalidAskPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateQuote("AAPL", 149.99m, 150.015m);

        // Assert
        hasViolation.Should().BeTrue("ask price 150.015 violates tick size");
    }

    [Fact]
    public void ValidateQuote_WithBothInvalidPrices_ShouldReturnTrue()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateQuote("AAPL", 149.995m, 150.015m);

        // Assert
        hasViolation.Should().BeTrue("both bid and ask prices violate tick size");
    }

    #endregion

    #region ValidateBar Tests

    [Fact]
    public void ValidateBar_WithAllValidPrices_ShouldReturnFalse()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateBar("AAPL", 150.00m, 152.50m, 149.50m, 151.25m);

        // Assert
        hasViolation.Should().BeFalse();
    }

    [Fact]
    public void ValidateBar_WithInvalidOpenPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateBar("AAPL", 150.005m, 152.50m, 149.50m, 151.25m);

        // Assert
        hasViolation.Should().BeTrue("open price 150.005 violates tick size");
    }

    [Fact]
    public void ValidateBar_WithInvalidHighPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateBar("AAPL", 150.00m, 152.555m, 149.50m, 151.25m);

        // Assert
        hasViolation.Should().BeTrue("high price 152.555 violates tick size");
    }

    [Fact]
    public void ValidateBar_WithInvalidLowPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateBar("AAPL", 150.00m, 152.50m, 149.505m, 151.25m);

        // Assert
        hasViolation.Should().BeTrue("low price 149.505 violates tick size");
    }

    [Fact]
    public void ValidateBar_WithInvalidClosePrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var hasViolation = _validator.ValidateBar("AAPL", 150.00m, 152.50m, 149.50m, 151.255m);

        // Assert
        hasViolation.Should().BeTrue("close price 151.255 violates tick size");
    }

    #endregion

    #region Custom Tick Size Tests

    [Fact]
    public void SetTickSize_ShouldOverrideDefaultTickSize()
    {
        // Arrange - set custom tick size for futures
        _validator.SetTickSize("ES", 0.25m);

        // Act - this would be invalid with $0.01 tick but valid with $0.25
        var validPrice = _validator.ValidateTrade("ES", 4500.25m);
        var invalidPrice = _validator.ValidateTrade("ES", 4500.10m);

        // Assert
        validPrice.Should().BeFalse("4500.25 is on valid $0.25 tick");
        invalidPrice.Should().BeTrue("4500.10 violates $0.25 tick size");
    }

    [Fact]
    public void SetTickSize_WithZeroTickSize_ShouldThrowArgumentOutOfRange()
    {
        // Arrange & Act
        var action = () => _validator.SetTickSize("AAPL", 0m);

        // Assert
        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Tick size must be positive*");
    }

    [Fact]
    public void SetTickSize_WithNegativeTickSize_ShouldThrowArgumentOutOfRange()
    {
        // Arrange & Act
        var action = () => _validator.SetTickSize("AAPL", -0.01m);

        // Assert
        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Tick size must be positive*");
    }

    [Fact]
    public void SetTickSize_ShouldBeCaseInsensitive()
    {
        // Arrange
        _validator.SetTickSize("aapl", 0.05m);

        // Act - test with different case
        var violation = _validator.ValidateTrade("AAPL", 100.03m);

        // Assert
        violation.Should().BeTrue("custom tick size should apply regardless of case");
    }

    #endregion

    #region OnViolation Event Tests

    [Fact]
    public void OnViolation_EventShouldBeFired_WhenViolationDetected()
    {
        // Arrange
        TickSizeViolationAlert? capturedAlert = null;
        _validator.OnViolation += alert => capturedAlert = alert;

        // Act
        _validator.ValidateTrade("AAPL", 100.005m);

        // Assert
        capturedAlert.Should().NotBeNull();
        capturedAlert!.Value.Symbol.Should().Be("AAPL");
        capturedAlert.Value.Price.Should().Be(100.005m);
        capturedAlert.Value.ExpectedTickSize.Should().Be(0.01m);
        capturedAlert.Value.PriceType.Should().Be(TickSizePriceType.Trade);
    }

    [Fact]
    public void OnViolation_ShouldIncludeNearestValidPrices()
    {
        // Arrange
        TickSizeViolationAlert? capturedAlert = null;
        _validator.OnViolation += alert => capturedAlert = alert;

        // Act
        _validator.ValidateTrade("AAPL", 100.005m);

        // Assert
        capturedAlert.Should().NotBeNull();
        capturedAlert!.Value.NearestValidPriceLow.Should().Be(100.00m);
        capturedAlert.Value.NearestValidPriceHigh.Should().Be(100.01m);
    }

    [Fact]
    public void OnViolation_ShouldIncludeRemainder()
    {
        // Arrange
        TickSizeViolationAlert? capturedAlert = null;
        _validator.OnViolation += alert => capturedAlert = alert;

        // Act
        _validator.ValidateTrade("AAPL", 100.007m);

        // Assert
        capturedAlert.Should().NotBeNull();
        capturedAlert!.Value.Remainder.Should().Be(0.007m);
    }

    [Fact]
    public void OnViolation_ShouldIncludeProvider()
    {
        // Arrange
        TickSizeViolationAlert? capturedAlert = null;
        _validator.OnViolation += alert => capturedAlert = alert;

        // Act
        _validator.ValidateTrade("AAPL", 100.005m, "Alpaca");

        // Assert
        capturedAlert.Should().NotBeNull();
        capturedAlert!.Value.Provider.Should().Be("Alpaca");
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public void GetStats_ShouldTrackViolationsCorrectly()
    {
        // Arrange & Act
        _validator.ValidateTrade("AAPL", 100.00m);  // Valid
        _validator.ValidateTrade("AAPL", 100.005m); // Invalid
        _validator.ValidateTrade("MSFT", 200.003m); // Invalid
        _validator.ValidateTrade("MSFT", 200.00m);  // Valid

        var stats = _validator.GetStats();

        // Assert
        stats.TotalPricesProcessed.Should().Be(4);
        stats.TotalViolationsDetected.Should().Be(2);
        stats.SymbolStats.Should().HaveCount(2);
    }

    [Fact]
    public void GetStats_ShouldOrderByViolationCount()
    {
        // Arrange
        _validator.ValidateTrade("AAPL", 100.005m); // 1 violation
        _validator.ValidateTrade("MSFT", 200.003m); // 1 violation
        _validator.ValidateTrade("MSFT", 200.007m); // 2 violations

        // Act
        var stats = _validator.GetStats();

        // Assert
        stats.SymbolStats[0].Symbol.Should().Be("MSFT");
        stats.SymbolStats[0].TotalViolations.Should().Be(2);
        stats.SymbolStats[1].Symbol.Should().Be("AAPL");
        stats.SymbolStats[1].TotalViolations.Should().Be(1);
    }

    [Fact]
    public void TotalViolationsDetected_ShouldMatchStatsCount()
    {
        // Arrange
        _validator.ValidateTrade("AAPL", 100.005m);
        _validator.ValidateTrade("AAPL", 100.007m);

        // Act & Assert
        _validator.TotalViolationsDetected.Should().Be(2);
        _validator.GetStats().TotalViolationsDetected.Should().Be(2);
    }

    #endregion

    #region Configuration Preset Tests

    [Fact]
    public void DefaultConfig_ShouldUseStandardUSEquityTickSizes()
    {
        // Arrange
        using var validator = new TickSizeValidator(TickSizeValidatorConfig.Default);

        // Act - standard $0.01 tick for prices >= $1
        var violation = validator.ValidateTrade("AAPL", 100.005m);

        // Assert
        violation.Should().BeTrue("default config uses $0.01 tick size");
    }

    [Fact]
    public void FuturesForexConfig_ShouldUseFinerTickSize()
    {
        // Arrange
        using var validator = new TickSizeValidator(TickSizeValidatorConfig.FuturesForex);

        // Act - $0.0001 tick for all prices
        var validPrice = validator.ValidateTrade("EURUSD", 1.2345m);
        var invalidPrice = validator.ValidateTrade("EURUSD", 1.23455m);

        // Assert
        validPrice.Should().BeFalse("1.2345 is on valid $0.0001 tick");
        invalidPrice.Should().BeTrue("1.23455 violates $0.0001 tick size");
    }

    [Fact]
    public void LenientConfig_ShouldAllowFinerPrecision()
    {
        // Arrange
        using var validator = new TickSizeValidator(TickSizeValidatorConfig.Lenient);

        // Act - $0.001 tick for prices >= $1
        var validPrice = validator.ValidateTrade("AAPL", 100.005m);
        var invalidPrice = validator.ValidateTrade("AAPL", 100.0005m);

        // Assert
        validPrice.Should().BeFalse("100.005 is on valid $0.001 tick");
        invalidPrice.Should().BeTrue("100.0005 violates $0.001 tick size");
    }

    #endregion

    #region Floating Point Tolerance Tests

    [Fact]
    public void ValidateTrade_ShouldAllowSmallFloatingPointTolerance()
    {
        // Arrange - use a price that might have floating point representation issues
        var price = 100.01m - 0.000001m; // Just slightly off due to FP

        // Act
        var hasViolation = _validator.ValidateTrade("AAPL", price);

        // Assert - should be within tolerance
        hasViolation.Should().BeFalse("small floating point differences should be tolerated");
    }

    #endregion

    #region Alert Cooldown Tests

    [Fact]
    public void ValidateTrade_ShouldRespectAlertCooldown()
    {
        // Arrange - create validator with long cooldown
        using var validator = new TickSizeValidator(new TickSizeValidatorConfig
        {
            AlertCooldownMs = 10000 // 10 seconds
        });

        var alertCount = 0;
        validator.OnViolation += _ => alertCount++;

        // Act - trigger multiple violations quickly
        validator.ValidateTrade("AAPL", 100.005m);
        validator.ValidateTrade("AAPL", 100.007m);
        validator.ValidateTrade("AAPL", 100.003m);

        // Assert - only first violation should trigger alert due to cooldown
        alertCount.Should().Be(1);
    }

    [Fact]
    public void ValidateTrade_DifferentSymbols_ShouldHaveIndependentCooldowns()
    {
        // Arrange
        using var validator = new TickSizeValidator(new TickSizeValidatorConfig
        {
            AlertCooldownMs = 10000
        });

        var alertCount = 0;
        validator.OnViolation += _ => alertCount++;

        // Act - trigger violations for different symbols
        validator.ValidateTrade("AAPL", 100.005m);
        validator.ValidateTrade("MSFT", 200.005m);
        validator.ValidateTrade("GOOG", 150.005m);

        // Assert - each symbol should get its own alert
        alertCount.Should().Be(3);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldStopProcessing()
    {
        // Arrange
        _validator.Dispose();

        // Act
        var hasViolation = _validator.ValidateTrade("AAPL", 100.005m);

        // Assert
        hasViolation.Should().BeFalse("disposed validator should not process");
    }

    [Fact]
    public void Dispose_ShouldNotFireEvents()
    {
        // Arrange
        var alertCount = 0;
        _validator.OnViolation += _ => alertCount++;
        _validator.Dispose();

        // Act
        _validator.ValidateTrade("AAPL", 100.005m);

        // Assert
        alertCount.Should().Be(0);
    }

    #endregion

    public void Dispose()
    {
        _validator.Dispose();
    }
}
