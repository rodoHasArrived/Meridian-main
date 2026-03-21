namespace Meridian.Storage.Export;

/// <summary>
/// Feature engineering for exported data. Computes returns, rolling statistics,
/// and technical indicators based on <see cref="FeatureSettings"/>.
/// Enriches raw record dictionaries with computed columns before writing to output formats.
/// </summary>
public sealed partial class AnalysisExportService
{
    /// <summary>
    /// Enriches a list of records with computed features based on settings.
    /// Records must be sorted by timestamp for correct computation.
    /// </summary>
    internal static List<Dictionary<string, object?>> EnrichWithFeatures(
        List<Dictionary<string, object?>> records,
        FeatureSettings features)
    {
        if (records.Count == 0)
            return records;

        // Extract price series for computations
        var prices = ExtractPriceSeries(records);
        if (prices.Length == 0)
            return records;

        // 1. Return features
        if (features.IncludeReturns)
        {
            AddReturnFeatures(records, prices, features);
        }

        // 2. Rolling statistics
        if (features.IncludeRollingStats)
        {
            AddRollingStats(records, prices, features);
        }

        // 3. Technical indicators
        if (features.IncludeTechnicalIndicators)
        {
            AddTechnicalIndicators(records, prices, features);
        }

        // 4. Microstructure features
        if (features.IncludeMicrostructure)
        {
            AddMicrostructureFeatures(records);
        }

        // 5. Normalization
        if (features.Normalization != NormalizationType.None)
        {
            NormalizeFeatures(records, features);
        }

        return records;
    }

    private static double[] ExtractPriceSeries(List<Dictionary<string, object?>> records)
    {
        var prices = new List<double>();
        foreach (var record in records)
        {
            // Try multiple price field names
            if (TryGetDouble(record, "Price", out var price) ||
                TryGetDouble(record, "price", out price) ||
                TryGetDouble(record, "Close", out price) ||
                TryGetDouble(record, "close", out price) ||
                TryGetDouble(record, "last", out price))
            {
                prices.Add(price);
            }
            else
            {
                prices.Add(double.NaN);
            }
        }
        return prices.ToArray();
    }

    private static void AddReturnFeatures(
        List<Dictionary<string, object?>> records,
        double[] prices,
        FeatureSettings features)
    {
        foreach (var horizon in features.ReturnHorizons)
        {
            var columnName = features.UseLogReturns
                ? $"log_return_{horizon}"
                : $"return_{horizon}";

            for (int i = 0; i < records.Count; i++)
            {
                if (i < horizon || double.IsNaN(prices[i]) || double.IsNaN(prices[i - horizon]) || prices[i - horizon] == 0)
                {
                    records[i][columnName] = null;
                }
                else if (features.UseLogReturns)
                {
                    records[i][columnName] = Math.Round(Math.Log(prices[i] / prices[i - horizon]), 8);
                }
                else
                {
                    records[i][columnName] = Math.Round((prices[i] - prices[i - horizon]) / prices[i - horizon], 8);
                }
            }
        }
    }

    private static void AddRollingStats(
        List<Dictionary<string, object?>> records,
        double[] prices,
        FeatureSettings features)
    {
        foreach (var window in features.RollingWindows)
        {
            var meanCol = $"rolling_mean_{window}";
            var stdCol = $"rolling_std_{window}";
            var minCol = $"rolling_min_{window}";
            var maxCol = $"rolling_max_{window}";

            for (int i = 0; i < records.Count; i++)
            {
                if (i < window - 1)
                {
                    records[i][meanCol] = null;
                    records[i][stdCol] = null;
                    records[i][minCol] = null;
                    records[i][maxCol] = null;
                    continue;
                }

                var windowPrices = new double[window];
                var validCount = 0;
                for (int j = 0; j < window; j++)
                {
                    var p = prices[i - window + 1 + j];
                    if (!double.IsNaN(p))
                    {
                        windowPrices[validCount++] = p;
                    }
                }

                if (validCount < 2)
                {
                    records[i][meanCol] = null;
                    records[i][stdCol] = null;
                    records[i][minCol] = null;
                    records[i][maxCol] = null;
                    continue;
                }

                var slice = windowPrices.AsSpan(0, validCount);
                var mean = Mean(slice);
                var std = StdDev(slice, mean);

                records[i][meanCol] = Math.Round(mean, 6);
                records[i][stdCol] = Math.Round(std, 6);
                records[i][minCol] = Math.Round(Min(slice), 6);
                records[i][maxCol] = Math.Round(Max(slice), 6);
            }
        }
    }

