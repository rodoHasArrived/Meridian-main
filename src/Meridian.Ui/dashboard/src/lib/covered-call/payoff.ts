/**
 * Closed-form payoff math for a covered-call position
 * (long underlying + short call). Used by the `PayoffDiagram` component;
 * server is not contacted.
 */

export interface ShortCallPayoffInputs {
  /** Strike of the short call leg. */
  strike: number;
  /** Premium collected at entry, per share. */
  entryCredit: number;
  /** Number of contracts (positive integer). */
  contracts: number;
  /** Contract multiplier — typically 100. */
  multiplier: number;
}

export interface CoveredCallPayoffInputs extends ShortCallPayoffInputs {
  /** Number of underlying shares held. */
  underlyingShares: number;
  /** Cost basis per share for the underlying position. */
  underlyingCostBasis: number;
}

/**
 * Total P&L of the short-call leg at the given underlying spot. Positive = profit to writer.
 *   payoff(S) = (credit - max(S - strike, 0)) * contracts * multiplier
 */
export function shortCallPayoff(spot: number, p: ShortCallPayoffInputs): number {
  const intrinsicAtExpiry = Math.max(spot - p.strike, 0);
  return (p.entryCredit - intrinsicAtExpiry) * p.contracts * p.multiplier;
}

/**
 * Total P&L of the underlying leg at the given spot. Positive = unrealised gain.
 */
export function underlyingPayoff(spot: number, p: CoveredCallPayoffInputs): number {
  return (spot - p.underlyingCostBasis) * p.underlyingShares;
}

/**
 * Combined P&L of the covered-call structure (long underlying + short call) at spot.
 */
export function coveredCallPayoff(spot: number, p: CoveredCallPayoffInputs): number {
  return underlyingPayoff(spot, p) + shortCallPayoff(spot, p);
}

export interface PayoffSample {
  spot: number;
  payoff: number;
}

export interface CoveredCallPayoffCurves {
  spots: number[];
  underlying: PayoffSample[];
  shortCall: PayoffSample[];
  covered: PayoffSample[];
}

/**
 * Builds payoff samples across `[spotMin, spotMax]` with `steps` evenly-spaced points.
 * Returns three aligned series so the chart can overlay them.
 */
export function buildCoveredCallPayoffCurves(
  p: CoveredCallPayoffInputs,
  spotMin: number,
  spotMax: number,
  steps = 100
): CoveredCallPayoffCurves {
  if (!Number.isFinite(spotMin) || !Number.isFinite(spotMax) || spotMax <= spotMin) {
    return { spots: [], underlying: [], shortCall: [], covered: [] };
  }
  const n = Math.max(2, Math.floor(steps));
  const stride = (spotMax - spotMin) / (n - 1);
  const spots: number[] = [];
  const underlying: PayoffSample[] = [];
  const shortCall: PayoffSample[] = [];
  const covered: PayoffSample[] = [];

  for (let i = 0; i < n; i++) {
    const spot = spotMin + i * stride;
    spots.push(spot);
    underlying.push({ spot, payoff: underlyingPayoff(spot, p) });
    shortCall.push({ spot, payoff: shortCallPayoff(spot, p) });
    covered.push({ spot, payoff: coveredCallPayoff(spot, p) });
  }
  return { spots, underlying, shortCall, covered };
}

/**
 * Net break-even underlying spot price for the covered call.
 * Below this spot the structure's combined payoff turns negative.
 */
export function coveredCallBreakEven(p: CoveredCallPayoffInputs): number {
  // Combined payoff(spot) = (spot - basis) * shares + credit * contracts * multiplier  (when spot <= strike)
  //                        + the capped term above strike
  // Below the strike, set combined = 0:  spot = basis - (credit * contracts * multiplier) / shares
  if (p.underlyingShares <= 0) return Number.NaN;
  return p.underlyingCostBasis - (p.entryCredit * p.contracts * p.multiplier) / p.underlyingShares;
}

/**
 * Short-call break-even (writer P&L = 0): spot = strike + entryCredit.
 * Above this spot the short call alone is unprofitable.
 */
export function shortCallBreakEven(p: ShortCallPayoffInputs): number {
  return p.strike + p.entryCredit;
}

/**
 * Builds payoff samples for the short-call leg alone (no underlying assumption).
 * Used when the backend does not return the underlying cost basis so the covered structure
 * cannot be drawn honestly.
 */
export function buildShortCallPayoffCurve(
  p: ShortCallPayoffInputs,
  spotMin: number,
  spotMax: number,
  steps = 100
): PayoffSample[] {
  if (!Number.isFinite(spotMin) || !Number.isFinite(spotMax) || spotMax <= spotMin) {
    return [];
  }
  const n = Math.max(2, Math.floor(steps));
  const stride = (spotMax - spotMin) / (n - 1);
  const out: PayoffSample[] = [];
  for (let i = 0; i < n; i++) {
    const spot = spotMin + i * stride;
    out.push({ spot, payoff: shortCallPayoff(spot, p) });
  }
  return out;
}
