import { useEffect, useState } from "react";
import { ChartCard } from "@/components/charts/ChartCard";
import { StatusTimeline } from "@/components/charts/StatusTimeline";
import { Button } from "@/components/ui/button";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { getProviderVerificationHistory, type ProviderVerificationHistory } from "@/lib/api/provider-verification-history.api";
import type { ProviderRoutingConnection } from "@/types";

export function ProviderVerificationTimeline({ connections }: { connections: ProviderRoutingConnection[] }) {
  const [chosenId, setChosenId] = useState("");
  const selected = connections.find((connection) => connection.connectionId === chosenId) ?? connections[0];
  const connectionId = selected?.connectionId ?? null;
  const [result, setResult] = useState<{ connectionId: string; history?: ProviderVerificationHistory; error?: string } | null>(null);
  const [retry, setRetry] = useState(0);
  const active = result?.connectionId === connectionId ? result : null;
  useEffect(() => {
    if (!connectionId) return;
    const controller = new AbortController();
    setResult(null);
    void getProviderVerificationHistory(connectionId, { signal: controller.signal }).then(
      (history) => { if (!controller.signal.aborted) setResult({ connectionId, history }); },
      () => { if (!controller.signal.aborted) setResult({ connectionId, error: "Retained verification history is unavailable for this connection. Current connection state does not establish historical uptime." }); }
    );
    return () => controller.abort();
  }, [connectionId, retry]);

  return <section aria-label="Provider verification history" className="space-y-3">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h3 className="text-sm font-semibold">Provider verification history</h3>
      {connections.length > 0 ? <label className="text-xs">Connection <select className="ml-2 border border-border bg-background p-2" aria-label="Verification history connection"
        value={connectionId ?? ""} onChange={(event) => setChosenId(event.target.value)}>
        {connections.map((connection) => <option key={connection.connectionId} value={connection.connectionId}>{connection.displayName}</option>)}
      </select></label> : null}
    </div>
    {!connectionId ? <p className="text-xs text-muted-foreground">No owned connection is loaded. Select a configured connection to inspect its retained verification events.</p>
      : active?.error ? <div role="status" className="text-sm"><p>{active.error}</p><Button size="sm" variant="outline" onClick={() => setRetry((value) => value + 1)}>Retry verification history</Button></div>
      : !active?.history ? <p role="status" className="text-xs text-muted-foreground">Loading retained verification history…</p>
      : active.history.observations.length === 0 ? <p role="status" className="text-xs text-muted-foreground">No retained verification events exist for this exact connection and environment.</p>
      : <>
        <ChartCard title={`${selected?.displayName} verification events`} subtitle="Retained audit recording times · passed and failed probes only" height={125}>
          <StatusTimeline label="Provider verification events over time" observations={active.history.observations.map((entry) => ({ timestamp: entry.recordedAtUtc, succeeded: entry.succeeded }))} />
        </ChartCard>
        <p className="text-xs text-muted-foreground">Green: passed · Red: failed. Discrete probes do not establish continuous uptime. {active.history.isTruncated ? "Showing the most recent retained events; earlier events are omitted." : "All retained verification events are shown."}</p>
        <details className="workspace-task-disclosure"><summary>View verification event table</summary>
          <TableWorkbench workbenchId="provider-verification-events" sourceKey={connectionId} sourceDescription="Retained verification event window for this exact owned connection and environment." completePopulation={!active.history.isTruncated} tableId="provider-verification-events" ariaLabel="Provider verification events" rows={active.history.observations.map((entry, index) => ({ ...entry, id: String(index) }))}
            getRowId={(row) => row.id} columns={[{ id: "recorded", label: "Recorded at UTC", render: (row) => row.recordedAtUtc, exportValue: (row) => row.recordedAtUtc }, { id: "result", label: "Result", render: (row) => row.succeeded ? "Passed" : "Failed", exportValue: (row) => row.succeeded }]} emptyText="No retained events." />
        </details>
      </>}
  </section>;
}
