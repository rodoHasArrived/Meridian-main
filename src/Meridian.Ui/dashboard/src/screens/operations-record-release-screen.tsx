import { ArrowRight, DatabaseZap, FileCheck2, Landmark, Network } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { WorkspaceFilterBar } from "@/components/meridian/workspace-primitives";
import { WorkspaceInspectorHost } from "@/components/meridian/workspace-primitives";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import { cn } from "@/lib/utils";
import { GateRail, SeverityBadge } from "@/components/operations";
import { MetricCard } from "@/components/data/concrete";
import {
  badgeVariantToSeverityStatus,
  readinessToneToPanelClass,
  readinessToneToSeverityStatus,
  semanticToneToMetricCardTone
} from "@/lib/shared-tone-mappings";
import {
  appendViewStateToRoute,
  readViewStateFromSearch,
  stripViewStateFromSearch
} from "@/lib/view-state-envelope";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { useOperationsContinuityScreenViewModel } from "@/screens/operations-continuity-screen.view-model";
import { useReportingScreenViewModel } from "@/screens/reporting-screen.view-model";
import {
  OPERATIONS_RECORD_RELEASE_VIEW_STATE_SCREEN,
  buildOperationsRecordReleaseViewModel,
  buildOperationsRecordReleaseViewStateEnvelope,
  normalizeOperationsRecordReleaseViewState,
  resolveOperationsRecordReleaseSelectedStepId,
  type OperationsRecordReleaseEvidenceRow,
  type OperationsRecordReleaseMetric,
  type OperationsRecordReleasePanel,
  type OperationsRecordReleaseStep,
  type OperationsRecordReleaseViewModel
} from "@/screens/operations-record-release-screen.view-model";
import type { AccountingWorkspaceResponse, DataWorkspaceResponse } from "@/types";

interface OperationsRecordReleaseScreenProps {
  data: DataWorkspaceResponse | null;
  reporting: AccountingWorkspaceResponse | null;
}

