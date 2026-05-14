import { AlertCircle, BookCheck, CheckCircle2, Landmark, Network, RefreshCcw, Search, ShieldCheck, Table2, TrendingUp, WalletCards } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { LotsTrackerPanel, SecurityDetailsPanel } from "@/components/meridian/security-details-tracker";
import { cn } from "@/lib/utils";
import { workspaceForPath } from "@/lib/workspace";
import {
  buildGovernanceLoadingViewState,
  resolveGovernanceWorkstream,
  SECURITY_IDENTITY_DETAIL_PANEL_ID,
  useGovernanceCashFlowViewModel,
  useGovernanceReconciliationViewModel,
  useGovernanceReportingViewModel,
  useReconciliationResolveDialogViewModel,
  useSecurityMasterViewModel
} from "@/screens/governance-screen.view-model";
import type {
  CalibrationProfileRowViewModel,
  CalibrationSummaryViewModel,
  CorporateActionsViewState,
  CorporateActionRowViewModel,
  ReconciliationQueuePanelViewState,
  ReconciliationQueueRunRowViewModel,
  ReconciliationQueueRunTone,
  GovernanceTrialBalanceRowViewModel,
  SecuritySchedulesViewState,
  SecurityScheduleRowViewModel,
  SecuritySearchResultRowViewModel,
  TradingParametersViewState
} from "@/screens/governance-screen.view-model";
import type { GovernanceWorkspaceResponse } from "@/types";

interface GovernanceScreenProps {
  data: GovernanceWorkspaceResponse | null;
}

const calibrationProfileColumns: DenseDataTableColumn<CalibrationProfileRowViewModel>[] = [
  {
    id: "profile",
    label: "Profile",
    render: (row) => <span className="font-mono text-foreground">{row.toleranceProfileId}</span>
  },
  {
    id: "route",
    label: "Route",
    render: (row) => <span className="text-muted-foreground">{row.exceptionRoute}</span>
  },
  {
    id: "severity",
    label: "Severity",
    render: (row) => <span className={cn("font-mono", calibrationSeverityClass(row.highestSeverity))}>{row.highestSeverity}</span>
  },
  {
    id: "open",
    label: "Open",
    align: "right",
    render: (row) => <span className={cn("font-mono tabular-nums", row.openBreakCount > 0 ? "text-warning" : "text-foreground")}>{row.openBreakCount}</span>
  },
  {
    id: "in-review",
    label: "Review",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-foreground">{row.inReviewBreakCount}</span>
  },
  {
    id: "pending-signoff",
    label: "Sign-off",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.pendingSignoffCount > 0 ? "text-warning" : "text-foreground")}>
        {row.pendingSignoffCount}
      </span>
    )
  },
  {
    id: "tolerance",
    label: "Tolerance",
    align: "right",
    render: (row) => <span className="font-mono text-muted-foreground">{row.maxToleranceBandLabel}</span>
  },
  {
    id: "updated",
    label: "Updated",
    render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedLabel}</span>
  }
];

const reconciliationQueueToneClass: Record<ReconciliationQueueRunTone, string> = {
  muted: "text-muted-foreground",
  warning: "text-warning",
  success: "text-success",
  primary: "text-primary"
};

function calibrationSeverityClass(severity: string): string {
  const normalized = severity.trim().toLowerCase();
  if (normalized === "critical") {
    return "text-danger";
  }

  if (normalized === "warning" || normalized === "warn") {
    return "text-warning";
  }

  return "text-foreground";
}

const reconciliationQueueColumns: DenseDataTableColumn<ReconciliationQueueRunRowViewModel>[] = [
  {
    id: "run",
    label: "Run",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.strategyName}</span>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.runId}</span>
      </span>
    )
  },
  { id: "mode", label: "Mode", render: (row) => <span className="font-mono uppercase text-muted-foreground">{row.modeLabel}</span> },
  { id: "status", label: "Status", render: (row) => row.runStatusLabel },
  { id: "breaks", label: "Breaks", align: "right", render: (row) => <span className="font-mono">{row.breakCountLabel}</span> },
  { id: "open", label: "Open", align: "right", render: (row) => <span className="font-mono">{row.openBreakLabel}</span> },
  {
    id: "reconciliation",
    label: "Reconciliation",
    render: (row) => (
      <span className={cn("font-mono text-xs uppercase tracking-[0.14em]", reconciliationQueueToneClass[row.reconciliationTone])}>
        {row.reconciliationStatusLabel}
      </span>
    )
  },
  { id: "updated", label: "Updated", render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedLabel}</span> }
];

const trialBalanceColumns: DenseDataTableColumn<GovernanceTrialBalanceRowViewModel>[] = [
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.accountLabel}</span>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.financialAccountId ?? "Unassigned"}</span>
      </span>
    )
  },
  { id: "type", label: "Type", render: (row) => <span className="font-mono text-muted-foreground">{row.accountTypeLabel}</span> },
  { id: "basis", label: "Basis", render: (row) => <Badge variant={row.basisTone}>{row.basisLabel}</Badge> },
  {
    id: "balance",
    label: "Balance",
    align: "right",
    render: (row) => (
      <span
        className={cn(
          "font-mono tabular-nums",
          row.balanceTone === "success" ? "text-success" : row.balanceTone === "danger" ? "text-danger" : "text-foreground"
        )}
      >
        {row.balanceLabel}
      </span>
    )
  },
  { id: "entries", label: "Entries", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.entryCountLabel}</span> }
];

const corporateActionColumns: DenseDataTableColumn<CorporateActionRowViewModel>[] = [
  {
    id: "eventType",
    label: "Event type",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.eventTypeLabel}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.corpActId}</span>
      </span>
    )
  },
  { id: "exDate", label: "Ex-date", render: (row) => <span className="font-mono text-muted-foreground">{row.exDateLabel}</span> },
  { id: "payDate", label: "Pay date", render: (row) => <span className="font-mono text-muted-foreground">{row.payDateLabel}</span> },
  { id: "amount", label: "Amount", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.amountLabel}</span> }
];

const securityScheduleColumns: DenseDataTableColumn<SecurityScheduleRowViewModel>[] = [
  {
    id: "eventType",
    label: "Event",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.eventTypeLabel}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.eventId}</span>
      </span>
    )
  },
  { id: "paymentDate", label: "Payment date", render: (row) => <span className="font-mono text-muted-foreground">{row.paymentDateLabel}</span> },
  { id: "expected", label: "Expected", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.expectedAmountLabel}</span> },
  {
    id: "actual",
    label: "Actual",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.actualAmount === null ? "text-muted-foreground" : "text-foreground")}>
        {row.actualAmountLabel}
      </span>
    )
  },
  {
    id: "variance",
    label: "Variance",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.postingStatus === "Variance" ? "text-danger" : "text-muted-foreground")}>
        {row.varianceLabel}
      </span>
    )
  },
  { id: "factor", label: "Factor", align: "right", render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.factorLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.postingStatusTone}>{row.postingStatusLabel}</Badge> }
];

