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
        reopened.GetToken("custom-provider")!.AccessToken.Should().Be("access-secret");
        reopened.GetToken("custom-provider")!.RefreshToken.Should().Be("refresh-secret");
        File.Exists(TokenPath).Should().BeFalse();
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            (await File.ReadAllTextAsync(file)).Should().NotContain("access-secret").And.NotContain("refresh-secret");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("connection")]
    [InlineData("account")]
    [InlineData("environment")]
    public async Task ScopedTokens_RestartRotationAndDeletionPreserveOtherOwners(string dimension)
    {
        var first = new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "paper");
        var second = new ProviderCredentialScope(
            dimension == "tenant" ? "tenant-b" : "tenant-a",
            dimension == "connection" ? "connection-b" : "connection-a",
            dimension == "account" ? "account-b" : "account-a",
            dimension == "environment" ? "live" : "paper");
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("provider", SampleToken("legacy-access"));
        await using (var firstService = new OAuthTokenRefreshService(_root, ownershipScope: first))
        await using (var secondService = new OAuthTokenRefreshService(_root, ownershipScope: second))
        {
            firstService.GetToken("provider").Should().BeNull();
            secondService.GetToken("provider").Should().BeNull();
            await Task.WhenAll(firstService.StoreTokenAsync("provider", SampleToken("first-access")),
                secondService.StoreTokenAsync("provider", SampleToken("second-access")));
        }
        await using (var firstService = new OAuthTokenRefreshService(_root, ownershipScope: first))
        await using (var secondService = new OAuthTokenRefreshService(_root, ownershipScope: second))
        {
            firstService.GetToken("provider")!.AccessToken.Should().Be("first-access");
            secondService.GetToken("provider")!.AccessToken.Should().Be("second-access");
            await firstService.StoreTokenAsync("provider", SampleToken("rotated-access"));
            (await vault.ReadScopedOAuthTokensAsync(first))["provider"].AccessToken.Should().Be("rotated-access");
            await firstService.RemoveTokenAsync("provider");
        }
        await using var removed = new OAuthTokenRefreshService(_root, ownershipScope: first);
        await using var retained = new OAuthTokenRefreshService(_root, ownershipScope: second);
        removed.GetToken("provider").Should().BeNull();
        retained.GetToken("provider")!.AccessToken.Should().Be("second-access");
        (await vault.ReadOAuthTokensAsync()).Should().ContainSingle().Which.Value.AccessToken.Should().Be("legacy-access");
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            (await File.ReadAllTextAsync(file)).Should().NotContain("first-access").And.NotContain("second-access")
                .And.NotContain("rotated-access").And.NotContain("refresh-secret");
    }

    [Fact]
    public async Task ScopedService_DoesNotClaimOrDestroyUnassignedLegacyTokens()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        var legacy = JsonSerializer.Serialize(new Dictionary<string, OAuthToken> { ["provider"] = SampleToken() });
        await File.WriteAllTextAsync(TokenPath, legacy);
        await using var scoped = new OAuthTokenRefreshService(_root,
            ownershipScope: new ProviderCredentialScope("tenant", "connection", "account", "paper"));
        scoped.GetToken("provider").Should().BeNull();
        await scoped.StoreTokenAsync("provider", SampleToken("owned-access"));
        (await File.ReadAllTextAsync(TokenPath)).Should().Be(legacy);
        (await new FileProviderCredentialStore(_root).ReadOAuthTokensAsync()).Should().BeEmpty();
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
        var construct = () => new OAuthTokenRefreshService(_root);
        construct.Should().Throw<InvalidOperationException>().WithMessage("Encrypted OAuth persistence could not be initialized.");
        (await File.ReadAllTextAsync(TokenPath)).Should().Be(legacy);
        Directory.Delete(auditPath);
        await using var recovered = new OAuthTokenRefreshService(_root);
        recovered.GetToken("provider")!.RefreshToken.Should().Be("refresh-secret");
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
