// Pure, React-free technical-indicator math for the candlestick chart. Kept in
// its own module (no React, no api imports) so it can be bundled into a Web
// Worker and run off the main thread without dragging the workstation graph in.

export interface BollingerBand {
  middle: number;
  upper: number;
  lower: number;
}

export function computeSimpleMovingAverage(values: readonly number[], period: number): Array<number | null> {
  if (period <= 0) return values.map(() => null);
  const result: Array<number | null> = new Array(values.length).fill(null);
  if (values.length < period) return result;

  let sum = 0;
  for (let i = 0; i < period; i++) sum += values[i]!;
  result[period - 1] = sum / period;
  for (let i = period; i < values.length; i++) {
    sum += values[i]! - values[i - period]!;
    result[i] = sum / period;
  }
  return result;
}

export function computeBollingerBands(
  values: readonly number[],
  period: number,
  multiplier: number
): Array<BollingerBand | null> {
  const result: Array<BollingerBand | null> = new Array(values.length).fill(null);
  if (period <= 0 || values.length < period) return result;

  let sum = 0;
  let sumSq = 0;
  for (let i = 0; i < period; i++) {
    const v = values[i]!;
    sum += v;
    sumSq += v * v;
  }
  for (let i = period - 1; i < values.length; i++) {
    if (i >= period) {
      const out = values[i - period]!;
      const incoming = values[i]!;
      sum += incoming - out;
      sumSq += incoming * incoming - out * out;
    }
    const mean = sum / period;
    const variance = Math.max(0, sumSq / period - mean * mean);
    const stddev = Math.sqrt(variance);
    result[i] = {
      middle: mean,
      upper: mean + multiplier * stddev,
      lower: mean - multiplier * stddev
    };
  }
  return result;
}

export function computeRsi(values: readonly number[], period: number): Array<number | null> {
  const result: Array<number | null> = new Array(values.length).fill(null);
  if (period <= 0 || values.length <= period) return result;

  let avgGain = 0;
  let avgLoss = 0;
  for (let i = 1; i <= period; i++) {
    const change = values[i]! - values[i - 1]!;
    if (change > 0) avgGain += change;
    else avgLoss += -change;
  }
  avgGain /= period;
  avgLoss /= period;
  result[period] = computeRsiFromAverages(avgGain, avgLoss);

  for (let i = period + 1; i < values.length; i++) {
    const change = values[i]! - values[i - 1]!;
    const gain = change > 0 ? change : 0;
    const loss = change < 0 ? -change : 0;
    avgGain = (avgGain * (period - 1) + gain) / period;
    avgLoss = (avgLoss * (period - 1) + loss) / period;
    result[i] = computeRsiFromAverages(avgGain, avgLoss);
  }
  return result;
}

function computeRsiFromAverages(avgGain: number, avgLoss: number): number {
  if (avgLoss === 0) return 100;
  const rs = avgGain / avgLoss;
  return 100 - 100 / (1 + rs);
}

// Indicator parameters the candlestick chart renders. Kept here so the worker,
// the builder, and the overlays all agree on periods.
export const CANDLESTICK_SMA_PERIODS = [20, 50] as const;
export const CANDLESTICK_BOLLINGER_PERIOD = 20;
export const CANDLESTICK_BOLLINGER_MULTIPLIER = 2;
export const CANDLESTICK_RSI_PERIOD = 14;

export interface CandlestickSmaSeries {
  period: number;
  values: Array<number | null>;
}

/** The full indicator set the candlestick chart needs, computed from close prices. */
export interface CandlestickIndicatorSet {
  sma: CandlestickSmaSeries[];
  bollinger: Array<BollingerBand | null>;
  rsi: Array<number | null>;
}

/** One-shot compute of every candlestick indicator — the unit handed to the worker. */
export function computeCandlestickIndicators(closes: readonly number[]): CandlestickIndicatorSet {
  return {
    sma: CANDLESTICK_SMA_PERIODS.map((period) => ({
      period,
      values: computeSimpleMovingAverage(closes, period)
    })),
    bollinger: computeBollingerBands(closes, CANDLESTICK_BOLLINGER_PERIOD, CANDLESTICK_BOLLINGER_MULTIPLIER),
    rsi: computeRsi(closes, CANDLESTICK_RSI_PERIOD)
  };
}

/** Convenience lookup for a precomputed SMA series by period. */
export function smaSeriesForPeriod(set: CandlestickIndicatorSet, period: number): Array<number | null> | undefined {
  return set.sma.find((series) => series.period === period)?.values;
}
