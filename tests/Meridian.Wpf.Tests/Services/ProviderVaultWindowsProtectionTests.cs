using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Wpf.Tests.Services;

/// <summary>Runs in the Windows-only WPF suite; never substitutes the development Unix AES vault.</summary>
public sealed class ProviderVaultWindowsProtectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-windows-vault", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScopedVault_RestartRotationAndRemovalUseDpapiWithoutAnAdjacentKey()
    {
        OperatingSystem.IsWindows().Should().BeTrue("this proof belongs to the supported Windows production envelope");
        var first = new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "paper");
        var second = new ProviderCredentialScope("tenant-b", "connection-b", "account-b", "paper");
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveScopedAsync(Credentials("synthetic-a-key"), first);
        await vault.SaveScopedAsync(Credentials("synthetic-b-key"), second);
        var original = (await vault.ReadScopedAsync("alpaca", first))!;
        await vault.SaveScopedRotatedCredentialsAsync(Credentials("synthetic-a-rotated"), first, original);

        vault = new FileProviderCredentialStore(_root);
        (await vault.ReadScopedAsync("alpaca", first))!.Get("KeyId").Should().Be("synthetic-a-rotated");
        (await vault.ReadScopedAsync("alpaca", second))!.Get("KeyId").Should().Be("synthetic-b-key");
        (await vault.ReadScopedAsync("alpaca", new ProviderCredentialScope(second.TenantId, first.ConnectionId, first.ExternalAccountId, first.Environment))).Should().BeNull();
        await vault.DeleteScopedAsync("alpaca", first, "synthetic-operator");
        (await new FileProviderCredentialStore(_root).ReadScopedAsync("alpaca", first)).Should().BeNull();
        (await new FileProviderCredentialStore(_root).ReadScopedAsync("alpaca", second))!.Get("KeyId").Should().Be("synthetic-b-key");

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var contents = await File.ReadAllTextAsync(path);
            contents.Should().NotContain("synthetic-a-key").And.NotContain("synthetic-a-rotated").And.NotContain("synthetic-b-key");
        }
        File.Exists(Path.Combine(_root, ".mdc", "provider-credentials.key")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "provider-credentials.json")).Should().BeFalse();
        using var envelope = JsonDocument.Parse(await File.ReadAllBytesAsync(vault.VaultPath));
        envelope.RootElement.GetProperty("protection").GetString().Should().Be("dpapi-current-user+scoped-v2");
        var cipher = Convert.FromBase64String(envelope.RootElement.GetProperty("cipherText").GetString()!);
        Action wrongProtectionContext = () => ProtectedData.Unprotect(cipher, [1, 2, 3], DataProtectionScope.CurrentUser);
        wrongProtectionContext.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task ScopedMigration_RollbackAndRotationRetainWindowsProtectedOwnershipAcrossRestart()
    {
        OperatingSystem.IsWindows().Should().BeTrue();
        var scope = new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "paper");
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Credentials("synthetic-legacy-key"));
        var legacy = (await vault.ReadForProviderAsync("alpaca"))!;
        await vault.MigrateToScopeAsync("alpaca", scope, "transfer-a", legacy.CredentialGeneration!, "synthetic-operator");
        vault = new FileProviderCredentialStore(_root);
        (await vault.ReadForProviderAsync("alpaca")).Should().BeNull();
        (await vault.ReadScopedAsync("alpaca", scope))!.Get("KeyId").Should().Be("synthetic-legacy-key");
        await vault.RollbackMigrationAsync("alpaca", scope, "transfer-a", "synthetic-operator");
        vault = new FileProviderCredentialStore(_root);
        (await vault.ReadScopedAsync("alpaca", scope)).Should().BeNull();
        legacy = (await vault.ReadForProviderAsync("alpaca"))!;
        await vault.MigrateToScopeAsync("alpaca", scope, "transfer-b", legacy.CredentialGeneration!, "synthetic-operator");
        var transferred = (await vault.ReadScopedAsync("alpaca", scope))!;
        await vault.SaveScopedRotatedCredentialsAsync(Credentials("synthetic-rotated-key"), scope, transferred);
        var rollback = () => vault.RollbackMigrationAsync("alpaca", scope, "transfer-b", "synthetic-operator");
        await rollback.Should().ThrowAsync<ProviderCredentialConflictException>();
        vault = new FileProviderCredentialStore(_root);
        (await vault.ReadForProviderAsync("alpaca")).Should().BeNull();
        (await vault.ReadScopedAsync("alpaca", scope))!.Get("KeyId").Should().Be("synthetic-rotated-key");
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            (await File.ReadAllTextAsync(path)).Should().NotContain("synthetic-legacy-key").And.NotContain("synthetic-rotated-key");
        File.Exists(Path.Combine(_root, ".mdc", "provider-credentials.key")).Should().BeFalse();
    }

    private static ProviderCredentialSaveRequest Credentials(string key) => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = key + "-secret" }, "paper", "synthetic-operator");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
