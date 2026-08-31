using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

/// <summary>
/// `.mdc/oauth_tokens.json` holds access and refresh tokens in cleartext. Unlike the provider
/// credential vault there is no encryption behind it, so on Unix the file mode is the entirety of
/// the protection. These pin that it is owner-only from creation, and repaired if an earlier
/// release left it readable.
/// </summary>
public sealed class OAuthTokenPersistencePermissionTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode WorldReadable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private readonly string _root;

    public OAuthTokenPersistencePermissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "oauth-tokens", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string TokenPath => Path.Combine(_root, ".mdc", "oauth_tokens.json");

    private static OAuthToken SampleToken() => new(
        AccessToken: "access-secret",
        TokenType: "Bearer",
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
        RefreshToken: "refresh-secret");

    [Fact]
    public async Task StoreTokenAsync_WritesTheTokenFileOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var service = new OAuthTokenRefreshService(_root);

        await service.StoreTokenAsync("alpaca", SampleToken());

        File.Exists(TokenPath).Should().BeTrue();
        File.GetUnixFileMode(TokenPath).Should().Be(
            OwnerOnly,
            "the tokens are stored in cleartext, so the file mode is the only thing protecting them");
    }

    // Rewriting only happens on the next refresh, which may be hours away or never while the tokens
    // remain valid. Repair therefore has to happen on load, not on write.
    [Fact]
    public async Task Construction_TightensATokenFileLeftReadableByAnEarlierRelease()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using (var seed = new OAuthTokenRefreshService(_root))
        {
            await seed.StoreTokenAsync("alpaca", SampleToken());
        }

        File.SetUnixFileMode(TokenPath, WorldReadable);

        await using var reopened = new OAuthTokenRefreshService(_root);

        File.GetUnixFileMode(TokenPath).Should().Be(OwnerOnly);
        reopened.GetToken("alpaca").Should().NotBeNull("tightening permissions must not cost the tokens");
        reopened.GetToken("alpaca")!.AccessToken.Should().Be("access-secret");
    }

    [Fact]
    public async Task RewritingAnAlreadyTightenedFileKeepsItOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var service = new OAuthTokenRefreshService(_root);
        await service.StoreTokenAsync("alpaca", SampleToken());

        await service.StoreTokenAsync("polygon", SampleToken());

        File.GetUnixFileMode(TokenPath).Should().Be(OwnerOnly);
    }
}
