import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { SecurityReportInputCompare } from "@/components/meridian/security-report-input-compare";
import { getReportSecurityInputLinks, type SecurityReportInputLink } from "@/lib/api/security-report-inputs.api";
import type { ReportLedgerAmountBinding } from "@/types/report-amount-proof";

/** The report-line link uses retained journal identities, never ticker or current-price guesses. */
export function ReportAmountSecurityInputs({ runId, bindings }: { runId: string; bindings: ReportLedgerAmountBinding[] }) {
  const [result, setResult] = useState<{ runId: string; links: SecurityReportInputLink[]; error: boolean } | null>(null);
  const [selected, setSelected] = useState<{ runId: string; securityId: string } | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    getReportSecurityInputLinks(runId, controller.signal).then(links => {
      if (!controller.signal.aborted) setResult({ runId, links, error: false });
    }).catch(() => { if (!controller.signal.aborted) setResult({ runId, links: [], error: true }); });
    return () => controller.abort();
  }, [runId]);
  const current = result?.runId === runId ? result : null;
  if (!current) return <p role="status" className="text-xs text-muted-foreground">Loading report-line security input links…</p>;
  if (current.error) return <p className="text-xs text-muted-foreground">Security input links are unavailable for this report or current access.</p>;
  const rows = bindings.flatMap(binding => [...new Set(current.links.filter(link => binding.journalEntryIds.includes(link.journalEntryId)).map(link => link.securityId))]
    .map(securityId => ({ label: binding.label, amountId: binding.amountId, securityId })));
  return <div className="space-y-3">
    {rows.length > 0 ? <><h3 className="text-sm font-semibold">Security inputs behind report amounts</h3><ul className="grid gap-2">{rows.map(row => <li key={`${row.amountId}:${row.securityId}`} className="flex flex-wrap items-center gap-3 text-sm">
      <span>{row.label}</span><Button size="sm" variant="outline" onClick={() => setSelected({ runId, securityId: row.securityId })}>Compare retained security inputs</Button>
    </li>)}</ul></> : <p className="text-xs text-muted-foreground">No security identities are retained on these report amounts.</p>}
    {selected?.runId === runId ? <SecurityReportInputCompare key={`${runId}:${selected.securityId}`} securityId={selected.securityId} runId={runId} /> : null}
  </div>;
}
