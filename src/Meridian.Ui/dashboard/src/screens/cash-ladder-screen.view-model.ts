import { formatCompactCurrency, formatCurrency } from "@/lib/format";
import type {
  PortfolioCashLadder,
  PortfolioCashLadderBucket,
  PortfolioCashLadderContribution,
  PortfolioCashScenario
} from "@/types/portfolio-cash-ladder.types";

// The ladder can be denominated in any currency (its base currency, or a contribution's own
// currency), so all money is rendered through the currency-aware Intl formatter rather than a
// dollar-only prefix — otherwise a EUR ladder would misleadingly show "$" amounts.
function formatLadderAmount(value: number, currency: string): string {
  return formatCurrency(value, { currency, minimumFractionDigits: 0 });
}

function formatLadderSigned(value: number, currency: string): string {
  if (!Number.isFinite(value)) {
    return "—";
  }
  const prefix = value > 0 ? "+" : "";
  return `${prefix}${formatCurrency(value, { currency, minimumFractionDigits: 0 })}`;
}

function formatLadderCompact(value: number, currency: string): string {
  try {
    return new Intl.NumberFormat("en-US", {
      style: "currency",
      currency,
      notation: "compact",
      maximumFractionDigits: 1
    }).format(value);
  } catch {
    return formatCompactCurrency(value);
  }
}

export const CASH_LADDER_CHART_WIDTH = 960;
export const CASH_LADDER_CHART_HEIGHT = 320;
export const CASH_LADDER_CHART_PADDING = { top: 16, right: 16, bottom: 40, left: 72 } as const;

/** Stable stack order and palette keys for source lanes emitted by the engine. */
export const CASH_LADDER_SOURCE_ORDER = [
  "Coupons & interest",
  "Maturities & principal",
  "Dividends & distributions",
  "Fees",
  "Capital activity",
  "Expense accruals",
  "Other"
] as const;

export interface CashLadderChartSegment {
  source: string;
  amount: number;
  y: number;
  height: number;
  paletteIndex: number;
}

export interface CashLadderChartBar {
  index: number;
  label: string;
  bucketStart: string;
  bucketEnd: string;
  x: number;
  width: number;
  segments: CashLadderChartSegment[];
  netFlowLabel: string;
  cumulativeLabel: string;
  breachesMinimumBalance: boolean;
  ariaLabel: string;
}

export interface CashLadderChartGeometry {
  bars: CashLadderChartBar[];
  cumulativePoints: string;
  cumulativeMarkers: Array<{ x: number; y: number; breaches: boolean }>;
  zeroLineY: number;
  thresholdY: number | null;
  thresholdBand: { y: number; height: number } | null;
  yTicks: Array<{ y: number; label: string }>;
}

export interface CashLadderScenarioOption {
  scenarioId: string;
  displayName: string;
}

export interface CashLadderDrillRow {
  id: string;
  instrument: string;
  assetClass: string;
  flowType: string;
  sourceLane: string;
  dueDate: string;
  amountLabel: string;
  amountTone: "success" | "danger";
  currency: string;
  projectionRunId: string;
  projectionEngineVersion: string;
  termsVersion: string;
  sourceDomain: string;
  scenarioAdjustment: string | null;
}

export function selectScenarioOptions(ladder: PortfolioCashLadder | null): CashLadderScenarioOption[] {
  if (!ladder) {
    return [{ scenarioId: "base", displayName: "Base projection" }];
  }

  return ladder.availableScenarios.map((scenario) => ({
    scenarioId: scenario.scenarioId,
    displayName: scenario.displayName
  }));
}

export function selectActiveScenario(ladder: PortfolioCashLadder | null): PortfolioCashScenario | null {
  if (!ladder) {
    return null;
  }

  return ladder.availableScenarios.find((scenario) => scenario.scenarioId === ladder.scenarioId) ?? null;
}

export function sourcePaletteIndex(source: string): number {
  const index = CASH_LADDER_SOURCE_ORDER.indexOf(source as (typeof CASH_LADDER_SOURCE_ORDER)[number]);
  return index >= 0 ? index : CASH_LADDER_SOURCE_ORDER.length - 1;
}

function niceStep(rawStep: number): number {
  const magnitude = 10 ** Math.floor(Math.log10(Math.max(rawStep, 1)));
  const normalized = rawStep / magnitude;
  const factor = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  return factor * magnitude;
}

