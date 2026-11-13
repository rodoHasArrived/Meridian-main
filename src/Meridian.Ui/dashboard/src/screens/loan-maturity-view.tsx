import { Button } from "@/components/ui/button";
import { formatTaxDecimal, isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import type { LoanMaturityBucket, LoanPortfolioSummary } from "@/types/direct-lending.types";

const CURRENCIES = ["USD", "EUR", "GBP", "JPY", "Other"];
export function maturityCurrency(value: string | number): string {
  if (typeof value === "number" || /^\d+$/.test(value)) return CURRENCIES[Number(value)] ?? "Unknown currency";
  const code = value.trim().toUpperCase();
  return code === "OTHER" ? "Other" : code || "Unknown currency";
}
export function maturityBucketId(bucket: LoanMaturityBucket): string {
  return `${maturityCurrency(bucket.currency)}:${bucket.year ?? "unknown"}:${bucket.quarter ?? "unknown"}`;
}
export function maturityQuarter(bucket: LoanMaturityBucket): string {
  return bucket.year != null && bucket.quarter != null && bucket.quarter >= 1 && bucket.quarter <= 4
    ? `${bucket.year} Q${bucket.quarter}` : "Maturity not reported";
}

export function LoanMaturityView({ summary, selectedId, onSelect }: {
  summary: LoanPortfolioSummary;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
}) {
  const buckets = summary.maturityBuckets;
  const currencies = [...new Set((buckets ?? []).map((bucket) => maturityCurrency(bucket.currency)))];
  return <section className="space-y-3 border-y border-border py-4" aria-label="Loan maturity concentration">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h2 className="text-sm font-semibold">Contractual maturity concentration</h2>
      {selectedId ? <Button size="sm" variant="outline" onClick={() => onSelect(null)}>Clear maturity filter</Button> : null}
    </div>
    <p className="text-xs text-muted-foreground">Current principal outstanding grouped by contractual maturity. This is not scheduled repayment cash flow or a refinancing forecast. Currencies have separate scales; no FX conversion.</p>
    {!buckets ? <p className="text-xs text-muted-foreground">Maturity aggregation was not supplied by this host. Facility maturity dates remain available in the table.</p> : null}
    {currencies.map((currency) => {
      const rows = buckets!.filter((bucket) => maturityCurrency(bucket.currency) === currency);
      const max = Math.max(1, ...rows.map((bucket) => Math.abs(Number(bucket.principalOutstanding)) || 0));
      return <div key={currency} className="space-y-1" role="group" aria-label={`Contractual maturities in ${currency}`}>
        <h3 className="text-xs font-semibold">{currency} · current principal outstanding</h3>
        {rows.map((bucket) => {
          const amount = bucket.principalOutstanding;
          const knownCurrency = /^(USD|EUR|GBP|JPY)$/.test(currency);
          const reported = knownCurrency && amount != null && isTaxDecimalText(amount);
          const label = reported ? `${currency} ${formatTaxDecimal(amount)}` : "Amount unavailable";
          const id = maturityBucketId(bucket);
          const quarter = maturityQuarter(bucket);
          const facilityCount = `${bucket.loanIds.length} ${bucket.loanIds.length === 1 ? "facility" : "facilities"}`;
          const visibleLabel = `${quarter} ${facilityCount} ${label}`;
          const signedAmount = reported ? Number(amount) : 0;
          const width = Math.abs(signedAmount) / max * 50;
          return <button key={id} type="button" aria-label={visibleLabel} aria-pressed={id === selectedId} onClick={() => onSelect(id === selectedId ? null : id)}
            className="grid w-full grid-cols-[7rem_minmax(3rem,1fr)_minmax(8rem,auto)] items-center gap-3 rounded-[2px] border border-transparent p-2 text-left text-xs hover:bg-muted/40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary aria-pressed:border-primary">
            <span>{quarter}{" "}<span className="block text-muted-foreground">{facilityCount}{" "}</span></span>
            <span className="relative h-5" aria-hidden="true"><span className="absolute inset-y-0 left-1/2 border-l border-border" /><span className="absolute inset-y-0 bg-primary/70" style={{ left: `${signedAmount < 0 ? 50 - width : 50}%`, width: `${width}%` }} /></span>
            <span className="text-right font-mono tabular-nums">{label}</span>
          </button>;
        })}
      </div>;
    })}
    <p className="text-xs text-muted-foreground">Bars extend from the center zero line: positive principal to the right and negative principal to the left. Exact signed amounts are shown beside each bar.</p>
    {buckets?.length === 0 ? <p className="text-xs text-muted-foreground">No facilities have retained maturity exposure.</p> : null}
    {selectedId && !buckets?.some((bucket) => maturityBucketId(bucket) === selectedId) ? <p role="status" className="text-xs text-warning">The selected maturity group is unavailable in this read. Clear the filter to review loaded facilities.</p> : null}
  </section>;
}
