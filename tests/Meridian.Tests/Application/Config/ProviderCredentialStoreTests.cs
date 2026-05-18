using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.Contracts.Configuration;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ProviderCredentialStoreTests : IDisposable
{
    private readonly string _root;

    public ProviderCredentialStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-credentials", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndRead_RoundTripsCredentialThroughEncryptedLocalStore()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "paper-key-id",
                ["SecretKey"] = "paper-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        var read = await store.ReadForProviderAsync("alpaca");
        var status = await store.GetStatusAsync("alpaca");

        read.Should().NotBeNull();
        read!.Source.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
        read.Get("KeyId").Should().Be("paper-key-id");
        read.Get("SecretKey").Should().Be("paper-secret");
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        status.CredentialSource.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
        status.MaskedKeyPreview.Should().NotContain("paper-key-id");
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistPlaintextSecretsInVaultOrAudit()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "plain-key",
                ["SecretKey"] = "plain-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        var vaultText = await File.ReadAllTextAsync(store.VaultPath);
        var auditText = await File.ReadAllTextAsync(Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl"));

        vaultText.Should().NotContain("plain-key");
        vaultText.Should().NotContain("plain-secret");
        auditText.Should().NotContain("plain-key");
        auditText.Should().NotContain("plain-secret");
        auditText.Should().Contain("\"action\":\"save\"");
    }

    [Fact]
    public async Task ReadForProviderAsync_UsesEnvironmentAsReadOnlyLegacyFallback()
    {
        using var env = new EnvironmentScope("POLYGON_API_KEY", "legacy-polygon-key");
        var store = new FileProviderCredentialStore(_root);

        var read = await store.ReadForProviderAsync("polygon");
        var status = await store.GetStatusAsync("polygon");
        await store.DeleteAsync("polygon", "test-operator");

        read.Should().NotBeNull();
        read!.Source.Should().Be(ProviderCredentialSourceDto.Environment);
        read.Get("ApiKey").Should().Be("legacy-polygon-key");
        status.CredentialSource.Should().Be(ProviderCredentialSourceDto.Environment);
        Environment.GetEnvironmentVariable("POLYGON_API_KEY").Should().Be("legacy-polygon-key");
    }

    [Fact]
    public async Task DeleteAsync_RemovesLocalCredentialAndKeepsAuditMetadata()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "finnhub",
            new Dictionary<string, string?> { ["ApiKey"] = "finnhub-secret" },
            Actor: "test-operator"));

        await store.DeleteAsync("finnhub", "test-operator");

        var status = await store.GetStatusAsync("finnhub");
        var auditText = await File.ReadAllTextAsync(Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl"));

        status.CredentialState.Should().Be(ProviderCredentialStateDto.Missing);
        status.CredentialSource.Should().Be(ProviderCredentialSourceDto.None);
        auditText.Should().Contain("\"action\":\"delete\"");
        auditText.Should().Contain("\"actor\":\"test-operator\"");
        auditText.Should().NotContain("finnhub-secret");
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
