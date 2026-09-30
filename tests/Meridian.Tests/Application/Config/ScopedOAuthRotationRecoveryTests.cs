using System.Net;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ScopedOAuthRotationRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-scoped-oauth-rotation", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "connection-a", "account-a", "paper");
    private static readonly ProviderCredentialScope Other = new("tenant-b", "connection-a", "account-a", "paper");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("corrupt-primary")]
    [InlineData(null)]
    public async Task CompletedRemoteRotation_RestartRecoversOnlyTheOwnersReplacementToken(string? corruption)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("provider", Token("unassigned"));
        await vault.SaveScopedOAuthTokenAsync("provider", Token("foreign"), Other);
        using var handler = new RotatingProvider();
        using var client = new HttpClient(handler);
        await using (var service = new OAuthTokenRefreshService(_root, httpClient: client, ownershipScope: Owner))
        {
            service.RegisterProvider(new OAuthProviderConfig("provider", "client", TokenEndpoint: "https://provider.example/token"));
            await service.StoreTokenAsync("provider", Token("original"));

            var refreshed = await service.RefreshTokenAsync("provider");

            refreshed.Success.Should().BeTrue();
            refreshed.Token!.RefreshToken.Should().Be("replacement-refresh");
        }
        if (corruption is null)
            File.Delete(vault.VaultPath);
        else
            await File.WriteAllTextAsync(vault.VaultPath, corruption);

        await using var restarted = new OAuthTokenRefreshService(_root, ownershipScope: Owner);
        await restarted.InitializeAsync();

        restarted.GetToken("provider")!.AccessToken.Should().Be("replacement-access");
        restarted.GetToken("provider")!.RefreshToken.Should().Be("replacement-refresh",
            "the provider invalidated this owner's original refresh token when rotation succeeded");
        (await vault.ReadScopedOAuthTokensAsync(Other))["provider"].RefreshToken.Should().Be("foreign-refresh");
        (await vault.ReadOAuthTokensAsync())["provider"].RefreshToken.Should().Be("unassigned-refresh");
        handler.Calls.Should().Be(1, "recovery must not request another remote rotation");
    }

    [Fact]
    public async Task RotationBackupFailure_IsNotAcknowledgedAndCanRetryForTheSameOwner()
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveScopedOAuthTokenAsync("provider", Token("foreign"), Other);
        using var handler = new RotatingProvider();
        using var client = new HttpClient(handler);
        await using var service = new OAuthTokenRefreshService(_root, httpClient: client, ownershipScope: Owner);
        service.RegisterProvider(new OAuthProviderConfig("provider", "client", TokenEndpoint: "https://provider.example/token"));
        await service.StoreTokenAsync("provider", Token("original"));
        var backup = vault.VaultPath + ".bak";
        File.Delete(backup);
        Directory.CreateDirectory(backup);
        var acknowledged = 0;
        service.OnTokenRefreshed += (_, _) => acknowledged++;

        var result = await service.RefreshTokenAsync("provider");

        result.Success.Should().BeFalse();
        acknowledged.Should().Be(0);
        service.GetToken("provider")!.RefreshToken.Should().Be("replacement-refresh");
        (await vault.ReadScopedOAuthTokensAsync(Owner))["provider"].RefreshToken.Should().Be("replacement-refresh");
        Directory.Delete(backup);
        await service.StoreTokenAsync("provider", service.GetToken("provider")!);
        await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");

        await using var restarted = new OAuthTokenRefreshService(_root, ownershipScope: Owner);
        await restarted.InitializeAsync();
        restarted.GetToken("provider")!.RefreshToken.Should().Be("replacement-refresh");
        (await vault.ReadScopedOAuthTokensAsync(Other))["provider"].RefreshToken.Should().Be("foreign-refresh");
        handler.Calls.Should().Be(1);
    }

    private static OAuthToken Token(string value) => new(value + "-access", "Bearer",
        DateTimeOffset.UtcNow.AddHours(1), value + "-refresh");

    private sealed class RotatingProvider : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"replacement-access","refresh_token":"replacement-refresh","token_type":"Bearer","expires_in":3600}""")
            });
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
