using FluentAssertions;
using Meridian.Application.Services;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for the CanonicalSymbolRegistry service.
/// </summary>
public sealed class CanonicalSymbolRegistryTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly SymbolRegistryService _registryService;
    private readonly CanonicalSymbolRegistry _canonicalRegistry;

    public CanonicalSymbolRegistryTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "CanonicalSymbolRegistryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);

        _registryService = new SymbolRegistryService(_testDirectory);
        _registryService.InitializeAsync().GetAwaiter().GetResult();
        _canonicalRegistry = new CanonicalSymbolRegistry(_registryService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Count_ReturnsNumberOfRegisteredSymbols()
    {
        _canonicalRegistry.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ResolveToCanonical_ByCanonicalName_ReturnsItself()
    {
        var result = _canonicalRegistry.ResolveToCanonical("AAPL");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_ByAlias_ReturnsCanonical()
    {
        var result = _canonicalRegistry.ResolveToCanonical("AAPL.US");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_ByExchangeSuffix_ReturnsCanonical()
    {
        var result = _canonicalRegistry.ResolveToCanonical("AAPL.O");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_ByIsin_ReturnsCanonical()
    {
        var result = _canonicalRegistry.ResolveToCanonical("US0378331005");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_ByFigi_ReturnsCanonical()
    {
        var result = _canonicalRegistry.ResolveToCanonical("BBG000B9XRY4");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_BySedol_ReturnsCanonical()
    {
        var result = _canonicalRegistry.ResolveToCanonical("2046251");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_ByCusip_ReturnsCanonical()
    {
        var result = _canonicalRegistry.ResolveToCanonical("037833100");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_CaseInsensitive_ReturnsCanonical()
    {
        var result = _canonicalRegistry.ResolveToCanonical("aapl");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void ResolveToCanonical_Unknown_ReturnsNull()
    {
        var result = _canonicalRegistry.ResolveToCanonical("UNKNOWN_SYMBOL_XYZ");

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveToCanonical_NullOrEmpty_ReturnsNull()
    {
        _canonicalRegistry.ResolveToCanonical(null!).Should().BeNull();
        _canonicalRegistry.ResolveToCanonical("").Should().BeNull();
        _canonicalRegistry.ResolveToCanonical("  ").Should().BeNull();
    }

    [Fact]
    public void ResolveToCanonical_HistoricalAlias_ReturnsCanonical()
    {
        // META has "FB" as a historical alias
        var result = _canonicalRegistry.ResolveToCanonical("FB");

        result.Should().Be("META");
    }

    [Fact]
    public async Task RegisterAsync_AddsNewSymbol()
    {
        var definition = new CanonicalSymbolDefinition
        {
            Canonical = "NFLX",
            DisplayName = "Netflix Inc.",
            Aliases = ["NFLX.US", "NFLX.O"],
            AssetClass = "equity",
            Exchange = "NASDAQ",
            Currency = "USD",
            Isin = "US64110L1061",
            Figi = "BBG000CL9VN6",
            Sedol = "2857817",
            Cusip = "64110L106",
            Country = "US"
        };

        await _canonicalRegistry.RegisterAsync(definition);

        // Should resolve by all identifiers
        _canonicalRegistry.ResolveToCanonical("NFLX").Should().Be("NFLX");
        _canonicalRegistry.ResolveToCanonical("NFLX.US").Should().Be("NFLX");
        _canonicalRegistry.ResolveToCanonical("US64110L1061").Should().Be("NFLX");
        _canonicalRegistry.ResolveToCanonical("BBG000CL9VN6").Should().Be("NFLX");
        _canonicalRegistry.ResolveToCanonical("2857817").Should().Be("NFLX");
        _canonicalRegistry.ResolveToCanonical("64110L106").Should().Be("NFLX");
    }

    [Fact]
    public async Task RegisterAsync_ThrowsOnNullDefinition()
    {
        var act = () => _canonicalRegistry.RegisterAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterAsync_ThrowsOnEmptyCanonical()
    {
        var definition = new CanonicalSymbolDefinition
        {
            Canonical = "",
            DisplayName = "Bad Symbol"
        };

        var act = () => _canonicalRegistry.RegisterAsync(definition);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RegisterBatchAsync_RegistersMultipleSymbols()
    {
        var definitions = new[]
        {
            new CanonicalSymbolDefinition
            {
                Canonical = "JPM",
                DisplayName = "JPMorgan Chase & Co.",
                Aliases = ["JPM.US", "JPM.N"],
                AssetClass = "equity",
                Exchange = "NYSE",
                Currency = "USD",
                Isin = "US46625H1005",
                Figi = "BBG000DMBXR2"
            },
            new CanonicalSymbolDefinition
            {
                Canonical = "V",
                DisplayName = "Visa Inc.",
                Aliases = ["V.US", "V.N"],
                AssetClass = "equity",
                Exchange = "NYSE",
                Currency = "USD",
                Isin = "US92826C8394",
                Figi = "BBG000PQKVN8"
            }
        };

        var count = await _canonicalRegistry.RegisterBatchAsync(definitions);

        count.Should().Be(2);
        _canonicalRegistry.ResolveToCanonical("JPM").Should().Be("JPM");
        _canonicalRegistry.ResolveToCanonical("V.US").Should().Be("V");
        _canonicalRegistry.ResolveToCanonical("US92826C8394").Should().Be("V");
    }

    [Fact]
    public void GetDefinition_ByCanonical_ReturnsFullDefinition()
    {
        var definition = _canonicalRegistry.GetDefinition("AAPL");

        definition.Should().NotBeNull();
        definition!.Canonical.Should().Be("AAPL");
        definition.DisplayName.Should().Be("Apple Inc.");
        definition.AssetClass.Should().Be("equity");
        definition.Exchange.Should().Be("NASDAQ");
        definition.Currency.Should().Be("USD");
        definition.Isin.Should().Be("US0378331005");
        definition.Figi.Should().Be("BBG000B9XRY4");
        definition.Sedol.Should().Be("2046251");
        definition.Cusip.Should().Be("037833100");
        definition.Aliases.Should().Contain("AAPL.US");
        definition.Aliases.Should().Contain("AAPL.O");
    }

    [Fact]
    public void GetDefinition_ByIsin_ReturnsFullDefinition()
    {
        var definition = _canonicalRegistry.GetDefinition("US0378331005");

        definition.Should().NotBeNull();
        definition!.Canonical.Should().Be("AAPL");
    }

    [Fact]
    public void GetDefinition_ByFigi_ReturnsFullDefinition()
    {
        var definition = _canonicalRegistry.GetDefinition("BBG000B9XRY4");

        definition.Should().NotBeNull();
        definition!.Canonical.Should().Be("AAPL");
    }

    [Fact]
    public void GetDefinition_Unknown_ReturnsNull()
    {
        var definition = _canonicalRegistry.GetDefinition("UNKNOWN_XYZ");

        definition.Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllDefinitions()
    {
        var all = _canonicalRegistry.GetAll();

        all.Should().NotBeEmpty();
        all.Select(d => d.Canonical).Should().Contain("AAPL");
        all.Select(d => d.Canonical).Should().Contain("MSFT");
        all.Select(d => d.Canonical).Should().Contain("SPY");
    }

    [Fact]
    public void GetByAssetClass_ReturnsFilteredDefinitions()
    {
        var equities = _canonicalRegistry.GetByAssetClass("equity");
        var etfs = _canonicalRegistry.GetByAssetClass("etf");

        equities.Should().NotBeEmpty();
        equities.Should().AllSatisfy(d => d.AssetClass.Should().Be("equity"));
        equities.Select(d => d.Canonical).Should().Contain("AAPL");

        etfs.Should().NotBeEmpty();
        etfs.Should().AllSatisfy(d => d.AssetClass.Should().Be("etf"));
        etfs.Select(d => d.Canonical).Should().Contain("SPY");
    }

    [Fact]
    public void GetByExchange_ReturnsFilteredDefinitions()
    {
        var nasdaq = _canonicalRegistry.GetByExchange("NASDAQ");
        var nyse = _canonicalRegistry.GetByExchange("NYSE");

        nasdaq.Should().NotBeEmpty();
        nasdaq.Should().AllSatisfy(d => d.Exchange.Should().Be("NASDAQ"));

        nyse.Should().NotBeEmpty();
        nyse.Should().AllSatisfy(d => d.Exchange.Should().Be("NYSE"));
    }

    [Fact]
    public void GetByExchange_EmptyString_ReturnsEmpty()
    {
        var result = _canonicalRegistry.GetByExchange("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void IsKnown_ReturnsTrueForRegisteredIdentifiers()
    {
        _canonicalRegistry.IsKnown("AAPL").Should().BeTrue();
        _canonicalRegistry.IsKnown("AAPL.US").Should().BeTrue();
        _canonicalRegistry.IsKnown("US0378331005").Should().BeTrue();
        _canonicalRegistry.IsKnown("BBG000B9XRY4").Should().BeTrue();
        _canonicalRegistry.IsKnown("2046251").Should().BeTrue();
        _canonicalRegistry.IsKnown("037833100").Should().BeTrue();
    }

    [Fact]
    public void IsKnown_ReturnsFalseForUnknown()
    {
        _canonicalRegistry.IsKnown("UNKNOWN_SYMBOL").Should().BeFalse();
        _canonicalRegistry.IsKnown("").Should().BeFalse();
        _canonicalRegistry.IsKnown(null!).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_RemovesSymbolAndAllIndexes()
    {
        // First register a symbol
        await _canonicalRegistry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "REMOVE_ME",
            DisplayName = "To Be Removed",
            Aliases = ["RMME.US"],
            AssetClass = "equity",
            Exchange = "NYSE",
            Currency = "USD",
            Isin = "US9999999999"
        });

        _canonicalRegistry.IsKnown("REMOVE_ME").Should().BeTrue();
        _canonicalRegistry.IsKnown("RMME.US").Should().BeTrue();
        _canonicalRegistry.IsKnown("US9999999999").Should().BeTrue();

        // Remove it
        var removed = await _canonicalRegistry.RemoveAsync("REMOVE_ME");

        removed.Should().BeTrue();
        _canonicalRegistry.IsKnown("REMOVE_ME").Should().BeFalse();
        _canonicalRegistry.IsKnown("RMME.US").Should().BeFalse();
        _canonicalRegistry.IsKnown("US9999999999").Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_NonExistent_ReturnsFalse()
    {
        var removed = await _canonicalRegistry.RemoveAsync("NONEXISTENT");

        removed.Should().BeFalse();
    }

    [Fact]
    public void DefaultSymbols_HaveFullIdentifiers()
    {
        // Verify that default symbols are populated with FIGI, SEDOL, CUSIP
        var aapl = _canonicalRegistry.GetDefinition("AAPL");
        aapl.Should().NotBeNull();
        aapl!.Isin.Should().NotBeNullOrEmpty();
        aapl.Figi.Should().NotBeNullOrEmpty();
        aapl.Sedol.Should().NotBeNullOrEmpty();
        aapl.Cusip.Should().NotBeNullOrEmpty();

        var msft = _canonicalRegistry.GetDefinition("MSFT");
        msft.Should().NotBeNull();
        msft!.Isin.Should().NotBeNullOrEmpty();
        msft.Figi.Should().NotBeNullOrEmpty();
        msft.Sedol.Should().NotBeNullOrEmpty();
        msft.Cusip.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DefaultSymbols_HaveStandardAliases()
    {
        var aapl = _canonicalRegistry.GetDefinition("AAPL");
        aapl.Should().NotBeNull();
        aapl!.Aliases.Should().Contain("AAPL.US");
        aapl.Aliases.Should().Contain("AAPL.O");

        var spy = _canonicalRegistry.GetDefinition("SPY");
        spy.Should().NotBeNull();
        spy!.Aliases.Should().Contain("SPY.US");
    }

    [Fact]
    public void CrossIdentifierResolution_WorksAcrossAllTypes()
    {
        // All of these should resolve to MSFT
        var identifiers = new[]
        {
            "MSFT",           // canonical
            "MSFT.US",        // alias (Stooq-style)
            "MSFT.O",         // alias (Reuters-style)
            "US5949181045",   // ISIN
            "BBG000BPH459",   // FIGI
            "2588173",        // SEDOL
            "594918104"       // CUSIP
        };

        foreach (var identifier in identifiers)
        {
            var resolved = _canonicalRegistry.ResolveToCanonical(identifier);
            resolved.Should().Be("MSFT", because: $"'{identifier}' should resolve to MSFT");
        }
    }

    [Fact]
    public void AllDefaultSymbols_ResolvableByIsin()
    {
        var all = _canonicalRegistry.GetAll();

        foreach (var definition in all)
        {
            if (definition.Isin is not null)
            {
                var resolved = _canonicalRegistry.ResolveToCanonical(definition.Isin);
                resolved.Should().Be(definition.Canonical,
                    because: $"ISIN '{definition.Isin}' should resolve to {definition.Canonical}");
            }
        }
    }

    [Fact]
    public void AllDefaultSymbols_ResolvableByFigi()
    {
        var all = _canonicalRegistry.GetAll();

        foreach (var definition in all)
        {
            if (definition.Figi is not null)
            {
                var resolved = _canonicalRegistry.ResolveToCanonical(definition.Figi);
                resolved.Should().Be(definition.Canonical,
                    because: $"FIGI '{definition.Figi}' should resolve to {definition.Canonical}");
            }
        }
    }

    // --- TryResolve (provider-aware) ---

    [Fact]
    public void TryResolve_FallsBackToGenericResolution()
    {
        // Without provider-specific mappings, TryResolve should still work via generic resolve
        var result = _canonicalRegistry.TryResolve("AAPL", "ALPACA");

        result.Should().Be("AAPL");
    }

    [Fact]
    public void TryResolve_NullSymbol_ReturnsNull()
    {
        _canonicalRegistry.TryResolve(null!, "ALPACA").Should().BeNull();
    }

    [Fact]
    public void TryResolve_EmptySymbol_ReturnsNull()
    {
        _canonicalRegistry.TryResolve("", "ALPACA").Should().BeNull();
    }

    [Fact]
    public void TryResolve_NullProvider_FallsBackToGeneric()
    {
        var result = _canonicalRegistry.TryResolve("AAPL", null!);

        result.Should().Be("AAPL");
    }

    [Fact]
    public void TryResolve_ByAlias_FallsBackToGenericResolution()
    {
        var result = _canonicalRegistry.TryResolve("AAPL.US", "POLYGON");

        result.Should().Be("AAPL");
    }
}
