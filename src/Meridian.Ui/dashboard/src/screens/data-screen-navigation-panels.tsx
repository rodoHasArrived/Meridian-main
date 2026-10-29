import { WorkspaceTaskWorkbench } from "@/components/meridian/workspace-task-workbench";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { useDataViewModel, type DataOperationsRouteFocusCardState } from "@/screens/data-screen.view-model";

export function DataOverviewHub({
  vm,
  degradedPanelCount
}: {
  vm: ReturnType<typeof useDataViewModel>;
  degradedPanelCount: number;
}) {
  const routes = [
    {
      id: "providers",
      title: "Provider connections",
      description: "Review credentials, connection health, routing trust, and recovery actions.",
      href: WORKSTATION_ROUTE_CATALOG.dataProviders,
      status: `${vm.providerSection.rows.length} configured`
    },
    {
      id: "import",
      title: "Import retained files",
      description: "Choose a governed template and preview a retained source before validation handoff.",
      href: WORKSTATION_ROUTE_CATALOG.dataImport,
      status: `${vm.uploadPanelState.templateOptions.length} templates`
    },
    {
      id: "backfills",
      title: "Backfill queue",
      description: "Inspect historical repair pressure and selected job evidence.",
      href: WORKSTATION_ROUTE_CATALOG.dataBackfills,
      status: `${vm.backfillSection.rows.length} jobs`
    },
    {
      id: "exports",
      title: "Export packages",
      description: "Review retained export records and downstream handoff posture.",
      href: WORKSTATION_ROUTE_CATALOG.dataExports,
      status: `${vm.exportSection.rows.length} packages`
    },
    {
      id: "query",
      title: "SQL query",
      description: "Run read-only discovery queries against the workstation store.",
      href: WORKSTATION_ROUTE_CATALOG.dataQuery,
      status: "Read only"
    },
    {
      id: "evidence",
      title: "Evidence workbench",
      description: "Inspect retained documents, request lists, lineage, and completeness.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      status: "Governed"
    }
  ];

  const jobs = vm.backfillSection.rows.map((row) => ({
    id: row.jobId,
    title: row.jobId,
    description: `${row.provider} · ${row.scope}`,
    status: row.status,
    href: WORKSTATION_ROUTE_CATALOG.dataBackfills,
    actionLabel: "Open Backfill queue",
    facts: [
      { label: "Provider", value: row.provider },
      { label: "Scope", value: row.scope },
      { label: "Progress", value: row.progress },
      { label: "Updated", value: row.updatedAt }
    ]
  }));
  return (
    <section className="space-y-3" aria-label="Data operations workbench">
      <div role="status" className="workspace-task-health">
        <strong>Data operations</strong>
        <Badge variant={degradedPanelCount > 0 ? "warning" : "success"}>
          {degradedPanelCount > 0 ? `${degradedPanelCount} diagnostics unavailable` : "Diagnostics ready"}
        </Badge>
      </div>
      <WorkspaceTaskWorkbench title="Historical repair jobs" description="Inspect loaded backfill work and its scope before opening the recovery workstream." items={jobs} stateKey="dataJob" emptyText="No historical repair jobs are loaded. Import data or open Backfill queue to review available work." />
      <details className="workspace-task-disclosure">
        <summary>Data tools and connections</summary>
        <WorkspaceTaskWorkbench title="Data tasks" description="File intake, providers, exports, and read-only discovery." items={routes.map((route) => ({ ...route, actionLabel: `Open ${route.title}` }))} stateKey="dataTask" />
      </details>
    </section>
  );
}

export function RouteFocusCard({ state }: { state: DataOperationsRouteFocusCardState }) {
  return (
    <Card id={state.id} role={state.role} aria-label={state.ariaLabel} className="panel-surface-strong">
      <CardHeader>
        <div className="eyebrow-label">{state.eyebrow}</div>
        <CardTitle>{state.title}</CardTitle>
        <CardDescription>{state.description}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        {state.rows.length > 0 ? (
          <dl className="space-y-2">
            {state.rows.map((row) => (
              <div key={row.id} className="flex items-center justify-between gap-4 rounded-lg border border-border/70 bg-secondary/40 px-3 py-2">
                <dt className="text-muted-foreground">{row.label}</dt>
                <dd className="font-mono text-foreground">{row.value}</dd>
              </div>
            ))}
          </dl>
        ) : (
          <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
            {state.description}
          </p>
        )}
        {state.action ? (
          <Button asChild variant="outline" className="w-full justify-center">
            <Link to={state.action.href} aria-label={state.action.ariaLabel}>
              {state.action.label}
            </Link>
          </Button>
        ) : null}
      </CardContent>
    </Card>
  );
}
