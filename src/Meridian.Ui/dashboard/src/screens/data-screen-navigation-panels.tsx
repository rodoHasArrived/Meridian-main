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
      href: WORKSTATION_ROUTE_CATALOG.dataEvidence,
      status: "Governed"
    }
  ];

  return (
    <section aria-labelledby="data-overview-hub-title" className="space-y-4">
      <Card className="border-border/80 bg-secondary/15">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Data command center</div>
              <CardTitle id="data-overview-hub-title" className="mt-2">Choose the next Data task</CardTitle>
              <CardDescription className="mt-2 max-w-3xl">
                Provider management, file intake, historical repair, exports, and querying stay in focused workspaces. Detailed diagnostics remain available below.
              </CardDescription>
            </div>
            <Badge variant={degradedPanelCount > 0 ? "warning" : "success"}>
              {degradedPanelCount > 0 ? `${degradedPanelCount} diagnostics unavailable` : "Diagnostics ready"}
            </Badge>
          </div>
        </CardHeader>
      </Card>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label="Data task routes">
        {routes.map((route) => (
          <Card key={route.id} className="h-full border-border/70">
            <CardHeader>
              <div className="flex items-start justify-between gap-3">
                <CardTitle className="text-base">{route.title}</CardTitle>
                <Badge variant="outline">{route.status}</Badge>
              </div>
              <CardDescription>{route.description}</CardDescription>
            </CardHeader>
            <CardContent>
              <Button asChild variant="outline" size="sm">
                <Link to={route.href}>Open {route.title}</Link>
              </Button>
            </CardContent>
          </Card>
        ))}
      </div>
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
