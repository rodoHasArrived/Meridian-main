using System.Collections.ObjectModel;
using System.Linq;

namespace Meridian.Backtesting.Sdk.Strategies.AdvancedCarry;

public enum CarryOptimizationMethod
{
    MeanVariance,
    RiskParity,
    MinimumVariance,
    MaximumSharpe
}

/// <summary>
/// Controls how carry signal is derived in <see cref="CarryTradeBacktestStrategy"/>.
/// </summary>
public enum YieldCarryMode
{
    /// <summary>
    /// Classic carry: expected return = historical price-momentum + assumed carry yield.
    /// </summary>
    ClassicCarry,

    /// <summary>
    /// Yield-spread carry: expected return = asset yield − risk-free rate.
    /// Long high-spread assets; underweight (or skip) low/negative-spread assets.
    /// Best for dividend stocks, bond ETFs, REITs, and FX carry pairs.
    /// </summary>
    YieldSpread,

    /// <summary>
    /// Yield rotation: ranks assets by yield each period; rotates into the top-quartile
    /// yielders and out of the bottom-quartile.  Applies an additional momentum tilt
    /// when an asset's yield has risen (price has compressed) relative to its 20-day average.
    /// </summary>
    YieldRotation,
}

public enum CarryExecutionAlgorithm
{
    Twap,
    Vwap,
    Pov,
    Adaptive,
    Iceberg
}

public enum CarryScenarioType
{
    RateSpike,
    CreditSpreadWidening,
    EquityShock,
    LiquidityCrunch
}

public sealed record AdvancedCarryRiskOptions(
    double MaxSinglePosition = 0.20,
    double? TargetVolatility = 0.10,
    double ConfidenceLevel = 0.95,
    double RateShockBps = 200,
    double CreditSpreadShockBps = 100,
    double EquityShock = -0.10,
    double LiquiditySpreadMultiple = 3.0,
    bool UseKellySizing = false,
    double KellyFraction = 0.25,
    double MaxKellyPosition = 0.10);

public sealed record AdvancedCarryExecutionOptions(
    CarryExecutionAlgorithm Algorithm = CarryExecutionAlgorithm.Twap,
    int DurationMinutes = 30,
    int SliceCount = 10,
    long MinSliceQuantity = 1,
    long MaxSliceQuantity = 500,
    double TargetParticipationRate = 0.10,
    bool UseIcebergOrders = false,
    long IcebergDisplayQuantity = 100);

public sealed record AdvancedCarryConfiguration(
    CarryOptimizationMethod OptimizationMethod = CarryOptimizationMethod.MeanVariance,
    double RiskAversion = 3.0,
    double RiskFreeRate = 0.04,
    AdvancedCarryRiskOptions? Risk = null,
    AdvancedCarryExecutionOptions? Execution = null)
{
    public AdvancedCarryRiskOptions EffectiveRisk { get; init; } = Risk ?? new AdvancedCarryRiskOptions();
    public AdvancedCarryExecutionOptions EffectiveExecution { get; init; } = Execution ?? new AdvancedCarryExecutionOptions();
}

public sealed record CarryAssetSnapshot(
    string Symbol,
    decimal LastPrice,
    double ExpectedAnnualReturn,
    double AnnualCarryYield,
    double AnnualPriceReturn,
    double AnnualVolatility,
    double DurationYears,
    double SpreadDurationYears,
    double AverageDailyVolume,
    double BidAskSpreadBps,
    double MarketBeta = 0.50,
    IReadOnlyList<double>? HistoricalDailyReturns = null)
{
    public CarryAssetSnapshot Normalize()
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Asset symbol is required.", nameof(Symbol));

        if (LastPrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(LastPrice), "Last price must be greater than zero.");

        if (AnnualVolatility < 0)
            throw new ArgumentOutOfRangeException(nameof(AnnualVolatility), "Volatility cannot be negative.");

        return this with { Symbol = Symbol.Trim().ToUpperInvariant() };
    }
}

public sealed record AssetCorrelation(
    string LeftSymbol,
    string RightSymbol,
    double Correlation)
{
    public AssetCorrelation Normalize()
    {
        if (string.IsNullOrWhiteSpace(LeftSymbol) || string.IsNullOrWhiteSpace(RightSymbol))
            throw new ArgumentException("Correlation symbols are required.");

        return this with
        {
            LeftSymbol = LeftSymbol.Trim().ToUpperInvariant(),
            RightSymbol = RightSymbol.Trim().ToUpperInvariant(),
            Correlation = Math.Clamp(Correlation, -1.0, 1.0)
        };
    }
}

public sealed record CarryPortfolioState(
    decimal PortfolioValue,
    IReadOnlyDictionary<string, long>? CurrentQuantities = null)
{
    public IReadOnlyDictionary<string, long> NormalizedQuantities =>
        CurrentQuantities is null
            ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            : new ReadOnlyDictionary<string, long>(
                CurrentQuantities.ToDictionary(
                    pair => pair.Key.Trim().ToUpperInvariant(),
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase));
}

public sealed record AdvancedCarryInput(
    IReadOnlyList<CarryAssetSnapshot> Assets,
    CarryPortfolioState Portfolio,
    IReadOnlyList<AssetCorrelation>? Correlations = null,
    DateTimeOffset? AsOf = null);

public interface ICarryForecastOverlay
{
    CarryAssetSnapshot Apply(CarryAssetSnapshot asset, AdvancedCarryInput input);
}

public sealed record OptimizedTargetWeight(
    string Symbol,
    double Weight,
    double ExpectedReturnContribution,
    double RiskContribution,
    double? KellyCap = null);

public sealed record RebalanceInstruction(
    string Symbol,
    double CurrentWeight,
    double TargetWeight,
    long CurrentQuantity,
    long TargetQuantity,
    long DeltaQuantity,
    decimal EstimatedNotional);

public sealed record ExecutionSlice(
    DateTimeOffset PlannedAt,
    long Quantity,
    decimal? LimitPrice,
    long DisplayQuantity);

public sealed record ExecutionPlan(
    string Symbol,
    CarryExecutionAlgorithm Algorithm,
    long TotalQuantity,
    IReadOnlyList<ExecutionSlice> Slices,
    string Notes);

public sealed record CarryTailRiskEstimate(
    string Method,
    double ConfidenceLevel,
    double ReturnFraction,
    decimal Amount);

public sealed record ScenarioImpact(
    CarryScenarioType Scenario,
    double PortfolioImpactFraction,
    IReadOnlyDictionary<string, double> PerAssetImpactFractions,
    string Narrative);

public sealed record CarryRiskReport(
    double ExpectedAnnualReturn,
    double ExpectedAnnualVolatility,
    double SharpeRatio,
    CarryTailRiskEstimate HistoricalTailRisk,
    CarryTailRiskEstimate ParametricTailRisk,
    double AverageCorrelation,
    IReadOnlyDictionary<string, double> RiskContributions,
    IReadOnlyList<ScenarioImpact> Scenarios);

public sealed record AdvancedCarryDecision(
    DateTimeOffset AsOf,
    double InvestedWeight,
    double CashWeight,
    IReadOnlyList<OptimizedTargetWeight> TargetWeights,
    IReadOnlyList<RebalanceInstruction> RebalanceInstructions,
    IReadOnlyList<ExecutionPlan> ExecutionPlans,
    CarryRiskReport RiskReport,
    IReadOnlyList<string> Diagnostics);
