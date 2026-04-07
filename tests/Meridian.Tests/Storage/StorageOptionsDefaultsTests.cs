using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Storage;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests to verify default storage configuration values.
/// Ensures that "BySymbol" is the default file naming convention as documented.
/// </summary>
public sealed class StorageOptionsDefaultsTests
{
    [Fact]
    public void StorageOptions_DefaultNamingConvention_ShouldBeBySymbol()
    {
        // Arrange & Act
        var options = new StorageOptions();

        // Assert
        options.NamingConvention.Should().Be(FileNamingConvention.BySymbol,
            "BySymbol is the recommended default for organizing files by symbol first");
    }

    [Fact]
    public void StorageConfig_DefaultNamingConvention_ShouldBeBySymbol()
    {
        // Arrange & Act
        var config = new StorageConfig();

        // Assert
        config.NamingConvention.Should().Be("BySymbol",
            "BySymbol is the recommended default in configuration");
    }

    [Fact]
    public void StorageConfig_ToStorageOptions_ShouldUseBySymbolWhenNull()
    {
        // Arrange
        var config = new StorageConfig(NamingConvention: null!); // Intentionally passing null to verify default fallback behavior

        // Act
        var options = config.ToStorageOptions("data", false);

        // Assert
        options.NamingConvention.Should().Be(FileNamingConvention.BySymbol,
            "null or empty NamingConvention should default to BySymbol");
    }

    [Fact]
    public void StorageConfig_ToStorageOptions_ShouldUseBySymbolWhenEmpty()
    {
        // Arrange
        var config = new StorageConfig(NamingConvention: "");

        // Act
        var options = config.ToStorageOptions("data", false);

        // Assert
        options.NamingConvention.Should().Be(FileNamingConvention.BySymbol,
            "empty NamingConvention should default to BySymbol");
    }

    [Fact]
    public void StorageConfig_ToStorageOptions_ShouldParseBySymbolCaseInsensitive()
    {
        // Arrange & Act
        var optionsLower = new StorageConfig(NamingConvention: "bysymbol").ToStorageOptions("data", false);
        var optionsUpper = new StorageConfig(NamingConvention: "BYSYMBOL").ToStorageOptions("data", false);
        var optionsMixed = new StorageConfig(NamingConvention: "BySymbol").ToStorageOptions("data", false);

        // Assert
        optionsLower.NamingConvention.Should().Be(FileNamingConvention.BySymbol);
        optionsUpper.NamingConvention.Should().Be(FileNamingConvention.BySymbol);
        optionsMixed.NamingConvention.Should().Be(FileNamingConvention.BySymbol);
    }

    [Fact]
    public void StorageConfig_ToStorageOptions_ShouldDefaultToBySymbolForInvalidValue()
    {
        // Arrange
        var config = new StorageConfig(NamingConvention: "InvalidValue");

        // Act
        var options = config.ToStorageOptions("data", false);

        // Assert
        options.NamingConvention.Should().Be(FileNamingConvention.BySymbol,
            "invalid NamingConvention should default to BySymbol");
    }

    [Fact]
    public void StorageOptions_DefaultDatePartition_ShouldBeDaily()
    {
        // Arrange & Act
        var options = new StorageOptions();

        // Assert
        options.DatePartition.Should().Be(DatePartition.Daily,
            "Daily is the recommended default for date partitioning");
    }

    [Theory]
    [InlineData("flat", FileNamingConvention.Flat)]
    [InlineData("bysymbol", FileNamingConvention.BySymbol)]
    [InlineData("bydate", FileNamingConvention.ByDate)]
    [InlineData("bytype", FileNamingConvention.ByType)]
    [InlineData("bysource", FileNamingConvention.BySource)]
    [InlineData("byassetclass", FileNamingConvention.ByAssetClass)]
    [InlineData("hierarchical", FileNamingConvention.Hierarchical)]
    [InlineData("canonical", FileNamingConvention.Canonical)]
    public void StorageConfig_ToStorageOptions_ShouldParseAllConventions(string input, FileNamingConvention expected)
    {
        // Arrange
        var config = new StorageConfig(NamingConvention: input);

        // Act
        var options = config.ToStorageOptions("data", false);

        // Assert
        options.NamingConvention.Should().Be(expected);
    }
}
