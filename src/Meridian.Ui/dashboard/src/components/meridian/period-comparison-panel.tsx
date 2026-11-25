import { StatusBanner } from "@/components/ui/status-banner";
import { Button } from "@/components/ui/button";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { varianceColumns } from "@/components/meridian/variance-columns";
import { formatTaxDecimal } from "@/lib/ledger-tax-results-format";
import { roundDecimal } from "@/lib/variance";
import type { MaterialityPolicy } from "@/lib/reporting-materiality";
import type { PeriodComparison, PeriodComparisonLine } from "@/lib/api/period-comparison.api";

/** Two places, negatives in parentheses, exact digits: the accounting display of a decimal. */
export function accountingAmount(value: string): string {
  const formatted = formatTaxDecimal(value, 2);
  return formatted.startsWith("-") ? `(${formatted.slice(1)})` : formatted;
}

export interface PeriodComparisonState {
  loading: boolean;
  error: string | null;
  result: PeriodComparison | null;
}

/**
 * A prior-period comparison (UX Phase E): the two windows compared, a table of current, prior,
 * variance and variance %, with material variances flagged, and the source's notes on what the
 * figures include. Unavailable and incompatible comparisons say why instead of showing zeros.
 */
export function PeriodComparisonPanel({ title, state, onRetry, policy, workbenchId, currentLabel = "Current", formatAmount = accountingAmount, countKeys = [] }: {
  title: string;
  state: PeriodComparisonState;
  onRetry?: () => void;
  policy?: MaterialityPolicy;
  workbenchId: string;
  currentLabel?: string;
  formatAmount?: (value: string) => string;
  /** Line keys whose figures are counts, shown without decimals. */
  countKeys?: readonly string[];
}) {
  const { loading, error, result } = state;
  const amount = (line: PeriodComparisonLine, value: string) => countKeys.includes(line.key) ? formatTaxDecimal(value, 0) : formatAmount(value);
  return (
    <section aria-label={title} className="space-y-2" data-period-comparison={workbenchId}>
      <h3 className="text-sm font-semibold">{title}</h3>
      {loading ? <p role="status" className="text-sm text-muted-foreground">Loading the prior-period comparison…</p>
        : error ? <StatusBanner role="alert" tone="warning" title="Comparison unavailable" detail={<>{error}{onRetry ? <> <Button size="sm" variant="outline" onClick={onRetry}>Retry comparison</Button></> : null}</>} />
          : !result ? null
            : result.status !== "Ready" ? <StatusBanner tone="warning" title="Comparison unavailable" detail={result.reason ?? "No prior figures could be established."} />
              : <>
                <p className="text-sm">{result.subject}{result.currency ? ` · ${result.currency}` : ""}. Current: {result.current.label}{result.current.start ? ` (${result.current.start} to ${result.current.end})` : ""}. Prior: {result.baseline?.label ?? "Unavailable"}{result.baseline?.start ? ` (${result.baseline.start} to ${result.baseline.end})` : ""}.</p>
                <TableWorkbench<PeriodComparisonLine> workbenchId={workbenchId} sourceKey={`${result.subject}:${result.current.end}:${result.baseline?.end ?? ""}`}
                  sourceDescription={`${result.subject} · current ${result.current.label} · prior ${result.baseline?.label ?? "unavailable"}`}
                  exportContext={{ Subject: result.subject, Currency: result.currency ?? "Not stated", Current: result.current.label, Prior: result.baseline?.label ?? "Unavailable", Generated: result.generatedAt }}
                  completePopulation rows={result.lines} getRowId={(line) => line.key} ariaLabel={title}
                  getRowClassName={(line) => line.isTotal ? "font-semibold" : undefined}
                  emptyText="No figures to compare in these periods."
                  columns={[
                    { id: "label", label: "Line", rowHeader: true, render: (line) => line.label, exportValue: (line) => line.label },
                    { id: "current", label: `${currentLabel}${result.currency ? ` · ${result.currency}` : ""}`, align: "right", className: "tabular-nums",
                      render: (line) => amount(line, line.current), exportType: "decimal", exportValue: (line) => line.current, exportDisplay: (line) => amount(line, line.current) },
                    ...varianceColumns<PeriodComparisonLine>({
                      prior: (line) => line.prior, change: (line) => line.change, percent: (line) => line.percentChange,
                      formatAmount: (value, line) => amount(line, value), formatPercent: (value) => `${formatTaxDecimal(roundDecimal(value, 2) ?? value, 2)}%`,
                      unit: result.currency ?? undefined, policy, assessRow: (line) => !countKeys.includes(line.key)
                    })
                  ]} />
                {result.notes.length > 0 ? <ul className="list-disc space-y-0.5 pl-5 text-xs text-muted-foreground">{result.notes.map((note) => <li key={note}>{note}</li>)}</ul> : null}
              </>}
    </section>
  );
}
