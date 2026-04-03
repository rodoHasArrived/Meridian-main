using System.Collections.ObjectModel;
using System.Linq;

namespace Meridian.Backtesting.Sdk.Strategies.AdvancedCarry;

public sealed partial class AdvancedCarryDecisionEngine
{
    private const double TradingDaysPerYear = 252.0;
    private readonly ICarryForecastOverlay? _forecastOverlay;

    public AdvancedCarryDecisionEngine(ICarryForecastOverlay? forecastOverlay = null)
    {
        _forecastOverlay = forecastOverlay;
    }

    public AdvancedCarryDecision BuildDecision(
        AdvancedCarryInput input,
        AdvancedCarryConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);

        var risk = configuration.EffectiveRisk;
        var execution = configuration.EffectiveExecution;
        var diagnostics = new List<string>();
        var asOf = input.AsOf ?? DateTimeOffset.UtcNow;

        var assets = input.Assets
            .Select(asset => _forecastOverlay?.Apply(asset.Normalize(), input) ?? asset.Normalize())
            .OrderBy(asset => asset.Symbol, StringComparer.Ordinal)
            .ToList();

        if (assets.Count == 0)
            throw new ArgumentException("At least one carry asset is required.", nameof(input));

        if (assets.Select(asset => asset.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assets.Count)
            throw new ArgumentException("Asset symbols must be unique.", nameof(input));

        if (risk.MaxSinglePosition <= 0 || risk.MaxSinglePosition > 1.0)
            throw new ArgumentOutOfRangeException(nameof(configuration), "Max single position must be between 0 and 1.");

        if (risk.MaxSinglePosition * assets.Count < 1.0)
            throw new InvalidOperationException("Max single position is too restrictive for the number of assets in the universe.");

        var covariance = BuildCovarianceMatrix(assets, input.Correlations, diagnostics);
        var expectedReturns = assets.Select(asset => asset.ExpectedAnnualReturn).ToArray();
        var baseWeights = Optimize(configuration.OptimizationMethod, expectedReturns, covariance, configuration);
        var sizedWeights = ApplyRiskSizing(baseWeights, expectedReturns, assets, covariance, configuration, diagnostics, out var cashWeight);

        var targetWeights = BuildTargetWeights(assets, sizedWeights, covariance, expectedReturns, risk);
        var riskReport = BuildRiskReport(assets, sizedWeights, covariance, configuration, input.Portfolio.PortfolioValue);
        var rebalance = BuildRebalanceInstructions(assets, input.Portfolio, targetWeights);
        var executionPlans = BuildExecutionPlans(rebalance, assets, execution, asOf);

        diagnostics.Add($"Optimisation method: {configuration.OptimizationMethod}.");
        diagnostics.Add($"Execution algorithm: {execution.Algorithm}.");
        diagnostics.Add($"Historical VaR method: {riskReport.HistoricalTailRisk.Method}.");

        return new AdvancedCarryDecision(
            asOf,
            targetWeights.Sum(weight => weight.Weight),
            cashWeight,
            targetWeights,
            rebalance,
            executionPlans,
            riskReport,
            diagnostics.AsReadOnly());
    }

