import type { MetricSnapshot } from "@/types";

// Older producers put the period inside Delta. Retain those declared periods,
// but never turn an unqualified "+8%" into a claim about an invented interval.
const DECLARED_PERIOD = /\b(?:vs\.?\s+\S|since\s+\S|(?:last|prior|previous)\s+(?:day|week|month|quarter|year)|\d+\s*(?:hours?|days?|weeks?|months?|quarters?|years?|[hdwmy])\b|[ymq]td\b|daily|weekly|monthly|annual)/i;

export function presentMetricChange(metric: Pick<MetricSnapshot, "delta" | "comparisonPeriod">): string | null {
  const delta = metric.delta?.trim();
  if (!delta) return null;
  const period = metric.comparisonPeriod?.trim();
  if (period) return `${delta} · ${period}`;
  return DECLARED_PERIOD.test(delta) ? delta : null;
}

export function metricBasisLabel(metric: Pick<MetricSnapshot, "basis" | "asOf">): string {
  return [metric.basis?.trim() || "Basis unavailable", metric.asOf?.trim() ? `as of ${metric.asOf.trim()}` : null]
    .filter(Boolean).join(" · ");
}
