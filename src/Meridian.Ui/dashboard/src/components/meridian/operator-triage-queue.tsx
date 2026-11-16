import { Link } from "react-router-dom";
import { useOnGlobalRefresh } from "@/lib/global-refresh";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { TableWorkbench, type TableWorkbenchColumn, type TableWorkbenchSelection } from "@/components/meridian/table-workbench";
import { DetailRail } from "@/components/meridian/detail-rail";
import { DesignSystemStatus } from "@/design-system/status";
import { useQueueKeyboard } from "@/lib/use-queue-keyboard";
import { TimeText } from "@/components/ui/time-text";
import { formatRelativeTime, formatTimestamp, pluralizeCount } from "@/lib/format";
import { formatEnumLabel } from "@/lib/display-labels";
import { isActiveTriageSnooze, routeForTriageItem, type OperatorTriageItem } from "@/lib/operator-inbox-triage";
import { triageSearchText, useOperatorTriageInbox, type OperatorTriageInboxState } from "@/components/meridian/use-operator-triage-inbox";

export function OperatorTriageQueue({ fundAccountId, fallback, refreshKey = false }: {
  fundAccountId?: string | null;
  /** Shown until the inbox loads, or when it fails. Omit when the host page already lists its own work. */
  fallback?: React.ReactNode;
  refreshKey?: unknown;
}) {
  const triage = useOperatorTriageInbox({ fundAccountId, refreshKey });
  useOnGlobalRefresh(triage.reload);
  const { inbox, loading, error, busy, rows, selected, capability, openAction } = triage;
  const keyboard = useQueueKeyboard({
    rows, selectedId: selected?.workItemId ?? null, getId: (item: OperatorTriageItem) => item.workItemId,
    onSelect: (item: OperatorTriageItem) => triage.setSelectedId(item.workItemId),
    actions: [{ key: "a", label: "Assign", enabled: capability(selected, "canAssign"), run: () => openAction("Assign", selected) }]
  });

  const columns: TableWorkbenchColumn<OperatorTriageItem>[] = [
    { id: "select", label: "", srLabel: "Select for assignment", render: (item) => <TriageAssignCheckbox triage={triage} item={item} /> },
    { id: "item", exportValue: (item) => item.label, label: "Item", wrap: true, render: (item) => <div><span className="font-medium">{item.label}</span><p className="mt-1 text-xs text-muted-foreground">{item.detail}</p></div> },
    { id: "severity", exportValue: (item) => item.tone, label: "Severity", render: (item) => <TriageSeverityStatus item={item} /> },
    { id: "workspace", priority: "low", exportValue: (item) => item.workspace, label: "Workspace", render: (item) => item.workspace ?? "Unspecified" },
    { id: "owner", priority: "medium", exportValue: (item) => item.ownerId, label: "Owner", render: (item) => item.ownerId ?? "Unassigned" },
    { id: "age", priority: "medium", exportValue: (item) => triageAgeLabel(item.createdAt), label: "Age", className: "whitespace-nowrap tabular-nums", render: (item) => <TriageAge value={item.createdAt} /> },
    { id: "due", exportValue: (item) => item.dueAt, label: "Due", className: "whitespace-nowrap tabular-nums", render: (item) => <TimeText value={item.dueAt} display="compact" fallback="No due time" /> },
    { id: "state", priority: "low", exportValue: triageStateExportValue, label: "Triage", render: triageStateLabel }
  ];
  const detailId = "operator-triage-detail";

  return <div role="group" aria-label="Authorized operator inbox" aria-busy={loading || busy} className="space-y-3">
    {loading ? <p role="status" className="text-xs text-muted-foreground">Refreshing authorized inbox…</p> : null}
    {error ? <OperatorTriageInboxError triage={triage} unaffectedText="The finance queue on this page is unaffected" /> : null}
    {!inbox ? (fallback ?? null) : <>
      <div className="flex flex-wrap items-center justify-between gap-2" role="group" aria-label="Inbox filters">
        <p className="text-xs text-muted-foreground">{rows.length} of {pluralizeCount(inbox.items.length, "inbox item")} · {fundAccountId ? "Account filter applied" : "All authorized accounts"}</p>
        <OperatorTriageInboxActions />
      </div>
      <OperatorTriageNotices triage={triage} />
      <div onKeyDownCapture={keyboard} className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <TableWorkbench key={`${inbox.actorId}:${fundAccountId ?? "all"}`} workbenchId="operator-inbox" completePopulation={!loading && !error}
          sourceDescription={`Authorized ranked inbox as of ${inbox.asOf}; filters apply to this inbox population, not historical work records.`}
          searchText={triage.search} onSearchTextChange={triage.setSearch} getSearchText={triageSearchText}
          filterState={{ visibility: triage.visibility, mineOnly: String(triage.mineOnly) }} onRestoreFilters={(filters) => restoreTriageFilters(triage, filters)}
          filters={<OperatorTriageInboxFilters triage={triage} />} selection={triageSelection(triage)}
          tableId="operator-triage-queue" columns={columns} rows={rows} getRowId={(item) => item.workItemId} selectedRowId={selected?.workItemId}
          onRowSelect={(item) => { triage.setSelectedId(item.workItemId); triage.closeAction(); }} getRowSelectAriaLabel={(item) => item.label}
          getRowAriaControls={() => detailId} getRowAriaExpanded={(item) => item.workItemId === selected?.workItemId}
          ariaLabel="Authorized ranked operator queue" emptyText={triage.mineOnly ? "No items are assigned to you in these filters." : "No authorized items match these filters."} />
        {selected ? <OperatorTriageDetailRail id={detailId} triage={triage} item={selected} /> : null}
      </div>
      <OperatorTriageActionDialog triage={triage} />
    </>}
  </div>;
}

