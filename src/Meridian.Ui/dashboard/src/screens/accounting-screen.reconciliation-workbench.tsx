import { useMemo, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode, type UIEvent } from "react";
import { Link, useLocation } from "react-router-dom";
import { Landmark, Network, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogCloseButton, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Combobox } from "@/components/ui/combobox";
import { Kbd } from "@/components/ui/kbd";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { SectionDescription } from "@/components/ui/section-description";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { DetailRail } from "@/components/meridian/detail-rail";
import { DesignSystemStatus } from "@/design-system/status";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import { BREAK_QUEUE_KEYBOARD_MAP, resolveQueueShortcut } from "@/lib/queue-keyboard-map";
import {
  BREAK_QUEUE_VISIT_SURFACE,
  classifySinceLastVisit,
  describeSinceLastVisit,
  summarizeSinceLastVisit,
  useSinceLastVisit,
  type SinceLastVisitMark,
  type SurfaceVisitServices
} from "@/lib/since-last-visit";
import { ReconciliationCaseDiscussion } from "./accounting-screen.case-discussion";
import { cn } from "@/lib/utils";
import { ReconciliationBulkTriageBar, useReconciliationBulkTriage, type BulkTriageServices } from "./reconciliation-bulk-triage-bar";
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
import { Select } from "@/components/ui/select";

type ReconciliationViewModel = ReturnType<typeof useAccountingReconciliationViewModel>;
type Reconciliation = Pick<ReconciliationViewModel,
  | "rows"
  | "selectedBreakId"
  | "selectedDetail"
  | "selectBreak"
  | "assignBreak"
  | "beginBreakQueueUpdate"
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
> & Partial<Pick<ReconciliationViewModel, "applyCaseworkUpdate" | "sourceRows">>;
type StatementRuns = Pick<ReconciliationViewModel,
  "statementRunsView" | "refreshStatementRuns" | "selectedRunId" | "selectRun" | "reconciliationQueue"
>;
type ResolveDialog = ReturnType<typeof useReconciliationResolveDialogViewModel>;
export interface ReconciliationQueueControls {
  queueSearch: string;
  queueStatus: string;
  queueSort: string;
  /** Hidden break-queue columns, kept in the link (C4). */
  hiddenColumns?: string[];
  setHiddenColumns?: (hiddenColumns: string[]) => void;
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
  { id: "category", label: "Case", hideable: true, render: (row) => <span className="text-foreground">{row.financeLabel}</span> },
  {
    id: "variance",
    label: "Variance",
    numeric: true,
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
  { id: "owner", label: "Owner", hideable: true, render: (row) => <span className="font-mono text-muted-foreground">{row.ownerLabel}</span> },
  { id: "updated", label: "Updated", hideable: true, render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedAtLabel}</span> },
  { id: "source", label: "Source observation", hideable: true, render: (row) => <span className="block"><span className="block">{sourceObservationLabel(row.lineage)}</span><span className="block text-muted-foreground">{sourceObservationAge(row.lineage)}</span></span> },
  { id: "status", label: "Case status", render: (row) => <Badge variant={row.statusBadgeVariant}>{row.status}</Badge> }
];

