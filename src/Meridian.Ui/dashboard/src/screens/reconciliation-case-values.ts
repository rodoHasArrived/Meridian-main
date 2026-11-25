import type { OperationsBreakCase, ReconciliationBreakMeasure, ReconciliationBreakQueueItem } from "@/types";
import { currencyMinorUnits, formatExactDecimal, parseExactDecimal } from "@/lib/exact-financial-value";

import { VALUE_STATE_LABELS } from "@/lib/format";
/** Reconciliation shows retained precision, including a nonzero residual below a currency's minor unit. */
export function reconciliationValueDecimals(value: string | number | null | undefined, unit?: string): number {
  const declared = unit?.trim();
  const minimum = declared && declared !== "currency" && !/^[A-Z]{3}$/.test(declared) ? 0 : currencyMinorUnits(declared);
  return Math.max(minimum, parseExactDecimal(value)?.scale ?? 0);
}

export function formatReconciliationValue(value: string | number | null | undefined, unit?: string, signed = false): string {
  if (reconciliationValueUnavailableReason(value)) return VALUE_STATE_LABELS.unavailable;
  return formatExactDecimal(value, { decimals: reconciliationValueDecimals(value, unit), signed })
    ?? (value == null ? VALUE_STATE_LABELS.unavailable : VALUE_STATE_LABELS.unavailable);
}

/** Legacy numeric responses cannot retain integer precision beyond this boundary. */
export function reconciliationValueUnavailableReason(value: string | number | null | undefined): string | undefined {
  if (typeof value === "number" && Math.abs(value) > Number.MAX_SAFE_INTEGER) {
    return "The source amount exceeds the precision of this response. An exact amount is unavailable.";
  }
  const parsed = parseExactDecimal(value);
  if (value != null && (!parsed || parsed.scale > 28)) return "The source did not supply a usable amount at retained precision.";
  return undefined;
}

export function reconciliationUnitLabel(unit?: string | null, kind?: ReconciliationBreakMeasure["kind"]): string {
  const declared = unit?.trim();
  if (declared === "currency" || !declared && (kind === "Value" || kind === "CostBasis")) return "Currency unavailable";
  return declared || "Unit unavailable";
}

export function retainedMeasureResidual(measure: ReconciliationBreakMeasure): string | number | null {
  const actual = caseMeasureAmount(measure, "actual");
  const expected = caseMeasureAmount(measure, "expected");
  if (measure.unavailableReason || !parseExactDecimal(actual) || !parseExactDecimal(expected)
    || reconciliationValueUnavailableReason(actual) || reconciliationValueUnavailableReason(expected)) return null;
  const value = caseMeasureAmount(measure, "variance");
  return !reconciliationValueUnavailableReason(value) && parseExactDecimal(value) ? value : null;
}

export function caseQueueResidual(item: ReconciliationBreakQueueItem): { amount: string; unit: string | null } {
  if (item.statementEvidence) {
    const { residuals } = item.statementEvidence.match;
    if (residuals.length > 1) return { amount: "Multiple", unit: null };
    if (residuals.length === 0) return { amount: VALUE_STATE_LABELS.unavailable, unit: null };
    const { residual, unit } = residuals[0];
    return { amount: formatReconciliationValue(residual, unit, true), unit: !reconciliationValueUnavailableReason(residual) && parseExactDecimal(residual) ? reconciliationUnitLabel(unit) : null };
  }
  const measures = item.measures ?? [];
  const reported = measures.filter((measure) => retainedMeasureResidual(measure) !== null);
  if (reported.length > 1) return { amount: "Multiple", unit: null };
  if (reported.length === 1) {
    const measure = reported[0];
    const unit = measure.unit?.trim();
    return { amount: formatReconciliationValue(caseMeasureAmount(measure, "variance"), unit, true),
      unit: reconciliationUnitLabel(unit, measure.kind) };
  }
  if (measures.length > 0) return { amount: VALUE_STATE_LABELS.unavailable, unit: null };
  const value = item.varianceExact === undefined ? item.variance : item.varianceExact;
  return { amount: formatReconciliationValue(value, undefined, true), unit: !reconciliationValueUnavailableReason(value) && parseExactDecimal(value) ? "Unit unavailable" : null };
}

export function caseVarianceLabel(item: ReconciliationBreakQueueItem): string {
  const { amount, unit } = caseQueueResidual(item);
  return [amount, unit].filter(Boolean).join(" ");
}

/** Close keeps each typed measure separate; legacy scalars have no declared unit. */
export function operationsCaseVarianceLabel(item: Pick<OperationsBreakCase, "measures" | "expectedAmount" | "actualAmount" | "variance">): string {
  if (item.measures?.length) return item.measures.map((measure) => {
    const unit = measure.unit?.trim();
    const unitLabel = reconciliationUnitLabel(unit, measure.kind);
    const expected = formatReconciliationValue(caseMeasureAmount(measure, "expected"), unit);
    const actual = formatReconciliationValue(caseMeasureAmount(measure, "actual"), unit);
    const residual = retainedMeasureResidual(measure);
    const variance = residual === null ? VALUE_STATE_LABELS.unavailable : formatReconciliationValue(residual, unit, true);
    const kind = measure.kind === "CostBasis" ? "Cost basis" : measure.kind;
    return `${item.measures!.length > 1 ? `${kind}: ` : ""}Expected ${expected} / Actual ${actual} / Variance ${variance} ${unitLabel}`;
  }).join("; ");
  if (item.expectedAmount == null && item.actualAmount == null && item.variance == null) return "Amounts not recorded";
  return `Expected ${formatReconciliationValue(item.expectedAmount)} / Actual ${formatReconciliationValue(item.actualAmount)} / Variance ${formatReconciliationValue(item.variance, undefined, true)} · Unit unavailable`;
}

export function caseMeasure(item: ReconciliationBreakQueueItem): ReconciliationBreakMeasure | undefined {
  return item.measures?.find((entry) => entry.kind === "Value") ?? item.measures?.[0];
}

export function caseResidualUnit(item: ReconciliationBreakQueueItem): string | undefined {
  const retained = item.statementEvidence?.match.residuals;
  if (retained) return retained.length === 1 ? retained[0].unit?.trim() : undefined;
  const measures = item.measures?.filter((measure) => retainedMeasureResidual(measure) !== null) ?? [];
  return measures.length === 1 ? measures[0].unit?.trim() : undefined;
}

/** An explicit unavailable measure stays unavailable; numeric fields support older hosts only. */
export function caseMeasureAmount(measure: ReconciliationBreakMeasure, field: "expected" | "actual" | "variance" | "tolerance"): string | number | null {
  const exact = measure[`${field}Exact`];
  return exact === undefined ? measure[field] : exact;
}

export function caseResidual(item: ReconciliationBreakQueueItem): string | number | null {
  if (item.statementEvidence) {
    const residuals = item.statementEvidence.match.residuals;
    const value = residuals.length === 1 ? residuals[0].residual : null;
    return reconciliationValueUnavailableReason(value) ? null : value;
  }
  const measures = item.measures ?? [];
  const reported = measures.filter((measure) => retainedMeasureResidual(measure) !== null);
  if (measures.length) return reported.length === 1 ? retainedMeasureResidual(reported[0]) : null;
  const value = item.varianceExact === undefined ? item.variance : item.varianceExact;
  return reconciliationValueUnavailableReason(value) ? null : value;
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
