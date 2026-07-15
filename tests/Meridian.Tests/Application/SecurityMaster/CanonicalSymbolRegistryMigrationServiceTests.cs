using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.UI;
using Meridian.Contracts.Catalog;
using Meridian.Core.Config;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Application.SecurityMaster;

/// <summary>
/// Guards restart-safe convergence of legacy provider aliases into the canonical symbol spine.
/// </summary>
public sealed class CanonicalSymbolRegistryMigrationServiceTests
{
    [Fact]
    public async Task StartAsync_InlineAndExternalLegacyMappings_ImportsBothAndPreservesRollbackInputs()
    {
        using var harness = await MigrationHarness.CreateAsync();
        const string externalJson = """
            {
              "mappings": [
                {
                  "canonicalSymbol": "BRK.B",
                  "displayName": "Berkshire Hathaway Class B",
                  "securityType": "STK",
                  "primaryExchange": "NYSE",
                  "currency": "USD",
                  "figi": "BBG000BH0FR6",
                  "providerSymbols": {
                    "stooq": "brk.b.us",
                    "yahoo": "BRK-B"
                  }
                }
              ]
            }
            """;
        await harness.WriteExternalMappingsAsync(externalJson);
        await harness.WriteConfigAsync(
            [new SymbolMappingConfig("AAPL", AlpacaSymbol: "AAPL", YahooSymbol: "AAPL", Name: "Apple Inc.")],
            harness.ExternalMappingsPath);
        var originalConfig = await File.ReadAllTextAsync(harness.ConfigPath);

        await harness.CreateService().StartAsync(CancellationToken.None);

        harness.ImportedDefinitions.Should().ContainSingle(definition =>
            definition.Canonical == "AAPL" &&
            definition.ProviderSymbols["alpaca"].Symbol == "AAPL");
        harness.ImportedDefinitions.Should().ContainSingle(definition =>
            definition.Canonical == "BRK.B" &&
            definition.Figi == "BBG000BH0FR6" &&
            definition.ProviderSymbols["stooq"].Symbol == "brk.b.us");
        (await File.ReadAllTextAsync(harness.ConfigPath)).Should().Be(originalConfig);
        (await File.ReadAllTextAsync(harness.ExternalMappingsPath)).Should().Be(externalJson);
    }

    [Fact]
    public async Task StartAsync_IdenticalFingerprintAfterRestart_IsANoOp()
    {
        using var harness = await MigrationHarness.CreateAsync();
        await harness.WriteConfigAsync([new SymbolMappingConfig("AAPL", YahooSymbol: "AAPL")]);

        await harness.CreateService().StartAsync(CancellationToken.None);
        await harness.CreateService().StartAsync(CancellationToken.None);

        harness.ImportedDefinitions.Should().ContainSingle();
        harness.PersistedMarkerWrites.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_ChangedLegacyFingerprint_RerunsImportAndReplacesMarker()
    {
        using var harness = await MigrationHarness.CreateAsync();
        await harness.WriteConfigAsync([new SymbolMappingConfig("AAPL", YahooSymbol: "AAPL")]);
        await harness.CreateService().StartAsync(CancellationToken.None);
        var firstMarker = harness.PersistedFingerprint;

        await harness.WriteConfigAsync([new SymbolMappingConfig("AAPL", YahooSymbol: "AAPL.NEW")]);
        await harness.CreateService().StartAsync(CancellationToken.None);

        harness.ImportedDefinitions.Should().HaveCount(2);
        harness.ImportedDefinitions[^1].ProviderSymbols["yahoo"].Symbol.Should().Be("AAPL.NEW");
        harness.PersistedMarkerWrites.Should().Be(2);
        harness.PersistedFingerprint.Should().NotBe(firstMarker);
    }

    [Fact]
    public async Task StartAsync_MalformedExternalLegacyJson_SkipsFileAndRetainsItForRepair()
    {
        using var harness = await MigrationHarness.CreateAsync();
        const string malformedJson = "{ \"mappings\": [ { \"canonicalSymbol\": \"BROKEN\" ";
        await harness.WriteExternalMappingsAsync(malformedJson);
        await harness.WriteConfigAsync(
            [new SymbolMappingConfig("MSFT", PolygonSymbol: "MSFT")],
            harness.ExternalMappingsPath);

        var act = () => harness.CreateService().StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        harness.ImportedDefinitions.Should().ContainSingle(definition => definition.Canonical == "MSFT");
        (await File.ReadAllTextAsync(harness.ExternalMappingsPath)).Should().Be(malformedJson);
        harness.PersistedMarkerWrites.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_CancelledBeforeImport_DoesNotImportOrPersistCompletionMarker()
    {
        using var harness = await MigrationHarness.CreateAsync();
        await harness.WriteConfigAsync([new SymbolMappingConfig("AAPL", YahooSymbol: "AAPL")]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => harness.CreateService().StartAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.ImportedDefinitions.Should().BeEmpty();
        harness.PersistedMarkerWrites.Should().Be(0);
    }

    private sealed class MigrationHarness : IDisposable
    {
        private const string MigrationId = "canonical-symbol-spine-v1";
        private readonly string _root;
        private readonly Mock<ICanonicalSymbolRegistry> _registry = new(MockBehavior.Strict);
        private readonly Mock<ISymbolRegistryService> _store = new(MockBehavior.Strict);

        private MigrationHarness(string root)
        {
            _root = root;
            ConfigPath = Path.Combine(root, "appsettings.json");
            ExternalMappingsPath = Path.Combine(root, "legacy-symbol-mappings.json");
            ConfigStore = new ConfigStore(ConfigPath);

            _registry
                .Setup(registry => registry.RegisterAsync(
                    It.IsAny<CanonicalSymbolDefinition>(),
                    It.IsAny<CancellationToken>()))
                .Callback<CanonicalSymbolDefinition, CancellationToken>((definition, _) =>
                    ImportedDefinitions.Add(definition))
                .Returns(Task.CompletedTask);
            _store
                .Setup(store => store.GetMigrationMarkerAsync(MigrationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => PersistedFingerprint);
            _store
                .Setup(store => store.SetMigrationMarkerAsync(
                    MigrationId,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((_, fingerprint, _) =>
                {
                    PersistedFingerprint = fingerprint;
                    PersistedMarkerWrites++;
                })
                .Returns(Task.CompletedTask);
        }

        public string ConfigPath { get; }
        public string ExternalMappingsPath { get; }
        public ConfigStore ConfigStore { get; }
        public List<CanonicalSymbolDefinition> ImportedDefinitions { get; } = [];
        public string? PersistedFingerprint { get; private set; }
        public int PersistedMarkerWrites { get; private set; }

        public static Task<MigrationHarness> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"canonical-symbol-migration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return Task.FromResult(new MigrationHarness(root));
        }

        public Task WriteConfigAsync(
            SymbolMappingConfig[] mappings,
            string? persistencePath = null)
            => ConfigStore.SaveAsync(new AppConfig(
                DataRoot: Path.Combine(_root, "data"),
                DataSources: new DataSourcesConfig(
                    SymbolMappings: new SymbolMappingsConfig(
                        PersistencePath: persistencePath,
                        Mappings: mappings))));

        public Task WriteExternalMappingsAsync(string json)
            => File.WriteAllTextAsync(ExternalMappingsPath, json);

        public CanonicalSymbolRegistryMigrationService CreateService()
            => new(
                ConfigStore,
                _registry.Object,
                _store.Object,
                NullLogger<CanonicalSymbolRegistryMigrationService>.Instance);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