    private static double[] Optimize(
        CarryOptimizationMethod method,
        double[] expectedReturns,
        double[,] covariance,
        AdvancedCarryConfiguration configuration)
    {
        return method switch
        {
            CarryOptimizationMethod.MeanVariance => OptimizeMeanVariance(expectedReturns, covariance, configuration.EffectiveRisk.MaxSinglePosition, configuration.RiskAversion),
            CarryOptimizationMethod.RiskParity => OptimizeRiskParity(covariance, configuration.EffectiveRisk.MaxSinglePosition),
            CarryOptimizationMethod.MinimumVariance => OptimizeMinimumVariance(covariance, configuration.EffectiveRisk.MaxSinglePosition),
            CarryOptimizationMethod.MaximumSharpe => OptimizeMaximumSharpe(expectedReturns, covariance, configuration.EffectiveRisk.MaxSinglePosition, configuration.RiskFreeRate),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    private static double[] OptimizeMeanVariance(double[] expectedReturns, double[,] covariance, double maxWeight, double riskAversion)
    {
        var weights = Enumerable.Repeat(1.0 / expectedReturns.Length, expectedReturns.Length).ToArray();

        for (var iteration = 0; iteration < 300; iteration++)
        {
            var sigmaTimesWeights = Multiply(covariance, weights);
            var gradient = new double[weights.Length];
            for (var i = 0; i < gradient.Length; i++)
                gradient[i] = expectedReturns[i] - riskAversion * sigmaTimesWeights[i];

            var step = 0.12 / Math.Sqrt(iteration + 1);
            for (var i = 0; i < weights.Length; i++)
                weights[i] = Math.Max(0.0, weights[i] + (step * gradient[i]));

            weights = NormalizeWithCap(weights, maxWeight);
        }

        return weights;
    }

    private static double[] OptimizeMinimumVariance(double[,] covariance, double maxWeight)
    {
        var count = covariance.GetLength(0);
        var weights = Enumerable.Repeat(1.0 / count, count).ToArray();

        for (var iteration = 0; iteration < 300; iteration++)
        {
            var sigmaTimesWeights = Multiply(covariance, weights);
            var step = 0.18 / Math.Sqrt(iteration + 1);
            for (var i = 0; i < count; i++)
                weights[i] = Math.Max(0.0, weights[i] - (step * sigmaTimesWeights[i]));

            weights = NormalizeWithCap(weights, maxWeight);
        }

        return weights;
    }

    private static double[] OptimizeRiskParity(double[,] covariance, double maxWeight)
    {
        var count = covariance.GetLength(0);
        var weights = Enumerable.Repeat(1.0 / count, count).ToArray();

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var sigmaTimesWeights = Multiply(covariance, weights);
            var portfolioVariance = Dot(weights, sigmaTimesWeights);
            var targetContribution = portfolioVariance / count;

            for (var i = 0; i < count; i++)
            {
                var currentContribution = Math.Max(1e-10, weights[i] * sigmaTimesWeights[i]);
                var adjustment = Math.Sqrt(targetContribution / currentContribution);
                weights[i] = Math.Max(0.0, weights[i] * adjustment);
            }

            weights = NormalizeWithCap(weights, maxWeight);
        }

        return weights;
    }

    private static double[] OptimizeMaximumSharpe(double[] expectedReturns, double[,] covariance, double maxWeight, double riskFreeRate)
    {
        var count = expectedReturns.Length;
        var weights = Enumerable.Repeat(1.0 / count, count).ToArray();
        var excessReturns = expectedReturns.Select(value => value - riskFreeRate).ToArray();

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var sigmaTimesWeights = Multiply(covariance, weights);
            var variance = Math.Max(1e-10, Dot(weights, sigmaTimesWeights));
            var volatility = Math.Sqrt(variance);
            var numerator = Dot(excessReturns, weights);
            var denominatorCubed = volatility * variance;
            var step = 0.10 / Math.Sqrt(iteration + 1);

            for (var i = 0; i < count; i++)
            {
                var gradient = (excessReturns[i] / volatility) - ((numerator * sigmaTimesWeights[i]) / denominatorCubed);
                weights[i] = Math.Max(0.0, weights[i] + (step * gradient));
            }

            weights = NormalizeWithCap(weights, maxWeight);
        }

        return weights;
    }

    private static double[] ApplyRiskSizing(
        double[] weights,
        double[] expectedReturns,
        IReadOnlyList<CarryAssetSnapshot> assets,
        double[,] covariance,
        AdvancedCarryConfiguration configuration,
        List<string> diagnostics,
        out double cashWeight)
    {
        var sized = weights.ToArray();
        var risk = configuration.EffectiveRisk;

        if (risk.UseKellySizing)
        {
            for (var i = 0; i < sized.Length; i++)
            {
                var kellyCap = CalculateKellyCap(expectedReturns[i], assets[i].AnnualVolatility, risk);
                sized[i] = Math.Min(sized[i], kellyCap);
            }

            var investedWeight = sized.Sum();
            if (investedWeight > 1.0)
            {
                for (var i = 0; i < sized.Length; i++)
                    sized[i] /= investedWeight;
            }

            diagnostics.Add("Kelly sizing applied as a per-position cap.");
        }

        var portfolioVolatility = Math.Sqrt(Math.Max(0.0, Dot(sized, Multiply(covariance, sized))));
        if (risk.TargetVolatility is > 0 && portfolioVolatility > risk.TargetVolatility.Value && portfolioVolatility > 0)
        {
            var scale = risk.TargetVolatility.Value / portfolioVolatility;
            for (var i = 0; i < sized.Length; i++)
                sized[i] *= scale;

            diagnostics.Add($"Risky sleeve scaled to target volatility {risk.TargetVolatility:P2}.");
        }

        cashWeight = Math.Max(0.0, 1.0 - sized.Sum());
        return sized;
    }

