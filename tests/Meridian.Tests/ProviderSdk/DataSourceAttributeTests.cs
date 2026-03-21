using FluentAssertions;
using Meridian.Infrastructure.DataSources;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

/// <summary>
/// Tests for DataSourceAttribute, DataSourceMetadata, and extension methods.
/// </summary>
public sealed class DataSourceAttributeTests
{
    #region DataSourceAttribute Constructor

    [Fact]
    public void DataSourceAttribute_ValidParams_SetsProperties()
    {
        var attr = new DataSourceAttribute("alpaca", "Alpaca Markets", DataSourceType.Hybrid, DataSourceCategory.Broker);

        attr.Id.Should().Be("alpaca");
        attr.DisplayName.Should().Be("Alpaca Markets");
        attr.Type.Should().Be(DataSourceType.Hybrid);
        attr.Category.Should().Be(DataSourceCategory.Broker);
    }

    [Fact]
    public void DataSourceAttribute_Defaults_AreCorrect()
    {
        var attr = new DataSourceAttribute("test", "Test", DataSourceType.Realtime, DataSourceCategory.Free);

        attr.Priority.Should().Be(100);
        attr.EnabledByDefault.Should().BeTrue();
        attr.Description.Should().BeNull();
        attr.ConfigSection.Should().BeNull();
    }

    [Fact]
    public void DataSourceAttribute_NullId_ThrowsArgumentNullException()
    {
        var act = () => new DataSourceAttribute(null!, "Name", DataSourceType.Realtime, DataSourceCategory.Free);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("id");
    }

    [Fact]
    public void DataSourceAttribute_NullDisplayName_ThrowsArgumentNullException()
    {
        var act = () => new DataSourceAttribute("id", null!, DataSourceType.Realtime, DataSourceCategory.Free);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("displayName");
    }

    [Fact]
    public void DataSourceAttribute_OptionalProperties_Settable()
    {
        var attr = new DataSourceAttribute("test", "Test Source", DataSourceType.Historical, DataSourceCategory.Premium)
        {
            Priority = 10,
            EnabledByDefault = false,
            Description = "A test data source",
            ConfigSection = "TestConfig"
        };

        attr.Priority.Should().Be(10);
        attr.EnabledByDefault.Should().BeFalse();
        attr.Description.Should().Be("A test data source");
        attr.ConfigSection.Should().Be("TestConfig");
    }

    #endregion

    #region DataSourceMetadata

    [Fact]
    public void DataSourceMetadata_FromAttribute_MapsAllProperties()
    {
        var attr = new DataSourceAttribute("polygon", "Polygon.io", DataSourceType.Hybrid, DataSourceCategory.Aggregator)
        {
            Priority = 20,
            EnabledByDefault = false,
            Description = "Polygon data",
            ConfigSection = "PolygonConfig"
        };

        var metadata = DataSourceMetadata.FromAttribute(attr, typeof(DataSourceAttributeTests));

        metadata.Id.Should().Be("polygon");
        metadata.DisplayName.Should().Be("Polygon.io");
        metadata.Description.Should().Be("Polygon data");
        metadata.Type.Should().Be(DataSourceType.Hybrid);
        metadata.Category.Should().Be(DataSourceCategory.Aggregator);
        metadata.Priority.Should().Be(20);
        metadata.EnabledByDefault.Should().BeFalse();
        metadata.ConfigSection.Should().Be("PolygonConfig");
        metadata.ImplementationType.Should().Be(typeof(DataSourceAttributeTests));
    }

    [Fact]
    public void DataSourceMetadata_FromAttribute_NullConfigSection_DefaultsToId()
    {
        var attr = new DataSourceAttribute("yahoo", "Yahoo Finance", DataSourceType.Historical, DataSourceCategory.Free);

        var metadata = DataSourceMetadata.FromAttribute(attr, typeof(object));

        metadata.ConfigSection.Should().Be("yahoo");
    }