export function OperationsRecordReleaseScreen({ data, reporting }: OperationsRecordReleaseScreenProps) {
  const location = useLocation();
  const navigate = useNavigate();
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
  const stepIds = vm.steps.map((step) => step.id);
  const envelope = readViewStateFromSearch(location.search, OPERATIONS_RECORD_RELEASE_VIEW_STATE_SCREEN);
  const viewState = normalizeOperationsRecordReleaseViewState(envelope?.state ?? null, stepIds);
  const selectedStepId = resolveOperationsRecordReleaseSelectedStepId(vm.steps, viewState?.selectedStepId);
  const selectedStep = vm.steps.find((step) => step.id === selectedStepId) ?? vm.steps[0] ?? null;
  const selectedPanel = selectedStep ? panelForReleaseStep(vm, selectedStep.id) : vm.sourcePanel;

  const handleStepSelect = (stepId: string) => {
    const baseRoute = `${location.pathname}${stripViewStateFromSearch(location.search)}`;
    navigate(
      appendViewStateToRoute(
        baseRoute,
        buildOperationsRecordReleaseViewStateEnvelope({ selectedStepId: stepId })
      ),
      { replace: true }
    );
  };

  return (
    <WorkspaceWorkbenchShell
      label="Operations record release workbench"
      statusBand={<OperationsRecordStatusBand vm={vm} />}
      contextRail={<OperationsRecordContextRail vm={vm} selectedStepId={selectedStep?.id ?? ""} />}
      contextRailLabel="Operations record release context"
      inspector={selectedStep ? <SelectedReleaseStepInspector step={selectedStep} panel={selectedPanel} /> : null}
      inspectorLabel="Operations record selected step slot"
      evidenceDrawer={<OperationsRecordEvidenceDrawer vm={vm} />}
      evidenceDrawerLabel="Operations record supporting evidence"
    >
      <WorkspaceFilterBar
        label="Operations record release route context"
        searchLabel="Release route"
        searchValue={vm.routePathLabel}
        options={vm.steps.map((step) => ({
          id: step.id,
          label: step.label,
          active: step.id === selectedStep?.id,
          count: step.statusLabel
        }))}
        fields={[
          { id: "status", label: "Release", value: vm.statusLabel },
          { id: "root", label: "Root nav", value: "7 workspaces unchanged" }
        ]}
      />

      <section
        role="region"
        aria-label={vm.stepsLabel}
        className="panel-surface grid gap-4 px-4 py-4"
      >
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div className="eyebrow-label">Release path</div>
            <h2 className="mt-2 text-base font-semibold text-foreground">Source data to report pack</h2>
            <p className="mt-1 max-w-3xl text-xs leading-5 text-muted-foreground">
              Selecting a step keeps the release path in place while updating the inspector with step-specific posture.
            </p>
          </div>
          <Button asChild variant="outline" size="sm">
            <Link to={WORKSTATION_ROUTE_CATALOG.reportingReportPacks} aria-label="Open report packs from operations record release">
              <FileCheck2 className="h-4 w-4" aria-hidden="true" />
              Report packs
            </Link>
          </Button>
        </div>
        <GateRail
          className="px-1 py-2"
          gates={vm.steps.map((step) => ({
            key: step.id,
            label: step.label,
            status: readinessToneToSeverityStatus(step.tone),
            statusLabel: step.statusLabel
          }))}
        />
        <ol className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {vm.steps.map((step, index) => (
            <li key={step.id}>
              <button
                type="button"
                aria-label={`Select release step ${step.ariaLabel}`}
                aria-pressed={step.id === selectedStep?.id}
                onClick={() => handleStepSelect(step.id)}
                className={cn(
                  "flex h-full w-full min-w-0 flex-col justify-between rounded-md border px-3 py-3 text-left text-sm transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40",
                  readinessToneToPanelClass(step.tone),
                  step.id === selectedStep?.id && "ring-2 ring-primary/35"
                )}
              >
                <span className="min-w-0">
                  <span className="flex items-start justify-between gap-2">
                    <span className="font-mono text-[11px] text-muted-foreground">{String(index + 1).padStart(2, "0")}</span>
                    <SeverityBadge status={readinessToneToSeverityStatus(step.tone)} label={step.statusLabel} />
                  </span>
                  <span className="mt-2 block font-semibold text-foreground">{step.label}</span>
                  <span className="mt-2 block text-xs leading-5 text-muted-foreground">{step.detail}</span>
                </span>
                <span className="mt-3 inline-flex items-center gap-1.5 font-mono text-[11px] text-primary">
                  {step.id === selectedStep?.id ? "Selected release step" : "Inspect step"}
                  <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                </span>
              </button>
            </li>
          ))}
        </ol>
      </section>
    </WorkspaceWorkbenchShell>
  );
}

function OperationsRecordStatusBand({ vm }: { vm: OperationsRecordReleaseViewModel }) {
  return (
    <div className={cn("grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]", readinessToneToPanelClass(vm.tone))}>
      <div className="min-w-0 px-1 py-1">
        <div className="eyebrow-label">{vm.eyebrow}</div>
        <div className="mt-2 flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1 className="font-display text-[1.25rem] font-semibold leading-tight text-foreground">{vm.title}</h1>
            <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">{vm.summary}</p>
          </div>
          <SeverityBadge status={readinessToneToSeverityStatus(vm.tone)} label={vm.statusLabel} />
        </div>
        <p className="mt-3 rounded-md border border-border/70 bg-background/45 px-3 py-2 text-sm leading-6 text-foreground">
          {vm.statusDetail}
        </p>
      </div>
      <div className="grid min-w-[16rem] gap-2 sm:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2" aria-label="Operations record release metrics">
        {vm.metrics.map((metric) => (
          <ReleaseMetric key={metric.id} metric={metric} />
        ))}
      </div>
    </div>
  );
}

