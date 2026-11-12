import { useId, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import type { ProviderFixedIncomeConnectionEvidence, ProviderFixedIncomeTaskCapability } from "@/types";
import type { CapabilityMatrixRowModel } from "./data-screen.capability-matrix.view-model";

const TASK_LABELS: Record<string, string> = {
  Terms: "Terms", EvaluatedPrices: "Evaluated prices", ExecutableQuotes: "Executable quotes",
  Trades: "Trades", Curves: "Curves", Fixings: "Fixings", Factors: "Factors", LifecycleNotices: "Lifecycle notices"
};

interface FixedIncomeReadinessRow {
  id: string;
  task: ProviderFixedIncomeTaskCapability;
  connection: ProviderFixedIncomeConnectionEvidence | null;
}

const READINESS_COLUMNS: DenseDataTableColumn<FixedIncomeReadinessRow>[] = [
  {
    id: "task", label: "Input task", rowHeader: true, className: "align-top font-medium",
    render: ({ task }) => TASK_LABELS[task.task] ?? task.task
  },
  {
    id: "coverage", label: "Adapter coverage", className: "max-w-72 align-top",
    render: ({ task }) => <>
      <span>{task.adapterSupport === "AdapterDeclared" ? "Adapter declared · review required" : "Unsupported"}</span>
      <details className="mt-1 text-muted-foreground"><summary className="cursor-pointer">Coverage details</summary><p className="mt-1">{task.detail}</p></details>
    </>
  },
  {
    id: "connection", label: "Configured connection", className: "align-top",
    render: ({ connection }) => connection
      ? <><span>{connection.displayName}</span><span className="block font-mono text-muted-foreground">{connection.connectionId}</span><span className="block">{connection.configurationState}</span></>
      : "No configured connection"
  },
  {
    id: "entitlement", label: "Entitlement", className: "align-top",
    render: ({ connection }) => connection?.entitlementState ?? "No connection evidence"
  },
  {
    id: "freshness", label: "Freshness", className: "align-top",
    render: ({ connection }) => <>
      {connection?.freshnessState ?? "No observation"}
      {connection?.observationAsOf ? <time className="block" dateTime={connection.observationAsOf}>{connection.observationAsOf}</time> : null}
    </>
  },
  {
    id: "verification", label: "Verification evidence", className: "max-w-80 align-top",
    render: ({ task, connection }) => <>
      {connection?.verificationState ?? (task.adapterSupport === "Unsupported" ? "unsupported" : "live-untested")}
      {connection ? <details className="mt-1 text-muted-foreground"><summary className="cursor-pointer">Inspect evidence</summary><ul className="mt-1 grid gap-1">{connection.evidence.map((evidence, index) => <li key={`${evidence.kind}-${index}`}><strong>{evidence.kind}</strong>: {evidence.detail}{evidence.observedAt ? <time className="block" dateTime={evidence.observedAt}>{evidence.observedAt}</time> : null}</li>)}</ul></details> : null}
    </>
  }
];

/** Declarations and retained connection evidence remain separate at every task boundary. */
export function FixedIncomeCapabilityReadiness({ rows }: { rows: CapabilityMatrixRowModel[] }) {
  const selectId = useId();
  const providers = rows.filter((row) => row.fixedIncomeTasks.length > 0);
  const [providerId, setProviderId] = useState("ibkr");
  const selected = providers.find((row) => row.providerId === providerId) ?? providers[0];
  if (!selected) return null;
  const taskRows: FixedIncomeReadinessRow[] = selected.fixedIncomeTasks.flatMap((task) =>
    (task.connections.length ? task.connections : [null]).map((connection) => ({
      id: `${task.task}-${connection?.connectionId ?? "none"}`, task, connection
    })));

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
      <DenseDataTable
        columns={READINESS_COLUMNS}
        rows={taskRows}
        getRowId={(row) => row.id}
        ariaLabel={`${selected.providerId} fixed-income task readiness`}
        emptyText="No fixed-income task declarations are available."
      />
      <p className="text-xs text-muted-foreground">Unknown means task-specific evidence has not been retained. General connection certification does not verify a bond, quote, fixing, factor, or curve input.</p>
    </div>
  );
}
