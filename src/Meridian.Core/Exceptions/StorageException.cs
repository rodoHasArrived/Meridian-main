namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception thrown when there are storage/file system errors
/// </summary>
public sealed class StorageException : MeridianException
{
    public string? Path { get; }

    public StorageException(string message) : base(message)
    {
    }

    public StorageException(string message, string? path = null)
        : base(message)
    {
        Path = path;
    }

    public StorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
