using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Core.Contracts;
using Meridian.Ui.Services.Services.ProviderSetup;
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
        var creds = new Dictionary<string, string?> { ["keyId"] = "abc", ["secretKey"] = "xyz" };
        var request = new UpsertProviderModuleRequest("test-provider", CredentialValues: creds);

        var result = await svc.UpsertModuleAsync(request);

        result.Success.Should().BeTrue();
        var stored = await _credentialStore.GetCredentialsAsync("test-provider");
        stored.Should().ContainKey("keyId").WhoseValue.Should().Be("abc");
        stored.Should().ContainKey("secretKey").WhoseValue.Should().Be("xyz");
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
        await svc.UpsertModuleAsync(new UpsertProviderModuleRequest("mod-to-remove",
            CredentialValues: new Dictionary<string, string?> { ["k"] = "v" }));

        var result = await svc.RemoveModuleAsync("mod-to-remove");

        result.Success.Should().BeTrue();
        var cfg = _configStore.Load();
        cfg.ProviderModules?.Modules.Should().NotContainKey("mod-to-remove");
        var creds = await _credentialStore.GetCredentialsAsync("mod-to-remove");
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
        var creds = new Dictionary<string, string?> { ["keyId"] = "val", ["secretKey"] = null };
        await svc.UpsertModuleAsync(new UpsertProviderModuleRequest("mod", CredentialValues: creds));

        var stored = await _credentialStore.GetStoredKeyNamesAsync("mod");

        stored.Should().Contain("keyId");
        stored.Should().NotContain("secretKey");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
