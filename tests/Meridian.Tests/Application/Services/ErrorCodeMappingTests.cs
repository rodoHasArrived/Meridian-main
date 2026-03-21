using FluentAssertions;
using Meridian.Application.Exceptions;
using Meridian.Application.ResultTypes;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Tests for <see cref="ErrorCodeExtensions.FromException"/> and exit code mapping.
/// Verifies that domain exceptions map to the correct <see cref="ErrorCode"/> and
/// that <see cref="ErrorCodeExtensions.ToExitCode"/> produces category-accurate exit codes.
/// </summary>
public sealed class ErrorCodeMappingTests
{
    // ── FromException: Domain exception mapping ─────────────────────

    [Fact]
    public void FromException_ConfigurationException_ReturnsConfigurationInvalid()
    {
        var ex = new ConfigurationException("bad config", "appsettings.json", "DataSource");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.ConfigurationInvalid);
        code.ToExitCode().Should().Be(3);
    }

    [Fact]
    public void FromException_ConnectionException_ReturnsConnectionFailed()
    {
        var ex = new ConnectionException("cannot connect", "Alpaca", "api.alpaca.markets", 443);
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.ConnectionFailed);
        code.ToExitCode().Should().Be(4);
    }

    [Fact]
    public void FromException_DataProviderException_ReturnsProviderError()
    {
        var ex = new DataProviderException("provider failure", "Polygon", "SPY");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.ProviderError);
        code.ToExitCode().Should().Be(5);
    }

    [Fact]
    public void FromException_RateLimitException_ReturnsRateLimitExceeded()
    {
        var ex = new RateLimitException("rate limit hit", "Alpaca", "SPY");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.RateLimitExceeded);
        code.ToExitCode().Should().Be(5);
    }

    [Fact]
    public void FromException_StorageException_ReturnsStorageError()
    {
        var ex = new StorageException("disk write failed", "/data/live");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.StorageError);
        code.ToExitCode().Should().Be(7);
    }

    [Fact]
    public void FromException_ValidationException_ReturnsValidationFailed()
    {
        var ex = new ValidationException("invalid field",
            new[] { new ValidationError("FIELD_REQUIRED", "Symbol is required", "Symbol") });
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.ValidationFailed);
        code.ToExitCode().Should().Be(2);
    }

    [Fact]
    public void FromException_SequenceValidationException_ReturnsSequenceGap()
    {
        var ex = new SequenceValidationException("gap detected", "SPY", 5, 3);
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.SequenceGap);
        code.ToExitCode().Should().Be(6);
    }

    [Fact]
    public void FromException_OperationTimeoutException_ReturnsTimeout()
    {
        var ex = new OperationTimeoutException("connect timed out", "ConnectAsync", TimeSpan.FromSeconds(30));
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.Timeout);
        code.ToExitCode().Should().Be(1);
    }

    // ── FromException: Standard .NET exceptions ─────────────────────

    [Fact]
    public void FromException_OperationCanceledException_ReturnsCancelled()
    {
        var ex = new OperationCanceledException();
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.Cancelled);
        code.ToExitCode().Should().Be(1);
    }

    [Fact]
    public void FromException_TimeoutException_ReturnsTimeout()
    {
        var ex = new TimeoutException("connection timed out");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.Timeout);
        code.ToExitCode().Should().Be(1);
    }

    [Fact]
    public void FromException_UnauthorizedAccessException_ReturnsFileAccessDenied()
    {
        var ex = new UnauthorizedAccessException("permission denied");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.FileAccessDenied);
        code.ToExitCode().Should().Be(7);
    }

    [Fact]
    public void FromException_IOException_ReturnsStorageError()
    {
        var ex = new IOException("disk full");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.StorageError);
        code.ToExitCode().Should().Be(7);
    }

    [Fact]
    public void FromException_JsonException_ReturnsConfigurationInvalid()
    {
        var ex = new System.Text.Json.JsonException("invalid json");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.ConfigurationInvalid);
        code.ToExitCode().Should().Be(3);
    }

    [Fact]
    public void FromException_ArgumentException_ReturnsValidationFailed()
    {
        var ex = new ArgumentException("bad argument");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.ValidationFailed);
        code.ToExitCode().Should().Be(2);
    }

    [Fact]
    public void FromException_NotSupportedException_ReturnsNotSupported()
    {
        var ex = new NotSupportedException("not supported");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.NotSupported);
        code.ToExitCode().Should().Be(1);
    }

    [Fact]
    public void FromException_UnknownException_ReturnsUnknown()
    {
        var ex = new Exception("something unexpected");
        var code = ErrorCodeExtensions.FromException(ex);

        code.Should().Be(ErrorCode.Unknown);
        code.ToExitCode().Should().Be(1);
    }

    // ── ToExitCode: Category ranges ─────────────────────────────────

    [Theory]
    [InlineData(ErrorCode.Unknown, 1)]        // General
    [InlineData(ErrorCode.InternalError, 1)]   // General
    [InlineData(ErrorCode.Cancelled, 1)]       // General
    [InlineData(ErrorCode.ValidationFailed, 2)] // Validation
    [InlineData(ErrorCode.RequiredFieldMissing, 2)]
    [InlineData(ErrorCode.ConfigurationInvalid, 3)] // Configuration
    [InlineData(ErrorCode.CredentialsMissing, 3)]
    [InlineData(ErrorCode.ConnectionFailed, 4)] // Connection
    [InlineData(ErrorCode.ConnectionTimeout, 4)]
    [InlineData(ErrorCode.ProviderError, 5)]    // Provider
    [InlineData(ErrorCode.RateLimitExceeded, 5)]
    [InlineData(ErrorCode.SequenceGap, 6)]      // Data Integrity
    [InlineData(ErrorCode.SchemaMismatch, 6)]
    [InlineData(ErrorCode.StorageError, 7)]     // Storage
    [InlineData(ErrorCode.FileAccessDenied, 7)]
    [InlineData(ErrorCode.PublishFailed, 8)]    // Messaging
    [InlineData(ErrorCode.BrokerConnectionFailed, 8)]
    public void ToExitCode_MapsToCorrectCategory(ErrorCode code, int expectedExitCode)
    {
        code.ToExitCode().Should().Be(expectedExitCode);
    }

    // ── GetCategory: Category names ─────────────────────────────────

    [Theory]
    [InlineData(ErrorCode.Unknown, "General")]
    [InlineData(ErrorCode.ValidationFailed, "Validation")]
    [InlineData(ErrorCode.ConfigurationInvalid, "Configuration")]
    [InlineData(ErrorCode.ConnectionFailed, "Connection")]
    [InlineData(ErrorCode.ProviderError, "Provider")]
    [InlineData(ErrorCode.SequenceGap, "DataIntegrity")]
    [InlineData(ErrorCode.StorageError, "Storage")]
    [InlineData(ErrorCode.PublishFailed, "Messaging")]
    public void GetCategory_ReturnsCorrectName(ErrorCode code, string expectedCategory)
    {
        code.GetCategory().Should().Be(expectedCategory);
    }

    // ── IsTransient: Retryable errors ───────────────────────────────

    [Theory]
    [InlineData(ErrorCode.Timeout, true)]
    [InlineData(ErrorCode.ConnectionFailed, true)]
    [InlineData(ErrorCode.RateLimitExceeded, true)]
    [InlineData(ErrorCode.CircuitBreakerOpen, true)]
    [InlineData(ErrorCode.ConfigurationInvalid, false)]
    [InlineData(ErrorCode.ValidationFailed, false)]
    [InlineData(ErrorCode.FileAccessDenied, false)]
    public void IsTransient_IdentifiesRetryableErrors(ErrorCode code, bool expectedTransient)
    {
        code.IsTransient().Should().Be(expectedTransient);
    }
}
