import { useEffect, useState } from "react";
import { ChartCard } from "@/components/charts/ChartCard";
import { SegmentedLineChart } from "@/components/charts/SegmentedLineChart";
import { Button } from "@/components/ui/button";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { getCloseChecklistHistory, type CloseChecklistHistory } from "@/lib/api/close-checklist-history.api";
import { TimeText } from "@/components/ui/time-text";

export function CloseChecklistHistoryView({ history }: { history: CloseChecklistHistory }) {
  const points = history.observations;
  const latest = points.at(-1);
  return <section aria-label="Retained close task history" className="space-y-3">
    <h3 className="text-sm font-semibold">Close task burndown</h3>
    {history.status !== "Ready" || !latest ? <p role="status" className="text-sm text-muted-foreground">{history.reason ?? "No retained checklist snapshots are available. Coverage begins with new canonical committed transitions."}</p>
      : <>
        <p className="text-xs text-muted-foreground">{latest.remainingTasks} of {latest.totalTasks} tasks remaining · {latest.blockedTasks} blocked · as of <TimeText value={latest.recordedAtUtc} fallback="Timestamp unavailable" /></p>
        {points.length >= 2 ? <ChartCard title="Remaining close tasks" subtitle="Retained committed workflow snapshots · gaps remain unconnected" height={215}>
          <SegmentedLineChart label="Remaining close tasks over retained history" maximum={Math.max(...points.map((point) => point.totalTasks))}
            points={points.map((point) => ({ timestamp: point.recordedAtUtc, value: point.remainingTasks, segment: point.segment }))} />
        </ChartCard> : <p role="status" className="text-xs text-muted-foreground">One retained snapshot is available. A trend requires a second committed observation.</p>}
        {history.isPartial ? <p role="status" className="text-xs text-muted-foreground">Partial coverage: {history.missingAuditIds.length} event(s) have no checklist snapshot; {history.versionGaps.length} workflow-version gap(s). Lines connect only observations in the same retained segment.</p> : null}
        <details className="workspace-task-disclosure"><summary>View retained checklist history table</summary>
          <TableWorkbench workbenchId="close-checklist-history" sourceKey={history.workflowId} completePopulation={!history.isPartial}
            sourceDescription="Retained committed checklist snapshots for this exact workflow; no legacy states are reconstructed."
            ariaLabel="Retained close checklist observations" rows={points} getRowId={(point) => point.auditId}
            columns={[{ id: "version", label: "Workflow version", render: (point) => point.workflowVersion, exportValue: (point) => point.workflowVersion },
              { id: "recorded", label: "Recorded at", render: (point) => <TimeText value={point.recordedAtUtc} fallback="Timestamp unavailable" />, exportValue: (point) => point.recordedAtUtc },
              { id: "remaining", label: "Remaining tasks", render: (point) => point.remainingTasks, exportValue: (point) => point.remainingTasks },
              { id: "blocked", label: "Blocked tasks", render: (point) => point.blockedTasks, exportValue: (point) => point.blockedTasks },
              { id: "total", label: "Total tasks", render: (point) => point.totalTasks, exportValue: (point) => point.totalTasks },
              { id: "audit", label: "Audit ID", render: (point) => point.auditId, exportValue: (point) => point.auditId }]}
            emptyText="No retained snapshots." />
        </details>
      </>}
  </section>;
}

export function CloseChecklistHistoryChart({ workflowId, workflowVersion }: { workflowId: string; workflowVersion?: number }) {
  const key = `${workflowId}:${workflowVersion ?? "current"}`;
  const [result, setResult] = useState<{ key: string; history?: CloseChecklistHistory; failed?: boolean } | null>(null);
  const [retry, setRetry] = useState(0);
  const active = result?.key === key ? result : null;
  useEffect(() => {
    const controller = new AbortController();
    setResult(null);
    void getCloseChecklistHistory(workflowId, { signal: controller.signal }).then(
      (history) => { if (!controller.signal.aborted) setResult({ key, history }); },
      () => { if (!controller.signal.aborted) setResult({ key, failed: true }); }
    );
    return () => controller.abort();
  }, [key, retry, workflowId]);
  return active?.history ? <CloseChecklistHistoryView history={active.history} /> : <section aria-label="Retained close task history" className="space-y-2">
    <h3 className="text-sm font-semibold">Close task burndown</h3>
    <p role="status" className="text-xs text-muted-foreground">{active?.failed ? "Retained checklist history is unavailable. Current task states remain available; no earlier completion history is inferred." : "Loading retained checklist history…"}</p>
    {active?.failed ? <Button size="sm" variant="outline" onClick={() => setRetry((value) => value + 1)}>Retry close task history</Button> : null}
  </section>;
}
