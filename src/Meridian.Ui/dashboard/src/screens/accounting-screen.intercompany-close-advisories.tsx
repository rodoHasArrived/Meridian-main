import { Link } from "react-router-dom";
import type { IntercompanyObligationMonitor } from "@/types/intercompany-obligations";

/** Advisory evidence never enters close blocker counts or readiness calculations. */
export function IntercompanyCloseAdvisories({ result }: { result?: IntercompanyObligationMonitor | null }) {
  const query = new URLSearchParams({ entityTab: "obligations" });
  const books = result?.sourceVersions.filter((source) => source.sourceKind === "ledger-book") ?? [];
  const bookId = result?.balanceSchedule?.ledgerBookId ?? (books.length === 1 ? books[0].sourceId : null);
  if (bookId) query.set("ledgerBookId", bookId);
  if (result?.balanceSchedule?.fundProfileId) query.set("fundProfileId", result.balanceSchedule.fundProfileId);
  if (result?.asOf) query.set("obligationAsOf", result.asOf);
  return <section className="space-y-2 rounded-md border border-border p-3" aria-label="Nonblocking intercompany advisories">
    <div className="flex flex-wrap items-center justify-between gap-2"><h4 className="font-semibold">Intercompany advisories</h4><Link className="text-sm underline underline-offset-4" to={`/accounting/consolidation?${query}#entity-operations`}>Inspect entity obligations</Link></div>
    <p className="text-sm text-muted-foreground">Advisory evidence does not change close readiness or posting permissions.</p>
    {!result ? <p className="text-sm text-muted-foreground">Intercompany monitoring is unavailable for this close scope.</p> : <><p className="text-sm">{result.status} · {result.isComplete ? "Complete" : "Incomplete"} · as of {result.asOf} · evaluated {result.evaluatedAtUtc}</p>{result.findings.length ? <ul className="space-y-1 text-sm">{result.findings.map((finding, index) => <li key={`${finding.code}:${index}`}>{finding.severity} · {finding.message}</li>)}</ul> : <p className="text-sm">No intercompany advisory findings were returned.</p>}</>}
  </section>;
}
