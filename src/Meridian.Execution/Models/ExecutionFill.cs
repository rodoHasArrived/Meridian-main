namespace Meridian.Execution.Models;

/// <summary>
/// Fill type for a paper trading execution fill.
/// </summary>
public enum FillType : byte
{
    /// <summary>A regular trade fill (buy or sell).</summary>
    Trade = 0,

    /// <summary>Commission or fee deducted from the account.</summary>
    Commission = 1,

    /// <summary>Cash dividend received into the account.</summary>
    Dividend = 2
}

/// <summary>
/// Lightweight paper-trading fill record used for session state tracking and replay verification.
/// Distinct from <see cref="Meridian.Execution.Sdk.ExecutionReport"/> which carries full
/// brokerage execution-report detail.
/// </summary>
public sealed record ExecutionFill
{
    /// <summary>Optional session identifier this fill belongs to.</summary>
    public string? SessionId { get; init; }

    /// <summary>Ticker symbol for the fill.</summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// Signed fill quantity: positive for buys, negative for sells.
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>Price at which the fill was executed.</summary>
    public decimal FillPrice { get; init; }

    /// <summary>Timestamp when the fill occurred.</summary>
    public DateTimeOffset FilledAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Semantic type of the fill.</summary>
    public FillType FillType { get; init; } = FillType.Trade;
}
