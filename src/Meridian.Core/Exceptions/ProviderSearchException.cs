namespace Meridian.Core.Exceptions;

/// <summary>The reason an explicit provider lookup could not establish a complete result.</summary>
public enum ProviderSearchFailure
{
    Unavailable,
    CredentialsUnavailable,
    TransportError,
    MalformedPayload,
    IncompleteResponse,
    Throttled
}

/// <summary>Unavailable search evidence, kept separate from a successful search with no matches.</summary>
public sealed class ProviderSearchException(
    ProviderSearchFailure failure, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public ProviderSearchFailure Failure { get; } = failure;
}
