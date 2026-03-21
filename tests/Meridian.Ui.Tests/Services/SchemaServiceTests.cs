using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public class SchemaServiceTests
{
    [Fact]
    public void GetJsonSchema_ReturnsSchemaForTrade()
    {
        // Arrange
        var service = new SchemaService();

        // Act
        var schema = service.GetJsonSchema("Trade");

        // Assert
        schema.Should().NotBeNull();
        schema.Should().Contain("Trade");
        schema.Should().Contain("symbol");
        schema.Should().Contain("price");
        schema.Should().Contain("size");
        schema.Should().Contain("timestamp");
    }

    [Fact]
    public void GetJsonSchema_ReturnsSchemaForBboQuote()
    {
        // Arrange
        var service = new SchemaService();

        // Act
        var schema = service.GetJsonSchema("BboQuote");

        // Assert
        schema.Should().NotBeNull();
        schema.Should().Contain("BboQuote");
        schema.Should().Contain("bidPrice");
        schema.Should().Contain("askPrice");
        schema.Should().Contain("spread");
    }

    [Fact]
    public void GetJsonSchema_ReturnsSchemaForLOBSnapshot()
    {
        // Arrange
        var service = new SchemaService();

        // Act
        var schema = service.GetJsonSchema("LOBSnapshot");

        // Assert
        schema.Should().NotBeNull();
        schema.Should().Contain("LOBSnapshot");
        schema.Should().Contain("bids");
        schema.Should().Contain("asks");
    }

    [Fact]
    public void GetJsonSchema_ReturnsSchemaForHistoricalBar()
    {
        // Arrange
        var service = new SchemaService();

        // Act
        var schema = service.GetJsonSchema("HistoricalBar");

        // Assert
        schema.Should().NotBeNull();
        schema.Should().Contain("HistoricalBar");
        schema.Should().Contain("open");
        schema.Should().Contain("high");
        schema.Should().Contain("low");
        schema.Should().Contain("close");
        schema.Should().Contain("volume");
    }

    [Fact]
    public void GetJsonSchema_ReturnsNullForUnknownType()
    {
        // Arrange
        var service = new SchemaService();

        // Act
        var schema = service.GetJsonSchema("UnknownEventType");

        // Assert
        schema.Should().BeNull();
    }

    [Fact]
    public void GetJsonSchema_IsCaseInsensitive()
    {
        // Arrange
        var service = new SchemaService();

        // Act
        var schema1 = service.GetJsonSchema("trade");
        var schema2 = service.GetJsonSchema("TRADE");
        var schema3 = service.GetJsonSchema("Trade");

        // Assert
        schema1.Should().NotBeNull();
        schema2.Should().NotBeNull();
        schema3.Should().NotBeNull();
        schema1.Should().Be(schema2);
        schema2.Should().Be(schema3);
    }
}
