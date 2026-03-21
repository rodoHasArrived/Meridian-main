using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for the SymbolRegistryService.
/// </summary>
public sealed class SymbolRegistryServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly SymbolRegistryService _service;

    public SymbolRegistryServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SymbolRegistryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);

        _service = new SymbolRegistryService(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesNewRegistry_WhenNoExistingRegistry()
    {
        // Act
        await _service.InitializeAsync();

        // Assert
        var registry = _service.GetRegistry();
        registry.Should().NotBeNull();
        registry.RegistryVersion.Should().Be("1.0.0");

        // Should have default symbols
        registry.Symbols.Should().NotBeEmpty();
        registry.Symbols.Should().ContainKey("AAPL");
        registry.Symbols.Should().ContainKey("SPY");

        // Verify symbols.json was created
        var symbolsPath = Path.Combine(_testDirectory, "_catalog", "symbols.json");
        File.Exists(symbolsPath).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingRegistry()
    {
        // Arrange
        var catalogDir = Path.Combine(_testDirectory, "_catalog");
        Directory.CreateDirectory(catalogDir);

        var existingRegistry = new SymbolRegistry
        {
            RegistryVersion = "1.0.0",
            Symbols = new Dictionary<string, SymbolRegistryEntry>
            {
                ["CUSTOM"] = new SymbolRegistryEntry
                {
                    Canonical = "CUSTOM",
                    DisplayName = "Custom Symbol",
                    AssetClass = "equity"
                }
            }
        };

        await File.WriteAllTextAsync(
            Path.Combine(catalogDir, "symbols.json"),
            JsonSerializer.Serialize(existingRegistry));

        // Act
        await _service.InitializeAsync();

        // Assert
        var registry = _service.GetRegistry();
        registry.Symbols.Should().ContainKey("CUSTOM");
        registry.Symbols["CUSTOM"].DisplayName.Should().Be("Custom Symbol");
    }

    [Fact]
    public async Task RegisterSymbolAsync_AddsNewSymbol()
    {
        // Arrange
        await _service.InitializeAsync();

        var entry = new SymbolRegistryEntry
        {
            Canonical = "TSLA",
            DisplayName = "Tesla Inc.",
            AssetClass = "equity",
            Exchange = "NASDAQ",
            Identifiers = new SymbolIdentifiers { Isin = "US88160R1014" }
        };

        // Act
        await _service.RegisterSymbolAsync(entry);

        // Assert
        var registry = _service.GetRegistry();
        registry.Symbols.Should().ContainKey("TSLA");
        registry.Symbols["TSLA"].DisplayName.Should().Be("Tesla Inc.");
    }

    [Fact]
    public async Task LookupSymbol_FindsByCanonical()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var result = _service.LookupSymbol("AAPL");

        // Assert
        result.Found.Should().BeTrue();
        result.MatchType.Should().Be("canonical");
        result.CanonicalSymbol.Should().Be("AAPL");
        result.Entry.Should().NotBeNull();
        result.Entry!.DisplayName.Should().Be("Apple Inc.");
    }

    [Fact]
    public async Task LookupSymbol_FindsByAlias()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "BRK.B",
            DisplayName = "Berkshire Hathaway Inc. Class B",
            AssetClass = "equity",
            Aliases = new List<SymbolAlias>
            {
                new() { Alias = "BRK-B", Source = "yahoo", Type = "ticker" },
                new() { Alias = "BRKB", Source = "alpaca", Type = "ticker" }
            }
        });

        // Act
        var result1 = _service.LookupSymbol("BRK-B");
        var result2 = _service.LookupSymbol("BRKB");

        // Assert
        result1.Found.Should().BeTrue();
        result1.MatchType.Should().Be("alias");
        result1.CanonicalSymbol.Should().Be("BRK.B");

        result2.Found.Should().BeTrue();
        result2.CanonicalSymbol.Should().Be("BRK.B");
    }

    [Fact]
    public async Task LookupSymbol_FindsByIdentifier()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act - lookup by ISIN (Apple's ISIN is set in default symbols)
        var result = _service.LookupSymbol("US0378331005");

        // Assert
        result.Found.Should().BeTrue();
        result.MatchType.Should().Be("identifier");
        result.CanonicalSymbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task LookupSymbol_ReturnsSuggestions_WhenNotFound()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var result = _service.LookupSymbol("AAPX");

        // Assert
        result.Found.Should().BeFalse();
        result.Suggestions.Should().NotBeNull();
        result.Suggestions.Should().Contain("AAPL");
    }

    [Fact]
    public async Task LookupSymbol_IsCaseInsensitive()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var resultUpper = _service.LookupSymbol("AAPL");
        var resultLower = _service.LookupSymbol("aapl");
        var resultMixed = _service.LookupSymbol("AaPl");

        // Assert
        resultUpper.Found.Should().BeTrue();
        resultLower.Found.Should().BeTrue();
        resultMixed.Found.Should().BeTrue();

        resultUpper.CanonicalSymbol.Should().Be(resultLower.CanonicalSymbol);
        resultLower.CanonicalSymbol.Should().Be(resultMixed.CanonicalSymbol);
    }

    [Fact]
    public async Task ResolveAlias_ReturnsCanonical_ForAlias()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "META",
            DisplayName = "Meta Platforms Inc.",
            AssetClass = "equity",
            Aliases = new List<SymbolAlias>
            {
                new() { Alias = "FB", Source = "historical", Type = "ticker", IsActive = false }
            }
        });

        // Act
        var resolved = _service.ResolveAlias("FB");

        // Assert
        resolved.Should().Be("META");
    }

    [Fact]
    public async Task ResolveAlias_ReturnsOriginal_WhenNotFound()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var resolved = _service.ResolveAlias("UNKNOWN");

        // Assert
        resolved.Should().Be("UNKNOWN");
    }

    [Fact]
    public async Task GetProviderSymbol_ReturnsProviderSpecificSymbol()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "BRK.B",
            DisplayName = "Berkshire Hathaway",
            AssetClass = "equity",
            ProviderSymbols = new Dictionary<string, string>
            {
                ["alpaca"] = "BRKB",
                ["polygon"] = "BRK.B",
                ["yahoo"] = "BRK-B"
            }
        });

        // Act
        var alpacaSymbol = _service.GetProviderSymbol("BRK.B", "alpaca");
        var yahooSymbol = _service.GetProviderSymbol("BRK.B", "yahoo");

        // Assert
        alpacaSymbol.Should().Be("BRKB");
        yahooSymbol.Should().Be("BRK-B");
    }

    [Fact]
    public async Task AddAliasAsync_AddsNewAlias()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        await _service.AddAliasAsync("AAPL", new SymbolAlias
        {
            Alias = "APPLE",
            Source = "custom",
            Type = "name"
        });

        // Assert
        var result = _service.LookupSymbol("APPLE");
        result.Found.Should().BeTrue();
        result.CanonicalSymbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task AddProviderMappingAsync_AddsMappingForProvider()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        await _service.AddProviderMappingAsync("AAPL", "custom_provider", "APPL.US");

        // Assert
        var providerSymbol = _service.GetProviderSymbol("AAPL", "custom_provider");
        providerSymbol.Should().Be("APPL.US");
    }

    [Fact]
    public async Task GetAllSymbols_ReturnsAllSymbolsSorted()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var symbols = _service.GetAllSymbols().ToList();

        // Assert
        symbols.Should().NotBeEmpty();
        symbols.Select(s => s.Canonical).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetSymbolsByAssetClass_ReturnsCorrectSymbols()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var equities = _service.GetSymbolsByAssetClass("equity").ToList();
        var etfs = _service.GetSymbolsByAssetClass("etf").ToList();

        // Assert
        equities.Should().NotBeEmpty();
        etfs.Should().NotBeEmpty();

        equities.Should().Contain(s => s.Canonical == "AAPL");
        etfs.Should().Contain(s => s.Canonical == "SPY");
    }

    [Fact]
    public async Task ImportSymbolsAsync_MergesWithExistingSymbols()
    {
        // Arrange
        await _service.InitializeAsync();

        var newSymbols = new[]
        {
            new SymbolRegistryEntry
            {
                Canonical = "AAPL",
                DisplayName = "Apple Inc. (Updated)",
                AssetClass = "equity",
                Aliases = new List<SymbolAlias>
                {
                    new() { Alias = "APPLE_MERGED", Source = "import" }
                }
            },
            new SymbolRegistryEntry
            {
                Canonical = "NEWSTOCK",
                DisplayName = "New Stock Inc.",
                AssetClass = "equity"
            }
        };

        // Act
        var imported = await _service.ImportSymbolsAsync(newSymbols, merge: true);

        // Assert
        imported.Should().Be(2);

        var registry = _service.GetRegistry();
        registry.Symbols.Should().ContainKey("NEWSTOCK");

        // AAPL should have merged aliases
        var aaplLookup = _service.LookupSymbol("APPLE_MERGED");
        aaplLookup.Found.Should().BeTrue();
        aaplLookup.CanonicalSymbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task Statistics_UpdatedCorrectly()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var registry = _service.GetRegistry();
        var stats = registry.Statistics;

        // Assert
        stats.TotalSymbols.Should().BeGreaterThan(0);
        stats.ActiveSymbols.Should().BeGreaterThan(0);
        stats.ByAssetClass.Should().ContainKey("equity");
        stats.ByExchange.Should().ContainKey("NASDAQ");
    }

    [Fact]
    public async Task SaveRegistryAsync_PersistsRegistryToDisk()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.RegisterSymbolAsync(new SymbolRegistryEntry
        {
            Canonical = "TEST",
            DisplayName = "Test Symbol",
            AssetClass = "equity"
        });

        // Act
        await _service.SaveRegistryAsync();

        // Assert
        var symbolsPath = Path.Combine(_testDirectory, "_catalog", "symbols.json");
        var json = await File.ReadAllTextAsync(symbolsPath);
        var savedRegistry = JsonSerializer.Deserialize<SymbolRegistry>(json);

        savedRegistry.Should().NotBeNull();
        savedRegistry!.Symbols.Should().ContainKey("TEST");
    }
}
