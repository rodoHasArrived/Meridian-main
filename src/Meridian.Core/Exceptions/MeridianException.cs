namespace Meridian.Core.Exceptions;

/// <summary>
/// Base exception for all Meridian-specific exceptions
/// </summary>
public class MeridianException : Exception
{
    public MeridianException()
    {
    }

    public MeridianException(string message) : base(message)
    {
    }

    public MeridianException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
