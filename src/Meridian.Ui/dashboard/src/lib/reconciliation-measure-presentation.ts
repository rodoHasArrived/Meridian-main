import { formatExactMagnitude, parseExactDecimal } from "@/lib/exact-decimal";
import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem } from "@/types";

export function displayAmount(value: string | number | null, signed = false): string {
  const decimal = parseExactDecimal(value);
  if (!decimal) return "Unavailable";
  const sign = decimal.units === 0n ? "" : decimal.negative ? "−" : signed ? "+" : "";
  return `${sign}${formatExactMagnitude(decimal, decimal.scale).text}`;
}

/** An explicit unavailable measure stays unavailable; numeric fields support older hosts only. */
export function caseMeasureAmount(measure: ReconciliationBreakMeasure, field: "expected" | "actual" | "variance" | "tolerance"): string | number | null {
  const exact = measure[`${field}Exact`];
  return exact === undefined ? measure[field] : exact;
}

export function retainedResidual(measure: ReconciliationBreakMeasure): string | number | null {
  if (measure.unavailableReason || !parseExactDecimal(caseMeasureAmount(measure, "actual")) || !parseExactDecimal(caseMeasureAmount(measure, "expected"))) return null;
  const residual = caseMeasureAmount(measure, "variance");
  return parseExactDecimal(residual) ? residual : null;
}

export function measureUnit(unit: string): string {
  return !unit || unit === "currency" ? "Currency unavailable" : unit;
}

export function caseQueueTolerance(item: ReconciliationBreakQueueItem): string | null {
  if (item.statementEvidence) {
    const { residuals } = item.statementEvidence.match;
    if (residuals.length > 1) return "Multiple measure tolerances";
    if (residuals.length === 0 || residuals[0].tolerance == null) return "Unavailable";
    return `${displayAmount(residuals[0].tolerance)} ${measureUnit(residuals[0].unit)}`;
  }
  const measures = item.measures ?? [];
  if (measures.length === 0) return null;
  if (measures.length > 1) return "Multiple measure tolerances";
  const measure = measures[0];
  const tolerance = caseMeasureAmount(measure, "tolerance");
  return tolerance == null ? "Unavailable" : `${displayAmount(tolerance)} ${measureUnit(measure.unit)}`;
}

export function caseQueueResidual(item: ReconciliationBreakQueueItem): { amount: string; unit: string | null } {
  if (item.statementEvidence) {
    const { residuals } = item.statementEvidence.match;
    if (residuals.length > 1) return { amount: "Multiple", unit: null };
    if (residuals.length === 1) return { amount: displayAmount(residuals[0].residual, true), unit: measureUnit(residuals[0].unit) };
    return { amount: "Unavailable", unit: null };
  }
  const measures = item.measures ?? [];
  if (measures.length > 1) return { amount: "Multiple", unit: null };
  const reported = measures.filter((measure) => retainedResidual(measure) != null);
  if (reported.length === 1) return { amount: displayAmount(retainedResidual(reported[0]), true), unit: measureUnit(reported[0].unit) };
  return measures.length > 0
    ? { amount: "Unavailable", unit: null }
    : { amount: displayAmount(item.varianceExact === undefined ? item.variance : item.varianceExact, true), unit: "Unit unavailable" };
}

