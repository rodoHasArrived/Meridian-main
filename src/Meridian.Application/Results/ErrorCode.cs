namespace Meridian.Application.ResultTypes;

/// <summary>
/// Standardized error codes for the Meridian system.
/// Error codes are grouped by category using numeric ranges for easy filtering and categorization.
/// </summary>
public enum ErrorCode : int
{
    // ========================================
    // General Errors (1000-1099)
    // ========================================

    /// <summary>Unknown or unclassified error</summary>
    Unknown = 1000,

    /// <summary>Internal system error</summary>
    InternalError = 1001,

    /// <summary>Operation was cancelled</summary>
    Cancelled = 1002,

    /// <summary>Operation timed out</summary>
    Timeout = 1003,

    /// <summary>Resource not found</summary>
    NotFound = 1004,

    /// <summary>Operation not supported</summary>
    NotSupported = 1005,

    /// <summary>Invalid operation for current state</summary>
    InvalidOperation = 1006,

    // ========================================
    // Validation Errors (2000-2099)
    // ========================================

    /// <summary>General validation failure</summary>
    ValidationFailed = 2000,

    /// <summary>Required field is missing</summary>
    RequiredFieldMissing = 2001,

    /// <summary>Field value is out of valid range</summary>
    OutOfRange = 2002,

    /// <summary>Invalid format for field value</summary>
    InvalidFormat = 2003,

    /// <summary>Duplicate value where uniqueness is required</summary>
    DuplicateValue = 2004,

    /// <summary>Constraint violation</summary>
    ConstraintViolation = 2005,

    // ========================================
    // Configuration Errors (3000-3099)
    // ========================================

    /// <summary>Configuration is invalid</summary>
    ConfigurationInvalid = 3000,

    /// <summary>Required configuration is missing</summary>
    ConfigurationMissing = 3001,

    /// <summary>Configuration value is out of valid range</summary>
    ConfigurationOutOfRange = 3002,

    /// <summary>API key or credential is missing</summary>
    CredentialsMissing = 3003,

    /// <summary>API key or credential is invalid</summary>
    CredentialsInvalid = 3004,

    // ========================================
    // Connection Errors (4000-4099)
    // ========================================

    /// <summary>Failed to establish connection</summary>
    ConnectionFailed = 4000,

    /// <summary>Connection was lost</summary>
    ConnectionLost = 4001,

    /// <summary>Connection was refused</summary>
    ConnectionRefused = 4002,

    /// <summary>Connection timed out</summary>
    ConnectionTimeout = 4003,

    /// <summary>Authentication failed</summary>
    AuthenticationFailed = 4004,

    /// <summary>SSL/TLS error</summary>
    SslError = 4005,

    /// <summary>DNS resolution failed</summary>
    DnsResolutionFailed = 4006,

    // ========================================
    // Provider Errors (5000-5099)
    // ========================================

    /// <summary>Provider operation failed</summary>
    ProviderError = 5000,

    /// <summary>Provider is not available</summary>
    ProviderUnavailable = 5001,

    /// <summary>Rate limit exceeded</summary>
    RateLimitExceeded = 5002,

    /// <summary>Subscription limit exceeded</summary>
    SubscriptionLimitExceeded = 5003,

    /// <summary>Symbol not found or not supported</summary>
    SymbolNotFound = 5004,

    /// <summary>Provider returned invalid data</summary>
    InvalidProviderData = 5005,

    /// <summary>Provider returned no data</summary>
    NoDataAvailable = 5006,

    /// <summary>Circuit breaker is open</summary>
    CircuitBreakerOpen = 5007,

    // ========================================
    // Data Integrity Errors (6000-6099)
    // ========================================

    /// <summary>Sequence gap detected</summary>
    SequenceGap = 6000,

    /// <summary>Out of order data detected</summary>
    OutOfOrderData = 6001,

    /// <summary>Duplicate data detected</summary>
    DuplicateData = 6002,

    /// <summary>Data corruption detected</summary>
    DataCorruption = 6003,

    /// <summary>Checksum validation failed</summary>
    ChecksumMismatch = 6004,

    /// <summary>Schema version mismatch</summary>
    SchemaMismatch = 6005,

    // ========================================
    // Storage Errors (7000-7099)
    // ========================================

    /// <summary>Storage operation failed</summary>
    StorageError = 7000,

    /// <summary>File not found</summary>
    FileNotFound = 7001,

    /// <summary>File access denied</summary>
    FileAccessDenied = 7002,

    /// <summary>Disk space insufficient</summary>
    InsufficientDiskSpace = 7003,

    /// <summary>Write operation failed</summary>
    WriteFailed = 7004,

    /// <summary>Read operation failed</summary>
    ReadFailed = 7005,

    /// <summary>Compression/decompression failed</summary>
    CompressionFailed = 7006,

    /// <summary>Serialization/deserialization failed</summary>
    SerializationFailed = 7007,

    // ========================================
    // Messaging Errors (8000-8099)
    // ========================================

    /// <summary>Message publish failed</summary>
    PublishFailed = 8000,

    /// <summary>Message consume failed</summary>
    ConsumeFailed = 8001,

    /// <summary>Message deserialization failed</summary>
    MessageDeserializationFailed = 8002,

    /// <summary>Message validation failed</summary>
    MessageValidationFailed = 8003,

    /// <summary>Dead letter queue write failed</summary>
    DeadLetterFailed = 8004,

