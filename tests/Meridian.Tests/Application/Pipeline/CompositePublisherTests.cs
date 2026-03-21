using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Events.Publishers;
using Meridian.Domain.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Meridian.Tests;

public class CompositePublisherTests
{
    [Fact]
    public void TryPublish_AllPublishersSucceed_ReturnsTrue()
    {
        // Arrange
        var publisher1 = Substitute.For<IMarketEventPublisher>();
        var publisher2 = Substitute.For<IMarketEventPublisher>();

        publisher1.TryPublish(Arg.Any<MarketEvent>()).Returns(true);
        publisher2.TryPublish(Arg.Any<MarketEvent>()).Returns(true);

        var composite = new CompositePublisher(publisher1, publisher2);
        var evt = CreateTestEvent();

        // Act
        var result = composite.TryPublish(evt);

        // Assert
        result.Should().BeTrue();
        publisher1.Received(1).TryPublish(Arg.Any<MarketEvent>());
        publisher2.Received(1).TryPublish(Arg.Any<MarketEvent>());
    }

    [Fact]
    public void TryPublish_OnePublisherFails_StillReturnsTrue()
    {
        // Arrange
        var publisher1 = Substitute.For<IMarketEventPublisher>();
        var publisher2 = Substitute.For<IMarketEventPublisher>();

        publisher1.TryPublish(Arg.Any<MarketEvent>()).Returns(true);
        publisher2.TryPublish(Arg.Any<MarketEvent>()).Returns(false);

        var composite = new CompositePublisher(publisher1, publisher2);
        var evt = CreateTestEvent();

        // Act
        var result = composite.TryPublish(evt);

        // Assert
        result.Should().BeTrue(); // At least one succeeded
        publisher1.Received(1).TryPublish(Arg.Any<MarketEvent>());
        publisher2.Received(1).TryPublish(Arg.Any<MarketEvent>());
    }

    [Fact]
    public void TryPublish_AllPublishersFail_ReturnsFalse()
    {
        // Arrange
        var publisher1 = Substitute.For<IMarketEventPublisher>();
        var publisher2 = Substitute.For<IMarketEventPublisher>();

        publisher1.TryPublish(Arg.Any<MarketEvent>()).Returns(false);
        publisher2.TryPublish(Arg.Any<MarketEvent>()).Returns(false);

        var composite = new CompositePublisher(publisher1, publisher2);
        var evt = CreateTestEvent();

        // Act
        var result = composite.TryPublish(evt);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryPublish_PublisherThrowsException_ContinuesToOthers()
    {
        // Arrange
        var publisher1 = Substitute.For<IMarketEventPublisher>();
        var publisher2 = Substitute.For<IMarketEventPublisher>();

        publisher1.TryPublish(Arg.Any<MarketEvent>()).Throws(new Exception("Test exception"));
        publisher2.TryPublish(Arg.Any<MarketEvent>()).Returns(true);

        var composite = new CompositePublisher(publisher1, publisher2);
        var evt = CreateTestEvent();

        // Act
        var result = composite.TryPublish(evt);

        // Assert
        result.Should().BeTrue(); // Second publisher succeeded
        publisher1.Received(1).TryPublish(Arg.Any<MarketEvent>());
        publisher2.Received(1).TryPublish(Arg.Any<MarketEvent>());
    }

    [Fact]
    public void Constructor_NullPublishers_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CompositePublisher((IMarketEventPublisher[])null!));
    }

    [Fact]
    public void TryPublish_NoPublishers_ReturnsFalse()
    {
        // Arrange
        var composite = new CompositePublisher(Array.Empty<IMarketEventPublisher>());
        var evt = CreateTestEvent();

        // Act
        var result = composite.TryPublish(evt);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryPublish_SinglePublisher_DelegatesToPublisher()
    {
        // Arrange
        var publisher = Substitute.For<IMarketEventPublisher>();
        publisher.TryPublish(Arg.Any<MarketEvent>()).Returns(true);

        var composite = new CompositePublisher(publisher);
        var evt = CreateTestEvent();

        // Act
        var result = composite.TryPublish(evt);

        // Assert
        result.Should().BeTrue();
        publisher.Received(1).TryPublish(Arg.Any<MarketEvent>());
    }

    private static MarketEvent CreateTestEvent()
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "TEST",
            Price: 100.00m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1
        );

        return MarketEvent.Trade(DateTimeOffset.UtcNow, "TEST", trade, seq: 1, source: "TEST");
    }
}
