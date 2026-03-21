using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Xunit;

namespace Meridian.Tests.Credentials;

public class OAuthTokenTests
{
    [Fact]
    public void OAuthToken_IsExpired_TrueWhenPastExpiration()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)
        );

        // Assert
        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void OAuthToken_IsExpired_FalseWhenNotExpired()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1)
        );

        // Assert
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void OAuthToken_IsExpiringSoon_TrueWhenWithin5Minutes()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(3)
        );

        // Assert
        token.IsExpiringSoon.Should().BeTrue();
    }

    [Fact]
    public void OAuthToken_IsExpiringSoon_FalseWhenMoreThan5Minutes()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10)
        );

        // Assert
        token.IsExpiringSoon.Should().BeFalse();
    }

    [Fact]
    public void OAuthToken_CanRefresh_TrueWhenRefreshTokenPresent()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            RefreshToken: "refresh_token_value"
        );

        // Assert
        token.CanRefresh.Should().BeTrue();
    }

    [Fact]
    public void OAuthToken_CanRefresh_FalseWhenNoRefreshToken()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            RefreshToken: null
        );

        // Assert
        token.CanRefresh.Should().BeFalse();
    }

    [Fact]
    public void OAuthToken_CanRefresh_FalseWhenRefreshTokenExpired()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            RefreshToken: "refresh_token_value",
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)
        );

        // Assert
        token.CanRefresh.Should().BeFalse();
    }

    [Fact]
    public void OAuthToken_TimeUntilExpiration_CalculatesCorrectly()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: expiresAt
        );

        // Assert
        token.TimeUntilExpiration.TotalHours.Should().BeApproximately(2, 0.1);
    }

    [Fact]
    public void OAuthToken_LifetimeRemainingPercent_CalculatesCorrectly()
    {
        // Arrange
        var issuedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1); // 2 hour total lifetime, 1 hour remaining = 50%
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: expiresAt,
            IssuedAt: issuedAt
        );

        // Assert
        token.LifetimeRemainingPercent.Should().BeApproximately(50, 5); // Allow 5% tolerance
    }

    [Fact]
    public void OAuthToken_LifetimeRemainingPercent_ZeroWhenExpired()
    {
        // Arrange
        var issuedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: expiresAt,
            IssuedAt: issuedAt
        );

        // Assert
        token.LifetimeRemainingPercent.Should().Be(0);
    }

    [Fact]
    public void OAuthToken_LifetimeRemainingPercent_100WhenNoIssuedAt()
    {
        // Arrange
        var token = new OAuthToken(
            AccessToken: "test_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            IssuedAt: null
        );

        // Assert
        token.LifetimeRemainingPercent.Should().Be(100);
    }

    [Fact]
    public void OAuthRefreshResult_Success_ContainsNewToken()
    {
        // Arrange
        var newToken = new OAuthToken(
            AccessToken: "new_token",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1)
        );

        var result = new OAuthRefreshResult(
            Success: true,
            Token: newToken,
            RefreshedAt: DateTimeOffset.UtcNow
        );

        // Assert
        result.Success.Should().BeTrue();
        result.Token.Should().NotBeNull();
        result.Token!.AccessToken.Should().Be("new_token");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void OAuthRefreshResult_Failure_ContainsErrorMessage()
    {
        // Arrange
        var result = new OAuthRefreshResult(
            Success: false,
            Error: "Invalid refresh token",
            RefreshedAt: DateTimeOffset.UtcNow
        );

        // Assert
        result.Success.Should().BeFalse();
        result.Token.Should().BeNull();
        result.Error.Should().Be("Invalid refresh token");
    }

    [Fact]
    public void OAuthProviderConfig_StoresAllFields()
    {
        // Arrange
        var config = new OAuthProviderConfig(
            ProviderName: "TestProvider",
            ClientId: "client_123",
            ClientSecret: "secret_456",
            AuthorizationEndpoint: "https://auth.example.com/authorize",
            TokenEndpoint: "https://auth.example.com/token",
            Scopes: new[] { "read", "write" },
            RedirectUri: "https://localhost/callback"
        );

        // Assert
        config.ProviderName.Should().Be("TestProvider");
        config.ClientId.Should().Be("client_123");
        config.ClientSecret.Should().Be("secret_456");
        config.AuthorizationEndpoint.Should().Be("https://auth.example.com/authorize");
        config.TokenEndpoint.Should().Be("https://auth.example.com/token");
        config.Scopes.Should().BeEquivalentTo(new[] { "read", "write" });
        config.RedirectUri.Should().Be("https://localhost/callback");
    }
}
