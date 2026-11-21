import { formatExactMagnitude, parseExactDecimal } from "@/lib/exact-decimal";
import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem } from "@/types";

export function displayAmount(value: string | number | null, signed = false): string {
  const decimal = parseExactDecimal(value);
  if (!decimal) return "Unavailable";
  const sign = decimal.units === 0n ? "" : decimal.negative ? "−" : signed ? "+" : "";
  return `${sign}${formatExactMagnitude(decimal, decimal.scale).text}`;
}

export function retainedResidual(measure: ReconciliationBreakMeasure): number | null {
  if (measure.unavailableReason || !Number.isFinite(measure.actual) || !Number.isFinite(measure.expected)) return null;
  return Number.isFinite(measure.variance) ? measure.variance : null;
}

export function measureUnit(unit: string): string {
  return !unit || unit === "currency" ? "Currency unavailable" : unit;
}

export function caseQueueTolerance(item: ReconciliationBreakQueueItem): string | null {
  const measures = item.measures ?? [];
  if (measures.length === 0) return null;
  if (measures.length > 1) return "Multiple measure tolerances";
  const measure = measures[0];
  return measure.tolerance == null ? "Unavailable" : `${displayAmount(measure.tolerance)} ${measureUnit(measure.unit)}`;
}

export function caseQueueResidual(item: ReconciliationBreakQueueItem): { amount: string; unit: string | null } {
  if (item.statementEvidence) {
    const { residuals } = item.statementEvidence.match;
    if (residuals.length > 1) return { amount: "Multiple", unit: null };
    if (residuals.length === 1) return { amount: displayAmount(residuals[0].residual, true), unit: residuals[0].unit };
    return { amount: "Unavailable", unit: null };
  }
  const measures = item.measures ?? [];
  const reported = measures.filter((measure) => retainedResidual(measure) != null);
  if (reported.length > 1) return { amount: "Multiple", unit: null };
  if (reported.length === 1) return { amount: displayAmount(reported[0].variance, true), unit: measureUnit(reported[0].unit) };
  return measures.length > 0
    ? { amount: "Unavailable", unit: null }
    : { amount: displayAmount(item.variance, true), unit: "Unit unavailable" };
}

