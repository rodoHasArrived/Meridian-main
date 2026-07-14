import {
  CheckCircle2,
  DatabaseZap,
  Download,
  FileSpreadsheet,
  FileUp,
  KeyRound,
  MonitorCheck,
  Plus,
  RadioTower,
  RefreshCcw,
  ShieldCheck,
  TimerReset
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { useMemo, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { StatStrip } from "@/components/meridian/stat-strip";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import type { DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  WorkspaceInspectorHost,
  WorkspaceTabStrip
} from "@/components/meridian/workspace-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogCloseButton, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { StatusBanner } from "@/components/ui/status-banner";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  buildDataAnalyticsDegradedViewModel,
  DataAnalyticsDegradedRegion
} from "@/screens/data-screen.analytics-status";
import { useDataQueryPanel } from "@/screens/data-screen.query-panel.view-model";
import { useDataQualityPanel } from "@/screens/data-screen.data-quality.view-model";
import {
  CAPABILITY_LEGEND,
  useCapabilityMatrixPanel,
  type CapabilityMatrixViewModel
} from "@/screens/data-screen.capability-matrix.view-model";
import {
  useCorporateActionInboxPanel,
  type CorporateActionInboxViewModel
} from "@/screens/data-screen.corporate-action-inbox.view-model";
import { useCoverageGapsPanel } from "@/screens/data-screen.coverage-gaps.view-model";
import { CoverageGapsRegion, DataQualityRegion } from "@/screens/data-screen.data-regions";
import { DataOverviewHub, RouteFocusCard } from "@/screens/data-screen-navigation-panels";
import { resultToneClass } from "@/screens/data-screen.tone-styles";
import { DataBackfillWorkstream, DataExportWorkstream, DataQueryWorkstream } from "@/screens/data-screen.workstreams";
import {
  DATA_PROVIDER_DETAIL_PANEL_ID,
  useDataViewModel
} from "@/screens/data-screen.view-model";
import type { DataWorkspaceResponse } from "@/types";
import type {
  BackfillResultCardState,
  DataOperationsEmptyState,
  DataUploadPanelState,
  DataOperationsLoadingState,
  DataOperationsProviderDiagnosticRow,
  DataOperationsProviderDetailState,
  DataOperationsProviderRow,
  DataOperationsProviderSummaryCardState,
  ProviderSetupInstitutionSearchState,
  ProviderSetupWorkflowStepState,
  ProviderSetupNextActionState
} from "@/screens/data-screen.view-model";
import type {
  ProviderConnectionRow,
  ProviderReadinessSummary,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot
} from "@/types";

interface DataScreenProps {
  data: DataWorkspaceResponse | null;
  providerConnections?: ProviderConnectionRow[] | null;
  providerReadiness?: ProviderReadinessSummary | null;
  providerRoutingConnections?: ProviderRoutingConnection[] | null;
  providerRoutingBindings?: ProviderRoutingBinding[] | null;
  providerRoutingTrustSnapshots?: ProviderRoutingTrustSnapshot[] | null;
  providerRoutingRefreshing?: boolean;
  onProviderSetupConfigured?: () => Promise<void> | void;
  onProviderRoutingRefresh?: () => Promise<void> | void;
}

const providerHealthColumns: DenseDataTableColumn<DataOperationsProviderRow>[] = [
  {
    id: "provider",
    label: "Provider",
    render: (provider) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{provider.provider}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{provider.capability}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (provider) => (
      <Badge
        variant={provider.statusTone === "danger" ? "danger" : provider.statusTone === "warning" ? "warning" : "success"}
        dot
      >
        {provider.status}
      </Badge>
    )
  },
  {
    id: "credential-posture",
    label: "Credential posture",
    render: (provider) => (
      <span className="block min-w-0">
        <span className="block text-xs text-foreground">{provider.credentialText}</span>
        <span className="mt-1 block text-xs text-muted-foreground">{provider.verificationText}</span>
      </span>
    )
  },
  {
    id: "trust-latency",
    label: "Trust / last good",
    render: (provider) => (
      <span className="block min-w-[8rem]">
        <span className="block font-mono text-xs text-foreground">{provider.trustScoreText}</span>
        <span className="mt-1 block font-mono text-xs text-muted-foreground">{provider.latencyText}</span>
        <span className="mt-1 block text-xs text-muted-foreground">{provider.signalSourceText}</span>
      </span>
    )
  },
  {
    id: "gate",
    label: "Workflows",
    render: (provider) => <span className="text-xs leading-5 text-muted-foreground">{provider.affectedWorkflowsText}</span>
  },
  {
    id: "action",
    label: "Next Action",
    render: (provider) => (
      <span className="block min-w-[7rem] whitespace-nowrap text-xs font-medium text-foreground" title={provider.recommendedActionText}>
        {provider.actionLabel}
      </span>
    )
  }
];

/**
 * Route-scoped views: each Data sub-route renders its focused workstream and
 * the workspace root renders the health/analytics overview. The tab strip and
 * the sidebar sub-navigation share this taxonomy.
 */
const dataRouteTabs = [
  { id: "overview", label: "Overview", route: WORKSTATION_ROUTE_CATALOG.data, workstream: "overview" },
  { id: "providers", label: "Providers", route: WORKSTATION_ROUTE_CATALOG.dataProviders, workstream: "providers" },
  { id: "import", label: "Import", route: WORKSTATION_ROUTE_CATALOG.dataImport, workstream: "import" },
  { id: "backfills", label: "Backfills", route: WORKSTATION_ROUTE_CATALOG.dataBackfills, workstream: "backfills" },
  { id: "exports", label: "Exports", route: WORKSTATION_ROUTE_CATALOG.dataExports, workstream: "exports" },
  { id: "query", label: "SQL query", route: WORKSTATION_ROUTE_CATALOG.dataQuery, workstream: "query" }
] as const;

const dataRouteViewCopy: Record<string, { title: string; description: string }> = {
  overview: {
    title: "Data overview",
    description: "Provider posture, data quality, and analytics posture. Providers, backfills, exports, and SQL have focused routes."
  },
  providers: {
    title: "Provider catalog",
    description: "Source health, credentials, routing trust, verification, and recovery actions."
  },
  import: {
    title: "Data import",
    description: "Template-led retained-file preview, validation evidence, and downstream handoff."
  },
  backfills: {
    title: "Backfill queue",
    description: "Historical repair jobs with operator-visible status, ranges, and result evidence."
  },
  exports: {
    title: "Export packages",
    description: "Governed export runs and downstream handoff evidence."
  },
  query: {
    title: "SQL query",
    description: "Read-only SQL workbench over the workstation store."
  }
};

export function DataScreen({
  data,
  providerConnections = null,
  providerReadiness = null,
  providerRoutingConnections = null,
  providerRoutingBindings = null,
  providerRoutingTrustSnapshots = null,
  providerRoutingRefreshing = false,
  onProviderSetupConfigured,
  onProviderRoutingRefresh
}: DataScreenProps) {
  const { pathname, search } = useLocation();
  const navigate = useNavigate();
  const providerSetupLifecycle = useMemo(
    () => ({ onConfigured: onProviderSetupConfigured }),
    [onProviderSetupConfigured]
  );
  const providerEvidence = useMemo(() => ({
    providerConnections,
    providerReadiness,
    providerRoutingConnections,
    providerRoutingBindings,
    providerRoutingTrustSnapshots,
    providerRoutingRefreshing,
    onProviderRoutingRefresh
  }), [
    onProviderRoutingRefresh,
    providerConnections,
    providerReadiness,
    providerRoutingBindings,
    providerRoutingConnections,
    providerRoutingRefreshing,
    providerRoutingTrustSnapshots
  ]);
  const vm = useDataViewModel(data, pathname, undefined, providerSetupLifecycle, providerEvidence);
  const queryPanel = useDataQueryPanel();
  const qualityPanel = useDataQualityPanel();
  const capabilityMatrixPanel = useCapabilityMatrixPanel();
  const corporateActionInboxPanel = useCorporateActionInboxPanel();
  const coverageGapsPanel = useCoverageGapsPanel();
  const [savedQueryName, setSavedQueryName] = useState("");
  const activeWorkstream = vm.workstream;
  const showHealthMonitoring = activeWorkstream === "overview";
  const showProviderWorkstream = activeWorkstream === "providers";
  const showImportWorkstream = activeWorkstream === "import";
  const showBackfillWorkstream = activeWorkstream === "backfills";
  const showExportWorkstream = activeWorkstream === "exports";
  const showQueryWorkstream = activeWorkstream === "query";

  if (!data) {
    return <DataOperationsLoadingPanel state={vm.loadingState} />;
  }

  const routeCopy = dataRouteViewCopy[activeWorkstream] ?? dataRouteViewCopy.overview;
  const routeTabs = dataRouteTabs.map((tab) => ({
    id: tab.id,
    label: tab.label,
    selected: tab.workstream === activeWorkstream
  }));
  const analyticsDegraded = buildDataAnalyticsDegradedViewModel([
    { id: "data-quality", label: "Data quality", error: qualityPanel.error, loading: qualityPanel.loading, refresh: qualityPanel.refresh },
    { id: "capability-matrix", label: "Provider capability matrix", error: capabilityMatrixPanel.error, loading: capabilityMatrixPanel.loading, refresh: capabilityMatrixPanel.refresh },
    { id: "corporate-actions", label: "Corporate action inbox", error: corporateActionInboxPanel.error, loading: corporateActionInboxPanel.loading, refresh: corporateActionInboxPanel.refresh },
    { id: "coverage-gaps", label: "Security-master coverage", error: coverageGapsPanel.error, loading: coverageGapsPanel.loading, refresh: coverageGapsPanel.refresh }
  ]);
  const analyticsUnavailable = analyticsDegraded?.affectedIds ?? new Set<string>();

  return (
    <div className="workspace-screen data-workspace-screen">
      <StatStrip metrics={data.metrics} label="Data headline metrics" />

      <section
        role="region"
        aria-label="Data workspace context"
        className="flex flex-wrap items-end justify-between gap-3"
      >
        <div className="min-w-0">
          <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
            {routeCopy.title}
          </h2>
          <p className="mt-0.5 max-w-3xl text-xs leading-5 text-muted-foreground">
            {routeCopy.description}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <WorkspaceTabStrip
            label="Data routes"
            tabs={routeTabs}
            onSelect={(id) => {
              const tab = dataRouteTabs.find((candidate) => candidate.id === id);
              if (tab) {
                // Preserve the querystring: the operating scope is threaded
                // through search params across the shell.
                navigate({ pathname: tab.route, search });
              }
            }}
          />
          <Button
            type="button"
            size="sm"
            onClick={() => {
              if (showProviderWorkstream) {
                vm.openProviderSetup();
                return;
              }

              navigate({
                pathname: showImportWorkstream ? WORKSTATION_ROUTE_CATALOG.dataProviders : WORKSTATION_ROUTE_CATALOG.dataImport,
                search
              });
            }}
            aria-label={showProviderWorkstream ? "Add a provider connection" : showImportWorkstream ? "Review provider connections" : "Import a retained data file"}
          >
            {showProviderWorkstream ? <Plus className="h-4 w-4" aria-hidden="true" /> : <FileUp className="h-4 w-4" aria-hidden="true" />}
            <span className="ml-1.5">
              {showProviderWorkstream ? "Add provider" : showImportWorkstream ? "Review providers" : "Import file"}
            </span>
          </Button>
        </div>
      </section>

      {showHealthMonitoring ? (
        <div className="space-y-4">
          <DataOverviewHub vm={vm} degradedPanelCount={analyticsUnavailable.size} />
          <details className="rounded-lg border border-border/70 bg-secondary/15 px-4 py-3">
            <summary className="cursor-pointer font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
              Review data diagnostics
            </summary>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              Open detailed quality, capability, corporate-action, and coverage evidence after choosing the next Data task.
            </p>
            <div className="mt-4 space-y-4">
              {analyticsDegraded ? <DataAnalyticsDegradedRegion vm={analyticsDegraded} /> : null}

              {!analyticsUnavailable.has("data-quality") ? <DataQualityRegion panel={qualityPanel} /> : null}

              {!analyticsUnavailable.has("capability-matrix") ? <CapabilityMatrixRegion panel={capabilityMatrixPanel} /> : null}

              {!analyticsUnavailable.has("corporate-actions") ? <CorporateActionInboxRegion panel={corporateActionInboxPanel} /> : null}

              {!analyticsUnavailable.has("coverage-gaps") ? <CoverageGapsRegion panel={coverageGapsPanel} /> : null}
            </div>
          </details>
        </div>
      ) : null}

      <section className="data-management-main" aria-label="Data workstreams">
        {activeWorkstream !== "overview" && !showBackfillWorkstream ? (
          <RouteFocusCard
            state={vm.routeFocusCard}
          />
        ) : null}

        {showImportWorkstream ? (
          <DataUploadIntakePanel
            state={vm.uploadPanelState}
            onTemplateSelect={vm.selectUploadTemplate}
            onFileSelect={vm.previewDataUpload}
          />
        ) : null}

        {showProviderWorkstream ? (
        <section aria-labelledby="data-provider-health-title" className="workspace-region data-provider-region">
          <CardHeader>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div>
                <div className="eyebrow-label">Provider management</div>
                <CardTitle id="data-provider-health-title" className="mt-2 flex items-center gap-2">
                  <MonitorCheck className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.providerSection.title}
                </CardTitle>
                <div className="mt-1 text-xs font-medium text-muted-foreground">Provider health</div>
                <CardDescription className="mt-2">
                  Provider health, credential verification, routing, and backup source status. {vm.providerSection.subtitle}
                </CardDescription>
                <p className="mt-2 max-w-3xl text-xs leading-5 text-muted-foreground">
                  {vm.providerSection.readinessSummary}
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge
                  variant={vm.providerSection.statusTone === "danger" ? "danger" : vm.providerSection.statusTone === "warning" ? "warning" : "success"}
                  dot={vm.providerSection.statusTone === "success"}
                >
                  {vm.providerSection.statusLabel}
                </Badge>
                {vm.providerSection.commandActions.map((action) => (
                  action.href ? (
                    <Button key={action.id} asChild size="sm" variant={action.variant}>
                      <Link to={action.href} aria-label={action.ariaLabel}>
                        <ProviderCommandIcon actionId={action.id} busy={action.busy} />
                        {action.label}
                      </Link>
                    </Button>
                  ) : (
                    <Button
                      key={action.id}
                      size="sm"
                      variant={action.variant}
                      onClick={() => {
                        if (action.id === "add") vm.openProviderSetup();
                        if (action.id === "refresh") void onProviderRoutingRefresh?.();
                        if (action.id === "diagnostics") void vm.verifySelectedProvider();
                      }}
                      disabled={action.disabled}
                      disabledReason={action.disabledReason}
                      busy={action.busy}
                      aria-label={action.ariaLabel}
                    >
                      <ProviderCommandIcon actionId={action.id} busy={action.busy} />
                      {action.label}
                    </Button>
                  )
                ))}
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.providerSection.hasRows ? (
              <>
                <div
                  className="flex flex-wrap items-stretch gap-2 rounded-md border border-border/70 bg-secondary/20 px-2 py-2"
                  aria-label="Provider management scan band"
                >
                  {vm.providerSection.summaryCards.map((card) => (
                    <ProviderSummaryCard key={card.id} card={card} />
                  ))}
                </div>
              <div className="data-provider-table-detail-layout workspace-table-stack">
                <div className="workspace-table-stack">
                  <label htmlFor="configured-provider-selector" className="workspace-inline-select">
                    <span>Configured Provider</span>
                    <select
                      id="configured-provider-selector"
                      value={vm.providerSection.selectedRowId ?? ""}
                      onChange={(event) => vm.selectProvider(event.target.value)}
                      aria-label="Configured Provider"
                    >
                      {vm.providerSection.providerOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>
                <DenseDataTable
                  columns={providerHealthColumns}
                  rows={vm.providerSection.rows}
                  getRowId={(provider) => provider.rowId}
                  getRowAriaLabel={(provider) => provider.ariaLabel}
                  getRowSelectAriaLabel={(provider) => provider.selectAriaLabel}
                  getRowAriaControls={(provider) => provider.detailPanelId}
                  getRowAriaExpanded={(provider) => provider.expanded}
                  getRowClassName={(provider) => provider.rowClassName}
                  selectedRowId={vm.providerSection.selectedRowId}
                  onRowSelect={(provider) => vm.selectProvider(provider.rowId)}
                  emptyText={vm.providerSection.emptyState.description}
                  ariaLabel={vm.providerSection.tableLabel}
                  caption={vm.providerSection.description}
                  maxVisibleRows={100}
                />
                </div>
                <ProviderDetailPanel
                  detail={vm.providerSection.selectedDetail}
                  emptyState={vm.providerSection.detailEmptyState}
                  activeTab={vm.selectedProviderTab}
                  onTabSelect={vm.selectProviderTab}
                  onVerify={vm.verifySelectedProvider}
                />
              </div>
              </>
            ) : (
              <ProviderEmptyState state={vm.providerSection.emptyState} onSetup={vm.openProviderSetup} />
            )}
          </CardContent>
        </section>
        ) : null}

        {showBackfillWorkstream ? <DataBackfillWorkstream vm={vm} /> : null}

        {showExportWorkstream ? <DataExportWorkstream vm={vm} /> : null}

        {showQueryWorkstream ? (
          <DataQueryWorkstream
            queryPanel={queryPanel}
            savedQueryName={savedQueryName}
            setSavedQueryName={setSavedQueryName}
          />
        ) : null}
      </section>

      <ProviderSetupDialog vm={vm} />
      <BackfillTriggerDialog vm={vm} />
    </div>
  );
}