export function ReconciliationWorkbench({ reconciliation, resolveDialog, runSelector, controls, selectionNotice, bulkServices, visitServices }: {
  reconciliation: Reconciliation;
  resolveDialog: ResolveDialog;
  runSelector?: ReactNode;
  controls: ReconciliationQueueControls;
  selectionNotice?: string | null;
  /** Bulk casework services; tests inject fakes. */
  bulkServices?: BulkTriageServices;
  /** Reads and records the operator's visit for "since your last visit" markers; tests inject fakes. */
  visitServices?: Partial<SurfaceVisitServices>;
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
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const triage = useReconciliationBulkTriage({
    visibleBreakIds: visibleRows.map((row) => row.breakId),
    beginItemsUpdate: reconciliation.beginBreakQueueUpdate,
    services: bulkServices
  });
  const { isSelected: isBulkSelected, toggle: toggleBulkSelected } = triage;
  // Since your last visit (C3): rows detected or updated after the operator's previous visit.
  const sinceLastVisit = useSinceLastVisit(BREAK_QUEUE_VISIT_SURFACE, visitServices);
  const visitBaseline = sinceLastVisit.baseline;
  const sinceLastVisitLine = describeSinceLastVisit(sinceLastVisit, summarizeSinceLastVisit(reconciliation.rows, visitBaseline));
  const withVisitMark = (row: ReconciliationBreakRowViewModel, label: string) => {
    const mark = classifySinceLastVisit(row, visitBaseline);
    return mark ? `${label}, ${mark} since your last visit` : label;
  };
  const breakColumns = useMemo<DenseDataTableColumn<ReconciliationBreakRowViewModel>[]>(() => [
    {
      id: "select",
      label: "",
      srLabel: "Bulk selection",
      render: (row) => (
        // The row selects on click; the checkbox only changes the bulk selection.
        <span onClick={(event) => event.stopPropagation()}>
          <Checkbox
            aria-label={`Add ${row.strategyName} ${row.financeLabel} to the bulk selection`}
            checked={isBulkSelected(row.breakId)}
            onCheckedChange={() => toggleBulkSelected(row.breakId)}
          />
        </span>
      )
    },
    ...reconciliationBreakColumns.map((column) => column.id !== "break" ? column : {
      ...column,
      render: (row: ReconciliationBreakRowViewModel) => {
        const mark = classifySinceLastVisit(row, visitBaseline);
        return (
          <span className="flex items-start gap-2">
            <span className="min-w-0 flex-1">{column.render(row)}</span>
            {mark ? <SinceLastVisitBadge mark={mark} /> : null}
          </span>
        );
      }
    })
  ], [isBulkSelected, toggleBulkSelected, visitBaseline]);
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

  function focusQueueRow(breakId: string) {
    requestAnimationFrame(() => {
      const rows = queueRef.current?.querySelectorAll<HTMLElement>("tr[data-dense-row-id]") ?? [];
      for (const row of rows) {
        if (row.dataset.denseRowId === breakId) {
          row.focus();
          return;
        }
      }
    });
  }

  function moveSelection(step: 1 | -1) {
    if (visibleRows.length === 0) return;
    const index = visibleRows.findIndex((row) => row.breakId === reconciliation.selectedBreakId);
    const nextIndex = index < 0 ? 0 : Math.min(visibleRows.length - 1, Math.max(0, index + step));
    const next = visibleRows[nextIndex];
    if (!next) return;
    reconciliation.selectBreak(next.breakId);
    focusQueueRow(next.breakId);
  }

  // Keyboard triage (C2). The capture phase claims these keys before a focused row's type-ahead.
  function handleQueueShortcut(event: KeyboardEvent<HTMLElement>): boolean {
    const action = resolveQueueShortcut(event);
    if (!action) return false;
    const current = selectedRow && visibleRows.some((row) => row.breakId === selectedRow.breakId) ? selectedRow : null;
    switch (action) {
      case "next":
        moveSelection(1);
        break;
      case "previous":
        moveSelection(-1);
        break;
      case "toggle-select":
        if (current) toggleBulkSelected(current.breakId);
        break;
      case "assign":
        if (triage.selectedIds.length > 0) triage.start("assign");
        else if (current?.canAssign) void reconciliation.assignBreak(current.breakId);
        break;
      case "evidence":
        if (current) openInspector();
        break;
      case "help":
        setShortcutsOpen(true);
        break;
    }
    event.preventDefault();
    event.stopPropagation();
    return true;
  }

  function handleWorkbenchKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (handleQueueShortcut(event)) return;
    const target = event.target as HTMLElement;
    if (!event.ctrlKey && !event.metaKey && !event.altKey && target.matches("tr[data-dense-row-id]")) {
      if (event.key === "j" || event.key === "k") {
        event.preventDefault();
        event.stopPropagation();
        // Reuse DenseDataTable's navigation so virtualization, focus and selection
        // follow the same path as arrow keys.
        target.dispatchEvent(new globalThis.KeyboardEvent("keydown", { key: event.key === "j" ? "ArrowDown" : "ArrowUp", bubbles: true }));
        return;
      }
      if (selectedRow && event.key === "a" && selectedRow.canAssign) {
        event.preventDefault(); event.stopPropagation();
        void reconciliation.assignBreak(selectedRow.breakId);
        return;
      }
      if (selectedRow && ((event.key === "r" && selectedRow.canResolve) || (event.key === "d" && selectedRow.canDismiss))) {
        event.preventDefault(); event.stopPropagation();
        resolveDialog.open(selectedRow.breakId, event.key === "r" ? "Resolved" : "Dismissed");
        openInspector();
        return;
      }
    }
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
          <DetailRail
            id={reconciliation.detailPanelId}
            ariaLabel={reconciliation.selectedDetail?.ariaLabel ?? "Selected reconciliation break"}
            className="reconciliation-inspector"
            title={reconciliation.selectedDetail?.title ?? reconciliation.detailEmptyTitle}
            status={reconciliation.selectedDetail ? <DesignSystemStatus status={reconciliation.selectedDetail.statusLabel}>{reconciliation.selectedDetail.statusLabel}</DesignSystemStatus> : undefined}
            actions={selectedRow ? renderBreakActions(selectedRow) : undefined}
            fields={selectedBreakPrimaryFields.map((field) => ({ ...field, numeric: field.label === "Variance" }))}
          >
            <Button type="button" variant="outline" className="reconciliation-back-to-queue" onClick={returnToQueue}>
              Back to break queue
            </Button>
            {reconciliation.selectedDetail ? <p className="mt-3 text-sm text-muted-foreground">{reconciliation.selectedDetail.description}</p> : <p role="status" aria-label={reconciliation.detailEmptyAriaLabel} className="text-sm text-muted-foreground">{reconciliation.detailEmptyText}</p>}
            {selectedRow ? <ReconciliationCaseDiscussion key={selectedRow.breakId} breakId={selectedRow.breakId} onUpdated={reconciliation.applyCaseworkUpdate} /> : null}
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
                <p className="text-sm">Review {resolveDialog.active.status === "Dismissed" ? "dismissal" : "resolution"} of 1 case: {activeResolveBreak.financeLabel} · {activeResolveBreak.strategyName}. The decision note is retained in case history.</p>
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
          </DetailRail>
        )}
      >
        <Card id="reconciliation-break-queue" role="region" aria-label="Reconciliation break checklist" className="panel-surface">
          <CardHeader className="flex flex-wrap items-center gap-2 px-3 py-1">
            <CardTitle>Reconciliation break queue</CardTitle>
            <SectionDescription descriptionKey="accounting:reconciliation-break-queue" label="About the reconciliation break queue">
              Review discrepancies, evidence, and permitted actions without leaving the queue.
            </SectionDescription>
          </CardHeader>
          <CardContent className="space-y-2 px-3 py-1">
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
                <Select value={controls.queueStatus} onChange={(e) => controls.setQueueStatus(e.target.value as ReconciliationQueueStatus)}>
                  {["all", "Open", "InReview", "Resolved", "Dismissed", "SignedOff"].map((status) => (
                    <option key={status} value={status}>
                      {status === "all" ? "All statuses" : status === "InReview" ? "In review" : status === "SignedOff" ? "Signed off" : status}
                    </option>
                  ))}
                </Select>
              </label>
              <label>
                Sort breaks
                <Select value={controls.queueSort} onChange={(e) => controls.setQueueSort(e.target.value as ReconciliationQueueSort)}>
                  <option value="default">Service order</option>
                  <option value="oldest">Oldest detected</option>
                  <option value="newest">Newest detected</option>
                  <option value="severity">Highest urgency</option>
                </Select>
              </label>
              <Button type="button" variant="ghost" onClick={controls.clearQueueFilters}>Clear filters</Button>
              <Button type="button" size="icon" variant="ghost" aria-label="Keyboard shortcuts" title="Keyboard shortcuts" aria-keyshortcuts="?" onClick={() => setShortcutsOpen(true)}>
                <Kbd keys="?" />
              </Button>
            </div>
            {triage.selectedIds.length > 0 || triage.draft || triage.message ? <ReconciliationBulkTriageBar triage={triage} visibleCount={visibleRows.length} /> : null}
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
            {selectedOutsideFilter ? <p role="status" className="text-sm text-muted-foreground">The selected break is outside these filters. Its evidence remains in the inspector.</p> : null}
            <div ref={queueRef} className="reconciliation-queue-scroll" onScroll={rememberQueueScroll}>
              <TableWorkbench
                compactToolbar
                workbenchId="accounting-reconciliation-breaks"
                sourceKey={reconciliation.sourceRows?.map((item) => `${item.tenantId}:${item.companyId}:${item.fundAccountId}:${item.runId}`).filter((item, index, values) => values.indexOf(item) === index).sort().join("|") ?? "unverified"}
                sourceDescription="Authorized reconciliation casework queue for the selected scope. Variance units and currency are retained only where supplied by the casework source."
                completePopulation={Boolean(reconciliation.sourceRows) && !reconciliation.loadingText && !reconciliation.errorText}
                filterState={{ search: controls.queueSearch, status: controls.queueStatus, sort: controls.queueSort }}
                onRestoreFilters={(filters) => {
                  controls.setQueueSearch(filters.search ?? "");
                  if (["all", "Open", "InReview", "Resolved", "Dismissed", "SignedOff"].includes(filters.status ?? "all")) controls.setQueueStatus((filters.status ?? "all") as ReconciliationQueueStatus);
                  if (["default", "newest", "oldest", "severity"].includes(filters.sort ?? "default")) controls.setQueueSort((filters.sort ?? "default") as ReconciliationQueueSort);
                }}
                hiddenColumnIds={controls.hiddenColumns}
                onHiddenColumnIdsChange={controls.setHiddenColumns}
                columns={breakColumns.map((column) => column.id === "select" ? column : ({ ...column, exportValue: (row: ReconciliationBreakRowViewModel) => {
                  const source = reconciliation.sourceRows?.find((item) => item.breakId === row.breakId);
                  switch (column.id) {
                    case "break": return source?.strategyName ?? row.strategyName;
                    case "category": return source?.category ?? row.financeLabel;
                    case "variance": return source?.variance ?? null;
                    case "owner": return source?.assigneeDisplayName ?? source?.assignedTo ?? null;
                    case "updated": return source?.lastUpdatedAt ?? null;
                    case "source": return source?.lineage?.observationState ?? null;
                    case "status": return source?.status ?? row.status;
                    default: return null;
                  }
                } }))}
                rows={visibleRows}
                getRowId={(item) => item.breakId}
                getRowAriaLabel={(item) => withVisitMark(item, item.rowAriaLabel)}
                getRowSelectAriaLabel={(item) => withVisitMark(item, item.rowSelectAriaLabel)}
                getRowAriaControls={(item) => item.detailPanelId}
                getRowAriaExpanded={(item) => item.isExpanded}
                onRowSelect={(item) => reconciliation.selectBreak(item.breakId)}
                selectedRowId={reconciliation.selectedBreakId}
                emptyText={reconciliation.rows.length ? "No breaks match these filters. Clear filters to review all loaded breaks." : reconciliation.emptyText}
                ariaLabel={reconciliation.tableLabel}
                caption={reconciliation.tableCaption}
              />
            </div>
            {triage.selectedIds.length === 0 && !triage.draft && !triage.message ? <ReconciliationBulkTriageBar triage={triage} visibleCount={visibleRows.length} /> : null}
            <div className="space-y-0.5">
              <p role="status" className="text-xs text-muted-foreground">{visibleRows.length} of {reconciliation.rows.length} loaded breaks</p>
              {sinceLastVisitLine ? <p className="text-xs text-muted-foreground">{sinceLastVisitLine}</p> : null}
            </div>
          </CardContent>
        </Card>
      </WorkspaceWorkbenchShell>
      <Dialog open={shortcutsOpen} onOpenChange={setShortcutsOpen}>
        <DialogContent aria-labelledby="break-queue-shortcuts-title" aria-describedby="break-queue-shortcuts-description" className="max-w-md">
          <DialogCloseButton label="Close keyboard shortcuts" onClick={() => setShortcutsOpen(false)} />
          <DialogHeader>
            <DialogTitle id="break-queue-shortcuts-title">Break queue shortcuts</DialogTitle>
            <DialogDescription id="break-queue-shortcuts-description">
              These keys work while focus is in the break queue, outside text fields. Actions follow the case
              permissions and require the same rationale as the buttons.
            </DialogDescription>
          </DialogHeader>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            {BREAK_QUEUE_KEYBOARD_MAP.map((shortcut) => (
              <div key={shortcut.key} className="contents">
                <dt><Kbd keys={[shortcut.key]} /></dt>
                <dd>{shortcut.description}</dd>
              </div>
            ))}
            <div className="contents">
              <dt><Kbd keys={["Enter"]} /></dt>
              <dd>Open the current break</dd>
            </div>
            <div className="contents">
              <dt><Kbd keys={["Esc"]} /></dt>
              <dd>Return from the drawer to the queue</dd>
            </div>
          </dl>
        </DialogContent>
      </Dialog>
    </section>
  );
}

