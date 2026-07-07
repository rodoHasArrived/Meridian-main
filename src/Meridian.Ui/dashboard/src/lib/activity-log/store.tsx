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
import type { ActivityEntry, ActivityEntryInput, ActivityUndoHandler } from "./types";

export interface ActivityLogApi {
  entries: ActivityEntry[];
  /** Entries recorded since the drawer was last opened. */
  unseenCount: number;
  /** Record a mutating action. Returns the entry id. */
  record: (input: ActivityEntryInput) => string;
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
  undo: async () => undefined,
  remove: () => undefined,
  clear: () => undefined,
  markAllSeen: () => undefined
};

export interface UseActivityLogServiceOptions {
  /** Injectable storage for tests; defaults to localStorage. */
  storage?: StorageLike | null;
  /** Injectable clock for deterministic tests. */
  now?: () => number;
}

let entrySeq = 0;

/**
 * Owns the action-history ledger: an in-memory list of recorded actions (persisted
 * as serializable snapshots) plus a side map of live undo handlers that never leave
 * memory. Mirrors the ToastProvider/price-alerts service shape.
 */
export function useActivityLogService(options: UseActivityLogServiceOptions = {}): ActivityLogApi {
  const { storage, now } = options;
  const clock = useMemo(() => now ?? (() => Date.now()), [now]);

  const [entries, setEntries] = useState<ActivityEntry[]>(() => loadActivityLog(storage));
  // Undo handlers are transient — keyed by entry id, never persisted.
  const undoHandlers = useRef(new Map<string, ActivityUndoHandler>());
  const [unseenCount, setUnseenCount] = useState(0);

  // Persist the serializable snapshot whenever the ledger changes.
  useEffect(() => {
    saveActivityLog(entries, storage);
  }, [entries, storage]);

  const record = useCallback(
    (input: ActivityEntryInput): string => {
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
        undoHandlers.current.set(id, input.undo);
      } else {
        undoHandlers.current.delete(id);
      }
      setEntries((current) => {
        const withoutDupe = current.filter((existing) => existing.id !== id);
        return [entry, ...withoutDupe].slice(0, ACTIVITY_LOG_LIMIT);
      });
      setUnseenCount((count) => count + 1);
      return id;
    },
    [clock]
  );

  const patchEntry = useCallback((id: string, patch: Partial<ActivityEntry>) => {
    setEntries((current) => current.map((entry) => (entry.id === id ? { ...entry, ...patch } : entry)));
  }, []);

  const undo = useCallback(
    async (id: string) => {
      const handler = undoHandlers.current.get(id);
      if (!handler) {
        return;
      }
      patchEntry(id, { undoStatus: "running", undoError: undefined });
      try {
        await handler.run();
        undoHandlers.current.delete(id);
        patchEntry(id, { undoStatus: "undone", undoError: undefined });
      } catch (error) {
        // Keep the handler so the operator can retry.
        patchEntry(id, {
          undoStatus: "failed",
          undoError: error instanceof Error && error.message ? error.message : "Undo failed."
        });
      }
    },
    [patchEntry]
  );

  const remove = useCallback((id: string) => {
    undoHandlers.current.delete(id);
    setEntries((current) => current.filter((entry) => entry.id !== id));
  }, []);

  const clear = useCallback(() => {
    undoHandlers.current.clear();
    setEntries([]);
    setUnseenCount(0);
  }, []);

  const markAllSeen = useCallback(() => {
    setUnseenCount(0);
  }, []);

  return useMemo(
    () => ({ entries, unseenCount, record, undo, remove, clear, markAllSeen }),
    [entries, unseenCount, record, undo, remove, clear, markAllSeen]
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