export function buildCashLadderChart(ladder: PortfolioCashLadder | null): CashLadderChartGeometry | null {
  if (!ladder || ladder.buckets.length === 0) {
    return null;
  }

  const currency = ladder.baseCurrency;
  const plotLeft = CASH_LADDER_CHART_PADDING.left;
  const plotTop = CASH_LADDER_CHART_PADDING.top;
  const plotWidth = CASH_LADDER_CHART_WIDTH - plotLeft - CASH_LADDER_CHART_PADDING.right;
  const plotHeight = CASH_LADDER_CHART_HEIGHT - plotTop - CASH_LADDER_CHART_PADDING.bottom;

  let maxValue = ladder.minimumCashThreshold;
  let minValue = Math.min(0, ladder.minimumCashThreshold);
  for (const bucket of ladder.buckets) {
    maxValue = Math.max(maxValue, bucket.inflows, bucket.cumulativeCash);
    minValue = Math.min(minValue, bucket.outflows, bucket.cumulativeCash);
  }
  maxValue = Math.max(maxValue, ladder.openingCash);
  minValue = Math.min(minValue, ladder.openingCash);
  if (maxValue === minValue) {
    maxValue = minValue + 1;
  }

  const range = maxValue - minValue;
  const paddedMax = maxValue + range * 0.05;
  const paddedMin = minValue - range * 0.05;
  const scaleY = (value: number) =>
    plotTop + ((paddedMax - value) / (paddedMax - paddedMin)) * plotHeight;

  const slotWidth = plotWidth / ladder.buckets.length;
  const barWidth = Math.max(6, slotWidth * 0.62);
  const zeroLineY = scaleY(0);

  const bars: CashLadderChartBar[] = ladder.buckets.map((bucket, index) => {
    const x = plotLeft + slotWidth * index + (slotWidth - barWidth) / 2;
    const segments = buildBarSegments(bucket, scaleY, zeroLineY);
    return {
      index,
      label: bucket.label,
      bucketStart: bucket.bucketStart,
      bucketEnd: bucket.bucketEnd,
      x,
      width: barWidth,
      segments,
      netFlowLabel: formatLadderSigned(bucket.netFlow, currency),
      cumulativeLabel: formatLadderAmount(bucket.cumulativeCash, currency),
      breachesMinimumBalance: bucket.breachesMinimumBalance,
      ariaLabel: `${bucket.label}: net ${formatLadderSigned(bucket.netFlow, currency)}, cumulative ${formatLadderAmount(bucket.cumulativeCash, currency)}${bucket.breachesMinimumBalance ? ", below minimum balance" : ""}`
    };
  });

  const cumulativeMarkers = ladder.buckets.map((bucket, index) => ({
    x: plotLeft + slotWidth * index + slotWidth / 2,
    y: scaleY(bucket.cumulativeCash),
    breaches: bucket.breachesMinimumBalance
  }));
  const cumulativePoints = cumulativeMarkers.map((point) => `${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(" ");

  const thresholdY = ladder.minimumCashThreshold > paddedMin && ladder.minimumCashThreshold < paddedMax
    ? scaleY(ladder.minimumCashThreshold)
    : null;
  const thresholdBand = thresholdY === null
    ? null
    : { y: thresholdY, height: Math.max(0, plotTop + plotHeight - thresholdY) };

  const step = niceStep((paddedMax - paddedMin) / 5);
  const yTicks: Array<{ y: number; label: string }> = [];
  for (let tick = Math.ceil(paddedMin / step) * step; tick <= paddedMax; tick += step) {
    yTicks.push({ y: scaleY(tick), label: formatLadderCompact(tick, currency) });
  }

  return { bars, cumulativePoints, cumulativeMarkers, zeroLineY, thresholdY, thresholdBand, yTicks };
}

function buildBarSegments(
  bucket: PortfolioCashLadderBucket,
  scaleY: (value: number) => number,
  zeroLineY: number
): CashLadderChartSegment[] {
  const ordered = [...bucket.sources].sort(
    (a, b) => sourcePaletteIndex(a.source) - sourcePaletteIndex(b.source)
  );
  const segments: CashLadderChartSegment[] = [];
  let inflowStack = 0;
  let outflowStack = 0;
  for (const slice of ordered) {
    if (slice.amount === 0) {
      continue;
    }

    if (slice.amount > 0) {
      const yTop = scaleY(inflowStack + slice.amount);
      const yBottom = scaleY(inflowStack);
      segments.push({
        source: slice.source,
        amount: slice.amount,
        y: yTop,
        height: Math.max(1, yBottom - yTop),
        paletteIndex: sourcePaletteIndex(slice.source)
      });
      inflowStack += slice.amount;
    } else {
      const yTop = scaleY(outflowStack);
      const yBottom = scaleY(outflowStack + slice.amount);
      segments.push({
        source: slice.source,
        amount: slice.amount,
        y: yTop,
        height: Math.max(1, yBottom - yTop),
        paletteIndex: sourcePaletteIndex(slice.source)
      });
      outflowStack += slice.amount;
    }
  }

  if (segments.length === 0) {
    segments.push({
      source: "No flows",
      amount: 0,
      y: zeroLineY - 0.5,
      height: 1,
      paletteIndex: CASH_LADDER_SOURCE_ORDER.length - 1
    });
  }

  return segments;
}

export function selectBucketContributions(
  ladder: PortfolioCashLadder | null,
  bucketIndex: number | null
): CashLadderDrillRow[] {
  if (!ladder || bucketIndex === null || bucketIndex < 0 || bucketIndex >= ladder.buckets.length) {
    return [];
  }

  const bucket = ladder.buckets[bucketIndex];
  return ladder.contributions
    .filter((row) => row.dueDate >= bucket.bucketStart && row.dueDate <= bucket.bucketEnd)
    .map((row) => toDrillRow(row))
    .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.instrument.localeCompare(b.instrument));
}

function toDrillRow(row: PortfolioCashLadderContribution): CashLadderDrillRow {
  return {
    id: `${row.securityId}-${row.dueDate}-${row.flowType}-${row.amount}`,
    instrument: row.displayName,
    assetClass: row.assetClass,
    flowType: row.flowType,
    sourceLane: row.sourceLane,
    dueDate: row.dueDate,
    amountLabel: formatLadderSigned(row.amount, row.currency),
    amountTone: row.amount >= 0 ? "success" : "danger",
    currency: row.currency,
    projectionRunId: row.projectionRunId ? row.projectionRunId.slice(0, 8) : "—",
    projectionEngineVersion: row.projectionEngineVersion ?? "—",
    termsVersion: row.termsHash ?? (row.termsVersionId ? row.termsVersionId.slice(0, 8) : "—"),
    sourceDomain: row.sourceDomain,
    scenarioAdjustment: row.scenarioAdjustment ?? null
  };
}

export interface CashLadderSummaryCard {
  id: string;
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger";
  detail: string;
}

export function selectSummaryCards(ladder: PortfolioCashLadder | null): CashLadderSummaryCard[] {
  if (!ladder) {
    return [];
  }

  const currency = ladder.baseCurrency;
  const totalInflows = ladder.buckets.reduce((sum, bucket) => sum + bucket.inflows, 0);
  const totalOutflows = ladder.buckets.reduce((sum, bucket) => sum + bucket.outflows, 0);
  const breachCount = ladder.buckets.filter((bucket) => bucket.breachesMinimumBalance).length;
  const endingCash = ladder.buckets.length > 0
    ? ladder.buckets[ladder.buckets.length - 1].cumulativeCash
    : ladder.openingCash;

  return [
    {
      id: "opening-cash",
      label: "Opening cash",
      value: formatLadderAmount(ladder.openingCash, currency),
      tone: "default",
      detail: `${ladder.baseCurrency} · as of ${ladder.asOfDate}`
    },
    {
      id: "projected-inflows",
      label: `Inflows (${ladder.horizonDays}d)`,
      value: formatLadderAmount(totalInflows, currency),
      tone: "success",
      detail: `${ladder.securitiesWithFlows} of ${ladder.securitiesEvaluated} positions contributing`
    },
    {
      id: "projected-outflows",
      label: `Outflows (${ladder.horizonDays}d)`,
      value: formatLadderAmount(Math.abs(totalOutflows), currency),
      tone: totalOutflows < 0 ? "warning" : "default",
      detail: "Capital activity, expenses, and scenario outflows"
    },
    {
      id: "ending-cash",
      label: "Projected ending cash",
      value: formatLadderAmount(endingCash, currency),
      tone: breachCount > 0 ? "danger" : "success",
      detail: breachCount > 0
        ? `${breachCount} bucket${breachCount === 1 ? "" : "s"} below minimum balance`
        : "No minimum-balance breaches in horizon"
    }
  ];
}