function OperationsRecordContextRail({
  vm,
  selectedStepId
}: {
  vm: OperationsRecordReleaseViewModel;
  selectedStepId: string;
}) {
  return (
    <div className="grid gap-3">
      <div>
        <div className="eyebrow-label">Route posture</div>
        <p className="mt-2 text-sm font-semibold text-foreground">{vm.statusLabel}</p>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{vm.rootAreaSummary}</p>
      </div>
      <ol className="grid gap-2" aria-label="Release step context">
        {vm.steps.map((step) => (
          <li
            key={step.id}
            className={cn(
              "rounded-md border border-border/70 px-2 py-2 text-xs leading-5",
              step.id === selectedStepId ? "bg-primary/10 text-foreground" : "bg-secondary/25 text-muted-foreground"
            )}
            aria-current={step.id === selectedStepId ? "step" : undefined}
          >
            <span className="block font-semibold text-foreground">{step.label}</span>
            <span className="mt-1 block">{step.statusLabel}</span>
          </li>
        ))}
      </ol>
      <p className="break-all rounded-md border border-border/70 bg-secondary/20 px-2 py-2 font-mono text-[11px] leading-5 text-muted-foreground">
        {vm.routePathLabel}
      </p>
    </div>
  );
}

function SelectedReleaseStepInspector({
  step,
  panel
}: {
  step: OperationsRecordReleaseStep;
  panel: OperationsRecordReleasePanel;
}) {
  const Icon = iconForReleaseStep(step.id);
  const blockerText = step.tone === "blocked" || step.tone === "review"
    ? step.detail
    : "No blockers in the loaded payload for this release step.";

  return (
    <WorkspaceInspectorHost
      label="Selected release step inspector"
      title={step.label}
      subtitle={step.routeLabel}
      status={<SeverityBadge status={readinessToneToSeverityStatus(step.tone)} label={step.statusLabel} />}
    >
      <div className="grid gap-3">
        <section className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3" aria-label="Selected release step status">
          <div className="flex items-center gap-2">
            <Icon className="h-4 w-4 text-primary" aria-hidden="true" />
            <span className="font-semibold text-foreground">{panel.title}</span>
          </div>
          <p className="mt-2 text-xs leading-5 text-muted-foreground">{panel.description}</p>
        </section>

        <section className="rounded-md border border-border/70 bg-background px-3 py-3" aria-label="Selected release step blockers">
          <h3 className="text-xs font-semibold uppercase tracking-[0.08em] text-muted-foreground">Blockers</h3>
          <p className="mt-2 text-sm leading-6 text-foreground">{blockerText}</p>
        </section>

        <section className="rounded-md border border-border/70 bg-background px-3 py-3" aria-label="Selected release step route">
          <h3 className="text-xs font-semibold uppercase tracking-[0.08em] text-muted-foreground">Route</h3>
          <p className="mt-2 break-all font-mono text-xs leading-5 text-foreground">{step.href}</p>
          <Button asChild variant="outline" size="sm" className="mt-3 w-full justify-center">
            <Link to={step.href} aria-label={`Open route for ${step.label}`}>
              Open step route
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </Link>
          </Button>
        </section>

        <section aria-label="Selected release step evidence">
          <div className="mb-2 flex items-center justify-between gap-2">
            <h3 className="text-xs font-semibold uppercase tracking-[0.08em] text-muted-foreground">Evidence</h3>
            <SeverityBadge status={readinessToneToSeverityStatus(panel.tone)} label={panel.statusLabel} />
          </div>
          {panel.rows.length > 0 ? (
            <div role="list" aria-label={`${step.label} release evidence`} className="grid gap-2">
              {panel.rows.map((row) => (
                <ReleaseEvidenceRow key={row.id} row={row} />
              ))}
            </div>
          ) : (
            <p role="status" className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
              {panel.emptyText}
            </p>
          )}
        </section>
      </div>
    </WorkspaceInspectorHost>
  );
}

