import type React from "react";
export interface LinkedSelection { id: string; value?: unknown; source?: string; [key: string]: unknown; }
export interface LinkedSelectionMeta { source?: string; force?: boolean; replaceHistory?: boolean; }
export interface LinkedSelectionApi {
  /** The scope name this provider registered under, or null for an unnamed provider. */
  scope: string | null;
  multiple: boolean;
  /** Single-selection surface — unchanged since 3.1.0. In multi mode this is the first entry. */
  selection: LinkedSelection | null;
  selectedId: string | null;
  select(value: any, meta?: LinkedSelectionMeta): void;
  clear(meta?: LinkedSelectionMeta): void;
  isSelected(value: any): boolean;
  /** Multi-selection surface (3.2.0). Always present; length <= 1 unless `multiple`. */
  selections: LinkedSelection[];
  selectedIds: string[];
  count: number;
  items: any[] | null;
  toggle(value: any, meta?: LinkedSelectionMeta): void;
  add(value: any, meta?: LinkedSelectionMeta): void;
  remove(value: any, meta?: LinkedSelectionMeta): void;
  /** Requires `items` on the provider to know the order; falls back to a plain select. */
  selectRange(fromValue: any, toValue: any, meta?: LinkedSelectionMeta): void;
  selectAll(meta?: LinkedSelectionMeta): void;
  /** Most recent first. Committed selections only — hover previews never enter history. */
  history: LinkedSelection[][];
  canGoBack: boolean;
  back(meta?: LinkedSelectionMeta): void;
}
export interface LinkedSelectionProviderProps {
  value?: any;
  defaultValue?: any;
  onChange?: (value: LinkedSelection | LinkedSelection[] | null, meta: LinkedSelectionMeta & { selections: LinkedSelection[] }) => void;
  urlParam?: string;
  storageKey?: string;
  clearOnEscape?: boolean;
  /** Name this provider so `useLinkedSelection("name")` can reach it past a nearer one.
   *  Lets one screen host several independent linked groups (e.g. "position" and "lot"). */
  scope?: string;
  multiple?: boolean;
  /** Cap on concurrent selections in multi mode. */
  max?: number;
  /** Ordered population — enables `selectRange` (shift-click) and `selectAll`. */
  items?: any[];
  /** How many prior selections `back()` can walk. Default 12; 0 disables history. */
  historyLimit?: number;
  children?: React.ReactNode;
}
export declare function LinkedSelectionProvider(props: LinkedSelectionProviderProps): React.JSX.Element;
export declare function useLinkedSelection(scope?: string): LinkedSelectionApi;
/** Bundle carrier. The compiler exposes only capitalized exports, so consumers of
 *  `_ds_bundle.js` read the hook through this object. */
export declare const SelectionHooks: { useLinkedSelection: typeof useLinkedSelection };
export declare function LinkedSelectionTarget(props: {
  selection: any;
  /** Bind to a named provider instead of the nearest one. */
  scope?: string;
  /** Position in the provider's `items` — enables shift-click range extension. */
  index?: number;
  as?: React.ElementType;
} & Record<string, any>): React.JSX.Element;
