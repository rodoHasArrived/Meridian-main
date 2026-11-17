// Period-over-period variance (UX Phase E). One rule, shared with the server's posted-period
// comparison: variance = current − prior; variance % = variance ÷ |prior| × 100, so the sign follows
// the variance; a prior of zero has no percentage ("Not comparable · prior zero"). Amounts are
// exact decimals: no binary floating point between the source figure and the variance.

export type DecimalInput = string | number | null | undefined;

export interface Variance {
  /** current − prior as an exact decimal string, or null when either side is missing. */
  change: string | null;
  /** change ÷ |prior| × 100, rounded half away from zero to two places; null when prior is zero or missing. */
  percent: string | null;
}

interface ScaledDecimal { units: bigint; scale: number }

function parseDecimal(value: DecimalInput): ScaledDecimal | null {
  if (value === null || value === undefined) return null;
  const text = typeof value === "number" ? (Number.isFinite(value) ? numberToPlainString(value) : "") : value.trim();
  const match = /^(-)?(\d+)(?:\.(\d+))?$/.exec(text);
  if (!match) return null;
  const fraction = match[3] ?? "";
  const units = BigInt(`${match[2]}${fraction}`) * (match[1] ? -1n : 1n);
  return { units, scale: fraction.length };
}

/** A number as plain decimal digits; String() uses exponent form for very small or large values. */
function numberToPlainString(value: number): string {
  const text = String(value);
  const match = /^(-)?(\d)(?:\.(\d+))?e([+-]\d+)$/i.exec(text);
  if (!match) return text;
  const digits = `${match[2]}${match[3] ?? ""}`;
  const point = 1 + Number(match[4]);
  const body = point <= 0
    ? `0.${"0".repeat(-point)}${digits}`
    : point >= digits.length ? `${digits}${"0".repeat(point - digits.length)}` : `${digits.slice(0, point)}.${digits.slice(point)}`;
  return `${match[1] ?? ""}${body}`;
}

function align(left: ScaledDecimal, right: ScaledDecimal): [bigint, bigint, number] {
  const scale = Math.max(left.scale, right.scale);
  return [left.units * 10n ** BigInt(scale - left.scale), right.units * 10n ** BigInt(scale - right.scale), scale];
}

function formatScaled(units: bigint, scale: number): string {
  const negative = units < 0n;
  const digits = (negative ? -units : units).toString().padStart(scale + 1, "0");
  const whole = scale === 0 ? digits : digits.slice(0, -scale);
  const fraction = scale === 0 ? "" : digits.slice(-scale);
  return `${negative && units !== 0n ? "-" : ""}${whole}${fraction ? `.${fraction}` : ""}`;
}

/** Divides with rounding half away from zero. */
function divideRounded(numerator: bigint, denominator: bigint): bigint {
  const negative = (numerator < 0n) !== (denominator < 0n);
  const n = numerator < 0n ? -numerator : numerator;
  const d = denominator < 0n ? -denominator : denominator;
  const quotient = (n * 2n + d) / (d * 2n);
  return negative ? -quotient : quotient;
}

export function computeVariance(current: DecimalInput, prior: DecimalInput): Variance {
  const currentValue = parseDecimal(current);
  const priorValue = parseDecimal(prior);
  if (!currentValue || !priorValue) return { change: null, percent: null };
  const [currentUnits, priorUnits, scale] = align(currentValue, priorValue);
  const changeUnits = currentUnits - priorUnits;
  const change = formatScaled(changeUnits, scale);
  if (priorUnits === 0n) return { change, percent: null };
  const absolutePrior = priorUnits < 0n ? -priorUnits : priorUnits;
  // Percent to two places: change × 100 × 100 ÷ |prior|, then place the decimal point.
  const percentHundredths = divideRounded(changeUnits * 10_000n, absolutePrior);
  return { change, percent: formatScaled(percentHundredths, 2) };
}

/** The variance as a number for threshold tests, or null. Exactness is not needed to compare to a threshold. */
export function varianceMagnitude(change: string | null): number | null {
  if (change === null) return null;
  const value = Number(change);
  return Number.isFinite(value) ? Math.abs(value) : null;
}
