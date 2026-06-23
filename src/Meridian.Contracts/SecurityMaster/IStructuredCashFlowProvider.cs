namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Port for a structured cash flow provider such as MIAC (agency MBS) or
/// Moody's Analytics (other prepaying/structured products).
/// </summary>
public interface IStructuredCashFlowProvider
{
    string ProviderId { get; }

    Task<StructuredCashFlowProjectionDto?> GetProjectedCashFlowsAsync(
        Guid securityId,
        string? isin,
        DateTimeOffset factorDate,
        StructuredCashFlowScenario scenario,
        CancellationToken ct = default);

    Task<decimal?> GetCurrentFactorAsync(
        Guid securityId,
        string? isin,
        CancellationToken ct = default);
}
