namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception thrown when there are connection issues with data providers
/// </summary>
public sealed class ConnectionException : MeridianException
{
    public string? Provider { get; }
    public string? Host { get; }
    public int? Port { get; }

    public ConnectionException(string message) : base(message)
    {
    }

    public ConnectionException(string message, string? provider = null, string? host = null, int? port = null)
        : base(message)
    {
        Provider = provider;
        Host = host;
        Port = port;
    }

    public ConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
