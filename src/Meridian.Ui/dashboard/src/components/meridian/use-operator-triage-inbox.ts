import { useCallback, useEffect, useRef, useState } from "react";
import {
  bulkAssignOperatorTriage, commandOperatorTriage, getOperatorTriageInbox, isActiveTriageSnooze,
  type OperatorTriageAction, type OperatorTriageBulkAssignment, type OperatorTriageCommand,
  type OperatorTriageCommandResult, type OperatorTriageInbox, type OperatorTriageItem
} from "@/lib/operator-inbox-triage";

export interface PendingTriageCommand {
  itemId?: string;
  command: OperatorTriageCommand | OperatorTriageBulkAssignment;
}

export type OperatorTriageCapability = "canAssign" | "canAcknowledge" | "canSnooze" | "canUnsnooze";
export type OperatorTriageDialogAction = OperatorTriageAction | "BulkAssign" | "BulkAcknowledge" | "BulkSnooze";
/** Bulk actions sent as one command per item, each confirmed in the dialog (UX Phase E). */
export type OperatorTriageEachAction = "BulkAcknowledge" | "BulkSnooze";

/** One item's outcome in a per-item bulk action. */
export interface OperatorTriageBulkItemResult {
  workItemId: string;
  label: string;
  outcome: "Applied" | "Conflict" | "Rejected" | "NotFound" | "Unavailable" | "Forbidden" | "Unconfirmed";
  detail: string | null;
}

/** Whether a checked item can take a per-item bulk action, and why not. */
export interface OperatorTriageBulkEligibility {
  item: OperatorTriageItem;
  eligible: boolean;
  reason: string | null;
}

export const OPERATOR_TRIAGE_BULK_LIMIT = 100;

function isEachAction(action: OperatorTriageDialogAction | null): action is OperatorTriageEachAction {
  return action === "BulkAcknowledge" || action === "BulkSnooze";
}
export type OperatorTriageVisibility = "active" | "all" | "snoozed";

/** The shared inbox searchable text; Today's merged grid reuses it for inbox rows. */
export function triageSearchText(item: OperatorTriageItem): string {
  return `${item.label} ${item.detail} ${item.workspace ?? ""} ${item.ownerId ?? ""}`;
}

/**
 * The shared host inbox: its authorized read, filters, bulk selection and the single
 * in-flight / unconfirmed triage command. Owning this above any presentation lets a screen
 * filter, hide or re-lay-out inbox rows without dropping a pending or uncertain command.
 */
