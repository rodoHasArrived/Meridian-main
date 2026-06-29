import { AlertCircle, BookCheck, CheckCircle2, Landmark, Network, RefreshCcw } from "lucide-react";
import { Link } from "react-router-dom";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { cn } from "@/lib/utils";
import { cashFlowTextClass } from "@/screens/accounting-screen.styles";
import type {
  CalibrationProfileRowViewModel,
  CalibrationSummaryViewModel,
  ReconciliationComparisonViewState,
  ReconciliationDetailActionsViewModel,
  ReconciliationDetailViewState,
  ReconciliationQueuePanelViewState,
  ReconciliationQueueRunRowViewModel,
  ReconciliationQueueRunTone,
  ReconciliationStatementRunRowViewModel,
  ReconciliationStatementRunsViewState
} from "@/screens/accounting-screen.view-model";

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

const reconciliationQueueToneClass: Record<ReconciliationQueueRunTone, string> = {
  muted: "text-muted-foreground",
  warning: "text-warning",
  success: "text-success",
  primary: "text-primary"
};

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

const reconciliationStatementRunColumns: DenseDataTableColumn<ReconciliationStatementRunRowViewModel>[] = [
  { id: "brokerCustodian", label: "Broker/Custodian", render: (row) => <span title={row.unavailableReason ?? undefined}>{row.brokerCustodianLabel}</span> },
  { id: "account", label: "Account", render: (row) => <span title={row.unavailableReason ?? undefined}>{row.accountLabel}</span> },
  { id: "period", label: "Period", render: (row) => <span className="font-mono text-muted-foreground" title={row.unavailableReason ?? undefined}>{row.periodLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusLabel === "Matched" || row.statusLabel === "Balanced" ? "success" : "warning"}>{row.statusLabel}</Badge> },
  { id: "validation", label: "Validation issues", align: "right", render: (row) => <span className="font-mono">{row.validationIssueCountLabel}</span> },
  { id: "matches", label: "Matches", align: "right", render: (row) => <span className="font-mono">{row.matchCountLabel}</span> },
  { id: "breaks", label: "Breaks", align: "right", render: (row) => <span className="font-mono">{row.breakCountLabel}</span> },
  { id: "cases", label: "Cases", align: "right", render: (row) => <span className="font-mono">{row.caseCountLabel}</span> },
  { id: "imported", label: "Imported", render: (row) => <span className="font-mono text-muted-foreground">{row.importedAtLabel}</span> }
];

export interface AccountingReconciliationCaseworkPanelProps {
  comparisonView: ReconciliationComparisonViewState;
  statementRunsView: ReconciliationStatementRunsViewState;
  queuePanelView: ReconciliationQueuePanelViewState;
  selectedStatementRunId: string | null;
  selectedQueueRunId: string | null;
  selectedDetail: ReconciliationDetailViewState | null;
  detailActions: ReconciliationDetailActionsViewModel | null;
  onRefreshStatementRuns: () => void;
  onSelectRun: (runId: string) => void;
}

