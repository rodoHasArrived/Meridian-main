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

    [Theory]
    [InlineData("ibkr", DataSourceKind.IB)]
    [InlineData("interactive-brokers", DataSourceKind.IB)]
    [InlineData(" INTERACTIVEBROKERS ", DataSourceKind.IB)]
    [InlineData("alpaca-options", DataSourceKind.Alpaca)]
    [InlineData("polygon-options", DataSourceKind.Polygon)]
    [InlineData("nyse-streaming", DataSourceKind.NYSE)]
    [InlineData("yahoo-finance", DataSourceKind.Yahoo)]
    public void Read_ConfiguredAliasesUseCanonicalFamily(string alias, DataSourceKind expected)
    {
        var json = JsonSerializer.Serialize(alias);
        JsonSerializer.Deserialize<DataSourceKind>(json, Options).Should().Be(expected);
    }

    [Fact]
    public void Read_ApplicationConfigurationAcceptsAliasAtTopLevelAndInSources()
    {
        var config = JsonSerializer.Deserialize<AppConfig>("""
            {
              "dataSource": "interactive-brokers",
              "dataSources": {
                "sources": [{ "id": "ibkr", "name": "Configured IB", "provider": "ibkr" }]
              }
            }
            """, AppConfigJsonOptions.Read);

        config!.DataSource.Should().Be(DataSourceKind.IB);
        config.DataSources!.Sources.Should().ContainSingle(source => source.Provider == DataSourceKind.IB);
    }

    [Theory]
    [InlineData("\"-1\"")]
    [InlineData("\"256\"")]
    [InlineData("\"999\"")]
    [InlineData("-1")]
    [InlineData("256")]
    [InlineData("999")]
    [InlineData("\"ib-sim\"")]
    [InlineData("\"ib-flex\"")]
    public void Read_UnsupportedModesAndInvalidNumericValuesRemainRejected(string json)
    {
        var read = () => JsonSerializer.Deserialize<DataSourceKind>(json, Options);
        read.Should().Throw<JsonException>();
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
    public void Read_UndefinedNumericString_FailsClosed()
    {
        // Enum.TryParse accepts numeric strings for undefined values; the converter must not.
        var act = () => JsonSerializer.Deserialize<DataSourceKind>("\"99\"", Options);

        act.Should().Throw<JsonException>().WithMessage("*99*");
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
