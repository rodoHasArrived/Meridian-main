import { Link } from "react-router-dom";
import { ArrowRight } from "lucide-react";
import { workstationRouteWithQuery } from "@/lib/workspace";
import type { LedgerJournalExecutionLineage } from "@/types";

/**
 * Where a posted journal entry came from: the order, fill and strategy run its metadata recorded,
 * then this entry. Each step links only where a destination exists (the order opens the Trading
 * execution journal; a run opens its Run Ledger); the fill has no page of its own and is shown as
 * its identifier. Nothing here is matched by symbol, amount or time.
 */
export function JournalExecutionLineage({ lineage, journalEntryId }: { lineage: LedgerJournalExecutionLineage; journalEntryId: string }) {
  const steps: { key: string; label: string; value: string; href: string | null }[] = [];
  if (lineage.strategyRunId) {
    steps.push({ key: "run", label: "Strategy run", value: lineage.strategyRunId, href: workstationRouteWithQuery("strategyRunLedger", { runId: lineage.strategyRunId }) });
  }
  if (lineage.orderId) {
    steps.push({ key: "order", label: "Order", value: lineage.orderId, href: workstationRouteWithQuery("tradingOrders", { orderId: lineage.orderId }) });
  }
  if (lineage.fillId) {
    steps.push({ key: "fill", label: "Fill", value: lineage.fillId, href: null });
  }
  steps.push({ key: "journal", label: "Journal entry", value: journalEntryId, href: null });

  return (
    <section aria-labelledby="journal-lineage-title" className="space-y-2">
      <h3 id="journal-lineage-title" className="text-sm font-semibold">Posted from execution</h3>
      <ol className="flex flex-wrap items-center gap-2 text-xs" aria-label="Execution lineage">
        {steps.map((step, index) => (
          <li key={step.key} className="flex items-center gap-2">
            {index > 0 ? <ArrowRight className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" /> : null}
            <span className="rounded-[var(--radius-card,2px)] border border-border px-2 py-1">
              <span className="text-muted-foreground">{step.label} </span>
              {step.href ? (
                <Link to={step.href} className="font-mono text-foreground underline underline-offset-4">{step.value}</Link>
              ) : (
                <span className="font-mono text-foreground" aria-current={step.key === "journal" ? "page" : undefined}>{step.value}</span>
              )}
            </span>
          </li>
        ))}
      </ol>
      <p className="text-xs text-muted-foreground">As recorded on the posting. The order opens in Trading › Orders for the fund account in scope.</p>
    </section>
  );
}
