import type { ChartEvidence } from "@/components/charts/chart-evidence";
import type { CoveredCallOpenPosition, CoveredCallRunResult } from "@/lib/covered-call";
import { buildShortCallPayoffCurve } from "@/lib/covered-call/payoff";
import { formatFixed } from "@/lib/format";
import { coveredCallRunResultEndpoint } from "@/lib/workstation-endpoints";
import { formatPrice } from "./covered-call-screen.formatters";

export interface CoveredCallPayoffChartLineViewModel {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface CoveredCallPayoffChartViewModel {
  viewBox: string;
  ariaLabel: string;
  zeroLine: CoveredCallPayoffChartLineViewModel;
  strikeLine: CoveredCallPayoffChartLineViewModel;
  path: string;
  observations: (ChartEvidence & { x: number; y: number })[];
  references: ChartEvidence[];
}

export function buildCoveredCallPayoffChart(
  position: CoveredCallOpenPosition,
  result: CoveredCallRunResult
): CoveredCallPayoffChartViewModel | null {
  const { underlyingSymbol } = result;
  const width = 320;
  const height = 180;
  const spotMin = position.strike * 0.75;
  const spotMax = position.strike * 1.25;
  const samples = buildShortCallPayoffCurve({
    strike: position.strike,
    entryCredit: position.entryCredit,
    contracts: position.contracts,
    multiplier: position.multiplier
  }, spotMin, spotMax, 80);
  const allPayoffs = samples.map((sample) => sample.payoff);
  const yMin = Math.min(...allPayoffs);
  const yMax = Math.max(...allPayoffs);
  const xScale = (spot: number) => ((spot - spotMin) / Math.max(spotMax - spotMin, 1e-6)) * width;
  const yScale = (value: number) => height - ((value - yMin) / Math.max(yMax - yMin, 1e-6)) * height;
  if (samples.length === 0 || !Number.isFinite(yMin) || !Number.isFinite(yMax)) {
    return null;
  }
  const proof = { href: coveredCallRunResultEndpoint(result.runId), label: "Open run result and position inputs" };
  const sourceDetail = `Position ${position.positionId} at run end ${result.to} · strike ${position.strike} · entry credit ${position.entryCredit} · ${position.contracts} contracts × ${position.multiplier}.`;
  const observations = samples.map(({ spot, payoff }, index) => ({
    id: `${result.runId}:${position.positionId}:point-${index}`,
    label: `Short-call expiration scenario ${index + 1}`,
    asOf: position.expiration,
    x: xScale(spot), y: yScale(payoff),
    values: [
      { series: `${underlyingSymbol} expiry spot`, value: spot, unit: "Model currency per share", basis: "Sampled underlying price at expiration." },
      { series: "Short-call payoff", value: payoff, unit: "Model currency units", basis: "(Entry credit − max(expiry spot − strike, 0)) × contracts × multiplier." }
    ],
    proof,
    sourceDetail
  }));
  const path = observations
    .map(({ x, y }, index) => `${index === 0 ? "M" : "L"}${formatFixed(x, 1)},${formatFixed(y, 1)}`)
    .join(" ");

  return {
    viewBox: `0 0 ${width} ${height}`,
    ariaLabel: `${underlyingSymbol} ${formatPrice(position.strike)} short-call payoff diagram`,
    zeroLine: { x1: 0, y1: yScale(0), x2: width, y2: yScale(0) },
    strikeLine: { x1: xScale(position.strike), y1: 0, x2: xScale(position.strike), y2: height },
    path,
    observations,
    references: [
      {
        id: `${result.runId}:${position.positionId}:strike`, label: "Strike marker", asOf: position.expiration,
        values: [{ series: "Strike", value: position.strike, unit: "Model currency per share", basis: "Exercise price in the retained option position." }],
        proof, sourceDetail
      },
      {
        id: `${result.runId}:${position.positionId}:zero`, label: "Zero payoff reference", asOf: "Fixed reference",
        values: [{ series: "Short-call payoff", value: 0, unit: "Model currency units", basis: "Zero net P&L reference." }],
        proof, sourceDetail
      }
    ]
  };
}
