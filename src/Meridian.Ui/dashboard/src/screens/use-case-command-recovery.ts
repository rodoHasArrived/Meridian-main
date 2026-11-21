import { useCallback, useEffect, useRef, useState } from "react";
import type { ReconciliationBreakQueueAuditEvent, ReconciliationBreakQueueItem, ReconciliationCaseworkCommand } from "@/types";

interface PendingCommand { commandId: string; action: string; command?: ReconciliationCaseworkCommand }
const events: Record<string, string> = { Assign: "Assigned", TransitionStatus: "StatusChanged", Waive: "Waived", Supersede: "Superseded",
  SignOff: "SignedOff", Reopen: "Reopened", SetRootCause: "RootCauseSet", SetResolution: "ResolutionSet",
  rootCause: "RootCauseSet", resolution: "ResolutionSet", waive: "Waived", supersede: "Superseded", signOff: "SignedOff", reopen: "Reopened" };

/** Uses the existing operator/case recovery key and retains payloads only in the parent workstation memory. */
export function useCaseCommandRecovery({ operatorId, item, memory, read, audit, onUpdated }: {
  operatorId?: string | null; item: ReconciliationBreakQueueItem | null;
  memory: Map<string, ReconciliationCaseworkCommand>;
  read: (id: string) => Promise<ReconciliationBreakQueueItem>;
  audit: (id: string) => Promise<unknown>;
  onUpdated: (item: ReconciliationBreakQueueItem) => void;
}) {
  const key = operatorId && item?.tenantId && item.companyId
    ? `meridian:reconciliation:decision:${JSON.stringify([operatorId, item.tenantId, item.companyId, item.breakId])}` : null;
  const context = key ?? JSON.stringify([operatorId, item?.breakId]);
  const activeKey = useRef(context); activeKey.current = context;
  const restore = useCallback((): PendingCommand | null => {
    if (!key) return null;
    try {
      const raw = sessionStorage.getItem(key); const retained = raw ? JSON.parse(raw) as PendingCommand : null;
      if (!retained || typeof retained.commandId !== "string" || typeof retained.action !== "string") return null;
      const command = memory.get(key);
      return { action: retained.action, commandId: retained.commandId, command: command?.commandId === retained.commandId ? command : undefined };
    } catch { return null; }
  }, [key, memory]);
  const [pending, setPending] = useState<PendingCommand | null>(restore);
  const [canResume, setCanResume] = useState(false);
  const [checking, setChecking] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => { setPending(restore()); setCanResume(false); setMessage(null); setChecking(false); }, [restore, context]);
  const retain = useCallback((value: PendingCommand | null) => {
    setPending(value); setCanResume(false);
    if (!key) return;
    if (value?.command) memory.set(key, value.command); else memory.delete(key);
    try {
      if (value) sessionStorage.setItem(key, JSON.stringify({ action: value.action, commandId: value.commandId }));
      else sessionStorage.removeItem(key);
    } catch { /* Identity remains visible in the current inspector. */ }
  }, [key, memory]);
  const begin = useCallback((command: ReconciliationCaseworkCommand) => {
    retain({ action: command.action, commandId: command.commandId, command }); setMessage(null);
  }, [retain]);
  const clear = useCallback(() => { retain(null); setMessage(null); }, [retain]);
  const check = useCallback(async () => {
    if (!pending || !item || checking) return;
    const requestKey = context; setChecking(true); setCanResume(false); setMessage(null);
    try {
      const response = await audit(item.breakId);
      if (activeKey.current !== requestKey) return;
      const history = (Array.isArray(response) ? response : []) as ReconciliationBreakQueueAuditEvent[];
      const action = pending.command?.action ?? pending.action;
      const expectedEvent = events[action] ?? events[action[0]?.toUpperCase() + action.slice(1)];
      const recorded = expectedEvent && history.some((event) => event.breakId === item.breakId
        && event.commandId === pending.commandId && event.eventType === expectedEvent);
      const needsHandoff = item.sourceType === "statement" && !["SetRootCause", "SetResolution", "rootCause", "resolution"].includes(action);
      const handoff = !needsHandoff || history.some((event) => event.eventType === "EvidenceLinked"
        && event.causationId === pending.commandId && event.source === "statement-casework-handoff");
      if (!recorded || !handoff) {
        setMessage(recorded ? "The case action is recorded. Source and downstream handoff remain unconfirmed."
          : "Outcome remains unconfirmed: no matching applied command is retained in case history.");
        setCanResume(Boolean(pending.command)); return;
      }
      const latest = await read(item.breakId);
      if (activeKey.current !== requestKey) return;
      if (latest.breakId !== item.breakId) throw new Error("The returned case does not match this command.");
      clear(); onUpdated(latest); setMessage("Command confirmed in retained case history.");
    } catch (error) {
      if (activeKey.current === requestKey) setMessage(error instanceof Error ? error.message : "The command outcome could not be checked.");
    } finally { if (activeKey.current === requestKey) setChecking(false); }
  }, [pending, item, checking, context, audit, read, clear, onUpdated]);
  return { pending, canResume, checking, message, begin, clear, check };
}
