import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode, type UIEvent } from "react";
import { Link, useLocation } from "react-router-dom";
import { Landmark, Network, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import { cn } from "@/lib/utils";
import { sourceObservationLabel, sourceObservationAge } from "./reconciliation-source-observation";
import { StatementRunDetailTabs } from "./accounting-screen.statement-run-detail";
import type {
  ReconciliationStatementRunRowViewModel,
  ReconciliationBreakRowViewModel,
  useAccountingReconciliationViewModel,
  useReconciliationResolveDialogViewModel
} from "./accounting-screen.view-model";
import type { ReconciliationQueueStatus, ReconciliationQueueSort } from "./reconciliation-view-state";
import "@/styles/reconciliation-workbench.css";

type ReconciliationViewModel = ReturnType<typeof useAccountingReconciliationViewModel>;
type Reconciliation = Pick<ReconciliationViewModel,
  | "rows"
  | "selectedBreakId"
  | "selectedDetail"
  | "selectBreak"
  | "assignBreak"
  | "detailPanelId"
  | "detailEmptyAriaLabel"
  | "detailEmptyTitle"
  | "detailEmptyText"
  | "detailActions"
  | "statusAnnouncement"
  | "loadingText"
  | "errorText"
  | "errorDetails"
  | "actionErrorText"
  | "actionErrorDetails"
  | "emptyText"
  | "tableLabel"
  | "tableCaption"
>;
type StatementRuns = Pick<ReconciliationViewModel,
  "statementRunsView" | "refreshStatementRuns" | "selectedRunId" | "selectRun" | "reconciliationQueue"
>;
type ResolveDialog = ReturnType<typeof useReconciliationResolveDialogViewModel>;
export interface ReconciliationQueueControls {
  queueSearch: string;
  queueStatus: string;
  queueSort: string;
  setQueueSearch: (value: string) => void;
  setQueueStatus: (value: ReconciliationQueueStatus) => void;
  setQueueSort: (value: ReconciliationQueueSort) => void;
  clearQueueFilters: () => void;
}
const reconciliationBreakColumns: DenseDataTableColumn<ReconciliationBreakRowViewModel>[] = [
  {
    id: "break",
    label: "Break",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.strategyName}</span>
        <span className="mt-1 block text-[11px] text-muted-foreground">{row.financeLabel}</span>
      </span>
    )
  },
  { id: "category", label: "Case", render: (row) => <span className="text-foreground">{row.financeLabel}</span> },
  {
    id: "variance",
    label: "Variance",
    align: "right",
    render: (row) => (
      <span
        className={cn(
          "font-mono tabular-nums",
          row.varianceTone === "success" ? "text-success" : row.varianceTone === "danger" ? "text-danger" : "text-foreground"
        )}
      >
        {row.varianceLabel}
      </span>
    )
  },
  { id: "owner", label: "Owner", render: (row) => <span className="font-mono text-muted-foreground">{row.ownerLabel}</span> },
  { id: "updated", label: "Updated", render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedAtLabel}</span> },
  { id: "source", label: "Source observation", render: (row) => <span className="block"><span className="block">{sourceObservationLabel(row.lineage)}</span><span className="block text-muted-foreground">{sourceObservationAge(row.lineage)}</span></span> },
  { id: "status", label: "Case status", render: (row) => <Badge variant={row.statusBadgeVariant}>{row.status}</Badge> }
];