function SinceLastVisitBadge({ mark }: { mark: SinceLastVisitMark }) {
  return (
    <Badge variant="outline" className="shrink-0">
      {mark === "new" ? "New" : "Updated"}
      <span className="sr-only"> since your last visit</span>
    </Badge>
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
  { id: "validation", label: "Validation issues", numeric: true, render: (row) => <span className="font-mono tabular-nums">{row.validationIssueCountLabel}</span> },
  { id: "matches", label: "Matches", numeric: true, render: (row) => <span className="font-mono tabular-nums">{row.matchCountLabel}</span> },
  { id: "breaks", label: "Breaks", numeric: true, render: (row) => <span className="font-mono tabular-nums">{row.breakCountLabel}</span> },
  { id: "cases", label: "Cases", numeric: true, render: (row) => <span className="font-mono tabular-nums">{row.caseCountLabel}</span> },
  { id: "imported", label: "Imported", render: (row) => <span className="font-mono text-muted-foreground">{row.importedAtLabel}</span> }
];

export function ReconciliationStatementRuns({ reconciliation, initiallyOpen = false }: { reconciliation: StatementRuns; initiallyOpen?: boolean }) {
  return (
    <div className="reconciliation-run-context">
      <Combobox
        id="reconciliation-run-picker"
        label="Reconciliation run"
        required
        className="max-w-lg"
        value={reconciliation.selectedRunId ?? ""}
        options={[
          { value: "", label: "Choose a reconciliation run" },
          ...reconciliation.reconciliationQueue.map((run) => ({
            value: run.runId,
            label: `${run.strategyName} · ${run.openBreakCount} open`
          }))
        ]}
        onChange={(value) => reconciliation.selectRun(value || null)}
        placeholder="Search reconciliation runs…"
        emptyText="No matching reconciliation runs"
      />
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
