using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Xunit;

namespace Meridian.Tests.Credentials;

public class CredentialStatusTests
{
    [Fact]
    public void CredentialTestResult_IsSuccess_TrueForValidStatus()
    {
        // Arrange
        var result = new CredentialTestResult(
            ProviderName: "Alpaca",
            Status: CredentialAuthStatus.Valid,
            Message: "Valid credentials",
            TestedAt: DateTimeOffset.UtcNow
        );

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CredentialTestResult_IsSuccess_TrueForExpiringSoonStatus()
    {
        // Arrange
        var result = new CredentialTestResult(
            ProviderName: "Alpaca",
            Status: CredentialAuthStatus.ExpiringSoon,
            Message: "Credentials expiring soon",
            TestedAt: DateTimeOffset.UtcNow
        );

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(CredentialAuthStatus.Invalid)]
    [InlineData(CredentialAuthStatus.Expired)]
    [InlineData(CredentialAuthStatus.TestFailed)]
    [InlineData(CredentialAuthStatus.NotConfigured)]
    [InlineData(CredentialAuthStatus.Unknown)]
    public void CredentialTestResult_IsSuccess_FalseForFailureStatuses(CredentialAuthStatus status)
    {
        // Arrange
        var result = new CredentialTestResult(
            ProviderName: "Test",
            Status: status,
            Message: "Test message",
            TestedAt: DateTimeOffset.UtcNow
        );

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void CredentialTestResult_TimeUntilExpiration_CalculatesCorrectly()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddDays(5);
        var result = new CredentialTestResult(
            ProviderName: "Test",
            Status: CredentialAuthStatus.Valid,
            Message: "Valid",
            TestedAt: DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt
        );

        // Assert
        result.TimeUntilExpiration.Should().NotBeNull();
        result.TimeUntilExpiration!.Value.TotalDays.Should().BeApproximately(5, 0.1);
    }

    [Fact]
    public void CredentialTestResult_TimeUntilExpiration_NullWhenNoExpiration()
    {
        // Arrange
        var result = new CredentialTestResult(
            ProviderName: "Test",
            Status: CredentialAuthStatus.Valid,
            Message: "Valid",
            TestedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null
        );

        // Assert
        result.TimeUntilExpiration.Should().BeNull();
    }

    [Fact]
    public void CredentialStatusSummary_AllValid_TrueWhenAllResultsSuccess()
    {
        // Arrange
        var results = new[]
        {
            new CredentialTestResult("Alpaca", CredentialAuthStatus.Valid, "OK", DateTimeOffset.UtcNow),
            new CredentialTestResult("Polygon", CredentialAuthStatus.Valid, "OK", DateTimeOffset.UtcNow)
        };

        var summary = new CredentialStatusSummary(
            Results: results,
            AllValid: results.All(r => r.IsSuccess),
            TestedAt: DateTimeOffset.UtcNow,
            Warnings: Array.Empty<string>()
        );

        // Assert
        summary.AllValid.Should().BeTrue();
    }

    [Fact]
    public void CredentialStatusSummary_AllValid_FalseWhenAnyResultFails()
    {
        // Arrange
        var results = new[]
        {
            new CredentialTestResult("Alpaca", CredentialAuthStatus.Valid, "OK", DateTimeOffset.UtcNow),
            new CredentialTestResult("Polygon", CredentialAuthStatus.Invalid, "Invalid key", DateTimeOffset.UtcNow)
        };

        var summary = new CredentialStatusSummary(
            Results: results,
            AllValid: results.All(r => r.IsSuccess),
            TestedAt: DateTimeOffset.UtcNow,
            Warnings: Array.Empty<string>()
        );

        // Assert
        summary.AllValid.Should().BeFalse();
    }

    [Fact]
    public void StoredCredentialStatus_TracksConsecutiveFailures()
    {
        // Arrange
        var status = new StoredCredentialStatus(
            ProviderName: "Test",
            LastSuccessfulAuth: DateTimeOffset.UtcNow.AddHours(-1),
            LastTestResult: CredentialAuthStatus.TestFailed,
            LastTestedAt: DateTimeOffset.UtcNow,
            ConsecutiveFailures: 3
        );

        // Assert
        status.ConsecutiveFailures.Should().Be(3);
        status.LastTestResult.Should().Be(CredentialAuthStatus.TestFailed);
    }

    [Fact]
    public void CredentialExpirationConfig_HasReasonableDefaults()
    {
        // Arrange
        var config = new CredentialExpirationConfig();

        // Assert
        config.WarnDaysBeforeExpiration.Should().Be(7);
        config.CriticalDaysBeforeExpiration.Should().Be(1);
        config.AutoRefreshEnabled.Should().BeTrue();
        config.AutoRefreshDaysBeforeExpiration.Should().Be(3);
    }
}
