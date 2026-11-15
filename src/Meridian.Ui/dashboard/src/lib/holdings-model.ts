import { decimalText, formatKpiMoney, positionMoney, resolveKpi, type ResolvedKpi } from "@/lib/kpi-registry";
import type { PortfolioWorkspaceResponse, TradingPosition } from "@/types";

/**
 * One figure in a holdings row. `display` is "—" when the value does not exist, and `reason` then
 * says why, for a tooltip. A stale figure keeps its last recorded value and says how old it is.
 */
export interface HoldingFigure {
  display: string;
  reason: string | null;
  stale: boolean;
  /** Short marker beside a stale value, such as "Stale · priced 2026-05-07". */
  staleLabel: string | null;
}

export interface HoldingsModelRow {
  /** ISO currency of the holding's amounts, or null when the source did not supply one. */
  currencyCode: string | null;
  currency: HoldingFigure;
  marketValue: HoldingFigure;
  weight: HoldingFigure;
  /** Absolute exposure as a plain decimal, the amount this holding adds to the headline total. */
  exposureAmount: string | null;
}

/**
 * The single source for holdings figures. The holdings table, the headline figures and the holding
 * detail panel all read from this model, so they cannot show different answers for one portfolio.
 */
export interface HoldingsModel {
  rows: HoldingsModelRow[];
  totalExposure: ResolvedKpi;
  totalUnrealizedPnl: ResolvedKpi;
  openPositions: ResolvedKpi;
}

export const MISSING_FIGURE = "—";

const present = (display: string, reason: string | null = null): HoldingFigure => ({ display, reason, stale: false, staleLabel: null });
const missing = (reason: string): HoldingFigure => ({ display: MISSING_FIGURE, reason, stale: false, staleLabel: null });

interface Decimal { units: bigint; scale: number }

function parseDecimal(value: string | null | undefined): Decimal | null {
  const text = value?.trim().replace(/−/g, "-");
  if (!text) return null;
  const match = /^([+-]?)((?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,18})?)$/.exec(text);
  if (!match) return null;
  const [whole, fraction = ""] = match[2].replace(/,/g, "").split(".");
  return { units: BigInt(`${whole}${fraction}`) * (match[1] === "-" ? -1n : 1n), scale: fraction.length };
}

const abs = (value: bigint) => (value < 0n ? -value : value);

/** Round half away from zero to cents; amounts with fewer decimals are left as they are. */
function toCents(value: Decimal): Decimal {
  if (value.scale <= 2) return value;
  const divisor = 10n ** BigInt(value.scale - 2);
  const magnitude = abs(value.units);
  const rounded = magnitude / divisor + (magnitude % divisor * 2n >= divisor ? 1n : 0n);
  return { units: value.units < 0n ? -rounded : rounded, scale: 2 };
}

function resolveCurrency(position: TradingPosition): string | null {
  const supplied = position.currency?.trim().toUpperCase();
  if (supplied) return /^[A-Z]{3}$/.test(supplied) ? supplied : null;
  // The same rule the headline total uses: a "$" amount in the workstation contract is USD.
  return positionMoney(position.exposure)?.currency ?? null;
}

function marketValueFigure(position: TradingPosition, currencyCode: string | null): HoldingFigure {
  const supplied = position.marketValue?.trim();
  if (supplied) {
    return currencyCode
      ? present(supplied)
      : present(supplied, "The source supplied this value without a currency.");
  }

  const quantity = parseDecimal(position.quantity);
  // A recorded price may be bare ("188.40") or carry its own denomination ("$188.40").
  const mark = parseDecimal(position.markPrice) ?? positionMoney(position.markPrice, currencyCode);
  if (!quantity || !mark) return missing("No market value or recorded price was supplied for this holding.");
  if (!currencyCode) return missing("The source did not supply a currency for this holding.");

  const sign = position.side === "Short" ? -1n : 1n;
  const value = toCents({ units: abs(quantity.units) * mark.units * sign, scale: quantity.scale + mark.scale });
  const display = formatKpiMoney({ ...value, currency: currencyCode }, false, true);
  const observedOn = position.markFreshness?.observedOn ?? null;
  const basis = `Last recorded value: quantity ${position.quantity} × recorded price ${position.markPrice}.`;
  if (position.markFreshness?.status === "Current") return present(display, basis);
  return {
    display,
    reason: `${basis} ${observedOn ? `The price was last observed on ${observedOn}.` : "The source did not supply a price date."} No current price is available.`,
    stale: true,
    staleLabel: observedOn ? `Stale · priced ${observedOn}` : "Stale · no price date"
  };
}

