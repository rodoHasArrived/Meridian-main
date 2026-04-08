using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="SymbolMappingService"/> — singleton access, provider-specific
/// symbol transformations, reverse transforms, known providers, and model types.
/// </summary>
public sealed class SymbolMappingServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNullSingleton()
    {
        // Act
        var instance = SymbolMappingService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = SymbolMappingService.Instance;
        var instance2 = SymbolMappingService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void Constructor_UsesConfiguredPersistencePathWhenPresent()
    {
        using var fixture = new PathFixture("mdc-symbol-map-path");
        File.WriteAllText(
            fixture.ConfigPath,
            """
            {
              "dataRoot": "retained-data",
              "dataSources": {
                "symbolMappings": {
                  "persistencePath": "state/symbol-mappings.json"
                }
              }
            }
            """);
        var config = new AppConfigDto { DataRoot = "retained-data" };
        var service = new SymbolMappingService(new FixedConfigService(fixture.ConfigPath, config));

        var path = GetPrivateField<string>(service, "_mappingsFilePath");

        path.Should().Be(Path.Combine(fixture.RootPath, "state", "symbol-mappings.json"));
    }

    // ── KnownProviders ───────────────────────────────────────────────

    [Fact]
    public void KnownProviders_ShouldNotBeEmpty()
    {
        // Act
        var providers = SymbolMappingService.KnownProviders;

        // Assert
        providers.Should().NotBeNull();
        providers.Should().NotBeEmpty();
    }

    [Fact]
    public void KnownProviders_ShouldContainMajorProviders()
    {
        // Act
        var providers = SymbolMappingService.KnownProviders;

        // Assert
        providers.Should().Contain(p => p.Id == "IB");
        providers.Should().Contain(p => p.Id == "Alpaca");
        providers.Should().Contain(p => p.Id == "Polygon");
        providers.Should().Contain(p => p.Id == "Yahoo");
        providers.Should().Contain(p => p.Id == "Stooq");
    }

    [Fact]
    public void KnownProviders_EachProvider_ShouldHaveDisplayName()
    {
        // Act
        var providers = SymbolMappingService.KnownProviders;

        // Assert
        foreach (var provider in providers)
        {
            provider.DisplayName.Should().NotBeNullOrWhiteSpace(
                $"Provider '{provider.Id}' should have a display name");
        }
    }

    [Fact]
    public void KnownProviders_EachProvider_ShouldHaveTransformDescription()
    {
        // Act
        var providers = SymbolMappingService.KnownProviders;

        // Assert
        foreach (var provider in providers)
        {
            provider.TransformDescription.Should().NotBeNullOrWhiteSpace(
                $"Provider '{provider.Id}' should have a transform description");
        }
    }

    // ── ApplyDefaultTransform ────────────────────────────────────────

    [Theory]
    [InlineData("AAPL", "Alpaca", "AAPL")]
    [InlineData("aapl", "Alpaca", "AAPL")]
    [InlineData("aapl", "Polygon", "AAPL")]
    [InlineData("aapl", "Finnhub", "AAPL")]
    [InlineData("aapl", "AlphaVantage", "AAPL")]
    [InlineData("aapl", "NYSE", "AAPL")]
    public void ApplyDefaultTransform_UppercaseProviders_ShouldReturnUppercase(
        string symbol, string providerId, string expected)
    {
        // Act
        var result = SymbolMappingService.ApplyDefaultTransform(symbol, providerId);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ApplyDefaultTransform_IB_ShouldReplaceDotsWithSpaces()
    {
        // Act
        var result = SymbolMappingService.ApplyDefaultTransform("BRK.B", "IB");

        // Assert
        result.Should().Be("BRK B");
    }

    [Fact]
    public void ApplyDefaultTransform_Yahoo_ShouldReplaceDotsWithDashes()
    {
        // Act
        var result = SymbolMappingService.ApplyDefaultTransform("BRK.B", "Yahoo");

        // Assert
        result.Should().Be("BRK-B");
    }

    [Fact]
    public void ApplyDefaultTransform_Tiingo_ShouldReplaceDotsWithDashes()
    {
        // Act
        var result = SymbolMappingService.ApplyDefaultTransform("BRK.B", "Tiingo");

        // Assert
        result.Should().Be("BRK-B");
    }

    [Fact]
    public void ApplyDefaultTransform_Stooq_ShouldReturnLowercaseWithUsSuffix()
    {
        // Act
        var result = SymbolMappingService.ApplyDefaultTransform("AAPL", "Stooq");

        // Assert
        result.Should().Be("aapl.us");
    }

    [Fact]
    public void ApplyDefaultTransform_Stooq_WithDots_ShouldReplaceDots()
    {
        // Act
        var result = SymbolMappingService.ApplyDefaultTransform("BRK.B", "Stooq");

        // Assert
        result.Should().Be("brk-b.us");
    }

    [Fact]
    public void ApplyDefaultTransform_UnknownProvider_ShouldReturnUppercase()
    {
        // Act
        var result = SymbolMappingService.ApplyDefaultTransform("aapl", "UnknownProvider");

        // Assert
        result.Should().Be("AAPL");
    }

    // ── ApplyReverseTransform ────────────────────────────────────────

    [Fact]
    public void ApplyReverseTransform_IB_ShouldReplaceSpacesWithDots()
    {
        // Act
        var result = SymbolMappingService.ApplyReverseTransform("BRK B", "IB");

        // Assert
        result.Should().Be("BRK.B");
    }

    [Fact]
    public void ApplyReverseTransform_Yahoo_ShouldReplaceDashesWithDots()
    {
        // Act
        var result = SymbolMappingService.ApplyReverseTransform("BRK-B", "Yahoo");

        // Assert
        result.Should().Be("BRK.B");
    }

    [Fact]
    public void ApplyReverseTransform_Stooq_ShouldRemoveUsSuffixAndReplaceDashes()
    {
        // Act
        var result = SymbolMappingService.ApplyReverseTransform("brk-b.us", "Stooq");

        // Assert
        result.Should().Be("BRK.B");
    }

    [Fact]
    public void ApplyReverseTransform_Alpaca_ShouldReturnUppercase()
    {
        // Act
        var result = SymbolMappingService.ApplyReverseTransform("aapl", "Alpaca");

        // Assert
        result.Should().Be("AAPL");
    }

    [Fact]
    public void ApplyReverseTransform_StockSharp_ShouldStripExchangeSuffix()
    {
        // Act
        var result = SymbolMappingService.ApplyReverseTransform("AAPL@NASDAQ", "StockSharp");

        // Assert
        result.Should().Be("AAPL");
    }

    [Fact]
    public void ApplyReverseTransform_UnknownProvider_ShouldReturnUppercase()
    {
        // Act
        var result = SymbolMappingService.ApplyReverseTransform("aapl", "UnknownProvider");

        // Assert
        result.Should().Be("AAPL");
    }

    // ── GetMappings / TestMapping ────────────────────────────────────

    [Fact]
    public void GetMappings_Default_ShouldReturnNonNullList()
    {
        // Arrange
        var service = SymbolMappingService.Instance;

        // Act
        var mappings = service.GetMappings();

        // Assert
        mappings.Should().NotBeNull();
    }

    [Fact]
    public void TestMapping_ShouldReturnEntryForEachKnownProvider()
    {
        // Arrange
        var service = SymbolMappingService.Instance;

        // Act
        var results = service.TestMapping("AAPL");

        // Assert
        results.Should().NotBeNull();
        results.Should().HaveCount(SymbolMappingService.KnownProviders.Count);
        foreach (var provider in SymbolMappingService.KnownProviders)
        {
            results.Should().ContainKey(provider.Id);
        }
    }

    [Fact]
    public void TestMapping_SimpleSymbol_ShouldApplyDefaultTransforms()
    {
        // Arrange
        var service = SymbolMappingService.Instance;

        // Act
        var results = service.TestMapping("AAPL");

        // Assert
        results["Alpaca"].Should().Be("AAPL");
        results["Polygon"].Should().Be("AAPL");
        results["Stooq"].Should().Be("aapl.us");
    }

    // ── SymbolMapping model ──────────────────────────────────────────

    [Fact]
    public void SymbolMapping_DefaultValues_ShouldHaveReasonableDefaults()
    {
        // Act
        var mapping = new SymbolMapping();

        // Assert
        mapping.CanonicalSymbol.Should().BeEmpty();
        mapping.SecurityType.Should().Be("STK");
        mapping.Currency.Should().Be("USD");
        mapping.IsCustomMapping.Should().BeFalse();
        mapping.DisplayName.Should().BeNull();
        mapping.PrimaryExchange.Should().BeNull();
        mapping.Figi.Should().BeNull();
        mapping.Isin.Should().BeNull();
        mapping.Cusip.Should().BeNull();
        mapping.Notes.Should().BeNull();
        mapping.ProviderSymbols.Should().BeNull();
    }

    [Fact]
    public void SymbolMapping_CanStoreAllProperties()
    {
        // Act
        var mapping = new SymbolMapping
        {
            CanonicalSymbol = "AAPL",
            DisplayName = "Apple Inc.",
            SecurityType = "STK",
            PrimaryExchange = "NASDAQ",
            Currency = "USD",
            Figi = "BBG000B9XRY4",
            Isin = "US0378331005",
            Cusip = "037833100",
            Notes = "Test mapping",
            IsCustomMapping = true,
            ProviderSymbols = new Dictionary<string, string>
            {
                ["IB"] = "AAPL",
                ["Stooq"] = "aapl.us"
            }
        };

        // Assert
        mapping.CanonicalSymbol.Should().Be("AAPL");
        mapping.DisplayName.Should().Be("Apple Inc.");
        mapping.SecurityType.Should().Be("STK");
        mapping.PrimaryExchange.Should().Be("NASDAQ");
        mapping.Currency.Should().Be("USD");
        mapping.Figi.Should().Be("BBG000B9XRY4");
        mapping.Isin.Should().Be("US0378331005");
        mapping.Cusip.Should().Be("037833100");
        mapping.Notes.Should().Be("Test mapping");
        mapping.IsCustomMapping.Should().BeTrue();
        mapping.ProviderSymbols.Should().HaveCount(2);
        mapping.ProviderSymbols!["IB"].Should().Be("AAPL");
        mapping.ProviderSymbols["Stooq"].Should().Be("aapl.us");
    }

    [Fact]
    public void SymbolMapping_CreatedAt_ShouldDefaultToUtcNow()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var mapping = new SymbolMapping();

        // Assert
        var after = DateTime.UtcNow.AddSeconds(1);
        mapping.CreatedAt.Should().BeAfter(before);
        mapping.CreatedAt.Should().BeBefore(after);
    }

    [Fact]
    public void SymbolMapping_UpdatedAt_ShouldDefaultToUtcNow()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var mapping = new SymbolMapping();

        // Assert
        var after = DateTime.UtcNow.AddSeconds(1);
        mapping.UpdatedAt.Should().BeAfter(before);
        mapping.UpdatedAt.Should().BeBefore(after);
    }

    // ── SymbolMappingsConfig model ───────────────────────────────────

    [Fact]
    public void SymbolMappingsConfig_DefaultValues_ShouldHaveReasonableDefaults()
    {
        // Act
        var config = new SymbolMappingsConfig();

        // Assert
        config.Version.Should().Be("1.0");
        config.Mappings.Should().BeNull();
    }

    [Fact]
    public void SymbolMappingsConfig_CanStoreMappingsArray()
    {
        // Act
        var config = new SymbolMappingsConfig
        {
            Mappings = new[]
            {
                new SymbolMapping { CanonicalSymbol = "SPY" },
                new SymbolMapping { CanonicalSymbol = "AAPL" }
            }
        };

        // Assert
        config.Mappings.Should().HaveCount(2);
        config.Mappings[0].CanonicalSymbol.Should().Be("SPY");
        config.Mappings[1].CanonicalSymbol.Should().Be("AAPL");
    }

    // ── MappingProviderInfo model ────────────────────────────────────

    [Fact]
    public void MappingProviderInfo_RecordProperties_ShouldBeAccessible()
    {
        // Act
        var info = new MappingProviderInfo("TestId", "Test Display", "No transform", SymbolTransform.None);

        // Assert
        info.Id.Should().Be("TestId");
        info.DisplayName.Should().Be("Test Display");
        info.TransformDescription.Should().Be("No transform");
        info.DefaultTransform.Should().Be(SymbolTransform.None);
    }

    [Fact]
    public void MappingProviderInfo_RecordEquality_ShouldWorkCorrectly()
    {
        // Arrange
        var info1 = new MappingProviderInfo("IB", "Interactive Brokers", "dots to spaces", SymbolTransform.DotsToSpaces);
        var info2 = new MappingProviderInfo("IB", "Interactive Brokers", "dots to spaces", SymbolTransform.DotsToSpaces);

        // Assert
        info1.Should().Be(info2);
    }

    // ── SymbolTransform enum ─────────────────────────────────────────

    [Theory]
    [InlineData(SymbolTransform.None)]
    [InlineData(SymbolTransform.Uppercase)]
    [InlineData(SymbolTransform.Lowercase)]
    [InlineData(SymbolTransform.DotsToSpaces)]
    [InlineData(SymbolTransform.DotsToDashes)]
    [InlineData(SymbolTransform.StooqFormat)]
    [InlineData(SymbolTransform.StockSharpFormat)]
    public void SymbolTransform_AllValues_ShouldBeDefined(SymbolTransform transform)
    {
        // Assert
        Enum.IsDefined(typeof(SymbolTransform), transform).Should().BeTrue();
    }

    // ── EnsureProviderEntries ────────────────────────────────────────

    [Fact]
    public void EnsureProviderEntries_NullProviderSymbols_ShouldInitializeAll()
    {
        // Arrange
        var service = SymbolMappingService.Instance;
        var mapping = new SymbolMapping
        {
            CanonicalSymbol = "TEST",
            ProviderSymbols = null
        };

        // Act
        var result = service.EnsureProviderEntries(mapping);

        // Assert
        result.ProviderSymbols.Should().NotBeNull();
        result.ProviderSymbols.Should().HaveCount(SymbolMappingService.KnownProviders.Count);
        foreach (var provider in SymbolMappingService.KnownProviders)
        {
            result.ProviderSymbols.Should().ContainKey(provider.Id);
        }
    }

    [Fact]
    public void EnsureProviderEntries_ExistingEntries_ShouldPreserveValues()
    {
        // Arrange
        var service = SymbolMappingService.Instance;
        var mapping = new SymbolMapping
        {
            CanonicalSymbol = "AAPL",
            ProviderSymbols = new Dictionary<string, string>
            {
                ["IB"] = "AAPL CUSTOM"
            }
        };

        // Act
        var result = service.EnsureProviderEntries(mapping);

        // Assert
        result.ProviderSymbols!["IB"].Should().Be("AAPL CUSTOM");
    }

    // ── ExportToCsv ──────────────────────────────────────────────────

    [Fact]
    public void ExportToCsv_ShouldReturnHeaderRow()
    {
        // Arrange
        var service = SymbolMappingService.Instance;

        // Act
        var csv = service.ExportToCsv();

        // Assert
        csv.Should().NotBeNull();
        csv.Should().StartWith("Canonical");
        foreach (var provider in SymbolMappingService.KnownProviders)
        {
            csv.Should().Contain(provider.Id);
        }
    }

    // ── MapToProvider / MapToCanonical round-trip ─────────────────────

    [Theory]
    [InlineData("SPY", "Alpaca")]
    [InlineData("AAPL", "Polygon")]
    [InlineData("MSFT", "Finnhub")]
    public void MapToProvider_SimpleSymbol_ShouldReturnUppercase(string symbol, string providerId)
    {
        // Arrange
        var service = SymbolMappingService.Instance;

        // Act
        var result = service.MapToProvider(symbol, providerId);

        // Assert
        result.Should().Be(symbol.ToUpperInvariant());
    }

    [Fact]
    public void MapToCanonical_SimpleSymbol_ShouldReturnUppercase()
    {
        // Arrange
        var service = SymbolMappingService.Instance;

        // Act
        var result = service.MapToCanonical("aapl", "Alpaca");

        // Assert
        result.Should().Be("AAPL");
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull();
        return (T)field!.GetValue(instance)!;
    }
}