export function useOperatorTriageInbox({ fundAccountId, refreshKey = false }: {
  fundAccountId?: string | null;
  refreshKey?: unknown;
}) {
  const [inbox, setInbox] = useState<OperatorTriageInbox | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [checkedIds, setCheckedIds] = useState<string[]>([]);
  const [mineOnly, setMineOnly] = useState(false);
  const [visibility, setVisibilityState] = useState<OperatorTriageVisibility>("active");
  const [search, setSearch] = useState("");
  const [action, setAction] = useState<OperatorTriageDialogAction | null>(null);
  const [actionTargetId, setActionTargetId] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [assignee, setAssignee] = useState("");
  const [until, setUntil] = useState("");
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState<PendingTriageCommand | null>(null);
  const [confirmedIds, setConfirmedIds] = useState<string[]>([]);
  const [bulkResults, setBulkResults] = useState<OperatorTriageBulkItemResult[] | null>(null);
  const [tick, setTick] = useState(0);
  const [reloadSequence, setReloadSequence] = useState(0);
  const readInvocation = useRef(0);
  const commandInvocation = useRef(0);
  const actor = useRef<string | null>(null);
  const commandController = useRef<AbortController | null>(null);
  const uncertainRef = useRef<PendingTriageCommand | null>(null);
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
    setInbox(null); setBusy(false); setUncertain(null); setAction(null); setActionTargetId(null);
    setSelectedId(null); setCheckedIds([]); setNotice(null); setConfirmedIds([]); setBulkResults(null);
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
        setSelectedId(null); setCheckedIds([]); setAction(null); setActionTargetId(null); setUncertain(null); setNotice(null);
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
      setCheckedIds((ids) => ids.filter((id) => result.items.some((item) => item.workItemId === id && isBulkSelectable(item))));
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

  /** Inbox rows after the inbox-owned filters (My items, visibility, search). */
  const rows = (inbox?.items ?? []).filter((item) => {
    const snoozed = isActiveTriageSnooze(item);
    return (!mineOnly || item.ownerId === inbox?.actorId)
      && (visibility === "all" || (visibility === "snoozed" ? snoozed : !snoozed))
      && (!search.trim() || triageSearchText(item).toLowerCase().includes(search.trim().toLowerCase()));
  });
  const selected = rows.find((item) => item.workItemId === selectedId) ?? rows[0] ?? null;
  const actionsAvailable = Boolean(inbox?.persistenceAvailable && !loading && !busy && !uncertain);
  const capability = (item: OperatorTriageItem | null, key: OperatorTriageCapability) =>
    Boolean(actionsAvailable && item?.capabilities[key] && (key !== "canSnooze" || item.tone !== "Critical"));
  /** Each checked item and whether it can take a per-item bulk action, with the reason when not. */
  const bulkEligibility = (next: OperatorTriageEachAction): OperatorTriageBulkEligibility[] =>
    (inbox?.items ?? []).filter((item) => checkedIds.includes(item.workItemId)).map((item) => {
      const key: OperatorTriageCapability = next === "BulkAcknowledge" ? "canAcknowledge" : "canSnooze";
      const eligible = capability(item, key);
      const reason = eligible ? null
        : next === "BulkAcknowledge" && item.acknowledgedAt ? "Already acknowledged."
          : next === "BulkSnooze" && item.tone === "Critical" ? "Blocked items remain visible."
            : next === "BulkSnooze" && isActiveTriageSnooze(item) ? "Already snoozed."
              : item.capabilities.disabledReason ?? `You cannot ${next === "BulkAcknowledge" ? "acknowledge" : "snooze"} this item.`;
      return { item, eligible, reason };
    });
  /** Opens the reviewed action dialog for one item, or for the checked items (bulk actions). */
  const openAction = (next: OperatorTriageDialogAction, target: OperatorTriageItem | null = null) => {
    setReason(""); setAssignee("");
    setUntil(new Date(Date.now() + 60 * 60 * 1000).toISOString().slice(0, 16));
    // Per-item bulk actions start with every eligible checked item confirmed; the operator can untick any.
    setConfirmedIds(isEachAction(next) ? bulkEligibility(next).filter((entry) => entry.eligible).map((entry) => entry.item.workItemId) : []);
    setNotice(null); setBulkResults(null);
    setActionTargetId(next === "BulkAssign" || isEachAction(next) ? null : target?.workItemId ?? null); setAction(next);
  };
  const toggleConfirmed = (workItemId: string, confirmed: boolean) =>
    setConfirmedIds((ids) => confirmed ? [...ids.filter((id) => id !== workItemId), workItemId] : ids.filter((id) => id !== workItemId));
  const closeAction = () => setAction(null);
  const setVisibility = (value: string) =>
    setVisibilityState(value === "all" || value === "snoozed" ? value : "active");
  const toggleChecked = (workItemId: string, checked: boolean) =>
    setCheckedIds((ids) => checked ? [...ids, workItemId] : ids.filter((id) => id !== workItemId));
  /** The item a single-item dialog acts on, re-read from the latest authorized inbox. */
  const actionTarget = action && action !== "BulkAssign" && !isEachAction(action)
    ? inbox?.items.find((item) => item.workItemId === actionTargetId) ?? null
    : null;

  async function execute(pending: PendingTriageCommand) {
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
      setNotice(`Command outcome is unconfirmed. Check the command outcome or retry this same command. ${failure instanceof Error ? failure.message : "Request failed."}`);
    } finally {
      if (!signal.aborted && current === commandInvocation.current) {
        commandController.current = null;
        setBusy(false);
      }
    }
  }

  /**
   * A per-item bulk action: one command per confirmed item, sent in turn, each with its own command
   * ID and the item's authoritative versions, so each item records its own activity entry. A failed
   * request marks that item unconfirmed and the rest still run.
   */
  async function executeEach(next: OperatorTriageEachAction, items: OperatorTriageItem[], common: { commandId: string; reason: string; fundAccountId?: string | null }, snoozedUntil: string | null) {
    if (commandController.current) return;
    const controller = new AbortController();
    commandController.current = controller;
    const current = ++commandInvocation.current;
    const signal = controller.signal;
    setBusy(true); setNotice(null); setBulkResults(null);
    const results: OperatorTriageBulkItemResult[] = [];
    try {
      for (const [index, item] of items.entries()) {
        if (signal.aborted || current !== commandInvocation.current) return;
        try {
          const result = await commandOperatorTriage(item.workItemId, {
            ...common, commandId: `${common.commandId}:${index}`,
            action: next === "BulkAcknowledge" ? "Acknowledge" : "Snooze",
            expectedVersion: item.version, expectedSourceVersion: item.sourceVersion,
            ...(next === "BulkSnooze" && snoozedUntil ? { snoozedUntil } : {})
          }, { signal });
          const known = ["Applied", "Conflict", "Rejected", "NotFound", "Unavailable", "Forbidden"].includes(result?.status);
          results.push({ workItemId: item.workItemId, label: item.label, outcome: known ? result.status as OperatorTriageBulkItemResult["outcome"] : "Unconfirmed", detail: known ? result.error ?? null : "The response could not be verified." });
        } catch (failure) {
          if (signal.aborted || current !== commandInvocation.current) return;
          results.push({ workItemId: item.workItemId, label: item.label, outcome: "Unconfirmed", detail: failure instanceof Error ? failure.message : "Request failed." });
        }
      }
      const applied = results.filter((result) => result.outcome === "Applied").length;
      const unconfirmed = results.filter((result) => result.outcome === "Unconfirmed").length;
      const refused = results.length - applied - unconfirmed;
      setNotice(`${next === "BulkAcknowledge" ? "Acknowledge" : "Snooze"}: ${applied} of ${results.length} applied${refused ? `, ${refused} not applied` : ""}${unconfirmed ? `, ${unconfirmed} unconfirmed (check after the inbox reloads)` : ""}. Source readiness is unchanged.`);
      setBulkResults(results); setAction(null); setCheckedIds([]);
      reload();
    } finally {
      if (!signal.aborted && current === commandInvocation.current) {
        commandController.current = null;
        setBusy(false);
      }
    }
  }

  const snoozeDate = Date.parse(`${until}Z`);
  const eachTargets = isEachAction(action)
    ? bulkEligibility(action).filter((entry) => entry.eligible && confirmedIds.includes(entry.item.workItemId)).map((entry) => entry.item)
    : [];
  const canSubmit = Boolean(action && reason.trim() && !busy && (action !== "Assign" && action !== "BulkAssign" || assignee.trim())
    && (action !== "Snooze" && action !== "BulkSnooze" || Number.isFinite(snoozeDate) && snoozeDate > Date.now() && snoozeDate <= Date.now() + 7 * 24 * 60 * 60 * 1000)
    && (isEachAction(action) ? eachTargets.length > 0 && eachTargets.length <= OPERATOR_TRIAGE_BULK_LIMIT
      : action === "BulkAssign" ? checkedIds.length > 0 && checkedIds.length <= OPERATOR_TRIAGE_BULK_LIMIT && checkedIds.every((id) => capability(inbox?.items.find((item) => item.workItemId === id) ?? null, "canAssign"))
      : capability(actionTarget, action === "Assign" ? "canAssign" : action === "Acknowledge" ? "canAcknowledge" : action === "Snooze" ? "canSnooze" : "canUnsnooze")));

  /** Submits the open dialog with a fresh command ID and the authoritative versions. */
  const submit = () => {
    if (!canSubmit || !action || !inbox) return;
    const common = { commandId: crypto.randomUUID(), reason: reason.trim(), fundAccountId };
    if (isEachAction(action)) {
      void executeEach(action, eachTargets, common, action === "BulkSnooze" ? new Date(snoozeDate).toISOString() : null);
    } else if (action === "BulkAssign") {
      void execute({ command: { ...common, action: "Assign", assigneeId: assignee.trim(), items: inbox.items.filter((item) => checkedIds.includes(item.workItemId)).map((item) => ({ workItemId: item.workItemId, expectedVersion: item.version, expectedSourceVersion: item.sourceVersion })) } });
    } else if (actionTarget) {
      void execute({ itemId: actionTarget.workItemId, command: { ...common, action, expectedVersion: actionTarget.version, expectedSourceVersion: actionTarget.sourceVersion, ...(action === "Assign" ? { assigneeId: assignee.trim() } : {}), ...(action === "Snooze" ? { snoozedUntil: new Date(snoozeDate).toISOString() } : {}) } });
    }
  };
  /** Re-sends the unconfirmed command with its original command ID (idempotent replay). */
  const retryUncertain = () => { if (uncertain) void execute(uncertain); };

  return {
    inbox, loading, error, notice, busy, uncertain, reload, retryUncertain,
    rows, selectedId, setSelectedId, selected,
    checkedIds, toggleChecked, clearChecked: () => setCheckedIds([]),
    mineOnly, setMineOnly, visibility, setVisibility, search, setSearch,
    actionsAvailable, capability,
    action, actionTarget, openAction, closeAction,
    bulkEligibility, confirmedIds, toggleConfirmed, bulkResults, clearBulkResults: () => setBulkResults(null),
    reason, setReason, assignee, setAssignee, until, setUntil, canSubmit, submit
  };
}

export type OperatorTriageInboxState = ReturnType<typeof useOperatorTriageInbox>;

/** A row can be checked when any bulk action could apply to it. */
export function isBulkSelectable(item: OperatorTriageItem): boolean {
  return item.capabilities.canAssign || item.capabilities.canAcknowledge || item.capabilities.canSnooze;
}
