using System.Net;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class OAuthRotationRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-oauth-rotation", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("corrupt-primary")]
    [InlineData(null)]
    public async Task CompletedRemoteRotation_RestartRecoversTheReplacementRefreshToken(string? corruption)
    {
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveOAuthTokenAsync("other-provider", Token("other"));
        using var handler = new RotatingProvider();
        using var client = new HttpClient(handler);
        await using (var service = new OAuthTokenRefreshService(_root, httpClient: client))
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

        await using var restarted = new OAuthTokenRefreshService(_root);
        await restarted.InitializeAsync();

        restarted.GetToken("provider")!.AccessToken.Should().Be("replacement-access");
        restarted.GetToken("provider")!.RefreshToken.Should().Be("replacement-refresh",
            "the provider invalidated the original refresh token when rotation succeeded");
        restarted.GetToken("other-provider")!.RefreshToken.Should().Be("other-refresh");
        handler.Calls.Should().Be(1, "recovery must not request another remote rotation");
    }

    [Fact]
    public async Task RotationBackupFailure_IsNotAcknowledgedAndCanRetryWithoutAnotherRemoteRotation()
    {
        var vault = new FileProviderCredentialStore(_root);
        using var handler = new RotatingProvider();
        using var client = new HttpClient(handler);
        await using var service = new OAuthTokenRefreshService(_root, httpClient: client);
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
        (await vault.ReadOAuthTokensAsync())["provider"].RefreshToken.Should().Be("replacement-refresh");
        Directory.Delete(backup);
        await service.StoreTokenAsync("provider", service.GetToken("provider")!);
        await File.WriteAllTextAsync(vault.VaultPath, "corrupt-primary");

        await using var restarted = new OAuthTokenRefreshService(_root);
        await restarted.InitializeAsync();
        restarted.GetToken("provider")!.RefreshToken.Should().Be("replacement-refresh");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RotationAuditAppendFailure_IsAcknowledgedAsCommittedWithAuditDeferred()
    {
        // Only the audit append fails: the replacement token is durable in both vault generations, so the
        // refresh succeeded and a retry would rotate a second time at the provider. The result says so.
        var vault = new FileProviderCredentialStore(_root);
        using var handler = new RotatingProvider();
        using var client = new HttpClient(handler);
        await using var service = new OAuthTokenRefreshService(_root, httpClient: client);
        service.RegisterProvider(new OAuthProviderConfig("provider", "client", TokenEndpoint: "https://provider.example/token"));
        await service.StoreTokenAsync("provider", Token("original"));
        var auditPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl");
        File.Delete(auditPath);
        Directory.CreateDirectory(auditPath);
        var acknowledged = 0;
        var failed = 0;
        service.OnTokenRefreshed += (_, _) => acknowledged++;
        service.OnRefreshFailed += (_, _) => failed++;

        var result = await service.RefreshTokenAsync("provider");

        result.Success.Should().BeTrue();
        result.AuditDeferred.Should().BeTrue();
        result.Token!.RefreshToken.Should().Be("replacement-refresh");
        acknowledged.Should().Be(1);
        failed.Should().Be(0);
        handler.Calls.Should().Be(1);
        (await vault.ReadOAuthTokensAsync())["provider"].RefreshToken.Should().Be("replacement-refresh");
        File.Exists(Path.Combine(_root, ".mdc", "provider-credentials.audit.pending.jsonl")).Should().BeTrue("the audit record is owed, not lost");
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
