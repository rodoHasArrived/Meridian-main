/**
 * SavedViews — named, persisted table views ("My breaks", "Live fills only"): a snapshot of
 * query + sort stack + filters that operators save, re-apply, and delete from a compact
 * trigger + menu. Pairs directly with `useTableState` (uses its getViewState/applyViewState),
 * or any custom getState/applyState pair for richer captures (column config, density).
 * `presets` are locked, non-deletable entries; saved views persist to localStorage under
 * `storageKey`. Shows an "· edited" indicator when current state has diverged from the
 * selected view, with an Update action for saved (non-preset) views.
 */
import * as React from "react";

export interface SavedViewEntry {
  name: string;
  /** Opaque view snapshot — whatever getState/getViewState returns. */
  state: any;
}

export interface SavedViewsProps {
  /** localStorage namespace ("<key>.views", "<key>.active"). Omit for session-only views. */
  storageKey?: string | null;
  /** A useTableState return object — snapshot via getViewState / applyViewState. */
  table?: any;
  /** Custom snapshot capture (overrides `table`). */
  getState?: (() => any) | null;
  /** Custom snapshot apply (overrides `table`). */
  applyState?: ((state: any) => void) | null;
  /** Locked, non-deletable views shipped by the screen. */
  presets?: SavedViewEntry[];
  /** Trigger text when no view is active. @default "All rows" */
  defaultLabel?: string;
  /** Small-caps eyebrow on the trigger. @default "View" */
  label?: string;
  /** Fired after a view is applied or saved. `name` is null for the default reset. */
  onApply?: ((name: string | null, state: any) => void) | null;
}
export declare function SavedViews(props: SavedViewsProps): JSX.Element;
