namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception thrown when data validation fails
/// </summary>
public sealed class ValidationException : MeridianException
{
    public IReadOnlyList<ValidationError> Errors { get; }
    public string? EntityType { get; }
    public string? EntityId { get; }

    public ValidationException(string message) : base(message)
    {
        Errors = Array.Empty<ValidationError>();
    }

    public ValidationException(
        string message,
        IEnumerable<ValidationError> errors,
        string? entityType = null,
        string? entityId = null)
        : base(message)
    {
        Errors = errors.ToList().AsReadOnly();
        EntityType = entityType;
        EntityId = entityId;
    }

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = Array.Empty<ValidationError>();
    }
}

/// <summary>
/// Represents a single validation error
/// </summary>
public sealed record ValidationError(
    string Code,
    string Message,
    string? Field = null,
    object? AttemptedValue = null);
