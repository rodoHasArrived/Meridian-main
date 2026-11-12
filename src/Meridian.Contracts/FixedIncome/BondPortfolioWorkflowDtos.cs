using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.FixedIncome;

public sealed record BondComparisonRequest(string RunId, BondCalculationContext Context,
    IReadOnlyList<BondWorkflowRequest> Bonds);
public sealed record BondComparisonResult(string RunId, string ModelVersion, BondCalculationContext Context,
    IReadOnlyList<BondWorkflowResult> Bonds, IReadOnlyList<string> Issues, bool IsComplete);
public sealed record BondComparisonSnapshotDto(BondComparisonRequest Request, BondComparisonResult Result);

public sealed record BondPortfolioRiskRequest(BondCalculationContext Context,
    decimal ParallelShiftBps = 100m, decimal SpreadWideningBps = 100m,
    IReadOnlyList<BondWorkflowRatePillar>? SteepeningPillars = null)
{
    public string? RunId { get; init; }
}
public sealed record BondPortfolioRiskPosition(Guid SecurityId, string? Issuer, string Currency,
    DateOnly? Maturity, decimal SignedOriginalFace, decimal? PresentValue, decimal? Dv01, decimal? Cs01,
    IReadOnlyList<BondPortfolioScenarioValue> Scenarios, IReadOnlyList<string> Issues)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BondWorkflowRequest? CalculationRequest { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BondCalculationInput? QuoteAuthority { get; init; }
}
public sealed record BondPortfolioScenarioValue(string Scenario, decimal? PresentValue, decimal? Change);
public sealed record BondPortfolioRiskBucket(string Dimension, string Key, string Currency, int Holdings,
    int CoveredHoldings, decimal KnownPresentValue, decimal KnownDv01, decimal KnownCs01);
public sealed record BondPortfolioRiskResult(AccountCashForecastScopeDto Scope, BondCalculationContext Context,
    Guid? HoldingsBatchId, string? HoldingsContentHash, IReadOnlyList<BondPortfolioRiskPosition> Positions,
    IReadOnlyList<BondPortfolioRiskBucket> Buckets, IReadOnlyList<string> Issues, bool IsComplete)
{
    public string? RunId { get; init; }
}
public sealed record BondPortfolioRiskSnapshotDto(BondPortfolioRiskRequest Request, BondPortfolioRiskResult Result);

public interface IBondComparisonService
{
    Task<BondComparisonResult> CompareAsync(BondComparisonRequest request, CancellationToken ct = default);
}
public interface IBondPortfolioRiskService
{
    Task<BondPortfolioRiskResult> CalculateAsync(AccountCashForecastScopeDto scope, BondPortfolioRiskRequest request,
        CancellationToken ct = default);
}