const focusCopy: Record<string, { title: string; description: string }> = {
  ledger: {
    title: "Ledger overview",
    description: "Cash, ledger coverage, and audit-facing balances remain visible from the workstation shell."
  },
  reconciliation: {
    title: "Reconciliation queue",
    description: "Open breaks, timing drift, and balanced runs stay visible without leaving Accounting."
  },
  "security-master": {
    title: "Security coverage",
    description: "Coverage gaps and reference integrity stay tied to reconciliation and reporting readiness."
  },
  reporting: {
    title: "Reporting profiles",
    description: "Report packs, governed exports, and loader artifacts stay tied to accounting evidence."
  }
};

export function GovernanceScreen({ data }: GovernanceScreenProps) {
  const { pathname } = useLocation();
  const workstream = resolveGovernanceWorkstream(pathname);
  const workspace = workspaceForPath(pathname);
  const reconciliation = useGovernanceReconciliationViewModel(data, workstream);
  const resolveDialog = useReconciliationResolveDialogViewModel(reconciliation.resolveBreak);
  const selectedReconciliation = reconciliation.selectedReconciliation;
  const selectedReconciliationDetail = reconciliation.detailView;
  const cashFlow = useGovernanceCashFlowViewModel(data?.cashFlow ?? null, pathname, workstream);
  const reporting = useGovernanceReportingViewModel(data?.reporting ?? null);
  const securityMaster = useSecurityMasterViewModel(workstream === "security-master");
  const identity = securityMaster.identityView;
  const selectedSecurityEntry = securityMaster.selectedSecurityId
    ? securityMaster.results?.find((entry) => entry.securityId === securityMaster.selectedSecurityId) ?? null
    : null;
  const identifierColumns: DenseDataTableColumn<NonNullable<typeof identity>["identifiers"][number]>[] = [
    { id: "kind", label: "Kind", render: (identifier) => <span className="font-mono">{identifier.kind}</span> },
    { id: "value", label: "Value", render: (identifier) => <span className="font-mono text-foreground">{identifier.value}</span> },
    { id: "provider", label: "Provider", render: (identifier) => identifier.providerLabel },
    { id: "state", label: "State", render: (identifier) => <Badge variant={identifier.primaryBadgeVariant}>{identifier.primaryLabel}</Badge> },
    { id: "range", label: "Valid range", render: (identifier) => <span className="font-mono text-muted-foreground">{identifier.validRangeLabel}</span> }
  ];
  const aliasColumns: DenseDataTableColumn<NonNullable<typeof identity>["aliases"][number]>[] = [
    { id: "kind", label: "Kind", render: (alias) => <span className="font-mono">{alias.aliasKind}</span> },
    {
      id: "alias",
      label: "Alias",
      render: (alias) => (
        <div>
          <div className="font-mono text-foreground">{alias.aliasValue}</div>
          <div className="mt-1 text-xs text-muted-foreground">{alias.reasonText}</div>
        </div>
      )
    },
    { id: "provider", label: "Provider", render: (alias) => alias.providerLabel },
    { id: "scope", label: "Scope", render: (alias) => alias.scope },
    { id: "state", label: "State", render: (alias) => <Badge variant={alias.enabledBadgeVariant}>{alias.enabledLabel}</Badge> },
    { id: "range", label: "Valid range", render: (alias) => <span className="font-mono text-muted-foreground">{alias.validRangeLabel}</span> }
  ];
  const securityResultColumns: DenseDataTableColumn<SecuritySearchResultRowViewModel>[] = [
    {
      id: "name",
      label: "Name",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-semibold text-foreground">{row.displayName}</span>
          <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.securityId}</span>
        </span>
      )
    },
    { id: "assetClass", label: "Asset Class", render: (row) => row.classification.assetClass },
    { id: "primaryId", label: "Primary ID", render: (row) => <span className="font-mono text-muted-foreground">{row.primaryIdentifierLabel}</span> },
    { id: "currency", label: "Currency", render: (row) => <span className="font-mono text-muted-foreground">{row.economicDefinition.currency}</span> },
    { id: "status", label: "Status", render: (row) => <Badge variant={row.statusTone === "success" ? "success" : "warning"}>{row.status}</Badge> }
  ];

  if (!data) {
    const loading = buildGovernanceLoadingViewState(pathname);
    return (
      <Card
        role={loading.role}
        aria-busy={loading.ariaBusy}
        aria-live={loading.ariaLive}
        aria-labelledby={loading.titleId}
        aria-describedby={loading.detailId}
      >
        <CardHeader>
          <CardTitle id={loading.titleId}>{loading.title}</CardTitle>
          <CardDescription id={loading.detailId}>{loading.detail}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const focus = focusCopy[workstream];

  return (
    <div className="space-y-8">
      <section
        role="region"
        aria-label={`${workspace.label} workbench context`}
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">{workspace.label} lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {focus.title}
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{focus.description}</p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <GovernanceChip label="Workstream" value={workstream} />
          <GovernanceChip label="Queue" value={String(data.reconciliationQueue.length)} />
          <GovernanceChip label="Breaks" value={String(data.breakQueue.length)} />
          <GovernanceChip label="Profiles" value={String(data.reporting.profileCount)} />
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">{workspace.label} Lane</div>
            <CardTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-primary" />
              {focus.title}
            </CardTitle>
            <CardDescription>{focus.description}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <GovernanceHighlight
              icon={BookCheck}
              title="Audit posture"
              description="Reconciliation health and audit readiness stay visible for every run on the queue."
            />
            <GovernanceHighlight
              icon={WalletCards}
              title="Cash flow"
              description="Portfolio cash and ledger cash stay paired so variance review is immediate."
            />
            <GovernanceHighlight
              icon={Landmark}
              title="Reporting"
              description="Export profiles stay close to accounting and reporting workflows instead of living in a separate tool."
            />
          </CardContent>
        </Card>

        <Card className="panel-surface-strong bg-panel-strong text-foreground" role="region" aria-label={cashFlow.ariaLabel}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="eyebrow-label">{cashFlow.eyebrow}</div>
                <CardTitle>{cashFlow.title}</CardTitle>
                <CardDescription className="mt-2 text-muted-foreground">
                  {cashFlow.description}
                </CardDescription>
              </div>
              <span
                aria-label={cashFlow.statusAriaLabel}
                className={cn(
                  "w-fit rounded-sm border px-2.5 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em]",
                  cashFlowBadgeClass(cashFlow.statusTone)
                )}
              >
                {cashFlow.statusLabel}
              </span>
            </div>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <span className="sr-only" aria-live="polite">{cashFlow.statusAnnouncement}</span>
            <div aria-label={cashFlow.rowGroupLabel} className="grid gap-3 sm:grid-cols-2">
              {cashFlow.rows.map((row) => (
                <GovernanceValue
                  key={row.id}
                  label={row.label}
                  value={row.value}
                  tone={cashFlowTextClass(row.tone)}
                  ariaLabel={row.ariaLabel}
                />
              ))}
            </div>
          </CardContent>
        </Card>
      </section>

      {workstream === "reconciliation" ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <BookCheck className="h-4 w-4 text-primary" />
                {reconciliation.queuePanelView.title}
              </CardTitle>
              <CardDescription>{reconciliation.queuePanelView.description}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {reconciliation.queuePanelView.hasRows ? (
                <DenseDataTable
                  columns={reconciliationQueueColumns}
                  rows={reconciliation.queuePanelView.rows}
                  getRowId={(row) => row.runId}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.controlsId}
                  getRowAriaExpanded={(row) => row.isExpanded}
                  selectedRowId={selectedReconciliation?.runId ?? null}
                  onRowSelect={(row) => reconciliation.selectRun(row.runId)}
                  emptyText={reconciliation.queuePanelView.emptyText}
                  ariaLabel={reconciliation.queuePanelView.listLabel}
                  caption={reconciliation.queuePanelView.description}
                />
              ) : (
                <div
                  role="status"
                  className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
                >
                  {reconciliation.queuePanelView.emptyText}
                </div>
              )}
            </CardContent>
          </Card>

          <Card
            id={reconciliation.queuePanelView.detailPanelId}
            className="panel-surface-strong bg-panel-strong text-foreground"
            role="region"
            aria-live="polite"
            aria-label={selectedReconciliationDetail?.ariaLabel ?? reconciliation.queuePanelView.detailEmptyAriaLabel}
          >
            <CardHeader>
              <div className="eyebrow-label">{selectedReconciliationDetail?.eyebrow ?? "Reconciliation detail"}</div>
              <CardTitle>{selectedReconciliationDetail?.title ?? reconciliation.queuePanelView.detailEmptyTitle}</CardTitle>
              <CardDescription className="text-muted-foreground">
                {selectedReconciliationDetail?.description ?? reconciliation.queuePanelView.detailEmptyText}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              {selectedReconciliationDetail ? (
                <>
                  {selectedReconciliationDetail.fields.map((field) => (
                    <GovernanceValue
                      key={field.label}
                      label={field.label}
                      value={field.value}
                      tone={cashFlowTextClass(field.tone)}
                      ariaLabel={field.ariaLabel}
                    />
                  ))}
                  <div
                    aria-label={selectedReconciliationDetail.narrativeLabel}
                    className="rounded-lg border border-border/70 bg-background/70 p-4 text-muted-foreground"
                  >
                    {selectedReconciliationDetail.narrative}
                  </div>
                  {reconciliation.detailActions ? (
                    <div className="flex flex-wrap gap-3">
                      <Button asChild variant="secondary">
                        <Link
                          to={reconciliation.detailActions.evidencePacketHref}
                          aria-label={reconciliation.detailActions.evidencePacketAriaLabel}
                        >
                          <Network className="h-4 w-4" />
                          {reconciliation.detailActions.evidencePacketLabel}
                        </Link>
                      </Button>
                      <Button asChild variant="secondary">
                        <a
                          href={reconciliation.detailActions.breakChecklistHref}
                          aria-label={reconciliation.detailActions.breakChecklistAriaLabel}
                        >
                          {reconciliation.detailActions.breakChecklistLabel}
                        </a>
                      </Button>
                      <Button asChild variant="outline" className="border-border/70 bg-transparent text-foreground hover:bg-secondary/60">
                        <a
                          href={reconciliation.detailActions.auditPacketHref}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={reconciliation.detailActions.auditPacketAriaLabel}
                        >
                          {reconciliation.detailActions.auditPacketLabel}
                        </a>
                      </Button>
                    </div>
                  ) : null}
                </>
              ) : (
                <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                  {reconciliation.queuePanelView.detailEmptyText}
                </div>
              )}
            </CardContent>
          </Card>
        </section>
      ) : null}

      {workstream === "ledger" && selectedReconciliation ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card aria-labelledby="trial-balance-title" aria-describedby="trial-balance-description" className="panel-surface">
            <CardHeader>
              <CardTitle id="trial-balance-title">{reconciliation.trialBalanceView.title}</CardTitle>
              <CardDescription id="trial-balance-description">{reconciliation.trialBalanceView.description}</CardDescription>
            </CardHeader>
            <CardContent>
              <span className="sr-only" aria-live="polite">{reconciliation.trialBalanceView.statusAnnouncement}</span>
              <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Accounting basis">
                {reconciliation.trialBalanceView.basisOptions.map((option) => (
                  <Button
                    key={option.id}
                    type="button"
                    size="sm"
                    variant={option.isSelected ? "default" : "outline"}
                    aria-pressed={option.isSelected}
                    aria-label={`${option.label} basis, ${option.rowCountLabel}. ${option.description}`}
                    onClick={() => reconciliation.selectAccountingBasis(option.id)}
                  >
                    <span>{option.label}</span>
                    <span className="ml-2 font-mono text-[10px] opacity-75">{option.rowCount}</span>
                  </Button>
                ))}
              </div>
              {reconciliation.trialBalanceView.hasRows ? (
                <div className="grid gap-3 xl:grid-cols-[minmax(0,1.25fr)_minmax(260px,0.75fr)]">
                  <DenseDataTable
                    columns={trialBalanceColumns}
                    rows={reconciliation.trialBalanceView.rows}
                    getRowId={(line) => line.rowId}
                    getRowAriaLabel={(line) => line.ariaLabel}
                    getRowSelectAriaLabel={(line) => line.selectAriaLabel}
                    getRowAriaControls={(line) => line.detailPanelId}
                    getRowAriaExpanded={(line) => line.isExpanded}
                    selectedRowId={reconciliation.trialBalanceView.selectedRowId}
                    onRowSelect={(line) => reconciliation.selectTrialBalanceRow(line.rowId)}
                    emptyText={reconciliation.trialBalanceView.emptyDetail}
                    ariaLabel={reconciliation.trialBalanceView.tableLabel}
                  />
                  {reconciliation.trialBalanceView.selectedDetail ? (
                    <div id={reconciliation.trialBalanceView.detailPanelId} className="min-w-0">
                      <EntitySummary
                        eyebrow={reconciliation.trialBalanceView.selectedDetail.eyebrow}
                        title={reconciliation.trialBalanceView.selectedDetail.title}
                        subtitle={reconciliation.trialBalanceView.selectedDetail.subtitle}
                        description={reconciliation.trialBalanceView.selectedDetail.description}
                        status={<Badge variant={reconciliation.trialBalanceView.selectedDetail.statusVariant} dot>{reconciliation.trialBalanceView.selectedDetail.statusLabel}</Badge>}
                        fields={reconciliation.trialBalanceView.selectedDetail.fields}
                        ariaLabel={reconciliation.trialBalanceView.selectedDetail.ariaLabel}
                      />
                    </div>
                  ) : (
                    <aside
                      id={reconciliation.trialBalanceView.detailPanelId}
                      role="region"
                      aria-label={reconciliation.trialBalanceView.detailEmptyAriaLabel}
                      className="row-detail-panel h-fit min-w-0"
                    >
                      <div className="eyebrow-label">Trial-balance detail</div>
                      <h3 className="mt-1 text-sm font-semibold text-foreground">{reconciliation.trialBalanceView.detailEmptyTitle}</h3>
                      <p className="mt-2 text-sm leading-6 text-muted-foreground">{reconciliation.trialBalanceView.detailEmptyText}</p>
                    </aside>
                  )}
                </div>
              ) : (
                <div
                  role={reconciliation.trialBalanceView.state === "error" ? "alert" : "status"}
                  className={cn(
                    "rounded-lg border px-4 py-4",
                    reconciliation.trialBalanceView.state === "error"
                      ? "border-danger/35 bg-danger/10 text-danger"
                      : "border-border/70 bg-secondary/25 text-muted-foreground"
                  )}
                >
                  <div className="text-sm font-semibold text-foreground">{reconciliation.trialBalanceView.emptyTitle}</div>
                  <p className="mt-2 text-sm leading-6">
                    {reconciliation.trialBalanceView.errorText ?? reconciliation.trialBalanceView.loadingText ?? reconciliation.trialBalanceView.emptyDetail}
                  </p>
                </div>
              )}
              {reconciliation.trialBalanceView.loadingText && reconciliation.trialBalanceView.hasRows ? (
                <p role="status" className="mt-3 text-sm text-muted-foreground">
                  {reconciliation.trialBalanceView.loadingText}
                </p>
              ) : null}
              {reconciliation.trialBalanceView.errorText && reconciliation.trialBalanceView.hasRows ? (
                <div role="alert" className="mt-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {reconciliation.trialBalanceView.errorText}
                </div>
              ) : null}
            </CardContent>
          </Card>
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>{reconciliation.trialBalanceView.basisBridge.title}</CardTitle>
              <CardDescription>{reconciliation.trialBalanceView.basisBridge.description}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div role="region" aria-label={reconciliation.trialBalanceView.basisBridge.tableLabel}>
                {reconciliation.trialBalanceView.basisBridge.hasRows ? (
                  <div className="space-y-2">
                    {reconciliation.trialBalanceView.basisBridge.rows.map((row) => (
                      <div key={row.rowId} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2" aria-label={row.ariaLabel}>
                        <div className="flex items-start justify-between gap-3">
                          <span className="min-w-0">
                            <span className="block truncate text-sm font-semibold text-foreground">{row.accountLabel}</span>
                            <span className="mt-1 block text-xs text-muted-foreground">{row.sourceLabel}</span>
                          </span>
                          <Badge variant={row.varianceTone}>{row.varianceLabel}</Badge>
                        </div>
                        <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-muted-foreground">
                          <span className="font-mono">Primary {row.primaryBalanceLabel}</span>
                          <span className="font-mono">{row.comparisonBalanceLabel}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm leading-6 text-muted-foreground">
                    {reconciliation.trialBalanceView.basisBridge.emptyText}
                  </p>
                )}
              </div>
              <div className="border-t border-border/70 pt-4">
                <h3 className="text-sm font-semibold text-foreground">Reporting exports</h3>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">Entry points for report/export handoff using existing export infrastructure.</p>
              </div>
              <Button asChild>
                <a href={reporting.backendLinks[0].href} target="_blank" rel="noreferrer" aria-label={reporting.backendLinks[0].ariaLabel}>
                  {reporting.backendLinks[0].label}
                </a>
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={!reporting.exportCanRun}
                disabledReason={reporting.exportDisabledReason}
                busy={reporting.exportBusy}
                busyLabel={reporting.exportButtonLabel}
                aria-label={reporting.exportAriaLabel}
                onClick={() => void reporting.runExport()}
              >
                {reporting.exportButtonLabel}
              </Button>
              <Button asChild variant="outline">
                <a href={reporting.backendLinks[1].href} target="_blank" rel="noreferrer" aria-label={reporting.backendLinks[1].ariaLabel}>
                  {reporting.backendLinks[1].label}
                </a>
              </Button>
              {reporting.exportStatusText ? (
                <p
                  role={reporting.exportStatusRole}
                  className={cn(
                    "rounded-lg border px-3 py-2 text-sm",
                    reporting.exportStatusTone === "success" ? "border-success/30 bg-success/10 text-success" : "",
                    reporting.exportStatusTone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : "",
                    reporting.exportStatusTone === "neutral" ? "border-border/70 bg-secondary/25 text-muted-foreground" : ""
                  )}
                >
                  {reporting.exportStatusText}
                </p>
              ) : null}
            </CardContent>
          </Card>
        </section>
      ) : null}

      <section className={cn("grid gap-4", workstream === "reconciliation" ? "xl:grid-cols-1" : "xl:grid-cols-[1.15fr_0.85fr]")}>
        {workstream !== "reconciliation" ? (
          <ReconciliationQueueSummaryCard view={reconciliation.queuePanelView} />
        ) : null}

        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Landmark className="h-4 w-4 text-primary" />
                  {reporting.title}
                </CardTitle>
                <CardDescription className="mt-2">{reporting.description}</CardDescription>
              </div>
              <span className="w-fit rounded-sm border border-primary/35 bg-primary/10 px-2 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em] text-primary">
                {reporting.countLabel}
              </span>
            </div>
          </CardHeader>
          <CardContent className="grid gap-4">
            <div className="min-w-0">
              <div className="mb-2 flex flex-wrap items-center justify-between gap-2 text-xs">
                <span className="font-medium uppercase tracking-[0.14em] text-muted-foreground">{reporting.listLabel}</span>
                <span className="font-mono text-muted-foreground">{reporting.visibleCountLabel}</span>
              </div>
              <div role="list" aria-label={reporting.listLabel} className="space-y-2">
                {reporting.hasRows ? reporting.rows.map((profile) => (
                  <div key={profile.id} role="listitem">
                    <button
                      type="button"
                      aria-pressed={profile.isSelected}
                      aria-controls={reporting.detailId}
                      aria-label={profile.selectAriaLabel}
                      onClick={() => reporting.selectProfile(profile.id)}
                      className={cn(
                        "w-full rounded-lg border px-4 py-3 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-primary/40",
                        profile.isSelected
                          ? "border-primary/45 bg-primary/10"
                          : "border-border/70 bg-secondary/30 hover:bg-secondary/45"
                      )}
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="font-semibold text-foreground">{profile.name}</div>
                          <div className="mt-1 truncate font-mono text-xs text-muted-foreground">{profile.targetLabel}</div>
                        </div>
                        <span className="shrink-0 rounded-sm border border-primary/30 bg-primary/10 px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.14em] text-primary">
                          {profile.formatLabel}
                        </span>
                      </div>
                      <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted-foreground">{profile.description}</p>
                      <div className="mt-3 flex flex-wrap gap-2">
                        {profile.badges.map((badge) => (
                          <span key={`${profile.id}-${badge.label}`} className={reportingBadgeClass(badge.tone)}>
                            {badge.label}
                          </span>
                        ))}
                      </div>
                    </button>
                  </div>
                )) : (
                  <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                    {reporting.emptyText}
                  </div>
                )}
              </div>
            </div>

            <aside
              id={reporting.detailId}
              aria-live="polite"
              data-testid="reporting-profile-detail"
              className="min-w-0 overflow-hidden rounded-lg border border-border/70 bg-background/35 p-4"
            >
              <div className="eyebrow-label">{reporting.statusTitle}</div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{reporting.statusDetail}</p>
              <p className="mt-2 font-mono text-xs text-muted-foreground">{reporting.nextAction}</p>
              {reporting.selectedProfile ? (
                <div id={reporting.selectedProfile.id} className="mt-4 border-t border-border/70 pt-4">
                  <div className="break-words font-semibold text-foreground">{reporting.selectedProfile.title}</div>
                  <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{reporting.selectedProfile.subtitle}</div>
                  <p className="mt-3 break-words text-sm leading-6 text-muted-foreground">{reporting.selectedProfile.description}</p>
                  <dl className="mt-4 grid gap-2">
                    {reporting.selectedProfile.fields.map((field) => (
                      <div key={field.label} className="grid min-w-0 grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
                        <dt className="min-w-0 text-xs text-muted-foreground">{field.label}</dt>
                        <dd className={cn(
                          "min-w-0 break-words text-right font-mono text-xs text-foreground",
                          field.tone === "success" ? "text-success" : field.tone === "warning" ? "text-warning" : field.tone === "muted" ? "text-muted-foreground" : ""
                        )}>
                          {field.value}
                        </dd>
                      </div>
                    ))}
                  </dl>
                </div>
              ) : null}
            </aside>
          </CardContent>
        </Card>
      </section>

      {/* --- Security Master panel (shown when security-master workstream is active) --- */}
      {workstream === "security-master" && (
        <section className="space-y-6">
          <section className="panel-surface-strong space-y-4 p-5" aria-label={securityMaster.pageView.ariaLabel}>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="max-w-3xl">
                <div className="eyebrow-label">{securityMaster.pageView.eyebrow}</div>
                <h2 className="mt-2 text-2xl font-semibold tracking-normal text-foreground">{securityMaster.pageView.title}</h2>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{securityMaster.pageView.description}</p>
              </div>
              <Button variant="outline" size="sm" asChild className="shrink-0">
                <a href="#security-master-search" aria-label="Jump to Security Master search">
                  <Search className="h-3.5 w-3.5" aria-hidden="true" />
                  Search securities
                </a>
              </Button>
            </div>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {securityMaster.pageView.metrics.map((metric) => (
                <div key={metric.id} className="rounded-lg border border-border/60 bg-secondary/25 p-4">
                  <div className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{metric.label}</div>
                  <div
                    className={cn(
                      "mt-2 min-w-0 break-words font-mono text-lg font-semibold tabular-nums",
                      metric.tone === "success" ? "text-success" : metric.tone === "warning" ? "text-warning" : "text-foreground"
                    )}
                  >
                    {metric.value}
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
                </div>
              ))}
            </div>
          </section>

          {/* Search panel */}
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Security Master</div>
              <CardTitle className="flex items-center gap-2">
                <Search className="h-5 w-5 text-primary" />
                Security search
              </CardTitle>
              <CardDescription>
                Search by ticker, ISIN, CUSIP, FIGI, or display name. Results show classification and economic definition from the Security Master.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2">
                <label htmlFor="security-master-search" className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  Search securities
                </label>
                <input
                  id="security-master-search"
                  type="text"
                  value={securityMaster.query}
                  onChange={(e) => securityMaster.updateQuery(e.target.value)}
                  placeholder="Search securities…"
                  aria-controls={securityMaster.hasResults ? "security-master-results" : undefined}
                  aria-describedby="security-master-search-help security-master-search-status"
                  aria-invalid={securityMaster.searchErrorText ? true : undefined}
                  className="w-full rounded-lg border border-border/70 bg-secondary/30 px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary/50"
                />
                <p id="security-master-search-help" className="text-xs text-muted-foreground">
                  Search by ticker, ISIN, CUSIP, FIGI, or display name.
                </p>
              </div>

              <span className="sr-only" aria-live="polite">{securityMaster.statusAnnouncement}</span>
              {securityMaster.searchStatusText && (
                <p
                  id="security-master-search-status"
                  role={securityMaster.searching ? "status" : undefined}
                  className="text-sm text-muted-foreground"
                >
                  {securityMaster.searchStatusText}
                </p>
              )}
              {securityMaster.searchErrorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {securityMaster.searchErrorText}
                </div>
              )}

              {securityMaster.hasResults && (
                <DenseDataTable
                  columns={securityResultColumns}
                  rows={securityMaster.resultRows}
                  getRowId={(row) => row.rowId}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.detailPanelId}
                  getRowAriaExpanded={(row) => row.isExpanded}
                  onRowSelect={(row) => void securityMaster.selectSecurity(row.securityId)}
                  selectedRowId={securityMaster.selectedSecurityId ? `security-result-${securityMaster.selectedSecurityId}` : null}
                  emptyText={securityMaster.searchStatusText ?? "No Security Master results returned."}
                  ariaLabel={securityMaster.resultsTableLabel}
                  tableId="security-master-results"
                  caption={securityMaster.searchStatusText}
                />
              )}
              <div id={identity?.panelId ?? SECURITY_IDENTITY_DETAIL_PANEL_ID} className="space-y-4">
                {securityMaster.identityLoading && (
                  <p role="status" className="rounded-md border border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)] px-3 py-3 text-sm text-[var(--state-pending-fg)]">
                    Loading identity drill-in…
                  </p>
                )}
                {securityMaster.identityErrorText && (
                  <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                    {securityMaster.identityErrorText}
                  </div>
                )}
                {identity && (
                  <>
                  <EntitySummary
                    eyebrow="Identity drill-in"
                    title={identity.title}
                    subtitle={identity.subtitle}
                    description={identity.description}
                    status={<Badge variant={identity.statusBadgeVariant} dot>{identity.statusLabel}</Badge>}
                    fields={identity.summaryFields}
                    ariaLabel={identity.ariaLabel}
                  />
                  <ToolbarStrip
                    ariaLabel="Security identity detail context"
                    items={[
                      { id: "identifiers", label: "Identifiers", value: String(identity.identifiers.length), active: true },
                      { id: "aliases", label: "Aliases", value: String(identity.aliases.length) },
                      { id: "status", label: "Status", value: identity.statusLabel }
                    ]}
                  />
                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{identity.identifiersTitle}</div>
                    {identity.identifiers.length === 0 ? (
                      <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                        {identity.identifierEmptyText}
                      </p>
                    ) : (
                      <DenseDataTable
                        columns={identifierColumns}
                        rows={identity.identifiers}
                        getRowId={(identifier) => identifier.rowId}
                        getRowAriaLabel={(identifier) => identifier.ariaLabel}
                        emptyText={identity.identifierEmptyText}
                        ariaLabel={identity.identifiersTableLabel}
                        caption={identity.identifiersTableLabel}
                      />
                    )}
                  </div>

                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{identity.aliasesTitle}</div>
                    {identity.aliases.length === 0 ? (
                      <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                        {identity.aliasEmptyText}
                      </p>
                    ) : (
                      <DenseDataTable
                        columns={aliasColumns}
                        rows={identity.aliases}
                        getRowId={(alias) => alias.rowId}
                        getRowAriaLabel={(alias) => alias.ariaLabel}
                        emptyText={identity.aliasEmptyText}
                        ariaLabel={identity.aliasesTableLabel}
                        caption={identity.aliasesTableLabel}
                      />
                    )}
                  </div>
                  </>
                )}
              </div>
            </CardContent>
          </Card>

          {/* Conflicts panel */}
          <Card className="panel-surface">
            <CardHeader>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                  <CardTitle className="flex flex-wrap items-center gap-2">
                    <ShieldCheck className="h-5 w-5 text-primary" />
                    Identifier conflicts
                    {securityMaster.openConflictCount > 0 && (
                      <span className="inline-flex items-center rounded-sm border border-warning/35 bg-warning/10 px-2 py-0.5 font-mono text-[10px] font-medium uppercase tracking-[0.12em] text-warning">
                        {securityMaster.conflictCountLabel}
                      </span>
                    )}
                  </CardTitle>
                  <CardDescription className="mt-2">
                    Identifier ambiguities detected when multiple providers map the same identifier to different securities.
                  </CardDescription>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  busy={securityMaster.conflictRefreshCommand.busy}
                  busyLabel={securityMaster.conflictRefreshCommand.busyLabel}
                  disabled={securityMaster.conflictRefreshCommand.disabled}
                  disabledReason={securityMaster.conflictRefreshCommand.disabledReason}
                  aria-label={securityMaster.conflictRefreshCommand.ariaLabel}
                  onClick={() => void securityMaster.refreshConflicts()}
                  className="shrink-0"
                >
                  <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  {securityMaster.conflictRefreshCommand.label}
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              {securityMaster.conflictsLoading && <p role="status" className="text-sm text-muted-foreground">Loading conflicts…</p>}
              {securityMaster.conflictsErrorText && (
                <div role="alert" className="mb-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {securityMaster.conflictsErrorText}
                </div>
              )}
              {securityMaster.conflictActionErrorText && (
                <div role="alert" className="mb-3 rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {securityMaster.conflictActionErrorText}
                </div>
              )}
              {!securityMaster.conflictsLoading && securityMaster.conflicts !== null && !securityMaster.hasConflicts && (
                <p className="text-sm text-muted-foreground">{securityMaster.conflictEmptyText}</p>
              )}
              {securityMaster.hasConflicts && (
                <div className="space-y-3" aria-label={securityMaster.conflictSectionAriaLabel}>
                  {securityMaster.conflictRows.map((conflict) => (
                    <div
                      key={conflict.conflictId}
                      role="group"
                      aria-label={conflict.ariaLabel}
                      className={cn(
                        "rounded-lg border p-4",
                        conflict.statusTone === "warning" ? "border-warning/40 bg-warning/5" : "border-border/60 bg-secondary/20"
                      )}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-semibold">{conflict.fieldLabel}</span>
                            <span className={cn("rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.12em]", conflict.statusTone === "warning" ? "border-warning/35 bg-warning/10 text-warning" : "border-border/70 bg-secondary text-muted-foreground")}>
                              {conflict.statusLabel}
                            </span>
                          </div>
                          <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                            <span><span className="font-semibold text-foreground">Provider A:</span> {conflict.providerASummary}</span>
                            <span><span className="font-semibold text-foreground">Provider B:</span> {conflict.providerBSummary}</span>
                            <span className="font-mono text-xs">{conflict.detectedLabel}</span>
                          </div>
                        </div>
                        {conflict.actions.length > 0 && (
                          <div className="flex flex-wrap gap-2">
                            {conflict.actions.map((action) => (
                              <Button
                                key={action.resolution}
                                size="sm"
                                variant={action.variant}
                                disabled={action.disabled}
                                disabledReason={action.disabledReason}
                                aria-label={action.ariaLabel}
                                onClick={() => void securityMaster.resolveConflict(conflict.conflictId, action.resolution)}
                              >
                                {action.label}
                              </Button>
                            ))}
                          </div>
                        )}
                      </div>
                      {conflict.resolutionStatusText && (
                        <p role="status" className="mt-3 text-xs text-muted-foreground">{conflict.resolutionStatusText}</p>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>

          {securityMaster.selectedSecurityId && (
            <section className="panel-surface-strong space-y-4 p-5" aria-labelledby="security-detail-page-title">
              <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
                <div className="min-w-0">
                  <div className="eyebrow-label">{securityMaster.pageView.detailEyebrow}</div>
                  <h2 id="security-detail-page-title" className="mt-2 text-xl font-semibold tracking-normal text-foreground">
                    {securityMaster.pageView.detailTitle}
                  </h2>
                  <p className="mt-1 break-words font-mono text-xs uppercase tracking-[0.12em] text-muted-foreground">
                    {securityMaster.pageView.detailSubtitle}
                  </p>
                  <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">
                    {securityMaster.pageView.detailDescription}
                  </p>
                </div>
                <Badge variant={securityMaster.pageView.detailStatusBadgeVariant} dot className="w-fit shrink-0">
                  {securityMaster.pageView.detailStatusLabel}
                </Badge>
              </div>
              <ToolbarStrip
                ariaLabel={securityMaster.pageView.detailToolbarAriaLabel}
                items={securityMaster.pageView.detailSections}
              />
            </section>
          )}

          {/* Schedule workbench, corporate actions, and trading controls — shown when a security is selected */}
          {securityMaster.selectedSecurityId && (
            <>
              <SecuritySchedulesPanel
                view={securityMaster.schedulesView}
                onSelect={securityMaster.selectScheduleEvent}
              />
              <div className="grid gap-4 xl:grid-cols-2">
                <CorporateActionsPanel
                  view={securityMaster.corporateActionsView}
                  onSelect={securityMaster.selectCorporateAction}
                />
                <TradingParametersPanel view={securityMaster.tradingParametersView} />
              </div>
            </>
          )}

          {/* Extended security details & lots tracker — shown when a security is selected */}
          {securityMaster.selectedSecurityId && (
            <>
              <SecurityDetailsPanel
                entry={selectedSecurityEntry}
                identity={securityMaster.identity}
                tradingParameters={securityMaster.tradingParameters}
              />
              <LotsTrackerPanel
                securityId={securityMaster.selectedSecurityId}
                currency={selectedSecurityEntry?.economicDefinition.currency ?? null}
              />
            </>
          )}
        </section>
      )}

      {workstream === "reconciliation" && (
        <section
          id={reconciliation.detailActions?.breakChecklistTargetId ?? "reconciliation-break-queue"}
          aria-label="Reconciliation break checklist"
          className="space-y-4"
        >
          <Card className="panel-surface">
            <CardHeader>
              <CardTitle>Reconciliation break queue</CardTitle>
              <CardDescription>Review/resolve workflow with assignment and audit metadata.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <span className="sr-only" aria-live="polite">{reconciliation.statusAnnouncement}</span>
              {reconciliation.loadingText && (
                <p role="status" className="text-sm text-muted-foreground">{reconciliation.loadingText}</p>
              )}
              {reconciliation.errorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {reconciliation.errorText}
                </div>
              )}
              {reconciliation.actionErrorText && (
                <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {reconciliation.actionErrorText}
                </div>
              )}
              {!reconciliation.loadingText && !reconciliation.hasBreaks && (
                <p className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                  {reconciliation.emptyText}
                </p>
              )}
              {reconciliation.rows.map((item) => (
                <div key={item.breakId} className="rounded-lg border border-border/70 p-3">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="font-semibold">{item.strategyName} · {item.category}</div>
                      <div className="mt-0.5 text-xs text-muted-foreground">{item.reason}</div>
                    </div>
                    <div className="flex shrink-0 items-center gap-2">
                      {typeof item.variance === "number" && item.variance !== 0 && (
                        <span className={cn("font-mono text-xs font-semibold", item.variance > 0 ? "text-success" : "text-danger")}>
                          {item.variance > 0 ? "+" : ""}{item.variance.toLocaleString("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 2 })}
                        </span>
                      )}
                      <Badge variant={item.status === "Resolved" ? "success" : item.status === "InReview" ? "warning" : item.status === "Dismissed" ? "outline" : "danger"}>
                        {item.status}
                      </Badge>
                    </div>
                  </div>
                  {item.explainabilitySummary && (
                    <div className="mt-2 rounded-md border border-border/50 bg-secondary/20 px-3 py-2 text-xs leading-5 text-muted-foreground">
                      <span className="font-medium text-foreground">Analysis: </span>
                      {item.explainabilitySummary}
                    </div>
                  )}
                  {item.recommendedAction && (
                    <div className="mt-2 rounded-md border border-primary/20 bg-primary/5 px-3 py-2 text-xs leading-5">
                      <span className="font-medium text-primary">Recommended: </span>
                      <span className="text-foreground">{item.recommendedAction}</span>
                    </div>
                  )}
                  <div className="mt-2 flex flex-wrap gap-2">
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={!item.canAssign}
                      disabledReason={item.assignDisabledReason}
                      aria-label={item.assignAriaLabel}
                      onClick={() => void reconciliation.assignBreak(item.breakId)}
                    >
                      {item.assignLabel}
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={!item.canResolve || resolveDialog.isOpenFor(item.breakId)}
                      disabledReason={resolveDialog.getActionDisabledReason(item.breakId, "resolve", item.resolveDisabledReason)}
                      aria-label={item.resolveAriaLabel}
                      onClick={() => resolveDialog.open(item.breakId, "Resolved")}
                    >
                      {item.resolveLabel}
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={!item.canDismiss || resolveDialog.isOpenFor(item.breakId)}
                      disabledReason={resolveDialog.getActionDisabledReason(item.breakId, "dismiss", item.dismissDisabledReason)}
                      aria-label={item.dismissAriaLabel}
                      onClick={() => resolveDialog.open(item.breakId, "Dismissed")}
                    >
                      {item.dismissLabel}
                    </Button>
                  </div>
                  {resolveDialog.active?.breakId === item.breakId && (
                    <form
                      className="mt-3 space-y-2 rounded-lg border border-border/50 bg-secondary/20 p-3"
                      aria-label={resolveDialog.active.formAriaLabel}
                      onSubmit={(e) => {
                        e.preventDefault();
                        void resolveDialog.submit();
                      }}
                    >
                      <label htmlFor={resolveDialog.active.inputId} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                        {resolveDialog.active.label}
                      </label>
                      <input
                        id={resolveDialog.active.inputId}
                        type="text"
                        required
                        autoFocus
                        aria-describedby={resolveDialog.active.helpId}
                        placeholder={resolveDialog.active.placeholder}
                        value={resolveDialog.active.rationale}
                        onChange={(e) => resolveDialog.updateRationale(e.target.value)}
                        className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                      />
                      <p id={resolveDialog.active.helpId} className="text-xs text-muted-foreground">
                        {resolveDialog.active.helpText}
                      </p>
                      <div className="flex gap-2">
                        <Button
                          type="submit"
                          size="sm"
                          disabled={resolveDialog.active.isSubmitDisabled}
                          disabledReason={resolveDialog.active.submitDisabledReason}
                          aria-label={resolveDialog.active.submitAriaLabel}
                        >
                          {resolveDialog.active.submitLabel}
                        </Button>
                        <Button type="button" size="sm" variant="ghost" aria-label={resolveDialog.active.cancelAriaLabel} onClick={resolveDialog.close}>
                          {resolveDialog.active.cancelLabel}
                        </Button>
                      </div>
                    </form>
                  )}
                </div>
              ))}
            </CardContent>
          </Card>

          <CalibrationSummaryPanel view={reconciliation.calibrationView} />
        </section>
      )}
    </div>
  );
}

function CalibrationSummaryPanel({ view }: { view: CalibrationSummaryViewModel }) {
  const StatusIcon = view.statusIcon === "check" ? CheckCircle2 : AlertCircle;

  return (
    <Card className="panel-surface">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle className="flex items-center gap-2 text-base">
            <BookCheck className="h-4 w-4 text-primary" />
            Calibration summary
          </CardTitle>
          <CardDescription>Tolerance profile health across all active reconciliation break routes.</CardDescription>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={view.refresh}
          disabled={view.refreshCommand.disabled}
          disabledReason={view.refreshCommand.disabledReason}
          aria-label={view.refreshCommand.ariaLabel}
          className="shrink-0"
        >
          <RefreshCcw className="mr-2 h-3.5 w-3.5" aria-hidden="true" />
          {view.refreshCommand.label}
        </Button>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {view.errorText}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <>
            <div className={cn("flex items-center gap-3 rounded-lg border px-4 py-3", view.statusBannerClassName)}>
              <StatusIcon aria-hidden="true" className={cn("size-4 shrink-0", view.statusTextClassName)} />
              <div className="flex-1 min-w-0">
                <span className={cn("text-sm font-semibold", view.statusTextClassName)}>{view.statusLabel}</span>
                {view.summary && <p className="mt-0.5 text-xs text-muted-foreground">{view.summary}</p>}
              </div>
              <span className="shrink-0 font-mono text-xs text-muted-foreground">as of {view.asOfLabel}</span>
            </div>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              {view.metricRows.map((metric) => (
                <div
                  key={metric.id}
                  role="group"
                  aria-label={metric.ariaLabel}
                  className="rounded-md border border-border/60 bg-secondary/25 px-3 py-2 text-center"
                >
                  <div className="text-xs text-muted-foreground">{metric.label}</div>
                  <div className={cn("mt-1 font-mono text-lg font-semibold tabular-nums", metric.tone === "warning" ? "text-warning" : "text-foreground")}>
                    {metric.value}
                  </div>
                </div>
              ))}
            </div>
            <div>
              <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{view.profilesLabel}</div>
              {view.hasProfiles ? (
                <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
                  <DenseDataTable
                    columns={calibrationProfileColumns}
                    rows={view.profileRows}
                    getRowId={(row) => row.toleranceProfileId}
                    getRowAriaLabel={(row) => row.ariaLabel}
                    getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                    getRowAriaControls={(row) => row.detailPanelId}
                    getRowAriaExpanded={(row) => row.isSelected}
                    selectedRowId={view.selectedProfileId}
                    onRowSelect={(row) => view.selectProfile(row.toleranceProfileId)}
                    emptyText={view.emptyText}
                    ariaLabel={view.tableAriaLabel}
                  />
                  <div id={view.detailPanelId} aria-live="polite">
                    {view.selectedProfile ? (
                      <EntitySummary
                        eyebrow="Tolerance profile"
                        title={view.selectedProfile.title}
                        subtitle={view.selectedProfile.subtitle}
                        description={view.selectedProfile.description}
                        status={<Badge variant={view.selectedProfile.statusTone} dot>{view.selectedProfile.statusLabel}</Badge>}
                        fields={view.selectedProfile.fields}
                        ariaLabel={view.selectedProfile.ariaLabel}
                      />
                    ) : (
                      <div role="status" className="rounded-lg border border-border/70 bg-secondary/25 px-4 py-3 text-sm text-muted-foreground">
                        Select a tolerance profile to inspect its calibration posture.
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                  {view.emptyText}
                </div>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function CorporateActionsPanel({
  view,
  onSelect
}: {
  view: CorporateActionsViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Table2 className="h-4 w-4 text-primary" />
          Corporate actions
        </CardTitle>
        <CardDescription>
          Dividends, splits, spin-offs, and other corporate events for <span className="font-mono">{view.securityId}</span>.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {view.errorText}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.35fr)_minmax(18rem,0.65fr)]">
            <DenseDataTable
              columns={corporateActionColumns}
              rows={view.rows}
              getRowId={(row) => row.rowId}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              getRowAriaControls={(row) => row.detailPanelId}
              getRowAriaExpanded={(row) => row.isExpanded}
              onRowSelect={(row) => onSelect(row.rowId)}
              selectedRowId={view.selectedRowId}
              emptyText={view.emptyText}
              ariaLabel={view.tableLabel}
              caption={view.tableCaption}
            />
            <div
              id={view.detailPanelId}
              className="row-detail-panel h-fit min-w-0"
            >
              {view.selectedDetail ? (
                <EntitySummary
                  eyebrow={view.selectedDetail.eyebrow}
                  title={view.selectedDetail.title}
                  subtitle={view.selectedDetail.subtitle}
                  description={view.selectedDetail.description}
                  ariaLabel={view.selectedDetail.ariaLabel}
                  status={<Badge variant={view.selectedDetail.statusLabel === "Pay date scheduled" ? "success" : "warning"}>{view.selectedDetail.statusLabel}</Badge>}
                  fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
                />
              ) : (
                <div role="region" aria-label={view.detailEmptyAriaLabel}>
                  <div className="eyebrow-label">Corporate action detail</div>
                  <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
                </div>
              )}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function SecuritySchedulesPanel({
  view,
  onSelect
}: {
  view: SecuritySchedulesViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              <Table2 className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.title}
            </CardTitle>
            <CardDescription className="mt-2">{view.description}</CardDescription>
          </div>
          <div className="min-w-0 lg:max-w-[28rem]">
            <ToolbarStrip ariaLabel={view.toolbarAriaLabel} items={view.toolbarItems} />
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.45fr)_minmax(20rem,0.55fr)]">
          <DenseDataTable
            columns={securityScheduleColumns}
            rows={view.rows}
            getRowId={(row) => row.rowId}
            getRowAriaLabel={(row) => row.ariaLabel}
            getRowSelectAriaLabel={(row) => row.selectAriaLabel}
            getRowAriaControls={(row) => row.detailPanelId}
            getRowAriaExpanded={(row) => row.isExpanded}
            onRowSelect={(row) => onSelect(row.rowId)}
            selectedRowId={view.selectedRowId}
            emptyText={view.emptyText}
            ariaLabel={view.tableLabel}
            caption={view.tableCaption}
          />
          <div id={view.detailPanelId} className="row-detail-panel h-fit min-w-0">
            {view.selectedDetail ? (
              <EntitySummary
                eyebrow={view.selectedDetail.eyebrow}
                title={view.selectedDetail.title}
                subtitle={view.selectedDetail.subtitle}
                description={view.selectedDetail.description}
                ariaLabel={view.selectedDetail.ariaLabel}
                status={<Badge variant={view.selectedDetail.statusTone}>{view.selectedDetail.statusLabel}</Badge>}
                fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
              />
            ) : (
              <div role="region" aria-label={view.detailEmptyAriaLabel}>
                <div className="eyebrow-label">Schedule event detail</div>
                <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
              </div>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function TradingParametersPanel({ view }: { view: TradingParametersViewState }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <TrendingUp className="h-4 w-4 text-primary" />
          Trading parameters
        </CardTitle>
        <CardDescription>
          Lot size, tick size, margin, and circuit-breaker constraints
          {view.securityId ? <> for <span className="font-mono">{view.securityId}</span></> : null}
          {view.asOfLabel !== "—" ? <> as of {view.asOfLabel}</> : null}.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {view.errorText}
          </div>
        )}
        {!view.loadingText && !view.errorText && view.fields.length === 0 && (
          <p className="text-sm text-muted-foreground">No trading parameters available for this security.</p>
        )}
        {view.fields.length > 0 && (
          <dl className="grid gap-2">
            {view.fields.map((field) => (
              <div key={field.label} className="grid min-w-0 grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
                <dt className="min-w-0 text-xs text-muted-foreground">{field.label}</dt>
                <dd className={cn(
                  "min-w-0 break-words text-right font-mono text-xs",
                  field.tone === "warning" ? "text-warning" : "text-foreground"
                )}>
                  {field.value}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </CardContent>
    </Card>
  );
}

function ReconciliationQueueSummaryCard({ view }: { view: ReconciliationQueuePanelViewState }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.overviewTitle}
            </CardTitle>
            <CardDescription className="mt-2">{view.overviewDescription}</CardDescription>
          </div>
          <Button asChild variant="outline" size="sm" className="w-fit shrink-0">
            <Link to={view.overviewActionHref} aria-label={view.overviewActionAriaLabel}>
              {view.overviewActionLabel}
            </Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <DenseDataTable
          columns={reconciliationQueueColumns}
          rows={view.rows}
          getRowId={(row) => row.runId}
          getRowAriaLabel={(row) => row.ariaLabel}
          emptyText={view.emptyText}
          ariaLabel={view.listLabel}
          caption={view.overviewCaption}
        />
      </CardContent>
    </Card>
  );
}

function GovernanceHighlight({
  icon: Icon,
  title,
  description
}: {
  icon: typeof ShieldCheck;
  title: string;
  description: string;
}) {
  return (
    <div className="workspace-header-card">
      <Icon className="mb-3 h-5 w-5 text-primary" />
      <div className="font-semibold text-foreground">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function GovernanceValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  return (
    <div aria-label={ariaLabel} className="data-grid-surface flex items-center justify-between gap-4 px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn("font-mono text-foreground", tone)}>{value}</span>
    </div>
  );
}

function GovernanceChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono capitalize text-foreground">{value}</span>
    </span>
  );
}

function cashFlowTextClass(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") return "text-success";
  if (tone === "warning") return "text-warning";
  if (tone === "danger") return "text-danger";
  return "";
}

function cashFlowBadgeClass(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") return "border-success/35 bg-success/10 text-success";
  if (tone === "warning") return "border-warning/35 bg-warning/10 text-warning";
  if (tone === "danger") return "border-danger/35 bg-danger/10 text-danger";
  return "border-border/70 bg-secondary text-muted-foreground";
}

function reportingBadgeClass(tone: "primary" | "success" | "warning" | "muted") {
  return cn(
    "rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.12em]",
    tone === "primary" ? "border-primary/35 bg-primary/10 text-primary" : "",
    tone === "success" ? "border-success/35 bg-success/10 text-success" : "",
    tone === "warning" ? "border-warning/35 bg-warning/10 text-warning" : "",
    tone === "muted" ? "border-border/70 bg-secondary text-muted-foreground" : ""
  );
}
