import { StatStrip } from "@/components/meridian/stat-strip";
import { WORKSPACE_KPI_IDS } from "@/lib/kpi-registry";
import { useState } from "react";
import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import type {
  AppShellAttentionState,
  AppShellTrustStripState,
  AppShellWorkflowContinuityViewModel
} from "@/app-shell.view-model";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DetailRail } from "@/components/meridian/detail-rail";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { buildAttentionSignalFact } from "@/components/meridian/attention-signal-fact";
import { StatusRow } from "@/components/meridian/status-row";
import type { OperationalTrustTone } from "@/components/meridian/operational-trust-summary";
import {
  OperatorTriageActionDialog, OperatorTriageDetailRail, OperatorTriageInboxActions, OperatorTriageInboxError,
  OperatorTriageInboxFilters, OperatorTriageNotices, restoreTriageFilters, TriageAge, TriageAssignCheckbox,
  triageDateLabel, TriageSeverityStatus, triageStateExportValue, triageStateLabel
} from "@/components/meridian/operator-triage-queue";
import { useOperatorTriageInbox } from "@/components/meridian/use-operator-triage-inbox";
import { useQueueKeyboard } from "@/lib/use-queue-keyboard";
import { SeverityBadge } from "@/components/operations";
import {
  buildDailyControlTowerModel, buildTodayQueueRows, todayQueueRowKey, type TodayQueueRow
} from "@/lib/daily-control-tower";
import { routeForTriageItem } from "@/lib/operator-inbox-triage";
import { eyebrowClassName } from "@/design-system/label-case";
import { formatTimestamp, pluralizeCount } from "@/lib/format";
import { toSeverity } from "@/design-system/status-styles";
import { SegmentedControl } from "@/components/ui/segmented-control";

/** Which source the one Today queue shows (UX consolidation P1). */
export type TodayQueueSource = "all" | "workspace" | "inbox";

export interface DailyControlTowerScreenProps {
  viewModel: AppShellWorkflowContinuityViewModel;
  trustStrip: AppShellTrustStripState;
  /** The shell attention state the masthead pill shows; Today lists every signal behind it. */
  attention: AppShellAttentionState;
  onEditOperatingScope?: () => void;
  onRefresh?: () => void;
  refreshing?: boolean;
}

type QueueSelection = { source: "workspace"; id: string } | { source: "inbox" } | null;

const EVIDENCE_PANEL_ID = "daily-control-tower-evidence-detail";
const TRIAGE_PANEL_ID = "daily-control-tower-triage-detail";