type DataOperationsVm = ReturnType<typeof useDataViewModel>;

function ProviderCommandIcon({
  actionId,
  busy
}: {
  actionId: "add" | "refresh" | "diagnostics" | "settings";
  busy: boolean;
}) {
  if (actionId === "add") return <Plus className="h-3.5 w-3.5" aria-hidden="true" />;
  if (actionId === "refresh") return <RefreshCcw className={cn("h-3.5 w-3.5", busy && "animate-spin")} aria-hidden="true" />;
  if (actionId === "diagnostics") return <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />;
  return <KeyRound className="h-3.5 w-3.5" aria-hidden="true" />;
}

function ProviderSummaryCard({ card }: { card: DataOperationsProviderSummaryCardState }) {
  const toneClass = card.tone === "success"
    ? "border-success/30 bg-success/10"
    : card.tone === "warning"
      ? "border-warning/35 bg-warning/10"
      : card.tone === "danger"
        ? "border-danger/35 bg-danger/10"
        : "border-border/70 bg-secondary/25";

  return (
    <div className={cn("min-w-[9rem] flex-1 rounded border px-2.5 py-2", toneClass)}>
      <div className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{card.label}</div>
      <div className="mt-1 break-words text-sm font-semibold leading-5 text-foreground">{card.value}</div>
      <p className="mt-0.5 break-words text-xs leading-5 text-muted-foreground">{card.detail}</p>
    </div>
  );
}