    private static IReadOnlyList<OptimizedTargetWeight> BuildTargetWeights(
        IReadOnlyList<CarryAssetSnapshot> assets,
        double[] weights,
        double[,] covariance,
        double[] expectedReturns,
        AdvancedCarryRiskOptions risk)
    {
        var sigmaTimesWeights = Multiply(covariance, weights);
        var portfolioVariance = Math.Max(1e-10, Dot(weights, sigmaTimesWeights));
        var targets = new List<OptimizedTargetWeight>(assets.Count);

        for (var i = 0; i < assets.Count; i++)
        {
            var riskContribution = weights[i] <= 0
                ? 0.0
                : (weights[i] * sigmaTimesWeights[i]) / portfolioVariance;

            targets.Add(new OptimizedTargetWeight(
                assets[i].Symbol,
                weights[i],
                weights[i] * expectedReturns[i],
                riskContribution,
                risk.UseKellySizing ? CalculateKellyCap(expectedReturns[i], assets[i].AnnualVolatility, risk) : null));
        }

        return targets
            .OrderByDescending(weight => weight.Weight)
            .ToList()
            .AsReadOnly();
    }

    private static CarryRiskReport BuildRiskReport(
        IReadOnlyList<CarryAssetSnapshot> assets,
        double[] weights,
        double[,] covariance,
        AdvancedCarryConfiguration configuration,
        decimal portfolioValue)
    {
        var sigmaTimesWeights = Multiply(covariance, weights);
        var annualVariance = Math.Max(1e-10, Dot(weights, sigmaTimesWeights));
        var annualVolatility = Math.Sqrt(annualVariance);
        var expectedReturn = Dot(weights, assets.Select(asset => asset.ExpectedAnnualReturn).ToArray());
        var sharpe = annualVolatility <= 0
            ? 0.0
            : (expectedReturn - configuration.RiskFreeRate) / annualVolatility;

        var riskContributions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < assets.Count; i++)
        {
            riskContributions[assets[i].Symbol] = weights[i] <= 0
                ? 0.0
                : (weights[i] * sigmaTimesWeights[i]) / annualVariance;
        }

        var dailyVolatility = annualVolatility / Math.Sqrt(TradingDaysPerYear);
        var zScore = configuration.EffectiveRisk.ConfidenceLevel switch
        {
            >= 0.99 => 2.326347874,
            >= 0.975 => 1.959963985,
            _ => 1.644853627
        };

        var parametricVarFraction = Math.Max(0.0, zScore * dailyVolatility);
        var parametricCvarFraction = Math.Max(0.0, dailyVolatility * StandardNormalPdf(zScore) / (1.0 - configuration.EffectiveRisk.ConfidenceLevel));

        var historicalReturns = BuildHistoricalPortfolioReturns(assets, weights);
        var historicalTailRisk = historicalReturns.Count >= 10
            ? BuildHistoricalTailRisk(historicalReturns, configuration.EffectiveRisk.ConfidenceLevel, portfolioValue)
            : new CarryTailRiskEstimate("Parametric fallback", configuration.EffectiveRisk.ConfidenceLevel, parametricVarFraction, decimal.Round(portfolioValue * (decimal)parametricVarFraction, 2));

        var parametricTailRisk = new CarryTailRiskEstimate(
            "Parametric Gaussian",
            configuration.EffectiveRisk.ConfidenceLevel,
            parametricCvarFraction,
            decimal.Round(portfolioValue * (decimal)parametricCvarFraction, 2));

        var averageCorrelation = ComputeAverageCorrelation(assets, covariance);
        var scenarios = BuildScenarioImpacts(assets, weights, configuration.EffectiveRisk);