/** The inbox-failure alert: names what still works and offers a retry. */
export function OperatorTriageInboxError({ triage, unaffectedText }: { triage: OperatorTriageInboxState; unaffectedText: string }) {
  return <div role="alert" className="space-y-2 border border-border p-3">
    <p>Inbox triage is unavailable. {unaffectedText}; assignment and personal triage are disabled.</p>
    <p className="text-xs">{triage.error}</p>
    <Button size="sm" variant="outline" onClick={triage.reload}>Retry inbox</Button>
  </div>;
}

/**
 * Bulk assignment for checked inbox rows, as a TableWorkbench selection bar. The bar exists only
 * while rows are checked, so the toolbar never carries a disabled "Assign 0 selected".
 */
export function triageSelection(triage: OperatorTriageInboxState): TableWorkbenchSelection {
  const count = triage.checkedIds.length;
  return {
    count,
    noun: "inbox item",
    onClear: triage.clearChecked,
    actions: <Button size="sm" disabled={!triage.actionsAvailable || count > 100}
      disabledReason={count > 100 ? "Assign at most 100 items at once." : !triage.actionsAvailable ? "Durable triage is unavailable." : null}
      onClick={() => triage.openAction("BulkAssign")}>Assign {count} selected</Button>
  };
}

/**
 * Shortcut help for the loaded inbox. Bulk assignment lives in the selection bar, and the inbox
 * reloads with the header Refresh; "Retry inbox" appears only in its error state.
 */
export function OperatorTriageInboxActions() {
  return <div className="flex flex-wrap items-center gap-2">
    <details className="text-xs"><summary className="cursor-pointer">Shortcuts</summary><p className="mt-1">j/k select; a opens assignment. Acknowledgement and snooze never clear source blockers.</p></details>
  </div>;
}

/** Durable-storage availability, the last command outcome, and unconfirmed-command recovery. */
export function OperatorTriageNotices({ triage, showPersistence = true }: { triage: OperatorTriageInboxState; showPersistence?: boolean }) {
  const { inbox, notice, uncertain, busy, loading } = triage;
  return <>
    {showPersistence && inbox && !inbox.persistenceAvailable ? <p role="alert" className="text-sm">{inbox.unavailableReason ?? "Durable triage is unavailable. Actions are disabled."}</p> : null}
    {notice ? <p role="status" className="text-sm">{notice}</p> : null}
    {uncertain ? <div className="flex flex-wrap gap-2"><Button size="sm" disabled={busy || loading || !inbox?.persistenceAvailable} onClick={triage.retryUncertain}>Retry same command</Button><Button size="sm" variant="outline" disabled={busy || loading} onClick={triage.reload}>Check command outcome</Button></div> : null}
  </>;
}

