using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using System.Text.Json;
using Xunit;

namespace Meridian.Tests.Application.Config;

/// <summary>OAuth secrets migrate to the shared encrypted vault without plaintext rewrites.</summary>
public sealed class OAuthTokenPersistencePermissionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-oauth-vault", Guid.NewGuid().ToString("N"));
    private string TokenPath => Path.Combine(_root, ".mdc", "oauth_tokens.json");
    private static OAuthToken SampleToken(string secret = "access-secret") => new(secret, "Bearer",
        DateTimeOffset.UtcNow.AddHours(1), "refresh-secret");

    [Fact]
    public async Task StoreToken_RestartsFromEncryptedVaultWithoutPlaintextFiles()
    {
        await using (var service = new OAuthTokenRefreshService(_root))
            await service.StoreTokenAsync("custom-provider", SampleToken());
        await using var reopened = new OAuthTokenRefreshService(_root);
        await reopened.InitializeAsync();
        reopened.GetToken("custom-provider")!.AccessToken.Should().Be("access-secret");
        reopened.GetToken("custom-provider")!.RefreshToken.Should().Be("refresh-secret");
        File.Exists(TokenPath).Should().BeFalse();
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            (await File.ReadAllTextAsync(file)).Should().NotContain("access-secret").And.NotContain("refresh-secret");
    }

    [Fact]
    public async Task LegacyMigration_PreservesNewerVaultTokenAndRemovesPlaintext()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("custom-provider", SampleToken("rotated-secret"));
        await File.WriteAllTextAsync(TokenPath, JsonSerializer.Serialize(new Dictionary<string, OAuthToken>
        {
            ["custom-provider"] = SampleToken("obsolete-secret"),
            ["another-provider"] = SampleToken()
        }));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(TokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        await using var service = new OAuthTokenRefreshService(_root);
        await service.InitializeAsync();
        service.GetToken("custom-provider")!.AccessToken.Should().Be("rotated-secret");
        service.GetToken("another-provider")!.AccessToken.Should().Be("access-secret");
        File.Exists(TokenPath).Should().BeFalse();
    }

    [Fact]
    public async Task SeparateServices_PreserveOtherProvidersAndDoNotResurrectDeletedTokensOnDispose()
    {
        var first = new OAuthTokenRefreshService(_root);
        await using var second = new OAuthTokenRefreshService(_root);
        await first.StoreTokenAsync("first", SampleToken());
        await second.StoreTokenAsync("second", SampleToken());
        await second.RemoveTokenAsync("first");
        await first.DisposeAsync();
        await using var reopened = new OAuthTokenRefreshService(_root);
        await reopened.InitializeAsync();
        reopened.GetToken("first").Should().BeNull();
        reopened.GetToken("second").Should().NotBeNull();
    }

    [Fact]
    public async Task FailedMigration_RetainsLegacySourceForRetry()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        var legacy = JsonSerializer.Serialize(new Dictionary<string, OAuthToken> { ["provider"] = SampleToken() });
        await File.WriteAllTextAsync(TokenPath, legacy);
        var auditPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl");
        Directory.CreateDirectory(auditPath);
        await using var failed = new OAuthTokenRefreshService(_root);
        var initialize = () => failed.InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidOperationException>().WithMessage("Encrypted OAuth persistence could not be initialized.");
        (await File.ReadAllTextAsync(TokenPath)).Should().Be(legacy);
        Directory.Delete(auditPath);
        await using var recovered = new OAuthTokenRefreshService(_root);
        await recovered.InitializeAsync();
        recovered.GetToken("provider")!.RefreshToken.Should().Be("refresh-secret");
        File.Exists(TokenPath).Should().BeFalse();
    }

    [Fact]
    public async Task InterruptedLegacyErasure_RestartsFromVaultAndFinishesCleanup()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("provider", SampleToken());
        // Persisted state after import, rename, and interrupted zero-fill of the old source.
        await File.WriteAllBytesAsync(TokenPath + ".migrated", new byte[300]);

        await using var service = new OAuthTokenRefreshService(_root);
        await service.InitializeAsync();

        service.GetToken("provider")!.AccessToken.Should().Be("access-secret");
        File.Exists(TokenPath).Should().BeFalse();
        File.Exists(TokenPath + ".migrated").Should().BeFalse();
    }

    [Fact]
    public async Task AuditFailures_ReconcileCommittedStoreAndDeleteWithRunningCache()
    {
        await using var service = new OAuthTokenRefreshService(_root);
        await service.InitializeAsync();
        var auditPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl");
        Directory.CreateDirectory(auditPath);

        var store = () => service.StoreTokenAsync("provider", SampleToken());
        var storeFailure = await Record.ExceptionAsync(store);
        (storeFailure is IOException or UnauthorizedAccessException).Should().BeTrue();
        service.GetToken("provider")!.AccessToken.Should().Be("access-secret");

        var remove = () => service.RemoveTokenAsync("provider");
        var deleteFailure = await Record.ExceptionAsync(remove);
        (deleteFailure is IOException or UnauthorizedAccessException).Should().BeTrue();
        service.GetToken("provider").Should().BeNull();
        // The recovery generation must also respect the committed deletion.
        var vault = new FileProviderCredentialStore(_root);
        await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");
        (await vault.ReadOAuthTokensAsync()).Should().NotContainKey("provider");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TruncatedPrimary_RecoversTokensWithoutOverwritingUsableBackup(string truncated)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("retained", SampleToken());
        await vault.SaveOAuthTokenAsync("later", SampleToken("later-access"));
        await File.WriteAllTextAsync(vault.VaultPath, truncated);

        await using var service = new OAuthTokenRefreshService(_root);
        await service.InitializeAsync();
        service.GetToken("retained")!.AccessToken.Should().Be("access-secret");
        await service.StoreTokenAsync("new", SampleToken("new-access"));
        await File.WriteAllTextAsync(vault.VaultPath, "corrupt-again");

        (await vault.ReadOAuthTokensAsync())["retained"].AccessToken.Should().Be("access-secret");
    }

    [Fact]
    public async Task FailedLegacyAuditThenDeletion_RetryDoesNotResurrectOAuthToken()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        await File.WriteAllTextAsync(TokenPath, JsonSerializer.Serialize(new Dictionary<string, OAuthToken>
        {
            ["provider"] = SampleToken()
        }));
        var auditPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl");
        Directory.CreateDirectory(auditPath);
        await using var failed = new OAuthTokenRefreshService(_root);
        var initialize = () => failed.InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidOperationException>();
        Directory.Delete(auditPath);
        await new FileProviderCredentialStore(_root).SaveOAuthTokenAsync("provider", null);

        await using var recovered = new OAuthTokenRefreshService(_root);
        await recovered.InitializeAsync();

        recovered.GetToken("provider").Should().BeNull();
        File.Exists(TokenPath).Should().BeFalse();
    }

    [Fact]
    public async Task ProviderCredentialMutationsAndOAuthMutationsPreserveEachOther()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("custom-provider", SampleToken());
        await vault.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?>
        {
            ["KeyId"] = "provider-key",
            ["SecretKey"] = "provider-secret"
        }));
        await vault.SaveOAuthTokenAsync("other-provider", SampleToken("other-access"));
        await vault.DeleteAsync("alpaca");
        var reopened = new FileProviderCredentialStore(_root);
        var tokens = await reopened.ReadOAuthTokensAsync();
        tokens["custom-provider"].AccessToken.Should().Be("access-secret");
        tokens["other-provider"].AccessToken.Should().Be("other-access");
        await reopened.SaveAsync(new ProviderCredentialSaveRequest("polygon", new Dictionary<string, string?> { ["apiKey"] = "retained-key" }));
        await reopened.SaveOAuthTokenAsync("custom-provider", null);
        (await reopened.ReadForProviderAsync("polygon"))!.Get("apiKey").Should().Be("retained-key");
        (await reopened.ReadOAuthTokensAsync()).Should().NotContainKey("custom-provider");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
