import { Activity, ArrowRight, ClipboardList, FileCheck2, RadioTower, RefreshCcw, ShieldCheck, TrendingUp } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { GateRail, SeverityBadge } from "@/components/operations";
import { MetricCard, type MetricCardTone } from "@/components/data/concrete";
import { cn } from "@/lib/utils";
import {
  useOperatorReadinessConsoleViewModel,
  type ReadinessConsolePanel,
  type ReadinessConsoleLevel,
  type ReadinessConsoleNextAction,
  type ReadinessConsoleRecoveryState,
  type ReadinessConsoleRowAction,
  type ReadinessConsoleRow,
  type ReadinessConsoleSelectedEvidenceDetail,
  type ReadinessConsoleSelectedWorkItemDetail
} from "@/screens/operator-readiness-console.view-model";
import type {
  DataWorkspaceResponse,
  AccountingWorkspaceResponse,
  MetricSnapshot,
  ReportingWorkspaceResponse,
  StrategyWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

interface OperatorReadinessConsoleProps {
  strategy: StrategyWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  data: DataWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  reporting: ReportingWorkspaceResponse | null;
}

// Concrete severity vocabulary. The console's readiness levels collapse onto the design
// system's canonical operator severities: ready · review · action · blocked · info.
// `SeverityBadge`/`GateRail` normalize the string, and "neutral" resolves to the muted
// "info" tone. Passing the level as the status keeps one source of truth.
const levelStatus: Record<ReadinessConsoleLevel, string> = {
  ready: "ready",
  review: "review",
  blocked: "blocked",
  neutral: "neutral"
};

// Severity-tinted panel shell: an alpha-10 wash + solid semantic border, never a solid
// fill, matching the Concrete readiness surfaces.
const levelPanel: Record<ReadinessConsoleLevel, string> = {
  ready: "border-[var(--severity-ready-bd)] bg-[var(--severity-ready-bg)]",
  review: "border-[var(--severity-review-bd)] bg-[var(--severity-review-bg)]",
  blocked: "border-[var(--severity-blocked-bd)] bg-[var(--severity-blocked-bg)]",
  neutral: "border-[var(--severity-info-bd)] bg-[var(--severity-info-bg)]"
};

// `MetricSnapshot` tone → Concrete MetricCard left-accent tone.
const metricTone: Record<MetricSnapshot["tone"], MetricCardTone> = {
  default: "neutral",
  success: "success",
  warning: "warning",
  danger: "danger"
};

const panelIcons: Record<ReadinessConsolePanel["id"], typeof ShieldCheck> = {
  "latest-runs": TrendingUp,
  "active-paper-session": Activity,
  "provider-trust": RadioTower,
  "reconciliation-breaks": ClipboardList,
  "promotion-blockers": ShieldCheck,
  "reporting-report-packs": FileCheck2
};

const workItemColumns: DenseDataTableColumn<ReadinessConsoleRow>[] = [
  {
    id: "item",
    label: "Work item",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block break-words font-mono text-[11px] text-muted-foreground">{row.id}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <SeverityBadge status={levelStatus[row.level]} label={row.value} aria-label={row.statusAriaLabel} />
  },
  {
    id: "target",
    label: "Target",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-medium text-foreground">{row.action?.label ?? "Review item"}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
          {row.action?.route ?? row.meta}
        </span>
      </span>
    )
  }
];

const evidencePanelColumns: DenseDataTableColumn<ReadinessConsoleRow>[] = [
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block break-words font-mono text-[11px] text-muted-foreground">{row.id}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <SeverityBadge status={levelStatus[row.level]} label={row.value} aria-label={row.statusAriaLabel} />
  },
  {
    id: "detail",
    label: "Detail",
    render: (row) => <span className="block min-w-[12rem] text-xs leading-5 text-foreground/80">{row.detail}</span>
  },
  {
    id: "source",
    label: "Source",
    render: (row) => <span className="block min-w-[10rem] break-words font-mono text-[11px] text-muted-foreground">{row.meta}</span>
  }
];