function weightFigure(position: TradingPosition, exposureAmount: Decimal | null, total: ResolvedKpi): HoldingFigure {
  if (position.weight?.trim() && position.weightBasis?.trim()) return present(position.weight.trim(), position.weightBasis.trim());
  if (total.status !== "available") return missing(`Weight needs a total exposure, and that is unavailable: ${total.delta || "not supplied"}.`);
  if (!exposureAmount) return missing("The exposure for this holding has no readable amount.");
  const totalAmount = totalExposureAmount(total);
  if (!totalAmount || Number(totalAmount) === 0) return missing("Total exposure is zero, so no weight can be calculated.");
  const share = Number(decimalText(exposureAmount)) / Number(totalAmount) * 100;
  return present(`${share.toFixed(1)}%`, `Share of total exposure (${total.value}).`);
}

/** The headline total as a plain decimal, read from the figure's canonical value. */
export function totalExposureAmount(total: ResolvedKpi): string | null {
  try {
    const value = (JSON.parse(total.canonicalValue) as { value?: unknown }).value;
    return typeof value === "string" || typeof value === "number" ? String(value) : null;
  } catch {
    return null;
  }
}

export function buildHoldingsModel(portfolio: PortfolioWorkspaceResponse | null | undefined): HoldingsModel {
  const models = { portfolio: portfolio ?? null };
  const totalExposure = resolveKpi("portfolio.total-exposure", models);
  const rows = (portfolio?.positions ?? []).map((position): HoldingsModelRow => {
    const currencyCode = resolveCurrency(position);
    const exposure = positionMoney(position.exposure, position.currency);
    const exposureAmount = exposure ? { units: abs(exposure.units), scale: exposure.scale } : null;
    return {
      currencyCode,
      currency: currencyCode ? present(currencyCode) : missing("The source did not supply a currency for this holding."),
      marketValue: marketValueFigure(position, currencyCode),
      weight: weightFigure(position, exposureAmount, totalExposure),
      exposureAmount: exposureAmount ? decimalText(exposureAmount) : null
    };
  });
  return {
    rows,
    totalExposure,
    totalUnrealizedPnl: resolveKpi("portfolio.unrealized-pnl", models),
    openPositions: resolveKpi("portfolio.open-positions", models)
  };
}

/** The row figures as the holdings table and the holding detail panel display them. */
export function holdingRowFigures(row: HoldingsModelRow) {
  return {
    marketValue: row.marketValue.display,
    marketValueFigure: row.marketValue,
    currency: row.currencyCode,
    currencyFigure: row.currency,
    weight: row.weight.display,
    weightFigure: row.weight,
    weightBasis: row.weight.reason,
    exposureAmount: row.exposureAmount
  };
}

/** The headline total exposure, restated under the holdings table from the same model. */
export interface HoldingsTotalLine { exposure: string; exposureAmount: string | null; currency: string | null; reason: string | null }

export function holdingsTotalLine({ totalExposure }: HoldingsModel): HoldingsTotalLine {
  const unavailable = totalExposure.status === "unavailable";
  return {
    exposure: unavailable ? MISSING_FIGURE : totalExposure.value,
    exposureAmount: totalExposure.status === "available" ? totalExposureAmount(totalExposure) : null,
    currency: totalExposure.currency ?? null,
    reason: unavailable ? totalExposure.delta || "Total exposure is unavailable." : null
  };
}
