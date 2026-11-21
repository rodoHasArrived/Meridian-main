import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem } from "@/types";

export function caseMeasure(item: ReconciliationBreakQueueItem): ReconciliationBreakMeasure | undefined {
  return item.measures?.find((entry) => entry.kind === "Value") ?? item.measures?.[0];
}

export function caseResidualUnit(item: ReconciliationBreakQueueItem): string | undefined {
  const retained = item.statementEvidence?.match.residuals;
  return retained?.length === 1 ? retained[0].unit : caseMeasure(item)?.unit;
}

/** An explicit unavailable measure stays unavailable; numeric fields support older hosts only. */
export function caseMeasureAmount(measure: ReconciliationBreakMeasure, field: "expected" | "actual" | "variance" | "tolerance"): string | number | null {
  const exact = measure[`${field}Exact`];
  return exact === undefined ? measure[field] : exact;
}

export function caseResidual(item: ReconciliationBreakQueueItem): string | number | null {
  if (item.statementEvidence) {
    const residuals = item.statementEvidence.match.residuals;
    return residuals.length === 1 ? residuals[0].residual : null;
  }
  const measure = caseMeasure(item);
  return measure ? caseMeasureAmount(measure, "variance") : item.varianceExact ?? item.variance;
}

export function caseTolerance(item: ReconciliationBreakQueueItem): string | number | null | undefined {
  if (item.statementEvidence) {
    const residuals = item.statementEvidence.match.residuals;
    return residuals.length === 1 ? residuals[0].tolerance : null;
  }
  const measure = caseMeasure(item);
  return measure ? caseMeasureAmount(measure, "tolerance")
    : item.toleranceBandExact === undefined ? item.toleranceBand : item.toleranceBandExact;
}
