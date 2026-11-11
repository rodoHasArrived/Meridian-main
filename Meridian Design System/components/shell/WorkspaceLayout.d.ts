/**
 * WorkspaceLayout — named, persisted arrangement for a workstation screen (4.1, track A of
 * session continuity). `SplitPane` is pane-local and `SavedViews` is table-local; this is the
 * screen-level layout identity Meridian was missing.
 *
 * Persists **arrangement only** — region size, collapse, visibility, and the active layout name —
 * to `localStorage` under `mds.workspace-layout.<screenId>`. Never filters, selection, period, or
 * as-of: a layout that carries data context makes a stale figure look intentional
 * (`guidelines/SESSION_CONTINUITY.md`).
 *
 * @example
 * <WorkspaceLayoutProvider screenId="ops-wall"
 *   regions={[{id:"queue",size:1.4},{id:"detail",size:1},{id:"evidence",size:.8,collapsed:true}]}
 *   layouts={[{name:"Breach triage",regions:{evidence:{collapsed:false,size:1.2}}}]}>
 *   <WorkspaceLayoutBar />
 *   <WorkspaceLayoutGrid direction="row">
 *     <LayoutRegion id="queue" label="Exception queue">…</LayoutRegion>
 *     <LayoutRegion id="detail" label="Case" onPopOut={detach}>…</LayoutRegion>
 *     <LayoutRegion id="evidence" label="Evidence">…</LayoutRegion>
 *   </WorkspaceLayoutGrid>
 * </WorkspaceLayoutProvider>
 */
export interface RegionArrangement { size?: number; collapsed?: boolean; hidden?: boolean }
export interface RegionDeclaration extends RegionArrangement {
  id: string;
  /** Flex weight, not pixels or a percentage. @default 1 */
  size?: number;
}
export interface NamedLayout { name: string; regions: Record<string, RegionArrangement> }
export interface WorkspaceLayoutApi {
  screenId: string;
  active: string;
  /** The current arrangement differs from the saved one. Meridian never silently overwrites. */
  dirty: boolean;
  /** A stored arrangement was found for this screen and applied on mount. */
  restored: boolean;
  names: string[];
  regions: Record<string, RegionArrangement>;
  meta: Record<string, { label?: string; minSize?: number }>;
  region(id: string): Required<RegionArrangement>;
  /** Regions announce themselves on mount so the bar can list them without prop drilling. */
  declare(id: string, info: { label?: string; minSize?: number }): void;
  /** Non-arrangement keys are filtered out rather than trusted. */
  setRegion(id: string, patch: RegionArrangement | ((current: RegionArrangement) => RegionArrangement)): void;
  toggleCollapse(id: string): void;
  select(name: string): void;
  /** Deliberate save. Omit `name` to overwrite the active layout. */
  save(name?: string): void;
  /** Discard unsaved changes, back to the saved arrangement. */
  reset(): void;
  resetToDefault(): void;
  remove(name: string): void;
}
export declare function WorkspaceLayoutProvider(props: {
  /** Screen identity. One store key per screen — never shared across screens. */
  screenId: string;
  regions?: RegionDeclaration[];
  /** Presets offered alongside the implicit `Default`. */
  layouts?: NamedLayout[];
  /** @default true */
  persist?: boolean;
  /** Storage shim for tests or a host-provided store. @default window.localStorage */
  storage?: Pick<Storage, "getItem" | "setItem">;
  onChange?: (state: { screenId: string; active: string; regions: Record<string, RegionArrangement>; dirty: boolean }) => void;
  children?: React.ReactNode;
}): JSX.Element;
export declare function useWorkspaceLayout(): WorkspaceLayoutApi;
/** Bundle carrier. The compiler exposes only capitalized exports. */
export declare const LayoutHooks: { useWorkspaceLayout: typeof useWorkspaceLayout };

/** Layout identity, dirty state, save/reset. Renders nothing outside a provider. */
export declare function WorkspaceLayoutBar(props: React.HTMLAttributes<HTMLDivElement> & {
  actions?: React.ReactNode;
  /** Show `Restored · <layout>` when a stored arrangement was applied. @default true */
  showRestored?: boolean;
  saveLabel?: React.ReactNode; resetLabel?: React.ReactNode;
}): JSX.Element | null;

/** Convenience composite — provider → bar → grid in one tag. Compose the three parts directly when
 *  the layout bar belongs elsewhere in the screen chrome. */
export declare function WorkspaceLayout(props: Omit<React.HTMLAttributes<HTMLDivElement>, "onChange"> & {
  screenId: string;
  regions?: RegionDeclaration[];
  layouts?: NamedLayout[];
  persist?: boolean;
  storage?: Pick<Storage, "getItem" | "setItem">;
  onChange?: (state: { screenId: string; active: string; regions: Record<string, RegionArrangement>; dirty: boolean }) => void;
  direction?: "row" | "column";
  gap?: number;
  /** Positional region declarations — one per child, in order. See `WorkspaceLayoutGrid.slots`. */
  slots?: SlotDeclaration[];
  /** Render `WorkspaceLayoutBar` above the grid. @default true */
  bar?: boolean;
  barActions?: React.ReactNode;
  children?: React.ReactNode;
}): JSX.Element;

/** Positional region declaration for `slots` — matched to children by index. */
export interface SlotDeclaration {
  id: string;
  label?: React.ReactNode;
  actions?: React.ReactNode;
  minSize?: number;
  /** @default true */
  collapsible?: boolean;
  /** @default true */
  chrome?: boolean;
  onPopOut?: (id: string) => void;
}

/** Flex container whose dividers are `role="separator"`, focusable, and arrow-key resizable. */
export declare function WorkspaceLayoutGrid(props: React.HTMLAttributes<HTMLDivElement> & {
  direction?: "row" | "column";
  /** @default 8 */
  gap?: number;
  /** Wrap each child in its own `LayoutRegion`, declared positionally. For markup-first callers
   *  (design-component templates, plain children) that cannot pass `LayoutRegion` elements. */
  slots?: SlotDeclaration[];
  children?: React.ReactNode;
}): JSX.Element;

/** A region whose size, collapse, and visibility persist. Collapses to a labelled, focusable rail —
 *  never to nothing. `onPopOut` must keep the detached pane inside the same linked selection and
 *  workspace context, or not open it. */
export declare function LayoutRegion(props: Omit<React.HTMLAttributes<HTMLElement>, "id"> & {
  id: string;
  label?: React.ReactNode;
  actions?: React.ReactNode;
  /** @default true */
  collapsible?: boolean;
  /** Render the region header. `false` for a region that supplies its own chrome. @default true */
  chrome?: boolean;
  onPopOut?: (id: string) => void;
  /** Floor for drag and keyboard resize, as a share of the grid. @default 0.1 */
  minSize?: number;
  /** Fallback weight when used without a provider. */
  size?: number;
  children?: React.ReactNode;
}): JSX.Element | null;
