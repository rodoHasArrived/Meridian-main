import { StatusBadge } from "@/design-system/status";
import type { TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { assessVariance, type MaterialityPolicy } from "@/lib/reporting-materiality";
import { varianceMagnitude } from "@/lib/variance";

/** The note for a variance % that has no value: the prior figure was zero. */
export const VARIANCE_PERCENT_NOT_COMPARABLE = "Not comparable · prior zero";

export interface VarianceColumnsOptions<T> {
  /** Prior figure as an exact decimal string. */
  prior: (row: T) => string | null;
  /** Variance (current − prior) as an exact decimal string. */
  change: (row: T) => string | null;
  /** Variance % (change ÷ |prior| × 100), null when prior is zero. */
  percent: (row: T) => string | null;
  /** Formats an amount for display, e.g. accounting parentheses with two places. */
  formatAmount: (value: string) => string;
  /** Formats a percentage for display; defaults to the value with a % sign. */
  formatPercent?: (value: string) => string;
  /** Unit shown in the headers, e.g. "USD". */
  unit?: string;
  /** Materiality thresholds; a variance past any of them is flagged "Material". */
  policy?: MaterialityPolicy;
  /** The figure a variance is measured against for the percentage-of-portfolio threshold. */
  portfolioValue?: (row: T) => number | null;
  /** Header for the prior column, e.g. "Prior movement". */
  priorLabel?: string;
  idPrefix?: string;
}

/**
 * Prior, Variance and Variance % columns for a TableWorkbench (UX Phase E). Variances past the
 * materiality policy carry a "Material" chip naming the threshold, so the rows that matter stand out
 * without a separate highlight rule per screen. Exports keep the exact decimal strings.
 */
export function varianceColumns<T>({
  prior, change, percent, formatAmount, formatPercent = (value) => `${value}%`, unit, policy, portfolioValue,
  priorLabel = "Prior", idPrefix = ""
}: VarianceColumnsOptions<T>): TableWorkbenchColumn<T>[] {
  const suffix = unit ? ` · ${unit}` : "";
  const assess = (row: T) => policy
    ? assessVariance({ variance: varianceMagnitude(change(row)), portfolioValue: portfolioValue?.(row) ?? null }, policy)
    : null;
  return [
    {
      id: `${idPrefix}prior`, label: `${priorLabel}${suffix}`, align: "right", className: "tabular-nums",
      render: (row) => { const value = prior(row); return value === null ? "—" : formatAmount(value); },
      exportType: "decimal", exportValue: (row) => prior(row), exportDisplay: (row) => { const value = prior(row); return value === null ? "" : formatAmount(value); }
    },
    {
      id: `${idPrefix}change`, label: `Variance${suffix}`, align: "right", className: "tabular-nums",
      render: (row) => {
        const value = change(row);
        if (value === null) return "—";
        const assessment = assess(row);
        const material = assessment?.outcome === "MaterialException";
        return (
          <span className="inline-flex items-center justify-end gap-1.5" data-variance-material={material || undefined}>
            {material ? <StatusBadge status="action" label="Material" title={assessment!.reason} /> : null}
            <span>{formatAmount(value)}</span>
          </span>
        );
      },
      exportType: "decimal", exportValue: (row) => change(row), exportDisplay: (row) => { const value = change(row); return value === null ? "" : formatAmount(value); }
    },
    {
      id: `${idPrefix}percent`, label: "Variance %", align: "right", className: "tabular-nums",
      // No percentage means a zero prior when there is a variance, and a missing figure otherwise.
      render: (row) => { const value = percent(row); return value !== null ? formatPercent(value) : change(row) === null ? "—" : <span className="text-muted-foreground">{VARIANCE_PERCENT_NOT_COMPARABLE}</span>; },
      exportType: "decimal", exportValue: (row) => percent(row), exportDisplay: (row) => { const value = percent(row); return value !== null ? formatPercent(value) : change(row) === null ? "" : VARIANCE_PERCENT_NOT_COMPARABLE; }
    }
  ];
}
