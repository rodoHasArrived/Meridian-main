import { WorkspaceTaskWorkbench } from "@/components/meridian/workspace-task-workbench";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { useDataViewModel, type DataOperationsRouteFocusCardState } from "@/screens/data-screen.view-model";
import { eyebrowClassName } from "@/design-system/label-case";
import { pluralizeCount } from "@/lib/format";
import { DesignSystemStatus, worstDesignSystemSeverity } from "@/design-system/status";
import { ProviderFreshnessChart } from "@/components/meridian/workstation-evidence-charts";
import type { ProviderConnectionRow, ProviderRoutingConnection } from "@/types";
import { ProviderVerificationTimeline } from "@/components/meridian/provider-verification-timeline";

export function DataOverviewHub({
  vm,
  degradedPanelCount,
  providerEvidence = [],
  connections = []
}: {
  vm: ReturnType<typeof useDataViewModel>;
  degradedPanelCount: number;
  providerEvidence?: ProviderConnectionRow[];
  connections?: ProviderRoutingConnection[];
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
      status: pluralizeCount(vm.uploadPanelState.templateOptions.length, "template")
    },
    {
      id: "backfills",
      title: "Backfill queue",
      description: "Inspect historical repair pressure and selected job evidence.",
      href: WORKSTATION_ROUTE_CATALOG.dataBackfills,
      status: pluralizeCount(vm.backfillSection.rows.length, "job")
    },
    {
      id: "exports",
      title: "Export packages",
      description: "Review retained export records and downstream handoff posture.",
      href: WORKSTATION_ROUTE_CATALOG.dataExports,
      status: pluralizeCount(vm.exportSection.rows.length, "package")
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
        <DesignSystemStatus status={worstDesignSystemSeverity([
          ...vm.providerSection.rows.map((provider) => provider.status),
          degradedPanelCount > 0 ? "action" : "ready",
          vm.providerSection.rows.length === 0 ? "review" : "ready"
        ])}>
          {vm.providerSection.statusLabel}{degradedPanelCount > 0 ? ` · ${degradedPanelCount} diagnostics unavailable` : ""}
        </DesignSystemStatus>
      </div>
      <section aria-label="Provider health overview" className="space-y-3">
        <h2 className="text-sm font-semibold">Provider health</h2>
        {vm.providerSection.rows.length === 0 ? (
          <p role="status" className="text-sm text-muted-foreground">No provider evidence is loaded. <Link to={WORKSTATION_ROUTE_CATALOG.dataProviders} className="underline underline-offset-4">Review provider connections</Link>.</p>
        ) : (
          <div className="grid gap-3 lg:grid-cols-2">
            {vm.providerSection.rows.map((provider) => (
              <article key={provider.rowId} aria-label={`${provider.provider} health`} className="min-w-0 border border-border bg-card p-4">
                <div className="flex items-start justify-between gap-3">
                  <h3 className="text-sm font-medium">{provider.provider}</h3>
                  <DesignSystemStatus status={provider.status}>{provider.status}</DesignSystemStatus>
                </div>
                <dl className="mt-3 grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 text-xs">
                  <dt className="text-muted-foreground">Last successful evidence</dt><dd>{provider.lastSuccessfulText}</dd>
                  <dt className="text-muted-foreground">Verification</dt><dd>{provider.verificationText}</dd>
                  <dt className="text-muted-foreground">Latency</dt><dd>{provider.latencyText}</dd>
                  <dt className="text-muted-foreground">Affected workflows</dt><dd>{provider.affectedWorkflowsText}</dd>
                </dl>
                <p className="mt-3 text-xs leading-5 text-muted-foreground">{provider.recommendedActionText}</p>
                <Link to={provider.actionHref} className="mt-2 inline-block text-xs text-foreground underline decoration-dotted underline-offset-4">{provider.actionLabel}</Link>
              </article>
            ))}
          </div>
        )}
      </section>
      <ProviderFreshnessChart providers={providerEvidence} />
      <ProviderVerificationTimeline connections={connections} />
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
        <div className={eyebrowClassName(state.eyebrow)}>{state.eyebrow}</div>
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
