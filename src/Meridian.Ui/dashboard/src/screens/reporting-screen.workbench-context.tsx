import { Network } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { WorkspaceTabStrip } from "@/components/meridian/workspace-primitives";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  ReportingChipViewModel,
  ReportingWorkbenchAction
} from "@/screens/reporting-screen.view-model";
import type {
  ReportingTaskModeId,
  ReportingTaskModeViewModel
} from "@/screens/reporting-screen.task-mode-view-model";

export interface ReportingWorkbenchContextProps {
  taskMode: ReportingTaskModeViewModel;
  actions: ReportingWorkbenchAction[];
}

const reportingRouteTabs: { id: ReportingTaskModeId; label: string; route: string }[] = [
  { id: "daily-reporting-cockpit", label: "Overview", route: WORKSTATION_ROUTE_CATALOG.reporting },
  { id: "report-builder", label: "Report Builder", route: WORKSTATION_ROUTE_CATALOG.reportingReportBuilder },
  { id: "run-status", label: "Run Status", route: WORKSTATION_ROUTE_CATALOG.reportingRunStatus },
  { id: "report-pack-approval", label: "Report packs", route: WORKSTATION_ROUTE_CATALOG.reportingReportPacks },
  { id: "exports", label: "Exports", route: WORKSTATION_ROUTE_CATALOG.reportingExports },
  { id: "governance", label: "Governance", route: WORKSTATION_ROUTE_CATALOG.reportingGovernance }
];

export function ReportingWorkbenchContext({ taskMode, actions }: ReportingWorkbenchContextProps) {
  const navigate = useNavigate();
  const { search } = useLocation();
  const routeTabs = reportingRouteTabs.map((tab) => ({
    id: tab.id,
    label: tab.label,
    selected:
      tab.id === taskMode.id ||
      (tab.id === "report-pack-approval" && taskMode.id === "delivery-evidence")
  }));

  return (
    <section
      role="region"
      aria-label="Reporting workbench context"
      className="flex flex-wrap items-end justify-between gap-3"
    >
      <div className="min-w-0">
        <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
          {taskMode.label}
        </h2>
        <p className="mt-0.5 max-w-3xl text-xs leading-5 text-muted-foreground">
          {taskMode.description}
        </p>
      </div>
      <div className="flex flex-wrap items-center justify-end gap-2">
        {actions.map((action) => (
          <Button key={action.id} asChild variant="outline" size="sm">
            <Link to={action.href} aria-label={action.ariaLabel}>
              <Network className="h-4 w-4" aria-hidden="true" />
              {action.label}
            </Link>
          </Button>
        ))}
        <WorkspaceTabStrip
          label="Reporting routes"
          tabs={routeTabs}
          onSelect={(id) => {
            const tab = reportingRouteTabs.find((candidate) => candidate.id === id);
            if (tab) {
              // Preserve the querystring: the operating scope is threaded
              // through search params across the shell.
              navigate({ pathname: tab.route, search });
            }
          }}
        />
      </div>
    </section>
  );
}

export function ReportingChip({ label, value }: ReportingChipViewModel) {
  return (
    <div className="toolbar-chip" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}
