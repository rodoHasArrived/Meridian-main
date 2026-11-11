import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { DetailRail } from "@/components/meridian/detail-rail";
import { DesignSystemStatus } from "@/design-system/status";
import { useQueueKeyboard } from "@/lib/use-queue-keyboard";
import {
  bulkAssignOperatorTriage, commandOperatorTriage, getOperatorTriageInbox,
  isActiveTriageSnooze, routeForTriageItem,
  type OperatorTriageAction, type OperatorTriageBulkAssignment, type OperatorTriageCommand,
  type OperatorTriageCommandResult, type OperatorTriageInbox, type OperatorTriageItem
} from "@/lib/operator-inbox-triage";

interface PendingCommand {
  itemId?: string;
  command: OperatorTriageCommand | OperatorTriageBulkAssignment;
}

export function OperatorTriageQueue({ fundAccountId, fallback, refreshKey = false }: {
  fundAccountId?: string | null;
  fallback: React.ReactNode;
  refreshKey?: unknown;
}) {
  const [inbox, setInbox] = useState<OperatorTriageInbox | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [checkedIds, setCheckedIds] = useState<string[]>([]);
  const [mineOnly, setMineOnly] = useState(false);
  const [visibility, setVisibility] = useState("active");
  const [search, setSearch] = useState("");
  const [action, setAction] = useState<OperatorTriageAction | "BulkAssign" | null>(null);
  const [reason, setReason] = useState("");
  const [assignee, setAssignee] = useState("");
  const [until, setUntil] = useState("");
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState<PendingCommand | null>(null);
  const [tick, setTick] = useState(0);
  const [reloadSequence, setReloadSequence] = useState(0);
  const readInvocation = useRef(0);
  const commandInvocation = useRef(0);
  const actor = useRef<string | null>(null);
  const commandController = useRef<AbortController | null>(null);
  const uncertainRef = useRef<PendingCommand | null>(null);
  uncertainRef.current = uncertain;

  const reload = useCallback(() => setReloadSequence((value) => value + 1), []);
  useEffect(() => {
    // Commands belong to one account/actor lifetime, not to an individual inbox read.
    // Evidence refreshes and snooze expiry may replace reads while a write is pending.
    commandInvocation.current += 1;
    commandController.current?.abort();
    commandController.current = null;
    actor.current = null;
    uncertainRef.current = null;
    setInbox(null); setBusy(false); setUncertain(null); setAction(null);
    setSelectedId(null); setCheckedIds([]); setNotice(null);
    setReason(""); setAssignee(""); setUntil(""); setMineOnly(false);
    return () => {
      commandInvocation.current += 1;
      commandController.current?.abort();
      commandController.current = null;
    };
  }, [fundAccountId]);

  useEffect(() => {
    const controller = new AbortController();
    const current = ++readInvocation.current;
    // A failed refresh must not leave old authority available for writes.
    setLoading(true);
    setError(null);
    void getOperatorTriageInbox(fundAccountId, { signal: controller.signal }).then((result) => {
      if (controller.signal.aborted || readInvocation.current !== current) return;
      if (actor.current && actor.current !== result.actorId) {
        commandInvocation.current += 1;
        commandController.current?.abort();
        commandController.current = null;
        uncertainRef.current = null;
        setBusy(false);
        setSelectedId(null); setCheckedIds([]); setAction(null); setUncertain(null); setNotice(null);
        setReason(""); setAssignee(""); setUntil(""); setMineOnly(false);
      }
      actor.current = result.actorId;
      const pending = uncertainRef.current;
      const pendingIds = pending ? pending.itemId ? [pending.itemId] : (pending.command as OperatorTriageBulkAssignment).items.map((item) => item.workItemId) : [];
      const retainedPending = pending && pendingIds.every((id, index) => {
        const commandId = pending.itemId ? pending.command.commandId : `${pending.command.commandId}:${index}`;
        return result.items.some((item) => item.workItemId === id && item.activity.some((entry) => entry.commandId === commandId));
      });
      if (retainedPending) {
        setUncertain(null);
        setNotice("The retained activity confirms the command. Review the refreshed item state.");
      }
      setInbox(result);
      setCheckedIds((ids) => ids.filter((id) => result.items.some((item) => item.workItemId === id && item.capabilities.canAssign)));
    }).catch((failure) => {
      if (!controller.signal.aborted && readInvocation.current === current) {
        setInbox(null);
        setAction(null);
        setError(failure instanceof Error ? failure.message : "Operator inbox could not be loaded.");
      }
    }).finally(() => {
      if (!controller.signal.aborted && readInvocation.current === current) setLoading(false);
    });
    return () => { controller.abort(); readInvocation.current += 1; };
  }, [fundAccountId, refreshKey, reloadSequence]);

  useEffect(() => {
    const expiries = inbox?.items.map((item) => Date.parse(item.snoozedUntil ?? "")).filter((time) => time > Date.now()) ?? [];
    if (!expiries.length) return;
    const timeout = setTimeout(() => { setTick((value) => value + 1); reload(); }, Math.min(60_000, Math.max(1, Math.min(...expiries) - Date.now() + 1)));
    return () => clearTimeout(timeout);
  }, [inbox, tick, reload]);

  const rows = (inbox?.items ?? []).filter((item) => {
    const snoozed = isActiveTriageSnooze(item);
    return (!mineOnly || item.ownerId === inbox?.actorId)
      && (visibility === "all" || (visibility === "snoozed" ? snoozed : !snoozed))
      && (!search.trim() || `${item.label} ${item.detail} ${item.workspace ?? ""} ${item.ownerId ?? ""}`.toLowerCase().includes(search.trim().toLowerCase()));
  });
  const selected = rows.find((item) => item.workItemId === selectedId) ?? rows[0] ?? null;
  const actionsAvailable = Boolean(inbox?.persistenceAvailable && !loading && !busy && !uncertain);
  const capability = (item: OperatorTriageItem | null, key: "canAssign" | "canAcknowledge" | "canSnooze" | "canUnsnooze") =>
    Boolean(actionsAvailable && item?.capabilities[key] && (key !== "canSnooze" || item.tone !== "Critical"));
  const openAction = (next: OperatorTriageAction | "BulkAssign") => {
    setReason(""); setAssignee("");
    setUntil(new Date(Date.now() + 60 * 60 * 1000).toISOString().slice(0, 16));
    setNotice(null); setAction(next);
  };
  const keyboard = useQueueKeyboard({
    rows, selectedId: selected?.workItemId ?? null, getId: (item: OperatorTriageItem) => item.workItemId,
    onSelect: (item: OperatorTriageItem) => setSelectedId(item.workItemId),
    actions: [{ key: "a", label: "Assign", enabled: capability(selected, "canAssign"), run: () => openAction("Assign") }]
  });

  async function execute(pending: PendingCommand) {
    if (commandController.current) return;
    const controller = new AbortController();
    commandController.current = controller;
    const current = ++commandInvocation.current;
    const signal = controller.signal;
    setBusy(true); setNotice(null);
    try {
      const results: OperatorTriageCommandResult[] = pending.itemId
        ? [await commandOperatorTriage(pending.itemId, pending.command as OperatorTriageCommand, { signal })]
        : (await bulkAssignOperatorTriage(pending.command as OperatorTriageBulkAssignment, { signal })).results;
      if (signal.aborted || current !== commandInvocation.current) return;
      if (!Array.isArray(results) || results.some((result) => !["Applied", "Conflict", "Rejected", "NotFound", "Unavailable", "Forbidden"].includes(result.status))) {
        throw new Error("The command response could not be verified.");
      }
      const applied = results.filter((result) => result.status === "Applied").length;
      const refused = results.filter((result) => result.status !== "Applied");
      setNotice(`${applied} applied${refused.length ? `; ${refused.length} not applied: ${refused.map((result) => `${result.workItemId}: ${result.error ?? result.status}`).join("; ")}` : ". Source readiness is unchanged."}`);
      setUncertain(null); setAction(null); setCheckedIds([]);
      reload();
    } catch (failure) {
      if (signal.aborted || current !== commandInvocation.current) return;
      setUncertain(pending); setAction(null);
      setNotice(`Command outcome is unconfirmed. Refresh activity or retry this same command. ${failure instanceof Error ? failure.message : "Request failed."}`);
    } finally {
      if (!signal.aborted && current === commandInvocation.current) {
        commandController.current = null;
        setBusy(false);
      }
    }
  }

  const columns: TableWorkbenchColumn<OperatorTriageItem>[] = [
    { id: "select", label: "", srLabel: "Select for assignment", render: (item) => <input type="checkbox" aria-label={`Select ${item.label} for assignment`}
      disabled={!capability(item, "canAssign")} checked={checkedIds.includes(item.workItemId)} onChange={(event) => setCheckedIds((ids) => event.target.checked ? [...ids, item.workItemId] : ids.filter((id) => id !== item.workItemId))} /> },
    { id: "item", exportValue: (item) => item.label, label: "Item", className: "min-w-[16rem]", render: (item) => <div><span className="font-medium">{item.label}</span><p className="mt-1 text-xs text-muted-foreground">{item.detail}</p></div> },
    { id: "severity", exportValue: (item) => item.tone, label: "Severity", render: (item) => <DesignSystemStatus status={item.tone === "Critical" ? "blocked" : item.tone === "Warning" ? "action" : item.tone === "Success" ? "ready" : "info"}>{item.tone === "Critical" ? "Blocked" : item.tone === "Warning" ? "Action" : item.tone}</DesignSystemStatus> },
    { id: "workspace", exportValue: (item) => item.workspace, label: "Workspace", render: (item) => item.workspace ?? "Unspecified" },
    { id: "owner", exportValue: (item) => item.ownerId, label: "Owner", render: (item) => item.ownerId ?? "Unassigned" },
    { id: "age", exportValue: (item) => triageAgeLabel(item.createdAt), label: "Age", className: "whitespace-nowrap tabular-nums", render: (item) => triageAgeLabel(item.createdAt) },
    { id: "due", exportValue: (item) => item.dueAt, label: "Due · UTC", className: "whitespace-nowrap tabular-nums", render: (item) => item.dueAt ? triageDateLabel(item.dueAt) : "No due time" },
    { id: "state", exportValue: (item) => isActiveTriageSnooze(item) ? `Snoozed until ${item.snoozedUntil}` : item.acknowledgedAt ? `Acknowledged at ${item.acknowledgedAt}` : "Open", label: "Triage", render: (item) => isActiveTriageSnooze(item) ? `Snoozed until ${triageDateLabel(item.snoozedUntil)}` : item.acknowledgedAt ? "Acknowledged" : "Open" }
  ];

  const snoozeDate = Date.parse(`${until}Z`);
  const canSubmit = Boolean(action && reason.trim() && !busy && (action !== "Assign" && action !== "BulkAssign" || assignee.trim())
    && (action !== "Snooze" || Number.isFinite(snoozeDate) && snoozeDate > Date.now() && snoozeDate <= Date.now() + 7 * 24 * 60 * 60 * 1000)
    && (action === "BulkAssign" ? checkedIds.length > 0 && checkedIds.length <= 100 && checkedIds.every((id) => capability(inbox?.items.find((item) => item.workItemId === id) ?? null, "canAssign"))
      : capability(selected, action === "Assign" ? "canAssign" : action === "Acknowledge" ? "canAcknowledge" : action === "Snooze" ? "canSnooze" : "canUnsnooze")));
  const detailId = "operator-triage-detail";

  return <div role="group" aria-label="Authorized operator inbox" aria-busy={loading || busy} className="space-y-3">
    {loading ? <p role="status" className="text-xs text-muted-foreground">Refreshing authorized inbox…</p> : null}
    {error ? <div role="alert" className="space-y-2 border border-border p-3"><p>Inbox triage is unavailable. Loaded workspace evidence remains visible below; assignment and personal triage are disabled.</p><p className="text-xs">{error}</p><Button size="sm" variant="outline" onClick={reload}>Retry inbox</Button></div> : null}
    {!inbox ? fallback : <>
      <div className="flex flex-wrap items-center justify-between gap-2" role="group" aria-label="Inbox filters">
        <p className="text-xs text-muted-foreground">{rows.length} of {inbox.items.length} authorized items · {fundAccountId ? "Account filter applied" : "All authorized accounts"}</p>
        <div className="flex flex-wrap items-center gap-2">
        <Button size="sm" variant="outline" disabled={!actionsAvailable || checkedIds.length === 0 || checkedIds.length > 100} onClick={() => openAction("BulkAssign")}>Assign {checkedIds.length} selected</Button>
        <Button size="sm" variant="outline" disabled={loading || busy} onClick={reload}>Refresh inbox</Button>
        <details className="text-xs"><summary className="cursor-pointer">Shortcuts</summary><p className="mt-1">j/k select; a opens assignment. Acknowledgement and snooze never clear source blockers.</p></details>
        </div>
      </div>
      {!inbox.persistenceAvailable ? <p role="alert" className="text-sm">{inbox.unavailableReason ?? "Durable triage is unavailable. Actions are disabled."}</p> : null}
      {notice ? <p role="status" className="text-sm">{notice}</p> : null}
      {uncertain ? <div className="flex flex-wrap gap-2"><Button size="sm" disabled={busy || loading || !inbox.persistenceAvailable} onClick={() => void execute(uncertain)}>Retry same command</Button><Button size="sm" variant="outline" disabled={busy || loading} onClick={reload}>Refresh command activity</Button></div> : null}
      <div onKeyDownCapture={keyboard} className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <TableWorkbench key={`${inbox.actorId}:${fundAccountId ?? "all"}`} workbenchId="operator-inbox" completePopulation={!loading && !error}
          sourceDescription={`Authorized ranked inbox as of ${inbox.asOf}; filters apply to this inbox population, not historical work records.`}
          searchText={search} onSearchTextChange={setSearch} getSearchText={(item) => `${item.label} ${item.detail} ${item.workspace ?? ""} ${item.ownerId ?? ""}`}
          filterState={{ visibility, mineOnly: String(mineOnly) }} onRestoreFilters={(filters) => { setVisibility(["active", "all", "snoozed"].includes(filters.visibility) ? filters.visibility : "active"); setMineOnly(filters.mineOnly === "true"); }}
          filters={<><label className="text-xs">Visibility<Select aria-label="Visibility" value={visibility} onChange={(event) => setVisibility(event.target.value)}><option value="active">Active items</option><option value="all">All items</option><option value="snoozed">Snoozed items</option></Select></label><label className="flex min-h-9 items-center gap-2 text-sm"><input type="checkbox" checked={mineOnly} onChange={(event) => setMineOnly(event.target.checked)} />My items</label></>}
          tableId="operator-triage-queue" columns={columns} rows={rows} getRowId={(item) => item.workItemId} selectedRowId={selected?.workItemId}
          onRowSelect={(item) => { setSelectedId(item.workItemId); setAction(null); }} getRowSelectAriaLabel={(item) => item.label}
          getRowAriaControls={() => detailId} getRowAriaExpanded={(item) => item.workItemId === selected?.workItemId}
          ariaLabel="Authorized ranked operator queue" emptyText={mineOnly ? "No items are assigned to you in these filters." : "No authorized items match these filters."} />
        {selected ? <DetailRail id={detailId} ariaLabel={`${selected.label} triage detail`} title={selected.label} fields={[{ label: "Workspace", value: selected.workspace ?? "Unspecified" }, { label: "Owner", value: selected.ownerId ?? "Unassigned" }, { label: "Observed · UTC", value: triageDateLabel(selected.createdAt) }]}>
          <p className="my-3 text-sm">{selected.detail}</p>
          <div className="flex flex-wrap gap-2">
            <Button size="sm" variant="outline" disabled={!capability(selected, "canAssign")} onClick={() => openAction("Assign")}>Assign</Button>
            <Button size="sm" variant="outline" disabled={!capability(selected, "canAcknowledge")} onClick={() => openAction("Acknowledge")}>Acknowledge</Button>
            {isActiveTriageSnooze(selected) ? <Button size="sm" variant="outline" disabled={!capability(selected, "canUnsnooze")} onClick={() => openAction("Unsnooze")}>Unsnooze</Button>
              : <Button size="sm" variant="outline" disabled={!capability(selected, "canSnooze")} disabledReason={selected.tone === "Critical" ? "Blocked items remain visible." : selected.capabilities.disabledReason} onClick={() => openAction("Snooze")}>Snooze</Button>}
          </div>
          {selected.capabilities.disabledReason ? <p className="text-xs text-muted-foreground">{selected.capabilities.disabledReason}</p> : null}
          {routeForTriageItem(selected) ? <Button asChild size="sm" variant="outline"><Link to={routeForTriageItem(selected)!}>Open in workspace</Link></Button> : <p className="text-xs">Workspace route unavailable.</p>}
          <details><summary className="cursor-pointer text-sm">Activity · {selected.activity.length}</summary>{selected.activity.length ? <ol className="mt-2 space-y-3">{selected.activity.map((entry) => <li key={entry.commandId} className="text-xs"><p>{entry.action} · {entry.actorId} · {triageDateLabel(entry.occurredAt)}</p><p>{entry.reason}</p></li>)}</ol> : <p className="mt-2 text-xs">No retained triage activity.</p>}</details>
          </DetailRail> : null}
      </div>
      <Dialog open={action !== null} onOpenChange={(open) => { if (!open && !busy) setAction(null); }}><DialogContent aria-labelledby="triage-action-title" aria-describedby="triage-action-description">
        <DialogTitle id="triage-action-title">{action === "BulkAssign" ? `Assign ${checkedIds.length} selected items` : `${action ?? "Review"} item`}</DialogTitle>
        <DialogDescription id="triage-action-description">{action === "BulkAssign" ? "Each item is authorized and version-checked separately. Partial failures remain visible." : selected?.label} This changes triage metadata; it does not resolve or approve the source work.</DialogDescription>
        <form className="mt-4 space-y-3" onSubmit={(event) => {
          event.preventDefault(); if (!canSubmit || !action) return;
          const common = { commandId: crypto.randomUUID(), reason: reason.trim(), fundAccountId };
          if (action === "BulkAssign") {
            void execute({ command: { ...common, action: "Assign", assigneeId: assignee.trim(), items: inbox.items.filter((item) => checkedIds.includes(item.workItemId)).map((item) => ({ workItemId: item.workItemId, expectedVersion: item.version, expectedSourceVersion: item.sourceVersion })) } });
          } else if (selected) void execute({ itemId: selected.workItemId, command: { ...common, action, expectedVersion: selected.version, expectedSourceVersion: selected.sourceVersion, ...(action === "Assign" ? { assigneeId: assignee.trim() } : {}), ...(action === "Snooze" ? { snoozedUntil: new Date(snoozeDate).toISOString() } : {}) } });
        }}>
          {action === "Assign" || action === "BulkAssign" ? <label className="block text-sm">Assignee operator ID<Input value={assignee} onChange={(event) => setAssignee(event.target.value)} autoComplete="off" required /></label> : null}
          {action === "Snooze" ? <label className="block text-sm">Snooze until · UTC (within 7 days)<Input type="datetime-local" value={until} onChange={(event) => setUntil(event.target.value)} required /></label> : null}
          <label className="block text-sm">Reason<Input value={reason} onChange={(event) => setReason(event.target.value)} required maxLength={2000} /></label>
          <div className="flex justify-end gap-2"><Button type="button" variant="outline" disabled={busy} onClick={() => setAction(null)}>Cancel</Button><Button type="submit" disabled={!canSubmit}>{busy ? "Applying…" : "Apply triage action"}</Button></div>
        </form>
      </DialogContent></Dialog>
    </>}
  </div>;
}

export function triageDateLabel(value: string | null | undefined): string {
  const timestamp = Date.parse(value ?? "");
  if (!Number.isFinite(timestamp)) return "Unavailable";
  return new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "UTC" }).format(timestamp);
}

export function triageAgeLabel(value: string | null | undefined, now = Date.now()): string {
  const timestamp = Date.parse(value ?? "");
  if (!Number.isFinite(timestamp) || timestamp > now) return "Age unavailable";
  const minutes = Math.floor((now - timestamp) / 60_000);
  if (minutes < 1) return "<1m";
  if (minutes < 60) return `${minutes}m`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)}h`;
  return `${Math.floor(minutes / 1440)}d`;
}
