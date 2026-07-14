import { useLocation, useNavigate } from "react-router-dom";
import { WorkspaceTabStrip } from "@/components/meridian/workspace-primitives";
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
}

/**
 * Route-scoped tabs for the catchall AccountingScreen task modes. Dedicated
 * screens (Ledger, Trial Balance, Close calendar, Entity setup, Statement
 * import, Evidence) stay sidebar-only, matching the Trading/Data pattern of
 * one tab per in-screen route view.
 */
const accountingRouteTabs: { id: string; label: string; route: string; workstreams: AccountingWorkstream[] }[] = [
  { id: "close", label: "Close", route: WORKSTATION_ROUTE_CATALOG.accounting, workstreams: ["ledger"] },
  { id: "reconciliation", label: "Reconciliation", route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation, workstreams: ["reconciliation"] },
  { id: "journal-entries", label: "Adjustments", route: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries, workstreams: ["journal-entries"] },
  { id: "capital-accounts", label: "Capital accounts", route: WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts, workstreams: ["capital-accounts"] },
  { id: "exceptions", label: "Exceptions", route: WORKSTATION_ROUTE_CATALOG.accountingExceptions, workstreams: ["exceptions"] },
  { id: "approvals", label: "Approvals", route: WORKSTATION_ROUTE_CATALOG.accountingApprovals, workstreams: ["approvals"] },
  { id: "security-master", label: "Data health", route: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster, workstreams: ["security-master"] },
  { id: "configure", label: "Reports", route: WORKSTATION_ROUTE_CATALOG.accountingConfigure, workstreams: ["configure"] },
  // The reporting workstream owns the showReporting-gated profile band, so it
  // gets its own tab: a "Reports" tab that navigated to /accounting/configure
  // while selected for /accounting/reporting would hide that band on click.
  { id: "reporting", label: "Delivery evidence", route: `${WORKSTATION_ROUTE_CATALOG.accounting}/reporting`, workstreams: ["reporting"] }
];

export function AccountingWorkbenchContext({
  workspace,
  workstream,
  taskMode
}: AccountingWorkbenchContextProps) {
  const navigate = useNavigate();
  const { search } = useLocation();
  const tabs = accountingRouteTabs.map((tab) => ({
    id: tab.id,
    label: tab.label,
    selected: tab.workstreams.includes(workstream)
  }));

  return (
    <section
      id="accounting-overview"
      role="region"
      aria-label={`${workspace.label} workbench context`}
      data-workstream={workstream}
      data-task-mode={taskMode.id}
      className="flex flex-wrap items-end justify-between gap-3"
    >
      <div className="min-w-0">
        <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
          {taskMode.label}
        </h2>
        <p className="mt-0.5 max-w-3xl text-xs leading-5 text-muted-foreground">{taskMode.description}</p>
      </div>
      <WorkspaceTabStrip
        label="Accounting routes"
        tabs={tabs}
        onSelect={(id) => {
          const tab = accountingRouteTabs.find((candidate) => candidate.id === id);
          if (tab) {
            // Preserve the querystring: the operating scope is threaded
            // through search params across the shell.
            navigate({ pathname: tab.route, search });
          }
        }}
      />
    </section>
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