        return new CarryRiskReport(
            expectedReturn,
            annualVolatility,
            sharpe,
            historicalTailRisk,
            parametricTailRisk,
            averageCorrelation,
            new ReadOnlyDictionary<string, double>(riskContributions),
            scenarios.AsReadOnly());
    }

    private static IReadOnlyList<RebalanceInstruction> BuildRebalanceInstructions(
        IReadOnlyList<CarryAssetSnapshot> assets,
        CarryPortfolioState portfolio,
        IReadOnlyList<OptimizedTargetWeight> targetWeights)
    {
        var bySymbol = assets.ToDictionary(asset => asset.Symbol, asset => asset, StringComparer.OrdinalIgnoreCase);
        var currentQuantities = portfolio.NormalizedQuantities;
        var instructions = new List<RebalanceInstruction>(targetWeights.Count);

        foreach (var target in targetWeights)
        {
            var asset = bySymbol[target.Symbol];
            var currentQuantity = currentQuantities.GetValueOrDefault(target.Symbol);
            var currentValue = currentQuantity * asset.LastPrice;
            var currentWeight = portfolio.PortfolioValue <= 0m ? 0.0 : (double)(currentValue / portfolio.PortfolioValue);
            var targetValue = portfolio.PortfolioValue * (decimal)target.Weight;
            var targetQuantity = asset.LastPrice == 0m ? 0L : (long)Math.Round(targetValue / asset.LastPrice, MidpointRounding.AwayFromZero);
            var deltaQuantity = targetQuantity - currentQuantity;

            instructions.Add(new RebalanceInstruction(
                target.Symbol,
                currentWeight,
                target.Weight,
                currentQuantity,
                targetQuantity,
                deltaQuantity,
                deltaQuantity * asset.LastPrice));
        }

        return instructions
            .Where(instruction => instruction.DeltaQuantity != 0)
            .OrderByDescending(instruction => Math.Abs(instruction.EstimatedNotional))
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<ExecutionPlan> BuildExecutionPlans(
        IReadOnlyList<RebalanceInstruction> rebalance,
        IReadOnlyList<CarryAssetSnapshot> assets,
        AdvancedCarryExecutionOptions execution,
        DateTimeOffset asOf)
    {
        var bySymbol = assets.ToDictionary(asset => asset.Symbol, asset => asset, StringComparer.OrdinalIgnoreCase);
        var plans = new List<ExecutionPlan>(rebalance.Count);

        foreach (var instruction in rebalance)
        {
            var asset = bySymbol[instruction.Symbol];
            var algorithm = SelectExecutionAlgorithm(asset, execution);
            var slices = algorithm switch
            {
                CarryExecutionAlgorithm.Twap => BuildTwapSlices(instruction, asset, execution, asOf),
                CarryExecutionAlgorithm.Vwap => BuildVwapSlices(instruction, asset, execution, asOf),
                CarryExecutionAlgorithm.Pov => BuildPovSlices(instruction, asset, execution, asOf),
                CarryExecutionAlgorithm.Iceberg => BuildTwapSlices(instruction, asset, execution with { UseIcebergOrders = true }, asOf),
                CarryExecutionAlgorithm.Adaptive => BuildAdaptiveSlices(instruction, asset, execution, asOf),
                _ => throw new ArgumentOutOfRangeException()
            };

            var notes = algorithm switch
            {
                CarryExecutionAlgorithm.Adaptive => "Adaptive routing selected slices based on spread and liquidity.",
                CarryExecutionAlgorithm.Pov => "POV schedule capped by target participation against average daily volume.",
                CarryExecutionAlgorithm.Iceberg => "Iceberg display size applied to all visible slices.",
                _ => $"{algorithm} schedule generated from the configured rebalance horizon."
            };

            plans.Add(new ExecutionPlan(instruction.Symbol, algorithm, instruction.DeltaQuantity, slices, notes));
        }

        return plans.AsReadOnly();
    }

    private static IReadOnlyList<ExecutionSlice> BuildTwapSlices(
        RebalanceInstruction instruction,
        CarryAssetSnapshot asset,
        AdvancedCarryExecutionOptions execution,
        DateTimeOffset asOf)
    {
        var sliceCount = Math.Max(1, execution.SliceCount);
        var signedQuantities = SplitSignedQuantity(instruction.DeltaQuantity, sliceCount, execution.MinSliceQuantity, execution.MaxSliceQuantity);
        return BuildSlicesFromQuantities(signedQuantities, asset, execution, asOf, execution.DurationMinutes);
    }

    private static IReadOnlyList<ExecutionSlice> BuildVwapSlices(
        RebalanceInstruction instruction,
        CarryAssetSnapshot asset,
        AdvancedCarryExecutionOptions execution,
        DateTimeOffset asOf)
    {
        var sliceCount = Math.Max(3, execution.SliceCount);
        var center = (sliceCount - 1) / 2.0;
        var weights = new double[sliceCount];
        for (var i = 0; i < sliceCount; i++)
        {
            var distance = Math.Abs(i - center) / Math.Max(1.0, center);
            weights[i] = 1.0 + (0.6 * distance * distance);
        }

        var signedQuantities = SplitSignedQuantityByWeights(instruction.DeltaQuantity, weights, execution.MinSliceQuantity, execution.MaxSliceQuantity);
        return BuildSlicesFromQuantities(signedQuantities, asset, execution, asOf, execution.DurationMinutes);
    }

    private static IReadOnlyList<ExecutionSlice> BuildPovSlices(
        RebalanceInstruction instruction,
        CarryAssetSnapshot asset,
        AdvancedCarryExecutionOptions execution,
        DateTimeOffset asOf)
    {
        var bucketMinutes = 5;
        var bucketCount = Math.Max(1, execution.DurationMinutes / bucketMinutes);
        var quantity = Math.Abs(instruction.DeltaQuantity);
        var perBucketCapacity = Math.Max(
            execution.MinSliceQuantity,
            (long)Math.Floor(asset.AverageDailyVolume * execution.TargetParticipationRate * (bucketMinutes / 390.0)));

        var slices = new List<long>();
        var remaining = quantity;
        while (remaining > 0)
        {
            var next = Math.Min(remaining, Math.Min(execution.MaxSliceQuantity, perBucketCapacity));
            if (next <= 0)
                next = Math.Min(remaining, execution.MaxSliceQuantity);

            slices.Add(Math.Sign(instruction.DeltaQuantity) * next);
            remaining -= next;
        }

        while (slices.Count < bucketCount)
            slices.Add(0);

        return BuildSlicesFromQuantities(slices, asset, execution, asOf, bucketMinutes * Math.Max(1, slices.Count));
    }

    private static IReadOnlyList<ExecutionSlice> BuildAdaptiveSlices(
        RebalanceInstruction instruction,
        CarryAssetSnapshot asset,
        AdvancedCarryExecutionOptions execution,
        DateTimeOffset asOf)
    {
        var adaptedAlgorithm = SelectAdaptiveSubAlgorithm(asset);
        return adaptedAlgorithm switch
        {
            CarryExecutionAlgorithm.Twap => BuildTwapSlices(instruction, asset, execution, asOf),
            CarryExecutionAlgorithm.Vwap => BuildVwapSlices(instruction, asset, execution, asOf),
            CarryExecutionAlgorithm.Pov => BuildPovSlices(instruction, asset, execution, asOf),
            _ => BuildTwapSlices(instruction, asset, execution, asOf)
        };
    }

    private static CarryExecutionAlgorithm SelectExecutionAlgorithm(CarryAssetSnapshot asset, AdvancedCarryExecutionOptions execution)
    {
        if (execution.Algorithm != CarryExecutionAlgorithm.Adaptive)
            return execution.Algorithm;

        return SelectAdaptiveSubAlgorithm(asset);
    }

    private static CarryExecutionAlgorithm SelectAdaptiveSubAlgorithm(CarryAssetSnapshot asset)
    {
        if (asset.BidAskSpreadBps > 30 || asset.AverageDailyVolume < 75_000)
            return CarryExecutionAlgorithm.Pov;

        if (asset.BidAskSpreadBps > 10 || asset.AnnualVolatility > 0.18)
            return CarryExecutionAlgorithm.Vwap;

        return CarryExecutionAlgorithm.Twap;
    }

    private static IReadOnlyList<ExecutionSlice> BuildSlicesFromQuantities(
        IReadOnlyList<long> signedQuantities,
        CarryAssetSnapshot asset,
        AdvancedCarryExecutionOptions execution,
        DateTimeOffset asOf,
        int durationMinutes)
    {
        var nonZeroCount = Math.Max(1, signedQuantities.Count(quantity => quantity != 0));
        var stepMinutes = Math.Max(1, durationMinutes / Math.Max(1, nonZeroCount));
        var slices = new List<ExecutionSlice>(signedQuantities.Count);
        var emitted = 0;

        foreach (var quantity in signedQuantities)
        {
            if (quantity == 0)
                continue;

            var displayQuantity = execution.UseIcebergOrders || execution.Algorithm == CarryExecutionAlgorithm.Iceberg
                ? Math.Min(Math.Abs(quantity), execution.IcebergDisplayQuantity)
                : Math.Abs(quantity);

            var limitPrice = EstimateLimitPrice(asset, quantity);
            slices.Add(new ExecutionSlice(
                asOf.AddMinutes(stepMinutes * emitted),
                quantity,
                limitPrice,
                displayQuantity));
            emitted++;
        }

        return slices.AsReadOnly();
    }

    private static decimal? EstimateLimitPrice(CarryAssetSnapshot asset, long quantity)
    {
        if (asset.BidAskSpreadBps <= 0)
            return null;

        var halfSpreadFraction = (decimal)(asset.BidAskSpreadBps / 20_000.0);
        return quantity > 0
            ? decimal.Round(asset.LastPrice * (1m + halfSpreadFraction), 4)
            : decimal.Round(asset.LastPrice * (1m - halfSpreadFraction), 4);
    }

    private static IReadOnlyList<long> SplitSignedQuantity(long signedQuantity, int sliceCount, long minSlice, long maxSlice)
    {
        var sign = Math.Sign(signedQuantity);
        var total = Math.Abs(signedQuantity);
        var quantities = new long[sliceCount];
        var baseQuantity = total / sliceCount;
        var remainder = total % sliceCount;

        for (var i = 0; i < sliceCount; i++)
        {
            var quantity = baseQuantity + (i < remainder ? 1 : 0);
            quantities[i] = sign * quantity;
        }

        return EnforceSliceBounds(quantities, total, minSlice, maxSlice, sign);
    }

    private static IReadOnlyList<long> SplitSignedQuantityByWeights(long signedQuantity, IReadOnlyList<double> weights, long minSlice, long maxSlice)
    {
        var sign = Math.Sign(signedQuantity);
        var total = Math.Abs(signedQuantity);
        var weightTotal = weights.Sum();
        var quantities = new long[weights.Count];
        var assigned = 0L;

        for (var i = 0; i < weights.Count; i++)
        {
            var raw = (i == weights.Count - 1)
                ? total - assigned
                : (long)Math.Round(total * (weights[i] / weightTotal), MidpointRounding.AwayFromZero);

            raw = Math.Max(0, raw);
            quantities[i] = sign * raw;
            assigned += raw;
        }

        return EnforceSliceBounds(quantities, total, minSlice, maxSlice, sign);
    }

    private static IReadOnlyList<long> EnforceSliceBounds(IReadOnlyList<long> rawQuantities, long totalAbsolute, long minSlice, long maxSlice, int sign)
    {
        var bounded = rawQuantities
            .Select(quantity => Math.Min(maxSlice, Math.Abs(quantity)))
            .Where(quantity => quantity > 0)
            .ToList();

        if (bounded.Count == 0)
            bounded.Add(Math.Min(maxSlice, Math.Max(minSlice, totalAbsolute)));

        var sum = bounded.Sum();
        if (sum < totalAbsolute)
            bounded[^1] += totalAbsolute - sum;
        else if (sum > totalAbsolute)
            bounded[^1] -= sum - totalAbsolute;

        if (bounded[^1] <= 0)
            bounded[^1] = Math.Min(maxSlice, Math.Max(minSlice, totalAbsolute));

        return bounded.Select(quantity => sign * quantity).ToList().AsReadOnly();
    }

    private static double CalculateKellyCap(double expectedReturn, double volatility, AdvancedCarryRiskOptions risk)
    {
        if (volatility <= 0 || expectedReturn <= 0)
            return 0.0;

        var winProbability = Math.Clamp(0.5 + (expectedReturn / Math.Max(0.01, 2.0 * volatility)), 0.0, 0.999);
        var odds = Math.Max(0.01, expectedReturn / 0.01);
        var fullKelly = ((odds * winProbability) - (1.0 - winProbability)) / odds;
        return Math.Clamp(fullKelly * risk.KellyFraction, 0.0, risk.MaxKellyPosition);
    }

    private static double[,] BuildCovarianceMatrix(
        IReadOnlyList<CarryAssetSnapshot> assets,
        IReadOnlyList<AssetCorrelation>? correlations,
        List<string> diagnostics)
    {
        var count = assets.Count;
        var matrix = new double[count, count];
        var correlationLookup = BuildCorrelationLookup(correlations);

        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                if (i == j)
                {
                    matrix[i, j] = assets[i].AnnualVolatility * assets[i].AnnualVolatility;
                    continue;
                }

                var correlation = ResolveCorrelation(assets[i], assets[j], correlationLookup);
                matrix[i, j] = correlation * assets[i].AnnualVolatility * assets[j].AnnualVolatility;
            }
        }

        if (correlations is null || correlations.Count == 0)
            diagnostics.Add("Covariance matrix built from historical-return correlations where available; missing pairs defaulted to zero correlation.");

        return matrix;
    }

    private static Dictionary<(string Left, string Right), double> BuildCorrelationLookup(IReadOnlyList<AssetCorrelation>? correlations)
    {
        var lookup = new Dictionary<(string Left, string Right), double>();
        if (correlations is null)
            return lookup;

        foreach (var correlation in correlations.Select(item => item.Normalize()))
        {
            lookup[(correlation.LeftSymbol, correlation.RightSymbol)] = correlation.Correlation;
            lookup[(correlation.RightSymbol, correlation.LeftSymbol)] = correlation.Correlation;
        }

        return lookup;
    }

    private static double ResolveCorrelation(
        CarryAssetSnapshot left,
        CarryAssetSnapshot right,
        IReadOnlyDictionary<(string Left, string Right), double> explicitCorrelations)
    {
        if (explicitCorrelations.TryGetValue((left.Symbol, right.Symbol), out var correlation))
            return correlation;

        if (left.HistoricalDailyReturns is { Count: > 4 } leftHistory &&
            right.HistoricalDailyReturns is { Count: > 4 } rightHistory)
        {
            return ComputeCorrelation(leftHistory, rightHistory);
        }

        return 0.0;
    }

    private static double ComputeAverageCorrelation(IReadOnlyList<CarryAssetSnapshot> assets, double[,] covariance)
    {
        if (assets.Count < 2)
            return 0.0;

        var values = new List<double>();
        for (var i = 0; i < assets.Count; i++)
        {
            for (var j = i + 1; j < assets.Count; j++)
            {
                var denominator = assets[i].AnnualVolatility * assets[j].AnnualVolatility;
                if (denominator <= 0)
                    continue;

                values.Add(covariance[i, j] / denominator);
            }
        }

        return values.Count == 0 ? 0.0 : values.Average();
    }

    private static List<double> BuildHistoricalPortfolioReturns(IReadOnlyList<CarryAssetSnapshot> assets, double[] weights)
    {
        if (assets.Any(asset => asset.HistoricalDailyReturns is null))
            return [];

        var seriesLength = assets.Min(asset => asset.HistoricalDailyReturns!.Count);
        if (seriesLength == 0)
            return [];

        var returns = new List<double>(seriesLength);
        for (var index = 0; index < seriesLength; index++)
        {
            var portfolioReturn = 0.0;
            for (var assetIndex = 0; assetIndex < assets.Count; assetIndex++)
                portfolioReturn += weights[assetIndex] * assets[assetIndex].HistoricalDailyReturns![index];

            returns.Add(portfolioReturn);
        }

        return returns;
    }

    private static CarryTailRiskEstimate BuildHistoricalTailRisk(IReadOnlyList<double> returns, double confidenceLevel, decimal portfolioValue)
    {
        var sorted = returns.OrderBy(value => value).ToList();
        var percentileIndex = Math.Clamp((int)Math.Floor((1.0 - confidenceLevel) * sorted.Count), 0, sorted.Count - 1);
        var varReturn = Math.Abs(sorted[percentileIndex]);
        var tail = sorted.Take(percentileIndex + 1).Select(Math.Abs).ToList();
        var cvarReturn = tail.Count == 0 ? varReturn : tail.Average();

        return new CarryTailRiskEstimate(
            "Historical simulation",
            confidenceLevel,
            cvarReturn,
            decimal.Round(portfolioValue * (decimal)cvarReturn, 2));
    }

    private static List<ScenarioImpact> BuildScenarioImpacts(
        IReadOnlyList<CarryAssetSnapshot> assets,
        double[] weights,
        AdvancedCarryRiskOptions risk)
    {
        var scenarios = new List<ScenarioImpact>(4);
        scenarios.Add(BuildScenario(
            CarryScenarioType.RateSpike,
            assets,
            weights,
            asset => -(asset.DurationYears * (risk.RateShockBps / 10_000.0)),
            $"Parallel rate shock of +{risk.RateShockBps:0} bps applied through duration."));
        scenarios.Add(BuildScenario(
            CarryScenarioType.CreditSpreadWidening,
            assets,
            weights,
            asset => -(asset.SpreadDurationYears * (risk.CreditSpreadShockBps / 10_000.0)),
            $"Credit spreads widen by +{risk.CreditSpreadShockBps:0} bps using spread duration."));
        scenarios.Add(BuildScenario(
            CarryScenarioType.EquityShock,
            assets,
            weights,
            asset => asset.MarketBeta * risk.EquityShock,
            $"Equity market shock of {risk.EquityShock:P1} applied through asset beta."));
        scenarios.Add(BuildScenario(
            CarryScenarioType.LiquidityCrunch,
            assets,
            weights,
            asset => -Math.Max(0.003, (asset.BidAskSpreadBps / 10_000.0) * risk.LiquiditySpreadMultiple),
            $"Liquidity crunch expands bid/ask costs by {risk.LiquiditySpreadMultiple:0.0}x normal spreads."));
        return scenarios;
    }

    private static ScenarioImpact BuildScenario(
        CarryScenarioType scenario,
        IReadOnlyList<CarryAssetSnapshot> assets,
        IReadOnlyList<double> weights,
        Func<CarryAssetSnapshot, double> assetShock,
        string narrative)
    {
        var perAsset = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var portfolioImpact = 0.0;

        for (var i = 0; i < assets.Count; i++)
        {
            var impact = assetShock(assets[i]);
            perAsset[assets[i].Symbol] = impact;
            portfolioImpact += weights[i] * impact;
        }

        return new ScenarioImpact(
            scenario,
            portfolioImpact,
            new ReadOnlyDictionary<string, double>(perAsset),
            narrative);
    }

    private static double ComputeCorrelation(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var count = Math.Min(left.Count, right.Count);
        if (count < 2)
            return 0.0;

        var leftMean = left.Take(count).Average();
        var rightMean = right.Take(count).Average();
        var covariance = 0.0;
        var leftVariance = 0.0;
        var rightVariance = 0.0;

        for (var i = 0; i < count; i++)
        {
            var leftDelta = left[i] - leftMean;
            var rightDelta = right[i] - rightMean;
            covariance += leftDelta * rightDelta;
            leftVariance += leftDelta * leftDelta;
            rightVariance += rightDelta * rightDelta;
        }

        if (leftVariance <= 0 || rightVariance <= 0)
            return 0.0;

        return Math.Clamp(covariance / Math.Sqrt(leftVariance * rightVariance), -1.0, 1.0);
    }

    private static double[] Multiply(double[,] matrix, IReadOnlyList<double> vector)
    {
        var result = new double[vector.Count];
        for (var row = 0; row < matrix.GetLength(0); row++)
        {
            var value = 0.0;
            for (var column = 0; column < matrix.GetLength(1); column++)
                value += matrix[row, column] * vector[column];

            result[row] = value;
        }

        return result;
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var sum = 0.0;
        for (var i = 0; i < left.Count; i++)
            sum += left[i] * right[i];

        return sum;
    }

    private static double[] NormalizeWithCap(IReadOnlyList<double> rawWeights, double maxWeight)
    {
        var weights = rawWeights.Select(value => Math.Max(0.0, value)).ToArray();
        var result = new double[weights.Length];
        var remainingBudget = 1.0;
        var active = Enumerable.Range(0, weights.Length).ToHashSet();

        while (active.Count > 0)
        {
            var activeWeightSum = active.Sum(index => weights[index]);
            if (activeWeightSum <= 0)
            {
                var equalWeight = remainingBudget / active.Count;
                foreach (var index in active)
                    result[index] = Math.Min(maxWeight, equalWeight);
                break;
            }

            var capped = new List<int>();
            foreach (var index in active)
            {
                var proportionalWeight = remainingBudget * (weights[index] / activeWeightSum);
                if (proportionalWeight > maxWeight)
                {
                    result[index] = maxWeight;
                    remainingBudget -= maxWeight;
                    capped.Add(index);
                }
            }

            if (capped.Count == 0)
            {
                foreach (var index in active)
                    result[index] = remainingBudget * (weights[index] / activeWeightSum);
                remainingBudget = 0.0;
                break;
            }

            foreach (var index in capped)
                active.Remove(index);
        }

        if (remainingBudget > 1e-8)
        {
            var unlocked = Enumerable.Range(0, result.Length)
                .Where(index => result[index] < maxWeight - 1e-8)
                .ToList();
            if (unlocked.Count > 0)
            {
                var increment = remainingBudget / unlocked.Count;
                foreach (var index in unlocked)
                    result[index] = Math.Min(maxWeight, result[index] + increment);
            }
        }

        var sum = result.Sum();
        if (sum <= 0)
            return Enumerable.Repeat(1.0 / result.Length, result.Length).ToArray();

        return result.Select(value => value / sum).ToArray();
    }

    private static double StandardNormalPdf(double value)
    {
        return Math.Exp(-(value * value) / 2.0) / Math.Sqrt(2.0 * Math.PI);
    }
}