function DataOperationsLoadingPanel({ state }: { state: DataOperationsLoadingState }) {
  return (
    <section
      role={state.role}
      aria-live={state.ariaLive}
      aria-busy={state.ariaBusy}
      aria-label={state.regionLabel}
      className="panel-surface-strong grid gap-4 px-4 py-4 lg:grid-cols-[1fr_auto]"
    >
      <div className="min-w-0">
        <div className="eyebrow-label">Data lane</div>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <span className="inline-flex h-2.5 w-2.5 animate-pulse rounded-full bg-primary" aria-hidden="true" />
          <h2 className="font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {state.title}
          </h2>
          <Badge variant="warning">{state.statusLabel}</Badge>
        </div>
        <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">{state.description}</p>
        <p className="mt-2 max-w-3xl text-sm leading-6 text-foreground">{state.detail}</p>
        <div className="mt-4 flex flex-wrap gap-2" aria-label="Data loading dependencies">
          {state.chips.map((chip) => (
            <span key={chip.label} className="toolbar-chip">
              <span className="text-muted-foreground">{chip.label}</span>
              <span className="font-mono text-warning">{chip.value}</span>
            </span>
          ))}
        </div>
      </div>
      <div className="flex flex-wrap items-start gap-2 lg:justify-end">
        {state.actions.map((action) => (
          <Button key={action.id} asChild variant={action.variant} size="sm">
            <Link to={action.href} aria-label={action.ariaLabel}>
              {action.id === "settings" ? (
                <DatabaseZap className="h-4 w-4" aria-hidden="true" />
              ) : (
                <RadioTower className="h-4 w-4" aria-hidden="true" />
              )}
              {action.label}
            </Link>
          </Button>
        ))}
        <RefreshCcw className="mt-2 h-4 w-4 animate-spin text-primary" aria-hidden="true" />
      </div>
    </section>
  );
}