export function AccountingReconciliationCaseworkPanel({
  comparisonView,
  statementRunsView,
  queuePanelView,
  selectedStatementRunId,
  selectedQueueRunId,
  selectedDetail,
  detailActions,
  onRefreshStatementRuns,
  onSelectRun
}: AccountingReconciliationCaseworkPanelProps) {
  return (
    <section id="accounting-exceptions" className="workspace-section-band" aria-labelledby="accounting-exceptions-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Exceptions</p>
          <h3 id="accounting-exceptions-heading" className="workspace-section-title">Reconciliation exceptions and evidence</h3>
          <p className="workspace-section-summary">Statement runs, selected queue detail, and evidence links are grouped for investigation.</p>
        </div>
        <a className="workspace-section-jump" href="#accounting-actions">Actions</a>
      </div>
      <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <div className="xl:col-span-2">
          <ReconciliationComparisonPanel view={comparisonView} />
        </div>

        <Card className="panel-surface xl:col-span-2">
          <CardHeader>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Landmark className="h-4 w-4 text-primary" aria-hidden="true" />
                  {statementRunsView.title}
                </CardTitle>
                <CardDescription className="mt-2">{statementRunsView.description}</CardDescription>
              </div>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={statementRunsView.loadingText !== null}
                disabledReason={statementRunsView.loadingText ? "Statement run refresh is already in progress." : null}
                aria-label={statementRunsView.recoveryActionAriaLabel}
                onClick={onRefreshStatementRuns}
              >
                <RefreshCcw className="h-4 w-4" aria-hidden="true" />
                {statementRunsView.recoveryActionLabel}
              </Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <span className="sr-only" aria-live="polite">{statementRunsView.statusAnnouncement}</span>
            {statementRunsView.loadingText ? (
              <StatusBanner
                role="status"
                tone="info"
                title="Statement runs loading"
                detail={statementRunsView.loadingText}
              />
            ) : null}
            {statementRunsView.errorText ? (
              <StatusBanner
                role="alert"
                tone="danger"
                title={statementRunsView.errorText}
                detail={statementRunsView.errorDetails.length > 0 ? (
                  <ul className="mt-2 list-disc pl-5">
                    {statementRunsView.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              />
            ) : null}
            <DenseDataTable
              columns={reconciliationStatementRunColumns}
              rows={statementRunsView.rows}
              getRowId={(row) => row.runId}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              getRowAriaControls={(row) => row.controlsId}
              getRowAriaExpanded={(row) => row.isSelected}
              selectedRowId={selectedStatementRunId}
              onRowSelect={(row) => onSelectRun(row.runId)}
              emptyText={statementRunsView.emptyText}
              ariaLabel={statementRunsView.tableLabel}
              caption={statementRunsView.tableCaption}
            />
            <Tabs
              id={statementRunsView.detailPanelId}
              aria-label="Statement run detail tabs"
              tabs={statementRunsView.tabs.map((tab) => ({
                ariaLabel: tab.ariaLabel,
                count: tab.badgeLabel,
                disabled: tab.disabled,
                id: tab.id,
                label: tab.label
              }))}
            >
              {statementRunsView.tabs.map((tab) => (
                <TabPanel key={tab.id}>
                  <StatusBanner
                    role={tab.disabled ? "status" : undefined}
                    tone={tab.disabled ? "warning" : "info"}
                    title={tab.label}
                    detail={tab.disabledReason ?? tab.description}
                  />
                </TabPanel>
              ))}
            </Tabs>
            <p className="text-xs text-muted-foreground">
              Matching, tolerance, validation, and case-state decisions remain in reconciliation services; this view shows service-reviewed results.
            </p>
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {queuePanelView.title}
            </CardTitle>
            <CardDescription>{queuePanelView.description}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {queuePanelView.hasRows ? (
              <DenseDataTable
                columns={reconciliationQueueColumns}
                rows={queuePanelView.rows}
                getRowId={(row) => row.runId}
                getRowAriaLabel={(row) => row.ariaLabel}
                getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                getRowAriaControls={(row) => row.controlsId}
                getRowAriaExpanded={(row) => row.isExpanded}
                selectedRowId={selectedQueueRunId}
                onRowSelect={(row) => onSelectRun(row.runId)}
                emptyText={queuePanelView.emptyText}
                ariaLabel={queuePanelView.listLabel}
                caption={queuePanelView.description}
              />
            ) : (
              <StatusBanner
                role="status"
                tone="warning"
                title="Reconciliation queue empty"
                detail={queuePanelView.emptyText}
              />
            )}
          </CardContent>
        </Card>

        <Card
          id={queuePanelView.detailPanelId}
          data-selected-source="Selected from reconciliation queue"
          className="row-detail-panel panel-surface-strong bg-panel-strong text-foreground"
          role="region"
          aria-live="polite"
          aria-label={selectedDetail?.ariaLabel ?? queuePanelView.detailEmptyAriaLabel}
        >
          <CardHeader>
            <div className="eyebrow-label">{selectedDetail?.eyebrow ?? "Reconciliation detail"}</div>
            <CardTitle>{selectedDetail?.title ?? queuePanelView.detailEmptyTitle}</CardTitle>
            <CardDescription className="text-muted-foreground">
              {selectedDetail?.description ?? queuePanelView.detailEmptyText}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            {selectedDetail ? (
              <>
                {selectedDetail.fields.map((field) => (
                  <ReconciliationValue
                    key={field.label}
                    label={field.label}
                    value={field.value}
                    tone={cashFlowTextClass(field.tone)}
                    ariaLabel={field.ariaLabel}
                  />
                ))}
                <div
                  aria-label={selectedDetail.narrativeLabel}
                  className="rounded-lg border border-border/70 bg-background/70 p-4 text-muted-foreground"
                >
                  {selectedDetail.narrative}
                </div>
                {detailActions ? (
                  <div className="panel-action-footer">
                    <Button asChild variant="secondary">
                      <Link
                        to={detailActions.evidencePacketHref}
                        aria-label={detailActions.evidencePacketAriaLabel}
                      >
                        <Network className="h-4 w-4" aria-hidden="true" />
                        {detailActions.evidencePacketLabel}
                      </Link>
                    </Button>
                    <Button asChild variant="secondary">
                      <a
                        href={detailActions.breakChecklistHref}
                        aria-label={detailActions.breakChecklistAriaLabel}
                      >
                        {detailActions.breakChecklistLabel}
                      </a>
                    </Button>
                    <Button asChild variant="outline" className="border-border/70 bg-transparent text-foreground hover:bg-secondary/60">
                      <a
                        href={detailActions.auditPacketHref}
                        target="_blank"
                        rel="noreferrer"
                        aria-label={detailActions.auditPacketAriaLabel}
                      >
                        {detailActions.auditPacketLabel}
                      </a>
                    </Button>
                  </div>
                ) : null}
              </>
            ) : (
              <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                {queuePanelView.detailEmptyText}
              </div>
            )}
          </CardContent>
        </Card>
      </section>
    </section>
  );
}

export function CalibrationSummaryPanel({ view }: { view: CalibrationSummaryViewModel }) {
  const StatusIcon = view.statusIcon === "check" ? CheckCircle2 : AlertCircle;

  return (
    <Card id="accounting-history" className="panel-surface">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle className="flex items-center gap-2 text-base">
            <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
            Calibration summary
          </CardTitle>
          <CardDescription>Tolerance profile health, break trend, auto-match rate, and T+0 closure rate across active reconciliation routes.</CardDescription>
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
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
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
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-9">
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

export function ReconciliationQueueSummaryCard({ view }: { view: ReconciliationQueuePanelViewState }) {
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

function ReconciliationComparisonPanel({ view }: { view: ReconciliationComparisonViewState }) {
  return (
    <section className="accounting-reference-panel" data-appearance="light" aria-label={view.ariaLabel}>
      <div className="accounting-reference-heading">
        <div className="min-w-0">
          <p className="accounting-reference-kicker">{view.title}</p>
          <p className="accounting-reference-subtitle">{view.subtitle}</p>
        </div>
        <div className="accounting-reference-badges" aria-label="Reconciliation match status">
          <span className="accounting-reference-badge accounting-reference-badge-success">{view.matchedBadgeLabel}</span>
          <span className="accounting-reference-badge accounting-reference-badge-warning">{view.openBadgeLabel}</span>
        </div>
      </div>

      <div className="accounting-reconciliation-grid">
        <div className="accounting-reconciliation-column-heading">
          <span>{view.statementHeading}</span>
        </div>
        <div className="accounting-reconciliation-column-heading">
          <span>{view.ledgerHeading}</span>
        </div>
        {view.rows.map((row) => (
          <div key={row.id} className="contents">
            <div className={cn("accounting-reconciliation-cell", row.statusTone === "success" ? "is-matched" : "is-open")}>
              <div className="min-w-0">
                <div className="accounting-reconciliation-title">{row.statementTitle}</div>
                <div className="accounting-reconciliation-meta">{row.statementMeta}</div>
              </div>
              <div className="accounting-reconciliation-value">{row.statementValue}</div>
            </div>
            <div className={cn("accounting-reconciliation-cell", row.statusTone === "success" ? "is-matched" : "is-open")}>
              <div className="min-w-0">
                <div className="accounting-reconciliation-title">{row.ledgerTitle}</div>
                <div className="accounting-reconciliation-meta">{row.ledgerMeta}</div>
              </div>
              <div className="accounting-reconciliation-value">{row.ledgerValue}</div>
            </div>
          </div>
        ))}
      </div>

      <div className="accounting-balance-strip">
        <div>
          <span>Statement balance</span>
          <strong>{view.statementBalanceLabel}</strong>
        </div>
        <div>
          <span>Ledger balance</span>
          <strong>{view.ledgerBalanceLabel}</strong>
        </div>
        <div className={cn("accounting-reference-balance-badge", view.varianceTone === "success" ? "is-balanced" : "is-out")}>
          <span aria-hidden="true" />
          {view.varianceLabel}
        </div>
      </div>
    </section>
  );
}

function ReconciliationValue({ label, value, tone, ariaLabel }: { label: string; value: string; tone?: string; ariaLabel?: string }) {
  return (
    <div aria-label={ariaLabel} className="data-grid-surface flex items-center justify-between gap-4 px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn("font-mono text-foreground", tone)}>{value}</span>
    </div>
  );
}