export function OperatorReadinessConsole({
  strategy,
  trading,
  data,
  accounting,
  reporting
}: OperatorReadinessConsoleProps) {
  const vm = useOperatorReadinessConsoleViewModel({
    strategy,
    trading,
    data,
    accounting,
    reporting
  });

  return (
    <div className="space-y-6">
      <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>

      <section
        role="region"
        aria-label="Readiness control strip"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Trading readiness</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {vm.title}
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{vm.subtitle}</p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <ConsoleChip label="As of" value={vm.asOf} />
          <ConsoleChip label="Inbox" value={vm.inboxSummary} />
          <SeverityBadge status={levelStatus[vm.overallLevel]} label={vm.overallLabel} />
        </div>
      </section>

      <section
        className="panel-surface px-4 py-4"
        role="group"
        aria-label="Readiness gate pipeline"
      >
        <div className="eyebrow-label mb-3">Readiness pipeline</div>
        <GateRail
          gates={vm.checkpointGates.map((gate) => ({
            key: gate.id,
            label: gate.label,
            status: levelStatus[gate.level],
            statusLabel: gate.value
          }))}
        />
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card className={cn("panel-surface-strong border", levelPanel[vm.overallLevel])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">Trading Readiness</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <ShieldCheck className="h-5 w-5 text-primary" />
                  {vm.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.subtitle}</CardDescription>
              </div>
              <SeverityBadge status={levelStatus[vm.overallLevel]} label={vm.overallLabel} />
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-sm leading-6 text-foreground/85">{vm.overallDetail}</p>
            <div className="flex flex-wrap items-center gap-2">
              <ConsoleChip label="Snapshot" value={vm.asOf} />
              <ConsoleChip label="Operator inbox" value={vm.inboxSummary} />
            </div>
            <PrimaryNextAction action={vm.nextAction} />
            {vm.inboxLoadingLabel ? (
              <p role="status" className="text-sm text-muted-foreground">{vm.inboxLoadingLabel}</p>
            ) : null}
            {vm.inboxErrorRecovery ? (
              <InboxErrorRecovery
                recovery={vm.inboxErrorRecovery}
                onRetry={vm.refreshInbox}
                disabled={vm.inboxRefreshDisabled}
                disabledReason={vm.inboxRefreshDisabledReason}
                busy={vm.inboxRefreshBusy}
              />
            ) : null}
            <div className="flex flex-wrap gap-2">
              <Button asChild variant="secondary" size="sm">
                <Link to="/trading">Trading cockpit</Link>
              </Button>
              <Button asChild variant="outline" size="sm">
                <Link to="/accounting/reconciliation">Break queue</Link>
              </Button>
              <Button asChild variant="outline" size="sm">
                <Link to="/reporting/report-packs">Report packs</Link>
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => void vm.refreshInbox()}
                disabled={vm.inboxRefreshDisabled}
                disabledReason={vm.inboxRefreshDisabledReason}
                busy={vm.inboxRefreshBusy}
                busyLabel={vm.inboxRefreshLabel}
                aria-label={vm.inboxRefreshAriaLabel}
              >
                <RefreshCcw className="h-4 w-4" aria-hidden="true" />
                {vm.inboxRefreshLabel}
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card aria-labelledby="api-contract-coverage-title" className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">API Contract Coverage</div>
                <CardTitle id="api-contract-coverage-title">Shared sources</CardTitle>
                <CardDescription>Local API payload health for readiness review.</CardDescription>
              </div>
              <ConsoleChip label="Sources" value={String(vm.apiSources.length)} />
            </div>
          </CardHeader>
          <CardContent className="space-y-2" role="list" aria-label={vm.apiSourcesLabel}>
            {vm.apiSources.map((source) => (
              <div
                key={source.id}
                role="listitem"
                aria-label={source.ariaLabel}
                className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2"
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="text-sm font-semibold">{source.label}</span>
                  <SeverityBadge status={levelStatus[source.level]} label={source.status} aria-label={source.statusAriaLabel} />
                </div>
                <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{source.endpoint}</p>
              </div>
            ))}
          </CardContent>
        </Card>
      </section>

      <section
        role="list"
        aria-label={vm.checkpointGatesLabel}
        className="grid gap-3 md:grid-cols-2 xl:grid-cols-4"
      >
        {vm.checkpointGates.map((gate) => (
          <div key={gate.id} role="listitem">
            <ReadinessRow row={gate} />
          </div>
        ))}
      </section>

      <section className="grid gap-3 md:grid-cols-2 xl:grid-cols-6" aria-label={vm.metricsLabel}>
        {vm.metrics.map((metric) => (
          <div
            key={metric.id}
            role="group"
            aria-label={metric.ariaLabel}
            aria-describedby={metric.detailId}
            className="grid gap-2"
          >
            <MetricCard
              label={metric.label}
              value={metric.value}
              delta={metric.delta}
              tone={metricTone[metric.tone]}
            />
            <p
              id={metric.detailId}
              className={cn("rounded-md border px-2.5 py-2 text-xs leading-5 text-foreground/75", levelPanel[metric.level])}
            >
              {metric.detail}
            </p>
          </div>
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        {vm.panels.map((panel) => (
          <ConsolePanel key={panel.id} panel={panel} />
        ))}
      </section>

      <Card role="region" aria-label={vm.workItemsRegionLabel} className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div className="eyebrow-label">Operator Inbox</div>
              <CardTitle>Review work items</CardTitle>
              <CardDescription>{vm.workItemsSummary}</CardDescription>
            </div>
            <ConsoleChip label="Visible" value={String(vm.workItems.length)} />
          </div>
        </CardHeader>
        <CardContent>
          {vm.workItemsOverflowText ? (
            <p role="status" className="mb-3 rounded-lg border border-border/70 bg-secondary/25 px-3 py-2 text-sm text-muted-foreground">
              {vm.workItemsOverflowText}
            </p>
          ) : null}
          {vm.workItems.length > 0 ? (
            <div className="grid gap-4 xl:grid-cols-[minmax(0,1.25fr)_minmax(320px,0.75fr)]">
              <DenseDataTable
                columns={workItemColumns}
                rows={vm.workItems}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.ariaLabel}
                getRowSelectAriaLabel={(row) => `Select operator work item ${row.label}`}
                getRowAriaControls={() => vm.workItemsDetailPanelId}
                getRowAriaExpanded={(row) => row.id === vm.selectedWorkItemId}
                onRowSelect={(row) => vm.selectWorkItem(row.id)}
                selectedRowId={vm.selectedWorkItemId}
                emptyText={vm.workItemsSummary}
                ariaLabel={vm.workItemsTableLabel}
                caption={vm.workItemsListLabel}
              />
              <SelectedWorkItemDetail
                detail={vm.selectedWorkItemDetail}
                id={vm.workItemsDetailPanelId}
                ariaLabel={vm.workItemsDetailLabel}
              />
            </div>
          ) : (
            <EmptyConsoleState text={vm.workItemsEmptyText} action={vm.workItemsEmptyAction} />
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function SelectedWorkItemDetail({
  detail,
  id,
  ariaLabel
}: {
  detail: ReadinessConsoleSelectedWorkItemDetail | null;
  id: string;
  ariaLabel: string;
}) {
  if (!detail) {
    return <EmptyConsoleState text="Select a work item to inspect its routing and evidence." />;
  }

  return (
    <aside
      id={id}
      className="row-detail-panel h-fit min-w-0"
      role="region"
      aria-label={ariaLabel}
      aria-live="polite"
    >
      <div className="head flex items-center justify-between gap-3">
        <span>Selected work item</span>
        <SeverityBadge status={levelStatus[detail.level]} label={detail.statusLabel} aria-label={detail.statusAriaLabel} />
      </div>
      <div className="body">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold text-foreground">{detail.title}</h3>
          <p className="mt-2 text-xs leading-5 text-foreground/80">{detail.detail}</p>
          <p className="mt-2 break-words font-mono text-[11px] text-muted-foreground">{detail.meta}</p>
        </div>
        <dl className="mt-3 grid gap-2">
          {detail.fields.map((field) => (
            <div
              key={field.label}
              className="grid grid-cols-[minmax(0,0.42fr)_minmax(0,0.58fr)] gap-3 rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
            >
              <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
              <dd className="break-words text-right font-mono text-xs text-foreground">{field.value}</dd>
            </div>
          ))}
        </dl>
        {detail.action ? (
          <div className="mt-3">
            <Button asChild variant={detail.action.variant} size="sm">
              <Link to={detail.action.route} aria-label={detail.action.ariaLabel}>
                {detail.action.label}
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Link>
            </Button>
          </div>
        ) : null}
      </div>
    </aside>
  );
}

function PrimaryNextAction({ action }: { action: ReadinessConsoleNextAction }) {
  const actionButton = action.route ? (
    <Button
      asChild
      variant={action.level === "blocked" ? "secondary" : "outline"}
      size="sm"
      className="shrink-0"
    >
      <Link to={action.route} aria-label={action.actionAriaLabel}>
        {action.label}
        <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
      </Link>
    </Button>
  ) : (
    <Button
      type="button"
      variant="outline"
      size="sm"
      className="shrink-0"
      disabled
      disabledReason={action.disabledReason}
      aria-label={action.actionAriaLabel}
    >
      {action.label}
    </Button>
  );

  return (
    <div
      role="group"
      aria-label={action.ariaLabel}
      className={cn("rounded-lg border px-3 py-3", levelPanel[action.level])}
    >
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="eyebrow-label">Primary next action</div>
          <div className="mt-1 text-sm font-semibold text-foreground">{action.title}</div>
          <p className="mt-1 text-xs leading-5 text-foreground/80">{action.detail}</p>
          <p className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{action.meta}</p>
        </div>
        {actionButton}
      </div>
    </div>
  );
}

function ConsolePanel({ panel }: { panel: ReadinessConsolePanel }) {
  const Icon = panelIcons[panel.id];
  const titleId = `${panel.id}-title`;

  return (
    <Card role="region" aria-label={panel.ariaLabel} className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <CardTitle id={titleId} className="flex items-center gap-2 text-base">
            <Icon className="h-4 w-4 text-primary" />
            {panel.title}
          </CardTitle>
          <ConsoleChip label="Rows" value={String(panel.rows.length)} />
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {panel.rows.length > 0 ? (
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.2fr)_minmax(300px,0.8fr)]">
            <DenseDataTable
              columns={evidencePanelColumns}
              rows={panel.rows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => `Select ${panel.title.toLowerCase()} evidence ${row.label}`}
              getRowAriaControls={() => panel.detailPanelId}
              getRowAriaExpanded={(row) => row.id === panel.selectedRowId}
              onRowSelect={(row) => panel.selectRow(row.id)}
              selectedRowId={panel.selectedRowId}
              emptyText={panel.emptyText}
              ariaLabel={panel.tableLabel}
              caption={panel.listLabel}
            />
            <SelectedEvidenceDetail
              detail={panel.selectedDetail}
              id={panel.detailPanelId}
              ariaLabel={panel.detailLabel}
            />
          </div>
        ) : (
          <EmptyConsoleState text={panel.emptyText} />
        )}
      </CardContent>
    </Card>
  );
}

function SelectedEvidenceDetail({
  detail,
  id,
  ariaLabel
}: {
  detail: ReadinessConsoleSelectedEvidenceDetail | null;
  id: string;
  ariaLabel: string;
}) {
  if (!detail) {
    return <EmptyConsoleState text="Select an evidence row to inspect its source and routing." />;
  }

  return (
    <aside
      id={id}
      className="row-detail-panel h-fit min-w-0"
      role="region"
      aria-label={ariaLabel}
      aria-live="polite"
    >
      <div className="head flex items-center justify-between gap-3">
        <span>Selected evidence</span>
        <SeverityBadge status={levelStatus[detail.level]} label={detail.statusLabel} aria-label={detail.statusAriaLabel} />
      </div>
      <div className="body">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold text-foreground">{detail.title}</h3>
          <p className="mt-2 text-xs leading-5 text-foreground/80">{detail.detail}</p>
          <p className="mt-2 break-words font-mono text-[11px] text-muted-foreground">{detail.meta}</p>
        </div>
        <dl className="mt-3 grid gap-2">
          {detail.fields.map((field) => (
            <div
              key={field.label}
              className="grid grid-cols-[minmax(0,0.42fr)_minmax(0,0.58fr)] gap-3 rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
            >
              <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
              <dd className="break-words text-right font-mono text-xs text-foreground">{field.value}</dd>
            </div>
          ))}
        </dl>
        {detail.action ? (
          <div className="mt-3">
            <Button asChild variant={detail.action.variant} size="sm">
              <Link to={detail.action.route} aria-label={detail.action.ariaLabel}>
                {detail.action.label}
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Link>
            </Button>
          </div>
        ) : null}
      </div>
    </aside>
  );
}

function ReadinessRow({ row }: { row: ReadinessConsoleRow }) {
  return (
    <div
      role="group"
      aria-label={row.ariaLabel}
      aria-describedby={row.detailId}
      className={cn(
        "rounded-[var(--radius-card)] border border-l-[3px] px-3 py-3",
        levelPanel[row.level]
      )}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-sm font-semibold text-foreground">{row.label}</div>
          <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{row.meta}</div>
        </div>
        <SeverityBadge status={levelStatus[row.level]} label={row.value} aria-label={row.statusAriaLabel} />
      </div>
      <p id={row.detailId} className="mt-2 text-xs leading-5 text-foreground/80">{row.detail}</p>
      {row.action ? (
        <div className="mt-3">
          <Button asChild variant={row.action.variant} size="sm">
            <Link to={row.action.route} aria-label={row.action.ariaLabel}>
              {row.action.label}
            </Link>
          </Button>
        </div>
      ) : null}
    </div>
  );
}

function InboxErrorRecovery({
  recovery,
  onRetry,
  disabled,
  disabledReason,
  busy
}: {
  recovery: ReadinessConsoleRecoveryState;
  onRetry: () => Promise<void>;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}) {
  return (
    <div role="alert" className="rounded-lg border border-warning/35 bg-warning/10 px-3 py-2 text-sm text-warning">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <div className="font-semibold">{recovery.title}</div>
          <p className="mt-1 leading-5">{recovery.detail}</p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => void onRetry()}
          disabled={disabled}
          disabledReason={disabledReason}
          busy={busy}
          busyLabel={recovery.actionLabel}
          aria-label={recovery.actionAriaLabel}
          className="shrink-0"
        >
          <RefreshCcw className="h-4 w-4" aria-hidden="true" />
          {recovery.actionLabel}
        </Button>
      </div>
    </div>
  );
}

function EmptyConsoleState({ text, action }: { text: string; action?: ReadinessConsoleRowAction }) {
  return (
    <div role="status" className="rounded-lg border border-dashed border-border/80 bg-secondary/20 px-3 py-4 text-sm text-muted-foreground">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <span>{text}</span>
        {action ? (
          <Button asChild variant={action.variant} size="sm" className="shrink-0">
            <Link to={action.route} aria-label={action.ariaLabel}>
              {action.label}
              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
            </Link>
          </Button>
        ) : null}
      </div>
    </div>
  );
}

function ConsoleChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip">
      <span className="text-muted-foreground">{label}</span>
      <span className="min-w-0 break-words font-mono text-foreground">{value}</span>
    </span>
  );
}