export function DailyControlTowerScreen({
  viewModel,
  trustStrip,
  attention,
  onEditOperatingScope,
  onRefresh,
  refreshing = false
}: DailyControlTowerScreenProps) {
  const model = buildDailyControlTowerModel(viewModel, trustStrip);
  const fundAccountId = viewModel.operatingScope.fundAccountId;
  // The shared inbox's read, filters and in-flight / unconfirmed command live here, above the
  // Source filter, so filtering inbox rows out of the grid never drops a pending command.
  const triage = useOperatorTriageInbox({ fundAccountId, refreshKey: refreshing });
  const [queueSource, setQueueSource] = useState<TodayQueueSource>("all");
  // Triage-in-place: the operator inspects any row without leaving Today. A workspace signal
  // shows its evidence; an inbox item shows its triage actions. Falls back to the leading
  // decision's row (or the first visible row) until one is chosen or when it leaves the grid.
  const [selection, setSelection] = useState<QueueSelection>(null);

  const search = triage.search.trim().toLowerCase();
  const workspaceRows = search
    ? model.queueRows.filter((row) => `${row.item.label} ${row.item.detail} ${row.item.workspaceLabel}`.toLowerCase().includes(search))
    : model.queueRows;
  const queueRows = buildTodayQueueRows(workspaceRows, triage.rows)
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
  const freshness = selectedRow?.source === "inbox"
    ? buildTowerFreshness(selectedRow.item.createdAt, "Observed time of the selected shared inbox item.")
    : buildTowerFreshness(selectedRow?.focus.proof?.timestampIso,
      selectedRow?.focus.proofPassportItems.find((item) => item.id === "freshness")?.detail);

  const selectRow = (row: TodayQueueRow) => {
    if (row.source === "inbox") {
      triage.setSelectedId(row.id);
      triage.closeAction();
      setSelection({ source: "inbox" });
    } else {
      setSelection({ source: "workspace", id: row.id });
    }
  };

  const workspaceCount = model.queueRows.length;
  const inboxCount = triage.inbox ? triage.inbox.items.length : null;
  const inboxState = triage.inbox ? "ready" : triage.error ? "unavailable" : "loading";
  const showsInbox = queueSource !== "workspace";

  const queueColumns: TableWorkbenchColumn<TodayQueueRow>[] = [
    {
      id: "select",
      label: "",
      srLabel: "Select inbox item for assignment",
      render: (row) => row.source === "inbox" ? <TriageAssignCheckbox triage={triage} item={row.item} /> : null
    },
    {
      id: "item",
      label: "Item",
      className: "min-w-[18rem]",
      exportValue: (row) => row.label,
      render: (row) => (
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium text-foreground">{row.label}</span>
            {row.source === "workspace"
              ? <SeverityBadge status={toSeverity(row.focus.badgeVariant)} label={row.focus.statusLabel} />
              : <TriageSeverityStatus item={row.item} />}
          </div>
          <p className="text-xs leading-5 text-muted-foreground">{row.detail}</p>
        </div>
      )
    },
    { id: "source", label: "Source", exportValue: (row) => row.sourceLabel, render: (row) => row.sourceLabel },
    { id: "workspace", label: "Workspace", exportValue: (row) => row.workspaceLabel, render: (row) => row.workspaceLabel },
    {
      id: "owner",
      label: "Owner",
      exportValue: (row) => row.source === "inbox" ? row.item.ownerId : null,
      render: (row) => row.source === "inbox" ? row.item.ownerId ?? "Unassigned" : "Not tracked"
    },
    {
      id: "age",
      label: "Age",
      className: "whitespace-nowrap tabular-nums",
      exportValue: (row) => row.observedAt,
      render: (row) => <TriageAge value={row.observedAt} />
    },
    {
      id: "due",
      label: "Due · UTC",
      className: "whitespace-nowrap tabular-nums",
      exportValue: (row) => row.source === "inbox" ? row.item.dueAt : null,
      render: (row) => row.source === "inbox" && row.item.dueAt ? triageDateLabel(row.item.dueAt) : "No due time"
    },
    {
      id: "state",
      label: "Triage",
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
    actions: [{
      key: "a",
      label: "Assign",
      enabled: triage.capability(selectedInboxItem, "canAssign"),
      run: () => triage.openAction("Assign", selectedInboxItem)
    }]
  });

  const emptyText = queueSource === "workspace"
    ? search ? "No workspace signals match this search." : model.emptyQueueText
    : queueSource === "inbox"
      ? triage.inbox
        ? triage.mineOnly ? "No shared inbox items are assigned to you in these filters." : "No shared inbox items match these filters."
        : triage.error ? "The shared inbox is unavailable." : "The shared inbox is loading."
      : search || triage.mineOnly || triage.visibility !== "active"
        ? "No queue items match these filters."
        : `${model.emptyQueueText} The shared inbox has no active items.`;

  return (
    <div className="space-y-3">
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
              {onRefresh ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={refreshing}
                  aria-label={refreshing ? "Refreshing control tower evidence" : "Refresh control tower evidence"}
                  onClick={onRefresh}
                >
                  {refreshing ? "Refreshing" : "Refresh evidence"}
                </Button>
              ) : null}
            </>
          }
        />
      </section>

      <section aria-label="Daily control tower confidence" className="space-y-2">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
          <span title={viewModel.operatingScope.summary}><span className="text-muted-foreground">Scope </span>{viewModel.operatingScope.hasScope ? viewModel.operatingScope.summary : "All authorized scopes"}</span>
          <span title={freshness.detail}><span className="text-muted-foreground">Selected evidence </span>{freshness.value}</span>
          <span>
            {pluralizeCount(workspaceCount, "workspace signal")} in the queue
            {" · "}
            {inboxCount !== null ? pluralizeCount(inboxCount, "shared inbox item") : triage.error ? "Shared inbox unavailable" : "Shared inbox loading"}
          </span>
          {onEditOperatingScope ? (
            <Button type="button" variant="ghost" size="sm" onClick={onEditOperatingScope}>
              {viewModel.operatingScope.hasScope ? "Change scope filter" : "Set operating scope"}
            </Button>
          ) : null}
        </div>
      </section>

      {/* One queue grid, two sources (UX consolidation P1). Workspace signals are exactly what the
          sidebar Today badge and the masthead count; the shared inbox is the host's triage
          population, which is not a subset of them and keeps its assignment and snooze. The
          Source filter filters rows of this one grid. */}
      <section aria-labelledby="today-queue-heading" className="space-y-3" data-today-inbox-state={inboxState}>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h3 id="today-queue-heading" className="text-sm font-semibold">Queue</h3>
            <p className="text-xs text-muted-foreground">
              Workspace signals are what the sidebar badges count. Shared inbox items can be assigned, snoozed and acknowledged here.
            </p>
          </div>
          <SegmentedControl
            aria-label="Queue source"
            size="sm"
            value={queueSource}
            onChange={(value) => setQueueSource(value as TodayQueueSource)}
            options={[
              { value: "all", label: "All sources", count: inboxCount !== null ? workspaceCount + inboxCount : undefined },
              { value: "workspace", label: "Workspace signals", count: workspaceCount },
              { value: "inbox", label: "Shared inbox", count: inboxCount ?? undefined }
            ]}
          />
        </div>

        {showsInbox ? (
          <div className="space-y-2" aria-busy={triage.loading || triage.busy}>
            {triage.loading ? <p role="status" className="text-xs text-muted-foreground">Refreshing authorized inbox…</p> : null}
            {triage.error ? <OperatorTriageInboxError triage={triage} unaffectedText="Workspace signals in this queue are unaffected" /> : null}
            {triage.inbox ? (
              <div className="flex flex-wrap items-center justify-between gap-2" role="group" aria-label="Inbox filters">
                <p className="text-xs text-muted-foreground">
                  {triage.rows.length} of {pluralizeCount(triage.inbox.items.length, "inbox item")} · {fundAccountId ? "Account filter applied" : "All authorized accounts"}
                </p>
                <OperatorTriageInboxActions triage={triage} />
              </div>
            ) : null}
          </div>
        ) : null}
        {/* Command outcome and unconfirmed-command recovery stay visible under every Source filter. */}
        <OperatorTriageNotices triage={triage} showPersistence={showsInbox} />

        <div onKeyDownCapture={queueKeyboard} className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
          <TableWorkbench
            workbenchId="operator-inbox"
            sourceKey={`${triage.inbox?.actorId ?? "pending"}:${fundAccountId ?? "all"}`}
            completePopulation={Boolean(triage.inbox && !triage.loading && !triage.error)}
            sourceDescription={`Today queue: ${pluralizeCount(workspaceCount, "workspace signal")} from the workstation read models and ${triage.inbox ? `${pluralizeCount(triage.inbox.items.length, "shared inbox item")} as of ${triage.inbox.asOf}` : "no loaded shared inbox"}; inbox filters apply to inbox rows only.`}
            searchText={triage.search}
            onSearchTextChange={triage.setSearch}
            filterState={{ source: queueSource, visibility: triage.visibility, mineOnly: String(triage.mineOnly) }}
            onRestoreFilters={(filters) => {
              restoreTriageFilters(triage, filters);
              setQueueSource(filters.source === "workspace" || filters.source === "inbox" ? filters.source : "all");
            }}
            filters={showsInbox && triage.inbox
              ? <OperatorTriageInboxFilters triage={triage} visibilityLabel="Inbox visibility" mineLabel="My inbox items" />
              : null}
            tableId="daily-control-tower-queue"
            columns={queueColumns}
            rows={queueRows}
            getRowId={(row) => row.key}
            getRowAriaLabel={(row) => `${row.label}. ${row.sourceLabel}. ${row.source === "workspace" ? row.focus.statusLabel : row.item.tone}. ${row.detail}`}
            getRowSelectAriaLabel={(row) => row.label}
            getRowAriaControls={(row) => row.source === "workspace" ? EVIDENCE_PANEL_ID : TRIAGE_PANEL_ID}
            getRowAriaExpanded={(row) => row.key === selectedRow?.key}
            getRowClassName={() => "align-top"}
            onRowSelect={selectRow}
            selectedRowId={selectedRow?.key ?? null}
            emptyText={emptyText}
            ariaLabel="Today queue"
            maxVisibleRows={null}
          />
          {selectedRow?.source === "inbox" ? (
            <OperatorTriageDetailRail id={TRIAGE_PANEL_ID} triage={triage} item={selectedRow.item} />
          ) : selectedRow?.source === "workspace" ? (
            <WorkspaceEvidenceRail row={selectedRow} />
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

const TOWER_FRESHNESS_WINDOW_MS = 24 * 60 * 60 * 1000;

function buildTowerFreshness(
  timestampIso: string | null | undefined,
  detail: string | null | undefined,
  nowMs = Date.now()
): { value: string; detail: string; tone: OperationalTrustTone } {
  const observedAtMs = Date.parse(timestampIso ?? "");
  if (!Number.isFinite(observedAtMs)) {
    return {
      value: "Timestamp unavailable",
      detail: detail ?? "No timestamped evidence is linked to the leading decision.",
      tone: "review"
    };
  }

  const observedAt = new Date(observedAtMs);
  const timestampLabel = formatTimestamp(observedAt);
  const stale = nowMs - observedAtMs > TOWER_FRESHNESS_WINDOW_MS;

  return {
    value: `${stale ? "Stale update" : "Current"} · ${timestampLabel}`,
    detail: detail ?? "Latest evidence for the leading decision.",
    tone: stale ? "review" : "ready"
  };
}
