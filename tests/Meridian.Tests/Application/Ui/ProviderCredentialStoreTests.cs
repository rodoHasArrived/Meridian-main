using FluentAssertions;
using Meridian.Ui.Shared.Services;
using System.Text.Json;

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
        await store.SaveCredentialsAsync("polygon", new Dictionary<string, string> { ["apiKey"] = "val-a" });
        await store.SaveCredentialsAsync("finnhub", new Dictionary<string, string> { ["apiKey"] = "val-b" });

        var a = await store.GetCredentialsAsync("polygon");
        var b = await store.GetCredentialsAsync("finnhub");

        a["apiKey"].Should().Be("val-a");
        b["apiKey"].Should().Be("val-b");
    }

    [Fact]
    public async Task GetStoredKeyNames_ReturnsNonEmptyKeys()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        await store.SaveCredentialsAsync("alpaca", new Dictionary<string, string>
        {
            ["keyId"] = "value",
            ["secretKey"] = ""
        });

        var keys = await store.GetStoredKeyNamesAsync("alpaca");

        keys.Should().Contain("keyId");
        keys.Should().NotContain("secretKey");
    }

    [Fact]
    public async Task DeleteCredentials_RemovesModuleEntry()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        await store.SaveCredentialsAsync("polygon", new Dictionary<string, string> { ["apiKey"] = "v" });
        await store.DeleteCredentialsAsync("polygon");

        var result = await store.GetCredentialsAsync("polygon");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveCredentials_MergesWithExisting()
    {
        var store = new ProviderCredentialStore(_dataRoot);
        await store.SaveCredentialsAsync("alpaca", new Dictionary<string, string> { ["keyId"] = "v1" });
        await store.SaveCredentialsAsync("alpaca", new Dictionary<string, string> { ["secretKey"] = "v2" });

        var result = await store.GetCredentialsAsync("alpaca");

        result.Should().ContainKey("keyId");
        result.Should().ContainKey("secretKey");
    }

    [Fact]
    public async Task FirstRead_MigratesAndRemovesLegacyPlaintextSidecar()
    {
        var legacyPath = Path.Combine(_dataRoot, "provider-credentials.json");
        const string rawSecret = "legacy-secret-value";
        await File.WriteAllTextAsync(
            legacyPath,
            JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
            {
                ["alpaca"] = new()
                {
                    ["keyId"] = "legacy-key",
                    ["secretKey"] = rawSecret
                }
            }));
        var store = new ProviderCredentialStore(_dataRoot);

        var result = await store.GetCredentialsAsync("alpaca");

        result["secretKey"].Should().Be(rawSecret);
        File.Exists(legacyPath).Should().BeFalse();
        File.ReadAllText(Path.Combine(_dataRoot, ".mdc", "provider-credentials.vault"))
            .Should().NotContain(rawSecret);
    }

    [Fact]
    public async Task SaveCredentials_NeverCreatesLegacyPlaintextSidecar()
    {
        var store = new ProviderCredentialStore(_dataRoot);

        await store.SaveCredentialsAsync(
            "alpaca",
            new Dictionary<string, string> { ["keyId"] = "key", ["secretKey"] = "secret" });

        File.Exists(Path.Combine(_dataRoot, "provider-credentials.json")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
