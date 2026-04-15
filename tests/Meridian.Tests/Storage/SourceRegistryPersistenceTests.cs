using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed class SourceRegistryPersistenceTests : IDisposable
{
    private readonly string _tempRoot;

    public SourceRegistryPersistenceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "meridian_source_registry_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RegisterSymbol_WithPersistencePath_PersistsRoundTrip()
    {
        var persistencePath = Path.Combine(_tempRoot, "registry.json");
        await File.WriteAllTextAsync(persistencePath, "{\"Sources\":[],\"Symbols\":[]}");

        var registry = new SourceRegistry(persistencePath);
        registry.RegisterSymbol(new SymbolInfo(
            Symbol: "MSFT",
            Canonical: "MSFT",
            Aliases: ["MSFT.OQ"],
            AssetClass: "equity",
            Exchange: "XNAS",
            Currency: "USD"));

        var persisted = await File.ReadAllTextAsync(persistencePath);
        using var document = JsonDocument.Parse(persisted);
        document.RootElement.GetProperty("Symbols").EnumerateArray()
            .Any(entry => entry.GetProperty("Symbol").GetString() == "MSFT")
            .Should().BeTrue();

        var reloaded = new SourceRegistry(persistencePath);
        var symbol = reloaded.GetSymbolInfo("MSFT");

        symbol.Should().NotBeNull();
        symbol!.Exchange.Should().Be("XNAS");
        reloaded.ResolveSymbolAlias("MSFT.OQ").Should().Be("MSFT");
    }

    [Fact]
    public async Task RegisterSource_WithPersistencePath_PersistsRoundTrip()
    {
        var persistencePath = Path.Combine(_tempRoot, "sources.json");
        await File.WriteAllTextAsync(persistencePath, "{\"Sources\":[],\"Symbols\":[]}");

        var registry = new SourceRegistry(persistencePath);
        registry.RegisterSource(new SourceInfo(
            Id: "test-feed",
            Name: "Test Feed",
            Type: SourceType.Live,
            Priority: 7,
            Enabled: true));

        var persisted = await File.ReadAllTextAsync(persistencePath);
        using var document = JsonDocument.Parse(persisted);
        document.RootElement.GetProperty("Sources").EnumerateArray()
            .Any(entry => entry.GetProperty("Id").GetString() == "test-feed")
            .Should().BeTrue();

        var reloaded = new SourceRegistry(persistencePath);
        reloaded.GetSourceInfo("test-feed").Should().NotBeNull();
        reloaded.GetSourcePriorityOrder().Should().Contain("test-feed");
    }

    [Fact]
    public void Constructor_WhenInitialPersistenceWriteFails_KeepsDefaultsInMemory()
    {
        var persistencePath = Path.Combine(_tempRoot, "registry-directory");
        Directory.CreateDirectory(persistencePath);

        var registry = new SourceRegistry(persistencePath);

        registry.GetSourceInfo("alpaca").Should().NotBeNull();
        registry.GetSourcePriorityOrder().Should().Contain("alpaca");
    }

    [Fact]
    public async Task RegisterSource_WhenPersistenceWriteFails_Throws()
    {
        var persistencePath = Path.Combine(_tempRoot, "broken.json");
        await File.WriteAllTextAsync(persistencePath, "{\"Sources\":[],\"Symbols\":[]}");

        var registry = new SourceRegistry(persistencePath);
        File.Delete(persistencePath);
        Directory.CreateDirectory(persistencePath);

        var act = () => registry.RegisterSource(new SourceInfo(
            Id: "broken-feed",
            Name: "Broken Feed",
            Type: SourceType.Live));

        var exception = act.Should().Throw<Exception>().Which;
        (exception is IOException || exception is UnauthorizedAccessException).Should().BeTrue();
    }
}