function DataUploadIntakePanel({
  state,
  onTemplateSelect,
  onFileSelect
}: {
  state: DataUploadPanelState;
  onTemplateSelect: (templateId: string) => void;
  onFileSelect: (file: File | null) => void;
}) {
  const statusId = "data-upload-status";
  const disabledReasonId = `${state.fileInput.id}-disabled-reason`;

  return (
    <section aria-labelledby="data-upload-intake-title" className="workspace-region">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className="eyebrow-label">Source intake</div>
            <CardTitle id="data-upload-intake-title" className="mt-2 flex items-center gap-2">
              <FileUp className="h-5 w-5 text-primary" aria-hidden="true" />
              {state.title}
            </CardTitle>
            <CardDescription className="mt-2">{state.description}</CardDescription>
          </div>
          <Badge variant={state.statusTone} dot={state.statusTone !== "paper"}>
            {state.statusLabel}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,0.95fr)_minmax(20rem,0.55fr)]">
        <div className="grid gap-4">
          <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
            <label htmlFor="data-upload-template-select" className="grid gap-1 text-sm">
              {state.templateSelectLabel}
              <select
                id="data-upload-template-select"
                value={state.selectedTemplateId}
                onChange={(event) => onTemplateSelect(event.currentTarget.value)}
                aria-label="Data upload template"
                className="min-h-10 rounded-md border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              >
                {state.templateOptions.map((option) => (
                  <option key={option.value} value={option.value}>{option.label}</option>
                ))}
              </select>
            </label>
            {state.templateDownload ? (
              <Button asChild variant="outline" size="sm">
                <a
                  href={state.templateDownload.href}
                  download={state.templateDownload.fileName}
                  aria-label={state.templateDownload.ariaLabel}
                >
                  <Download className="h-4 w-4" aria-hidden="true" />
                  {state.templateDownload.label}
                </a>
              </Button>
            ) : null}
          </div>

          {state.selectedTemplate ? (
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="outline">{state.selectedTemplate.dataDomain}</Badge>
                <span className="font-mono text-xs text-muted-foreground">{state.selectedTemplate.targetWorkflow}</span>
              </div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{state.selectedTemplate.description}</p>
              <div className="mt-3 grid gap-3 md:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
                <div className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
                  <div className="eyebrow-label">Source setup</div>
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {state.sourceKinds.map((sourceKind) => (
                      <Badge key={sourceKind} variant="outline">{sourceKind}</Badge>
                    ))}
                  </div>
                  <ul className="mt-2 grid gap-1.5 text-xs leading-5 text-muted-foreground">
                    {state.setupChecklist.map((item) => (
                      <li key={item} className="flex gap-2">
                        <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-success" aria-hidden="true" />
                        <span>{item}</span>
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
                  <div className="eyebrow-label">Mapping readiness</div>
                  <p className="mt-2 text-xs font-semibold text-foreground">{state.mappingSummary}</p>
                  <ul className="mt-2 grid gap-1.5 text-xs leading-5 text-muted-foreground">
                    {state.mappingGuidance.map((item) => (
                      <li key={item} className="flex gap-2">
                        <FileSpreadsheet className="mt-0.5 h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
                        <span>{item}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              </div>
              <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {state.selectedTemplateFields.map((field) => (
                  <div key={field.id} className="rounded-md border border-border/60 bg-background/45 px-2.5 py-2">
                    <div className="flex items-center justify-between gap-2">
                      <div className="eyebrow-label">{field.label}</div>
                      <Badge variant={field.required ? "warning" : "outline"}>{field.requiredLabel}</Badge>
                    </div>
                    <div className="mt-1 truncate font-mono text-xs text-foreground" title={field.name}>{field.name}</div>
                    <p className="mt-1 line-clamp-2 text-xs leading-5 text-muted-foreground">{field.description}</p>
                    <p className="mt-1 truncate font-mono text-[11px] text-muted-foreground" title={field.example}>{field.example}</p>
                  </div>
                ))}
              </div>
            </div>
          ) : null}
        </div>

        <div className="row-detail-panel h-fit min-w-0" role="region" aria-labelledby="data-upload-preview-title">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="eyebrow-label">Preview</div>
              <h3 id="data-upload-preview-title" className="mt-2 text-sm font-semibold text-foreground">Retained source file</h3>
              <p id={statusId} role="status" aria-live="polite" className="mt-2 text-sm leading-6 text-muted-foreground">
                {state.resultSummary}
              </p>
            </div>
            <Badge variant={state.statusTone}>{state.statusLabel}</Badge>
          </div>

          <label htmlFor={state.fileInput.id} className="mt-3 grid gap-1 text-sm">
            {state.fileInput.label}
            <input
              id={state.fileInput.id}
              type="file"
              accept={state.acceptedFileTypes}
              disabled={state.fileInput.disabled}
              aria-label={state.fileInput.ariaLabel}
              aria-describedby={joinDescribedByIds(statusId, `${state.fileInput.id}-help`, disabledReasonId)}
              className="min-h-10 rounded-md border border-border bg-background px-3 py-2 text-sm file:mr-3 file:rounded-sm file:border-0 file:bg-primary file:px-3 file:py-1.5 file:text-xs file:font-semibold file:text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              onChange={(event) => {
                const file = event.currentTarget.files?.[0] ?? null;
                void onFileSelect(file);
                event.currentTarget.value = "";
              }}
            />
            <FieldSupportText
              helpText={`Accepted ${state.acceptedFileTypes || ".csv"} up to ${state.maxFileSizeLabel}.`}
              helpId={`${state.fileInput.id}-help`}
              disabledReason={state.fileInput.disabledReason}
              disabledReasonId={disabledReasonId}
            />
          </label>

          {state.retainedPath ? (
            <div className="mt-3 grid gap-2">
              <FieldTile field={{ id: "retained-path", label: "Retained path", value: state.retainedPath }} />
            </div>
          ) : null}

          {state.issueRows.length > 0 ? (
            <div className="mt-3 grid gap-2" role="alert" aria-label="Upload validation issues">
              {state.issueRows.map((issue) => (
                <StatusBanner
                  key={issue.id}
                  tone={issue.tone === "danger" ? "danger" : "warning"}
                  title={`${issue.severity} · ${issue.field} · ${issue.rowLabel}`}
                  detail={issue.message}
                />
              ))}
            </div>
          ) : null}

          {state.previewRows.length > 0 ? (
            <div className="mt-3 overflow-x-auto rounded-md border border-border/70" aria-label="Upload preview rows">
              <table className="dense-data-table min-w-full">
                <thead>
                  <tr>
                    {state.previewHeaders.map((header) => (
                      <th key={header} scope="col">{header}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {state.previewRows.map((row) => (
                    <tr key={row.id}>
                      {row.values.map((value) => (
                        <td key={value.id}>{value.value || "-"}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </div>
      </CardContent>
    </section>
  );
}

function ProviderEmptyState({
  state,
  onSetup
}: {
  state: DataOperationsEmptyState;
  onSetup: () => void;
}) {
  return (
    <div
      role="status"
      className="rounded-lg border border-dashed border-border/80 bg-secondary/20 px-3 py-4 text-sm text-muted-foreground"
    >
      <div className="font-semibold text-foreground">{state.title}</div>
      <p className="mt-1 leading-6">{state.description}</p>
      <Button type="button" variant="outline" size="sm" className="mt-3" onClick={onSetup}>
        <Plus className="h-3.5 w-3.5" aria-hidden="true" />
        Add provider
      </Button>
    </div>
  );
}

function ProviderDetailPanel({
  detail,
  emptyState,
  activeTab,
  onTabSelect,
  onVerify
}: {
  detail: DataOperationsProviderDetailState | null;
  emptyState: DataOperationsEmptyState | null;
  activeTab: DataOperationsProviderDetailState["activeTab"];
  onTabSelect: (tab: DataOperationsProviderDetailState["activeTab"]) => void;
  onVerify: () => void;
}) {
  if (!detail) {
    return (
      <WorkspaceInspectorHost
        id={DATA_PROVIDER_DETAIL_PANEL_ID}
        label="Provider detail empty state"
        title={emptyState?.title ?? "No provider selected"}
        subtitle="Provider Detail"
        className="h-fit min-w-0"
      >
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          {emptyState?.description ?? "Select a provider row to inspect trust evidence and recovery guidance."}
        </p>
      </WorkspaceInspectorHost>
    );
  }

  return (
    <WorkspaceInspectorHost
      id={detail.id}
      label={detail.ariaLabel}
      title={detail.title}
      subtitle={detail.subtitle}
      status={(
        <Badge
          variant={detail.statusTone === "danger" ? "danger" : detail.statusTone === "warning" ? "warning" : "success"}
          dot
        >
          {detail.status}
        </Badge>
      )}
      className="h-fit min-w-0"
    >
      <p className="text-sm leading-6 text-muted-foreground">{detail.description}</p>
      <Tabs
        aria-label={`${detail.title} provider detail tabs`}
        className="mt-3"
        value={activeTab}
        onValueChange={(tab) => onTabSelect(tab as DataOperationsProviderDetailState["activeTab"])}
        tabs={detail.tabs.map((tab) => ({
          id: tab.id,
          label: tab.label,
          panelId: tab.ariaControls
        }))}
      >
        {detail.tabs.map((tab) => (
          <TabPanel key={tab.id}>
            <ProviderDetailTabPanel
              detail={detail}
              activeTab={tab.id as DataOperationsProviderDetailState["activeTab"]}
              onVerify={onVerify}
            />
          </TabPanel>
        ))}
      </Tabs>
    </WorkspaceInspectorHost>
  );
}

function ProviderDetailTabPanel({
  detail,
  activeTab,
  onVerify
}: {
  detail: DataOperationsProviderDetailState;
  activeTab: DataOperationsProviderDetailState["activeTab"];
  onVerify: () => void;
}) {
  if (activeTab === "credentials") {
    return (
      <div id={`${DATA_PROVIDER_DETAIL_PANEL_ID}-credentials`} role="tabpanel" className="mt-3">
        <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          {detail.credentialFields.map((field) => (
            <FieldTile key={field.id} field={field} />
          ))}
        </div>
        <div className="mt-3 rounded-md border border-border/60 bg-background/45 px-3 py-2">
          <div className="eyebrow-label">Secret handling</div>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">
            Raw secrets are never displayed after submit. Use Settings to replace or clear stored values.
          </p>
        </div>
      </div>
    );
  }

  if (activeTab === "diagnostics") {
    return (
      <div id={`${DATA_PROVIDER_DETAIL_PANEL_ID}-diagnostics`} role="tabpanel" className="mt-3 space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border/60 bg-background/45 px-3 py-2">
          <div className="min-w-0">
            <div className="eyebrow-label">Diagnostics</div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{detail.verifyAction.statusLabel}</p>
            {detail.verifyAction.details.length > 0 ? (
              <ul className="mt-2 grid gap-1 text-xs leading-5 text-muted-foreground">
                {detail.verifyAction.details.map((item) => <li key={item}>{item}</li>)}
              </ul>
            ) : null}
          </div>
          <Button
            size="sm"
            variant="outline"
            onClick={() => void onVerify()}
            disabled={detail.verifyAction.disabled}
            disabledReason={detail.verifyAction.disabledReason}
            busy={detail.verifyAction.busy}
            aria-label={detail.verifyAction.ariaLabel}
          >
            <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
            {detail.verifyAction.label}
          </Button>
        </div>
        {detail.diagnosticsEmptyState ? (
          <div
            role="status"
            aria-label={`${detail.title} diagnostics empty state`}
            className="rounded-md border border-dashed border-border/80 bg-secondary/20 px-3 py-3"
          >
            <div className="font-semibold text-foreground">{detail.diagnosticsEmptyState.title}</div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{detail.diagnosticsEmptyState.description}</p>
          </div>
        ) : (
          <div className="grid gap-2">
            {detail.diagnostics.map((diagnostic) => (
              <ProviderDiagnosticRow key={diagnostic.id} diagnostic={diagnostic} />
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <div id={`${DATA_PROVIDER_DETAIL_PANEL_ID}-overview`} role="tabpanel" className="mt-3">
      <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
        {detail.overviewFields.map((field) => (
          <FieldTile key={field.id} field={field} />
        ))}
      </div>
      <div className="mt-3 rounded-md border border-border/60 bg-background/45 px-3 py-2">
        <div className="eyebrow-label">Recommended action</div>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{detail.actionText}</p>
        <p className="mt-2 text-xs leading-5 text-muted-foreground">Reason: {detail.reasonLabelText}</p>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">Gate: {detail.gateImpactText}</p>
        {detail.reasonLabelText !== detail.reasonCodeText ? (
          <TechnicalDetails
            label="System details"
            description="Raw provider status retained for diagnostics and support handoff."
            className="mt-3"
          >
            <div className="grid gap-1 text-xs">
              <span className="text-muted-foreground">Reason code</span>
              <code className="break-all text-foreground">{detail.reasonCodeText}</code>
            </div>
          </TechnicalDetails>
        ) : null}
      </div>
    </div>
  );
}

function ProviderDiagnosticRow({ diagnostic }: { diagnostic: DataOperationsProviderDiagnosticRow }) {
  const variant = diagnostic.status === "pass"
    ? "success"
    : diagnostic.status === "fail"
      ? "danger"
      : diagnostic.status === "warning"
        ? "warning"
        : "outline";

  return (
    <div className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="text-xs font-semibold text-foreground">{diagnostic.label}</div>
        <Badge variant={variant}>{diagnostic.statusLabel}</Badge>
      </div>
      <p className="mt-1 text-xs leading-5 text-muted-foreground">{diagnostic.detail}</p>
    </div>
  );
}

function dataStatusToneToBannerTone(tone: "default" | "warning" | "danger" | "success"): "danger" | "info" | "success" | "warning" {
  switch (tone) {
    case "danger":
      return "danger";
    case "success":
      return "success";
    case "warning":
      return "warning";
    case "default":
    default:
      return "info";
  }
}

function ProviderSetupDialog({ vm }: { vm: DataOperationsVm }) {
  return (
    <Dialog open={vm.providerSetupOpen} onOpenChange={(open) => { if (!open) vm.closeProviderSetup(); }}>
      <DialogContent aria-labelledby={vm.providerSetupDialogState.titleId} aria-describedby={vm.providerSetupDialogState.descriptionId}>
        <div className="flex items-start justify-between gap-4">
          <DialogHeader className="mb-0">
            <div className="eyebrow-label">Data providers</div>
            <DialogTitle id={vm.providerSetupDialogState.titleId}>Configure provider</DialogTitle>
            <DialogDescription id={vm.providerSetupDialogState.descriptionId}>
              Register a data or brokerage provider with Meridian and seed routing for selected capabilities.
            </DialogDescription>
          </DialogHeader>
          <DialogCloseButton
            label={vm.providerSetupDialogState.closeButtonLabel}
            disabled={vm.providerPhase === "submitting"}
            disabledReason={vm.providerSetupDialogState.closeButtonDisabledReason}
            onClick={vm.closeProviderSetup}
          />
        </div>

        {vm.providerPhase === "success" && vm.providerSetupResult ? (
          <div className="mt-5">
            <div className="flex items-center gap-3 rounded-lg border border-success/35 bg-success/10 px-4 py-4">
              <CheckCircle2 className="h-5 w-5 shrink-0 text-success" aria-hidden="true" />
              <div>
                <div className="font-semibold text-success">{vm.providerSetupResult.providerName} configured</div>
                <p className="mt-1 text-sm text-muted-foreground">{vm.providerSetupResult.message}</p>
              </div>
            </div>
            <div
              className="mt-4 rounded-lg border border-border/70 bg-secondary/25 px-3 py-3"
              role="region"
              aria-label={vm.providerSetupDialogState.successMetadata.metadataAriaLabel}
            >
              <div className="eyebrow-label">Routing posture</div>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {vm.providerSetupDialogState.successMetadata.rows.map((row) => (
                  <FieldTile key={row.id} field={row} />
                ))}
              </div>
              {vm.providerSetupDialogState.successMetadata.warnings.length > 0 ? (
                <div
                  className="mt-3 rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning"
                  role="status"
                  aria-label={vm.providerSetupDialogState.successMetadata.warningsAriaLabel}
                >
                  <ul className="grid gap-1">
                    {vm.providerSetupDialogState.successMetadata.warnings.map((warning) => (
                      <li key={warning}>{warning}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </div>
            <div className="mt-4">
              <ProviderSetupWorkflowSteps steps={vm.providerSetupDialogState.workflowSteps} />
            </div>
            <div
              className="mt-4 rounded-lg border border-border/70 bg-secondary/25 px-3 py-3"
              role="region"
              aria-label={vm.providerSetupDialogState.successPanel.ariaLabel}
            >
              <div className="eyebrow-label">{vm.providerSetupDialogState.successPanel.title}</div>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {vm.providerSetupDialogState.successActions.map((action) => (
                  <ProviderSetupNextAction
                    key={action.id}
                    action={action}
                    onNavigate={vm.closeProviderSetup}
                  />
                ))}
              </div>
            </div>
            <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button variant="outline" onClick={vm.closeProviderSetup}>Done</Button>
              <Button onClick={vm.openProviderSetup}>Configure another</Button>
            </div>
          </div>
        ) : (
          <>
            <div className="mt-5 grid gap-4" role="group" aria-label={vm.providerSetupDialogState.formLabel}>
              <label htmlFor="provider-setup-kind" className="grid gap-1 text-sm">
                {vm.providerSetupDialogState.providerKindField.label}
                <select
                  id={vm.providerSetupDialogState.providerKindField.id}
                  className="rounded-md border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  value={vm.providerForm.kind}
                  aria-label={vm.providerSetupDialogState.providerKindField.ariaLabel}
                  disabled={vm.providerSetupDialogState.providerKindField.disabled}
                  aria-describedby={joinDescribedByIds(
                    `${vm.providerSetupDialogState.providerKindField.id}-description`,
                    `${vm.providerSetupDialogState.providerKindField.id}-disabled-reason`
                  )}
                  onChange={(e) => vm.updateProviderForm("kind", e.target.value)}
                >
                  {vm.providerSetupDialogState.providerKindField.options.map((p) => (
                    <option key={p.value} value={p.value}>{p.label}</option>
                  ))}
                </select>
                <FieldSupportText
                  helpId={`${vm.providerSetupDialogState.providerKindField.id}-description`}
                  helpText={vm.providerSetupDialogState.providerKindField.description}
                  disabledReason={vm.providerSetupDialogState.providerKindField.disabledReason}
                  disabledReasonId={`${vm.providerSetupDialogState.providerKindField.id}-disabled-reason`}
                />
              </label>

              <div
                className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-3"
                role="region"
                aria-label={`${vm.providerSetupDialogState.selectedProviderSummary.providerLabel} setup summary`}
              >
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="font-semibold">{vm.providerSetupDialogState.selectedProviderSummary.providerLabel}</div>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">
                      {vm.providerSetupDialogState.selectedProviderSummary.description}
                    </p>
                  </div>
                  {vm.providerSetupDialogState.selectedProviderSummary.noCredentialMessage ? (
                    <Badge variant="success">No key needed</Badge>
                  ) : null}
                </div>
                <div className="mt-3 grid gap-2 sm:grid-cols-3">
                  {vm.providerSetupDialogState.selectedProviderSummary.rows.map((row) => (
                    <FieldTile key={row.id} field={row} />
                  ))}
                </div>
                {vm.providerSetupDialogState.selectedProviderSummary.noCredentialMessage ? (
                  <p className="mt-3 rounded-md border border-success/30 bg-success/10 px-3 py-2 text-xs leading-5 text-success">
                    {vm.providerSetupDialogState.selectedProviderSummary.noCredentialMessage}
                  </p>
                ) : null}
              </div>

              {vm.providerSetupDialogState.institutionSearch ? (
                <ProviderSetupInstitutionSearch
                  state={vm.providerSetupDialogState.institutionSearch}
                  onQueryChange={vm.updatePlaidInstitutionQuery}
                  onSearch={vm.searchPlaidInstitutions}
                  onSelect={vm.selectPlaidInstitution}
                  onCreateLinkToken={vm.createPlaidLinkToken}
                />
              ) : null}

              <ProviderSetupWorkflowSteps steps={vm.providerSetupDialogState.workflowSteps} />

              <label htmlFor={vm.providerSetupDialogState.displayNameField.id} className="grid gap-1 text-sm">
                {vm.providerSetupDialogState.displayNameField.label}
                <input
                  id={vm.providerSetupDialogState.displayNameField.id}
                  className="rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  value={vm.providerSetupDialogState.displayNameField.value}
                  aria-label={vm.providerSetupDialogState.displayNameField.ariaLabel}
                  disabled={vm.providerSetupDialogState.displayNameField.disabled}
                  aria-describedby={joinDescribedByIds(`${vm.providerSetupDialogState.displayNameField.id}-disabled-reason`)}
                  onChange={(e) => vm.updateProviderForm(vm.providerSetupDialogState.displayNameField.field, e.target.value)}
                />
                <FieldSupportText
                  disabledReason={vm.providerSetupDialogState.displayNameField.disabledReason}
                  disabledReasonId={`${vm.providerSetupDialogState.displayNameField.id}-disabled-reason`}
                />
              </label>

              <label htmlFor={vm.providerSetupDialogState.environmentField.id} className="grid gap-1 text-sm">
                {vm.providerSetupDialogState.environmentField.label}
                <select
                  id={vm.providerSetupDialogState.environmentField.id}
                  className="rounded-md border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  value={vm.providerSetupDialogState.environmentField.value}
                  aria-label={vm.providerSetupDialogState.environmentField.ariaLabel}
                  disabled={vm.providerSetupDialogState.environmentField.disabled}
                  aria-describedby={joinDescribedByIds(
                    `${vm.providerSetupDialogState.environmentField.id}-description`,
                    `${vm.providerSetupDialogState.environmentField.id}-disabled-reason`
                  )}
                  onChange={(e) => vm.updateProviderForm("environment", e.target.value)}
                >
                  {vm.providerSetupDialogState.environmentField.options.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
                <FieldSupportText
                  helpId={`${vm.providerSetupDialogState.environmentField.id}-description`}
                  helpText={vm.providerSetupDialogState.environmentField.description}
                  disabledReason={vm.providerSetupDialogState.environmentField.disabledReason}
                  disabledReasonId={`${vm.providerSetupDialogState.environmentField.id}-disabled-reason`}
                />
              </label>

              {vm.providerSetupDialogState.liveAcknowledgement.visible ? (
                <div className="rounded-md border border-live-env/35 bg-live-env/10 px-3 py-2.5">
                  <Checkbox
                    id={vm.providerSetupDialogState.liveAcknowledgement.id}
                    checked={vm.providerSetupDialogState.liveAcknowledgement.checked}
                    disabled={vm.providerSetupDialogState.liveAcknowledgement.disabled}
                    aria-label={vm.providerSetupDialogState.liveAcknowledgement.ariaLabel}
                    label={<span className="text-live-env">{vm.providerSetupDialogState.liveAcknowledgement.label}</span>}
                    hint={vm.providerSetupDialogState.liveAcknowledgement.detail}
                    onCheckedChange={vm.setProviderLiveAcknowledged}
                  />
                </div>
              ) : null}

              {vm.providerSetupDialogState.credentialFields.map((field) => (
                <label key={field.id} htmlFor={field.id} className="grid gap-1 text-sm">
                  {field.label}
                  <input
                    id={field.id}
                    type={field.type}
                    autoComplete={field.autoComplete}
                    className="rounded-md border border-border bg-background px-3 py-2 font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    value={field.value}
                    aria-label={field.ariaLabel}
                    placeholder={field.placeholder ?? undefined}
                    disabled={field.disabled}
                    aria-describedby={joinDescribedByIds(`${field.id}-disabled-reason`)}
                    onChange={(e) => vm.updateProviderForm(field.field, e.target.value)}
                  />
                  <FieldSupportText
                    disabledReason={field.disabledReason}
                    disabledReasonId={`${field.id}-disabled-reason`}
                  />
                </label>
              ))}

              <fieldset>
                <legend className="mb-2 text-sm">Capabilities</legend>
                <div className="grid gap-2 sm:grid-cols-2">
                  {vm.providerSetupDialogState.capabilityOptions.map((cap) => (
                    <div
                      key={cap.id}
                      className={cn(
                        "flex cursor-pointer items-start gap-3 rounded-md border px-3 py-2.5 transition-colors",
                        cap.selected
                          ? "border-primary/40 bg-primary/[0.08]"
                          : "border-border/70 bg-secondary/20 hover:bg-secondary/35"
                      )}
                    >
                      <Checkbox
                        checked={cap.selected}
                        disabled={cap.disabled}
                        aria-describedby={joinDescribedByIds(`${cap.id}-description`, `${cap.id}-disabled-reason`)}
                        aria-label={cap.label}
                        label={cap.label}
                        onCheckedChange={() => vm.toggleProviderCapability(cap.id)}
                      />
                      <div className="min-w-0">
                        <div id={`${cap.id}-description`} className="text-xs text-muted-foreground">{cap.description}</div>
                        <FieldSupportText
                          disabledReason={cap.disabledReason}
                          disabledReasonId={`${cap.id}-disabled-reason`}
                        />
                      </div>
                    </div>
                  ))}
                </div>
              </fieldset>
            </div>

            <StatusBanner
              id="provider-setup-status"
              role="status"
              aria-live="polite"
              tone="info"
              title="Provider setup status"
              detail={vm.providerSetupDialogState.statusLabel}
              className="mt-4"
            />

        {vm.providerSetupError && (
          <StatusBanner
            role="alert"
            className="mt-3"
            tone="danger"
            title={vm.providerSetupError.summary}
            detail={vm.providerSetupError.details.length > 0 ? (
                <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5 text-danger/90">
                  {vm.providerSetupError.details.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              ) : null}
          />
        )}

            <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button
                variant="outline"
                onClick={vm.closeProviderSetup}
                disabled={vm.providerSetupDialogState.cancelAction.disabled}
                disabledReason={vm.providerSetupDialogState.cancelAction.disabledReason}
                aria-label={vm.providerSetupDialogState.cancelAction.ariaLabel}
              >
                {vm.providerSetupDialogState.cancelAction.label}
              </Button>
              <Button
                onClick={() => void vm.submitProviderSetup()}
                disabled={vm.providerSetupDialogState.submitAction.disabled}
                disabledReason={vm.providerSetupDialogState.submitAction.disabledReason}
                busy={vm.providerSetupDialogState.submitAction.busy}
                busyLabel={vm.providerSetupDialogState.submitAction.busyLabel}
                aria-label={vm.providerSetupDialogState.submitAction.ariaLabel}
              >
                {vm.providerSetupDialogState.submitAction.label}
              </Button>
            </div>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

function ProviderSetupInstitutionSearch({
  state,
  onQueryChange,
  onSearch,
  onSelect,
  onCreateLinkToken
}: {
  state: ProviderSetupInstitutionSearchState;
  onQueryChange: (value: string) => void;
  onSearch: () => void;
  onSelect: (institutionId: string) => void;
  onCreateLinkToken: () => void;
}) {
  const resultListId = `${state.id}-results`;
  return (
    <div
      className="rounded-lg border border-border/70 bg-secondary/20 px-3 py-3"
      role="region"
      aria-label="Bank connection institution search"
    >
      <label htmlFor={state.id} className="grid gap-1 text-sm">
        {state.label}
        <div className="flex flex-col gap-2 sm:flex-row">
          <input
            id={state.id}
            role="combobox"
            aria-expanded={state.results.length > 0}
            aria-controls={resultListId}
            aria-label={state.ariaLabel}
            className="min-h-10 flex-1 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            value={state.value}
            placeholder={state.placeholder}
            disabled={state.disabled}
            aria-describedby={joinDescribedByIds(`${state.id}-description`, `${state.id}-status`, `${state.id}-disabled-reason`)}
            onChange={(event) => onQueryChange(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                if (!state.searchAction.disabled) {
                  onSearch();
                }
              }
            }}
          />
          <Button
            type="button"
            variant="outline"
            onClick={onSearch}
            disabled={state.searchAction.disabled}
            disabledReason={state.searchAction.disabledReason}
            busy={state.searchAction.busy}
            aria-label={state.searchAction.ariaLabel}
          >
            {state.searchAction.label}
          </Button>
        </div>
        <FieldSupportText
          helpId={`${state.id}-description`}
          helpText={state.description}
          disabledReason={state.disabledReason}
          disabledReasonId={`${state.id}-disabled-reason`}
        />
      </label>
      <div id={`${state.id}-status`} role="status" aria-live="polite" className="mt-2 text-xs leading-5 text-muted-foreground">
        {state.statusLabel}
      </div>
      {state.results.length > 0 ? (
        <div id={resultListId} role="listbox" aria-label="Supported financial institutions" className="mt-3 grid gap-2">
          {state.results.map((institution) => (
            <button
              key={institution.institutionId}
              type="button"
              role="option"
              aria-selected={institution.selected}
              className={cn(
                "rounded-md border px-3 py-2 text-left text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                institution.selected
                  ? "border-primary/45 bg-primary/[0.08]"
                  : "border-border/70 bg-background/55 hover:bg-secondary/35"
              )}
              onClick={() => onSelect(institution.institutionId)}
            >
              <span className="flex items-center justify-between gap-3">
                <span className="font-medium text-foreground">{institution.name}</span>
                {institution.selected ? <Badge variant="success">Selected</Badge> : null}
              </span>
              <span className="mt-1 block text-xs text-muted-foreground">{institution.detail}</span>
            </button>
          ))}
        </div>
      ) : null}
      {state.selectedInstitutionLabel ? (
        <div className="mt-3 rounded-md border border-success/30 bg-success/10 px-3 py-2 text-xs leading-5 text-success">
          {state.selectedInstitutionLabel} is selected for the next bank connection step.
        </div>
      ) : null}
      <div
        className="mt-3 rounded-md border border-border/70 bg-background/55 px-3 py-3"
        role="region"
        aria-label="Plaid sandbox bank connection"
      >
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <div className="text-sm font-medium text-foreground">Secure bank connection</div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{state.linkTokenStatusLabel}</p>
          </div>
          <Button
            type="button"
            variant="default"
            onClick={onCreateLinkToken}
            disabled={state.linkTokenAction.disabled}
            disabledReason={state.linkTokenAction.disabledReason}
            busy={state.linkTokenAction.busy}
            aria-label={state.linkTokenAction.ariaLabel}
          >
            {state.linkTokenAction.label}
          </Button>
        </div>
        {state.linkTokenResult ? (
          <div className="mt-3 grid gap-2 text-xs sm:grid-cols-2">
            <FieldTile field={{ id: "plaid-link-token", label: "Link token", value: state.linkTokenResult.linkTokenPreview }} />
            <FieldTile field={{ id: "plaid-link-environment", label: "Environment", value: state.linkTokenResult.environmentLabel ?? "Sandbox" }} />
            <FieldTile field={{ id: "plaid-link-institution", label: "Institution", value: state.linkTokenResult.institutionLabel ?? "Selected bank" }} />
            <FieldTile field={{ id: "plaid-link-expiration", label: "Expires", value: state.linkTokenResult.expirationLabel ?? "Temporary" }} />
          </div>
        ) : null}
        {state.linkedEvidence ? (
          <div
            className="mt-3 rounded-md border border-success/30 bg-success/10 px-3 py-3 text-xs leading-5 text-success"
            role="status"
            aria-label="Plaid linked account evidence"
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="font-medium">
                {state.linkedEvidence.institutionName} linked
              </div>
              <Badge variant="success">{state.linkedEvidence.accountCountLabel}</Badge>
            </div>
            <div className="mt-2 grid gap-2 sm:grid-cols-3">
              <FieldTile field={{ id: "plaid-linked-item", label: "Item", value: state.linkedEvidence.itemId }} />
              <FieldTile field={{ id: "plaid-linked-status", label: "Status", value: state.linkedEvidence.status }} />
              <FieldTile field={{ id: "plaid-linked-request", label: "Request", value: state.linkedEvidence.requestId ?? "Recorded" }} />
            </div>
            {state.linkedEvidence.accounts.length > 0 ? (
              <ul className="mt-3 grid gap-2" aria-label="Linked Plaid accounts">
                {state.linkedEvidence.accounts.map((account) => (
                  <li key={account.id} className="rounded-md border border-success/25 bg-background/65 px-3 py-2 text-foreground">
                    <div className="font-medium">{account.name}</div>
                    <div className="mt-1 text-muted-foreground">{account.detail || account.id}</div>
                  </li>
                ))}
              </ul>
            ) : null}
          </div>
        ) : null}
        {state.sandboxGuide ? (
          <div className="mt-3 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
            <div className="font-medium">{state.sandboxGuide.title}</div>
            <p className="mt-1">{state.sandboxGuide.detail}</p>
            <dl className="mt-2 grid gap-1 sm:grid-cols-2">
              <div>
                <dt className="font-medium">Username</dt>
                <dd className="font-mono">{state.sandboxGuide.username}</dd>
              </div>
              <div>
                <dt className="font-medium">Password</dt>
                <dd className="font-mono">{state.sandboxGuide.password}</dd>
              </div>
            </dl>
          </div>
        ) : null}
        {state.linkTokenPhase === "error" && state.linkTokenStatusLabel ? (
          <p className="mt-3 rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-xs leading-5 text-danger">
            {state.linkTokenStatusLabel}
          </p>
        ) : null}
      </div>
    </div>
  );
}

function ProviderSetupWorkflowSteps({ steps }: { steps: ProviderSetupWorkflowStepState[] }) {
  return (
    <div
      className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-3"
      role="region"
      aria-label="Data integration workflow"
    >
      <div className="eyebrow-label">Data integration flow</div>
      <ol className="mt-3 grid gap-2 sm:grid-cols-2">
        {steps.map((step) => (
          <li
            key={step.id}
            className={cn(
              "rounded-md border px-3 py-2",
              step.status === "complete"
                ? "border-success/35 bg-success/10"
                : step.status === "current"
                  ? "border-primary/40 bg-primary/[0.08]"
                  : "border-border/70 bg-background/40"
            )}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-sm font-medium">{step.label}</span>
              <Badge
                variant={step.status === "complete" ? "success" : step.status === "current" ? "default" : "outline"}
              >
                {step.statusLabel}
              </Badge>
            </div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{step.description}</p>
          </li>
        ))}
      </ol>
    </div>
  );
}

function ProviderSetupNextAction({
  action,
  onNavigate
}: {
  action: ProviderSetupNextActionState;
  onNavigate: () => void;
}) {
  const Icon = providerSetupNextActionIcons[action.id];

  return (
    <Button asChild variant={action.variant} size="sm" className="justify-start">
      <Link to={action.href} aria-label={action.ariaLabel} onClick={onNavigate}>
        <Icon className="h-4 w-4" aria-hidden="true" />
        {action.label}
      </Link>
    </Button>
  );
}

const providerSetupNextActionIcons: Record<ProviderSetupNextActionState["id"], LucideIcon> = {
  "live-quotes": RadioTower,
  backfill: TimerReset,
  readiness: ShieldCheck,
  "security-master": DatabaseZap,
  "plaid-link": KeyRound,
  "plaid-transfers": RefreshCcw
};

function BackfillTriggerDialog({ vm }: { vm: DataOperationsVm }) {
  return (
    <Dialog open={vm.dialogOpen} onOpenChange={(open) => { if (!open) vm.closeBackfillDialog(); }}>
      <DialogContent
        aria-labelledby={vm.dialogState.titleId}
        aria-describedby={vm.dialogState.descriptionId}
        className="max-w-xl p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <DialogHeader className="mb-0">
            <div className="eyebrow-label">Backfill</div>
            <DialogTitle id={vm.dialogState.titleId}>Trigger backfill</DialogTitle>
            <DialogDescription id={vm.dialogState.descriptionId}>
              Preview the request before writing historical bars.
            </DialogDescription>
          </DialogHeader>
          <DialogCloseButton
            label={vm.dialogState.closeButtonLabel}
            disabled={vm.busy}
            disabledReason={vm.dialogState.closeButtonDisabledReason}
            onClick={vm.closeBackfillDialog}
          />
        </div>

        <dl className="mt-5 grid gap-2 rounded-lg border border-border/80 bg-secondary/20 p-3 sm:grid-cols-3">
          {vm.dialogState.summaryItems.map((item) => (
            <div key={item.id} className="min-w-0">
              <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{item.label}</dt>
              <dd className={cn("mt-1 truncate font-mono text-xs", item.tone === "warning" ? "text-warning" : "text-foreground")}>
                {item.value}
              </dd>
            </div>
          ))}
        </dl>

        <div className="mt-5 grid gap-4" role="group" aria-label={vm.dialogState.formLabel}>
          <label htmlFor={vm.dialogState.providerField.id} className="grid gap-1 text-sm">
            {vm.dialogState.providerField.label}
            <select
              id={vm.dialogState.providerField.id}
              className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
              value={vm.form.provider}
              aria-label={vm.dialogState.providerField.ariaLabel}
              disabled={vm.dialogState.providerField.disabled}
              aria-describedby={joinDescribedByIds(
                `${vm.dialogState.providerField.id}-detail`,
                `${vm.dialogState.providerField.id}-disabled-reason`
              )}
              onChange={(event) => vm.updateBackfillForm("provider", event.target.value)}
            >
              {vm.dialogState.providerOptions.map((provider) => (
                <option key={provider.value} value={provider.value}>
                  {provider.label}
                </option>
              ))}
            </select>
            <FieldSupportText
              helpId={`${vm.dialogState.providerField.id}-detail`}
              helpText={vm.dialogState.selectedProviderDetail}
              disabledReason={vm.dialogState.providerField.disabledReason}
              disabledReasonId={`${vm.dialogState.providerField.id}-disabled-reason`}
            />
            <div className="flex flex-wrap gap-2" aria-label="Backfill provider options">
              {vm.dialogState.providerOptions.map((provider) => (
                <button
                  key={provider.value}
                  type="button"
                  className={cn(
                    "rounded-md border px-2.5 py-1.5 text-left text-xs transition-colors",
                    vm.form.provider === provider.value
                      ? "border-primary/45 bg-primary/[0.08] text-foreground"
                      : "border-border/70 bg-secondary/20 text-muted-foreground hover:bg-secondary/35"
                  )}
                  disabled={vm.dialogState.providerField.disabled}
                  title={provider.description}
                  aria-pressed={vm.form.provider === provider.value}
                  onClick={() => vm.updateBackfillForm("provider", provider.value)}
                >
                  <span className="font-semibold">{provider.label}</span>
                  <span className="ml-2 font-mono text-[10px] uppercase tracking-[0.12em] text-primary">{provider.badge}</span>
                </button>
              ))}
            </div>
          </label>
          <label htmlFor={vm.dialogState.symbolsField.id} className="grid gap-1 text-sm">
            {vm.dialogState.symbolsField.label}
            <input
              id={vm.dialogState.symbolsField.id}
              className="min-h-12 rounded-md border border-border bg-background px-3 py-2 font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
              placeholder={vm.dialogState.symbolsField.placeholder}
              value={vm.form.symbols}
              aria-label={vm.dialogState.symbolsField.ariaLabel}
              aria-invalid={vm.validationError !== null}
              disabled={vm.dialogState.symbolsField.disabled}
              aria-describedby={joinDescribedByIds(vm.dialogState.symbolsField.describedBy, `${vm.dialogState.symbolsField.id}-disabled-reason`)}
              data-dialog-autofocus={vm.dialogState.symbolsField.autoFocus ? "" : undefined}
              onChange={(event) => vm.updateBackfillForm("symbols", event.target.value)}
            />
            <FieldSupportText
              helpId="backfill-symbols-help"
              helpText={vm.symbolsHelpText}
              disabledReason={vm.dialogState.symbolsField.disabledReason}
              disabledReasonId={`${vm.dialogState.symbolsField.id}-disabled-reason`}
            />
          </label>
          <div className="grid gap-3 md:grid-cols-2">
            <label htmlFor={vm.dialogState.fromField.id} className="grid gap-1 text-sm">
              {vm.dialogState.fromField.label}
              <input
                id={vm.dialogState.fromField.id}
                type="date"
                className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
                value={vm.form.from}
                aria-label={vm.dialogState.fromField.ariaLabel}
                disabled={vm.dialogState.fromField.disabled}
                aria-describedby={joinDescribedByIds(`${vm.dialogState.fromField.id}-disabled-reason`)}
                onChange={(event) => vm.updateBackfillForm("from", event.target.value)}
              />
              <FieldSupportText
                disabledReason={vm.dialogState.fromField.disabledReason}
                disabledReasonId={`${vm.dialogState.fromField.id}-disabled-reason`}
              />
            </label>
            <label htmlFor={vm.dialogState.toField.id} className="grid gap-1 text-sm">
              {vm.dialogState.toField.label}
              <input
                id={vm.dialogState.toField.id}
                type="date"
                className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
                value={vm.form.to}
                aria-label={vm.dialogState.toField.ariaLabel}
                disabled={vm.dialogState.toField.disabled}
                aria-describedby={joinDescribedByIds(`${vm.dialogState.toField.id}-disabled-reason`)}
                onChange={(event) => vm.updateBackfillForm("to", event.target.value)}
              />
              <FieldSupportText
                disabledReason={vm.dialogState.toField.disabledReason}
                disabledReasonId={`${vm.dialogState.toField.id}-disabled-reason`}
              />
            </label>
          </div>
        </div>

        <StatusBanner
          id="backfill-form-status"
          role="status"
          aria-live="polite"
          tone={dataStatusToneToBannerTone(vm.dialogState.formStatusTone)}
          title="Backfill request status"
          detail={vm.dialogState.formStatusLabel}
          className="mt-4"
        />

        {vm.feedbackText && (
          <StatusBanner
            id="backfill-form-feedback"
            role="alert"
            className="mt-4"
            tone={vm.feedbackTone === "warning" ? "warning" : "danger"}
            title={vm.feedbackText}
            detail={vm.feedbackDetails.length > 0 ? (
                <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                  {vm.feedbackDetails.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              ) : null}
          />
        )}
        <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>
        {vm.previewResultCard && <BackfillResultCard state={vm.previewResultCard} />}
        {vm.runResultCard && <BackfillResultCard state={vm.runResultCard} />}

        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button
            variant="outline"
            onClick={() => void vm.previewBackfill()}
            disabled={vm.dialogState.previewAction.disabled}
            disabledReason={vm.dialogState.previewAction.disabledReason}
            busy={vm.dialogState.previewAction.busy}
            busyLabel={vm.dialogState.previewAction.busyLabel}
            aria-label={vm.dialogState.previewAction.ariaLabel}
          >
            {vm.dialogState.previewAction.label}
          </Button>
          {vm.preview && (
            <Button
              onClick={() => void vm.runBackfill()}
              disabled={vm.dialogState.runAction.disabled}
              disabledReason={vm.dialogState.runAction.disabledReason}
              busy={vm.dialogState.runAction.busy}
              busyLabel={vm.dialogState.runAction.busyLabel}
              aria-label={vm.dialogState.runAction.ariaLabel}
            >
              {vm.dialogState.runAction.label}
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function FieldTile({ field }: { field: { id: string; label: string; value: string } }) {
  return (
    <div className="rounded-md border border-border/60 bg-background/45 px-2.5 py-2">
      <div className="eyebrow-label">{field.label}</div>
      <div className="mt-1 truncate font-mono text-xs text-foreground">{field.value}</div>
    </div>
  );
}

function BackfillResultCard({ state }: { state: BackfillResultCardState }) {
  return (
    <div
      role="status"
      aria-label={state.ariaLabel}
      className={cn("mt-4 rounded-md border p-3 text-sm", resultToneClass[state.tone])}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="font-semibold">{state.title}</div>
        <div className="font-mono text-xs">{state.statusLabel}</div>
      </div>
      <div className="mt-3 grid gap-2 sm:grid-cols-2">
        {state.rows.map((row) => (
          <FieldTile key={row.id} field={row} />
        ))}
      </div>
      {state.errorText && <p className="mt-3 text-xs leading-5">{state.errorText}</p>}
    </div>
  );
}

function CapabilityMatrixRegion({ panel }: { panel: CapabilityMatrixViewModel }) {
  return (
    <section aria-labelledby="capability-matrix-title" className="workspace-region capability-matrix-region">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="capability-matrix-title">Provider capability matrix</CardTitle>
            <CardDescription>
              Declared provider coverage per instrument type.
              {panel.model ? ` ${panel.model.summary}` : null}
            </CardDescription>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void panel.refresh()}
            disabled={panel.loading}
            aria-label="Refresh provider capability matrix"
          >
            <RefreshCcw className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{panel.loading ? "Refreshing…" : "Refresh"}</span>
          </Button>
        </CardHeader>
        <CardContent>
          {panel.error ? (
            <StatusBanner tone="danger" title="Capability matrix unavailable" detail={panel.error} />
          ) : !panel.model ? (
            <p className="text-sm text-muted-foreground" role="status">Loading capability matrix…</p>
          ) : (
            <div className="grid gap-4">
              <div className="overflow-x-auto">
                <table className="w-full border-collapse text-sm" aria-label="Provider capability by instrument type">
                  <thead>
                    <tr>
                      <th scope="col" className="border-b p-2 text-left font-semibold">Provider</th>
                      {panel.model.columns.map((column) => (
                        <th key={column} scope="col" className="border-b p-2 text-left font-semibold">
                          {column}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {panel.model.rows.map((row) => (
                      <tr key={row.providerId}>
                        <th scope="row" className="border-b p-2 text-left font-mono font-semibold">
                          {row.providerId}
                        </th>
                        {row.cells.map((cell) => (
                          <td
                            key={cell.instrumentType}
                            title={cell.description}
                            className={cn(
                              "border-b p-2 font-mono",
                              cell.supported ? "text-foreground" : "text-muted-foreground/50"
                            )}
                          >
                            <span aria-label={cell.description}>{cell.marks}</span>
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <p className="text-xs text-muted-foreground">
                {CAPABILITY_LEGEND.map((mark) => `${mark.code} = ${mark.label}`).join(" · ")}
              </p>
              {panel.model.failures.length > 0 && (
                <div>
                  <h3 className="text-sm font-semibold">Discovery failures</h3>
                  <ul className="mt-2 grid gap-1.5">
                    {panel.model.failures.map((failure, index) => (
                      <li key={`${failure.stage}-${failure.subject}-${index}`} className="text-sm text-muted-foreground">
                        <Badge variant="danger">{failure.stage}</Badge>{" "}
                        <span className="font-mono">{failure.subject}</span> — {failure.errorType}: {failure.errorMessage}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function CorporateActionInboxRegion({ panel }: { panel: CorporateActionInboxViewModel }) {
  return (
    <section aria-labelledby="corporate-action-inbox-title" className="workspace-region corporate-action-inbox-region">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="corporate-action-inbox-title">
              Corporate action inbox
              {panel.model && panel.model.stagedCount > 0 ? ` (${panel.model.stagedCount})` : null}
            </CardTitle>
            <CardDescription>
              Staged provider announcements awaiting operator review.
              {panel.model ? ` ${panel.model.summary} Last ingest: ${panel.model.lastIngestLabel}.` : null}
            </CardDescription>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void panel.refresh()}
            disabled={panel.loading}
            aria-label="Refresh corporate action inbox"
          >
            <RefreshCcw className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{panel.loading ? "Refreshing…" : "Refresh"}</span>
          </Button>
        </CardHeader>
        <CardContent>
          {panel.error ? (
            <StatusBanner tone="danger" title="Corporate action inbox unavailable" detail={panel.error} />
          ) : !panel.model ? (
            <p className="text-sm text-muted-foreground" role="status">Loading corporate action inbox…</p>
          ) : panel.model.rows.length === 0 ? (
            <p className="text-sm text-muted-foreground" role="status">{panel.model.summary}</p>
          ) : (
            <ul className="grid gap-1.5" aria-label="Staged corporate action proposals">
              {panel.model.rows.map((row) => (
                <li key={row.key} className="flex flex-wrap items-center gap-2 text-sm">
                  <Badge variant={row.tone === "warning" ? "warning" : "outline"}>
                    {row.actionType}
                  </Badge>
                  <span className="font-mono font-semibold">{row.ticker}</span>
                  <span className="text-muted-foreground">
                    {row.valueLabel} · ex {row.exDateLabel} ({row.countdownLabel}) · {row.consensusLabel}
                    {row.dissentingSources.length > 0
                      ? ` · disputed by ${row.dissentingSources.join(", ")}`
                      : ""}
                  </span>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => void panel.apply(row)}
                    disabled={panel.applyingKey !== null}
                    aria-label={`Apply ${row.actionType} for ${row.ticker}`}
                  >
                    {panel.applyingKey === row.key ? "Applying…" : "Apply"}
                  </Button>
                  {panel.applyErrors[row.key] ? (
                    <span className="text-sm text-destructive" role="alert">{panel.applyErrors[row.key]}</span>
                  ) : null}
                </li>
              ))}
            </ul>
          )}
          {panel.model && panel.model.errors.length > 0 && (
            <div className="mt-3">
              <h3 className="text-sm font-semibold">Provider errors last run</h3>
              <ul className="mt-2 grid gap-1">
                {panel.model.errors.map((message) => (
                  <li key={message} className="text-sm text-muted-foreground font-mono">{message}</li>
                ))}
              </ul>
            </div>
          )}
        </CardContent>
      </Card>
    </section>
  );
}
