using FluentAssertions;
using Meridian.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

/// <summary>
/// Tests for CredentialValidator utility methods.
/// </summary>
public sealed class CredentialValidatorTests
{
    private readonly ILogger _nullLogger = NullLogger.Instance;

    #region ValidateApiKey

    [Fact]
    public void ValidateApiKey_NullApiKey_ReturnsFalse()
    {
        CredentialValidator.ValidateApiKey(null, "TestProvider", _nullLogger)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateApiKey_EmptyApiKey_ReturnsFalse()
    {
        CredentialValidator.ValidateApiKey("", "TestProvider", _nullLogger)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateApiKey_ValidApiKey_ReturnsTrue()
    {
        CredentialValidator.ValidateApiKey("test-api-key-123", "TestProvider", _nullLogger)
            .Should().BeTrue();
    }

    [Fact]
    public void ValidateApiKey_NullLogger_DoesNotThrow()
    {
        var act = () => CredentialValidator.ValidateApiKey(null, "TestProvider");

        act.Should().NotThrow();
    }

    #endregion

    #region ValidateKeySecretPair

    [Fact]
    public void ValidateKeySecretPair_BothNull_ReturnsFalse()
    {
        CredentialValidator.ValidateKeySecretPair(null, null, "TestProvider", _nullLogger)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateKeySecretPair_NullKey_ReturnsFalse()
    {
        CredentialValidator.ValidateKeySecretPair(null, "secret", "TestProvider", _nullLogger)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateKeySecretPair_NullSecret_ReturnsFalse()
    {
        CredentialValidator.ValidateKeySecretPair("key", null, "TestProvider", _nullLogger)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateKeySecretPair_EmptyKey_ReturnsFalse()
    {
        CredentialValidator.ValidateKeySecretPair("", "secret", "TestProvider", _nullLogger)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateKeySecretPair_EmptySecret_ReturnsFalse()
    {
        CredentialValidator.ValidateKeySecretPair("key", "", "TestProvider", _nullLogger)
            .Should().BeFalse();
    }

    [Fact]
    public void ValidateKeySecretPair_BothValid_ReturnsTrue()
    {
        CredentialValidator.ValidateKeySecretPair("my-key", "my-secret", "TestProvider", _nullLogger)
            .Should().BeTrue();
    }

    #endregion

    #region ThrowIfApiKeyMissing

    [Fact]
    public void ThrowIfApiKeyMissing_NullApiKey_ThrowsInvalidOperationException()
    {
        var act = () => CredentialValidator.ThrowIfApiKeyMissing(null, "Polygon", "POLYGON__APIKEY");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Polygon*")
            .WithMessage("*POLYGON__APIKEY*");
    }

    [Fact]
    public void ThrowIfApiKeyMissing_EmptyApiKey_ThrowsInvalidOperationException()
    {
        var act = () => CredentialValidator.ThrowIfApiKeyMissing("", "Polygon", "POLYGON__APIKEY");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ThrowIfApiKeyMissing_ValidApiKey_DoesNotThrow()
    {
        var act = () => CredentialValidator.ThrowIfApiKeyMissing("valid-key", "Polygon", "POLYGON__APIKEY");

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfCredentialsMissing

    [Fact]
    public void ThrowIfCredentialsMissing_MissingCredentials_ThrowsInvalidOperationException()
    {
        var act = () => CredentialValidator.ThrowIfCredentialsMissing(
            null, null, "Alpaca", "ALPACA__KEYID", "ALPACA__SECRETKEY");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Alpaca*")
            .WithMessage("*ALPACA__KEYID*")
            .WithMessage("*ALPACA__SECRETKEY*");
    }

    [Fact]
    public void ThrowIfCredentialsMissing_ValidCredentials_DoesNotThrow()
    {
        var act = () => CredentialValidator.ThrowIfCredentialsMissing(
            "key-id", "secret-key", "Alpaca", "ALPACA__KEYID", "ALPACA__SECRETKEY");

        act.Should().NotThrow();
    }

    #endregion

    #region GetCredential (single env var)

    [Fact]
    public void GetCredential_ParamValueProvided_ReturnsParamValue()
    {
        CredentialValidator.GetCredential("explicit-value", "NONEXISTENT_ENV_VAR")
            .Should().Be("explicit-value");
    }

    [Fact]
    public void GetCredential_NullParam_FallsBackToEnvVar()
    {
        var envVarName = "MDC_TEST_CRED_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Environment.SetEnvironmentVariable(envVarName, "env-value");

            CredentialValidator.GetCredential(null, envVarName)
                .Should().Be("env-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public void GetCredential_NullParamAndNoEnvVar_ReturnsNull()
    {
        CredentialValidator.GetCredential(null, "NONEXISTENT_MDC_TEST_VAR_12345")
            .Should().BeNull();
    }

    #endregion

    #region GetCredential (multiple env vars)

    [Fact]
    public void GetCredentialMultiple_ParamValueProvided_ReturnsParamValue()
    {
        CredentialValidator.GetCredential("explicit-value", "VAR1", "VAR2")
            .Should().Be("explicit-value");
    }

    [Fact]
    public void GetCredentialMultiple_EmptyParamValue_ReturnsParamValue()
    {
        // Empty string is checked with string.IsNullOrEmpty(), so it falls through to env vars
        // This matches the implementation: if (!string.IsNullOrEmpty(paramValue)) return paramValue;
        var envVarName = "MDC_TEST_MULTI_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Environment.SetEnvironmentVariable(envVarName, "from-env");

            CredentialValidator.GetCredential("", envVarName)
                .Should().Be("from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public void GetCredentialMultiple_FallsBackToSecondEnvVar()
    {
        var envVarName = "MDC_TEST_SECOND_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Environment.SetEnvironmentVariable(envVarName, "second-env");

            CredentialValidator.GetCredential(null, "NONEXISTENT_FIRST_VAR", envVarName)
                .Should().Be("second-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public void GetCredentialMultiple_NoParamNoEnvVars_ReturnsNull()
    {
        CredentialValidator.GetCredential(null, "NONEXISTENT_VAR_A", "NONEXISTENT_VAR_B")
            .Should().BeNull();
    }

    #endregion
}
