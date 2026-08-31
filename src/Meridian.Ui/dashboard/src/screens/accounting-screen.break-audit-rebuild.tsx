/**
 * Break audit rebuild check for the reconciliation break detail panel.
 *
 * The rebuild route replays a break's audit trail and returns the state that
 * trail implies. Run on demand — replaying a trail is not something to do on
 * every row selection — it answers the one question the stored record cannot:
 * whether the break agrees with its own audit history.
 */

import { useCallback, useState } from "react";
import { History } from "lucide-react";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { getReconciliationBreakRebuiltSnapshot } from "@/lib/api/break-audit-rebuild.api";
import { getReconciliationBreakDetail } from "@/lib/api";
import {
  buildBreakAuditRebuildViewModel,
  type BreakAuditRebuildViewModel
} from "@/screens/accounting-screen.break-audit-rebuild.view-model";

export interface BreakAuditRebuildCheckProps {
  breakId: string | null;
}

export function BreakAuditRebuildCheck({ breakId }: BreakAuditRebuildCheckProps) {
  const [view, setView] = useState<BreakAuditRebuildViewModel | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const rebuild = useCallback(async () => {
    if (!breakId) {
      return;
    }

    setBusy(true);
    setError(null);
    setView(null);
    try {
      // Both halves are fetched together so the comparison is between the break
      // as it stands now and the trail as it stands now, not against a copy the
      // panel happened to be holding.
      const [stored, rebuilt] = await Promise.all([
        getReconciliationBreakDetail(breakId),
        getReconciliationBreakRebuiltSnapshot(breakId)
      ]);
      setView(buildBreakAuditRebuildViewModel(stored, rebuilt));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Audit rebuild failed.");
    } finally {
      setBusy(false);
    }
  }, [breakId]);

  return (
    <div className="space-y-2">
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={() => void rebuild()}
        disabled={!breakId || busy}
        aria-label={breakId
          ? `Rebuild break ${breakId} from its audit trail and compare it with the stored record.`
          : "Rebuild from audit trail. No break is selected."}
      >
        <History className="mr-2 h-4 w-4" />
        {busy ? "Rebuilding…" : "Rebuild from audit"}
      </Button>

      {error ? <StatusBanner role="alert" tone="danger" title="Audit rebuild failed" detail={error} /> : null}

      {view?.compared ? (
        <div className="space-y-2">
          <StatusBanner
            role="status"
            tone={view.matches ? "success" : "danger"}
            title={view.matches ? "Audit trail agrees" : "Audit trail disagrees"}
            detail={view.verdict}
          />
          {view.differences.length > 0 ? (
            <table className="w-full text-xs" aria-label="Fields where the stored break and its audit rebuild differ">
              <caption className="sr-only">
                Each row is a field whose stored value does not match the value implied by the audit trail.
              </caption>
              <thead>
                <tr className="text-left text-muted-foreground">
                  <th scope="col" className="py-1 pr-3 font-medium">Field</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Stored</th>
                  <th scope="col" className="py-1 font-medium">From audit</th>
                </tr>
              </thead>
              <tbody>
                {view.differences.map((difference) => (
                  <tr key={difference.field} className="border-t border-border/60">
                    <th scope="row" className="py-1 pr-3 text-left font-mono font-normal text-foreground">
                      {difference.field}
                    </th>
                    <td className="py-1 pr-3 font-mono text-danger">{difference.storedValue}</td>
                    <td className="py-1 font-mono text-muted-foreground">{difference.rebuiltValue}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
          {view.notReconstructedNotice ? (
            <p className="text-xs text-muted-foreground">{view.notReconstructedNotice}</p>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
