namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception thrown when sequence validation fails (gaps, out-of-order data)
/// </summary>
public sealed class SequenceValidationException : MeridianException
{
    public string? Symbol { get; }
    public long? ExpectedSequence { get; }
    public long? ActualSequence { get; }
    public SequenceValidationType ValidationType { get; }

    public SequenceValidationException(string message) : base(message)
    {
    }

    public SequenceValidationException(
        string message,
        string? symbol = null,
        long? expectedSequence = null,
        long? actualSequence = null,
        SequenceValidationType validationType = SequenceValidationType.Unknown)
        : base(message)
    {
        Symbol = symbol;
        ExpectedSequence = expectedSequence;
        ActualSequence = actualSequence;
        ValidationType = validationType;
    }

    public SequenceValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Types of sequence validation failures
/// </summary>
public enum SequenceValidationType : byte
{
    Unknown,
    Gap,
    OutOfOrder,
    Duplicate,
    Reset
}
