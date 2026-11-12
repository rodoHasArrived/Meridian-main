import { useId, useState } from "react";
import type { CapabilityMatrixRowModel } from "./data-screen.capability-matrix.view-model";

const TASK_LABELS: Record<string, string> = {
  Terms: "Terms", EvaluatedPrices: "Evaluated prices", ExecutableQuotes: "Executable quotes",
  Trades: "Trades", Curves: "Curves", Fixings: "Fixings", Factors: "Factors", LifecycleNotices: "Lifecycle notices"
};

/** Declarations and retained connection evidence remain separate at every task boundary. */
export function FixedIncomeCapabilityReadiness({ rows }: { rows: CapabilityMatrixRowModel[] }) {
  const selectId = useId();
  const providers = rows.filter((row) => row.fixedIncomeTasks.length > 0);
  const [providerId, setProviderId] = useState("ibkr");
  const selected = providers.find((row) => row.providerId === providerId) ?? providers[0];
  if (!selected) return null;

  return (
    <div className="grid gap-3 rounded-md border p-3" aria-label="Fixed-income input readiness">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">Can this connection supply the inputs this bond needs?</h3>
          <p className="text-xs text-muted-foreground">Review each task. Adapter coverage alone does not establish entitlement or current usable evidence.</p>
        </div>
        <div className="grid gap-1">
          <label htmlFor={selectId} className="text-xs font-medium">Provider</label>
          <select id={selectId} value={selected.providerId} onChange={(event) => setProviderId(event.target.value)} className="rounded-md border bg-background px-2 py-1 text-sm">
            {providers.map((row) => <option key={row.providerId} value={row.providerId}>{row.providerId}</option>)}
          </select>
        </div>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full border-collapse text-xs" aria-label={`${selected.providerId} fixed-income task readiness`}>
          <thead><tr>
            {["Input task", "Adapter coverage", "Configured connection", "Entitlement", "Freshness", "Verification evidence"].map((label) => <th key={label} scope="col" className="border-b p-2 text-left font-semibold">{label}</th>)}
          </tr></thead>
          <tbody>{selected.fixedIncomeTasks.flatMap((task) => {
            const connections = task.connections.length ? task.connections : [null];
            return connections.map((connection) => (
              <tr key={`${task.task}-${connection?.connectionId ?? "none"}`}>
                <th scope="row" className="border-b p-2 text-left align-top font-medium">{TASK_LABELS[task.task] ?? task.task}</th>
                <td className="max-w-72 border-b p-2 align-top">
                  <span>{task.adapterSupport === "AdapterDeclared" ? "Adapter declared · review required" : "Unsupported"}</span>
                  <details className="mt-1 text-muted-foreground"><summary className="cursor-pointer">Coverage details</summary><p className="mt-1">{task.detail}</p></details>
                </td>
                <td className="border-b p-2 align-top">{connection ? <><span>{connection.displayName}</span><span className="block font-mono text-muted-foreground">{connection.connectionId}</span><span className="block">{connection.configurationState}</span></> : "No configured connection"}</td>
                <td className="border-b p-2 align-top">{connection?.entitlementState ?? "No connection evidence"}</td>
                <td className="border-b p-2 align-top">{connection?.freshnessState ?? "No observation"}{connection?.observationAsOf ? <time className="block" dateTime={connection.observationAsOf}>{connection.observationAsOf}</time> : null}</td>
                <td className="max-w-80 border-b p-2 align-top">
                  {connection?.verificationState ?? (task.adapterSupport === "Unsupported" ? "unsupported" : "live-untested")}
                  {connection ? <details className="mt-1 text-muted-foreground"><summary className="cursor-pointer">Inspect evidence</summary><ul className="mt-1 grid gap-1">{connection.evidence.map((evidence, index) => <li key={`${evidence.kind}-${index}`}><strong>{evidence.kind}</strong>: {evidence.detail}{evidence.observedAt ? <time className="block" dateTime={evidence.observedAt}>{evidence.observedAt}</time> : null}</li>)}</ul></details> : null}
                </td>
              </tr>
            ));
          })}</tbody>
        </table>
      </div>
      <p className="text-xs text-muted-foreground">Unknown means task-specific evidence has not been retained. General connection certification does not verify a bond, quote, fixing, factor, or curve input.</p>
    </div>
  );
}