    private static void AddTechnicalIndicators(
        List<Dictionary<string, object?>> records,
        double[] prices,
        FeatureSettings features)
    {
        foreach (var indicator in features.TechnicalIndicators)
        {
            switch (indicator.ToUpperInvariant())
            {
                case "SMA":
                    AddSma(records, prices, 20, "sma_20");
                    break;
                case "EMA":
                    AddEma(records, prices, 20, "ema_20");
                    break;
                case "RSI":
                    AddRsi(records, prices, 14, "rsi_14");
                    break;
                case "BOLLINGER":
                    AddBollingerBands(records, prices, 20);
                    break;
            }
        }
    }

    private static void AddSma(
        List<Dictionary<string, object?>> records,
        double[] prices, int period, string colName)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (i < period - 1)
            {
                records[i][colName] = null;
                continue;
            }

            var sum = 0.0;
            var count = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                if (!double.IsNaN(prices[j]))
                {
                    sum += prices[j];
                    count++;
                }
            }

            records[i][colName] = count > 0 ? Math.Round(sum / count, 6) : null;
        }
    }

    private static void AddEma(
        List<Dictionary<string, object?>> records,
        double[] prices, int period, string colName)
    {
        var multiplier = 2.0 / (period + 1);
        double? ema = null;

        for (int i = 0; i < records.Count; i++)
        {
            if (double.IsNaN(prices[i]))
            {
                records[i][colName] = ema.HasValue ? Math.Round(ema.Value, 6) : (object?)null;
                continue;
            }

            if (ema == null)
            {
                // Initialize with SMA of first 'period' prices
                if (i >= period - 1)
                {
                    var sum = 0.0;
                    for (int j = i - period + 1; j <= i; j++)
                        sum += prices[j];
                    ema = sum / period;
                }
            }
            else
            {
                ema = (prices[i] - ema.Value) * multiplier + ema.Value;
            }

            records[i][colName] = ema.HasValue ? Math.Round(ema.Value, 6) : null;
        }
    }

    private static void AddRsi(
        List<Dictionary<string, object?>> records,
        double[] prices, int period, string colName)
    {
        var gains = new double[prices.Length];
        var losses = new double[prices.Length];

        for (int i = 1; i < prices.Length; i++)
        {
            if (double.IsNaN(prices[i]) || double.IsNaN(prices[i - 1]))
            {
                gains[i] = 0;
                losses[i] = 0;
                continue;
            }

            var change = prices[i] - prices[i - 1];
            gains[i] = change > 0 ? change : 0;
            losses[i] = change < 0 ? -change : 0;
        }

        double avgGain = 0, avgLoss = 0;

        for (int i = 0; i < records.Count; i++)
        {
            if (i < period)
            {
                records[i][colName] = null;
                continue;
            }

            if (i == period)
            {
                avgGain = 0;
                avgLoss = 0;
                for (int j = 1; j <= period; j++)
                {
                    avgGain += gains[j];
                    avgLoss += losses[j];
                }
                avgGain /= period;
                avgLoss /= period;
            }
            else
            {
                avgGain = (avgGain * (period - 1) + gains[i]) / period;
                avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
            }

            if (avgLoss == 0)
            {
                records[i][colName] = 100.0;
            }
            else
            {
                var rs = avgGain / avgLoss;
                records[i][colName] = Math.Round(100.0 - 100.0 / (1.0 + rs), 4);
            }
        }
    }

    private static void AddBollingerBands(
        List<Dictionary<string, object?>> records,
        double[] prices, int period)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (i < period - 1)
            {
                records[i]["bb_upper"] = null;
                records[i]["bb_middle"] = null;
                records[i]["bb_lower"] = null;
                continue;
            }

            // Build a window of valid (non-NaN) prices for this period
            var windowStart = i - period + 1;
            var validCount = 0;
            for (int j = 0; j < period; j++)
            {
                if (!double.IsNaN(prices[windowStart + j]))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                records[i]["bb_upper"] = null;
                records[i]["bb_middle"] = null;
                records[i]["bb_lower"] = null;
                continue;
            }

            var windowPrices = new double[validCount];
            var idx = 0;
            for (int j = 0; j < period; j++)
            {
                var price = prices[windowStart + j];
                if (!double.IsNaN(price))
                {
                    windowPrices[idx++] = price;
                }
            }
            var mean = Mean(windowPrices);
            var std = StdDev(windowPrices, mean);

            records[i]["bb_upper"] = Math.Round(mean + 2 * std, 6);
            records[i]["bb_middle"] = Math.Round(mean, 6);
            records[i]["bb_lower"] = Math.Round(mean - 2 * std, 6);
        }
    }

    private static void AddMicrostructureFeatures(List<Dictionary<string, object?>> records)
    {
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];

            // Spread features from quote data
            if (TryGetDouble(record, "BidPrice", out var bid) && TryGetDouble(record, "AskPrice", out var ask))
            {
                var spread = ask - bid;
                var midPrice = (bid + ask) / 2.0;
                record["spread"] = Math.Round(spread, 8);
                record["spread_bps"] = midPrice > 0 ? Math.Round(spread / midPrice * 10000, 4) : null;
                record["mid_price"] = Math.Round(midPrice, 8);
            }

            // Volume-weighted features
            if (TryGetDouble(record, "Size", out var size) && TryGetDouble(record, "Price", out var price))
            {
                record["dollar_volume"] = Math.Round(price * size, 2);
            }

            // Order imbalance from bid/ask sizes
            if (TryGetDouble(record, "BidSize", out var bidSize) && TryGetDouble(record, "AskSize", out var askSize))
            {
                var totalSize = bidSize + askSize;
                record["order_imbalance"] = totalSize > 0
                    ? Math.Round((bidSize - askSize) / totalSize, 6)
                    : null;
            }
        }
    }

    private static void NormalizeFeatures(
        List<Dictionary<string, object?>> records,
        FeatureSettings features)
    {
        if (records.Count == 0)
            return;

        // Find all computed feature columns (not original data)
        var featureCols = records[0].Keys
            .Where(k => k.StartsWith("return_") || k.StartsWith("log_return_") ||
                        k.StartsWith("rolling_") || k.StartsWith("sma_") ||
                        k.StartsWith("ema_") || k.StartsWith("rsi_") ||
                        k.StartsWith("bb_") || k == "spread_bps" || k == "order_imbalance")
            .ToList();

        foreach (var col in featureCols)
        {
            var values = records
                .Select(r => r.TryGetValue(col, out var v) && v is double d ? d : (double?)null)
                .ToArray();

            var validValues = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            if (validValues.Length < 2)
                continue;

            switch (features.Normalization)
            {
                case NormalizationType.MinMax:
                    {
                        var min = validValues.Min();
                        var max = validValues.Max();
                        var range = max - min;
                        if (range == 0)
                            break;

                        for (int i = 0; i < records.Count; i++)
                        {
                            if (values[i].HasValue)
                                records[i][col] = Math.Round((values[i]!.Value - min) / range, 8);
                        }
                        break;
                    }
                case NormalizationType.ZScore:
                    {
                        var mean = Mean(validValues);
                        var std = StdDev(validValues, mean);
                        if (std == 0)
                            break;

                        for (int i = 0; i < records.Count; i++)
                        {
                            if (values[i].HasValue)
                                records[i][col] = Math.Round((values[i]!.Value - mean) / std, 8);
                        }
                        break;
                    }
                case NormalizationType.Robust:
                    {
                        var sorted = validValues.OrderBy(v => v).ToArray();
                        var median = sorted[sorted.Length / 2];
                        var q1 = sorted[sorted.Length / 4];
                        var q3 = sorted[3 * sorted.Length / 4];
                        var iqr = q3 - q1;
                        if (iqr == 0)
                            break;

                        for (int i = 0; i < records.Count; i++)
                        {
                            if (values[i].HasValue)
                                records[i][col] = Math.Round((values[i]!.Value - median) / iqr, 8);
                        }
                        break;
                    }
            }
        }
    }

    // Math helpers
    private static double Mean(ReadOnlySpan<double> values)
    {
        var sum = 0.0;
        foreach (var v in values)
            sum += v;
        return sum / values.Length;
    }

    private static double Mean(double[] values) => Mean(values.AsSpan());

    private static double StdDev(ReadOnlySpan<double> values, double mean)
    {
        var sumSq = 0.0;
        foreach (var v in values)
        {
            var diff = v - mean;
            sumSq += diff * diff;
        }
        return Math.Sqrt(sumSq / values.Length);
    }

    private static double StdDev(double[] values, double mean) => StdDev(values.AsSpan(), mean);

    private static double Min(ReadOnlySpan<double> values)
    {
        var min = double.MaxValue;
        foreach (var v in values)
            if (v < min)
                min = v;
        return min;
    }

    private static double Max(ReadOnlySpan<double> values)
    {
        var max = double.MinValue;
        foreach (var v in values)
            if (v > max)
                max = v;
        return max;
    }

    private static bool TryGetDouble(Dictionary<string, object?> record, string key, out double value)
    {
        value = 0;
        if (!record.TryGetValue(key, out var obj) || obj is null)
            return false;

        if (obj is double d)
        { value = d; return true; }
        if (obj is float f)
        { value = f; return true; }
        if (obj is decimal dec)
        { value = (double)dec; return true; }
        if (obj is int i)
        { value = i; return true; }
        if (obj is long l)
        { value = l; return true; }
        if (obj is string s && double.TryParse(s, out var parsed))
        { value = parsed; return true; }

        return false;
    }
}
