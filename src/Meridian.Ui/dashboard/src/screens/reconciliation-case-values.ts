import { caseMeasureAmount, caseQueueResidual, retainedResidual } from "@/lib/reconciliation-measure-presentation";
import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem } from "@/types";
export { caseMeasureAmount } from "@/lib/reconciliation-measure-presentation";

export function caseMeasure(item: ReconciliationBreakQueueItem): ReconciliationBreakMeasure | undefined {
  return item.measures?.find((entry) => entry.kind === "Value") ?? item.measures?.[0];
}

export function caseResidualUnit(item: ReconciliationBreakQueueItem): string | undefined {
  return caseQueueResidual(item).unit ?? undefined;
}

export function caseResidual(item: ReconciliationBreakQueueItem): string | number | null {
  if (item.statementEvidence) {
    const residuals = item.statementEvidence.match.residuals;
    return residuals.length === 1 ? residuals[0].residual : null;
  }
  if (item.measures?.length) {
    return item.measures.length === 1 ? retainedResidual(item.measures[0]) : null;
  }
  return item.varianceExact === undefined ? item.variance : item.varianceExact;
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
