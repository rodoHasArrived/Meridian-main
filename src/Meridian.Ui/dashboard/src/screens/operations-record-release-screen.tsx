import { ArrowRight, DatabaseZap, FileCheck2, Landmark, Network } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { useOperationsContinuityScreenViewModel } from "@/screens/operations-continuity-screen.view-model";
import { useReportingScreenViewModel } from "@/screens/reporting-screen.view-model";
import {
  buildOperationsRecordReleaseViewModel,
  type OperationsRecordReleaseEvidenceRow,
  type OperationsRecordReleaseMetric,
  type OperationsRecordReleasePanel,
  type OperationsRecordReleaseTone
} from "@/screens/operations-record-release-screen.view-model";
import type { AccountingWorkspaceResponse, DataWorkspaceResponse } from "@/types";

interface OperationsRecordReleaseScreenProps {
  data: DataWorkspaceResponse | null;
  reporting: AccountingWorkspaceResponse | null;
}

const tonePanel: Record<OperationsRecordReleaseTone, string> = {
  ready: "border-success/35 bg-success/10",
  review: "border-warning/35 bg-warning/10",
  blocked: "border-danger/35 bg-danger/10",
  neutral: "border-border/70 bg-secondary/25"
};

export function OperationsRecordReleaseScreen({ data, reporting }: OperationsRecordReleaseScreenProps) {
  const continuity = useOperationsContinuityScreenViewModel();
  const reportingVm = useReportingScreenViewModel(
    reporting?.reporting ?? null,
    undefined,
    WORKSTATION_ROUTE_CATALOG.reportingReportPacks
  );
  const vm = buildOperationsRecordReleaseViewModel({
    data,
    continuity,
    reporting: reportingVm
  });

  return (
    <div className="space-y-6">
      <section
        role="region"
        aria-label="Operations record release overview"
        className={cn("panel-surface-strong border px-4 py-4", tonePanel[vm.tone])}
      >
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="eyebrow-label">{vm.eyebrow}</div>
            <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
              {vm.title}
            </h2>
            <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">{vm.summary}</p>
            <p className="mt-2 max-w-4xl text-xs leading-5 text-muted-foreground">{vm.rootAreaSummary}</p>
          </div>
          <div className="flex flex-col items-start gap-2 sm:items-end">
            <Badge variant={vm.badgeVariant} dot>{vm.statusLabel}</Badge>
            <span className="font-mono text-[11px] text-muted-foreground">{vm.routePathLabel}</span>
          </div>
        </div>
        <p className="mt-4 rounded-md border border-border/70 bg-background/45 px-3 py-2 text-sm leading-6 text-foreground">
          {vm.statusDetail}
        </p>
      </section>

      <section className="grid gap-3 md:grid-cols-2 xl:grid-cols-4" aria-label="Operations record release metrics">
        {vm.metrics.map((metric) => (
          <ReleaseMetric key={metric.id} metric={metric} />
        ))}
      </section>

      <section
        role="region"
        aria-label={vm.stepsLabel}
        className="panel-surface grid gap-4 px-4 py-4"
      >
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div className="eyebrow-label">Demo path</div>
            <h3 className="mt-2 text-base font-semibold text-foreground">Source data to report pack</h3>
          </div>
          <Button asChild variant="outline" size="sm">
            <Link to={WORKSTATION_ROUTE_CATALOG.reportingReportPacks} aria-label="Open report packs from operations record release">
              <FileCheck2 className="h-4 w-4" aria-hidden="true" />
              Report packs
            </Link>
          </Button>
        </div>
        <ol className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
          {vm.steps.map((step) => (
            <li key={step.id}>
              <Link
                to={step.href}
                aria-label={step.ariaLabel}
                className={cn(
                  "flex h-full min-w-0 flex-col justify-between rounded-md border px-3 py-3 text-sm transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40",
                  tonePanel[step.tone]
                )}
              >
                <span className="min-w-0">
                  <span className="flex items-center justify-between gap-2">
                    <span className="font-semibold text-foreground">{step.label}</span>
                    <Badge variant={step.badgeVariant}>{step.statusLabel}</Badge>
                  </span>
                  <span className="mt-2 block text-xs leading-5 text-muted-foreground">{step.detail}</span>
                </span>
                <span className="mt-3 inline-flex items-center gap-1.5 font-mono text-[11px] text-primary">
                  {step.routeLabel}
                  <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                </span>
              </Link>
            </li>
          ))}
        </ol>
      </section>

      <section className="grid gap-4 xl:grid-cols-3">
        <ReleasePanel icon="source" panel={vm.sourcePanel} />
        <ReleasePanel icon="accounting" panel={vm.accountingPanel} />
        <ReleasePanel icon="reporting" panel={vm.reportPackPanel} />
      </section>

      <section className="grid gap-4 xl:grid-cols-[0.9fr_1.1fr]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>{vm.distributionLabel}</CardTitle>
            <CardDescription>Recipient, owner, channel, and pending-item state remain attached to the governed packet.</CardDescription>
          </CardHeader>
          <CardContent>
            {vm.distributions.length > 0 ? (
              <div role="list" aria-label={vm.distributionLabel} className="grid gap-2">
                {vm.distributions.map((distribution) => (
                  <div
                    key={distribution.id}
                    role="listitem"
                    aria-label={distribution.ariaLabel}
                    className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-semibold text-foreground">{distribution.label}</span>
                      <Badge variant={distribution.pendingItemsLabel === "0 pending" ? "outline" : "warning"}>
                        {distribution.pendingItemsLabel}
                      </Badge>
                    </div>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">
                      {distribution.channel} - {distribution.ownerLabel} - {distribution.pendingSummary}
                    </p>
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
                {vm.distributionEmptyText}
              </p>
            )}
          </CardContent>
        </Card>

        <div className="grid gap-4 md:grid-cols-2">
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>{vm.templateLabel}</CardTitle>
              <CardDescription>Approved and in-review template metadata stays visible for the release path.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-2">
              {vm.templates.length > 0 ? vm.templates.slice(0, 4).map((template) => (
                <div key={`${template.id}-${template.version}`} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-semibold text-foreground">{template.name}</span>
                    <Badge variant={template.statusVariant}>{template.statusLabel}</Badge>
                  </div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {template.family} - {template.version} - {template.sectionSummary}
                  </p>
                </div>
              )) : (
                <p role="status" className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
                  {vm.templateEmptyText}
                </p>
              )}
            </CardContent>
          </Card>

          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>{vm.recentRunsLabel}</CardTitle>
              <CardDescription>Recent run lineage and next actions show whether report-pack evidence is current.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-2">
              {vm.recentRuns.length > 0 ? vm.recentRuns.slice(0, 4).map((run) => (
                <div key={run.id} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="break-all font-mono text-xs font-semibold text-foreground">{run.id}</span>
                    <Badge variant={run.status === "Failed" ? "warning" : "outline"}>{run.status}</Badge>
                  </div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {run.family} - {run.lineageSummary} - {run.auditSummary}
                  </p>
                </div>
              )) : (
                <p role="status" className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
                  {vm.recentRunsEmptyText}
                </p>
              )}
            </CardContent>
          </Card>
        </div>
      </section>
    </div>
  );
}

function ReleaseMetric({ metric }: { metric: OperationsRecordReleaseMetric }) {
  return (
    <div className={cn("rounded-md border px-3 py-3", tonePanel[metric.tone])}>
      <div className="flex items-start justify-between gap-2">
        <span className="text-xs text-muted-foreground">{metric.label}</span>
        <Badge variant={metric.tone === "ready" ? "success" : metric.tone === "blocked" ? "danger" : metric.tone === "review" ? "warning" : "outline"}>
          {metric.value}
        </Badge>
      </div>
      <p className="mt-3 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
    </div>
  );
}

function ReleasePanel({
  icon,
  panel
}: {
  icon: "source" | "accounting" | "reporting";
  panel: OperationsRecordReleasePanel;
}) {
  const Icon = icon === "source" ? DatabaseZap : icon === "accounting" ? Landmark : Network;

  return (
    <Card className={cn("border", tonePanel[panel.tone])}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2">
              <Icon className="h-4 w-4 text-primary" aria-hidden="true" />
              {panel.title}
            </CardTitle>
            <CardDescription className="mt-2">{panel.description}</CardDescription>
          </div>
          <Badge variant={panel.badgeVariant}>{panel.statusLabel}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <Button asChild variant="outline" size="sm">
          <Link to={panel.primaryHref} aria-label={panel.primaryAriaLabel}>
            {panel.primaryLabel}
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Link>
        </Button>
        {panel.rows.length > 0 ? (
          <div role="list" aria-label={`${panel.title} release evidence`} className="grid gap-2">
            {panel.rows.map((row) => (
              <ReleaseEvidenceRow key={row.id} row={row} />
            ))}
          </div>
        ) : (
          <p role="status" className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
            {panel.emptyText}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function ReleaseEvidenceRow({ row }: { row: OperationsRecordReleaseEvidenceRow }) {
  const content = (
    <>
      <span className="flex min-w-0 items-start justify-between gap-2">
        <span className="min-w-0">
          <span className="block font-semibold text-foreground">{row.label}</span>
          <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.detail}</span>
        </span>
        <Badge variant={row.badgeVariant}>{row.value}</Badge>
      </span>
      <span className="mt-2 inline-flex items-center gap-1.5 font-mono text-[11px] text-primary">
        {row.routeLabel}
        {row.href ? <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" /> : null}
      </span>
    </>
  );

  if (row.href) {
    return (
      <Link
        to={row.href}
        role="listitem"
        aria-label={row.ariaLabel}
        className={cn("block rounded-md border px-3 py-2 hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40", tonePanel[row.tone])}
      >
        {content}
      </Link>
    );
  }

  return (
    <div
      role="listitem"
      aria-label={row.ariaLabel}
      className={cn("rounded-md border px-3 py-2", tonePanel[row.tone])}
    >
      {content}
    </div>
  );
}
