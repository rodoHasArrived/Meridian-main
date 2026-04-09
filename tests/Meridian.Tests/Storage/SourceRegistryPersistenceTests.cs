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

        using var watcher = new FileSystemWatcher(_tempRoot, Path.GetFileName(persistencePath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        var saveObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        FileSystemEventHandler onChange = (_, args) =>
        {
            if (string.Equals(args.FullPath, persistencePath, StringComparison.OrdinalIgnoreCase))
            {
                saveObserved.TrySetResult(true);
            }
        };

        watcher.Created += onChange;
        watcher.Changed += onChange;

        try
        {
            var registry = new SourceRegistry(persistencePath);
            registry.RegisterSymbol(new SymbolInfo(
                Symbol: "MSFT",
                Canonical: "MSFT",
                Aliases: ["MSFT.OQ"],
                AssetClass: "equity",
                Exchange: "XNAS",
                Currency: "USD"));

            await saveObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var reloaded = new SourceRegistry(persistencePath);
            var symbol = reloaded.GetSymbolInfo("MSFT");

            symbol.Should().NotBeNull();
            symbol!.Exchange.Should().Be("XNAS");
            reloaded.ResolveSymbolAlias("MSFT.OQ").Should().Be("MSFT");
        }
        finally
        {
            watcher.Created -= onChange;
            watcher.Changed -= onChange;
        }
    }

    [Fact]
    public async Task RegisterSource_WithPersistencePath_PersistsRoundTrip()
    {
        var persistencePath = Path.Combine(_tempRoot, "sources.json");
        await File.WriteAllTextAsync(persistencePath, "{\"Sources\":[],\"Symbols\":[]}");

        using var watcher = new FileSystemWatcher(_tempRoot, Path.GetFileName(persistencePath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        var saveObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        FileSystemEventHandler onChange = (_, args) =>
        {
            if (string.Equals(args.FullPath, persistencePath, StringComparison.OrdinalIgnoreCase))
            {
                saveObserved.TrySetResult(true);
            }
        };

        watcher.Created += onChange;
        watcher.Changed += onChange;

        try
        {
            var registry = new SourceRegistry(persistencePath);
            registry.RegisterSource(new SourceInfo(
                Id: "test-feed",
                Name: "Test Feed",
                Type: SourceType.Live,
                Priority: 7,
                Enabled: true));

            await saveObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var persisted = await File.ReadAllTextAsync(persistencePath);
            using var document = JsonDocument.Parse(persisted);
            document.RootElement.GetProperty("Sources").EnumerateArray()
                .Any(entry => entry.GetProperty("Id").GetString() == "test-feed")
                .Should().BeTrue();

            var reloaded = new SourceRegistry(persistencePath);
            reloaded.GetSourceInfo("test-feed").Should().NotBeNull();
            reloaded.GetSourcePriorityOrder().Should().Contain("test-feed");
        }
        finally
        {
            watcher.Created -= onChange;
            watcher.Changed -= onChange;
        }
    }
}