    [Fact]
    public void DataSourceMetadata_IsRealtime_TrueForRealtimeAndHybrid()
    {
        var realtimeAttr = new DataSourceAttribute("rt", "RT", DataSourceType.Realtime, DataSourceCategory.Free);
        var hybridAttr = new DataSourceAttribute("hb", "HB", DataSourceType.Hybrid, DataSourceCategory.Free);
        var historicalAttr = new DataSourceAttribute("hs", "HS", DataSourceType.Historical, DataSourceCategory.Free);

        DataSourceMetadata.FromAttribute(realtimeAttr, typeof(object)).IsRealtime.Should().BeTrue();
        DataSourceMetadata.FromAttribute(hybridAttr, typeof(object)).IsRealtime.Should().BeTrue();
        DataSourceMetadata.FromAttribute(historicalAttr, typeof(object)).IsRealtime.Should().BeFalse();
    }

    [Fact]
    public void DataSourceMetadata_IsHistorical_TrueForHistoricalAndHybrid()
    {
        var realtimeAttr = new DataSourceAttribute("rt", "RT", DataSourceType.Realtime, DataSourceCategory.Free);
        var hybridAttr = new DataSourceAttribute("hb", "HB", DataSourceType.Hybrid, DataSourceCategory.Free);
        var historicalAttr = new DataSourceAttribute("hs", "HS", DataSourceType.Historical, DataSourceCategory.Free);

        DataSourceMetadata.FromAttribute(realtimeAttr, typeof(object)).IsHistorical.Should().BeFalse();
        DataSourceMetadata.FromAttribute(hybridAttr, typeof(object)).IsHistorical.Should().BeTrue();
        DataSourceMetadata.FromAttribute(historicalAttr, typeof(object)).IsHistorical.Should().BeTrue();
    }

    #endregion

    #region DataSourceType Enum

    [Fact]
    public void DataSourceType_HasExpectedValues()
    {
        Enum.GetValues<DataSourceType>().Should().HaveCount(3);
        ((int)DataSourceType.Realtime).Should().Be(0);
        ((int)DataSourceType.Historical).Should().Be(1);
        ((int)DataSourceType.Hybrid).Should().Be(2);
    }

    #endregion

    #region DataSourceCategory Enum

    [Fact]
    public void DataSourceCategory_HasExpectedValues()
    {
        Enum.GetValues<DataSourceCategory>().Should().HaveCount(5);
        Enum.GetValues<DataSourceCategory>().Should().Contain(DataSourceCategory.Exchange);
        Enum.GetValues<DataSourceCategory>().Should().Contain(DataSourceCategory.Broker);
        Enum.GetValues<DataSourceCategory>().Should().Contain(DataSourceCategory.Aggregator);
        Enum.GetValues<DataSourceCategory>().Should().Contain(DataSourceCategory.Free);
        Enum.GetValues<DataSourceCategory>().Should().Contain(DataSourceCategory.Premium);
    }

    #endregion

    #region Extension Methods

    [Fact]
    public void IsDataSource_TypeWithoutAttribute_ReturnsFalse()
    {
        typeof(string).IsDataSource().Should().BeFalse();
    }

    [Fact]
    public void IsDataSource_AbstractType_ReturnsFalse()
    {
        typeof(Stream).IsDataSource().Should().BeFalse();
    }

    [Fact]
    public void IsDataSource_InterfaceType_ReturnsFalse()
    {
        typeof(IDataSource).IsDataSource().Should().BeFalse();
    }

    [Fact]
    public void GetDataSourceAttribute_TypeWithoutAttribute_ReturnsNull()
    {
        typeof(string).GetDataSourceAttribute().Should().BeNull();
    }

    [Fact]
    public void GetDataSourceMetadata_TypeWithoutAttribute_ReturnsNull()
    {
        typeof(string).GetDataSourceMetadata().Should().BeNull();
    }

    #endregion
}
