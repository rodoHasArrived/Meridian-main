namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// Opening cash balance row joined into the portfolio cash ladder.
/// Amounts are stated in <see cref="Currency"/>; the ladder engine reports
/// non-base-currency balances as an explicit assumption.
/// </summary>
public sealed record PortfolioCashBalanceDto(
    string AccountId,
    string DisplayName,
    decimal Amount,
    string Currency,
    string SourceDomain,
    string? SourceEntityId);

/// <summary>
/// Dated investor or fund-level capital activity joined into the ladder
/// (contributions, redemptions, distributions, expense accruals).
/// The engine derives cash direction from <see cref="ActivityKind"/>;
/// <see cref="Amount"/> is an unsigned magnitude.
/// </summary>
public sealed record PortfolioCapitalActivityDto(
    Guid ActivityId,
    string ActivityKind,
    DateOnly DueDate,
    decimal Amount,
    string Currency,
    string SourceDomain,
    string? SourceEntityId,
    string Summary);

/// <summary>
/// One held position fed into the portfolio cash ladder: the instrument's
/// asset-operations projection plus the held quantity multiplier applied to
/// its projected flows (1.0 means one unit of the retained terms basis).
/// </summary>
public sealed record PortfolioCashLadderPositionDto(
    AssetOperationsDetailDto Operations,
    decimal Quantity = 1m);

/// <summary>
/// A single projected flow contributing to the ladder, traceable back to the
/// instrument projection run and Security Master terms version that produced it.
/// </summary>
public sealed record PortfolioCashLadderContributionDto(
    Guid SecurityId,
    string DisplayName,
    string AssetClass,
    Guid? ProjectionRunId,
    string? ProjectionEngineVersion,
    Guid? TermsVersionId,
    string? TermsHash,
    string SourceDomain,
    string? SourceEntityId,
    string FlowType,
    string SourceLane,
    DateOnly DueDate,
    decimal Amount,
    string Currency,
    decimal BaseAmount,
    string? ScenarioAdjustment);

/// <summary>Per-source subtotal inside one ladder bucket.</summary>
public sealed record PortfolioCashLadderSourceSliceDto(
    string Source,
    decimal Amount,
    int FlowCount);

/// <summary>One dated bucket of the portfolio cash ladder.</summary>
public sealed record PortfolioCashLadderBucketDto(
    DateOnly BucketStart,
    DateOnly BucketEnd,
    string Label,
    decimal Inflows,
    decimal Outflows,
    decimal NetFlow,
    decimal CumulativeCash,
    bool BreachesMinimumBalance,
    IReadOnlyList<PortfolioCashLadderSourceSliceDto> Sources);

/// <summary>
/// A stress scenario the ladder can be re-rendered under. The engine states
/// explicitly what each scenario models and what it merely assumes so operators
/// can judge the output honestly.
/// </summary>
public sealed record PortfolioCashScenarioDto(
    string ScenarioId,
    string DisplayName,
    string Description,
    IReadOnlyList<string> ModeledEffects,
    IReadOnlyList<string> Assumptions);

/// <summary>
/// Portfolio-wide dated cash ladder: instrument obligations aggregated across
/// held positions, joined with opening cash and capital activity, bucketed over
/// the horizon with a cumulative balance line and minimum-balance breaches.
/// </summary>
public sealed record PortfolioCashLadderDto(
    string EngineVersion,
    DateOnly AsOfDate,
    int HorizonDays,
    string BaseCurrency,
    string ScenarioId,
    decimal OpeningCash,
    decimal MinimumCashThreshold,
    IReadOnlyList<PortfolioCashLadderBucketDto> Buckets,
    IReadOnlyList<PortfolioCashLadderContributionDto> Contributions,
    IReadOnlyList<PortfolioCashScenarioDto> AvailableScenarios,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAt,
    int SecuritiesEvaluated,
    int SecuritiesWithFlows);

/// <summary>Query parameters for the portfolio cash ladder read model.</summary>
public sealed record PortfolioCashLadderQuery(
    string? ScenarioId = null,
    int HorizonDays = 90,
    decimal? MinimumCashThreshold = null,
    int BucketDays = 7);

/// <summary>
/// One held position as the cash ladder consumes it: the security identity and
/// the quantity actually held, expressed as a multiple of the retained terms
/// basis the instrument projection is scaled against (1.0 = one unit of face/basis).
/// </summary>
public sealed record PortfolioHoldingDto(
    Guid SecurityId,
    decimal Quantity);

/// <summary>
/// Supplies the securities actually held by the portfolio and the quantity of each,
/// so the ladder forecasts flows only for held instruments and scales them by the
/// real position size rather than a unit-quantity placeholder.
/// </summary>
public interface IPortfolioHoldingsSource
{
    Task<IReadOnlyList<PortfolioHoldingDto>> GetHoldingsAsync(DateOnly asOf, CancellationToken ct = default);
}

/// <summary>Supplies opening cash balances for the portfolio cash ladder.</summary>
public interface IPortfolioCashBalanceProvider
{
    Task<IReadOnlyList<PortfolioCashBalanceDto>> GetCashBalancesAsync(CancellationToken ct = default);
}

/// <summary>Supplies dated capital activity (investor schedule, expense accruals) for the ladder.</summary>
public interface IPortfolioCapitalScheduleProvider
{
    Task<IReadOnlyList<PortfolioCapitalActivityDto>> GetCapitalActivityAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default);
}

/// <summary>Read surface for the portfolio-wide cash ladder.</summary>
public interface IPortfolioCashLadderQueryService
{
    Task<PortfolioCashLadderDto> GetCashLadderAsync(PortfolioCashLadderQuery query, CancellationToken ct = default);
}
