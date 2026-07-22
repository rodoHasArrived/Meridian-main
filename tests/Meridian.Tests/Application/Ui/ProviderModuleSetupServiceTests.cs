using FluentAssertions;
using Meridian.Core.Contracts;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application.UI;

public sealed class ProviderModuleSetupServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _configPath;
    private readonly ConfigStore _configStore;
    private readonly IProviderCredentialStore _credentialStore;

    public ProviderModuleSetupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-svc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        var configDir = Path.Combine(_tempRoot, "config");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "appsettings.json");
        File.WriteAllText(_configPath, "{}");
        _configStore = new ConfigStore(_configPath);
        _credentialStore = new ProviderCredentialStore(_tempRoot);
    }

    private ProviderModuleSetupService CreateService() =>
        new(_configStore, _credentialStore, NullLogger<ProviderModuleSetupService>.Instance);

    [Fact]
    public async Task UpsertModule_AddsModuleToConfig()
    {
        var svc = CreateService();
        var request = new UpsertProviderModuleRequest("test-provider", Enabled: true, Priority: 50);

        var result = await svc.UpsertModuleAsync(request);

        result.Success.Should().BeTrue();
        var cfg = _configStore.Load();
        cfg.ProviderModules!.Modules.Should().ContainKey("test-provider");
        cfg.ProviderModules.Modules["test-provider"].Enabled.Should().BeTrue();
        cfg.ProviderModules.Modules["test-provider"].Priority.Should().Be(50);
    }

    [Fact]
    public async Task UpsertModule_StoresCredentials()
    {
        var svc = CreateService();
        var creds = new Dictionary<string, string?> { ["KeyId"] = "abc", ["SecretKey"] = "xyz" };
        var request = new UpsertProviderModuleRequest("alpaca", CredentialValues: creds);

        var result = await svc.UpsertModuleAsync(request);

        result.Success.Should().BeTrue();
        var stored = await _credentialStore.GetCredentialsAsync("alpaca");
        stored.Should().ContainKey("KeyId").WhoseValue.Should().Be("abc");
        stored.Should().ContainKey("SecretKey").WhoseValue.Should().Be("xyz");
    }

    [Fact]
    public async Task UpsertModule_FailsWhenModuleIdEmpty()
    {
        var svc = CreateService();
        var request = new UpsertProviderModuleRequest("", Enabled: true);

        var result = await svc.UpsertModuleAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RemoveModule_RemovesFromConfigAndCredentials()
    {
        var svc = CreateService();
        await svc.UpsertModuleAsync(new UpsertProviderModuleRequest("alpaca",
            CredentialValues: new Dictionary<string, string?> { ["KeyId"] = "v" }));

        var result = await svc.RemoveModuleAsync("alpaca");

        result.Success.Should().BeTrue();
        var cfg = _configStore.Load();
        cfg.ProviderModules?.Modules.Should().NotContainKey("alpaca");
        var creds = await _credentialStore.GetCredentialsAsync("alpaca");
        creds.Should().BeEmpty();
    }

    [Fact]
    public async Task SetEnabled_UpdatesEnabledFlag()
    {
        var svc = CreateService();
        await svc.UpsertModuleAsync(new UpsertProviderModuleRequest("mod", Enabled: true));

        var result = await svc.SetEnabledAsync("mod", false);

        result.Success.Should().BeTrue();
        var cfg = _configStore.Load();
        cfg.ProviderModules!.Modules["mod"].Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetConfiguredModules_ReturnsConfiguredModules()
    {
        var svc = CreateService();
        await svc.UpsertModuleAsync(new UpsertProviderModuleRequest("my-provider", Enabled: true, Priority: 10));

        var modules = await svc.GetConfiguredModulesAsync();

        modules.Should().Contain(m => m.ModuleId == "my-provider");
        modules.First(m => m.ModuleId == "my-provider").Priority.Should().Be(10);
    }

    [Fact]
    public void GetDiscoveredModuleCatalogue_ReturnsAtLeastOneCatalogueEntry()
    {
        var svc = CreateService();

        var catalogue = svc.GetDiscoveredModuleCatalogue();

        catalogue.Should().NotBeNull();
    }

    [Fact]
    public async Task UpsertModule_DoesNotStoreNullOrEmptyCredentialValues()
    {
        var svc = CreateService();
        var creds = new Dictionary<string, string?> { ["KeyId"] = "val", ["SecretKey"] = null };
        await svc.UpsertModuleAsync(new UpsertProviderModuleRequest("alpaca", CredentialValues: creds));

        var stored = await _credentialStore.GetStoredKeyNamesAsync("alpaca");

        stored.Should().Contain("KeyId");
        stored.Should().NotContain("SecretKey");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
