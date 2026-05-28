import type { PriceAlertCondition } from "./types";

export interface EvaluationInput {
  condition: PriceAlertCondition;
  threshold: number;
  previousPrice: number | null;
  currentPrice: number;
}

export function shouldTrigger({ condition, threshold, previousPrice, currentPrice }: EvaluationInput): boolean {
  if (!Number.isFinite(currentPrice) || !Number.isFinite(threshold)) {
    return false;
  }

  switch (condition) {
    case "above":
      return currentPrice >= threshold;
    case "below":
      return currentPrice <= threshold;
    case "crosses-up":
      return previousPrice !== null
        && Number.isFinite(previousPrice)
        && previousPrice < threshold
        && currentPrice >= threshold;
    case "crosses-down":
      return previousPrice !== null
        && Number.isFinite(previousPrice)
        && previousPrice > threshold
        && currentPrice <= threshold;
  }
}

export function describeCondition(condition: PriceAlertCondition, threshold: number, field: string = "last"): string {
  const t = formatThreshold(threshold);
  switch (condition) {
    case "above":
      return `${field} ≥ ${t}`;
    case "below":
      return `${field} ≤ ${t}`;
    case "crosses-up":
      return `${field} crosses up through ${t}`;
    case "crosses-down":
      return `${field} crosses down through ${t}`;
  }
}

function formatThreshold(value: number): string {
  if (!Number.isFinite(value)) {
    return "—";
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: value % 1 === 0 ? 0 : 2,
    maximumFractionDigits: 4
  });
}
