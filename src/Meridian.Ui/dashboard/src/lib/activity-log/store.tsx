import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode
} from "react";
import { ACTIVITY_LOG_LIMIT, loadActivityLog, saveActivityLog, type StorageLike } from "./storage";
import type { ActivityEntry, ActivityEntryInput, ActivityOperationInput, ActivityOperationStatus, ActivityOperationUpdate, ActivityTone, ActivityUndoHandler } from "./types";

export interface ActivityLogApi {
  entries: ActivityEntry[];
  /** Entries recorded since the drawer was last opened. */
  unseenCount: number;
  /** Record a mutating action. Returns the entry id. */
  record: (input: ActivityEntryInput) => string;
  /** Display-only operation tracking; never stores commands or grants retry authority. */
  beginOperation: (input: ActivityOperationInput) => string;
  updateOperation: (id: string, update: ActivityOperationUpdate) => void;
  /** Run (or retry) the compensating operation for a reversible entry. */
  undo: (id: string) => Promise<void>;
  /** Remove a single entry from the ledger. */
  remove: (id: string) => void;
  /** Clear the whole ledger. */
  clear: () => void;
  /** Mark every current entry as seen (resets the unseen badge). */
  markAllSeen: () => void;
}

const ActivityLogContext = createContext<ActivityLogApi | null>(null);

const noopActivityLog: ActivityLogApi = {
  entries: [],
  unseenCount: 0,
  record: () => "",
  beginOperation: () => "",
  updateOperation: () => undefined,
  undo: async () => undefined,
  remove: () => undefined,
  clear: () => undefined,
  markAllSeen: () => undefined
};

export interface UseActivityLogServiceOptions {
  /** Injectable storage for tests; defaults to sessionStorage. */
  storage?: StorageLike | null;
  /** Injectable clock for deterministic tests. */
  now?: () => number;
  /** Available shell identity for history isolation, not authenticated command authority. */
  sessionIdentity?: string | null;
}

let entrySeq = 0;

/**
 * Owns the action-history ledger: an in-memory list of recorded actions (persisted
 * as serializable snapshots) plus a side map of live undo handlers that never leave
 * memory. Mirrors the ToastProvider/price-alerts service shape.
 */
