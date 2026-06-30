import { Link } from "react-router-dom";
import { WorkspaceFilterBar, type WorkspaceFilterBarField } from "@/components/meridian/workspace-primitives";
import { Button } from "@/components/ui/button";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  AccountingTaskModeViewModel,
  AccountingWorkstream
} from "@/screens/accounting-screen.task-mode-view-model";
import type { WorkspaceSummary } from "@/types";

export interface AccountingChipViewModel {
  label: string;
  value: string;
}

export interface AccountingWorkbenchContextProps {
  workspace: WorkspaceSummary;
  workstream: AccountingWorkstream;
  taskMode: AccountingTaskModeViewModel;
  recoveryFields: WorkspaceFilterBarField[];
  chips: AccountingChipViewModel[];
}

export function AccountingWorkbenchContext({
  workspace,
  workstream,
  taskMode,
  recoveryFields,
  chips
}: AccountingWorkbenchContextProps) {
  return (
    <>
      <section
        id="accounting-overview"
        role="region"
        aria-label={`${workspace.label} workbench context`}
        data-workstream={workstream}
        data-task-mode={taskMode.id}
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">{workspace.label} lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {taskMode.label}
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{taskMode.description}</p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <AccountingChip label="Task mode" value={taskMode.label} />
          <AccountingChip label="Workstream" value={workstream} />
          {chips.map((chip) => (
            <AccountingChip key={chip.label} label={chip.label} value={chip.value} />
          ))}
        </div>
      </section>

      <WorkspaceFilterBar
        label="Accounting recovery navigator"
        searchLabel="Current Accounting route"
        searchValue={`${workspace.label} / ${taskMode.label}`}
        fields={recoveryFields}
        actions={
          <div className="flex flex-wrap gap-2" aria-label="Accounting task-mode routes">
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.accounting}>Close Cockpit</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.accountingReconciliation}>Reconciliation Casework</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.accountingLedger}>Ledger Explorer</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}>Journal Entry</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={WORKSTATION_ROUTE_CATALOG.accountingConfigure}>Governance</Link>
            </Button>
          </div>
        }
      />
    </>
  );
}

export function AccountingChip({ label, value }: AccountingChipViewModel) {
  return (
    <span className="toolbar-chip" role="group" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono capitalize text-foreground">{value}</span>
    </span>
  );
}
