// Change notifications and sync status for the operator presentation preferences (appearance, density, display
// time zone, page descriptions, shortcut overrides). Each preference keeps its own browser store;
// writers announce a change here so the host sync can save the set, and the sync announces values
// it applied from the host so mounted controls can re-read their store. Kept dependency-free so
// the preference modules can import it without cycles.

type Listener = () => void;

const changeListeners = new Set<Listener>();
const appliedListeners = new Set<Listener>();

/** An operator changed a presentation preference in this browser. */
export function notifyOperatorPreferenceChanged(): void {
  for (const listener of [...changeListeners]) {
    listener();
  }
}

export function onOperatorPreferenceChanged(listener: Listener): () => void {
  changeListeners.add(listener);
  return () => {
    changeListeners.delete(listener);
  };
}

/** Preferences loaded from the host were written to this browser's stores. */
export function notifyOperatorPreferencesApplied(): void {
  for (const listener of [...appliedListeners]) {
    listener();
  }
}

export function onOperatorPreferencesApplied(listener: Listener): () => void {
  appliedListeners.add(listener);
  return () => {
    appliedListeners.delete(listener);
  };
}

// Sync status for the operator presentation preferences, so Settings can say honestly whether the
// current set reached the host. Only the host sync writes it; screens read it.

export type OperatorPreferenceSyncStatus =
  /** The sync is not running (fixture or demo session, or no signed-in operator). */
  | { state: "local"; reason: "no-sync" }
  /** Reading the operator's preferences from the host. */
  | { state: "loading" }
  /** Saving the current set to the host. */
  | { state: "saving"; lastSavedAt: string | null }
  /** The host holds the current set. */
  | { state: "synced"; lastSavedAt: string | null }
  /** The host could not be read or written; the change stays in this browser until a retry succeeds. */
  | { state: "failed"; lastSavedAt: string | null; reason: "load" | "save" };

const NO_SYNC_STATUS: OperatorPreferenceSyncStatus = { state: "local", reason: "no-sync" };

let syncStatus: OperatorPreferenceSyncStatus = NO_SYNC_STATUS;
const statusListeners = new Set<Listener>();
let retryHandler: (() => void) | null = null;

export function getOperatorPreferenceSyncStatus(): OperatorPreferenceSyncStatus {
  return syncStatus;
}

export function subscribeOperatorPreferenceSyncStatus(listener: Listener): () => void {
  statusListeners.add(listener);
  return () => {
    statusListeners.delete(listener);
  };
}

/** Replaces the status (a new object each time, so React sees a new snapshot). Used by the sync only. */
export function setOperatorPreferenceSyncStatus(status: OperatorPreferenceSyncStatus): void {
  syncStatus = { ...status };
  for (const listener of [...statusListeners]) {
    listener();
  }
}

/** Resets the status to "sync not running". */
export function resetOperatorPreferenceSyncStatus(): void {
  setOperatorPreferenceSyncStatus(NO_SYNC_STATUS);
}

/** The running sync registers how to retry at once; returns an unregister function. */
export function registerOperatorPreferenceSyncRetry(handler: () => void): () => void {
  retryHandler = handler;
  return () => {
    if (retryHandler === handler) {
      retryHandler = null;
    }
  };
}

/** Asks the running sync to retry now. Returns false when no sync is running. */
export function retryOperatorPreferenceSync(): boolean {
  if (!retryHandler) {
    return false;
  }
  retryHandler();
  return true;
}
