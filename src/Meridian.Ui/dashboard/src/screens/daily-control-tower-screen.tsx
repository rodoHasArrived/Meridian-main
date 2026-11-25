import { QUEUE_KEY_BINDINGS } from "@/lib/keymap";
import { DueChip } from "@/components/meridian/due-chip";
import { StatStrip } from "@/components/meridian/stat-strip";
import { WORKSPACE_KPI_IDS } from "@/lib/kpi-registry";
import { useState } from "react";
import { ArrowRight } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import type {
  AppShellAttentionState,
  AppShellTrustStripState,
  AppShellWorkflowContinuityViewModel
} from "@/app-shell.view-model";
import { updateWorkstationSearch } from "@/lib/workstation-close-routing";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DetailRail } from "@/components/meridian/detail-rail";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { buildAttentionSignalFact } from "@/components/meridian/attention-signal-fact";
import { StatusRow } from "@/components/meridian/status-row";
import {
  OperatorTriageActionDialog, OperatorTriageDetailRail, OperatorTriageInboxActions, OperatorTriageInboxError,
  OperatorTriageInboxFilters, OperatorTriageNotices, restoreTriageFilters, TriageAge, TriageAssignCheckbox,
  triageSelection, TriageSeverityStatus, triageStateExportValue, triageStateLabel
} from "@/components/meridian/operator-triage-queue";
import { useScreenPaletteActions } from "@/components/meridian/command-palette.actions";
import { useOperatorTriageInbox, type OperatorTriageInboxState } from "@/components/meridian/use-operator-triage-inbox";
import { ATTENTION_COUNT_RULE, countAttention, needsAttention } from "@/lib/attention-items";
import { useQueueKeyboard } from "@/lib/use-queue-keyboard";
import { SeverityBadge } from "@/components/operations";
import {
  buildDailyControlTowerModel, buildTodayQueueRows, describeTodayQueueRank, todayQueueRowKey, todayRowVisitTimes, type TodayQueueRow
} from "@/lib/daily-control-tower";
import { routeForTriageItem } from "@/lib/operator-inbox-triage";
import { eyebrowClassName } from "@/design-system/label-case";
import { pluralizeCount, VALUE_STATE_LABELS } from "@/lib/format";
import { toSeverity } from "@/design-system/status-styles";
import { SegmentedControl } from "@/components/ui/segmented-control";
import { SinceLastVisitBadge } from "@/components/meridian/since-last-visit-badge";
import {
  classifySinceLastVisit, describeSinceLastVisit, summarizeSinceLastVisit, TODAY_QUEUE_VISIT_SURFACE, useSinceLastVisit,
  type SurfaceVisitServices
} from "@/lib/since-last-visit";

/** Which source the one Today queue shows (UX consolidation P1). */
export type TodayQueueSource = "all" | "workspace" | "inbox";

export interface DailyControlTowerScreenProps {
  viewModel: AppShellWorkflowContinuityViewModel;
  trustStrip: AppShellTrustStripState;
  /** The shell attention state the masthead pill shows; Today lists every signal behind it. */
  attention: AppShellAttentionState;
  /**
   * The shell's shared-inbox state, so Today triages the same inbox the sidebar badges count.
   * Omit it to let Today read the inbox itself (a standalone render).
   */
  triage?: OperatorTriageInboxState;
  refreshing?: boolean;
  /** Test seam for the per-viewer visit record behind "new since you last looked". */
  visitServices?: Partial<SurfaceVisitServices>;
}

type QueueSelection = { source: "workspace"; id: string } | { source: "inbox" } | null;

const EVIDENCE_PANEL_ID = "daily-control-tower-evidence-detail";
const TRIAGE_PANEL_ID = "daily-control-tower-triage-detail";

export function DailyControlTowerScreen(props: DailyControlTowerScreenProps) {
  return props.triage
    ? <DailyControlTowerQueue {...props} triage={props.triage} />
    : <DailyControlTowerWithOwnInbox {...props} />;
}