function OperationsRecordEvidenceDrawer({ vm }: { vm: OperationsRecordReleaseViewModel }) {
  return (
    <div className="grid gap-4 xl:grid-cols-[0.9fr_1.1fr_1fr]">
      <section aria-label={vm.distributionLabel}>
        <h2 className="text-sm font-semibold text-foreground">{vm.distributionLabel}</h2>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">
          Recipient, owner, channel, and pending-item state remain attached to the governed packet.
        </p>
        {vm.distributions.length > 0 ? (
          <div role="list" aria-label={vm.distributionLabel} className="mt-3 grid gap-2">
            {vm.distributions.map((distribution) => (
              <div
                key={distribution.id}
                role="listitem"
                aria-label={distribution.ariaLabel}
                className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2"
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{distribution.label}</span>
                  <SeverityBadge
                    status={distribution.pendingItemsLabel === "0 pending" ? "Ready" : "ReviewRequired"}
                    label={distribution.pendingItemsLabel}
                  />
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {distribution.channel} - {distribution.ownerLabel} - {distribution.pendingSummary}
                </p>
              </div>
            ))}
          </div>
        ) : (
          <p role="status" className="mt-3 rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
            {vm.distributionEmptyText}
          </p>
        )}
      </section>

      <section aria-label={vm.templateLabel}>
        <h2 className="text-sm font-semibold text-foreground">{vm.templateLabel}</h2>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">
          Approved and in-review template metadata stays visible for the release path.
        </p>
        <div className="mt-3 grid gap-2">
          {vm.templates.length > 0 ? vm.templates.slice(0, 4).map((template) => (
            <div key={`${template.id}-${template.version}`} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-semibold text-foreground">{template.name}</span>
                <SeverityBadge status={badgeVariantToSeverityStatus(template.statusVariant)} label={template.statusLabel} />
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
        </div>
      </section>

      <section aria-label={vm.recentRunsLabel}>
        <h2 className="text-sm font-semibold text-foreground">{vm.recentRunsLabel}</h2>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">
          Recent run lineage and next actions show whether report-pack evidence is current.
        </p>
        <div className="mt-3 grid gap-2">
          {vm.recentRuns.length > 0 ? vm.recentRuns.slice(0, 4).map((run) => (
            <div key={run.id} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="break-all font-mono text-xs font-semibold text-foreground">{run.id}</span>
                <SeverityBadge status={run.status} label={run.status} />
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
        </div>
      </section>
    </div>
  );
}

function ReleaseMetric({ metric }: { metric: OperationsRecordReleaseMetric }) {
  return (
    <MetricCard
      label={metric.label}
      value={metric.value}
      delta={metric.detail}
      tone={semanticToneToMetricCardTone(metric.tone)}
      trend="flat"
    />
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
        <SeverityBadge status={readinessToneToSeverityStatus(row.tone)} label={row.value} />
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
        className={cn("block rounded-md border px-3 py-2 hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40", readinessToneToPanelClass(row.tone))}
      >
        {content}
      </Link>
    );
  }

  return (
    <div
      role="listitem"
      aria-label={row.ariaLabel}
      className={cn("rounded-md border px-3 py-2", readinessToneToPanelClass(row.tone))}
    >
      {content}
    </div>
  );
}

function panelForReleaseStep(
  vm: OperationsRecordReleaseViewModel,
  stepId: string
): OperationsRecordReleasePanel {
  if (stepId === "source-data") {
    return vm.sourcePanel;
  }

  if (stepId === "report") {
    return vm.reportPackPanel;
  }

  return vm.accountingPanel;
}

function iconForReleaseStep(stepId: string) {
  if (stepId === "source-data") {
    return DatabaseZap;
  }

  if (stepId === "report") {
    return Network;
  }

  return Landmark;
}