    /// <summary>Message broker connection failed</summary>
    BrokerConnectionFailed = 8005
}

/// <summary>
/// Extension methods for ErrorCode
/// </summary>
public static class ErrorCodeExtensions
{
    /// <summary>
    /// Gets the category name for an error code
    /// </summary>
    public static string GetCategory(this ErrorCode code)
    {
        return (int)code switch
        {
            >= 1000 and < 2000 => "General",
            >= 2000 and < 3000 => "Validation",
            >= 3000 and < 4000 => "Configuration",
            >= 4000 and < 5000 => "Connection",
            >= 5000 and < 6000 => "Provider",
            >= 6000 and < 7000 => "DataIntegrity",
            >= 7000 and < 8000 => "Storage",
            >= 8000 and < 9000 => "Messaging",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Determines if the error is transient and may succeed if retried
    /// </summary>
    public static bool IsTransient(this ErrorCode code)
    {
        return code switch
        {
            ErrorCode.Timeout => true,
            ErrorCode.ConnectionFailed => true,
            ErrorCode.ConnectionLost => true,
            ErrorCode.ConnectionTimeout => true,
            ErrorCode.RateLimitExceeded => true,
            ErrorCode.ProviderUnavailable => true,
            ErrorCode.CircuitBreakerOpen => true,
            ErrorCode.BrokerConnectionFailed => true,
            _ => false
        };
    }

    /// <summary>
    /// Maps an error code to a process exit code (1-8) based on category.
    /// Exit code 0 is reserved for success (not produced by this method).
    /// </summary>
    public static int ToExitCode(this ErrorCode code)
    {
        return (int)code switch
        {
            >= 1000 and < 2000 => 1,  // General
            >= 2000 and < 3000 => 2,  // Validation
            >= 3000 and < 4000 => 3,  // Configuration
            >= 4000 and < 5000 => 4,  // Connection
            >= 5000 and < 6000 => 5,  // Provider
            >= 6000 and < 7000 => 6,  // Data Integrity
            >= 7000 and < 8000 => 7,  // Storage
            >= 8000 and < 9000 => 8,  // Messaging
            _ => 1
        };
    }

    /// <summary>
    /// Maps a domain exception to the most appropriate <see cref="ErrorCode"/>.
    /// Uses the exception type hierarchy from <c>Meridian.Application.Exceptions</c>
    /// to produce category-accurate exit codes via <see cref="ToExitCode"/>.
    /// </summary>
    public static ErrorCode FromException(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => ErrorCode.Cancelled,
            TimeoutException => ErrorCode.Timeout,

            // Domain exception types (Meridian.Application.Exceptions namespace)
            _ when ex.GetType().Name == "RateLimitException" => ErrorCode.RateLimitExceeded,
            _ when ex.GetType().Name == "ConfigurationException" => ErrorCode.ConfigurationInvalid,
            _ when ex.GetType().Name == "ValidationException" => ErrorCode.ValidationFailed,
            _ when ex.GetType().Name == "ConnectionException" => ErrorCode.ConnectionFailed,
            _ when ex.GetType().Name == "StorageException" => ErrorCode.StorageError,
            _ when ex.GetType().Name == "DataProviderException" => ErrorCode.ProviderError,
            _ when ex.GetType().Name == "SequenceValidationException" => ErrorCode.SequenceGap,
            _ when ex.GetType().Name == "OperationTimeoutException" => ErrorCode.Timeout,

            // Standard .NET exceptions
            UnauthorizedAccessException => ErrorCode.FileAccessDenied,
            IOException => ErrorCode.StorageError,
            System.Text.Json.JsonException => ErrorCode.ConfigurationInvalid,
            ArgumentException => ErrorCode.ValidationFailed,
            NotSupportedException => ErrorCode.NotSupported,
            InvalidOperationException => ErrorCode.InvalidOperation,

            _ => ErrorCode.Unknown
        };
    }

    /// <summary>
    /// Gets the suggested HTTP status code for an error code
    /// </summary>
    public static int ToHttpStatusCode(this ErrorCode code)
    {
        return code switch
        {
            ErrorCode.NotFound => 404,
            ErrorCode.SymbolNotFound => 404,
            ErrorCode.FileNotFound => 404,

            ErrorCode.ValidationFailed => 400,
            ErrorCode.RequiredFieldMissing => 400,
            ErrorCode.OutOfRange => 400,
            ErrorCode.InvalidFormat => 400,
            ErrorCode.InvalidProviderData => 400,
            ErrorCode.MessageValidationFailed => 400,

            ErrorCode.AuthenticationFailed => 401,
            ErrorCode.CredentialsInvalid => 401,

            ErrorCode.FileAccessDenied => 403,
            ErrorCode.SubscriptionLimitExceeded => 403,

            ErrorCode.Timeout => 408,
            ErrorCode.ConnectionTimeout => 408,

            ErrorCode.DuplicateValue => 409,
            ErrorCode.DuplicateData => 409,

            ErrorCode.RateLimitExceeded => 429,

            ErrorCode.InternalError => 500,
            ErrorCode.StorageError => 500,
            ErrorCode.SerializationFailed => 500,

            ErrorCode.ProviderUnavailable => 503,
            ErrorCode.CircuitBreakerOpen => 503,
            ErrorCode.BrokerConnectionFailed => 503,

            ErrorCode.ConnectionFailed => 502,
            ErrorCode.ConnectionLost => 502,
            ErrorCode.ProviderError => 502,

            _ => 500
        };
    }
}
