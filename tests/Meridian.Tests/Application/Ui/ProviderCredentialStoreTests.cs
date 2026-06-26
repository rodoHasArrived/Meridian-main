using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Application.UI;

public sealed class ProviderCredentialStoreTests : IDisposable
{
    private readonly string _dataRoot;

    public ProviderCredentialStoreTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
    }

    [Fact]
    public async Task SaveAndGet_RoundTripsCredentials()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        var creds = new Dictionary<string, string> { ["keyId"] = "abc", ["secretKey"] = "xyz" };

        await store.SaveCredentialsAsync("alpaca", creds);
        var result = await store.GetCredentialsAsync("alpaca");

        result.Should().ContainKey("keyId").WhoseValue.Should().Be("abc");
        result.Should().ContainKey("secretKey").WhoseValue.Should().Be("xyz");
    }

    [Fact]
    public async Task GetCredentials_ReturnsEmpty_WhenNoFileExists()
    {
        var store = new ProviderCredentialStore(_dataRoot);

        var result = await store.GetCredentialsAsync("unknown-module");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveCredentials_IsolatesModules()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        await store.SaveCredentialsAsync("mod-a", new Dictionary<string, string> { ["key"] = "val-a" });
        await store.SaveCredentialsAsync("mod-b", new Dictionary<string, string> { ["key"] = "val-b" });

        var a = await store.GetCredentialsAsync("mod-a");
        var b = await store.GetCredentialsAsync("mod-b");

        a["key"].Should().Be("val-a");
        b["key"].Should().Be("val-b");
    }

    [Fact]
    public async Task GetStoredKeyNames_ReturnsNonEmptyKeys()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        await store.SaveCredentialsAsync("mod", new Dictionary<string, string>
        {
            ["keyId"] = "value",
            ["secretKey"] = ""
        });

        var keys = await store.GetStoredKeyNamesAsync("mod");

        keys.Should().Contain("keyId");
        keys.Should().NotContain("secretKey");
    }

    [Fact]
    public async Task DeleteCredentials_RemovesModuleEntry()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        await store.SaveCredentialsAsync("mod", new Dictionary<string, string> { ["k"] = "v" });
        await store.DeleteCredentialsAsync("mod");

        var result = await store.GetCredentialsAsync("mod");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveCredentials_MergesWithExisting()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        await store.SaveCredentialsAsync("mod", new Dictionary<string, string> { ["k1"] = "v1" });
        await store.SaveCredentialsAsync("mod", new Dictionary<string, string> { ["k2"] = "v2" });

        var result = await store.GetCredentialsAsync("mod");

        result.Should().ContainKey("k2");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