function DailyControlTowerWithOwnInbox(props: DailyControlTowerScreenProps) {
  const triage = useOperatorTriageInbox({
    fundAccountId: props.viewModel.operatingScope.fundAccountId,
    refreshKey: props.refreshing ?? false
  });
  return <DailyControlTowerQueue {...props} triage={triage} />;
}

function DailyControlTowerQueue({
  viewModel,
  trustStrip,
  attention,
  triage,
  visitServices
}: DailyControlTowerScreenProps & { triage: OperatorTriageInboxState }) {
  const model = buildDailyControlTowerModel(viewModel, trustStrip);
  const fundAccountId = viewModel.operatingScope.fundAccountId;
  // The shared inbox's read, filters and in-flight / unconfirmed command live above the Source
  // filter (in the shell), so filtering inbox rows out of the grid never drops a pending command.
  const location = useLocation();
  const navigate = useNavigate();
  const sourceQuery = new URLSearchParams(location.search).get("source");
  const queueSource: TodayQueueSource = sourceQuery === "workspace" || sourceQuery === "inbox" ? sourceQuery : "all";
  const setQueueSource = (value: TodayQueueSource) => navigate({ search: updateWorkstationSearch(location.search, "source", value === "all" ? null : value), hash: location.hash });
  // Triage-in-place: the operator inspects any row without leaving Today. A workspace signal
  // shows its evidence; an inbox item shows its triage actions. Falls back to the leading
  // decision's row (or the first visible row) until one is chosen or when it leaves the grid.
  const [selection, setSelection] = useState<QueueSelection>(null);
  const paletteUnavailable = !triage.actionsAvailable ? "Durable triage is unavailable." : null;
  const paletteAcknowledgeable = triage.bulkEligibility("BulkAcknowledge").filter((entry) => entry.eligible).length;
  const paletteSnoozable = triage.bulkEligibility("BulkSnooze").filter((entry) => entry.eligible).length;
  useScreenPaletteActions("today", [
    {
      id: "today.bulk-acknowledge",
      verbLabel: "Acknowledge selected inbox items",
      description: "Opens the acknowledge confirmation for the checked Today items.",
      keywords: ["today", "triage", "acknowledge"],
      disabled: paletteUnavailable !== null || paletteAcknowledgeable === 0,
      disabledReason: paletteUnavailable ?? (paletteAcknowledgeable === 0 ? "No selected item can be acknowledged." : null),
      run: () => triage.openAction("BulkAcknowledge")
    },
    {
      id: "today.bulk-snooze",
      verbLabel: "Snooze selected inbox items",
      description: "Opens the snooze confirmation for the checked Today items.",
      keywords: ["today", "triage", "snooze"],
      disabled: paletteUnavailable !== null || paletteSnoozable === 0,
      disabledReason: paletteUnavailable ?? (paletteSnoozable === 0 ? "No selected item can be snoozed." : null),
      run: () => triage.openAction("BulkSnooze")
    }
  ]);

  const search = triage.search.trim().toLowerCase();
  // Workspace signals have no owner, so "Assigned to me" leaves them out (and says so).
  const workspaceRows = triage.mineOnly ? [] : search
    ? model.queueRows.filter((row) => `${row.item.label} ${row.item.detail} ${row.item.workspaceLabel}`.toLowerCase().includes(search))
    : model.queueRows;
  // The default view is the count rule itself, so the rows on screen are the rows the sidebar
  // badges and the Source filter count. "All items" adds informational and snoozed rows;
  // workspace signals cannot be snoozed, so "Snoozed items" lists inbox rows only.
  const queueRows = buildTodayQueueRows(workspaceRows, triage.rows, triage.inbox?.items ?? null)
    .filter((row) => triage.visibility === "all" || (triage.visibility === "snoozed" ? row.snoozed : needsAttention(row)))
    .filter((row) => queueSource === "all" || row.source === queueSource);
  const selectedKey = selection?.source === "workspace"
    ? todayQueueRowKey("workspace", selection.id)
    : selection?.source === "inbox" && triage.selectedId ? todayQueueRowKey("inbox", triage.selectedId) : null;
  const leadingKey = model.leadingRow ? todayQueueRowKey("workspace", model.leadingRow.item.id) : null;
  const selectedRow = queueRows.find((row) => row.key === selectedKey)
    ?? queueRows.find((row) => row.key === leadingKey)
    ?? queueRows[0]
    ?? null;
  const selectedInboxItem = selectedRow?.source === "inbox" ? selectedRow.item : null;
  const rankReason = selectedRow ? describeTodayQueueRank(queueRows, selectedRow.key) : null;
  // New since you last looked (UX Phase E): rows that appeared or changed after this viewer's
  // previous visit to Today, kept per operator by the server.
  const sinceLastVisit = useSinceLastVisit(TODAY_QUEUE_VISIT_SURFACE, visitServices);
  const visitBaseline = sinceLastVisit.baseline;
  const sinceLastVisitLine = describeSinceLastVisit(sinceLastVisit, summarizeSinceLastVisit(queueRows.map(todayRowVisitTimes), visitBaseline), "Items");

  const selectRow = (row: TodayQueueRow) => {
    if (row.source === "inbox") {
      triage.setSelectedId(row.id);
      triage.closeAction();
      setSelection({ source: "inbox" });
    } else {
      setSelection({ source: "workspace", id: row.id });
    }
  };

  // The attention list before any filter, counted by the one rule: the same list and the same
  // rule as the sidebar badges and the masthead pill.
  const allRows = buildTodayQueueRows(model.queueRows, triage.inbox?.items ?? []);
  const attentionCounts = countAttention(allRows);
  const otherItemCount = allRows.length - attentionCounts.total;
  const inboxState = triage.inbox ? "ready" : triage.error ? "unavailable" : "loading";
  const showsInbox = queueSource !== "workspace";

  const queueColumns: TableWorkbenchColumn<TodayQueueRow>[] = [
    {
      id: "select",
      label: "",
      srLabel: "Select inbox item for bulk actions",
      render: (row) => row.source === "inbox" ? <TriageAssignCheckbox triage={triage} item={row.item} /> : null
    },
    {
      id: "item",
      label: "Item",
      exportValue: (row) => row.label,
      // One line per item: severity first, then the title. The description is in the detail rail.
      render: (row) => (
        <div className="flex min-w-0 max-w-[32rem] items-center gap-2">
          <span className="shrink-0">
            {row.source === "workspace"
              ? <SeverityBadge status={toSeverity(row.focus.badgeVariant)} label={row.focus.statusLabel} />
              : <TriageSeverityStatus item={row.item} />}
          </span>
          <span className="truncate font-medium text-foreground" title={`${row.label}. ${row.detail}`}>{row.label}</span>
          {(() => { const mark = classifySinceLastVisit(todayRowVisitTimes(row), visitBaseline); return mark ? <SinceLastVisitBadge mark={mark} /> : null; })()}
        </div>
      )
    },
    { id: "source", label: "Source", priority: "medium", exportValue: (row) => row.sourceLabel, render: (row) => row.sourceLabel },
    { id: "workspace", label: "Workspace", priority: "low", exportValue: (row) => row.workspaceLabel, render: (row) => row.workspaceLabel },
    {
      id: "owner",
      label: "Owner",
      priority: "low",
      exportValue: (row) => row.owner,
      hideWhenEmpty: (row) => !row.owner,
      emptyNote: "Owner: no item is assigned",
      render: (row) => row.owner ?? (row.source === "inbox" ? "Unassigned" : "Not tracked")
    },
    {
      id: "age",
      label: "Age",
      priority: "medium",
      className: "whitespace-nowrap tabular-nums",
      exportValue: (row) => row.observedAt,
      hideWhenEmpty: (row) => !row.observedAt,
      emptyNote: "Age: no item has an observed time",
      // A single missing age is a dash with its reason on hover, not "Age unavailable" per row.
      render: (row) => row.observedAt ? <TriageAge value={row.observedAt} /> : <span title="No observed time is linked to this item" aria-label="No observed time">{VALUE_STATE_LABELS.unavailable}</span>
    },
    {
      id: "due",
      label: "Due",
      className: "whitespace-nowrap tabular-nums",
      exportValue: (row) => row.dueAt,
      hideWhenEmpty: (row) => !row.dueAt,
      emptyNote: "Due: no item has a due time",
      render: (row) => <DueChip dueAt={row.dueAt} />
    },
    {
      id: "state",
      label: "Triage",
      priority: "low",
      exportValue: (row) => row.source === "inbox" ? triageStateExportValue(row.item) : "Signal only",
      render: (row) => row.source === "inbox" ? triageStateLabel(row.item) : "Signal only"
    },
    {
      id: "action",
      label: "Next action",
      render: (row) => <QueueRowAction row={row} />
    },
    {
      id: "output",
      label: "Affected output",
      hideable: true,
      defaultHidden: true,
      exportValue: (row) => row.source === "workspace" ? row.focus.outputLabel : null,
      render: (row) => row.source === "workspace" ? row.focus.outputLabel : "See source workspace"
    },
    {
      id: "evidence",
      label: "Evidence",
      hideable: true,
      defaultHidden: true,
      exportValue: (row) => row.source === "workspace" ? row.focus.proof?.label ?? null : null,
      render: (row) => row.source === "workspace" ? row.focus.proof?.label ?? "Evidence not linked" : "Triage activity in detail"
    }
  ];

  const queueKeyboard = useQueueKeyboard({
    rows: queueRows,
    selectedId: selectedRow?.key ?? null,
    getId: (row: TodayQueueRow) => row.key,
    onSelect: selectRow,
    // Inbox actions act on the selected shared-inbox item; each opens its reviewed dialog.
    actions: [
      { key: QUEUE_KEY_BINDINGS.assign.keys, label: "Assign", enabled: triage.capability(selectedInboxItem, "canAssign"), run: () => triage.openAction("Assign", selectedInboxItem) },
      { key: QUEUE_KEY_BINDINGS.acknowledge.keys, label: "Acknowledge", enabled: triage.capability(selectedInboxItem, "canAcknowledge"), run: () => triage.openAction("Acknowledge", selectedInboxItem) },
      { key: QUEUE_KEY_BINDINGS.snooze.keys, label: "Snooze", enabled: triage.capability(selectedInboxItem, "canSnooze"), run: () => triage.openAction("Snooze", selectedInboxItem) }
    ]
  });

  const otherItemsText = otherItemCount > 0
    ? ` ${pluralizeCount(otherItemCount, "other item")} (informational or snoozed) ${otherItemCount === 1 ? "is" : "are"} listed under All items.`
    : "";
  const emptyText = queueSource === "inbox" && !triage.inbox
    ? triage.error ? "The shared inbox is unavailable." : "The shared inbox is loading."
    : search || triage.owner !== "all" || triage.visibility !== "active"
      ? queueSource === "workspace" ? "No workspace signals match these filters."
        : queueSource === "inbox"
          ? triage.owner === "mine" ? "No shared inbox items are assigned to you in these filters."
            : triage.owner === "unassigned" ? "No unassigned shared inbox items match these filters." : "No shared inbox items match these filters."
          : "No queue items match these filters."
      : queueSource === "workspace" ? `${model.emptyQueueText}${otherItemsText}`
        : queueSource === "inbox" ? `No shared inbox items need attention.${otherItemsText}`
          : `Nothing needs attention.${otherItemsText}`;

  return (
    <div className="space-y-2">
      <h2 className="sr-only">What needs an operator decision now</h2>
      <StatStrip ids={WORKSPACE_KPI_IDS.today} label="Today headline metrics" compact />
      <section aria-label="Priority decision">
        {/* The masthead and Today share one attention object; retain every signal and its
            recovery path while showing only open issues in the collapsed row. */}
        <StatusRow
          label="Workstation status"
          clearText="All clear: no open issues"
          facts={attention.signals.map((signal) => buildAttentionSignalFact(signal, viewModel.operatingScope))}
          action={
            <>
              <Button asChild variant="outline" size="sm">
                <Link to={model.nextActionHref} aria-label={model.nextActionAriaLabel}>
                  {model.nextActionLabel}<ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                </Link>
              </Button>
            </>
          }
        />
      </section>

      <section aria-label="Daily control tower confidence" className="space-y-2">
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 text-xs">
          {showsInbox && triage.inbox ? (
            <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Inbox filters" aria-busy={triage.loading || triage.busy}>
              <span className="sr-only">{fundAccountId ? "Account filter applied" : "All authorized accounts"}</span>
              {/* A seeded preview inbox is read-only by design, not a fault: its triage buttons are
                  simply disabled. Only a real loss of durable triage interrupts the operator. */}
              {!triage.inbox.persistenceAvailable && !SEEDED_INBOX_REASON.test(triage.inbox.unavailableReason ?? "") ? (
                <span role="alert" className="text-xs text-warning">
                  {triage.inbox.unavailableReason ?? "Durable triage is unavailable. Actions are disabled."}
                </span>
              ) : null}
              <OperatorTriageInboxActions />
            </div>
          ) : null}
        </div>
        {showsInbox && triage.loading ? <p role="status" className="text-xs text-muted-foreground">Refreshing authorized inbox…</p> : null}
        {showsInbox && triage.error ? <OperatorTriageInboxError triage={triage} unaffectedText="Workspace signals in this queue are unaffected" /> : null}
      </section>

      {/* One queue grid over the one attention list (UX consolidation P1). The count beside the
          heading, the Source filter counts, the rows in the default view, the sidebar badges and
          the masthead pill are all this list counted by the one rule, which is stated here once. */}
      <section aria-labelledby="today-queue-heading" className="space-y-2" data-today-inbox-state={inboxState}>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
            <h3 id="today-queue-heading" className="text-sm font-semibold">Queue</h3>
            <p className="text-xs text-muted-foreground" data-testid="today-attention-count">
              {attentionCounts.total} need{attentionCounts.total === 1 ? "s" : ""} attention · {ATTENTION_COUNT_RULE}
              {triage.inbox ? "" : triage.error ? " · shared inbox unavailable" : " · shared inbox loading"}
            </p>
          </div>
          <SegmentedControl
            aria-label="Queue source"
            size="sm"
            value={queueSource}
            onChange={(value) => setQueueSource(value as TodayQueueSource)}
            options={[
              { value: "all", label: "All", count: attentionCounts.total },
              { value: "workspace", label: "Workspace signals", count: attentionCounts.bySource.workspace },
              { value: "inbox", label: "Shared inbox", count: triage.inbox ? attentionCounts.bySource.inbox : undefined }
            ]}
          />
        </div>

        {sinceLastVisitLine ? <p className="text-xs text-muted-foreground" data-testid="today-since-last-visit">{sinceLastVisitLine}</p> : null}
        {/* Command outcome and unconfirmed-command recovery stay visible under every Source filter. */}
        <OperatorTriageNotices triage={triage} showPersistence={false} />

        {triage.mineOnly && queueSource !== "inbox" && model.queueRows.length > 0 ? (
          <p role="status" className="text-xs text-muted-foreground">Workspace signals have no owner, so Assigned to me hides them.</p>
        ) : null}
        <div onKeyDownCapture={queueKeyboard} className="today-queue grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
          <TableWorkbench
            compactToolbar
            workbenchId="operator-inbox"
            sourceKey={`${triage.inbox?.actorId ?? "pending"}:${fundAccountId ?? "all"}`}
            completePopulation={Boolean(triage.inbox && !triage.loading && !triage.error)}
            sourceDescription={`Today queue: workspace signals from the workstation data and ${triage.inbox ? `the shared inbox as of ${triage.inbox.asOf}` : "no loaded shared inbox"}. Counts are ${ATTENTION_COUNT_RULE}.`}
            searchText={triage.search}
            onSearchTextChange={triage.setSearch}
            filterState={{ source: queueSource, visibility: triage.visibility, owner: triage.owner }}
            onRestoreFilters={(filters) => {
              restoreTriageFilters(triage, filters);
              setQueueSource(filters.source === "workspace" || filters.source === "inbox" ? filters.source : "all");
            }}
            filters={<OperatorTriageInboxFilters triage={triage} visibilityLabel="Show" activeLabel="Needs attention"
              mineLabel="Assigned to me" showMine={Boolean(triage.inbox)} />}
            selection={showsInbox && triage.inbox ? triageSelection(triage) : undefined}
            tableId="daily-control-tower-queue"
            columns={queueColumns}
            rows={queueRows}
            getRowId={(row) => row.key}
            getRowAriaLabel={(row) => `${row.label}. ${row.sourceLabel}. ${row.source === "workspace" ? row.focus.statusLabel : row.item.tone}. ${row.detail}`}
            getRowSelectAriaLabel={(row) => row.label}
            getRowAriaControls={(row) => row.source === "workspace" ? EVIDENCE_PANEL_ID : TRIAGE_PANEL_ID}
            getRowAriaExpanded={(row) => row.key === selectedRow?.key}
            onRowSelect={selectRow}
            selectedRowId={selectedRow?.key ?? null}
            emptyText={emptyText}
            ariaLabel="Today queue"
            maxVisibleRows={null}
          />
          {selectedRow ? (
            <div className="min-w-0 space-y-2">
              {rankReason ? <p className="text-xs leading-5 text-muted-foreground" data-testid="today-rank-reason"><span className="font-medium text-foreground">Why ranked here: </span>{rankReason}</p> : null}
              {selectedRow.source === "inbox"
                ? <OperatorTriageDetailRail id={TRIAGE_PANEL_ID} triage={triage} item={selectedRow.item} />
                : <WorkspaceEvidenceRail row={selectedRow} />}
            </div>
          ) : null}
        </div>
        <OperatorTriageActionDialog triage={triage} />
      </section>
    </div>
  );
}

