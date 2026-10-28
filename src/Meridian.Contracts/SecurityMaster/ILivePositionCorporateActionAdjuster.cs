namespace Meridian.Contracts.SecurityMaster;

/// <summary>Result of applying corporate actions to a live open position.</summary>
public record PositionCorporateActionAdjustment(
    string Ticker,
    decimal OriginalQuantity,
    decimal AdjustedQuantity,
    decimal OriginalCostBasis,
    decimal AdjustedCostBasis,
    int ActionCount)
{
    /// <summary>
    /// Exact remaining carrying basis, when supported. Null identifies a legacy result
    /// supplying only an average unit quote.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? AdjustedTotalCostBasis { get; init; }
}

/// <summary>Adjusts an open position's quantity and carrying basis for corporate actions.</summary>
public interface ILivePositionCorporateActionAdjuster
{
    /// <summary>Returns unchanged values when no applicable action or identity is found.</summary>
    Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker,
        decimal quantity,
        decimal costBasis,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Preserves retained total carrying basis when supported. Legacy implementations retain
    /// their quantity/quote behavior without claiming exact total-basis evidence.
    /// </summary>
    Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker,
        decimal quantity,
        decimal costBasis,
        decimal totalCostBasis,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
        => AdjustPositionAsync(ticker, quantity, costBasis, positionOpenedAt, ct);
}
