namespace Meridian.Execution.Exceptions;

/// <summary>
/// Raised when a provider-independent order request cannot be represented by the
/// current gateway implementation. Providers can enrich the message with their own
/// constraints without exposing provider-specific types upstream.
/// </summary>
public sealed class UnsupportedOrderRequestException(string message) : InvalidOperationException(message);
