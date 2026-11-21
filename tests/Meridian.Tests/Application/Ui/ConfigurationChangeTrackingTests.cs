using System.Text.Json;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Tests.Application.UI;

public sealed class ConfigurationChangeTrackingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-change-impact-" + Guid.NewGuid().ToString("N"));

    private async Task<ConfigStore> CreateStore(AppConfig? config = null)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config ?? new AppConfig(), AppConfigJsonOptions.Write));
        return new ConfigStore(path);
    }

    [Fact]
    public async Task LiveSaveDoesNotHidePendingProviderChangeAcrossStoreInstances()
    {
        var store = await CreateStore();
        await store.UpdateAsync(config => config with { DataSource = DataSourceKind.IB });
        var second = new ConfigStore(store.ConfigPath);
        second.GetChangeStatus().RequiresRestart.Should().BeTrue("the current runtime cannot rebuild providers in process");
        second.GetChangeStatus().CanReconnectInProcess.Should().BeFalse();
        await second.UpdateAsync(config => config with { Symbols = [new SymbolConfig("AAPL")] });
        var status = second.GetChangeStatus();
        status.Impact.Should().Be(ConfigurationChangeImpact.Live);
        status.PendingChanges.Should().ContainSingle(change => change.Path == "dataSource");
        status.RequiresRestart.Should().BeTrue();
        status.RequiresReconnect.Should().BeFalse();
    }

    [Fact]
    public async Task RevertingConstructionTimeSettingClearsItsPendingState()
    {
        var baseline = new BackfillConfig(Providers: new BackfillProvidersConfig(Yahoo: new YahooFinanceConfig(Priority: 10)));
        var store = await CreateStore(new AppConfig(Backfill: baseline));
        await store.UpdateAsync(config => config with
        {
            Backfill = baseline with
            {
                Providers = baseline.Providers! with { Yahoo = baseline.Providers.Yahoo! with { Priority = 4 } }
            }
        });
        store.GetChangeStatus().RequiresRestart.Should().BeTrue();
        await store.UpdateAsync(config => config with { Backfill = baseline });
        store.GetChangeStatus().PendingChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task ImpactResponseContainsNoConfigurationValuesOrCredentials()
    {
        var store = await CreateStore();
        await store.UpdateAsync(config => config with { Alpaca = new AlpacaOptions("sensitive-key-123", "sensitive-secret-456") });
        var json = JsonSerializer.Serialize(store.GetChangeStatus());
        json.Should().NotContain("sensitive-key-123").And.NotContain("sensitive-secret-456");
        store.GetChangeStatus().Changes.Should().Contain(change => change.Path == "alpaca.secretKey");
    }

    [Fact]
    public async Task WholeSaveRefusesMalformedAuthoritativeFileWithoutReplacingIt()
    {
        var store = await CreateStore();
        await File.WriteAllTextAsync(store.ConfigPath, "{");
        await Assert.ThrowsAsync<JsonException>(() => store.SaveAsync(new AppConfig()));
        (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be("{");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
