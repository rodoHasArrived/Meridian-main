using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Core.Subscriptions.Models;

namespace Meridian.Tests.Application.Subscriptions;

/// <summary>
/// Regression coverage for corrupt-store handling in the subscription services.
/// Every store in this area follows a load-mutate-save pattern, so a swallowed load
/// failure must quarantine the unreadable file instead of silently falling back to an
/// empty set that the next save would atomically write over the user's data.
/// </summary>
public sealed class SubscriptionStoreQuarantineTests : IDisposable
{
    private const string CorruptJson = "{ this is not valid json";

    private readonly string _tempRoot;
    private readonly ConfigStore _configStore;

    public SubscriptionStoreQuarantineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-quarantine-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        File.WriteAllText(configPath, "{}");
        _configStore = new ConfigStore(configPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of temp files.
        }
    }

    private string[] QuarantineFiles(string storePath) =>
        Directory.GetFiles(_tempRoot, $"{Path.GetFileName(storePath)}.corrupt-*");

    [Fact]
    public async Task WatchlistService_MissingFile_ReturnsEmptyWithoutQuarantine()
    {
        var path = Path.Combine(_tempRoot, "watchlists.json");
        var service = new WatchlistService(_configStore, path);

        var watchlists = await service.GetAllWatchlistsAsync();

        watchlists.Should().BeEmpty();
        QuarantineFiles(path).Should().BeEmpty();
    }

    [Fact]
    public async Task WatchlistService_CorruptFile_ReturnsEmptyAndQuarantinesOriginal()
    {
        var path = Path.Combine(_tempRoot, "watchlists.json");
        File.WriteAllText(path, CorruptJson);
        var service = new WatchlistService(_configStore, path);

        var watchlists = await service.GetAllWatchlistsAsync();

        watchlists.Should().BeEmpty();
        var quarantined = QuarantineFiles(path);
        quarantined.Should().ContainSingle();
        File.ReadAllText(quarantined[0]).Should().Be(CorruptJson);
        File.Exists(path).Should().BeTrue("loading must never delete the original store");
    }

    [Fact]
    public async Task WatchlistService_MutationAfterCorruptLoad_PreservesOriginalContentInQuarantine()
    {
        var path = Path.Combine(_tempRoot, "watchlists.json");
        File.WriteAllText(path, CorruptJson);
        var service = new WatchlistService(_configStore, path);

        var created = await service.CreateWatchlistAsync(new CreateWatchlistRequest("Recovered"));

        created.Should().NotBeNull();
        var quarantined = QuarantineFiles(path);
        quarantined.Should().ContainSingle("the pre-corruption content must survive the overwriting save");
        File.ReadAllText(quarantined[0]).Should().Be(CorruptJson);

        var reloaded = await service.GetAllWatchlistsAsync();
        reloaded.Should().ContainSingle(w => w.Name == "Recovered");
    }

    [Fact]
    public async Task WatchlistService_CanceledToken_PropagatesCancellationWithoutQuarantine()
    {
        var path = Path.Combine(_tempRoot, "watchlists.json");
        File.WriteAllText(path, "[]");
        var service = new WatchlistService(_configStore, path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.GetAllWatchlistsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        QuarantineFiles(path).Should().BeEmpty("cancellation is not corruption and must not quarantine the store");
    }

    [Fact]
    public async Task TemplateService_CorruptFile_KeepsBuiltInsAndQuarantinesOriginal()
    {
        var path = Path.Combine(_tempRoot, "templates.json");
        File.WriteAllText(path, CorruptJson);
        var service = new TemplateService(_configStore, path);

        var templates = await service.GetAllTemplatesAsync();

        templates.Should().NotBeEmpty("built-in templates remain available");
        templates.Should().OnlyContain(t => !t.Id.StartsWith("custom_"));
        var quarantined = QuarantineFiles(path);
        quarantined.Should().ContainSingle();
        File.ReadAllText(quarantined[0]).Should().Be(CorruptJson);
    }

    [Fact]
    public async Task SchedulingService_CorruptFile_InitializesEmptyAndQuarantinesOriginal()
    {
        var path = Path.Combine(_tempRoot, "schedules.json");
        File.WriteAllText(path, CorruptJson);
        await using var service = new SchedulingService(_configStore, path);

        await service.InitializeAsync();

        service.GetAllSchedules().Should().BeEmpty();
        var quarantined = QuarantineFiles(path);
        quarantined.Should().ContainSingle();
        File.ReadAllText(quarantined[0]).Should().Be(CorruptJson);
    }

    [Fact]
    public async Task MetadataEnrichmentService_CorruptCache_FallsBackToBuiltInsAndQuarantinesOriginal()
    {
        var path = Path.Combine(_tempRoot, "metadata_cache.json");
        File.WriteAllText(path, CorruptJson);
        var service = new MetadataEnrichmentService(path);

        var metadata = await service.GetMetadataAsync("AAPL");

        metadata.Should().NotBeNull("built-in metadata remains available after the corrupt cache is quarantined");
        var quarantined = QuarantineFiles(path);
        quarantined.Should().ContainSingle();
        File.ReadAllText(quarantined[0]).Should().Be(CorruptJson);
    }
}