function QueueRowAction({ row }: { row: TodayQueueRow }) {
  const linkClassName = "inline-flex items-center gap-1 font-medium text-primary hover:underline focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40";
  if (row.source === "workspace") {
    return (
      <Link to={row.focus.item.route} className={linkClassName} aria-label={row.focus.item.ariaLabel}>
        <span>{row.focus.item.actionLabel}</span>
        <ArrowRight className="h-3 w-3" aria-hidden="true" />
      </Link>
    );
  }
  const route = routeForTriageItem(row.item);
  return route ? (
    <Link to={route} className={linkClassName} aria-label={`Open ${row.label} in its workspace`}>
      <span>Open in workspace</span>
      <ArrowRight className="h-3 w-3" aria-hidden="true" />
    </Link>
  ) : <span className="text-xs text-muted-foreground">Route unavailable</span>;
}

function WorkspaceEvidenceRail({ row }: { row: Extract<TodayQueueRow, { source: "workspace" }> }) {
  const { focus } = row;
  return (
    <DetailRail
      id={EVIDENCE_PANEL_ID}
      ariaLabel={`${focus.item.label} evidence summary`}
      title={focus.item.label}
      fields={focus.proofPassportItems.slice(0, 4).map((passportItem) => ({ label: passportItem.label,
        value: <><span>{passportItem.value}</span><p className="mt-1 text-xs leading-5 text-muted-foreground">{passportItem.detail}</p></> }))}
    >
      <p className="mt-3 text-sm leading-6 text-foreground">{row.detail}</p>
      <p className="my-3 text-sm leading-6 text-muted-foreground">{focus.proofPassportSummary}</p>
      {focus.proofPassportItems.length > 4 ? (
        <TechnicalDetails
          label="More evidence"
          description="Downstream usage, blockers, evidence packets, and audit trail for the selected queue item."
        >
          <dl className="grid gap-2">
            {focus.proofPassportItems.slice(4).map((passportItem) => (
              <div key={passportItem.id} className="rounded border border-border bg-card p-2">
                <dt className={eyebrowClassName(passportItem.label)}>{passportItem.label}</dt>
                <dd className="mt-1 text-xs font-medium leading-5 text-foreground">{passportItem.value}</dd>
                <dd className="mt-1 text-xs leading-5 text-muted-foreground">{passportItem.detail}</dd>
              </div>
            ))}
          </dl>
        </TechnicalDetails>
      ) : null}
    </DetailRail>
  );
}

const SEEDED_INBOX_REASON = /^Seeded read-only inbox\./i;