export function ReconciliationWorkbench({ reconciliation, resolveDialog, runSelector, controls, selectionNotice }: {
  reconciliation: Reconciliation;
  resolveDialog: ResolveDialog;
  runSelector?: ReactNode;
  controls: ReconciliationQueueControls;
  selectionNotice?: string | null;
}) {
  const location = useLocation();
  const queueRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const pendingQueueScroll = useRef<number | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const selectedRow = reconciliation.rows.find((row) => row.breakId === reconciliation.selectedBreakId) ?? null;
  const activeResolveBreak = resolveDialog.active
    ? reconciliation.rows.find((row) => row.breakId === resolveDialog.active?.breakId) ?? null : null;
  const visibleRows = filterReconciliationRows(reconciliation.rows, controls);
  const selectedOutsideFilter = selectedRow && !visibleRows.some((row) => row.breakId === selectedRow.breakId);
  useLayoutEffect(() => {
    const position = (window.history.state?.usr as { reconciliationQueueScroll?: number } | null)?.reconciliationQueueScroll;
    pendingQueueScroll.current = typeof position === "number" ? position : null;
  }, [location.key]);
  useLayoutEffect(() => {
    const queue = queueRef.current;
    const position = pendingQueueScroll.current;
    if (!queue || position === null) return;
    // Navigation can restore before the asynchronous queue has mounted its rows.
    // Keep the desired offset until there is content rather than accepting a clamp to zero.
    if (position > 0 && visibleRows.length === 0) return;
    queue.scrollTop = position;
    if (queue.scrollTop === position || !reconciliation.loadingText) {
      pendingQueueScroll.current = null;
    }
  }, [location.key, visibleRows.length, reconciliation.loadingText]);
  useEffect(() => {
    if (resolveDialog.active && resolveDialog.active.breakId !== reconciliation.selectedBreakId) resolveDialog.close();
  }, [reconciliation.selectedBreakId, resolveDialog]);
  const selectedBreakPrimaryFields = reconciliation.selectedDetail?.fields.filter((field) => [
    "Variance",
    "Owner",
    "Detected",
    "Updated",
    "Urgency",
    "Required sign-off",
    "Decision note",
    "Comments",
    "Evidence links"
  ].includes(field.label)) ?? [];
  if (selectedRow) {
    selectedBreakPrimaryFields.push({
      label: "Source observation",
      value: `${sourceObservationLabel(selectedRow.lineage)} · ${sourceObservationAge(selectedRow.lineage)}`
    });
  }
  const selectedBreakTechnicalFields = reconciliation.selectedDetail?.fields.filter((field) => (
    !selectedBreakPrimaryFields.includes(field)
  )) ?? [];
  function renderBreakActions(item: ReconciliationBreakRowViewModel) {
    return (
      <div className="panel-action-zone">
        <Button
          size="sm"
          variant="outline"
          disabled={!item.canAssign}
          disabledReason={item.assignDisabledReason}
          aria-label={item.assignAriaLabel}
          onClick={() => {
            reconciliation.selectBreak(item.breakId);
            void reconciliation.assignBreak(item.breakId);
          }}
        >
          {item.assignLabel}
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={!item.canResolve || resolveDialog.isOpenFor(item.breakId)}
          disabledReason={resolveDialog.getActionDisabledReason(item.breakId, "resolve", item.resolveDisabledReason)}
          aria-label={item.resolveAriaLabel}
          onClick={() => {
            reconciliation.selectBreak(item.breakId);
            resolveDialog.open(item.breakId, "Resolved");
          }}
        >
          {item.resolveLabel}
        </Button>
        <Button
          size="sm"
          variant="ghost"
          disabled={!item.canDismiss || resolveDialog.isOpenFor(item.breakId)}
          disabledReason={resolveDialog.getActionDisabledReason(item.breakId, "dismiss", item.dismissDisabledReason)}
          aria-label={item.dismissAriaLabel}
          onClick={() => {
            reconciliation.selectBreak(item.breakId);
            resolveDialog.open(item.breakId, "Dismissed");
          }}
        >
          {item.dismissLabel}
        </Button>
      </div>
    );
  }

  function returnToQueue() {
    setDetailOpen(false);
    requestAnimationFrame(() => {
      const row = queueRef.current?.querySelector<HTMLElement>('[aria-selected="true"]');
      (row ?? toggleRef.current)?.focus();
    });
  }

  function openInspector() {
    setDetailOpen(true);
    requestAnimationFrame(() => document.getElementById(reconciliation.detailPanelId)?.focus());
  }

  function handleWorkbenchKeyDown(event: KeyboardEvent<HTMLElement>) {
    const target = event.target as HTMLElement;
    if (
      (event.key === "Enter" || event.key === " ")
      && queueRef.current?.contains(target)
      && target.matches("tr[data-dense-row-id]")
    ) {
      setDetailOpen(true);
    }
    if (event.key === "Escape" && detailOpen && target.closest(".reconciliation-inspector")) {
      event.preventDefault();
      event.stopPropagation();
      returnToQueue();
    }
  }

  function rememberQueueScroll(event: UIEvent<HTMLDivElement>) {
    if (pendingQueueScroll.current !== null) return;
    const state = window.history.state ?? {};
    window.history.replaceState({
      ...state,
      usr: { ...state.usr, reconciliationQueueScroll: event.currentTarget.scrollTop }
    }, "");
  }

  return (
    <section
      id="accounting-actions"
      aria-label="Reconciliation casework"
      className="reconciliation-workbench"
      onKeyDownCapture={handleWorkbenchKeyDown}
    >
      {runSelector}
      <WorkspaceWorkbenchShell
        label="Reconciliation workbench"
        inspectorRole="region"
        className={cn("reconciliation-workbench-shell", detailOpen && "reconciliation-workbench--detail-open")}
        inspector={(
          <DenseRowDetailPanel
            id={reconciliation.detailPanelId}
            ariaLabel="Selected reconciliation break"
            className="reconciliation-inspector"
            selectedSourceLabel="Selected from break queue"
          >
            <Button type="button" variant="outline" className="reconciliation-back-to-queue" onClick={returnToQueue}>
              Back to break queue
            </Button>
            <div className="row-detail-panel">
              {reconciliation.selectedDetail ? (
                <EntitySummary
                  eyebrow={reconciliation.selectedDetail.eyebrow}
                  title={reconciliation.selectedDetail.title}
                  subtitle={reconciliation.selectedDetail.subtitle}
                  description={reconciliation.selectedDetail.description}
                  ariaLabel={reconciliation.selectedDetail.ariaLabel}
                  actions={selectedRow ? (
                    <div className="panel-action-zone" aria-label="Selected break actions">
                      {renderBreakActions(selectedRow)}
                    </div>
                  ) : null}
                  fields={selectedBreakPrimaryFields}
                  status={<Badge variant={reconciliation.selectedDetail.statusBadgeVariant}>{reconciliation.selectedDetail.statusLabel}</Badge>}
                />
              ) : (
                <section
                  role="region"
                  aria-label={reconciliation.detailEmptyAriaLabel}
                  className="rounded-lg border border-border/70 bg-secondary/20 px-4 py-3 text-sm text-muted-foreground"
                >
                  <div className="eyebrow-label">{reconciliation.detailEmptyTitle}</div>
                  <p className="mt-1">{reconciliation.detailEmptyText}</p>
                </section>
              )}
            </div>
            {reconciliation.selectedDetail && selectedBreakTechnicalFields.length > 0 ? (
              <TechnicalDetails
                label="Break metadata"
                description="Identifiers, routing metadata, policy values, and retained explanation detail for audit and support use."
              >
                <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
                  <AccountingValue label="Break ID" value={reconciliation.selectedBreakId ?? "Not retained"} />
                  {selectedBreakTechnicalFields.map((field) => (
                    <AccountingValue key={field.label} label={field.label} value={field.value} />
                  ))}
                </div>
              </TechnicalDetails>
            ) : null}
            {reconciliation.selectedDetail?.analysisText ? (
              <div className="rounded-md border border-border/50 bg-secondary/20 px-3 py-2 text-xs leading-5 text-muted-foreground">
                <span className="font-medium text-foreground">Analysis: </span>
                {reconciliation.selectedDetail.analysisText}
              </div>
            ) : null}
            {reconciliation.selectedDetail?.recommendedActionText ? (
              <div className="rounded-md border border-primary/20 bg-primary/5 px-3 py-2 text-xs leading-5">
                <span className="font-medium text-primary">Recommended: </span>
                <span className="text-foreground">{reconciliation.selectedDetail.recommendedActionText}</span>
              </div>
            ) : null}
            {reconciliation.selectedDetail?.routingActionHref && reconciliation.selectedDetail.routingActionLabel ? (
              <Button asChild variant="secondary" className="w-fit">
                <Link
                  to={reconciliation.selectedDetail.routingActionHref}
                  aria-label={reconciliation.selectedDetail.routingActionAriaLabel ?? reconciliation.selectedDetail.routingActionLabel}
                >
                  <Network className="h-4 w-4" aria-hidden="true" />
                  {reconciliation.selectedDetail.routingActionLabel}
                </Link>
              </Button>
            ) : null}
            {reconciliation.detailActions ? (
              <div className="panel-action-zone">
                <Button asChild variant="secondary">
                  <Link to={reconciliation.detailActions.evidencePacketHref}>Evidence packet</Link>
                </Button>
                <Button asChild variant="secondary">
                  <a href={reconciliation.detailActions.auditPacketHref}>Review audit packet</a>
                </Button>
              </div>
            ) : null}
            {activeResolveBreak && resolveDialog.active ? (
              <form
                className="space-y-2 rounded-lg border border-border/50 bg-secondary/20 p-3"
                aria-label={resolveDialog.active.formAriaLabel}
                onSubmit={(e) => {
                  e.preventDefault();
                  void resolveDialog.submit();
                }}
              >
                <FormRow
                  label={resolveDialog.active.label}
                  labelFor={resolveDialog.active.inputId}
                  hint={resolveDialog.active.helpText}
                >
                  <Input
                    id={resolveDialog.active.inputId}
                    type="text"
                    required
                    autoFocus
                    aria-describedby={resolveDialog.active.helpId}
                    placeholder={resolveDialog.active.placeholder}
                    value={resolveDialog.active.rationale}
                    onChange={(e) => resolveDialog.updateRationale(e.target.value)}
                  />
                  <span id={resolveDialog.active.helpId} className="sr-only">
                    {resolveDialog.active.helpText}
                  </span>
                </FormRow>
                <div className="panel-action-zone justify-start">
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
            ) : null}
          </DenseRowDetailPanel>
        )}
      >
        <Card id="reconciliation-break-queue" role="region" aria-label="Reconciliation break checklist" className="panel-surface">
          <CardHeader>
            <CardTitle>Reconciliation break queue</CardTitle>
            <CardDescription>Review discrepancies, evidence, and permitted actions without leaving the queue.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <span className="sr-only" aria-live="polite">{reconciliation.statusAnnouncement}</span>
            {reconciliation.loadingText && (
              <p role="status" className="text-sm text-muted-foreground">{reconciliation.loadingText}</p>
            )}
            {reconciliation.errorText && (
              <StatusBanner
                role="alert"
                tone="danger"
                title={reconciliation.errorText}
                detail={reconciliation.errorDetails.length > 0 ? (
                  <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                    {reconciliation.errorDetails.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              />
            )}
            {reconciliation.actionErrorText && (
              <StatusBanner
                role="alert"
                tone="danger"
                title={reconciliation.actionErrorText}
                detail={reconciliation.actionErrorDetails.length > 0 ? (
                  <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                    {reconciliation.actionErrorDetails.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              />
            )}

            {selectionNotice ? <p role="status" className="text-sm text-muted-foreground">{selectionNotice}</p> : null}
            <div className="reconciliation-queue-toolbar" role="group" aria-label="Break queue controls">
              <label>
                Search breaks
                <Input value={controls.queueSearch} onChange={(e) => controls.setQueueSearch(e.target.value)} placeholder="Break, account, owner…" />
              </label>
              <label>
                Case status
                <select value={controls.queueStatus} onChange={(e) => controls.setQueueStatus(e.target.value as ReconciliationQueueStatus)}>
                  {["all", "Open", "InReview", "Resolved", "Dismissed", "SignedOff"].map((status) => (
                    <option key={status} value={status}>
                      {status === "all" ? "All statuses" : status === "InReview" ? "In review" : status === "SignedOff" ? "Signed off" : status}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Sort breaks
                <select value={controls.queueSort} onChange={(e) => controls.setQueueSort(e.target.value as ReconciliationQueueSort)}>
                  <option value="default">Service order</option>
                  <option value="oldest">Oldest detected</option>
                  <option value="newest">Newest detected</option>
                  <option value="severity">Highest urgency</option>
                </select>
              </label>
              <Button type="button" variant="ghost" onClick={controls.clearQueueFilters}>Clear filters</Button>
            </div>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p role="status" className="text-xs text-muted-foreground">{visibleRows.length} of {reconciliation.rows.length} loaded breaks</p>
              <Button
                ref={toggleRef}
                type="button"
                variant="outline"
                aria-controls={reconciliation.detailPanelId}
                aria-expanded={detailOpen}
                className="reconciliation-detail-toggle"
                onClick={openInspector}
              >
                Inspect selected break
              </Button>
            </div>
            {selectedOutsideFilter ? <p role="status" className="text-sm text-muted-foreground">The selected break is outside these filters. Its evidence remains in the inspector.</p> : null}
            <div ref={queueRef} className="reconciliation-queue-scroll" onScroll={rememberQueueScroll}>
              <DenseDataTable
                columns={reconciliationBreakColumns}
                rows={visibleRows}
                getRowId={(item) => item.breakId}
                getRowAriaLabel={(item) => item.rowAriaLabel}
                getRowSelectAriaLabel={(item) => item.rowSelectAriaLabel}
                getRowAriaControls={(item) => item.detailPanelId}
                getRowAriaExpanded={(item) => item.isExpanded}
                onRowSelect={(item) => reconciliation.selectBreak(item.breakId)}
                selectedRowId={reconciliation.selectedBreakId}
                emptyText={reconciliation.rows.length ? "No breaks match these filters. Clear filters to review all loaded breaks." : reconciliation.emptyText}
                ariaLabel={reconciliation.tableLabel}
                caption={reconciliation.tableCaption}
              />
            </div>
          </CardContent>
        </Card>
      </WorkspaceWorkbenchShell>
    </section>
  );
}

function AccountingValue({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="break-words text-sm text-foreground">{value}</div>
    </div>
  );
}

export function filterReconciliationRows(rows: ReconciliationBreakRowViewModel[], controls: Pick<ReconciliationQueueControls, "queueSearch" | "queueStatus" | "queueSort">) {
  const query = controls.queueSearch.trim().toLocaleLowerCase();
  const filtered = rows.filter((row) => (
    (controls.queueStatus === "all" || row.status === controls.queueStatus)
    && (!query || [row.breakId, row.strategyName, row.financeLabel, row.ownerLabel, row.reason]
      .some((value) => value?.toLocaleLowerCase().includes(query)))
  ));
  if (controls.queueSort === "oldest" || controls.queueSort === "newest") {
    const direction = controls.queueSort === "oldest" ? 1 : -1;
    return filtered.sort((a, b) => direction * ((Date.parse(a.detectedAt) || 0) - (Date.parse(b.detectedAt) || 0)));
  }
  if (controls.queueSort === "severity") return filtered.sort((a, b) => urgencyRank(b) - urgencyRank(a));
  return filtered;
}
function urgencyRank(row: ReconciliationBreakRowViewModel) {
  const sla = row.slaState === "Breached" || row.slaBreachedAt ? 2 : row.slaState === "Warning" ? 1 : 0;
  const priority = row.priority ? { Low: 0, Normal: 1, High: 2, Critical: 3 }[row.priority] : -1;
  return sla * 10 + priority;
}

const reconciliationStatementRunColumns: DenseDataTableColumn<ReconciliationStatementRunRowViewModel>[] = [
  { id: "brokerCustodian", label: "Broker/Custodian", render: (row) => <span title={row.unavailableReason ?? undefined}>{row.brokerCustodianLabel}</span> },
  { id: "account", label: "Account", render: (row) => <span title={row.unavailableReason ?? undefined}>{row.accountLabel}</span> },
  { id: "period", label: "Period", render: (row) => <span className="font-mono text-muted-foreground" title={row.unavailableReason ?? undefined}>{row.periodLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusLabel === "Matched" || row.statusLabel === "Balanced" ? "success" : "warning"}>{row.statusLabel}</Badge> },
  { id: "validation", label: "Validation issues", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.validationIssueCountLabel}</span> },
  { id: "matches", label: "Matches", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.matchCountLabel}</span> },
  { id: "breaks", label: "Breaks", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.breakCountLabel}</span> },
  { id: "cases", label: "Cases", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.caseCountLabel}</span> },
  { id: "imported", label: "Imported", render: (row) => <span className="font-mono text-muted-foreground">{row.importedAtLabel}</span> }
];

export function ReconciliationStatementRuns({ reconciliation, initiallyOpen = false }: { reconciliation: StatementRuns; initiallyOpen?: boolean }) {
  return (
    <div className="reconciliation-run-context">
      <label className="reconciliation-run-picker">
        <span>Reconciliation run</span>
        <select value={reconciliation.reconciliationQueue.some((run) => run.runId === reconciliation.selectedRunId) ? reconciliation.selectedRunId ?? "" : ""} onChange={(event) => reconciliation.selectRun(event.target.value || null)}>
          <option value="">Choose a reconciliation run</option>
          {reconciliation.reconciliationQueue.map((run) => <option key={run.runId} value={run.runId}>{run.strategyName} · {run.openBreakCount} open</option>)}
        </select>
      </label>
      <TechnicalDetails label="Statement runs and import evidence" open={initiallyOpen}>
      <Card className="panel-surface xl:col-span-2">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <CardTitle className="flex items-center gap-2 text-base">
                <Landmark className="h-4 w-4 text-primary" aria-hidden="true" />
                {reconciliation.statementRunsView.title}
              </CardTitle>
              <CardDescription className="mt-2">{reconciliation.statementRunsView.description}</CardDescription>
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={reconciliation.statementRunsView.loadingText !== null}
              disabledReason={reconciliation.statementRunsView.loadingText ? "Statement run refresh is already in progress." : null}
              aria-label={reconciliation.statementRunsView.recoveryActionAriaLabel}
              onClick={reconciliation.refreshStatementRuns}
            >
              <RefreshCcw className="h-4 w-4" aria-hidden="true" />
              {reconciliation.statementRunsView.recoveryActionLabel}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <span className="sr-only" aria-live="polite">{reconciliation.statementRunsView.statusAnnouncement}</span>
          {reconciliation.statementRunsView.loadingText ? (
            <StatusBanner
              role="status"
              tone="info"
              title="Statement runs loading"
              detail={reconciliation.statementRunsView.loadingText}
            />
          ) : null}
          {reconciliation.statementRunsView.errorText ? (
            <StatusBanner
              role="alert"
              tone="danger"
              title={reconciliation.statementRunsView.errorText}
              detail={reconciliation.statementRunsView.errorDetails.length > 0 ? (
                <ul className="mt-2 list-disc pl-5">
                  {reconciliation.statementRunsView.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
                </ul>
              ) : null}
            />
          ) : null}
          <DenseDataTable
            columns={reconciliationStatementRunColumns}
            rows={reconciliation.statementRunsView.rows}
            getRowId={(row) => row.runId}
            getRowAriaLabel={(row) => row.ariaLabel}
            getRowSelectAriaLabel={(row) => row.selectAriaLabel}
            getRowAriaControls={(row) => row.controlsId}
            getRowAriaExpanded={(row) => row.isSelected}
            selectedRowId={reconciliation.selectedRunId}
            onRowSelect={(row) => reconciliation.selectRun(row.runId)}
            emptyText={reconciliation.statementRunsView.emptyText}
            ariaLabel={reconciliation.statementRunsView.tableLabel}
            caption={reconciliation.statementRunsView.tableCaption}
          />
          <StatementRunDetailTabs
            panelId={reconciliation.statementRunsView.detailPanelId}
            runId={reconciliation.selectedRunId}
            tabs={reconciliation.statementRunsView.tabs}
            onRunReconciled={() => void reconciliation.refreshStatementRuns()}
          />
          <p className="text-xs text-muted-foreground">
            Matching, tolerance, validation, and case-state decisions remain in reconciliation services; this view shows service-reviewed results.
          </p>
        </CardContent>
      </Card>
      </TechnicalDetails>
    </div>
  );
}