export function OperatorTriageInboxFilters({ triage, visibilityLabel = "Visibility", mineLabel = "My items", activeLabel = "Active items", showMine = true }: {
  triage: OperatorTriageInboxState;
  visibilityLabel?: string;
  mineLabel?: string;
  /** Names the default view; Today calls it "Needs attention" because it applies the count rule. */
  activeLabel?: string;
  /** Hide "My items" where no inbox is loaded to own any. */
  showMine?: boolean;
}) {
  return <>
    <label className="text-xs">{visibilityLabel}<Select aria-label={visibilityLabel} value={triage.visibility} onChange={(event) => triage.setVisibility(event.target.value)}><option value="active">{activeLabel}</option><option value="all">All items</option><option value="snoozed">Snoozed items</option></Select></label>
    {showMine ? <label className="flex min-h-9 items-center gap-2 text-sm"><input type="checkbox" checked={triage.mineOnly} onChange={(event) => triage.setMineOnly(event.target.checked)} />{mineLabel}</label> : null}
  </>;
}

export function restoreTriageFilters(triage: OperatorTriageInboxState, filters: Record<string, string>) {
  triage.setVisibility(filters.visibility);
  triage.setMineOnly(filters.mineOnly === "true");
}

export function TriageAssignCheckbox({ triage, item }: { triage: OperatorTriageInboxState; item: OperatorTriageItem }) {
  return <input type="checkbox" aria-label={`Select ${item.label} for assignment`}
    disabled={!triage.capability(item, "canAssign")} checked={triage.checkedIds.includes(item.workItemId)}
    onChange={(event) => triage.toggleChecked(item.workItemId, event.target.checked)} />;
}

export function TriageSeverityStatus({ item }: { item: OperatorTriageItem }) {
  return <DesignSystemStatus status={item.tone === "Critical" ? "blocked" : item.tone === "Warning" ? "action" : item.tone === "Success" ? "ready" : "info"}>{item.tone === "Critical" ? "Blocked" : item.tone === "Warning" ? "Action" : item.tone}</DesignSystemStatus>;
}

export function triageStateLabel(item: OperatorTriageItem): string {
  return isActiveTriageSnooze(item) ? `Snoozed until ${triageDateLabel(item.snoozedUntil)}` : item.acknowledgedAt ? "Acknowledged" : "Open";
}

export function triageStateExportValue(item: OperatorTriageItem): string {
  return isActiveTriageSnooze(item) ? `Snoozed until ${item.snoozedUntil}` : item.acknowledgedAt ? `Acknowledged at ${item.acknowledgedAt}` : "Open";
}

/** The selected inbox item: assign / acknowledge / snooze, its route, and retained activity. */
export function OperatorTriageDetailRail({ id, triage, item }: { id: string; triage: OperatorTriageInboxState; item: OperatorTriageItem }) {
  const { capability, openAction } = triage;
  const route = routeForTriageItem(item);
  return <DetailRail id={id} ariaLabel={`${item.label} triage detail`} title={item.label} fields={[{ label: "Workspace", value: item.workspace ?? "Unspecified" }, { label: "Owner", value: item.ownerId ?? "Unassigned" }, { label: "Observed", value: triageDateLabel(item.createdAt) }]}>
    <p className="my-3 text-sm">{item.detail}</p>
    <div className="flex flex-wrap gap-2">
      <Button size="sm" variant="outline" disabled={!capability(item, "canAssign")} onClick={() => openAction("Assign", item)}>Assign</Button>
      <Button size="sm" variant="outline" disabled={!capability(item, "canAcknowledge")} onClick={() => openAction("Acknowledge", item)}>Acknowledge</Button>
      {isActiveTriageSnooze(item) ? <Button size="sm" variant="outline" disabled={!capability(item, "canUnsnooze")} onClick={() => openAction("Unsnooze", item)}>Unsnooze</Button>
        : <Button size="sm" variant="outline" disabled={!capability(item, "canSnooze")} disabledReason={item.tone === "Critical" ? "Blocked items remain visible." : item.capabilities.disabledReason} onClick={() => openAction("Snooze", item)}>Snooze</Button>}
    </div>
    {item.capabilities.disabledReason ? <p className="text-xs text-muted-foreground">{item.capabilities.disabledReason}</p> : null}
    {route ? <Button asChild size="sm" variant="outline"><Link to={route}>Open in workspace</Link></Button> : <p className="text-xs">Workspace route unavailable.</p>}
    <details><summary className="cursor-pointer text-sm">Activity · {item.activity.length}</summary>{item.activity.length ? <ol className="mt-2 space-y-3">{item.activity.map((entry) => <li key={entry.commandId} className="text-xs"><p>{formatEnumLabel(entry.action)} · {entry.actorId} · {triageDateLabel(entry.occurredAt)}</p><p>{entry.reason}</p></li>)}</ol> : <p className="mt-2 text-xs">No retained triage activity.</p>}</details>
  </DetailRail>;
}

