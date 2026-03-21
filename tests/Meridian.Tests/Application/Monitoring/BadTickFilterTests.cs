using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

namespace Meridian.Tests.Monitoring;

public sealed class BadTickFilterTests : IDisposable
{
    private readonly BadTickFilter _filter;

    public BadTickFilterTests()
    {
        _filter = new BadTickFilter(new BadTickFilterConfig
        {
            MaxDeviationPercent = 50.0,
            MinValidPrice = 0.0001m,
            MaxValidPrice = 1000000m,
            AlertCooldownMs = 0 // Disable cooldown for tests
        });
    }

    [Fact]
    public void IsBadTrade_WithValidPrice_ShouldReturnFalse()
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", 150.25m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeFalse();
    }

    [Fact]
    public void IsBadTrade_WithNegativePrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", -10.00m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void IsBadTrade_WithZeroPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", 0m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.001)]
    [InlineData(0.0001)]
    [InlineData(9999)]
    [InlineData(9999.99)]
    [InlineData(99999.99)]
    [InlineData(999999.99)]
    [InlineData(1234.56)]
    [InlineData(12345.67)]
    public void IsBadTrade_WithPlaceholderPrice_ShouldReturnTrue(double price)
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", (decimal)price, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue($"price {price} should be detected as placeholder");
    }

    [Theory]
    [InlineData(11.11)]
    [InlineData(22.22)]
    [InlineData(33.33)]
    [InlineData(44.44)]
    [InlineData(55.55)]
    [InlineData(66.66)]
    [InlineData(77.77)]
    [InlineData(88.88)]
    [InlineData(99.99)]
    [InlineData(111.11)]
    [InlineData(222.22)]
    [InlineData(333.33)]
    [InlineData(444.44)]
    [InlineData(555.55)]
    [InlineData(666.66)]
    [InlineData(777.77)]
    [InlineData(888.88)]
    [InlineData(999.99)]
    [InlineData(1111.11)]
    [InlineData(5555.55)]
    [InlineData(9999.99)]
    public void IsBadTrade_WithRepeatingDigitPrice_ShouldReturnTrue(double price)
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", (decimal)price, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue($"repeating digit price {price} should be detected as placeholder");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(10.0)]
    [InlineData(100.0)]
    [InlineData(1000.0)]
    [InlineData(10000.0)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(5.0)]
    [InlineData(50.0)]
    [InlineData(500.0)]
    public void IsBadTrade_WithValidRoundNumbers_ShouldReturnFalse(double price)
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", (decimal)price, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeFalse($"valid round number {price} should not be detected as placeholder");
    }

    [Fact]
    public void IsBadTrade_WithInvalidSize_ShouldReturnTrue()
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", 150.00m, -100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void IsBadTrade_WithZeroSize_ShouldReturnTrue()
    {
        // Arrange & Act
        var isBad = _filter.IsBadTrade("AAPL", 150.00m, 0, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void IsBadTrade_WithExtremeDeviation_ShouldReturnTrue()
    {
        // Arrange - first establish reference price
        _filter.SetReferencePrice("AAPL", 150.00m);

        // Act - 70% deviation
        var isBad = _filter.IsBadTrade("AAPL", 255.00m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void IsBadTrade_WithModerateDeviation_ShouldReturnFalse()
    {
        // Arrange - first establish reference price
        _filter.SetReferencePrice("AAPL", 150.00m);

        // Act - 20% deviation (within threshold)
        var isBad = _filter.IsBadTrade("AAPL", 180.00m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeFalse();
    }

    [Fact]
    public void IsBadQuote_WithCrossedMarket_ShouldReturnTrue()
    {
        // Arrange & Act - bid > ask
        var isBad = _filter.IsBadQuote("AAPL", 151.00m, 150.00m, 100, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void IsBadQuote_WithValidQuote_ShouldReturnFalse()
    {
        // Arrange & Act
        var isBad = _filter.IsBadQuote("AAPL", 149.95m, 150.05m, 100, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeFalse();
    }

    [Fact]
    public void IsBadQuote_WithPlaceholderBidPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var isBad = _filter.IsBadQuote("AAPL", 111.11m, 150.05m, 100, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void IsBadQuote_WithPlaceholderAskPrice_ShouldReturnTrue()
    {
        // Arrange & Act
        var isBad = _filter.IsBadQuote("AAPL", 149.95m, 222.22m, 100, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void OnBadTick_EventShouldBeFired_WhenBadTickDetected()
    {
        // Arrange
        BadTickAlert? capturedAlert = null;
        _filter.OnBadTick += alert => capturedAlert = alert;

        // Act
        _filter.IsBadTrade("AAPL", -10.00m, 100, DateTimeOffset.UtcNow);

        // Assert
        capturedAlert.Should().NotBeNull();
        capturedAlert!.Value.Symbol.Should().Be("AAPL");
        capturedAlert.Value.Price.Should().Be(-10.00m);
        capturedAlert.Value.Reasons.Should().Contain(BadTickReason.NegativeOrZeroPrice);
    }

    [Fact]
    public void GetStats_ShouldTrackBadTicksCorrectly()
    {
        // Arrange & Act
        _filter.IsBadTrade("AAPL", 150.00m, 100, DateTimeOffset.UtcNow); // Valid
        _filter.IsBadTrade("AAPL", -10.00m, 100, DateTimeOffset.UtcNow); // Bad
        _filter.IsBadTrade("MSFT", 111.11m, 100, DateTimeOffset.UtcNow); // Bad - placeholder

        var stats = _filter.GetStats();

        // Assert
        stats.TotalTicksProcessed.Should().Be(3);
        stats.TotalBadTicksDetected.Should().Be(2);
        stats.SymbolStats.Should().HaveCount(2);
    }

    [Fact]
    public void SetReferencePrice_ShouldEstablishBaseline()
    {
        // Arrange
        _filter.SetReferencePrice("AAPL", 150.00m);

        // Act - significant deviation from reference
        var isBad = _filter.IsBadTrade("AAPL", 300.00m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void StrictConfig_ShouldBeSensitiveToSmallDeviations()
    {
        // Arrange
        using var strictFilter = new BadTickFilter(BadTickFilterConfig.Strict);
        strictFilter.SetReferencePrice("AAPL", 150.00m);

        // Act - 15% deviation exceeds 10% threshold
        var isBad = strictFilter.IsBadTrade("AAPL", 172.50m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeTrue();
    }

    [Fact]
    public void LenientConfig_ShouldAllowLargerDeviations()
    {
        // Arrange
        using var lenientFilter = new BadTickFilter(BadTickFilterConfig.Lenient);
        lenientFilter.SetReferencePrice("AAPL", 150.00m);

        // Act - 60% deviation within 100% threshold
        var isBad = lenientFilter.IsBadTrade("AAPL", 240.00m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeFalse();
    }

    [Fact]
    public void Dispose_ShouldStopProcessing()
    {
        // Arrange
        _filter.Dispose();

        // Act
        var isBad = _filter.IsBadTrade("AAPL", -10.00m, 100, DateTimeOffset.UtcNow);

        // Assert
        isBad.Should().BeFalse();
    }

    public void Dispose()
    {
        _filter.Dispose();
    }
}
