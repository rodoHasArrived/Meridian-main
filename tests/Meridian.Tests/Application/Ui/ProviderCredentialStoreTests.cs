using FluentAssertions;
using Meridian.Ui.Shared.Services;
using System.Text.Json;
using Meridian.DataIntegration.Credentials;
using ProviderCredentialStore = Meridian.Ui.Shared.Services.ProviderCredentialStore;

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

    [Fact]
    public async Task Migration_InvalidLaterProviderDoesNotPartiallyImportAndRetainsSidecar()
    {
        var legacyPath = Path.Combine(_dataRoot, "provider-credentials.json");
        var legacy = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
        {
            ["alpaca"] = new() { ["keyId"] = "legacy-key", ["secretKey"] = "legacy-secret" },
            ["polygon"] = new() { ["unsupportedField"] = "invalid-secret" }
        });
        await File.WriteAllTextAsync(legacyPath, legacy);
        var store = new ProviderCredentialStore(_dataRoot);

        var migrate = () => store.GetCredentialsAsync("alpaca");
        await migrate.Should().ThrowAsync<ProviderCredentialValidationException>();

        (await File.ReadAllTextAsync(legacyPath)).Should().Be(legacy);
        File.Exists(Path.Combine(_dataRoot, ".mdc", "provider-credentials.vault")).Should().BeFalse();
    }

    [Fact]
    public async Task Migration_AuditFailureThenRotationAndRetryPreservesAuthoritativeVault()
    {
        var legacyPath = Path.Combine(_dataRoot, "provider-credentials.json");
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
        {
            ["alpaca"] = new() { ["keyId"] = "legacy-key", ["secretKey"] = "legacy-secret" },
            ["polygon"] = new() { ["apiKey"] = "polygon-legacy-secret" }
        }));
        var auditPath = Path.Combine(_dataRoot, ".mdc", "provider-credentials.audit.jsonl");
        Directory.CreateDirectory(auditPath);
        var store = new ProviderCredentialStore(_dataRoot);
        var migrate = () => store.GetCredentialsAsync("alpaca");
        var failure = await Record.ExceptionAsync(migrate);
        (failure is IOException or UnauthorizedAccessException).Should().BeTrue("the audit destination is a directory");
        File.Exists(legacyPath).Should().BeTrue();

        // Vault publication succeeded before audit failed; both providers must survive restart.
        var vault = new FileProviderCredentialStore(_dataRoot);
        (await vault.ReadForProviderAsync("polygon"))!.Get("apiKey").Should().Be("polygon-legacy-secret");
        Directory.Delete(auditPath);
        await vault.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?>
        {
            ["KeyId"] = "rotated-key",
            ["SecretKey"] = "rotated-secret"
        }));
        await vault.RecordVerificationAsync(new ProviderCredentialVerificationUpdate("alpaca", true, ExternalAccountId: "verified-account"));

        var reopened = new ProviderCredentialStore(_dataRoot);
        (await reopened.GetCredentialsAsync("alpaca"))["SecretKey"].Should().Be("rotated-secret");
        var retained = await vault.ReadForProviderAsync("alpaca");
        retained!.ExternalAccountId.Should().Be("verified-account");
        retained.LastVerifiedAt.Should().NotBeNull();
        File.Exists(legacyPath).Should().BeFalse();
        (await File.ReadAllTextAsync(auditPath)).Should().Contain("legacy-import-or-preserve").And.NotContain("rotated-secret").And.NotContain("polygon-legacy-secret");
    }

    [Fact]
    public async Task ConcurrentVaultInstances_MergeFieldsWithoutLosingAnAcknowledgedSave()
    {
        var first = new FileProviderCredentialStore(_dataRoot);
        var second = new FileProviderCredentialStore(_dataRoot);
        await Task.WhenAll(
            first.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?> { ["KeyId"] = "concurrent-key" })),
            second.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?> { ["SecretKey"] = "concurrent-secret" })));

        var reopened = new FileProviderCredentialStore(_dataRoot);
        var retained = await reopened.ReadForProviderAsync("alpaca");
        retained!.Get("KeyId").Should().Be("concurrent-key");
        retained.Get("SecretKey").Should().Be("concurrent-secret");
    }

    [Fact]
    public async Task VaultLock_ContentionHonorsCancellationAndAllowsSubsequentSave()
    {
        var vault = new FileProviderCredentialStore(_dataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(vault.VaultPath)!);
        using var cancellation = new CancellationTokenSource();
        var request = new ProviderCredentialSaveRequest("polygon", new Dictionary<string, string?> { ["apiKey"] = "retry-secret" });
        using (var held = new FileStream(vault.VaultPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var pending = vault.SaveAsync(request, cancellation.Token);
            pending.IsCompleted.Should().BeFalse();
            await cancellation.CancelAsync();
            var observe = () => pending;
            await observe.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(vault.VaultPath).Should().BeFalse();
        }
        await vault.SaveAsync(request);
        (await vault.ReadForProviderAsync("polygon"))!.Get("apiKey").Should().Be("retry-secret");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
