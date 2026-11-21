import type React from "react";
/**
 * ObjectTrail — per-operator traversal across the seventeen object types (4.1, track A of session
 * continuity). `Breadcrumb` states where an object sits in a hierarchy; the trail states where the
 * *person* has been, which is a different question and had no surface.
 *
 * An entry is a **reference, not a snapshot**: `{type, id, version, label}`. If `currentVersion`
 * differs from the `version` viewed, returning to it is a navigation **with a notice** — silently
 * loading newer data under an older heading is how an operator comes to quote a number that no
 * longer exists (`guidelines/SESSION_CONTINUITY.md`).
 *
 * Recents and pinned entries persist to `localStorage` under `mds.object-trail.<screenId>`.
 * Pinning is intent; recency is behaviour — they render separately.
 *
 * @example
 * <ObjectTrailProvider screenId="ops-wall" onNavigate={(e,{versionMoved})=>open(e,versionMoved)}>
 *   <ObjectTrail />
 *   …
 * </ObjectTrailProvider>
 * // the screen records arrivals itself:
 * trail.visit({type:"reconciliation", id:"REC-2026-07-CASH", label:"July cash — General Account",
 *              version:12, currentVersion:12, meta:"3 breaks open", at:"09:41"})
 */
export interface TrailEntry {
  /** A `MeridianObjectType` — supplies the type label, so "July" is never ambiguous. */
  type: string;
  id?: string | number;
  label?: React.ReactNode;
  /** Override the label drawn from the object ledger. */
  typeLabel?: string;
  /** The version the operator actually looked at. */
  version?: string | number;
  /** The version in force now. Differs → the entry is marked moved. */
  currentVersion?: string | number;
  /** One short qualifier — "3 breaks open", "STAT · 2026-07". Never the object's data. */
  meta?: string;
  at?: string;
}
export interface ObjectTrailApi {
  screenId: string;
  current: TrailEntry | null;
  stack: TrailEntry[];
  recents: TrailEntry[];
  pinned: TrailEntry[];
  canBack: boolean; canForward: boolean;
  /** Set after a back / forward / jump onto a moved entry; render it, then `dismissNotice()`. */
  notice: { type: "version-moved"; entry: TrailEntry } | null;
  moved(entry: TrailEntry): boolean;
  refOf(entry: TrailEntry): string;
  isPinned(entry: TrailEntry): boolean;
  /** Record an arrival. The screen calls this — the trail never guesses navigation. */
  visit(entry: TrailEntry): void;
  back(): void; forward(): void;
  /** Jump to a recent or pinned entry; pushes onto the stack rather than rewriting history. */
  go(entry: TrailEntry): void;
  pin(entry: TrailEntry): void; unpin(entry: TrailEntry): void; togglePin(entry: TrailEntry): void;
  /** Clears behaviour, keeps intent: pinned entries survive. */
  clearRecents(): void;
  dismissNotice(): void;
}
export declare function ObjectTrailProvider(props: {
  /** Trails are per operator per screen. `"global"` for a shell-wide trail. @default "global" */
  screenId?: string;
  /** Recents cap. @default 12 */
  max?: number;
  /** @default true */
  persist?: boolean;
  storage?: Pick<Storage, "getItem" | "setItem">;
  defaultEntries?: TrailEntry[];
  defaultPinned?: TrailEntry[];
  /** Called on back / forward / jump. `versionMoved` is the screen's cue to state the change. */
  onNavigate?: (entry: TrailEntry, detail: { direction: "back" | "forward" | "jump"; versionMoved: boolean }) => void;
  children?: React.ReactNode;
}): React.JSX.Element;
export declare function useObjectTrail(): ObjectTrailApi;
export declare function trailRef(entry: TrailEntry): string;
export declare function trailMoved(entry: TrailEntry): boolean;
/** Bundle carrier. The compiler exposes only capitalized exports. */
export declare const TrailHooks: {
  useObjectTrail: typeof useObjectTrail; trailRef: typeof trailRef; trailMoved: typeof trailMoved;
};

/** Horizontal trail: back / forward, the current object with its version, pinned chips, and a
 *  recents disclosure. Renders the version-moved notice above itself. Hidden in print. */
export declare function ObjectTrail(props: React.HTMLAttributes<HTMLElement> & {
  label?: string;
  /** @default true */
  showPinned?: boolean;
  /** Pinned chips shown inline before the disclosure. @default 4 */
  maxPinned?: number;
  recentsLabel?: React.ReactNode;
}): React.JSX.Element | null;

/** Vertical trail for a rail or an arrival screen — pinned above recents, each with its version
 *  state. Empty is a statement, never a blank panel. */
export declare function ObjectTrailList(props: React.HTMLAttributes<HTMLDivElement> & {
  heading?: React.ReactNode;
  /** @default 6 */
  limit?: number;
  /** @default true */
  showPinned?: boolean;
  emptyStatement?: React.ReactNode;
}): React.JSX.Element | null;
