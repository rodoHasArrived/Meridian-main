/** A searchable row — same shape the CommandPalette's `resolve` returns. */
export interface GlobalSearchRow {
  /** Stable row id. */
  id?: string;
  /** Group heading this row sits under ("Securities", "Breaks", "Go to"…). */
  group?: string;
  /** The row's primary text. The matched span is highlighted. */
  label: string;
  /** Right-aligned mono detail — an id, a code, a count. */
  meta?: string;
  /** What Enter / click does. */
  run?: () => void;
}

export interface GlobalSearchProps {
  /** Whether the overlay is showing. */
  open?: boolean;
  /** Called on Escape, scrim click, or after a row runs. */
  onClose?: () => void;
  /**
   * The current screen's object lookup. Given the query, return matching rows.
   * A screen that already declares `objects` in its operator layer gets this for free —
   * `MeridianOperatorLayer.build()` publishes its resolver and ds-base.js wires it up.
   */
  resolve?: (query: string) => GlobalSearchRow[];
  /** Every destination in the app map, shown unfiltered when the query is empty. */
  destinations?: GlobalSearchRow[];
  /** The chip at the right of the field naming the object scope. */
  scopeLabel?: string;
  placeholder?: string;
  /** Replaces the default explanation in the empty state. */
  emptyHint?: string;
}

/**
 * The masthead search field, made real: a typeahead over the current screen's objects plus every
 * destination in the app map. Grouped, keyboard-first (↑↓ move · ↵ open · esc close).
 *
 * Mounted automatically by `templates/_scaffold/ds-base.js`, so screens write nothing — mount it
 * by hand only to put it somewhere other than the masthead.
 */
export declare function GlobalSearch(props: GlobalSearchProps): JSX.Element | null;