export function useActivityLogService(options: UseActivityLogServiceOptions = {}): ActivityLogApi {
  const { storage, now, sessionIdentity } = options;
  const clock = useMemo(() => now ?? (() => Date.now()), [now]);

  // Identity labels can repeat (A -> B -> A). Invalidate captured callbacks at
  // every observed transition, before children can read or act on old history.
  const session = useRef({ identity: sessionIdentity, epoch: 0 });
  if (session.current.identity !== sessionIdentity) {
    session.current = { identity: sessionIdentity, epoch: session.current.epoch + 1 };
  }
  const epoch = session.current.epoch;
  const [history, setHistory] = useState(() => ({ owner: sessionIdentity, epoch, entries: loadActivityLog(storage, sessionIdentity) }));
  const entries = useMemo(() => history.epoch === epoch ? history.entries : [], [epoch, history]);
  const setEntries = useCallback((update: (current: ActivityEntry[]) => ActivityEntry[]) => {
    if (session.current.epoch !== epoch || sessionIdentity === null) return;
    setHistory((current) => session.current.epoch !== epoch ? current : ({ owner: sessionIdentity, epoch, entries: update(current.epoch === epoch ? current.entries : []) }));
  }, [epoch, sessionIdentity]);
  // Undo handlers are transient — keyed by entry id, never persisted.
  const undoHandlers = useRef(new Map<string, { epoch: number; handler: ActivityUndoHandler }>());
  const undoInFlight = useRef(new Map<string, number>());
  const [unseen, setUnseen] = useState({ epoch, count: 0 });
  const unseenCount = unseen.epoch === epoch ? unseen.count : 0;

  // Hide old-owner history immediately; clear live thunks before a new identity can use them.
  useEffect(() => {
    if (session.current.epoch !== epoch || history.epoch === epoch) return;
    for (const [id, retained] of undoHandlers.current) {
      if (retained.epoch !== epoch) undoHandlers.current.delete(id);
    }
    for (const [id, retainedEpoch] of undoInFlight.current) {
      if (retainedEpoch !== epoch) undoInFlight.current.delete(id);
    }
    setUnseen((current) => current.epoch === epoch ? current : { epoch, count: 0 });
    if (sessionIdentity === null) saveActivityLog([], storage, null);
    setHistory((current) => current.epoch === epoch ? current : { owner: sessionIdentity, epoch, entries: history.owner === null ? loadActivityLog(storage, sessionIdentity) : [] });
  }, [epoch, history.epoch, history.owner, sessionIdentity, storage]);

  // An initially unresolved session leaves retained history untouched until identity resolves.
  useEffect(() => {
    if (session.current.epoch === epoch && history.epoch === epoch && sessionIdentity !== null) saveActivityLog(history.entries, storage, sessionIdentity);
  }, [epoch, history, sessionIdentity, storage]);

  const record = useCallback(
    (input: ActivityEntryInput): string => {
      if (session.current.epoch !== epoch || sessionIdentity === null) return "";
      const id = input.id ?? `activity-${clock()}-${(entrySeq += 1)}`;
      const entry: ActivityEntry = {
        id,
        kind: input.kind,
        title: input.title,
        detail: input.detail,
        route: input.route,
        routeLabel: input.routeLabel,
        tone: input.tone ?? "default",
        timestamp: input.timestamp ?? new Date(clock()).toISOString(),
        undoStatus: input.undo ? "idle" : "none"
      };
      if (input.undo) {
        undoHandlers.current.set(id, { epoch, handler: input.undo });
      } else {
        undoHandlers.current.delete(id);
      }
      setEntries((current) => {
        const withoutDupe = current.filter((existing) => existing.id !== id);
        return [entry, ...withoutDupe].slice(0, ACTIVITY_LOG_LIMIT);
      });
      setUnseen((current) => session.current.epoch !== epoch ? current : { epoch, count: (current.epoch === epoch ? current.count : 0) + 1 });
      return id;
    },
    [clock, epoch, sessionIdentity, setEntries]
  );

  const patchEntry = useCallback((id: string, patch: Partial<ActivityEntry>) => {
    setEntries((current) => current.map((entry) => (entry.id === id ? { ...entry, ...patch } : entry)));
  }, [setEntries]);

  const beginOperation = useCallback((input: ActivityOperationInput) => {
    if (session.current.epoch !== epoch || sessionIdentity === null) return "";
    const id = record({ ...input, tone: "default" });
    patchEntry(id, { operation: {
      status: "pending", actor: input.actor, scope: input.scope, reference: input.reference,
      updatedAt: new Date(clock()).toISOString()
    } });
    return id;
  }, [clock, epoch, patchEntry, record, sessionIdentity]);

  const updateOperation = useCallback((id: string, update: ActivityOperationUpdate) => {
    const tones: Record<ActivityOperationStatus, ActivityTone> = { pending: "default", completed: "success", failed: "danger", uncertain: "warning" };
    setEntries((current) => current.map((entry) => entry.id !== id || !entry.operation ? entry : {
      ...entry,
      tone: tones[update.status],
      detail: update.detail ?? entry.detail,
      route: update.route ?? entry.route,
      routeLabel: update.routeLabel ?? entry.routeLabel,
      operation: {
        ...entry.operation,
        status: update.status,
        reference: update.reference ?? entry.operation.reference,
        updatedAt: new Date(clock()).toISOString()
      }
    }));
  }, [clock, setEntries]);

  const undo = useCallback(
    async (id: string) => {
      const retained = undoHandlers.current.get(id);
      if (!retained || retained.epoch !== epoch || undoInFlight.current.get(id) === epoch || session.current.epoch !== epoch) {
        return;
      }
      const handler = retained.handler;
      undoInFlight.current.set(id, epoch);
      patchEntry(id, { undoStatus: "running", undoError: undefined });
      try {
        await handler.run();
        if (session.current.epoch !== epoch || undoHandlers.current.get(id) !== retained) return;
        undoHandlers.current.delete(id);
        patchEntry(id, { undoStatus: "undone", undoError: undefined });
      } catch (error) {
        if (session.current.epoch !== epoch || undoHandlers.current.get(id) !== retained) return;
        // Keep the handler so the operator can retry.
        patchEntry(id, {
          undoStatus: "failed",
          undoError: error instanceof Error && error.message ? error.message : "Undo failed."
        });
      } finally { if (session.current.epoch === epoch) undoInFlight.current.delete(id); }
    },
    [epoch, patchEntry]
  );

  const remove = useCallback((id: string) => {
    if (session.current.epoch !== epoch) return;
    undoHandlers.current.delete(id);
    setEntries((current) => current.filter((entry) => entry.id !== id));
  }, [epoch, setEntries]);

  const clear = useCallback(() => {
    if (session.current.epoch !== epoch) return;
    undoHandlers.current.clear();
    setEntries(() => []);
    setUnseen({ epoch, count: 0 });
  }, [epoch, setEntries]);

  const markAllSeen = useCallback(() => {
    if (session.current.epoch === epoch) setUnseen({ epoch, count: 0 });
  }, [epoch]);

  return useMemo(
    () => ({ entries, unseenCount, record, beginOperation, updateOperation, undo, remove, clear, markAllSeen }),
    [entries, unseenCount, record, beginOperation, updateOperation, undo, remove, clear, markAllSeen]
  );
}

export function ActivityLogProvider({
  children,
  ...options
}: UseActivityLogServiceOptions & { children?: ReactNode }) {
  const api = useActivityLogService(options);
  return <ActivityLogContext.Provider value={api}>{children}</ActivityLogContext.Provider>;
}

/** Access the activity log. Returns a no-op API when rendered outside a provider. */
export function useActivityLog(): ActivityLogApi {
  return useContext(ActivityLogContext) ?? noopActivityLog;
}
