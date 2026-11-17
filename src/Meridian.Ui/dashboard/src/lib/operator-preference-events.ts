// Change notifications for the operator presentation preferences (appearance, density, display
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
