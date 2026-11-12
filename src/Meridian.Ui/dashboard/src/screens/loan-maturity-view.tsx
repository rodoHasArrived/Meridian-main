import { useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { formatCurrency } from "@/lib/format";
import { buildLoanMaturityBuckets, type LoanMaturityBucket } from "@/screens/loan-maturity";
import type { LoanSummary } from "@/types/direct-lending.types";

function amount(value: number | null | undefined, currency: string): string {
  return value != null && Number.isFinite(value) ? formatCurrency(value, { currency, maximumFractionDigits: 2 }) : "Unavailable";
}

export function LoanMaturityView({ loans, today = new Date().toISOString().slice(0, 10) }: { loans: LoanSummary[]; today?: string }) {
  const buckets = useMemo(() => buildLoanMaturityBuckets(loans, today), [loans, today]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = buckets.find(bucket => bucket.id === selectedId);
  const currencies = [...new Set(buckets.map(bucket => bucket.currency))];
  const label = (bucket: LoanMaturityBucket) => `${bucket.currency} ${bucket.period}: ${amount(bucket.amount, bucket.currency)}${bucket.missingAmountCount ? ` plus ${bucket.missingAmountCount} unreported balance(s)` : ""}`;
  return <section aria-labelledby="loan-maturity-title" className="space-y-3 rounded-sm border border-border p-4">
    <h2 id="loan-maturity-title" className="text-base font-semibold">Loan maturity concentration</h2>
    <p className="text-sm text-muted-foreground">Current principal outstanding by contractual maturity. This is exposure concentration, not scheduled repayments or projected refinancing. All recorded facilities are included, including closed or zero-balance records. Currency panels use separate scales; no FX conversion.</p>
    <p className="text-xs text-muted-foreground">Overdue boundary: {today} (UTC calendar day). The portfolio response does not supply a balance snapshot timestamp. Bar length shows balance magnitude; signed labels identify negative balances.</p>
    {currencies.length ? currencies.map(currency => {
      const rows = buckets.filter(bucket => bucket.currency === currency);
      const max = Math.max(1, ...rows.map(bucket => Math.abs(bucket.amount ?? 0)));
      return <div key={currency} className="space-y-2" role="group" aria-label={`${currency} maturity concentration`}>
        <h3 className="text-sm font-semibold">{currency} · current principal</h3>
        {rows.map(bucket => <button key={bucket.id} type="button" aria-pressed={selectedId === bucket.id} aria-label={`Inspect ${label(bucket)}`} onClick={() => setSelectedId(bucket.id)} className="grid w-full grid-cols-[8rem_1fr] items-center gap-x-3 rounded-sm border border-border px-3 py-2 text-left text-sm focus-visible:outline-2 focus-visible:outline-primary sm:grid-cols-[8rem_1fr_14rem]">
          <span>{bucket.period}<span className="block text-xs text-muted-foreground">{bucket.loans.length} facilities</span></span>
          <span className="relative block h-5 border-l border-foreground/60 bg-secondary" aria-hidden="true"><span className={`absolute inset-y-0 left-0 ${bucket.amount != null && bucket.amount < 0 ? "bg-warning" : "bg-primary"}`} style={{ width: `${Math.abs(bucket.amount ?? 0) / max * 100}%` }} /></span>
          <span className="col-span-2 font-mono sm:col-span-1 sm:text-right">{amount(bucket.amount, currency)}{bucket.missingAmountCount ? " + unknown" : ""}</span>
        </button>)}
      </div>;
    }) : <p className="text-sm">No facilities are available for maturity analysis.</p>}
    {selected ? <section aria-label="Selected maturity facilities" className="space-y-2">
      <div className="flex items-center justify-between gap-3"><h3 className="text-sm font-semibold">{label(selected)}</h3><Button size="sm" variant="outline" onClick={() => setSelectedId(null)}>Clear maturity selection</Button></div>
      <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="sr-only">Facilities in selected maturity bucket</caption><thead><tr>{["Borrower / facility", "Contractual maturity", "Outstanding", "Commitment", "Loan identity"].map(title => <th key={title} scope="col" className="border-b p-2">{title}</th>)}</tr></thead><tbody>{selected.loans.map(loan => <tr key={loan.loanId}><th scope="row" className="border-b p-2 font-normal">{loan.borrowerName}<br />{loan.facilityName}</th><td className="border-b p-2">{loan.maturityDate || "Unknown"}</td><td className="border-b p-2 font-mono">{selected.currency === "Unspecified currency" ? `${loan.principalOutstanding ?? "Unavailable"} (denomination unverified)` : amount(loan.principalOutstanding, selected.currency)}</td><td className="border-b p-2 font-mono">{selected.currency === "Unspecified currency" ? "Denomination unverified" : amount(loan.commitmentAmount, selected.currency)}</td><td className="border-b p-2 font-mono text-xs">{loan.loanId}</td></tr>)}</tbody></table></div>
    </section> : <p className="text-xs text-muted-foreground">Select a maturity bar to inspect its borrower, facility, contractual date and retained amounts.</p>}
  </section>;
}
