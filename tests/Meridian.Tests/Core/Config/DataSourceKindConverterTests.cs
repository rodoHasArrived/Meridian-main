using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

public sealed class DataSourceKindConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new DataSourceKindConverter() }
    };

    [Theory]
    [InlineData("\"IB\"", DataSourceKind.IB)]
    [InlineData("\"alpaca\"", DataSourceKind.Alpaca)]
    [InlineData("\"SYNTHETIC\"", DataSourceKind.Synthetic)]
    public void Read_ValidStrings_ParseCaseInsensitively(string json, DataSourceKind expected)
    {
        JsonSerializer.Deserialize<DataSourceKind>(json, Options).Should().Be(expected);
    }

    [Fact]
    public void Read_UnknownString_FailsClosedWithValidValues()
    {
        // Coercing a typo to a default provider would silently route the operator to a data
        // source they never configured — the config load must fail instead.
        var act = () => JsonSerializer.Deserialize<DataSourceKind>("\"alpacca\"", Options);

        act.Should().Throw<JsonException>()
            .WithMessage("*alpacca*")
            .WithMessage($"*{nameof(DataSourceKind.Alpaca)}*");
    }

    [Fact]
    public void Read_UnknownNumber_FailsClosed()
    {
        var act = () => JsonSerializer.Deserialize<DataSourceKind>("999", Options);

        act.Should().Throw<JsonException>().WithMessage("*999*");
    }

    [Fact]
    public void Read_DefinedNumber_Parses()
    {
        var expected = Enum.GetValues<DataSourceKind>()[0];

        JsonSerializer.Deserialize<DataSourceKind>(((int)expected).ToString(), Options)
            .Should().Be(expected);
    }

    [Fact]
    public void Write_RoundTripsAsString()
    {
        JsonSerializer.Serialize(DataSourceKind.Alpaca, Options).Should().Be("\"Alpaca\"");
    }
}
