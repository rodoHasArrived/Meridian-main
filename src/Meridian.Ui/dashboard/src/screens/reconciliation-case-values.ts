import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem } from "@/types";

export function caseMeasure(item: ReconciliationBreakQueueItem): ReconciliationBreakMeasure | undefined {
  return item.measures?.find((entry) => entry.kind === "Value") ?? item.measures?.[0];
}

/** An explicit unavailable measure stays unavailable; numeric fields support older hosts only. */
export function caseMeasureAmount(measure: ReconciliationBreakMeasure, field: "expected" | "actual" | "variance" | "tolerance"): string | number | null {
  const exact = measure[`${field}Exact`];
  return exact === undefined ? measure[field] : exact;
}

export function caseResidual(item: ReconciliationBreakQueueItem): string | number | null {
  const measure = caseMeasure(item);
  return measure ? caseMeasureAmount(measure, "variance") : item.varianceExact ?? item.variance;
}

export function caseTolerance(item: ReconciliationBreakQueueItem): string | number | null | undefined {
  const measure = caseMeasure(item);
  return measure ? caseMeasureAmount(measure, "tolerance")
    : item.toleranceBandExact === undefined ? item.toleranceBand : item.toleranceBandExact;
}
