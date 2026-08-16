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

    [Fact]
    public async Task RegisterSource_WhenAtomicWriteFails_DoesNotPublishOrSmuggleFailedCandidate()
    {
        var persistencePath = Path.Combine(_tempRoot, "faulted-registry.json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await File.WriteAllTextAsync(
            persistencePath,
            "{\"Sources\":[],\"Symbols\":[]}",
            timeout.Token);
        using var writer = new AtomicSnapshotTestWriter();
        var registry = new SourceRegistry(persistencePath, writer.Write);
        registry.RegisterSource(new SourceInfo(
            Id: "baseline-feed",
            Name: "Baseline Feed",
            Type: SourceType.Live,
            Priority: 1,
            Enabled: true));
        writer.FailNextWrite();

        var failedSource = new SourceInfo(
            Id: "failed-feed",
            Name: "Failed Feed",
            Type: SourceType.Live,
            Priority: 2,
            Enabled: true);

        var act = () => registry.RegisterSource(failedSource);

        act.Should().Throw<IOException>();
        registry.GetSourceInfo("baseline-feed").Should().NotBeNull();
        registry.GetSourceInfo("failed-feed").Should().BeNull();

        registry.RegisterSource(new SourceInfo(
            Id: "committed-feed",
            Name: "Committed Feed",
            Type: SourceType.Historical,
            Priority: 3,
            Enabled: true));

        var restarted = new SourceRegistry(persistencePath);
        restarted.GetSourceInfo("baseline-feed").Should().NotBeNull();
        restarted.GetSourceInfo("failed-feed").Should().BeNull();
        restarted.GetSourceInfo("committed-feed").Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterSymbol_WhileAtomicWriteIsBlocked_PublishesSymbolAndAliasTogetherAfterCommit()
    {
        var persistencePath = Path.Combine(_tempRoot, "blocked-registry.json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await File.WriteAllTextAsync(
            persistencePath,
            "{\"Sources\":[],\"Symbols\":[]}",
            timeout.Token);
        using var writer = new AtomicSnapshotTestWriter();
        var registry = new SourceRegistry(persistencePath, writer.Write);
        var block = writer.BlockNextWrite();

        var registration = Task.Run(() => registry.RegisterSymbol(new SymbolInfo(
            Symbol: "NVDA",
            Canonical: "NVDA",
            Aliases: ["NVDA.OQ"],
            AssetClass: "equity",
            Exchange: "XNAS",
            Currency: "USD")));

        try
        {
            await block.WaitUntilEnteredAsync(timeout.Token);
            registry.GetSymbolInfo("NVDA").Should().BeNull();
            registry.GetSymbolInfo("NVDA.OQ").Should().BeNull();
            registry.ResolveSymbolAlias("NVDA.OQ").Should().Be("NVDA.OQ");
        }
        finally
        {
            block.Release();
        }

        await registration.WaitAsync(timeout.Token);
        registry.GetSymbolInfo("NVDA").Should().NotBeNull();
        registry.GetSymbolInfo("nvda.oq").Should().NotBeNull();
        registry.ResolveSymbolAlias("nvda.oq").Should().Be("NVDA");
    }

    [Fact]
    public async Task RegisterSymbol_CallerAndGetterMutation_DoesNotChangePublishedOrPersistedSnapshot()
    {
        var persistencePath = Path.Combine(_tempRoot, "defensive-registry.json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await File.WriteAllTextAsync(
            persistencePath,
            "{\"Sources\":[],\"Symbols\":[]}",
            timeout.Token);
        var aliases = new[] { "AMD.OQ" };
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["region"] = "US"
        };
        var registry = new SourceRegistry(persistencePath);

        registry.RegisterSymbol(new SymbolInfo(
            Symbol: "AMD",
            Canonical: "AMD",
            Aliases: aliases,
            AssetClass: "equity",
            Exchange: "XNAS",
            Currency: "USD",
            Metadata: metadata));

        aliases[0] = "MUTATED";
        metadata["region"] = "changed";
        var returned = registry.GetSymbolInfo("AMD")!;
        returned.Aliases![0] = "LEAKED";
        returned.Metadata!["region"] = "leaked";

        var retained = registry.GetSymbolInfo("amd")!;
        retained.Aliases.Should().Equal("AMD.OQ");
        retained.Metadata.Should().Contain("region", "US");
        registry.ResolveSymbolAlias("amd.oq").Should().Be("AMD");

        var restarted = new SourceRegistry(persistencePath);
        restarted.GetSymbolInfo("AMD")!.Aliases.Should().Equal("AMD.OQ");
        restarted.GetSymbolInfo("AMD")!.Metadata.Should().Contain("region", "US");
    }

    [Fact]
    public void RegisterSource_WithoutPersistencePath_RetainsInMemoryModeWithDefensiveCopies()
    {
        var assetClasses = new[] { "equity" };
        var registry = new SourceRegistry();

        registry.RegisterSource(new SourceInfo(
            Id: "in-memory-feed",
            Name: "In-Memory Feed",
            Type: SourceType.Live,
            AssetClasses: assetClasses));

        assetClasses[0] = "mutated";
        var returned = registry.GetSourceInfo("in-memory-feed")!;
        returned.AssetClasses![0] = "leaked";

        registry.GetSourceInfo("IN-MEMORY-FEED")!.AssetClasses.Should().Equal("equity");
    }
}