/** The reviewed triage-action dialog; every command needs a reason and carries authoritative versions. */
export function OperatorTriageActionDialog({ triage }: { triage: OperatorTriageInboxState }) {
  const { action, busy, checkedIds } = triage;
  return <Dialog open={action !== null} onOpenChange={(open) => { if (!open && !busy) triage.closeAction(); }}><DialogContent aria-labelledby="triage-action-title" aria-describedby="triage-action-description">
    <DialogTitle id="triage-action-title">{action === "BulkAssign" ? `Assign ${checkedIds.length} selected items` : `${formatEnumLabel(action, "Review")} item`}</DialogTitle>
    <DialogDescription id="triage-action-description">{action === "BulkAssign" ? "Each item is authorized and version-checked separately. Partial failures remain visible." : triage.actionTarget?.label} This changes triage metadata; it does not resolve or approve the source work.</DialogDescription>
    <form className="mt-4 space-y-3" onSubmit={(event) => { event.preventDefault(); triage.submit(); }}>
      {action === "Assign" || action === "BulkAssign" ? <label className="block text-sm">Assignee operator ID<Input value={triage.assignee} onChange={(event) => triage.setAssignee(event.target.value)} autoComplete="off" required /></label> : null}
      {action === "Snooze" ? <label className="block text-sm">Snooze until · UTC (within 7 days)<Input type="datetime-local" value={triage.until} onChange={(event) => triage.setUntil(event.target.value)} required /></label> : null}
      <label className="block text-sm">Reason<Input value={triage.reason} onChange={(event) => triage.setReason(event.target.value)} required maxLength={2000} /></label>
      <div className="flex justify-end gap-2"><Button type="button" variant="outline" disabled={busy} onClick={triage.closeAction}>Cancel</Button><Button type="submit" disabled={!triage.canSubmit}>{busy ? "Applying…" : "Apply triage action"}</Button></div>
    </form>
  </DialogContent></Dialog>;
}

export function triageDateLabel(value: string | null | undefined): string {
  const timestamp = Date.parse(value ?? "");
  if (!Number.isFinite(timestamp)) return "Unavailable";
  // One time format: the shared formatter, in the operator's display zone, with the zone shown.
  return formatTimestamp(timestamp);
}

function hasTriageAge(value: string | null | undefined, now: number): value is string {
  const timestamp = Date.parse(value ?? "");
  // A future instant (clock skew) has no truthful age.
  return Number.isFinite(timestamp) && timestamp <= now;
}

/** Age of a triage item as text, e.g. "5m ago" (`formatRelativeTime`); "Age unavailable" when absent, invalid or in the future. */
export function triageAgeLabel(value: string | null | undefined, now = Date.now()): string {
  return hasTriageAge(value, now) ? formatRelativeTime(value, now) : "Age unavailable";
}

/** The triage age cell: "5m ago" with the absolute timestamp in the display zone on hover. */
export function TriageAge({ value, now = Date.now() }: { value: string | null | undefined; now?: number }) {
  return hasTriageAge(value, now) ? <TimeText value={value} display="relative" now={now} /> : <span>Age unavailable</span>;
}
